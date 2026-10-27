using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Tether.IntegrationTests.Infrastructure;
using Tether.Lectures.Application;
using Tether.Lectures.Persistence;
using Tether.SharedKernel;
using Tether.SharedKernel.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace Tether.IntegrationTests;

public class LectureAudioTicketTests(ApiFactory factory) : LectureTestBase(factory), IClassFixture<ApiFactory>
{
    private sealed class FakeUser(Guid? id, Guid? sid) : ICurrentUser
    {
        public Guid? UserId { get; } = id;
        public Guid? AuthSessionId { get; } = sid;
    }

    private async Task<Guid> AuthSidAsync(Guid userId) =>
        Guid.Parse((await F.TextAsync($"select id from identity.sessions where user_id = '{userId}' and revoked_at is null order by created_at desc limit 1"))!);

    /// <summary>The consumer as the future audio socket will call it: scoped to a user and login.</summary>
    private AudioTicketService Consumer(Guid? userId, Guid? sid)
    {
        var fake = new FakeUser(userId, sid);
        var options = new DbContextOptionsBuilder<LecturesDbContext>()
            .UseNpgsql(F.ConnectionString, n => n.MigrationsHistoryTable("__ef_migrations_history", LecturesDbContext.Schema))
            .AddInterceptors(new OwnershipSaveChangesInterceptor(fake)).Options;
        return new AudioTicketService(new LecturesDbContext(options, fake), fake, F.Clock);
    }

    private static async Task<string> IssueAsync(HttpClient c, string sessionId)
    {
        var res = await PostAsync(c, $"/sessions/{sessionId}/audio-ticket");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return (await res.BodyAsync()).GetProperty("ticket").GetString()!;
    }

    private async Task<(Actor A, string SessionId)> LiveSessionAsync()
    {
        var a = await NewActorAsync();
        var s = await StartOkAsync(a.Client, await CourseAsync(a.Client));
        return (a, s.GetProperty("id").GetString()!);
    }

    [Fact]
    public async Task Issue_returns_a_256_bit_url_safe_ticket_valid_for_30_seconds_matching_the_contract()
    {
        var (a, id) = await LiveSessionAsync();
        var before = F.Clock.GetUtcNow();
        var res = await PostAsync(a.Client, $"/sessions/{id}/audio-ticket");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var b = await res.BodyAsync();
        ContractSchemas.AssertValid("audio-ticket-response", b);
        var ticket = b.GetProperty("ticket").GetString()!;
        Assert.Equal(43, ticket.Length); // 32 bytes base64url, unpadded
        Assert.Equal(32, WebEncoders(ticket).Length);
        var expires = b.GetProperty("expiresAt").GetDateTimeOffset();
        Assert.InRange((expires - before).TotalSeconds, 29.99, 30.01);
        Assert.Contains("no-store", res.Headers.CacheControl!.ToString());
        Assert.False(res.Headers.Contains("Access-Control-Allow-Origin"), "no CORS allow headers on the ticket endpoint (C-01)");
    }

    private static byte[] WebEncoders(string s) => Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlDecode(s);

