using Tether.Ai.Budget;
using Tether.Ai.CatchUp;
using Tether.Ai.Provider;
using Tether.Ai.Validation;
using Tether.Lectures.Contracts;
using Tether.SharedKernel;
using Tether.Transcript.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tether.IntegrationTests;

/// <summary>N-02: a reservation whose settle never happened (crash, kill, shutdown mid-call) must not stay counted forever.</summary>
public class AiReservationReaperTests(BudgetFactory factory) : AiBudgetTestBase(factory), IClassFixture<BudgetFactory>
{
    private ReservationReaper Reaper => F.Services.GetRequiredService<ReservationReaper>();

    private Task<long> ReservedCountAsync(Guid userId) => F.ScalarAsync($"select count(*) from ai.ai_usage where user_id = '{userId}' and outcome = 'reserved'");

    [Fact]
    public async Task A_stale_reservation_is_settled_to_zero_and_its_hold_leaves_the_global_counter_while_a_fresh_one_is_kept()
    {
        await Reaper.SweepAsync(startup: true, default); // tests share one database: start from nothing held
        var a = await NewActorAsync();
        Assert.True(await ReserveAsync(a.UserId, Guid.NewGuid(), 400_000));
        Assert.Equal(400_000, await GlobalAsync());
        F.Clock.Advance(TimeSpan.FromMinutes(10)); // well past the call timeout plus the grace
        Assert.True(await ReserveAsync(a.UserId, Guid.NewGuid(), 300_000)); // a call that is genuinely still in flight

        var reaped = await Reaper.SweepAsync(startup: false, default);

        Assert.Equal(1, reaped);
        Assert.Equal(1, await ReservedCountAsync(a.UserId));
        Assert.Equal(1, await F.ScalarAsync($"select count(*) from ai.ai_usage where user_id = '{a.UserId}' and outcome = 'error' and est_cost_micros = 0"));
        Assert.Equal(300_000, await GlobalAsync());
        Assert.Equal(0, await Reaper.SweepAsync(startup: false, default)); // idempotent
        Assert.Equal(300_000, await GlobalAsync());
    }

    [Fact]
    public async Task A_reservation_inside_the_call_timeout_is_never_reaped_by_the_periodic_sweep()
    {
        await Reaper.SweepAsync(startup: true, default); // tests share one database: start from nothing held
        var a = await NewActorAsync();
        Assert.True(await ReserveAsync(a.UserId, Guid.NewGuid(), 400_000));
        F.Clock.Advance(TimeSpan.FromSeconds(30)); // longer than a call (20 s) but inside timeout plus grace

        Assert.Equal(0, await Reaper.SweepAsync(startup: false, default));
        Assert.Equal(1, await ReservedCountAsync(a.UserId));
    }

    [Fact]
    public async Task The_startup_sweep_releases_every_reservation_whatever_its_age_because_nothing_is_in_flight_in_a_new_process()
    {
        await Reaper.SweepAsync(startup: true, default); // tests share one database: start from nothing held
        var a = await NewActorAsync();
        Assert.True(await ReserveAsync(a.UserId, Guid.NewGuid(), 400_000));
        Assert.True(await ReserveAsync(a.UserId, Guid.NewGuid(), 100_000));

        var reaped = await Reaper.SweepAsync(startup: true, default);

        Assert.True(reaped >= 2);
        Assert.Equal(0, await ReservedCountAsync(a.UserId));
        Assert.Equal(0, await GlobalAsync());
    }

    /// <summary>Answers Active until the <paramref name="throwOnCall"/>th lookup, which is cancelled (the host is shutting down).</summary>
    private sealed class CancelledDirectory(int throwOnCall) : ISessionDirectory
    {
        private int _calls;
        public Task<SessionInfo?> FindAsync(Guid userId, Guid sessionId, CancellationToken ct = default) =>
            Interlocked.Increment(ref _calls) >= throwOnCall
                ? throw new OperationCanceledException()
                : Task.FromResult<SessionInfo?>(new SessionInfo(sessionId, userId, Guid.NewGuid(), LectureState.Active, 0, DateTimeOffset.UtcNow));
    }

    [Theory]
    [InlineData(1)] // the only check on the old code; the pre-check on the new
    [InlineData(2)] // the last gate after the reservation
    public async Task Cancellation_at_an_active_check_never_leaves_a_reservation_behind(int throwOnCall)
    {
        await Reaper.SweepAsync(startup: true, default); // tests share one database: start from nothing held
        var a = await NewActorAsync();
        var sid = Guid.NewGuid();
        await using var scope = Scopes.CreateUserScope(a.UserId);
        var sp = scope.ServiceProvider;
        var opts = F.Services.GetRequiredService<AiOptions>();
        var gateway = new AiGateway(F.Services.GetRequiredService<IAiClient>(), sp.GetRequiredService<UsageLedger>(), sp.GetRequiredService<BudgetGate>(),
            F.Services.GetRequiredService<AiHealth>(), opts, F.Services.GetRequiredService<SessionCutoff>(), new CancelledDirectory(throwOnCall), NullLogger<AiGateway>.Instance);
        var seg = new FinalSegment(sid, 1, 1, 0, 1500, "Mitosis has four phases.", null);
        var built = CatchUpRequests.Build(a.UserId, sid, Guid.NewGuid(), 5, [seg], opts.CatchUp);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => gateway.RunAsync(built.Request, j => CatchUpValidator.Validate(j, built.Context), default));

        Assert.Equal(0, await ReservedCountAsync(a.UserId));
        Assert.Equal(0, await GlobalAsync());
    }
}
