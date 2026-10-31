using System.Net;
using System.Text;
using System.Text.Json;
using Tether.Ai.Notes;
using Tether.Ai.Provider;
using Tether.Ai.Summary;
using Tether.Ai.Validation;
using Tether.IntegrationTests.Infrastructure;
using Tether.SharedKernel;
using Tether.Transcript.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Tether.IntegrationTests;

/// <summary>
/// PO-18d: after Stop, a deliberate "Make notes" press is the ONLY thing that may send the stored transcript to an AI provider, and one
/// press makes both the notes and the Final summary revision. Mock provider only; nothing here says how a real model behaves.
/// </summary>
public abstract class NotesTestBase(ApiFactory factory) : AiRouteTestBase(factory)
{
    private protected MockAiClient Mock => F.Services.GetRequiredService<MockAiClient>();

    protected async Task<(Actor A, string SessionId)> StoppedSessionAsync(Actor? actor = null, int finals = 3)
    {
        var (a, id) = await LiveSessionAsync(actor);
        if (finals > 0) await SeedFinalsAsync(id, a.UserId, finals);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(a.Client, $"/sessions/{id}/stop")).StatusCode);
        return (a, id);
    }

    protected static HttpRequestMessage Press(string sessionId, string? key, string body = "{}", string contentType = "application/json")
    {
        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/sessions/{sessionId}/notes") { Content = new StringContent(body, Encoding.UTF8, contentType) };
        if (key is not null) req.Headers.TryAddWithoutValidation("Idempotency-Key", key);
        return req;
    }

    protected static Task<HttpResponseMessage> PressAsync(HttpClient c, string sessionId, string? key = null) =>
        c.SendAsync(Press(sessionId, key ?? NewKey()));

    protected static string NewKey() => "k" + Guid.NewGuid().ToString("N");

    protected async Task<JsonElement> WaitSettledAsync(HttpClient c, string sessionId)
    {
        JsonElement body = default;
        for (var i = 0; i < 200; i++)
        {
            var res = await c.GetAsync($"/api/v1/sessions/{sessionId}/notes");
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            body = await res.BodyAsync();
            if (body.GetProperty("status").GetString() != "Pending") return body;
            await Task.Delay(25);
        }
        Assert.Fail("notes stayed Pending: " + body.GetRawText());
        return body;
    }

    protected Task<long> NotesRowsAsync(string sessionId) => F.ScalarAsync($"select count(*) from ai.lecture_notes where session_id = '{sessionId}'");

    protected Task<long> PressAuditsAsync(Guid userId) =>
        F.ScalarAsync($"select count(*) from lectures.audit_events where user_id = '{userId}' and action = 'notes.requested'");

    protected async Task AssertNothingHeldAsync(Guid userId) =>
        Assert.Equal(0, await F.ScalarAsync($"select count(*) from ai.ai_usage where user_id = '{userId}' and outcome = 'reserved'"));

    protected static string StripTrace(JsonElement e)
    {
        var d = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(e.GetRawText())!;
        d.Remove("traceId");
        return JsonSerializer.Serialize(d.OrderBy(k => k.Key).ToDictionary(k => k.Key, k => k.Value));
    }
}

/// <summary>Default instant mock: authorisation, validation, CSRF, idempotency, generation, Final summary, budget and the B3-06 regression.</summary>
public class AiNotesTests(ApiFactory factory) : NotesTestBase(factory), IClassFixture<ApiFactory>
{
    // ---------- authentication and ownership ----------

    [Fact]
    public async Task Unauthenticated_press_and_read_get_401()
    {
        var anon = F.NewClient();
        var id = Guid.NewGuid().ToString();
        await (await anon.SendAsync(Press(id, NewKey()))).AssertProblemAsync(HttpStatusCode.Unauthorized, "unauthenticated");
        await (await anon.GetAsync($"/api/v1/sessions/{id}/notes")).AssertProblemAsync(HttpStatusCode.Unauthorized, "unauthenticated");
    }