    [Fact]
    public async Task Tickets_are_unique_and_only_the_sha256_hash_is_stored()
    {
        var (a, id) = await LiveSessionAsync();
        var t1 = await IssueAsync(a.Client, id);
        var stored = await F.TextAsync($"select encode(token_hash, 'hex') from lectures.audio_tickets where session_id = '{id}'");
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes(t1))), stored);
        var t2 = await IssueAsync(a.Client, id);
        Assert.NotEqual(t1, t2);
        Assert.Equal(0, await F.ScalarAsync($"select count(*) from lectures.audio_tickets where encode(token_hash,'escape') like '%{t1}%' or encode(token_hash,'escape') like '%{t2}%'"));
    }

    [Fact]
    public async Task Only_one_ticket_is_outstanding_per_session_the_newest_wins()
    {
        var (a, id) = await LiveSessionAsync();
        var sid = await AuthSidAsync(a.UserId);
        var t1 = await IssueAsync(a.Client, id);
        var t2 = await IssueAsync(a.Client, id);
        Assert.Equal(1, await CountAsync("audio_tickets", $"session_id = '{id}'"));
        Assert.False(await Consumer(a.UserId, sid).ConsumeAsync(t1, Guid.Parse(id)));
        Assert.True(await Consumer(a.UserId, sid).ConsumeAsync(t2, Guid.Parse(id)));
    }

    [Fact]
    public async Task A_ticket_is_single_use()
    {
        var (a, id) = await LiveSessionAsync();
        var sid = await AuthSidAsync(a.UserId);
        var t = await IssueAsync(a.Client, id);
        Assert.True(await Consumer(a.UserId, sid).ConsumeAsync(t, Guid.Parse(id)));
        Assert.False(await Consumer(a.UserId, sid).ConsumeAsync(t, Guid.Parse(id)));
        Assert.Equal(0, await CountAsync("audio_tickets", $"session_id = '{id}'"));
    }

    [Fact]
    public async Task Concurrent_redemptions_of_one_ticket_succeed_exactly_once()
    {
        var (a, id) = await LiveSessionAsync();
        var sid = await AuthSidAsync(a.UserId);
        var t = await IssueAsync(a.Client, id);
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Consumer(a.UserId, sid).ConsumeAsync(t, Guid.Parse(id))));
        Assert.Equal(1, results.Count(r => r));
    }

    [Fact]
    public async Task A_ticket_expires_after_30_seconds()
    {
        var (a, id) = await LiveSessionAsync();
        var sid = await AuthSidAsync(a.UserId);
        var t = await IssueAsync(a.Client, id);
        F.Clock.Advance(TimeSpan.FromSeconds(31));
        Assert.False(await Consumer(a.UserId, sid).ConsumeAsync(t, Guid.Parse(id)));
    }

    [Fact]
    public async Task A_ticket_is_still_valid_just_before_expiry()
    {
        var (a, id) = await LiveSessionAsync();
        var sid = await AuthSidAsync(a.UserId);
        var t = await IssueAsync(a.Client, id);
        F.Clock.Advance(TimeSpan.FromSeconds(29));
        Assert.True(await Consumer(a.UserId, sid).ConsumeAsync(t, Guid.Parse(id)));
    }

    [Fact]
    public async Task Another_user_cannot_redeem_a_ticket_and_the_failed_attempt_is_audited_without_burning_nothing_else()
    {
        var (a, id) = await LiveSessionAsync();
        var bob = await NewActorAsync();
        var t = await IssueAsync(a.Client, id);
        Assert.False(await Consumer(bob.UserId, await AuthSidAsync(bob.UserId)).ConsumeAsync(t, Guid.Parse(id)));
        Assert.Equal(1, await CountAsync("audit_events", $"user_id = '{bob.UserId}' and action = 'audio_ticket.rejected'"));
        // Alice's ticket is untouched by bob's failed attempt (his filter cannot even see it) and still works for her.
        Assert.True(await Consumer(a.UserId, await AuthSidAsync(a.UserId)).ConsumeAsync(t, Guid.Parse(id)));
    }

    [Fact]
    public async Task A_ticket_is_bound_to_the_login_session_so_a_different_login_or_logout_invalidates_it()
    {
        var (a, id) = await LiveSessionAsync();
        var oldSid = await AuthSidAsync(a.UserId);
        var t = await IssueAsync(a.Client, id);
        _ = await F.LoginAsync(a.Email); // a second login: new identity.sessions row for the same user
        var newSid = Guid.Parse((await F.TextAsync(
            $"select id from identity.sessions where user_id = '{a.UserId}' and id <> '{oldSid}' and revoked_at is null"))!);
        Assert.NotEqual(oldSid, newSid);
        Assert.False(await Consumer(a.UserId, newSid).ConsumeAsync(t, Guid.Parse(id)));
    }

    [Fact]
    public async Task Anonymous_callers_and_wrong_session_ids_cannot_redeem()
    {
        var (a, id) = await LiveSessionAsync();
        var sid = await AuthSidAsync(a.UserId);
        var t = await IssueAsync(a.Client, id);
        Assert.False(await Consumer(null, null).ConsumeAsync(t, Guid.Parse(id)));
        Assert.False(await Consumer(a.UserId, null).ConsumeAsync(t, Guid.Parse(id)));
        Assert.False(await Consumer(a.UserId, sid).ConsumeAsync(t, Guid.NewGuid()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("not url safe !!!!!!!!!!!!!!!!!!!!!!!!!!!!")]
    public async Task Garbage_tickets_fail_identically_and_audit(string ticket)
    {
        var (a, id) = await LiveSessionAsync();
        var sid = await AuthSidAsync(a.UserId);
        Assert.False(await Consumer(a.UserId, sid).ConsumeAsync(ticket, Guid.Parse(id)));
        Assert.True(await CountAsync("audit_events", $"user_id = '{a.UserId}' and action = 'audio_ticket.rejected'") >= 1);
    }

    [Fact]
    public async Task Pause_invalidates_the_outstanding_ticket_and_resume_does_not_revive_it()
    {
        var (a, id) = await LiveSessionAsync();
        var sid = await AuthSidAsync(a.UserId);
        var t = await IssueAsync(a.Client, id);
        await PostAsync(a.Client, $"/sessions/{id}/pause");
        Assert.Equal(0, await CountAsync("audio_tickets", $"session_id = '{id}'"));
        await PostAsync(a.Client, $"/sessions/{id}/resume");
        Assert.False(await Consumer(a.UserId, sid).ConsumeAsync(t, Guid.Parse(id)));
    }

    [Fact]
    public async Task Stop_and_delete_invalidate_the_outstanding_ticket()
    {
        var (a, id) = await LiveSessionAsync();
        await IssueAsync(a.Client, id);
        await PostAsync(a.Client, $"/sessions/{id}/stop");
        Assert.Equal(0, await CountAsync("audio_tickets", $"session_id = '{id}'"));

        var (b, id2) = await LiveSessionAsync();
        var sid = await AuthSidAsync(b.UserId);
        var t = await IssueAsync(b.Client, id2);
        await b.Client.DeleteAsync($"/api/v1/sessions/{id2}");
        Assert.False(await Consumer(b.UserId, sid).ConsumeAsync(t, Guid.Parse(id2)));
    }

    [Fact]
    public async Task Redemption_requires_the_session_to_still_be_Active_even_if_a_ticket_row_survives()
    {
        var (a, id) = await LiveSessionAsync();
        var sid = await AuthSidAsync(a.UserId);
        var t = await IssueAsync(a.Client, id);
        await F.ExecAsync($"update lectures.lecture_sessions set state = 'Paused' where id = '{id}'"); // bypasses the API on purpose
        Assert.False(await Consumer(a.UserId, sid).ConsumeAsync(t, Guid.Parse(id)));
    }

    [Fact]
    public async Task Issue_is_409_unless_Active_404_for_missing_and_403_when_the_recorded_consent_version_is_no_longer_accepted()
    {
        var (a, id) = await LiveSessionAsync();
        await PostAsync(a.Client, $"/sessions/{id}/pause");
        var res = await PostAsync(a.Client, $"/sessions/{id}/audio-ticket");
        await res.AssertProblemAsync(HttpStatusCode.Conflict, "invalid_state");
        await PostAsync(a.Client, $"/sessions/{id}/resume");

        await (await PostAsync(a.Client, $"/sessions/{Guid.NewGuid()}/audio-ticket")).AssertProblemAsync(HttpStatusCode.NotFound, "not_found");

        await F.ExecAsync($"update lectures.consent_records set text_version = 'retired-9' where session_id = '{id}'");
        await (await PostAsync(a.Client, $"/sessions/{id}/audio-ticket")).AssertProblemAsync(HttpStatusCode.Forbidden, "consent_required");
    }

    [Fact]
    public async Task Ticket_issuance_is_limited_to_6_per_minute_per_user_with_retry_after()
    {
        var (a, id) = await LiveSessionAsync();
        for (var i = 0; i < 6; i++)
            Assert.Equal(HttpStatusCode.OK, (await PostAsync(a.Client, $"/sessions/{id}/audio-ticket")).StatusCode);
        var limited = await PostAsync(a.Client, $"/sessions/{id}/audio-ticket");
        await limited.AssertProblemAsync(HttpStatusCode.TooManyRequests, "rate_limited");
        Assert.True(limited.Headers.Contains("Retry-After"));
        // Another user is unaffected.
        var (b, id2) = await LiveSessionAsync();
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(b.Client, $"/sessions/{id2}/audio-ticket")).StatusCode);
    }

    [Fact]
    public async Task Issuance_is_audited_without_the_ticket_value()
    {
        var (a, id) = await LiveSessionAsync();
        var t = await IssueAsync(a.Client, id);
        Assert.Equal(1, await CountAsync("audit_events", $"session_id = '{id}' and action = 'audio_ticket.issued'"));
        Assert.Equal(0, await F.ScalarAsync($"select count(*) from lectures.audit_events where action like '%{t}%'"));
    }
}

