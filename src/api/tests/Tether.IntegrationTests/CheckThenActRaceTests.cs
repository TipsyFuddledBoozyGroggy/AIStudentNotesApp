using System.Net;
using Tether.Ai.Persistence;
using Tether.IntegrationTests.Infrastructure;
using Tether.IntegrationTests.Syllabus;
using Tether.Syllabus.Persistence;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Tether.IntegrationTests;

/// <summary>The AI module's DbContext with a <see cref="CommandTrap"/>.</summary>
public sealed class NotesPressRaceFactory : ApiFactory
{
    public CommandTrap Trap { get; } = new();

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(s => s.ConfigureDbContext<AiDbContext>(o => o.AddInterceptors(Trap)));
    }
}

/// <summary>
/// Deterministic form of the race behind the intermittent <c>AiNotesTests.Concurrent_presses_start_exactly_one_generation</c> failure
/// (one of five same-session presses answered 429). API_CONTRACTS section 17 item 4: "Concurrent presses start exactly one generation
/// (one-pending unique index; the losers get the winner's press with 200)".
/// </summary>
public class NotesPressRaceTests(NotesPressRaceFactory factory) : NotesTestBase(factory), IClassFixture<NotesPressRaceFactory>
{
    [Fact]
    public async Task A_same_session_press_that_commits_between_the_latest_read_and_the_per_user_check_is_returned_with_200_not_429()
    {
        var (a, id) = await StoppedSessionAsync();
        var winner = Guid.NewGuid();
        var trap = ((NotesPressRaceFactory)F).Trap;
        // The per-user "one pending press" check is the only EXISTS over lecture_notes that does not name the session.
        trap.Arm(sql => sql.Contains("lecture_notes", StringComparison.Ordinal) && sql.Contains("EXISTS", StringComparison.Ordinal)
                        && !sql.Contains("session_id", StringComparison.Ordinal),
            () => InsertPressAsync(id, a.UserId, winner, started: false)); // the concurrent winner commits: same user, SAME session

        var res = await PressAsync(a.Client, id);
        Assert.True(trap.Fired, "the race window was not reached");
        Assert.True(res.StatusCode == HttpStatusCode.OK, $"{(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
        Assert.Equal("nt_" + winner.ToString("N"), (await res.BodyAsync()).GetProperty("notesId").GetString());
        Assert.Equal(1, await NotesRowsAsync(id));
        Assert.Equal(0, await PressAuditsAsync(a.UserId)); // the loser starts nothing and audits nothing
        await F.ExecAsync($"update ai.lecture_notes set state = 'Unavailable', completed_at = now() where id = '{winner}'");
    }

    [Fact]
    public async Task Another_sessions_pending_press_is_still_429()
    {
        var (a, first) = await StoppedSessionAsync();
        var (_, second) = await StoppedSessionAsync(a);
        var trap = ((NotesPressRaceFactory)F).Trap;
        var other = Guid.NewGuid();
        trap.Arm(sql => sql.Contains("lecture_notes", StringComparison.Ordinal) && sql.Contains("EXISTS", StringComparison.Ordinal)
                        && !sql.Contains("session_id", StringComparison.Ordinal),
            () => InsertPressAsync(first, a.UserId, other, started: false)); // a DIFFERENT session of the same user
        await (await PressAsync(a.Client, second)).AssertProblemAsync(HttpStatusCode.TooManyRequests, "rate_limited");
        Assert.True(trap.Fired);
        Assert.Equal(0, await NotesRowsAsync(second));
        await F.ExecAsync($"update ai.lecture_notes set state = 'Unavailable', completed_at = now() where id = '{other}'");
    }
}

/// <summary>The syllabus module's DbContext with a <see cref="CommandTrap"/>; worker off, as in SyllabusIdempotencyAndQuotaTests.</summary>
public sealed class SyllabusCreateRaceFactory : ApiFactory
{
    public CommandTrap Trap { get; } = new();

    protected override IEnumerable<KeyValuePair<string, string?>> Settings =>
        [new("Syllabus:Worker:Enabled", "false"), new("Syllabus:NoticeVersions:0", "draft-2026-10")];

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(s => s.ConfigureDbContext<SyllabusDbContext>(o => o.AddInterceptors(Trap)));
    }
}

/// <summary>
/// Deterministic form of the race behind the intermittent <c>Two_simultaneous_identical_creates_yield_one_syllabus_and_one_job</c> failure
/// (one response 409). API_CONTRACTS_P2 section 2.1: a retry with the same key and fingerprint while the original job is running "gets the
/// replay (202, current representation, Idempotency-Replayed: true), not 409 extraction_in_progress".
/// </summary>
public class SyllabusCreateRaceTests(SyllabusCreateRaceFactory factory) : SyllabusTestBase(factory), IClassFixture<SyllabusCreateRaceFactory>
{
    [Fact]
    public async Task An_identical_create_that_commits_between_the_key_lookup_and_the_quota_check_is_a_replay_not_409()
    {
        var a = await NewActorAsync(); var course = await CourseAsync(a.Client);
        var key = NewKey(); var body = Paste("Quiz 1 due Oct 11, 2026 at 9:00 AM");
        var trap = ((SyllabusCreateRaceFactory)F).Trap;
        HttpResponseMessage? first = null;
        // Step 6's in-flight quota check is the first EXISTS over extraction_jobs; the identical request completes right before it.
        trap.Arm(sql => sql.Contains("extraction_jobs", StringComparison.Ordinal) && sql.Contains("EXISTS", StringComparison.Ordinal),
            async () => first = await a.Client.SendAsync(Req(HttpMethod.Post, $"{Api}/courses/{course}/syllabi", body, key)));

        var second = await a.Client.SendAsync(Req(HttpMethod.Post, $"{Api}/courses/{course}/syllabi", body, key));
        Assert.True(trap.Fired, "the race window was not reached");
        Assert.Equal(HttpStatusCode.Accepted, first!.StatusCode);
        Assert.True(second.StatusCode == HttpStatusCode.Accepted, $"{(int)second.StatusCode} {await second.Content.ReadAsStringAsync()}");
        Assert.Equal("true", second.Headers.TryGetValues("Idempotency-Replayed", out var v) ? v.Single() : null);
        Assert.Equal((await first.BodyAsync()).GetProperty("id").GetString(), (await second.BodyAsync()).GetProperty("id").GetString());
        Assert.Equal(1, await F.ScalarAsync($"select count(*) from syllabus.syllabi where user_id = '{a.UserId}'"));
        Assert.Equal(1, await F.ScalarAsync($"select count(*) from syllabus.extraction_jobs where user_id = '{a.UserId}'"));
    }

    [Fact]
    public async Task A_different_create_while_one_is_in_flight_is_still_409_extraction_in_progress()
    {
        var a = await NewActorAsync(); var course = await CourseAsync(a.Client);
        Assert.Equal(HttpStatusCode.Accepted, (await a.Client.SendAsync(Req(HttpMethod.Post, $"{Api}/courses/{course}/syllabi", Paste("Quiz 1 due Oct 11, 2026"), NewKey()))).StatusCode);
        var other = await a.Client.SendAsync(Req(HttpMethod.Post, $"{Api}/courses/{course}/syllabi", Paste("Quiz 2 due Oct 12, 2026"), NewKey()));
        await other.AssertProblemAsync(HttpStatusCode.Conflict, "extraction_in_progress");
    }
}