    [Fact]
    public async Task Another_users_stopped_session_is_404_like_a_missing_one_and_nothing_is_created_audited_or_sent()
    {
        var (alice, id) = await StoppedSessionAsync();
        var bob = await NewActorAsync();
        var before = Mock.Calls;

        var theirs = await PressAsync(bob.Client, id);
        var missing = await PressAsync(bob.Client, Guid.NewGuid().ToString());
        await theirs.AssertProblemAsync(HttpStatusCode.NotFound, "not_found");
        await missing.AssertProblemAsync(HttpStatusCode.NotFound, "not_found");
        Assert.Equal(StripTrace(await theirs.BodyAsync()), StripTrace(await missing.BodyAsync()));

        var read = await bob.Client.GetAsync($"/api/v1/sessions/{id}/notes");
        var readMissing = await bob.Client.GetAsync($"/api/v1/sessions/{Guid.NewGuid()}/notes");
        await read.AssertProblemAsync(HttpStatusCode.NotFound, "not_found");
        Assert.Equal(StripTrace(await read.BodyAsync()), StripTrace(await readMissing.BodyAsync()));

        await Task.Delay(200);
        Assert.Equal(0, await NotesRowsAsync(id));
        Assert.Equal(0, await PressAuditsAsync(bob.UserId));
        Assert.Equal(0, await PressAuditsAsync(alice.UserId));
        Assert.Equal(before, Mock.Calls);
        Assert.Equal(0, await F.ScalarAsync($"select count(*) from ai.ai_usage where user_id = '{bob.UserId}'"));
    }

    [Fact]
    public async Task Another_users_ready_notes_are_404_with_the_same_body_as_a_missing_session()
    {
        var (alice, id) = await StoppedSessionAsync();
        Assert.Equal(HttpStatusCode.Accepted, (await PressAsync(alice.Client, id)).StatusCode);
        await WaitSettledAsync(alice.Client, id);
        var bob = await NewActorAsync();

        var theirs = await bob.Client.GetAsync($"/api/v1/sessions/{id}/notes");
        var missing = await bob.Client.GetAsync($"/api/v1/sessions/{Guid.NewGuid()}/notes");
        await theirs.AssertProblemAsync(HttpStatusCode.NotFound, "not_found");
        Assert.Equal(StripTrace(await theirs.BodyAsync()), StripTrace(await missing.BodyAsync()));
        // Bob replaying Alice's key on Alice's session learns nothing either.
        var rows = await NotesRowsAsync(id);
        await (await PressAsync(bob.Client, id)).AssertProblemAsync(HttpStatusCode.NotFound, "not_found");
        Assert.Equal(rows, await NotesRowsAsync(id));
    }

    [Fact]
    public async Task A_press_without_an_Origin_or_from_a_foreign_Origin_is_refused_before_anything_happens()
    {
        var (a, id) = await StoppedSessionAsync();
        await (await NoOriginClient(a).SendAsync(Press(id, NewKey()))).AssertProblemAsync(HttpStatusCode.Forbidden, "csrf_failed");
        var foreign = NoOriginClient(a);
        foreign.DefaultRequestHeaders.Add("Origin", "https://evil.example");
        await (await foreign.SendAsync(Press(id, NewKey()))).AssertProblemAsync(HttpStatusCode.Forbidden, "csrf_failed");
        Assert.Equal(0, await NotesRowsAsync(id));
        Assert.Equal(0, await PressAuditsAsync(a.UserId));
    }

    // ---------- state rule: only a Stopped session ----------

    [Theory]
    [InlineData("active")]
    [InlineData("paused")]
    public async Task A_live_or_paused_session_cannot_be_pressed_and_nothing_is_sent(string state)
    {
        var (a, id) = await LiveSessionAsync();
        await SeedFinalsAsync(id, a.UserId);
        if (state == "paused") Assert.Equal(HttpStatusCode.OK, (await PostAsync(a.Client, $"/sessions/{id}/pause")).StatusCode);
        var before = Mock.Calls;

        await (await PressAsync(a.Client, id)).AssertProblemAsync(HttpStatusCode.Conflict, "invalid_state");
        await (await a.Client.GetAsync($"/api/v1/sessions/{id}/notes")).AssertProblemAsync(HttpStatusCode.Conflict, "invalid_state");

        await Task.Delay(200);
        Assert.Equal(before, Mock.Calls);
        Assert.Equal(0, await NotesRowsAsync(id));
        Assert.Equal(0, await PressAuditsAsync(a.UserId));
    }