/// <summary>C-09: no cross-user reads or writes, with overlapping per-session seq values and identical ids probed.</summary>
public class LectureIsolationTests(ApiFactory factory) : LectureTestBase(factory), IClassFixture<ApiFactory>
{
    private sealed class FakeUser(Guid? id) : ICurrentUser { public Guid? UserId { get; } = id; }

    private LecturesDbContext Ctx(Guid? user)
    {
        var fake = new FakeUser(user);
        var options = new DbContextOptionsBuilder<LecturesDbContext>()
            .UseNpgsql(F.ConnectionString, n => n.MigrationsHistoryTable("__ef_migrations_history", LecturesDbContext.Schema))
            .AddInterceptors(new OwnershipSaveChangesInterceptor(fake)).Options;
        return new LecturesDbContext(options, fake);
    }

    private async Task<(Actor Alice, Actor Bob, string AliceCourse, string BobCourse, string AliceSession, string BobSession)> TwoUsersAsync()
    {
        var alice = await NewActorAsync(); var bob = await NewActorAsync();
        var ac = await CourseAsync(alice.Client); var bc = await CourseAsync(bob.Client);
        var a = (await StartOkAsync(alice.Client, ac)).GetProperty("id").GetString()!;
        var b = (await StartOkAsync(bob.Client, bc)).GetProperty("id").GetString()!;
        // Overlapping seq values: both sessions now have seq 1; give both a seq 2 as well.
        await PostAsync(alice.Client, $"/sessions/{a}/pause");
        await PostAsync(bob.Client, $"/sessions/{b}/pause");
        return (alice, bob, ac, bc, a, b);
    }

