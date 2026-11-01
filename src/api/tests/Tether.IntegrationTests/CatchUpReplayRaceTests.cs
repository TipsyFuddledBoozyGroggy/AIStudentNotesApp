using System.Net;
using Tether.Ai.CatchUp;
using Tether.Ai.Persistence;
using Tether.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Tether.IntegrationTests;

/// <summary>
/// The AI module's DbContext with a <see cref="CommandTrap"/>. Slow mock AI so a catch-up stays Pending through the test, and two requests per
/// minute per session so the limiter fall-back can be reached.
/// </summary>
public sealed class CatchUpReplayRaceFactory : ApiFactory
{
    public CommandTrap Trap { get; } = new();

    protected override IEnumerable<KeyValuePair<string, string?>> Settings => [new("Ai:Mock:LatencyMs", "6000"), new("Ai:CatchUp:PerMinute", "2")];

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(s => s.ConfigureDbContext<AiDbContext>(o => o.AddInterceptors(Trap)));
    }
}

/// <summary>
/// SBR-10 (docs/security/syllabus-fixes-rereview.md): deterministic form of the intermittent
/// <c>AiRouteTests.Concurrent_requests_with_the_same_key_make_exactly_one_catch_up_and_all_get_it</c> failure (4 replay headers instead of 5).
/// A request whose key lookup missed, and which then finds the catch-up a concurrent request with the SAME key committed meanwhile, is a
/// replay of that key: 200, the same catch-up, and <c>Idempotency-Replayed: true</c>. Each fall-back that can find such a row is covered.
/// </summary>
public class CatchUpReplayRaceTests(CatchUpReplayRaceFactory factory) : AiRouteTestBase(factory), IClassFixture<CatchUpReplayRaceFactory>
{
    private CommandTrap Trap => ((CatchUpReplayRaceFactory)F).Trap;

    /// <summary>The n-th query over ai.catch_ups with an ORDER BY (the key lookups have none; the coalesce, limiter and unique-violation fall-backs do).</summary>
    private static Func<string, bool> NthOrderedCatchUpQuery(int n)
    {
        var seen = 0;
        return sql => sql.Contains("catch_ups", StringComparison.Ordinal) && sql.Contains("ORDER BY", StringComparison.Ordinal) && ++seen == n;
    }

    private static string? ReplayHeader(HttpResponseMessage r) => r.Headers.TryGetValues("Idempotency-Replayed", out var v) ? v.Single() : null;

    private async Task AssertReplayOfAsync(HttpResponseMessage winner, HttpResponseMessage loser, string sessionId)
    {
        Assert.Equal(HttpStatusCode.OK, winner.StatusCode);
        Assert.Null(ReplayHeader(winner));                                  // the winner made it
        Assert.True(loser.StatusCode == HttpStatusCode.OK, $"{(int)loser.StatusCode} {await loser.Content.ReadAsStringAsync()}");
        Assert.Equal((await winner.BodyAsync()).GetProperty("catchUpId").GetString(), (await loser.BodyAsync()).GetProperty("catchUpId").GetString());
        Assert.Equal("true", ReplayHeader(loser));                          // ... and the loser was given it as a replay of the same key
        Assert.Equal(1, await F.ScalarAsync($"select count(*) from ai.catch_ups where session_id = '{sessionId}' and idempotency_key is not null"));
    }

    [Fact]
    public async Task A_same_key_request_that_commits_between_the_key_lookup_and_the_coalesce_query_is_returned_as_a_replay()
    {
        var (a, id) = await LiveSessionAsync();
        await SeedFinalsAsync(id, a.UserId);
        HttpResponseMessage? winner = null;
        // the first ordered catch-up query of the request under test is the coalesce query, right after its key lookup missed
        Trap.Arm(NthOrderedCatchUpQuery(1), async () => winner = await QuickRecapAsync(a.Client, id, key: "k-sbr10-coalesce"));

        var loser = await QuickRecapAsync(a.Client, id, key: "k-sbr10-coalesce");
        Assert.True(Trap.Fired, "the race window was not reached");
        await AssertReplayOfAsync(winner!, loser, id);
        Assert.Equal(1, await CatchUpCountAsync(id));
    }