    [Fact]
    public async Task Reading_notes_of_a_stopped_session_before_any_press_is_409_invalid_state()
    {
        var (a, id) = await StoppedSessionAsync();
        await (await a.Client.GetAsync($"/api/v1/sessions/{id}/notes")).AssertProblemAsync(HttpStatusCode.Conflict, "invalid_state");
    }

    // ---------- request validation ----------

    [Fact]
    public async Task The_press_has_a_closed_shape_and_a_required_idempotency_key()
    {
        var (a, id) = await StoppedSessionAsync();
        await (await a.Client.SendAsync(Press(id, key: null))).AssertProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        await (await a.Client.SendAsync(Press(id, "bad key!"))).AssertProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        await (await a.Client.SendAsync(Press(id, new string('k', 65)))).AssertProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        await (await a.Client.SendAsync(Press(id, NewKey(), """{"windowMinutes":5}"""))).AssertProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        await (await a.Client.SendAsync(Press(id, NewKey(), "[]"))).AssertProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        await (await a.Client.SendAsync(Press(id, NewKey(), "not json"))).AssertProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        await (await a.Client.SendAsync(Press(id, NewKey(), "{}", "text/plain"))).AssertProblemAsync(HttpStatusCode.UnsupportedMediaType, "unsupported_media_type");
        Assert.Equal(0, await NotesRowsAsync(id));
        Assert.Equal(0, await PressAuditsAsync(a.UserId));
    }

    // ---------- the happy path: one press, cited notes plus the Final summary ----------

    [Fact]
    public async Task One_press_makes_cited_mock_notes_and_a_final_summary_revision_from_the_stored_transcript()
    {
        var (a, id) = await StoppedSessionAsync(finals: 6);
        var before = Mock.Calls;

        var res = await PressAsync(a.Client, id);
        Assert.Equal(HttpStatusCode.Accepted, res.StatusCode);
        Assert.Equal($"/api/v1/sessions/{id}/notes", res.Headers.Location?.OriginalString);
        var accepted = await res.BodyAsync();
        ContractSchemas.AssertValid("notes", accepted);
        Assert.Equal(1, accepted.GetProperty("revision").GetInt32());

        var notes = await WaitSettledAsync(a.Client, id);
        ContractSchemas.AssertValid("notes", notes);
        Assert.Equal("Ready", notes.GetProperty("status").GetString());
        Assert.Equal("mock", notes.GetProperty("source").GetString()); // B3-08 class: mock output is labelled on the wire
        Assert.Equal(accepted.GetProperty("notesId").GetString(), notes.GetProperty("notesId").GetString());
        Assert.Equal(2000, notes.GetProperty("coveredFromMs").GetInt64());
        Assert.Equal(6 * 2000 + 1500, notes.GetProperty("coveredToMs").GetInt64());
        Assert.False(notes.GetProperty("truncated").GetBoolean());

        // Every bullet is cited to a STORED segment of this session, with the stored start time (never the model's).
        var bullets = notes.GetProperty("sections").EnumerateArray().SelectMany(s => s.GetProperty("bullets").EnumerateArray()).ToList();
        Assert.NotEmpty(bullets);
        foreach (var b in bullets)
            foreach (var c in b.GetProperty("citations").EnumerateArray())
            {
                var n = int.Parse(c.GetProperty("segmentId").GetString()!["seg_".Length..]);
                Assert.InRange(n, 1, 6);
                Assert.Equal(n * 2000, c.GetProperty("startMs").GetInt64());
            }

        var finalRev = notes.GetProperty("finalSummaryRevision").GetInt32();
        var summary = await (await a.Client.GetAsync($"/api/v1/sessions/{id}/summary?revision={finalRev}")).BodyAsync();
        ContractSchemas.AssertValid("summary-revision", summary);
        Assert.Equal("Final", summary.GetProperty("status").GetString());
        Assert.Equal("mock", summary.GetProperty("source").GetString());
        var latest = await (await a.Client.GetAsync($"/api/v1/sessions/{id}/summary")).BodyAsync();
        Assert.Equal(finalRev, latest.GetProperty("revision").GetInt32());

        // Derived data is stored apart from the transcript with provenance; the press is audited once; spend is in the lecture pool.
        Assert.Equal("1", await F.TextAsync($"select source_from_seq from ai.lecture_notes where session_id = '{id}'"));
        Assert.Equal("6", await F.TextAsync($"select source_to_seq from ai.lecture_notes where session_id = '{id}'"));
        Assert.Equal("lecture-notes.v1", await F.TextAsync($"select prompt_version from ai.lecture_notes where session_id = '{id}'"));
        Assert.Equal(1, await PressAuditsAsync(a.UserId));
        Assert.True(await F.ScalarAsync($"select count(*) from ai.ai_usage where scope_id = '{id}' and workflow = 'Notes' and outcome = 'ok' and pool = 'lecture'") >= 1);
        Assert.True(await F.ScalarAsync($"select count(*) from ai.ai_usage where scope_id = '{id}' and workflow = 'FinalSummary' and outcome = 'ok'") >= 1);
        Assert.Equal(before + 2, Mock.Calls); // one notes part, one final-summary chunk: nothing else
        await AssertNothingHeldAsync(a.UserId);
    }

