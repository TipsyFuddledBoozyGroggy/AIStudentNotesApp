using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Tether.IntegrationTests.Infrastructure;
using Tether.Lectures.Application;
using Tether.SharedKernel.Http;
using Npgsql;

namespace Tether.IntegrationTests;

/// <summary>Shared helpers for the Lectures REST tests.</summary>
public abstract class LectureTestBase(ApiFactory factory)
{
    protected ApiFactory F { get; } = factory;

    protected sealed record Actor(HttpClient Client, Guid UserId, string Email);

    protected async Task<Actor> NewActorAsync()
    {
        var email = $"u{Guid.NewGuid():N}@dev.local";
        var c = await F.LoginAsync(email);
        var me = await (await c.GetAsync("/api/v1/me")).BodyAsync();
        return new Actor(c, Guid.Parse(me.GetProperty("id").GetString()!), email);
    }

    protected static async Task<string> CourseAsync(HttpClient c, string name = "Biology 101")
    {
        var res = await c.PostAsJsonAsync("/api/v1/courses", new { name });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await res.BodyAsync()).GetProperty("id").GetString()!;
    }

    protected static object Consent(string version = ConsentTextRegistry.DraftV1) => new { acknowledged = true, textVersion = version };

    protected static Task<HttpResponseMessage> StartAsync(HttpClient c, string courseId, string? key = null, string? title = "Cell division", object? consent = null)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/courses/{courseId}/sessions")
        {
            Content = JsonContent.Create(new { title, consent = consent ?? Consent() }),
        };
        if (key is not null) req.Headers.TryAddWithoutValidation("Idempotency-Key", key);
        return c.SendAsync(req);
    }

    protected static async Task<JsonElement> StartOkAsync(HttpClient c, string courseId, string? key = null)
    {
        var res = await StartAsync(c, courseId, key);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return await res.BodyAsync();
    }

    protected static Task<HttpResponseMessage> PostAsync(HttpClient c, string path) => c.PostAsync($"/api/v1{path}", null);

    protected async Task<long> CountAsync(string table, string where) => await F.ScalarAsync($"select count(*) from lectures.{table} where {where}");
}

public class LectureSessionTests(ApiFactory factory) : LectureTestBase(factory), IClassFixture<ApiFactory>
{
    // ---------------- create ----------------

    [Fact]
    public async Task Create_returns_201_with_location_etag_contract_shape_and_writes_consent_audit_and_seq_rows()
    {
        var a = await NewActorAsync();
        var courseId = await CourseAsync(a.Client);
        var res = await StartAsync(a.Client, courseId);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var b = await res.BodyAsync();
        ContractSchemas.AssertValid("session", b);

        var id = b.GetProperty("id").GetString()!;
        Assert.Equal($"/api/v1/sessions/{id}", res.Headers.Location!.ToString());
        Assert.Equal(b.GetProperty("etag").GetString(), res.Headers.ETag!.ToString());
        Assert.Equal("Active", b.GetProperty("state").GetString());
        Assert.Equal(courseId, b.GetProperty("courseId").GetString());
        Assert.Equal("Cell division", b.GetProperty("title").GetString());
        Assert.Equal("1", b.GetProperty("lastSeq").GetString());
        Assert.Equal(ConsentTextRegistry.DraftV1, b.GetProperty("consent").GetProperty("textVersion").GetString());
        Assert.False(b.TryGetProperty("stoppedAt", out _));
        Assert.False(b.TryGetProperty("userId", out _), "owner id must not leak");

        Assert.Equal(1, await CountAsync("consent_records", $"session_id = '{id}' and course_attested and user_id = '{a.UserId}' and course_id = '{courseId}'"));
        Assert.Equal(1, await CountAsync("consent_events", $"session_id = '{id}' and kind = 'Start'"));
        Assert.Equal(1, await CountAsync("audit_events", $"session_id = '{id}' and action = 'session.start' and user_id = '{a.UserId}'"));
        Assert.Equal(1, await CountAsync("session_events", $"session_id = '{id}' and seq = 1 and state = 'Active'"));
        Assert.Equal(1, await F.ScalarAsync($"select last_seq from lectures.session_counters where session_id = '{id}'"));
    }

    [Fact]
    public async Task Create_title_is_optional_and_serialized_as_null()
    {
        var a = await NewActorAsync();
        var courseId = await CourseAsync(a.Client);
        var res = await StartAsync(a.Client, courseId, title: "   ");
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var b = await res.BodyAsync();
        ContractSchemas.AssertValid("session", b);
        Assert.Equal(JsonValueKind.Null, b.GetProperty("title").ValueKind);
    }