    [Fact]
    public async Task A_same_key_request_that_commits_after_the_rate_limited_key_re_check_is_returned_as_a_replay()
    {
        var (a, id) = await LiveSessionAsync();
        await SeedFinalsAsync(id, a.UserId);
        // use up this session's two tokens on other windows (both stay Pending under the slow mock)
        Assert.Equal(HttpStatusCode.OK, (await QuickRecapAsync(a.Client, id, new { windowMinutes = 2 })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await QuickRecapAsync(a.Client, id, new { windowMinutes = 10 })).StatusCode);
        var sid = Guid.Parse(id);
        var limiter = F.Services.GetRequiredService<CatchUpLimiter>();
        HttpResponseMessage? winner = null;
        // 1st ordered query: the coalesce (misses, window 5); limiter refuses; the keyed re-check misses; 2nd ordered query: the "running" fall-back.
        // Right before it, a same-key request (given back one token, as a request that finished in the meantime would) creates the catch-up.
        Trap.Arm(NthOrderedCatchUpQuery(2), async () =>
        {
            limiter.Release(sid);
            winner = await QuickRecapAsync(a.Client, id, new { windowMinutes = 5 }, "k-sbr10-limited");
        });

        var loser = await QuickRecapAsync(a.Client, id, new { windowMinutes = 5 }, "k-sbr10-limited");
        Assert.True(Trap.Fired, "the race window was not reached");
        await AssertReplayOfAsync(winner!, loser, id);
        Assert.Equal(3, await CatchUpCountAsync(id));
    }

    [Fact]
    public async Task A_same_key_request_that_commits_after_the_unique_violation_key_re_check_is_returned_as_a_replay()
    {
        var (a, id) = await LiveSessionAsync();
        await SeedFinalsAsync(id, a.UserId);
        // a keyless in-flight catch-up for window 5, older than the coalesce window: the coalesce query misses it, the insert then hits ux_catch_ups_pending
        var first = await (await QuickRecapAsync(a.Client, id)).BodyAsync();
        Assert.Equal("Pending", first.GetProperty("aiState").GetString());
        await F.ExecAsync($"update ai.catch_ups set created_at = created_at - interval '60 seconds' where session_id = '{id}'");
        HttpResponseMessage? winner = null;
        // 1st ordered query: the coalesce (misses); the insert violates the pending index; the keyed re-check misses; 2nd ordered query: the
        // "existing" fall-back. Right before it the old catch-up finishes and a same-key request creates the new in-flight one.
        Trap.Arm(NthOrderedCatchUpQuery(2), async () =>
        {
            await F.ExecAsync($"update ai.catch_ups set ai_state = 'Unavailable' where session_id = '{id}'");
            winner = await QuickRecapAsync(a.Client, id, key: "k-sbr10-unique");
        });

        var loser = await QuickRecapAsync(a.Client, id, key: "k-sbr10-unique");
        Assert.True(Trap.Fired, "the race window was not reached");
        await AssertReplayOfAsync(winner!, loser, id);
        Assert.Equal(2, await CatchUpCountAsync(id));
    }

    [Fact]
    public async Task A_keyless_catch_up_shared_by_a_keyed_request_is_not_called_a_replay()
    {
        // control: sharing someone else's in-flight catch-up (no key, or another key) is not a replay of this request's key
        var (a, id) = await LiveSessionAsync();
        await SeedFinalsAsync(id, a.UserId);
        var keyless = await QuickRecapAsync(a.Client, id);
        var keyed = await QuickRecapAsync(a.Client, id, key: "k-sbr10-other");
        var otherKey = await QuickRecapAsync(a.Client, id, key: "k-sbr10-other-2");
        foreach (var r in new[] { keyless, keyed, otherKey }) Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Null(ReplayHeader(keyed));
        Assert.Null(ReplayHeader(otherKey));
        var cu = (await keyless.BodyAsync()).GetProperty("catchUpId").GetString();
        Assert.Equal(cu, (await keyed.BodyAsync()).GetProperty("catchUpId").GetString());
        Assert.Equal(cu, (await otherKey.BodyAsync()).GetProperty("catchUpId").GetString());
    }
}