    [Fact]
    public async Task Replaying_the_key_returns_the_same_press_with_200_and_never_generates_again()
    {
        var (a, id) = await StoppedSessionAsync();
        var key = NewKey();
        var first = await (await PressAsync(a.Client, id, key)).BodyAsync();
        var ready = await WaitSettledAsync(a.Client, id);
        var calls = Mock.Calls;

        var replay = await PressAsync(a.Client, id, key);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal("true", replay.Headers.GetValues("Idempotency-Replayed").Single());
        var body = await replay.BodyAsync();
        ContractSchemas.AssertValid("notes", body);
        Assert.Equal(first.GetProperty("notesId").GetString(), body.GetProperty("notesId").GetString());
        Assert.Equal(ready.GetRawText(), body.GetRawText());

        var another = await PressAsync(a.Client, id); // a second, different press after Ready gets the same notes, not a new generation
        Assert.Equal(HttpStatusCode.OK, another.StatusCode);
        Assert.Equal(first.GetProperty("notesId").GetString(), (await another.BodyAsync()).GetProperty("notesId").GetString());

        await Task.Delay(200);
        Assert.Equal(calls, Mock.Calls);
        Assert.Equal(1, await NotesRowsAsync(id));
        Assert.Equal(1, await F.ScalarAsync($"select count(*) from ai.summary_revisions where session_id = '{id}' and status = 'Final'"));
        Assert.Equal(1, await PressAuditsAsync(a.UserId)); // only a press that could send something is audited; a replay or a press answered with the ready notes cannot
    }

