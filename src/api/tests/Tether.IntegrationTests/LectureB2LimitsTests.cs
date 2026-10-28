using System.Net;
using Tether.IntegrationTests.Infrastructure;
using Tether.Lectures.Application;
using Tether.Lectures.Persistence;
using Tether.SharedKernel;
using Tether.SharedKernel.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Tether.IntegrationTests;

/// <summary>B2 deferrals from docs/security/m2-b1-review.md: B1-05 (per-session audit coalescing) and B1-06 (transition caps).</summary>
public class LectureB2LimitsTests(LectureB2LimitsTests.Factory f) : LectureTestBase(f), IClassFixture<LectureB2LimitsTests.Factory>
{
    public sealed class Factory : ApiFactory
    {
        protected override IEnumerable<KeyValuePair<string, string?>> Settings =>
            [new("Lectures:MaxTransitionsPerSession", "30")];
    }

    private sealed class FakeUser(Guid? id, Guid? sid) : ICurrentUser
    {
        public Guid? UserId { get; } = id;
        public Guid? AuthSessionId { get; } = sid;
    }

    [Fact]
    public async Task Rejected_ticket_audits_are_coalesced_per_user_and_per_session_per_minute()
    {
        var a = await NewActorAsync();
        var course = await CourseAsync(a.Client);
        var first = (await StartOkAsync(a.Client, course)).GetProperty("id").GetString()!;
        await PostAsync(a.Client, $"/sessions/{first}/stop");
        var second = (await StartOkAsync(a.Client, course)).GetProperty("id").GetString()!;
        var sid = Guid.Parse((await F.TextAsync($"select id from identity.sessions where user_id = '{a.UserId}' and revoked_at is null limit 1"))!);
        var fake = new FakeUser(a.UserId, sid);
        var options = new DbContextOptionsBuilder<LecturesDbContext>()
            .UseNpgsql(F.ConnectionString, n => n.MigrationsHistoryTable("__ef_migrations_history", LecturesDbContext.Schema))
            .AddInterceptors(new OwnershipSaveChangesInterceptor(fake)).Options;
        var consumer = new AudioTicketService(new LecturesDbContext(options, fake), fake, F.Clock);

        for (var i = 0; i < 5; i++) Assert.False(await consumer.ConsumeAsync("x", Guid.Parse(first)));
        for (var i = 0; i < 5; i++) Assert.False(await consumer.ConsumeAsync("x", Guid.Parse(second)));
        for (var i = 0; i < 5; i++) Assert.False(await consumer.ConsumeAsync("x", Guid.NewGuid()));

        // One row per session that exists for this user, plus one for "no such session", each once per minute.
        Assert.Equal(1, await CountAsync("audit_events", $"user_id = '{a.UserId}' and action = 'audio_ticket.rejected' and session_id = '{first}'"));
        Assert.Equal(1, await CountAsync("audit_events", $"user_id = '{a.UserId}' and action = 'audio_ticket.rejected' and session_id = '{second}'"));
        Assert.Equal(1, await CountAsync("audit_events", $"user_id = '{a.UserId}' and action = 'audio_ticket.rejected' and session_id is null"));
    }

    [Fact]
    public async Task Pause_resume_churn_is_capped_per_session_per_minute_with_429_and_stop_still_works()
    {
        var a = await NewActorAsync();
        var id = (await StartOkAsync(a.Client, await CourseAsync(a.Client))).GetProperty("id").GetString()!;
        HttpStatusCode last = default;
        var ok = 0;
        for (var i = 0; i < 20; i++)
        {
            var res = await PostAsync(a.Client, $"/sessions/{id}/{(i % 2 == 0 ? "pause" : "resume")}");
            last = res.StatusCode;
            if (res.StatusCode == HttpStatusCode.TooManyRequests)
            {
                await res.AssertProblemAsync(HttpStatusCode.TooManyRequests, "rate_limited");
                break;
            }
            ok++;
        }
        Assert.Equal(HttpStatusCode.TooManyRequests, last);
        Assert.Equal(12, ok);

        // Stop is never blocked by the cap (the student can always end a session).
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(a.Client, $"/sessions/{id}/stop")).StatusCode);
    }

    [Fact]
    public async Task The_per_minute_window_reopens_after_a_minute()
    {
        var a = await NewActorAsync();
        var id = (await StartOkAsync(a.Client, await CourseAsync(a.Client))).GetProperty("id").GetString()!;
        for (var i = 0; i < 12; i++) Assert.Equal(HttpStatusCode.OK, (await PostAsync(a.Client, $"/sessions/{id}/{(i % 2 == 0 ? "pause" : "resume")}")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await PostAsync(a.Client, $"/sessions/{id}/pause")).StatusCode);
        F.Clock.Advance(TimeSpan.FromSeconds(61));
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(a.Client, $"/sessions/{id}/pause")).StatusCode);
    }

    [Fact]
    public async Task A_hard_maximum_of_transitions_per_session_applies_across_minutes()
    {
        var a = await NewActorAsync();
        var id = (await StartOkAsync(a.Client, await CourseAsync(a.Client))).GetProperty("id").GetString()!;
        // Factory sets the per-session maximum to 30 (default 200).
        var accepted = 0;
        for (var round = 0; round < 6; round++)
        {
            for (var i = 0; i < 12; i++)
            {
                var res = await PostAsync(a.Client, $"/sessions/{id}/{(accepted % 2 == 0 ? "pause" : "resume")}");
                if (res.StatusCode == HttpStatusCode.TooManyRequests) break;
                Assert.Equal(HttpStatusCode.OK, res.StatusCode);
                accepted++;
            }
            F.Clock.Advance(TimeSpan.FromSeconds(61));
        }
        Assert.Equal(29, accepted); // seq 1 is the Start event; the maximum counts all events of the session
        Assert.Equal(HttpStatusCode.TooManyRequests, (await PostAsync(a.Client, $"/sessions/{id}/{(accepted % 2 == 0 ? "pause" : "resume")}")).StatusCode);
    }
}