    public static IEnumerable<object?[]> BadConsents() =>
    [
        [null],                                                                    // no consent object
        [new { acknowledged = false, textVersion = "draft-1" }],                   // not acknowledged
        [new { textVersion = "draft-1" }],                                         // acknowledged missing
        [new { acknowledged = true }],                                             // no version
        [new { acknowledged = true, textVersion = "draft-999" }],                  // unregistered version (C-13)
        [new { acknowledged = true, textVersion = "" }],
    ];

    [Theory, MemberData(nameof(BadConsents))]
    public async Task Create_without_valid_consent_is_403_consent_required_with_fixed_detail_and_writes_nothing(object? consent)
    {
        var a = await NewActorAsync();
        var courseId = await CourseAsync(a.Client);
        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/courses/{courseId}/sessions")
        {
            Content = JsonContent.Create(new { title = "x", consent }),
        };
        var res = await a.Client.SendAsync(req);
        await res.AssertProblemAsync(HttpStatusCode.Forbidden, "consent_required");
        Assert.Equal(Problems.ConsentRequiredText, (await res.BodyAsync()).GetProperty("detail").GetString());
        Assert.Equal(0, await CountAsync("lecture_sessions", $"user_id = '{a.UserId}'"));
        Assert.Equal(0, await CountAsync("consent_records", $"user_id = '{a.UserId}'"));
    }

    [Fact]
    public async Task Create_with_no_body_is_403_consent_required()
    {
        var a = await NewActorAsync();
        var courseId = await CourseAsync(a.Client);
        var res = await a.Client.PostAsync($"/api/v1/courses/{courseId}/sessions", null);
        await res.AssertProblemAsync(HttpStatusCode.Forbidden, "consent_required");
    }

    [Fact]
    public async Task Create_for_missing_or_foreign_course_is_404_even_without_consent()
    {
        var alice = await NewActorAsync(); var bob = await NewActorAsync();
        var aliceCourse = await CourseAsync(alice.Client);
        await (await StartAsync(bob.Client, aliceCourse)).AssertProblemAsync(HttpStatusCode.NotFound, "not_found");
        await (await StartAsync(bob.Client, Guid.NewGuid().ToString())).AssertProblemAsync(HttpStatusCode.NotFound, "not_found");
        var noConsent = await bob.Client.PostAsJsonAsync($"/api/v1/courses/{aliceCourse}/sessions", new { title = "x" });
        await noConsent.AssertProblemAsync(HttpStatusCode.NotFound, "not_found");
        Assert.Equal(0, await CountAsync("consent_records", $"user_id = '{bob.UserId}'"));
    }

    [Fact]
    public async Task Create_rejects_overlong_title_and_overlong_version_with_400()
    {
        var a = await NewActorAsync();
        var courseId = await CourseAsync(a.Client);
        var res = await StartAsync(a.Client, courseId, title: new string('t', 201));
        await res.AssertProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        Assert.True((await res.BodyAsync()).GetProperty("errors").TryGetProperty("title", out _));
        var res2 = await StartAsync(a.Client, courseId, consent: new { acknowledged = true, textVersion = new string('v', 65) });
        await res2.AssertProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
    }

    [Fact]
    public async Task Create_while_another_session_is_active_or_paused_is_409_invalid_state_with_fixed_detail()
    {
        var a = await NewActorAsync();
        var c1 = await CourseAsync(a.Client); var c2 = await CourseAsync(a.Client, "Chem");
        var s = await StartOkAsync(a.Client, c1);
        var id = s.GetProperty("id").GetString()!;

        var res = await StartAsync(a.Client, c2);
        await res.AssertProblemAsync(HttpStatusCode.Conflict, "invalid_state");
        Assert.Equal(Problems.InvalidStateText, (await res.BodyAsync()).GetProperty("detail").GetString());

        await PostAsync(a.Client, $"/sessions/{id}/pause");
        await (await StartAsync(a.Client, c2)).AssertProblemAsync(HttpStatusCode.Conflict, "invalid_state");

        await PostAsync(a.Client, $"/sessions/{id}/stop");
        Assert.Equal(HttpStatusCode.Created, (await StartAsync(a.Client, c2)).StatusCode);
    }