    [Fact]
    public async Task Foreign_session_ids_are_404_on_every_endpoint_and_look_identical_to_missing_ones()
    {
        var (_, bob, _, _, aliceSession, _) = await TwoUsersAsync();
        var missing = Guid.NewGuid().ToString();
        var calls = new (HttpMethod M, string Path)[]
        {
            (HttpMethod.Get, "/sessions/{0}"),
            (HttpMethod.Post, "/sessions/{0}/pause"),
            (HttpMethod.Post, "/sessions/{0}/resume"),
            (HttpMethod.Post, "/sessions/{0}/stop"),
            (HttpMethod.Delete, "/sessions/{0}"),
            (HttpMethod.Post, "/sessions/{0}/audio-ticket"),
            (HttpMethod.Get, "/sessions/{0}/transcript"),
            (HttpMethod.Get, "/sessions/{0}/summary"),
            (HttpMethod.Get, "/sessions/{0}/catch-ups/abc"),
            (HttpMethod.Get, "/sessions/{0}/notes"),
        };
        foreach (var (m, path) in calls)
        {
            var foreign = await bob.Client.SendAsync(new HttpRequestMessage(m, "/api/v1" + string.Format(path, aliceSession)));
            var absent = await bob.Client.SendAsync(new HttpRequestMessage(m, "/api/v1" + string.Format(path, missing)));
            await foreign.AssertProblemAsync(HttpStatusCode.NotFound, "not_found");
            await absent.AssertProblemAsync(HttpStatusCode.NotFound, "not_found");
            var fb = await foreign.BodyAsync(); var ab = await absent.BodyAsync();
            Assert.Equal(ab.GetProperty("title").GetString(), fb.GetProperty("title").GetString()); // no existence oracle
            Assert.Equal(absent.StatusCode, foreign.StatusCode);
        }
    }

    [Fact]
    public async Task Foreign_attempts_change_nothing_for_the_owner()
    {
        var (alice, bob, aliceCourse, _, aliceSession, _) = await TwoUsersAsync();
        var kinds = await F.TextAsync($"select count(*)::text from lectures.consent_events where session_id = '{aliceSession}'");
        await PostAsync(bob.Client, $"/sessions/{aliceSession}/resume");
        await PostAsync(bob.Client, $"/sessions/{aliceSession}/stop");
        await bob.Client.DeleteAsync($"/api/v1/sessions/{aliceSession}");
        var a = await (await alice.Client.GetAsync($"/api/v1/sessions/{aliceSession}")).BodyAsync();
        Assert.Equal("Paused", a.GetProperty("state").GetString());
        Assert.Equal("2", a.GetProperty("lastSeq").GetString());
        Assert.Equal(kinds, await F.TextAsync($"select count(*)::text from lectures.consent_events where session_id = '{aliceSession}'"));
        Assert.Equal(0, await CountAsync("audit_events", $"user_id = '{bob.UserId}' and session_id = '{aliceSession}'"));
        // Bob cannot list or start under Alice's course either.
        await (await bob.Client.GetAsync($"/api/v1/courses/{aliceCourse}/sessions")).AssertProblemAsync(HttpStatusCode.NotFound, "not_found");
        await (await StartAsync(bob.Client, aliceCourse)).AssertProblemAsync(HttpStatusCode.NotFound, "not_found");
    }

