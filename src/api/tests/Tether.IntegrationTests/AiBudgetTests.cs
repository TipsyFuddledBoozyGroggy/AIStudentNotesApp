using Tether.Ai.Budget;
using Tether.Ai.CatchUp;
using Tether.Ai.Domain;
using Tether.Ai.Provider;
using Tether.Ai.Validation;
using Tether.IntegrationTests.Infrastructure;
using Tether.SharedKernel;
using Tether.Transcript.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Tether.IntegrationTests;

/// <summary>Per session 500k micro-USD, per user-day huge, global 5M: room for exactly one 400k call per session and 12 across the month.</summary>
public sealed class BudgetFactory : ApiFactory
{
    protected override int MaxPoolSize => RacePool.Size; // NAS-R09: the session-cap and global-cap races fire 8 and 20 reservations at once
    protected override IEnumerable<KeyValuePair<string, string?>> Settings =>
    [
        new("Ai:Budget:PerSessionMicroUsd", "500000"), new("Ai:Budget:PerUserDailyMicroUsd", "100000000"), new("Ai:Budget:GlobalMonthlyMicroUsd", "5000000"),
    ];
}

public abstract class AiBudgetTestBase(ApiFactory factory) : LectureTestBase(factory)
{
    protected IServiceScopeFactory Scopes => F.Services.GetRequiredService<IServiceScopeFactory>();

    protected async Task<bool> ReserveAsync(Guid userId, Guid sessionId, long estimate)
    {
        await using var scope = Scopes.CreateUserScope(userId);
        var o = await scope.ServiceProvider.GetRequiredService<BudgetGate>().ReserveAsync(userId, sessionId, AiWorkflow.QuickRecap, "t.v1", "m", estimate, default);
        return o.Reservation is not null;
    }

    private readonly Dictionary<Guid, Guid> _sessions = new();

    /// <summary>Runs one catch-up call through the gateway for a real, Active session of the actor (the gateway refuses any other, B3-06). One session per actor, reused.</summary>
    private protected async Task<GatewayOutcome<CatchUpResult>> RunGatewayAsync(Actor a)
    {
        var userId = a.UserId;
        if (!_sessions.TryGetValue(userId, out var sid))
            _sessions[userId] = sid = Guid.Parse((await StartOkAsync(a.Client, await CourseAsync(a.Client))).GetProperty("id").GetString()!);
        await using var scope = Scopes.CreateUserScope(userId);
        var opts = F.Services.GetRequiredService<AiOptions>();
        var seg = new FinalSegment(sid, 1, 1, 0, 1500, "Mitosis has four phases.", null);
        var built = CatchUpRequests.Build(userId, sid, Guid.NewGuid(), 5, [seg], opts.CatchUp);
        return await scope.ServiceProvider.GetRequiredService<AiGateway>().RunAsync(built.Request, j => CatchUpValidator.Validate(j, built.Context), default);
    }

    protected Task<long> GlobalAsync() => F.ScalarAsync("select coalesce(sum(spent_micros), 0) from ai.global_spend");
}

[Collection(RacePool.Name)]
public class AiBudgetSessionCapTests(BudgetFactory factory) : AiBudgetTestBase(factory), IClassFixture<BudgetFactory>
{
    [Fact]
    public async Task Parallel_reservations_against_a_cap_of_one_call_admit_exactly_one()
    {
        var a = await NewActorAsync();
        var session = Guid.NewGuid();
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => ReserveAsync(a.UserId, session, 400_000))));
        Assert.Equal(1, results.Count(r => r));
        Assert.Equal(1, await F.ScalarAsync($"select count(*) from ai.ai_usage where session_id = '{session}' and outcome = 'reserved'"));
        Assert.Equal(400_000, await GlobalAsync());
    }
}