    [Fact]
    public async Task Concurrent_presses_start_exactly_one_generation()
    {
        var (a, id) = await StoppedSessionAsync();
        var before = Mock.Calls;

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => PressAsync(a.Client, id)));
        Assert.All(responses, r => Assert.True(r.StatusCode is HttpStatusCode.Accepted or HttpStatusCode.OK, r.StatusCode.ToString()));
        var ids = new HashSet<string>();
        foreach (var r in responses) ids.Add((await r.BodyAsync()).GetProperty("notesId").GetString()!);
        Assert.Single(ids);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Accepted);

        Assert.Equal("Ready", (await WaitSettledAsync(a.Client, id)).GetProperty("status").GetString());
        Assert.Equal(1, await NotesRowsAsync(id));
        Assert.Equal(before + 2, Mock.Calls);
        Assert.Equal(1, await F.ScalarAsync($"select count(*) from ai.summary_revisions where session_id = '{id}' and status = 'Final'"));
    }

    [Fact]
    public async Task A_stopped_lecture_without_transcript_text_is_NotNeeded_and_sends_nothing()
    {
        var (a, id) = await StoppedSessionAsync(finals: 0);
        var before = Mock.Calls;
        var res = await PressAsync(a.Client, id);
        Assert.Equal(HttpStatusCode.Accepted, res.StatusCode);
        var body = await res.BodyAsync();
        ContractSchemas.AssertValid("notes", body);
        Assert.Equal("NotNeeded", body.GetProperty("status").GetString());
        await Task.Delay(200);
        Assert.Equal(before, Mock.Calls);
        Assert.Equal(0, await F.ScalarAsync($"select count(*) from ai.ai_usage where user_id = '{a.UserId}'"));
    }

    // ---------- B3-06 regression: Stop alone sends nothing ----------

    [Fact]
    public async Task Stopping_without_a_press_sends_nothing_and_makes_no_final_summary()
    {
        var (a, id) = await LiveSessionAsync();
        await SeedFinalsAsync(id, a.UserId, 8);
        var scheduler = F.Services.GetRequiredService<SummaryScheduler>();
        for (var n = 1; n <= 8; n++) scheduler.OnFinalPersisted(a.UserId, Guid.Parse(id), n);
        var before = Mock.Calls;

        Assert.Equal(HttpStatusCode.OK, (await PostAsync(a.Client, $"/sessions/{id}/stop")).StatusCode);
        await scheduler.TickAsync(default);
        await Task.Delay(300);

        Assert.Equal(before, Mock.Calls);
        Assert.Equal(0, await F.ScalarAsync($"select count(*) from ai.ai_usage where user_id = '{a.UserId}'"));
        Assert.Equal(0, await F.ScalarAsync($"select count(*) from ai.summary_revisions where session_id = '{id}'"));
        Assert.Equal(0, await NotesRowsAsync(id));
    }

    // ---------- the gateway gate itself: every after-Stop call needs a live press ----------

    public static TheoryData<string> RefusedCases => new() { "no-press", "active", "paused", "deleted", "other-user", "expired", "finished", "wrong-session" };

    private async Task<GatewayOutcome<NotesChunkResult>> NotesCallAsync(Guid userId, string sessionId, Guid pressId)
    {
        await using var scope = F.Services.GetRequiredService<IServiceScopeFactory>().CreateUserScope(userId);
        var opts = F.Services.GetRequiredService<AiOptions>();
        var sid = Guid.Parse(sessionId);
        var seg = new FinalSegment(sid, 1, 1, 2000, 3500, "Mitosis has four phases.", null);
        var part = NotesRequests.Build(userId, sid, pressId, [seg], false, opts.Notes).Parts.Single();
        return await scope.ServiceProvider.GetRequiredService<AiGateway>().RunAsync(part.Request, j => NotesValidator.Validate(j, part.Context), default);
    }

    private Task InsertPressAsync(string sessionId, Guid userId, Guid pressId, string state = "Pending", DateTimeOffset? requestedAt = null) =>
        F.ExecAsync("insert into ai.lecture_notes (id, session_id, user_id, revision, state, idempotency_key, requested_at, completed_at, sections, truncated) " +
                    $"values ('{pressId}', '{sessionId}', '{userId}', 1, '{state}', 'k{pressId:N}', '{(requestedAt ?? F.Clock.GetUtcNow()):O}', " +
                    $"{(state == "Pending" ? "null" : $"'{F.Clock.GetUtcNow():O}'")}, '[]', false)");

    [Theory]
    [MemberData(nameof(RefusedCases))]
    public async Task The_gateway_refuses_an_after_stop_call_without_a_live_press_of_this_user_for_this_session(string scenario)
    {
        var (a, id) = scenario is "active" or "paused" ? await LiveSessionAsync() : await StoppedSessionAsync();
        var press = Guid.NewGuid();
        var caller = a.UserId;
        switch (scenario)
        {
            case "active": await InsertPressAsync(id, a.UserId, press); break; // a press row the API would never create on a live session
            case "paused":
                await InsertPressAsync(id, a.UserId, press);
                Assert.Equal(HttpStatusCode.OK, (await PostAsync(a.Client, $"/sessions/{id}/pause")).StatusCode);
                break;
            case "deleted":
                await InsertPressAsync(id, a.UserId, press);
                Assert.Equal(HttpStatusCode.NoContent, (await a.Client.DeleteAsync($"/api/v1/sessions/{id}")).StatusCode);
                break;
            case "other-user":
                await InsertPressAsync(id, a.UserId, press);
                caller = (await NewActorAsync()).UserId;
                break;
            case "expired": await InsertPressAsync(id, a.UserId, press, requestedAt: F.Clock.GetUtcNow() - TimeSpan.FromHours(1)); break;
            case "finished": await InsertPressAsync(id, a.UserId, press, state: "Unavailable"); break;
            case "wrong-session":
                var (_, other) = await StoppedSessionAsync(a);
                await InsertPressAsync(other, a.UserId, press); // a live press, but for another of the user's sessions
                break;
        }
        var before = Mock.Calls;

        var outcome = await NotesCallAsync(caller, id, press);

        Assert.Equal(AiFailure.SessionClosed, outcome.Failure);
        Assert.Equal(before, Mock.Calls);
        await AssertNothingHeldAsync(caller);
        Assert.Equal(0, await F.ScalarAsync($"select coalesce(sum(est_cost_micros),0) from ai.ai_usage where user_id = '{caller}'"));
    }

    [Fact]
    public async Task Control_the_gateway_allows_the_call_for_a_live_press_on_a_stopped_session()
    {
        var (a, id) = await StoppedSessionAsync();
        var press = Guid.NewGuid();
        await InsertPressAsync(id, a.UserId, press);
        var before = Mock.Calls;
        var outcome = await NotesCallAsync(a.UserId, id, press);
        Assert.True(outcome.Ok, outcome.Failure);
        Assert.Equal(before + 1, Mock.Calls);
        await AssertNothingHeldAsync(a.UserId);
    }

    [Fact]
    public async Task A_live_workflow_request_that_carries_a_press_is_still_refused_on_a_stopped_session()
    {
        var (a, id) = await StoppedSessionAsync();
        var press = Guid.NewGuid();
        await InsertPressAsync(id, a.UserId, press);
        await using var scope = F.Services.GetRequiredService<IServiceScopeFactory>().CreateUserScope(a.UserId);
        var opts = F.Services.GetRequiredService<AiOptions>();
        var sid = Guid.Parse(id);
        var seg = new FinalSegment(sid, 1, 1, 2000, 3500, "Mitosis has four phases.", null);
        var built = SummaryRequests.BuildDelta(a.UserId, sid, 0, [], [], [seg], opts.Summary, opts.CallTimeout, pressId: press); // RollingSummary
        var before = Mock.Calls;
        var outcome = await scope.ServiceProvider.GetRequiredService<AiGateway>().RunAsync(built.Request, j => SummaryValidator.Validate(j, new SummaryContext(0, built.Allowed)), default);
        Assert.Equal(AiFailure.SessionClosed, outcome.Failure);
        Assert.Equal(before, Mock.Calls);
    }

    // ---------- budget ----------

    [Fact]
    public async Task An_exhausted_session_budget_gives_BudgetLimited_without_a_call_and_a_later_press_can_try_again()
    {
        var (a, id) = await StoppedSessionAsync();
        var usage = Guid.NewGuid();
        await F.ExecAsync("insert into ai.ai_usage (id, user_id, session_id, scope_kind, scope_id, pool, workflow, prompt_version, model, input_tokens, cached_tokens, output_tokens, est_cost_micros, latency_ms, outcome, created_at) " +
                          $"values ('{usage}', '{a.UserId}', '{id}', 'lecture', '{id}', 'lecture', 'RollingSummary', 'rolling-summary.v2', 'mock-fast', 0, 0, 0, 200000, 0, 'ok', now())");
        var before = Mock.Calls;

        var res = await PressAsync(a.Client, id);
        Assert.Equal(HttpStatusCode.Accepted, res.StatusCode);
        var body = await res.BodyAsync();
        ContractSchemas.AssertValid("notes", body);
        Assert.Equal("BudgetLimited", body.GetProperty("status").GetString());
        await Task.Delay(200);
        Assert.Equal(before, Mock.Calls);

        await F.ExecAsync($"delete from ai.ai_usage where id = '{usage}'"); // budget is back (test only; the ledger is otherwise never edited)
        var retry = await PressAsync(a.Client, id);
        Assert.Equal(HttpStatusCode.Accepted, retry.StatusCode);
        Assert.Equal(2, (await retry.BodyAsync()).GetProperty("revision").GetInt32());
        Assert.Equal("Ready", (await WaitSettledAsync(a.Client, id)).GetProperty("status").GetString());
    }

    // ---------- deletion ----------

    [Fact]
    public async Task Deleting_the_session_removes_the_notes_and_the_final_summary_but_keeps_the_content_free_audit()
    {
        var (a, id) = await StoppedSessionAsync();
        Assert.Equal(HttpStatusCode.Accepted, (await PressAsync(a.Client, id)).StatusCode);
        await WaitSettledAsync(a.Client, id);
        Assert.Equal(HttpStatusCode.NoContent, (await a.Client.DeleteAsync($"/api/v1/sessions/{id}")).StatusCode);

        Assert.Equal(0, await NotesRowsAsync(id));
        Assert.Equal(0, await F.ScalarAsync($"select count(*) from ai.summary_revisions where session_id = '{id}'"));
        Assert.Equal(1, await PressAuditsAsync(a.UserId));
        await (await a.Client.GetAsync($"/api/v1/sessions/{id}/notes")).AssertProblemAsync(HttpStatusCode.NotFound, "not_found");
    }
}

