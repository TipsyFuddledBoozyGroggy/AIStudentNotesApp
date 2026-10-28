using System.Net;
using System.Net.Http.Json;
using Npgsql;
using Tether.Courses.Contracts;
using Tether.IntegrationTests.Infrastructure;
using Tether.Lectures.Application;
using Tether.Lectures.Persistence;
using Tether.SharedKernel;
using Tether.SharedKernel.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Tether.IntegrationTests;

/// <summary>
/// Fix pass B1.5 (docs/security/m2-b1-review.md): DB-level cleanup and integrity (B1-01/02/03), idempotency (B1-04),
/// audit bounding and sweeping (B1-05/06/10), Resume consent re-check (B1-08), constraint and title hygiene (B1-11).
/// </summary>
public class LectureB15Tests(ApiFactory factory) : LectureTestBase(factory), IClassFixture<ApiFactory>
{
    private async Task<(Actor A, string Course, string Session)> LiveAsync()
    {
        var a = await NewActorAsync();
        var course = await CourseAsync(a.Client);
        var s = await StartOkAsync(a.Client, course);
        return (a, course, s.GetProperty("id").GetString()!);
    }

    private async Task AssertFailsAsync(string sql)
    {
        var ex = await Assert.ThrowsAsync<PostgresException>(() => F.ExecAsync(sql));
        Assert.NotNull(ex.SqlState);
    }

    private async Task<long> ChildCountsAsync(string userId, string sessionId) =>
        await F.ScalarAsync($"""
            select (select count(*) from lectures.lecture_sessions where user_id = '{userId}')
                 + (select count(*) from lectures.audio_tickets where user_id = '{userId}')
                 + (select count(*) from lectures.session_events where user_id = '{userId}')
                 + (select count(*) from lectures.session_counters where user_id = '{userId}')
                 + (select count(*) from lectures.idempotency_keys where user_id = '{userId}')
            """);

    // ---------------- B1-01: cleanup cascades ----------------