    [Fact]
    public async Task Lists_never_include_another_users_sessions()
    {
        var (alice, bob, aliceCourse, bobCourse, aliceSession, bobSession) = await TwoUsersAsync();
        var al = await (await alice.Client.GetAsync($"/api/v1/courses/{aliceCourse}/sessions")).BodyAsync();
        var bl = await (await bob.Client.GetAsync($"/api/v1/courses/{bobCourse}/sessions")).BodyAsync();
        Assert.Equal([aliceSession], al.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetString()));
        Assert.Equal([bobSession], bl.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetString()));
    }

    [Fact]
    public async Task Overlapping_seq_values_are_scoped_by_user_and_session_in_the_data_layer()
    {
        var (alice, bob, _, _, aliceSession, bobSession) = await TwoUsersAsync();
        // Both users own events with seq 1 and 2 (overlap by design: seq is only meaningful within a session).
        Assert.Equal(2, await F.ScalarAsync("select count(*) from lectures.session_events where seq = 1 and user_id in " +
            $"('{alice.UserId}','{bob.UserId}')"));

        await using (var ctx = Ctx(alice.UserId))
        {
            var ev = await ctx.SessionEvents.AsNoTracking().ToListAsync();
            Assert.All(ev, e => Assert.Equal(Guid.Parse(aliceSession), e.SessionId));
            Assert.Equal([1L, 2L], ev.Select(e => e.Seq).Order().ToArray());
            // Alice asks for Bob's session by id, with a seq that exists in both: nothing comes back.
            Assert.Empty(await ctx.SessionEvents.Where(e => e.SessionId == Guid.Parse(bobSession) && e.Seq == 1).ToListAsync());
            Assert.Null(await ctx.Counters.FirstOrDefaultAsync(c => c.SessionId == Guid.Parse(bobSession)));
        }
        await using (var ctx = Ctx(null))
        {
            Assert.Empty(await ctx.Sessions.ToListAsync());
            Assert.Empty(await ctx.SessionEvents.ToListAsync());
            Assert.Empty(await ctx.Consents.ToListAsync());
            Assert.Empty(await ctx.ConsentEvents.ToListAsync());
            Assert.Empty(await ctx.AudioTickets.ToListAsync());
            Assert.Empty(await ctx.IdempotencyKeys.ToListAsync());
            Assert.Empty(await ctx.AuditEvents.ToListAsync());
        }
    }

    [Fact]
    public void Every_user_owned_lectures_entity_has_a_global_query_filter()
    {
        _ = F.CreateClient();
        using var ctx = Ctx(Guid.NewGuid());
        var owned = ctx.Model.GetEntityTypes().Where(t => typeof(IUserOwned).IsAssignableFrom(t.ClrType)).ToList();
        Assert.Equal(ctx.Model.GetEntityTypes().Count(), owned.Count); // every entity in the context is user owned
        Assert.True(owned.Count >= 8);
        foreach (var t in owned) Assert.NotEmpty(t.GetDeclaredQueryFilters());
    }

    [Fact]
    public async Task The_ownership_interceptor_rejects_writing_lectures_rows_for_another_user()
    {
        var (alice, bob, _, _, aliceSession, _) = await TwoUsersAsync();
        await using var ctx = Ctx(bob.UserId);
        ctx.AuditEvents.Add(new Tether.Lectures.Domain.AuditEvent { Id = Ids.New(), UserId = alice.UserId, Action = "x", OccurredAt = DateTimeOffset.UtcNow });
        await Assert.ThrowsAsync<OwnershipViolationException>(() => ctx.SaveChangesAsync());
    }

    [Fact]
    public async Task Consent_metadata_is_per_user_and_deleting_one_users_session_does_not_touch_the_other()
    {
        var (alice, bob, _, _, aliceSession, bobSession) = await TwoUsersAsync();
        await alice.Client.DeleteAsync($"/api/v1/sessions/{aliceSession}");
        Assert.Equal(1, await CountAsync("lecture_sessions", $"id = '{bobSession}'"));
        Assert.Equal(1, await CountAsync("consent_records", $"session_id = '{bobSession}' and user_id = '{bob.UserId}'"));
        Assert.Equal(1, await CountAsync("consent_records", $"session_id = '{aliceSession}' and user_id = '{alice.UserId}'")); // kept, content free
        Assert.Equal(HttpStatusCode.OK, (await bob.Client.GetAsync($"/api/v1/sessions/{bobSession}")).StatusCode);
    }

    [Fact]
    public async Task Anonymous_callers_get_401_on_every_lecture_route()
    {
        var anon = F.NewClient();
        var id = Guid.NewGuid();
        var calls = new (HttpMethod M, string Path)[]
        {
            (HttpMethod.Post, $"/courses/{id}/sessions"), (HttpMethod.Get, $"/courses/{id}/sessions"),
            (HttpMethod.Get, $"/sessions/{id}"), (HttpMethod.Post, $"/sessions/{id}/pause"),
            (HttpMethod.Post, $"/sessions/{id}/resume"), (HttpMethod.Post, $"/sessions/{id}/stop"),
            (HttpMethod.Delete, $"/sessions/{id}"), (HttpMethod.Post, $"/sessions/{id}/audio-ticket"),
            (HttpMethod.Get, $"/sessions/{id}/transcript"), (HttpMethod.Get, $"/sessions/{id}/summary"),
            (HttpMethod.Get, $"/sessions/{id}/catch-ups/x"), (HttpMethod.Get, $"/sessions/{id}/notes"),
        };
        foreach (var (m, path) in calls)
            await (await anon.SendAsync(new HttpRequestMessage(m, "/api/v1" + path))).AssertProblemAsync(HttpStatusCode.Unauthorized, "unauthenticated");
    }
}