/// <summary>Per-press estimated-cost cap of 1 micro-USD: every press is over it before any call.</summary>
public sealed class TinyNotesCapFactory : ApiFactory
{
    protected override IEnumerable<KeyValuePair<string, string?>> Settings => [new("Ai:Notes:PerRequestMicroUsd", "1")];
}

public class AiNotesBudgetTests(TinyNotesCapFactory factory) : NotesTestBase(factory), IClassFixture<TinyNotesCapFactory>
{
    [Fact]
    public async Task A_press_over_the_per_press_cap_ends_BudgetLimited_and_never_reaches_the_provider()
    {
        var (a, id) = await StoppedSessionAsync();
        var before = Mock.Calls;
        Assert.Equal(HttpStatusCode.Accepted, (await PressAsync(a.Client, id)).StatusCode);
        var notes = await WaitSettledAsync(a.Client, id);
        ContractSchemas.AssertValid("notes", notes);
        Assert.Equal("BudgetLimited", notes.GetProperty("status").GetString());
        Assert.Equal(before, Mock.Calls);
        Assert.Equal(1, await F.ScalarAsync($"select count(*) from ai.ai_usage where scope_id = '{id}' and workflow = 'Notes' and outcome = 'budget_denied'"));
        Assert.Equal(0, await F.ScalarAsync($"select count(*) from ai.summary_revisions where session_id = '{id}'"));
        await AssertNothingHeldAsync(a.UserId);
    }
}