public class AiBudgetSettleTests(BudgetFactory factory) : AiBudgetTestBase(factory), IClassFixture<BudgetFactory>
{
    [Fact]
    public async Task A_completed_call_replaces_its_reservation_with_the_real_cost_in_the_ledger_and_the_global_counter()
    {
        var a = await NewActorAsync();
        var outcome = await RunGatewayAsync(a);
        Assert.True(outcome.Ok, outcome.Failure);
        Assert.Equal(0, await F.ScalarAsync($"select count(*) from ai.ai_usage where user_id = '{a.UserId}' and outcome = 'reserved'"));
        var ledger = await F.ScalarAsync($"select coalesce(sum(est_cost_micros),0) from ai.ai_usage where user_id = '{a.UserId}'");
        Assert.True(ledger > 0);
        Assert.Equal(ledger, await GlobalAsync()); // the estimate was trued up to the actual figure in the counter too
        Assert.Equal(1, await F.ScalarAsync($"select count(*) from ai.ai_usage where user_id = '{a.UserId}' and outcome in ('ok','repaired')"));
    }
}

[Collection(RacePool.Name)]
public class AiBudgetGlobalCapTests(BudgetFactory factory) : AiBudgetTestBase(factory), IClassFixture<BudgetFactory>
{
    [Fact]
    public async Task The_global_monthly_cap_holds_under_parallel_load_from_many_users()
    {
        var actors = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => NewActorAsync()));
        var results = await Task.WhenAll(actors.Select(a => Task.Run(() => ReserveAsync(a.UserId, Guid.NewGuid(), 400_000))));
        Assert.Equal(12, results.Count(r => r)); // 12 x 400k = 4.8M fits in 5M, the 13th does not
        Assert.Equal(4_800_000, await GlobalAsync());
        Assert.Equal(4_800_000, await F.ScalarAsync("select coalesce(sum(est_cost_micros),0) from ai.ai_usage where outcome = 'reserved'"));
    }
}

public class AiBudgetSettleFailureTests(BudgetFactory factory) : AiBudgetTestBase(factory), IClassFixture<BudgetFactory>
{
    [Fact]
    public async Task When_settling_fails_the_reservation_stays_counted_and_further_paid_calls_stop()
    {
        var a = await NewActorAsync();
        await F.ExecAsync(
            "create function ai.t_fail_update() returns trigger language plpgsql as $$ begin raise exception 'boom'; end $$; " +
            "create trigger t_fail before update on ai.ai_usage for each row execute function ai.t_fail_update();");
        var first = await RunGatewayAsync(a);
        Assert.True(first.Ok); // the student still gets the answer
        // spend was not lost: the hold is still in the ledger and in the global counter
        var held = await F.ScalarAsync($"select est_cost_micros from ai.ai_usage where user_id = '{a.UserId}' and outcome = 'reserved'");
        Assert.True(held > 0);
        Assert.Equal(held, await GlobalAsync());
        Assert.True(F.Services.GetRequiredService<AiHealth>().IsOpen); // fail closed
        var rowsBefore = await F.ScalarAsync("select count(*) from ai.ai_usage");
        var second = await RunGatewayAsync(a);
        Assert.Equal(AiFailure.Unavailable, second.Failure);
        Assert.Equal(rowsBefore, await F.ScalarAsync("select count(*) from ai.ai_usage"));
    }
}

public class AiBudgetReserveFailureTests(BudgetFactory factory) : AiBudgetTestBase(factory), IClassFixture<BudgetFactory>
{
    [Fact]
    public async Task When_the_ledger_cannot_be_written_no_provider_call_is_made()
    {
        var a = await NewActorAsync();
        await F.ExecAsync("alter table ai.ai_usage rename to ai_usage_broken");
        var outcome = await RunGatewayAsync(a);
        Assert.False(outcome.Ok);
        Assert.Equal(AiFailure.Unavailable, outcome.Failure);
        Assert.True(F.Services.GetRequiredService<AiHealth>().IsOpen);
        Assert.Equal(0, await GlobalAsync());
    }
}