/// <summary>C-23 for the Lectures module: sentinel content, tickets and cookies never reach any log.</summary>
public class LectureLogCanaryTests(LectureLogCanaryTests.Factory f) : LectureTestBase(f), IClassFixture<LectureLogCanaryTests.Factory>
{
    public sealed class Factory : ApiFactory
    {
        public LogCanaryTests.CapturingProvider Logs { get; } = new();
        protected override IEnumerable<KeyValuePair<string, string?>> Settings =>
            [new("ConnectionStrings:Default", ConnectionString + ";Include Error Detail=true")];
        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(s => s.AddSingleton<ILoggerProvider>(Logs));
        }
    }

    [Fact]
    public async Task Sentinel_title_failing_row_ticket_and_cookie_never_reach_the_logs_or_responses()
    {
        const string sentinel = "CANARY-5d2e8b4a-lecture-session";
        var a = await NewActorAsync();
        var courseId = await CourseAsync(a.Client);
        var cookie = a.Client.DefaultRequestHeaders.GetValues("Cookie").Single().Split('=', 2)[1];

        // Healthy path first: a ticket is issued and a session exists.
        var ok = await StartOkAsync(a.Client, courseId);
        var ticket = (await (await PostAsync(a.Client, $"/sessions/{ok.GetProperty("id").GetString()}/audio-ticket")).BodyAsync())
            .GetProperty("ticket").GetString()!;
        await PostAsync(a.Client, $"/sessions/{ok.GetProperty("id").GetString()}/stop");

        // Failing path: the INSERT fails with a PostgresException whose Detail echoes the row (Include Error Detail=true).
        await F.ExecAsync($"alter table lectures.lecture_sessions add constraint ck_canary check (coalesce(title,'') not like '%{sentinel}%')");
        var res = await StartAsync(a.Client, courseId, title: "Lecture " + sentinel);
        await res.AssertProblemAsync(HttpStatusCode.InternalServerError, "internal_error");
        Assert.DoesNotContain(sentinel, await res.Content.ReadAsStringAsync());

        var lines = f.Logs.Lines.ToArray();
        Assert.Contains(lines, l => l.Contains("Unhandled", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, l => l.Contains(sentinel, StringComparison.Ordinal));
        Assert.DoesNotContain(lines, l => l.Contains(ticket, StringComparison.Ordinal));
        Assert.DoesNotContain(lines, l => l.Contains(cookie, StringComparison.Ordinal));
    }
}