/// <summary>C-23 for the notes path: transcript text that goes through a press, the mock and the notes store never reaches any log.</summary>
public class AiNotesLogCanaryTests(AiNotesLogCanaryTests.Factory f) : NotesTestBase(f), IClassFixture<AiNotesLogCanaryTests.Factory>
{
    public const string Sentinel = "CANARY-NOTES-TEXT-5e21";

    public sealed class Factory : ApiFactory
    {
        public LogCanaryTests.CapturingProvider Logs { get; } = new();
        protected override IEnumerable<KeyValuePair<string, string?>> Settings => LogPipelineCanary.TraceEverything(ConnectionString);
        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(s => s.AddSingleton<ILoggerProvider>(Logs));
        }
    }

    [Fact]
    public async Task Notes_text_never_reaches_any_log_category()
    {
        var (a, id) = await LiveSessionAsync();
        for (var n = 1; n <= 4; n++)
            await F.ExecAsync($"insert into transcript.transcript_events (session_id, seq, user_id, kind, segment_no, start_ms, end_ms, text, provider, occurred_at) " +
                              $"values ('{id}', {n}, '{a.UserId}', 'Final', {n}, {n * 2000}, {n * 2000 + 1500}, 'The lecturer said {Sentinel} number {n}.', 'replay', now())");
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(a.Client, $"/sessions/{id}/stop")).StatusCode);

        Assert.Equal(HttpStatusCode.Accepted, (await PressAsync(a.Client, id)).StatusCode);
        var notes = await WaitSettledAsync(a.Client, id);
        Assert.Equal("Ready", notes.GetProperty("status").GetString());
        Assert.Contains(Sentinel, notes.GetRawText()); // the text really went through the pipeline (extractive mock)
        await Task.Delay(300);

        var logs = ((Factory)F).Logs.Lines.ToArray();
        Assert.NotEmpty(logs);
        Assert.DoesNotContain(logs, l => l.Contains(Sentinel, StringComparison.Ordinal));
    }
}