    [Fact]
    public async Task Concurrent_creates_for_one_user_yield_exactly_one_session()
    {
        var a = await NewActorAsync();
        var c1 = await CourseAsync(a.Client);
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => StartAsync(a.Client, c1)));
        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(5, results.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        Assert.Equal(1, await CountAsync("lecture_sessions", $"user_id = '{a.UserId}'"));
        Assert.Equal(1, await CountAsync("consent_events", $"user_id = '{a.UserId}'"));
    }

    [Fact]
    public async Task Database_enforces_one_live_session_per_user_even_outside_the_api()
    {
        var a = await NewActorAsync();
        var s = await StartOkAsync(a.Client, await CourseAsync(a.Client));
        var ex = await Assert.ThrowsAsync<PostgresException>(() => F.ExecAsync($"""
            insert into lectures.lecture_sessions (id, user_id, course_id, state, started_at, consent_record_id)
            select gen_random_uuid(), user_id, course_id, 'Paused', now(), consent_record_id
            from lectures.lecture_sessions where id = '{s.GetProperty("id").GetString()}'
            """));
        Assert.Equal("23505", ex.SqlState);
        Assert.Equal("ux_lecture_sessions_one_live_per_user", ex.ConstraintName);
    }

    // ---------------- idempotency (C-32) ----------------

    [Fact]
    public async Task Idempotency_key_replay_returns_the_original_session_without_a_second_consent_event()
    {
        var a = await NewActorAsync();
        var courseId = await CourseAsync(a.Client);
        var key = "key-" + Guid.NewGuid();
        var first = await StartAsync(a.Client, courseId, key);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var fb = await first.BodyAsync();

        var again = await StartAsync(a.Client, courseId, key);
        Assert.Equal(HttpStatusCode.Created, again.StatusCode);
        Assert.Equal("true", again.Headers.GetValues("Idempotency-Replayed").Single());
        var ab = await again.BodyAsync();
        ContractSchemas.AssertValid("session", ab);
        Assert.Equal(fb.GetProperty("id").GetString(), ab.GetProperty("id").GetString());
        Assert.Equal(fb.GetProperty("startedAt").GetString(), ab.GetProperty("startedAt").GetString());

        Assert.Equal(1, await CountAsync("lecture_sessions", $"user_id = '{a.UserId}'"));
        Assert.Equal(1, await CountAsync("consent_records", $"user_id = '{a.UserId}'"));
        Assert.Equal(1, await CountAsync("consent_events", $"user_id = '{a.UserId}'"));
        Assert.Equal(1, await CountAsync("audit_events", $"user_id = '{a.UserId}' and action = 'session.start'"));
        Assert.False(first.Headers.Contains("Idempotency-Replayed"));
    }

    [Fact]
    public async Task Idempotency_replay_still_works_after_the_session_moved_on_and_returns_its_current_state()
    {
        var a = await NewActorAsync();
        var courseId = await CourseAsync(a.Client);
        var key = "k-" + Guid.NewGuid();
        var s = await StartOkAsync(a.Client, courseId, key);
        await PostAsync(a.Client, $"/sessions/{s.GetProperty("id").GetString()}/pause");
        var again = await StartAsync(a.Client, courseId, key);
        Assert.Equal(HttpStatusCode.Created, again.StatusCode);
        Assert.Equal("Paused", (await again.BodyAsync()).GetProperty("state").GetString());
    }

    [Fact]
    public async Task Idempotency_key_reused_for_a_different_request_is_400_and_creates_nothing()
    {
        var a = await NewActorAsync();
        var c1 = await CourseAsync(a.Client); var c2 = await CourseAsync(a.Client, "Chem");
        var key = "k-" + Guid.NewGuid();
        await StartOkAsync(a.Client, c1, key);
        var res = await StartAsync(a.Client, c2, key);
        await res.AssertProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        Assert.Equal(1, await CountAsync("lecture_sessions", $"user_id = '{a.UserId}'"));
    }

    [Fact]
    public async Task Idempotency_keys_are_per_user_and_stored_hashed()
    {
        var alice = await NewActorAsync(); var bob = await NewActorAsync();
        var ac = await CourseAsync(alice.Client); var bc = await CourseAsync(bob.Client);
        var key = "shared-key-" + Guid.NewGuid();
        var a = await StartOkAsync(alice.Client, ac, key);
        var b = await StartOkAsync(bob.Client, bc, key); // same key text, different user: not a replay
        Assert.NotEqual(a.GetProperty("id").GetString(), b.GetProperty("id").GetString());
        Assert.Equal(0, await F.ScalarAsync($"select count(*) from lectures.idempotency_keys where encode(key_hash,'escape') like '%{key}%'"));
    }

    [Fact]
    public async Task Concurrent_requests_with_the_same_key_converge_on_one_session()
    {
        var a = await NewActorAsync();
        var courseId = await CourseAsync(a.Client);
        var key = "race-" + Guid.NewGuid();
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => StartAsync(a.Client, courseId, key)));
        Assert.All(results, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));
        var ids = (await Task.WhenAll(results.Select(r => r.BodyAsync()))).Select(b => b.GetProperty("id").GetString()).Distinct().ToList();
        Assert.Single(ids);
        Assert.Equal(1, await CountAsync("consent_events", $"user_id = '{a.UserId}'"));
    }

    [Fact]
    public async Task Idempotency_key_expires_after_24_hours()
    {
        var a = await NewActorAsync();
        var courseId = await CourseAsync(a.Client);
        var key = "exp-" + Guid.NewGuid();
        var first = await StartOkAsync(a.Client, courseId, key);
        await PostAsync(a.Client, $"/sessions/{first.GetProperty("id").GetString()}/stop");
        F.Clock.Advance(TimeSpan.FromHours(25));
        var fresh = await F.LoginAsync(a.Email); // the old cookie's sliding window has passed
        var again = await StartAsync(fresh, courseId, key);
        Assert.Equal(HttpStatusCode.Created, again.StatusCode);
        Assert.NotEqual(first.GetProperty("id").GetString(), (await again.BodyAsync()).GetProperty("id").GetString());
    }

    [Theory]
    [InlineData("has space")]
    public async Task Invalid_idempotency_key_header_is_400(string key)
    {
        var a = await NewActorAsync();
        var courseId = await CourseAsync(a.Client);
        var res = await StartAsync(a.Client, courseId, key);
        await res.AssertProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        Assert.True((await res.BodyAsync()).GetProperty("errors").TryGetProperty("Idempotency-Key", out _));
    }

    [Fact]
    public async Task Overlong_idempotency_key_is_400()
    {
        var a = await NewActorAsync();
        var courseId = await CourseAsync(a.Client);
        await (await StartAsync(a.Client, courseId, new string('k', 129))).AssertProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
    }

    // ---------------- get / list ----------------

    [Fact]
    public async Task Get_returns_the_session_with_matching_etag_header()
    {
        var a = await NewActorAsync();
        var s = await StartOkAsync(a.Client, await CourseAsync(a.Client));
        var res = await a.Client.GetAsync($"/api/v1/sessions/{s.GetProperty("id").GetString()}");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var b = await res.BodyAsync();
        ContractSchemas.AssertValid("session", b);
        Assert.Equal(b.GetProperty("etag").GetString(), res.Headers.ETag!.ToString());
        Assert.Equal(s.GetProperty("etag").GetString(), b.GetProperty("etag").GetString());
    }

    [Fact]
    public async Task Etag_changes_when_the_session_changes_and_never_contains_the_raw_xmin()
    {
        var a = await NewActorAsync();
        var s = await StartOkAsync(a.Client, await CourseAsync(a.Client));
        var id = s.GetProperty("id").GetString()!;
        var paused = await (await PostAsync(a.Client, $"/sessions/{id}/pause")).BodyAsync();
        Assert.NotEqual(s.GetProperty("etag").GetString(), paused.GetProperty("etag").GetString());
        var xmin = await F.TextAsync($"select xmin::text from lectures.lecture_sessions where id = '{id}'");
        Assert.DoesNotContain($"\"{xmin}\"", paused.GetProperty("etag").GetString());
    }

    [Fact]
    public async Task List_pages_newest_first_with_cursor_and_validates_limits()
    {
        var a = await NewActorAsync();
        var courseId = await CourseAsync(a.Client);
        var ids = new List<string>();
        for (var i = 0; i < 3; i++)
        {
            var s = await StartOkAsync(a.Client, courseId);
            var id = s.GetProperty("id").GetString()!;
            ids.Add(id);
            await PostAsync(a.Client, $"/sessions/{id}/stop");
        }
        var p1 = await (await a.Client.GetAsync($"/api/v1/courses/{courseId}/sessions?limit=2")).BodyAsync();
        var items1 = p1.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(2, items1.Count);
        items1.ForEach(i => ContractSchemas.AssertValid("session", i));
        Assert.Equal([ids[2], ids[1]], items1.Select(i => i.GetProperty("id").GetString()).ToList());
        var cursor = p1.GetProperty("nextCursor").GetString()!;

        var p2 = await (await a.Client.GetAsync($"/api/v1/courses/{courseId}/sessions?limit=2&cursor={cursor}")).BodyAsync();
        Assert.Equal([ids[0]], p2.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetString()).ToList());
        Assert.False(p2.TryGetProperty("nextCursor", out var nc) && nc.ValueKind != JsonValueKind.Null);
        Assert.Equal("Stopped", items1[0].GetProperty("state").GetString());
        Assert.True(items1[0].TryGetProperty("stoppedAt", out _));
    }

    [Theory]
    [InlineData("limit=0")]
    [InlineData("limit=101")]
    [InlineData("limit=-1")]
    [InlineData("cursor=not-a-cursor")]
    public async Task List_rejects_bad_limit_and_cursor_with_400(string query)
    {
        var a = await NewActorAsync();
        var courseId = await CourseAsync(a.Client);
        await (await a.Client.GetAsync($"/api/v1/courses/{courseId}/sessions?{query}")).AssertProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
    }

    [Fact]
    public async Task List_accepts_the_server_maximum()
    {
        var a = await NewActorAsync();
        var courseId = await CourseAsync(a.Client);
        Assert.Equal(HttpStatusCode.OK, (await a.Client.GetAsync($"/api/v1/courses/{courseId}/sessions?limit=100")).StatusCode);
    }

    [Fact]
    public async Task List_is_scoped_to_the_course()
    {
        var a = await NewActorAsync();
        var c1 = await CourseAsync(a.Client); var c2 = await CourseAsync(a.Client, "Chem");
        var s = await StartOkAsync(a.Client, c1);
        var empty = await (await a.Client.GetAsync($"/api/v1/courses/{c2}/sessions")).BodyAsync();
        Assert.Empty(empty.GetProperty("items").EnumerateArray());
        var one = await (await a.Client.GetAsync($"/api/v1/courses/{c1}/sessions")).BodyAsync();
        Assert.Equal(s.GetProperty("id").GetString(), Assert.Single(one.GetProperty("items").EnumerateArray()).GetProperty("id").GetString());
    }

    // ---------------- lifecycle ----------------

    [Fact]
    public async Task Pause_resume_stop_follow_the_state_machine_assign_gap_free_seq_and_write_a_consent_event_each()
    {
        var a = await NewActorAsync();
        var s = await StartOkAsync(a.Client, await CourseAsync(a.Client));
        var id = s.GetProperty("id").GetString()!;

        F.Clock.Advance(TimeSpan.FromSeconds(1));
        var paused = await PostAsync(a.Client, $"/sessions/{id}/pause");
        Assert.Equal(HttpStatusCode.OK, paused.StatusCode);
        var pb = await paused.BodyAsync();
        ContractSchemas.AssertValid("session", pb);
        Assert.Equal(("Paused", "2"), (pb.GetProperty("state").GetString(), pb.GetProperty("lastSeq").GetString()));

        F.Clock.Advance(TimeSpan.FromSeconds(1));
        var resumed = await (await PostAsync(a.Client, $"/sessions/{id}/resume")).BodyAsync();
        Assert.Equal(("Active", "3"), (resumed.GetProperty("state").GetString(), resumed.GetProperty("lastSeq").GetString()));

        F.Clock.Advance(TimeSpan.FromSeconds(1));
        var stopped = await (await PostAsync(a.Client, $"/sessions/{id}/stop")).BodyAsync();
        ContractSchemas.AssertValid("session", stopped);
        Assert.Equal(("Stopped", "4"), (stopped.GetProperty("state").GetString(), stopped.GetProperty("lastSeq").GetString()));
        Assert.True(stopped.TryGetProperty("stoppedAt", out var at) && at.ValueKind == JsonValueKind.String);

        Assert.Equal(["Start", "Pause", "Resume", "Stop"],
            await KindsAsync(id));
        Assert.Equal(4, await CountAsync("session_events", $"session_id = '{id}'"));
        Assert.Equal("1,2,3,4", await F.TextAsync($"select string_agg(seq::text, ',' order by seq) from lectures.session_events where session_id = '{id}'"));
        foreach (var action in new[] { "session.start", "session.pause", "session.resume", "session.stop" })
            Assert.Equal(1, await CountAsync("audit_events", $"session_id = '{id}' and action = '{action}'"));
    }

    private async Task<string[]> KindsAsync(string id) =>
        (await F.TextAsync($"select string_agg(kind, ',' order by occurred_at, case kind when 'Start' then 0 when 'Pause' then 1 when 'Resume' then 2 else 3 end) from lectures.consent_events where session_id = '{id}'"))!.Split(',');

    [Fact]
    public async Task Transitions_into_the_current_state_are_idempotent_and_write_no_new_events()
    {
        var a = await NewActorAsync();
        var s = await StartOkAsync(a.Client, await CourseAsync(a.Client));
        var id = s.GetProperty("id").GetString()!;

        Assert.Equal(HttpStatusCode.OK, (await PostAsync(a.Client, $"/sessions/{id}/resume")).StatusCode); // already Active
        await PostAsync(a.Client, $"/sessions/{id}/pause");
        var again = await PostAsync(a.Client, $"/sessions/{id}/pause");
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal("2", (await again.BodyAsync()).GetProperty("lastSeq").GetString());
        await PostAsync(a.Client, $"/sessions/{id}/stop");
        var stopAgain = await PostAsync(a.Client, $"/sessions/{id}/stop");
        Assert.Equal(HttpStatusCode.OK, stopAgain.StatusCode);
        Assert.Equal("3", (await stopAgain.BodyAsync()).GetProperty("lastSeq").GetString());

        Assert.Equal(3, await CountAsync("consent_events", $"session_id = '{id}'"));
    }

    [Fact]
    public async Task Stop_is_allowed_from_Paused()
    {
        var a = await NewActorAsync();
        var s = await StartOkAsync(a.Client, await CourseAsync(a.Client));
        var id = s.GetProperty("id").GetString()!;
        await PostAsync(a.Client, $"/sessions/{id}/pause");
        var stop = await PostAsync(a.Client, $"/sessions/{id}/stop");
        Assert.Equal("Stopped", (await stop.BodyAsync()).GetProperty("state").GetString());
    }

    [Theory]
    [InlineData("pause")]
    [InlineData("resume")]
    public async Task Pause_or_resume_of_a_stopped_session_is_409_invalid_state(string action)
    {
        var a = await NewActorAsync();
        var s = await StartOkAsync(a.Client, await CourseAsync(a.Client));
        var id = s.GetProperty("id").GetString()!;
        await PostAsync(a.Client, $"/sessions/{id}/stop");
        var res = await PostAsync(a.Client, $"/sessions/{id}/{action}");
        await res.AssertProblemAsync(HttpStatusCode.Conflict, "invalid_state");
        Assert.Equal(Problems.InvalidStateText, (await res.BodyAsync()).GetProperty("detail").GetString());
        Assert.Equal(2, await CountAsync("consent_events", $"session_id = '{id}'")); // Start + Stop only
    }

    [Fact]
    public async Task Server_set_Failed_state_rejects_every_transition()
    {
        var a = await NewActorAsync();
        var s = await StartOkAsync(a.Client, await CourseAsync(a.Client));
        var id = s.GetProperty("id").GetString()!;
        await F.ExecAsync($"update lectures.lecture_sessions set state = 'Failed', stopped_at = now() where id = '{id}'");
        foreach (var action in new[] { "pause", "resume", "stop" })
            await (await PostAsync(a.Client, $"/sessions/{id}/{action}")).AssertProblemAsync(HttpStatusCode.Conflict, "invalid_state");
        var got = await (await a.Client.GetAsync($"/api/v1/sessions/{id}")).BodyAsync();
        ContractSchemas.AssertValid("session", got);
        Assert.Equal("Failed", got.GetProperty("state").GetString());
    }

    [Fact]
    public async Task Concurrent_pause_requests_write_exactly_one_pause_event()
    {
        var a = await NewActorAsync();
        var s = await StartOkAsync(a.Client, await CourseAsync(a.Client));
        var id = s.GetProperty("id").GetString()!;
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => PostAsync(a.Client, $"/sessions/{id}/pause")));
        Assert.All(results, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.Equal(1, await CountAsync("consent_events", $"session_id = '{id}' and kind = 'Pause'"));
        Assert.Equal("1,2", await F.TextAsync($"select string_agg(seq::text, ',' order by seq) from lectures.session_events where session_id = '{id}'"));
    }

    // ---------------- delete (C-21) ----------------

    [Fact]
    public async Task Delete_of_an_active_session_stops_it_removes_session_data_and_keeps_content_free_consent_metadata()
    {
        var a = await NewActorAsync();
        var s = await StartOkAsync(a.Client, await CourseAsync(a.Client));
        var id = s.GetProperty("id").GetString()!;
        await PostAsync(a.Client, $"/sessions/{id}/audio-ticket");
        await StartAsync(a.Client, await CourseAsync(a.Client), "idem-" + Guid.NewGuid()); // 409; leaves no row

        var del = await a.Client.DeleteAsync($"/api/v1/sessions/{id}");
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);
        await (await a.Client.GetAsync($"/api/v1/sessions/{id}")).AssertProblemAsync(HttpStatusCode.NotFound, "not_found");

        Assert.Equal(0, await CountAsync("lecture_sessions", $"id = '{id}'"));
        Assert.Equal(0, await CountAsync("session_events", $"session_id = '{id}'"));
        Assert.Equal(0, await CountAsync("session_counters", $"session_id = '{id}'"));
        Assert.Equal(0, await CountAsync("audio_tickets", $"session_id = '{id}'"));
        Assert.Equal(0, await CountAsync("idempotency_keys", $"session_id = '{id}'"));

        Assert.Equal(1, await CountAsync("consent_records", $"session_id = '{id}'"));
        Assert.Equal(["Start", "Stop"], await KindsAsync(id));
        Assert.Equal(1, await CountAsync("audit_events", $"session_id = '{id}' and action = 'session.stop'"));
        Assert.Equal(1, await CountAsync("audit_events", $"session_id = '{id}' and action = 'session.delete'"));

        // The recording slot is free again.
        Assert.Equal(HttpStatusCode.Created, (await StartAsync(a.Client, await CourseAsync(a.Client))).StatusCode);
    }

    [Fact]
    public async Task Delete_of_a_stopped_session_does_not_add_a_second_stop_event_and_second_delete_is_404()
    {
        var a = await NewActorAsync();
        var s = await StartOkAsync(a.Client, await CourseAsync(a.Client));
        var id = s.GetProperty("id").GetString()!;
        await PostAsync(a.Client, $"/sessions/{id}/stop");
        Assert.Equal(HttpStatusCode.NoContent, (await a.Client.DeleteAsync($"/api/v1/sessions/{id}")).StatusCode);
        Assert.Equal(["Start", "Stop"], await KindsAsync(id));
        await (await a.Client.DeleteAsync($"/api/v1/sessions/{id}")).AssertProblemAsync(HttpStatusCode.NotFound, "not_found");
    }

    // ---------------- placeholders for later increments (summary, catch-ups, notes) ----------------

    [Fact]
    public async Task Transcript_summary_catchups_and_notes_have_defined_empty_behaviour_for_the_owner()
    {
        var a = await NewActorAsync();
        var s = await StartOkAsync(a.Client, await CourseAsync(a.Client));
        var id = s.GetProperty("id").GetString()!;

        var t = await a.Client.GetAsync($"/api/v1/sessions/{id}/transcript?afterSeq=0&limit=50");
        Assert.Equal(HttpStatusCode.OK, t.StatusCode);
        var tb = await t.BodyAsync();
        // B2: the transcript endpoint returns every persisted event, including the session.state events (section 13.1).
        ContractSchemas.AssertValid("transcript-page", tb);
        Assert.Equal("session.state", tb.GetProperty("events")[0].GetProperty("type").GetString());
        Assert.Single(tb.GetProperty("events").EnumerateArray());
        Assert.False(tb.GetProperty("hasMore").GetBoolean());

        await (await a.Client.GetAsync($"/api/v1/sessions/{id}/summary")).AssertProblemAsync(HttpStatusCode.NotFound, "not_found");
        await (await a.Client.GetAsync($"/api/v1/sessions/{id}/catch-ups/abc")).AssertProblemAsync(HttpStatusCode.NotFound, "not_found");
        await (await a.Client.GetAsync($"/api/v1/sessions/{id}/notes")).AssertProblemAsync(HttpStatusCode.Conflict, "invalid_state");
    }

    [Theory]
    [InlineData("limit=0")]
    [InlineData("limit=501")]
    [InlineData("afterSeq=abc")]
    [InlineData("afterSeq=-1")]
    public async Task Transcript_validates_query_parameters(string query)
    {
        var a = await NewActorAsync();
        var s = await StartOkAsync(a.Client, await CourseAsync(a.Client));
        await (await a.Client.GetAsync($"/api/v1/sessions/{s.GetProperty("id").GetString()}/transcript?{query}"))
            .AssertProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
    }

    // ---------------- misc ----------------

    [Fact]
    public async Task Unsafe_lecture_requests_without_an_allowed_Origin_are_rejected_before_any_work()
    {
        var a = await NewActorAsync();
        var courseId = await CourseAsync(a.Client);
        var c = F.NewClient(withOrigin: false);
        c.DefaultRequestHeaders.Add("Cookie", a.Client.DefaultRequestHeaders.GetValues("Cookie").Single());
        var res = await c.PostAsJsonAsync($"/api/v1/courses/{courseId}/sessions", new { consent = Consent() });
        await res.AssertProblemAsync(HttpStatusCode.Forbidden, "csrf_failed");
        Assert.Equal(0, await CountAsync("consent_records", $"user_id = '{a.UserId}'"));
    }

    [Fact]
    public async Task The_schema_validator_is_live_and_rejects_a_malformed_session()
    {
        Assert.False(ContractSchemas.IsValid("session", """{"id":"x","courseId":"y","title":null,"state":"Recording","startedAt":"2026-10-27T14:03:05Z","lastSeq":"1","consent":{"textVersion":"d","acknowledgedAt":"2026-10-27T14:03:05Z"},"etag":"e"}"""));
        Assert.False(ContractSchemas.IsValid("session", """{"id":"x","courseId":"y","title":null,"state":"Active","startedAt":"2026-10-27T14:03:05Z","lastSeq":1,"consent":{"textVersion":"d","acknowledgedAt":"2026-10-27T14:03:05Z"},"etag":"e"}"""));
    }

    [Fact]
    public async Task Every_lectures_table_carries_user_id()
    {
        _ = F.CreateClient();
        var tables = await F.ScalarAsync("select count(*) from pg_tables where schemaname = 'lectures' and tablename <> '__ef_migrations_history'");
        var withUser = await F.ScalarAsync("select count(distinct table_name) from information_schema.columns where table_schema = 'lectures' and column_name = 'user_id'");
        Assert.True(tables >= 8);
        Assert.Equal(tables, withUser);
    }

    [Fact]
    public void Consent_registry_accepts_only_registered_versions_and_labels_the_draft_text()
    {
        var r = new ConsentTextRegistry();
        Assert.True(r.IsAccepted("draft-1"));
        Assert.False(r.IsAccepted("draft-2"));
        Assert.False(r.IsAccepted(null));
        Assert.False(r.IsAccepted(""));
        Assert.False(r.IsAccepted("DRAFT-1")); // ordinal
        Assert.StartsWith("DRAFT", r.TextFor(r.Current));
    }

    [Fact]
    public async Task Fixed_error_vocabulary_is_stable_for_all_three_codes_including_provider_unavailable()
    {
        foreach (var (result, status, code, text) in new[]
        {
            (Problems.ConsentRequired(), 403, "consent_required", Problems.ConsentRequiredText),
            (Problems.InvalidState(), 409, "invalid_state", Problems.InvalidStateText),
            (Problems.ProviderUnavailable(), 503, "provider_unavailable", Problems.ProviderUnavailableText),
        })
        {
            var ctx = new Microsoft.AspNetCore.Http.DefaultHttpContext();
            ctx.Response.Body = new MemoryStream();
            await result.ExecuteAsync(ctx);
            Assert.Equal(status, ctx.Response.StatusCode);
            ctx.Response.Body.Position = 0;
            var body = JsonDocument.Parse(await new StreamReader(ctx.Response.Body).ReadToEndAsync()).RootElement;
            Assert.Equal(code, body.GetProperty("code").GetString());
            Assert.Equal(text, body.GetProperty("detail").GetString());
            Assert.Equal(text, body.GetProperty("title").GetString());
            var error = JsonSerializer.Serialize(new
            {
                v = 1, type = "error", sessionId = "s1", seq = (string?)null, ts = "2026-10-27T14:03:05Z", payload = new { code, detail = text },
            });
            Assert.True(ContractSchemas.IsValid("hub-event", error), $"{code} detail must fit the hub error event");
        }
        Assert.Equal(3, new[] { Problems.ConsentRequiredText, Problems.InvalidStateText, Problems.ProviderUnavailableText }.Distinct().Count());
    }
}