    [Fact]
    public async Task Purging_a_course_removes_its_sessions_tickets_events_counters_and_keys_but_keeps_consent_and_audit_evidence()
    {
        var a = await NewActorAsync();
        var course = await CourseAsync(a.Client);
        var s = await StartOkAsync(a.Client, course, "k-" + Guid.NewGuid());
        var id = s.GetProperty("id").GetString()!;
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(a.Client, $"/sessions/{id}/audio-ticket")).StatusCode);
        Assert.True(await ChildCountsAsync(a.UserId.ToString(), id) >= 5);

        Assert.Equal(HttpStatusCode.NoContent, (await a.Client.DeleteAsync($"/api/v1/courses/{course}?purge=true")).StatusCode);

        Assert.Equal(0, await ChildCountsAsync(a.UserId.ToString(), id));
        await (await a.Client.GetAsync($"/api/v1/sessions/{id}")).AssertProblemAsync(HttpStatusCode.NotFound, "not_found");
        // B1-09 (open decision): evidence is kept by default.
        Assert.Equal(1, await CountAsync("consent_records", $"user_id = '{a.UserId}'"));
        Assert.True(await CountAsync("consent_events", $"user_id = '{a.UserId}'") >= 1);
        Assert.True(await CountAsync("audit_events", $"user_id = '{a.UserId}'") >= 1);
    }

    [Fact]
    public async Task Deleting_a_user_removes_their_sessions_and_children_only_and_keeps_evidence_until_the_purge_path_runs()
    {
        var (gone, _, goneSession) = await LiveAsync();
        var (kept, _, keptSession) = await LiveAsync();
        await PostAsync(gone.Client, $"/sessions/{goneSession}/audio-ticket");

        await F.ExecAsync($"delete from identity.users where id = '{gone.UserId}'");

        Assert.Equal(0, await ChildCountsAsync(gone.UserId.ToString(), goneSession));
        Assert.True(await ChildCountsAsync(kept.UserId.ToString(), keptSession) >= 3);
        Assert.Equal(1, await CountAsync("consent_records", $"user_id = '{gone.UserId}'"));
        Assert.True(await CountAsync("audit_events", $"user_id = '{gone.UserId}'") >= 1);

        // The one explicit purge path (retention / erasure), and only for the named user.
        await F.ExecAsync($"select lectures.purge_evidence('{gone.UserId}')");
        Assert.Equal(0, await CountAsync("consent_records", $"user_id = '{gone.UserId}'"));
        Assert.Equal(0, await CountAsync("consent_events", $"user_id = '{gone.UserId}'"));
        Assert.Equal(0, await CountAsync("audit_events", $"user_id = '{gone.UserId}'"));
        Assert.Equal(1, await CountAsync("consent_records", $"user_id = '{kept.UserId}'"));
    }

    [Fact]
    public async Task Database_rejects_a_session_for_an_unknown_user_or_course()
    {
        var (a, course, _) = await LiveAsync();
        var ghost = Ids.New();
        var consent = await F.TextAsync($"select id from lectures.consent_records where user_id = '{a.UserId}'");
        await AssertFailsAsync($"insert into lectures.lecture_sessions (id, user_id, course_id, state, started_at, consent_record_id) values ('{Ids.New()}', '{ghost}', '{course}', 'Stopped', now(), '{consent}')");
        await AssertFailsAsync($"insert into lectures.lecture_sessions (id, user_id, course_id, state, started_at, stopped_at, consent_record_id) values ('{Ids.New()}', '{a.UserId}', '{ghost}', 'Stopped', now(), now(), '{consent}')");
    }

    private sealed class DenyAllCourses : ICourseOwnership
    {
        public Task<bool> IsOwnedByAsync(Guid userId, Guid courseId, CancellationToken ct = default) => Task.FromResult(false);
    }

    private sealed class FakeUser(Guid id, Guid sid) : ICurrentUser
    {
        public Guid? UserId { get; } = id;
        public Guid? AuthSessionId { get; } = sid;
    }

    private SessionService ServiceWith(Actor a, ICourseOwnership courses)
    {
        var sid = Guid.Parse(F.TextAsync($"select id from identity.sessions where user_id = '{a.UserId}' and revoked_at is null limit 1").Result!);
        var fake = new FakeUser(a.UserId, sid);
        var options = new DbContextOptionsBuilder<LecturesDbContext>()
            .UseNpgsql(F.ConnectionString, n => n.MigrationsHistoryTable("__ef_migrations_history", LecturesDbContext.Schema))
            .AddInterceptors(new OwnershipSaveChangesInterceptor(fake)).Options;
        return new SessionService(new LecturesDbContext(options, fake), fake, courses, new ConsentTextRegistry(), F.Clock,
            new LectureLimits(12, 200, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30), TimeSpan.FromHours(3)), new SessionActivityTracker(F.Clock), [], F.Services, Microsoft.Extensions.Logging.Abstractions.NullLogger<SessionService>.Instance);
    }

    [Fact]
    public async Task Resume_and_ticket_issuance_return_404_when_the_course_is_no_longer_owned()
    {
        var (a, _, id) = await LiveAsync();
        await PostAsync(a.Client, $"/sessions/{id}/pause");
        var svc = ServiceWith(a, new DenyAllCourses());
        var resumed = await svc.TransitionAsync(Guid.Parse(id), Transition.Resume, default);
        Assert.NotNull(resumed.Problem);
        Assert.Null(resumed.Row);
        Assert.Equal("Paused", await F.TextAsync($"select state from lectures.lecture_sessions where id = '{id}'"));

        await PostAsync(a.Client, $"/sessions/{id}/resume");
        var (ticket, problem) = await ServiceWith(a, new DenyAllCourses()).IssueTicketAsync(Guid.Parse(id), default);
        Assert.Null(ticket);
        Assert.NotNull(problem);
        Assert.Equal(0, await CountAsync("audio_tickets", $"session_id = '{id}'"));
    }

    // ---------------- B1-03: composite FKs ----------------

    [Fact]
    public async Task Database_rejects_child_rows_that_name_a_different_user_than_their_session()
    {
        var (a, _, id) = await LiveAsync();
        var b = await NewActorAsync();
        var consent = await F.TextAsync($"select id from lectures.consent_records where session_id = '{id}'");

        await AssertFailsAsync($"insert into lectures.audio_tickets (id, user_id, session_id, auth_session_id, token_hash, created_at, expires_at) values ('{Ids.New()}', '{b.UserId}', '{id}', '{Ids.New()}', '\\x01', now(), now())");
        await AssertFailsAsync($"insert into lectures.session_events (session_id, seq, user_id, state, occurred_at) values ('{id}', 99, '{b.UserId}', 'Active', now())");
        await AssertFailsAsync($"insert into lectures.idempotency_keys (id, user_id, key_hash, fingerprint, session_id, created_at) values ('{Ids.New()}', '{b.UserId}', '\\x01', '\\x01', '{id}', now())");
        await AssertFailsAsync($"update lectures.session_counters set user_id = '{b.UserId}' where session_id = '{id}'");
        await AssertFailsAsync($"update lectures.session_events set user_id = '{b.UserId}' where session_id = '{id}'");
        await AssertFailsAsync($"insert into lectures.consent_events (id, user_id, consent_record_id, session_id, kind, text_version, occurred_at) values ('{Ids.New()}', '{b.UserId}', '{consent}', '{id}', 'Pause', 'draft-1', now())");
        // A session cannot be reassigned to another user, nor point at another user's consent record.
        await AssertFailsAsync($"update lectures.lecture_sessions set user_id = '{b.UserId}' where id = '{id}'");
        Assert.Equal(a.UserId.ToString(), await F.TextAsync($"select user_id from lectures.lecture_sessions where id = '{id}'"));
    }

    [Fact]
    public async Task Database_rejects_a_session_that_points_at_another_users_consent_record()
    {
        var (a, course, id) = await LiveAsync();
        var (b, _, bSession) = await LiveAsync();
        var bConsent = await F.TextAsync($"select id from lectures.consent_records where session_id = '{bSession}'");
        await F.ExecAsync($"update lectures.lecture_sessions set state = 'Stopped', stopped_at = now() where id = '{id}'");
        await AssertFailsAsync($"update lectures.lecture_sessions set consent_record_id = '{bConsent}' where id = '{id}'");
        _ = (a, course, b);
    }

    // ---------------- B1-02: append-only evidence ----------------

    [Fact]
    public async Task Consent_events_and_audit_events_reject_update_delete_and_truncate_on_the_app_path()
    {
        var (a, _, id) = await LiveAsync();
        await PostAsync(a.Client, $"/sessions/{id}/pause");
        await AssertFailsAsync($"update lectures.consent_events set kind = 'Stop' where user_id = '{a.UserId}'");
        await AssertFailsAsync($"delete from lectures.consent_events where user_id = '{a.UserId}'");
        await AssertFailsAsync($"update lectures.audit_events set action = 'x' where user_id = '{a.UserId}'");
        await AssertFailsAsync($"delete from lectures.audit_events where user_id = '{a.UserId}'");
        await AssertFailsAsync("truncate lectures.consent_events");
        await AssertFailsAsync("truncate lectures.audit_events");
        Assert.True(await CountAsync("consent_events", $"user_id = '{a.UserId}'") >= 2);
        // Normal app writes (append) still work: pause/resume above wrote rows.
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(a.Client, $"/sessions/{id}/resume")).StatusCode);
    }

    [Fact]
    public async Task Deleting_a_session_through_the_api_keeps_its_evidence_rows()
    {
        var (a, _, id) = await LiveAsync();
        Assert.Equal(HttpStatusCode.NoContent, (await a.Client.DeleteAsync($"/api/v1/sessions/{id}")).StatusCode);
        Assert.True(await CountAsync("consent_events", $"session_id = '{id}'") >= 2);
        Assert.True(await CountAsync("audit_events", $"session_id = '{id}'") >= 2);
    }

    // ---------------- B1-04: idempotency ----------------

    [Fact]
    public void Idempotency_fingerprint_is_not_ambiguous_across_field_boundaries()
    {
        var c = Guid.NewGuid();
        var one = SessionService.Fingerprint(c, "T|draft-1", "draft-1", true);
        var two = SessionService.Fingerprint(c, "T", "draft-1|draft-1", true);
        Assert.NotEqual(one, two);
        Assert.NotEqual(SessionService.Fingerprint(c, "a", "b|c", true), SessionService.Fingerprint(c, "a|b", "c", true));
        Assert.NotEqual(SessionService.Fingerprint(c, null, "v", true), SessionService.Fingerprint(c, "", "v", true));
        Assert.NotEqual(SessionService.Fingerprint(c, "t", "v", true), SessionService.Fingerprint(c, "t", "v", false));
        Assert.Equal(SessionService.Fingerprint(c, "t", "v", true), SessionService.Fingerprint(c, "t", "v", true));
    }

    [Fact]
    public async Task A_replay_that_carries_an_unregistered_consent_version_is_403_not_a_replayed_success()
    {
        var a = await NewActorAsync();
        var course = await CourseAsync(a.Client);
        var key = "k-" + Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Created, (await StartAsync(a.Client, course, key, "T|draft-1")).StatusCode);

        var bad = await StartAsync(a.Client, course, key, "T", Consent("draft-1|draft-1"));
        await bad.AssertProblemAsync(HttpStatusCode.Forbidden, "consent_required");
        Assert.False(bad.Headers.Contains("Idempotency-Replayed"));
        var notAck = await StartAsync(a.Client, course, key, "T|draft-1", new { acknowledged = false, textVersion = "draft-1" });
        await notAck.AssertProblemAsync(HttpStatusCode.Forbidden, "consent_required");
        Assert.Equal(1, await CountAsync("lecture_sessions", $"user_id = '{a.UserId}'"));
    }

    [Fact]
    public async Task A_valid_replay_still_returns_the_original_and_a_different_valid_request_with_the_key_is_400()
    {
        var a = await NewActorAsync();
        var course = await CourseAsync(a.Client);
        var key = "k-" + Guid.NewGuid();
        var first = await StartAsync(a.Client, course, key, "T|draft-1");
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var again = await StartAsync(a.Client, course, key, "T|draft-1");
        Assert.Equal("true", again.Headers.GetValues("Idempotency-Replayed").Single());
        var other = await StartAsync(a.Client, course, key, "T");
        await other.AssertProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
    }

    // ---------------- B1-05/06/10: bounded audit and sweeper ----------------

    [Fact]
    public async Task Failed_ticket_consumes_write_a_bounded_number_of_audit_rows_and_never_attacker_chosen_session_ids()
    {
        var (a, _, id) = await LiveAsync();
        var sid = Guid.Parse((await F.TextAsync($"select id from identity.sessions where user_id = '{a.UserId}' and revoked_at is null limit 1"))!);
        var fake = new FakeUser(a.UserId, sid);
        var options = new DbContextOptionsBuilder<LecturesDbContext>()
            .UseNpgsql(F.ConnectionString, n => n.MigrationsHistoryTable("__ef_migrations_history", LecturesDbContext.Schema))
            .AddInterceptors(new OwnershipSaveChangesInterceptor(fake)).Options;
        var consumer = new AudioTicketService(new LecturesDbContext(options, fake), fake, F.Clock);

        for (var i = 0; i < 50; i++) Assert.False(await consumer.ConsumeAsync("x", Guid.NewGuid()));
        for (var i = 0; i < 50; i++) Assert.False(await consumer.ConsumeAsync(new string('a', 43), Guid.NewGuid()));
        Assert.Equal(1, await CountAsync("audit_events", $"user_id = '{a.UserId}' and action = 'audio_ticket.rejected'"));
        Assert.Equal(0, await CountAsync("audit_events", $"user_id = '{a.UserId}' and action = 'audio_ticket.rejected' and session_id is not null and session_id <> '{id}'"));

        // A new window may record one more.
        F.Clock.Advance(TimeSpan.FromSeconds(61));
        Assert.False(await consumer.ConsumeAsync("x", Guid.Parse(id)));
        Assert.Equal(2, await CountAsync("audit_events", $"user_id = '{a.UserId}' and action = 'audio_ticket.rejected'"));
        Assert.Equal(1, await CountAsync("audit_events", $"user_id = '{a.UserId}' and action = 'audio_ticket.rejected' and session_id = '{id}'"));
    }

    [Fact]
    public async Task Sweeper_removes_expired_tickets_and_expired_idempotency_rows_for_all_users_and_leaves_live_ones()
    {
        var (a, course, id) = await LiveAsync();
        var key = "k-" + Guid.NewGuid();
        // A second user with a stopped session carrying an idempotency key.
        var b = await NewActorAsync();
        var bCourse = await CourseAsync(b.Client);
        var bs = await StartOkAsync(b.Client, bCourse, key);
        await PostAsync(b.Client, $"/sessions/{bs.GetProperty("id").GetString()}/audio-ticket");
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(a.Client, $"/sessions/{id}/audio-ticket")).StatusCode);

        using (var scope = F.Services.CreateScope())
        {
            var early = await scope.ServiceProvider.GetRequiredService<ExpiredRowSweeper>().SweepAsync(default);
            Assert.Equal((0, 0), early); // nothing expired yet
        }
        var mine = $"user_id in ('{a.UserId}','{b.UserId}')";
        Assert.Equal(2, await CountAsync("audio_tickets", mine));

        F.Clock.Advance(TimeSpan.FromMinutes(10)); // tickets expired (30 s + grace)
        using (var scope = F.Services.CreateScope())
        {
            var (tickets, keys) = await scope.ServiceProvider.GetRequiredService<ExpiredRowSweeper>().SweepAsync(default);
            Assert.True(tickets >= 2);
            Assert.Equal(0, keys);
        }
        Assert.Equal(0, await CountAsync("audio_tickets", mine));
        Assert.Equal(1, await CountAsync("idempotency_keys", mine));

        F.Clock.Advance(TimeSpan.FromHours(25));
        using (var scope = F.Services.CreateScope())
        {
            var (_, keys) = await scope.ServiceProvider.GetRequiredService<ExpiredRowSweeper>().SweepAsync(default);
            Assert.True(keys >= 1);
        }
        Assert.Equal(0, await CountAsync("idempotency_keys", mine));
        Assert.Equal(2, await F.ScalarAsync("select count(*) from lectures.lecture_sessions where user_id in ('" + a.UserId + "','" + b.UserId + "')"));
        _ = course;
    }

    [Fact]
    public void The_sweeper_is_registered_as_a_hosted_service()
    {
        Assert.Contains(F.Services.GetServices<Microsoft.Extensions.Hosting.IHostedService>(), h => h.GetType().Name == "ExpiredRowSweeperService");
    }

    // ---------------- B1-08, B1-11 ----------------

    [Fact]
    public async Task Resume_rechecks_the_consent_version_and_returns_403_for_a_retired_one()
    {
        var (a, _, id) = await LiveAsync();
        await PostAsync(a.Client, $"/sessions/{id}/pause");
        await F.ExecAsync($"update lectures.consent_records set text_version = 'retired-9' where session_id = '{id}'");
        var res = await PostAsync(a.Client, $"/sessions/{id}/resume");
        await res.AssertProblemAsync(HttpStatusCode.Forbidden, "consent_required");
        Assert.Equal("Paused", await F.TextAsync($"select state from lectures.lecture_sessions where id = '{id}'"));
        // Stop is still allowed so the student can always end a session.
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(a.Client, $"/sessions/{id}/stop")).StatusCode);
    }

    [Fact]
    public async Task A_stopped_session_must_have_stopped_at()
    {
        var (_, _, id) = await LiveAsync();
        await AssertFailsAsync($"update lectures.lecture_sessions set state = 'Stopped', stopped_at = null where id = '{id}'");
        await F.ExecAsync($"update lectures.lecture_sessions set state = 'Stopped', stopped_at = now() where id = '{id}'");
    }

    [Theory]
    [InlineData("bad\u0000title")]
    [InlineData("bad\u0007title")]
    [InlineData("two\nlines")]
    [InlineData("tab\there")]
    [InlineData("bidi‮evil")]
    public async Task Titles_with_control_or_bidi_override_characters_are_400(string title)
    {
        var a = await NewActorAsync();
        var course = await CourseAsync(a.Client);
        var res = await StartAsync(a.Client, course, null, title);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.True((await res.BodyAsync()).GetProperty("errors").TryGetProperty("title", out _));
        Assert.Equal(0, await CountAsync("lecture_sessions", $"user_id = '{a.UserId}'"));
    }

    [Fact]
    public async Task Ordinary_unicode_titles_are_still_accepted()
    {
        var a = await NewActorAsync();
        var course = await CourseAsync(a.Client);
        Assert.Equal(HttpStatusCode.Created, (await StartAsync(a.Client, course, null, "Cálculo II – 中文 \U0001F9EA")).StatusCode);
    }
}
