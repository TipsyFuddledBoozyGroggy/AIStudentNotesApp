using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Tether.Ai.CatchUp;
using Tether.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Tether.IntegrationTests;

/// <summary>Shared helpers for the AI route tests (B3-03): sessions, seeded transcript and summary rows, raw request building.</summary>
public abstract class AiRouteTestBase(ApiFactory factory) : LectureTestBase(factory)
{
    protected async Task<(Actor A, string SessionId)> LiveSessionAsync(Actor? actor = null)
    {
        var a = actor ?? await NewActorAsync();
        var s = await StartOkAsync(a.Client, await CourseAsync(a.Client));
        return (a, s.GetProperty("id").GetString()!);
    }

    /// <summary>Writes finals straight into the transcript table (the replay STT path is covered elsewhere); each is 1.5 s long, 2 s apart.</summary>
    protected async Task SeedFinalsAsync(string sessionId, Guid userId, int count = 3)
    {
        for (var n = 1; n <= count; n++)
            await F.ExecAsync($"insert into transcript.transcript_events (session_id, seq, user_id, kind, segment_no, start_ms, end_ms, text, provider, occurred_at) " +
                              $"values ('{sessionId}', {n}, '{userId}', 'Final', {n}, {n * 2000}, {n * 2000 + 1500}, 'Mitosis has four phases and this is sentence {n}.', 'replay', now())");
    }

    protected async Task SeedSummaryAsync(string sessionId, Guid userId, int revision)
    {
        await F.ExecAsync($"insert into ai.summary_revisions (session_id, revision, user_id, kind, status, based_on_seq, source_from_seq, source_to_seq, covered_from_ms, covered_to_ms, " +
                          $"tldr, topics, terms, citations, model, prompt_version, created_at) values ('{sessionId}', {revision}, '{userId}', 'Delta', 'Draft', {revision}, 1, {revision}, 0, 5000, " +
                          $"'Cells divide.', '[]', '[]', '[{{\"segmentId\":\"seg_1\",\"startMs\":2000}}]', 'mock-fast', 'rolling-summary.v1', now())");
    }

    protected static HttpRequestMessage QuickRecap(string sessionId, object? body = null, string? key = null, string contentType = "application/json")
    {
        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/sessions/{sessionId}/quick-recap")
        {
            Content = new StringContent(JsonSerializer.Serialize(body ?? new { windowMinutes = 5 }), System.Text.Encoding.UTF8, contentType),
        };
        if (key is not null) req.Headers.TryAddWithoutValidation("Idempotency-Key", key);
        return req;
    }

    protected static Task<HttpResponseMessage> QuickRecapAsync(HttpClient c, string sessionId, object? body = null, string? key = null) =>
        c.SendAsync(QuickRecap(sessionId, body, key));

    /// <summary>A client with the actor's session cookie but no Origin header (a cross-site form post, or a script without CORS).</summary>
    protected HttpClient NoOriginClient(Actor a)
    {
        var c = F.NewClient(withOrigin: false);
        c.DefaultRequestHeaders.Add("Cookie", a.Client.DefaultRequestHeaders.GetValues("Cookie").Single());
        return c;
    }

    protected async Task WaitNotPendingAsync(Guid catchUpId)
    {
        for (var i = 0; i < 100 && await F.TextAsync($"select ai_state from ai.catch_ups where id = '{catchUpId}'") == "Pending"; i++) await Task.Delay(50);
    }

    protected Task<long> CatchUpCountAsync(string sessionId) => F.ScalarAsync($"select count(*) from ai.catch_ups where session_id = '{sessionId}'");
}

/// <summary>Default mock (instant): authorisation, validation, CSRF, idempotency and summary reads.</summary>
[Collection(RacePool.Name)]
public class AiRouteTests(RacePoolFactory factory) : AiRouteTestBase(factory), IClassFixture<RacePoolFactory>
{
    // ---------- authentication ----------

    [Fact]
    public async Task Unauthenticated_requests_to_all_three_routes_get_401()
    {
        var anon = F.NewClient();
        var id = Guid.NewGuid();
        await (await anon.GetAsync($"/api/v1/sessions/{id}/summary")).AssertProblemAsync(HttpStatusCode.Unauthorized, "unauthenticated");
        await (await anon.GetAsync($"/api/v1/sessions/{id}/catch-ups/cu_{Guid.NewGuid():N}")).AssertProblemAsync(HttpStatusCode.Unauthorized, "unauthenticated");
        await (await QuickRecapAsync(anon, id.ToString())).AssertProblemAsync(HttpStatusCode.Unauthorized, "unauthenticated");
    }

    // ---------- ownership (C-09): another user and a missing session are indistinguishable ----------

    [Fact]
    public async Task Another_users_summary_is_404_with_the_same_body_as_a_session_that_does_not_exist()
    {
        var (alice, id) = await LiveSessionAsync();
        await SeedSummaryAsync(id, alice.UserId, 1);
        var bob = await NewActorAsync();

        var theirs = await bob.Client.GetAsync($"/api/v1/sessions/{id}/summary");
        var missing = await bob.Client.GetAsync($"/api/v1/sessions/{Guid.NewGuid()}/summary");
        await theirs.AssertProblemAsync(HttpStatusCode.NotFound, "not_found");
        await missing.AssertProblemAsync(HttpStatusCode.NotFound, "not_found");
        Assert.Equal(StripTrace(await theirs.BodyAsync()), StripTrace(await missing.BodyAsync()));
        Assert.Equal(HttpStatusCode.OK, (await alice.Client.GetAsync($"/api/v1/sessions/{id}/summary")).StatusCode); // control: the owner can read it
    }

    [Fact]
    public async Task Another_users_quick_recap_is_404_creates_nothing_and_looks_like_a_missing_session()
    {
        var (alice, id) = await LiveSessionAsync();
        await SeedFinalsAsync(id, alice.UserId);
        var bob = await NewActorAsync();

        var theirs = await QuickRecapAsync(bob.Client, id);
        var missing = await QuickRecapAsync(bob.Client, Guid.NewGuid().ToString());
        await theirs.AssertProblemAsync(HttpStatusCode.NotFound, "not_found");
        await missing.AssertProblemAsync(HttpStatusCode.NotFound, "not_found");
        Assert.Equal(StripTrace(await theirs.BodyAsync()), StripTrace(await missing.BodyAsync()));
        Assert.Equal(0, await CatchUpCountAsync(id));
        Assert.Equal(0, await F.ScalarAsync($"select count(*) from ai.ai_usage where user_id = '{bob.UserId}'"));
    }

    [Fact]
    public async Task Another_users_catch_up_is_404_and_so_is_one_that_belongs_to_a_different_session_of_the_same_user()
    {
        var (alice, id) = await LiveSessionAsync();
        await SeedFinalsAsync(id, alice.UserId);
        var created = await (await QuickRecapAsync(alice.Client, id)).BodyAsync();
        var cuId = created.GetProperty("catchUpId").GetString()!;
        Assert.Equal(HttpStatusCode.OK, (await alice.Client.GetAsync($"/api/v1/sessions/{id}/catch-ups/{cuId}")).StatusCode);

        var bob = await NewActorAsync();
        await (await bob.Client.GetAsync($"/api/v1/sessions/{id}/catch-ups/{cuId}")).AssertProblemAsync(HttpStatusCode.NotFound, "not_found");

        Assert.True((await PostAsync(alice.Client, $"/sessions/{id}/stop")).IsSuccessStatusCode);
        var (_, otherSession) = await LiveSessionAsync(alice);
        await (await alice.Client.GetAsync($"/api/v1/sessions/{otherSession}/catch-ups/{cuId}")).AssertProblemAsync(HttpStatusCode.NotFound, "not_found");
    }

    [Theory]
    [InlineData("nope")]
    [InlineData("cu_123")]
    [InlineData("cu_zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")]
    public async Task A_malformed_catch_up_id_is_404_not_400(string cuId)
    {
        var (a, id) = await LiveSessionAsync();
        await (await a.Client.GetAsync($"/api/v1/sessions/{id}/catch-ups/{cuId}")).AssertProblemAsync(HttpStatusCode.NotFound, "not_found");
    }

    // ---------- summary reads ----------

    [Fact]
    public async Task Summary_returns_the_latest_or_a_named_revision_matching_the_contract_and_marked_as_mock()
    {
        var (a, id) = await LiveSessionAsync();
        await SeedSummaryAsync(id, a.UserId, 1);
        await SeedSummaryAsync(id, a.UserId, 2);

        var latest = await (await a.Client.GetAsync($"/api/v1/sessions/{id}/summary")).BodyAsync();
        ContractSchemas.AssertValid("summary-revision", latest);
        Assert.Equal(2, latest.GetProperty("revision").GetInt32());
        Assert.Equal("mock", latest.GetProperty("source").GetString()); // B3-08: never presented as a model's work

        var first = await (await a.Client.GetAsync($"/api/v1/sessions/{id}/summary?revision=1")).BodyAsync();
        Assert.Equal(1, first.GetProperty("revision").GetInt32());
        await (await a.Client.GetAsync($"/api/v1/sessions/{id}/summary?revision=99")).AssertProblemAsync(HttpStatusCode.NotFound, "not_found");
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-3")]
    public async Task A_non_positive_revision_is_a_validation_error(string revision)
    {
        var (a, id) = await LiveSessionAsync();
        await (await a.Client.GetAsync($"/api/v1/sessions/{id}/summary?revision={revision}")).AssertProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
    }

    [Fact]
    public async Task Summary_of_a_session_with_no_revisions_is_404()
    {
        var (a, id) = await LiveSessionAsync();
        await (await a.Client.GetAsync($"/api/v1/sessions/{id}/summary")).AssertProblemAsync(HttpStatusCode.NotFound, "not_found");
    }

    // ---------- request validation ----------

    [Fact]
    public async Task Quick_recap_rejects_a_wrong_content_type_extra_fields_and_bad_windows()
    {
        var (a, id) = await LiveSessionAsync();
        await SeedFinalsAsync(id, a.UserId);
        await (await a.Client.SendAsync(QuickRecap(id, contentType: "text/plain"))).AssertProblemAsync(HttpStatusCode.UnsupportedMediaType, "unsupported_media_type");
        await (await QuickRecapAsync(a.Client, id, new { windowMinutes = 5, extra = 1 })).AssertProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        await (await QuickRecapAsync(a.Client, id, new { windowMinutes = 3 })).AssertProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        await (await QuickRecapAsync(a.Client, id, new { windowMinutes = "5" })).AssertProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        await (await QuickRecapAsync(a.Client, id, new { })).AssertProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        Assert.Equal(0, await CatchUpCountAsync(id));
    }

    [Theory]
    [InlineData("has space")]
    [InlineData("semi;colon")]
    [InlineData("0123456789012345678901234567890123456789012345678901234567890123456789")] // 70 chars
    public async Task A_malformed_idempotency_key_is_a_validation_error(string key)
    {
        var (a, id) = await LiveSessionAsync();
        await (await QuickRecapAsync(a.Client, id, key: key)).AssertProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        Assert.Equal(0, await CatchUpCountAsync(id));
    }

    [Fact]
    public async Task Quick_recap_on_a_stopped_session_is_409()
    {
        var (a, id) = await LiveSessionAsync();
        Assert.True((await PostAsync(a.Client, $"/sessions/{id}/stop")).IsSuccessStatusCode);
        await (await QuickRecapAsync(a.Client, id)).AssertProblemAsync(HttpStatusCode.Conflict, "invalid_state");
    }

    // ---------- CSRF / origin ----------

    [Fact]
    public async Task Quick_recap_without_an_origin_or_from_a_foreign_origin_is_rejected_and_creates_nothing()
    {
        var (a, id) = await LiveSessionAsync();
        await SeedFinalsAsync(id, a.UserId);

        await (await NoOriginClient(a).SendAsync(QuickRecap(id))).AssertProblemAsync(HttpStatusCode.Forbidden, "csrf_failed");

        var foreign = QuickRecap(id);
        foreign.Headers.Remove("Origin");
        foreign.Headers.Add("Origin", "https://evil.example");
        await (await a.Client.SendAsync(foreign)).AssertProblemAsync(HttpStatusCode.Forbidden, "csrf_failed");

        Assert.Equal(0, await CatchUpCountAsync(id));
        Assert.Equal(0, await F.ScalarAsync($"select count(*) from ai.ai_usage where user_id = '{a.UserId}'"));
    }

    [Fact]
    public async Task The_get_routes_need_no_origin()
    {
        var (a, id) = await LiveSessionAsync();
        await SeedSummaryAsync(id, a.UserId, 1);
        Assert.Equal(HttpStatusCode.OK, (await NoOriginClient(a).GetAsync($"/api/v1/sessions/{id}/summary")).StatusCode);
    }

    // ---------- idempotency ----------

    [Fact]
    public async Task A_replayed_idempotency_key_returns_the_stored_catch_up_and_says_so()
    {
        var (a, id) = await LiveSessionAsync();
        await SeedFinalsAsync(id, a.UserId);
        var first = await QuickRecapAsync(a.Client, id, key: "k-replay-1");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.False(first.Headers.Contains("Idempotency-Replayed"));
        var second = await QuickRecapAsync(a.Client, id, key: "k-replay-1");
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal("true", second.Headers.GetValues("Idempotency-Replayed").Single());
        Assert.Equal((await first.BodyAsync()).GetProperty("catchUpId").GetString(), (await second.BodyAsync()).GetProperty("catchUpId").GetString());
        Assert.Equal(1, await CatchUpCountAsync(id));
    }

    [Fact]
    public async Task The_same_key_with_a_different_window_is_a_conflict_and_changes_nothing()
    {
        var (a, id) = await LiveSessionAsync();
        await SeedFinalsAsync(id, a.UserId);
        Assert.Equal(HttpStatusCode.OK, (await QuickRecapAsync(a.Client, id, new { windowMinutes = 5 }, "k-conflict")).StatusCode);
        await (await QuickRecapAsync(a.Client, id, new { windowMinutes = 10 }, "k-conflict")).AssertProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        Assert.Equal(1, await CatchUpCountAsync(id));
    }

    [Fact]
    public async Task Keys_are_scoped_to_the_user_so_another_user_cannot_replay_or_collide()
    {
        var (alice, aliceSession) = await LiveSessionAsync();
        await SeedFinalsAsync(aliceSession, alice.UserId);
        var (bob, bobSession) = await LiveSessionAsync();
        await SeedFinalsAsync(bobSession, bob.UserId);
        var a = await (await QuickRecapAsync(alice.Client, aliceSession, key: "shared-key")).BodyAsync();
        var bRes = await QuickRecapAsync(bob.Client, bobSession, key: "shared-key");
        Assert.Equal(HttpStatusCode.OK, bRes.StatusCode);
        Assert.False(bRes.Headers.Contains("Idempotency-Replayed"));
        Assert.NotEqual(a.GetProperty("catchUpId").GetString(), (await bRes.BodyAsync()).GetProperty("catchUpId").GetString());
    }

    [Fact]
    public async Task Concurrent_requests_with_the_same_key_make_exactly_one_catch_up_and_all_get_it()
    {
        var (a, id) = await LiveSessionAsync();
        await SeedFinalsAsync(id, a.UserId);
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => QuickRecapAsync(a.Client, id, key: "k-race")));
        Assert.All(results, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        var ids = await Task.WhenAll(results.Select(async r => (await r.BodyAsync()).GetProperty("catchUpId").GetString()));
        Assert.Single(ids.Distinct());
        Assert.Equal(1, await CatchUpCountAsync(id));
        Assert.Equal(5, results.Count(r => r.Headers.Contains("Idempotency-Replayed")));
    }

    // ---------- B3-08 on the catch-up wire ----------

    [Fact]
    public async Task An_ai_catch_up_from_the_mock_says_so_and_the_extractive_one_carries_no_source()
    {
        var (a, id) = await LiveSessionAsync();
        await SeedFinalsAsync(id, a.UserId);
        var created = await (await QuickRecapAsync(a.Client, id)).BodyAsync();
        Assert.False(created.TryGetProperty("source", out _)); // extractive text is not model output
        var cuId = created.GetProperty("catchUpId").GetString()!;
        JsonElement final = default;
        for (var i = 0; i < 100; i++)
        {
            final = await (await a.Client.GetAsync($"/api/v1/sessions/{id}/catch-ups/{cuId}")).BodyAsync();
            if (final.GetProperty("aiState").GetString() != "Pending") break;
            await Task.Delay(50);
        }
        Assert.Equal("Ready", final.GetProperty("aiState").GetString());
        Assert.Equal("mock", final.GetProperty("source").GetString());
    }

    [Fact]
    public async Task Real_catch_up_responses_validate_against_the_catchup_schema_before_and_after_the_ai_finishes()
    {
        var (a, id) = await LiveSessionAsync();
        await SeedFinalsAsync(id, a.UserId);
        var created = await (await QuickRecapAsync(a.Client, id)).BodyAsync();
        ContractSchemas.AssertValid("catchup", created); // POST body (extractive, aiState Pending)
        var cuId = created.GetProperty("catchUpId").GetString()!;
        JsonElement final = default;
        for (var i = 0; i < 100; i++)
        {
            final = await (await a.Client.GetAsync($"/api/v1/sessions/{id}/catch-ups/{cuId}")).BodyAsync();
            if (final.GetProperty("aiState").GetString() != "Pending") break;
            await Task.Delay(50);
        }
        Assert.Equal("Ready", final.GetProperty("aiState").GetString());
        Assert.True(final.TryGetProperty("nowDiscussing", out _) && final.TryGetProperty("keyTerms", out _) && final.TryGetProperty("confidence", out _)); // the rich fields are really on the wire
        ContractSchemas.AssertValid("catchup", final);
        var bogus = System.Text.Json.Nodes.JsonNode.Parse(final.GetRawText())!.AsObject();
        bogus["aiState"] = "Bogus";
        Assert.False(ContractSchemas.IsValid("catchup", bogus.ToJsonString())); // negative control: the validator is live
    }

    // ---------- B3-04 ----------

    [Fact]
    public async Task A_pending_catch_up_older_than_the_stale_limit_reads_back_as_unavailable()
    {
        var (a, id) = await LiveSessionAsync();
        await SeedFinalsAsync(id, a.UserId);
        var created = await (await QuickRecapAsync(a.Client, id)).BodyAsync();
        var cuId = created.GetProperty("catchUpId").GetString()!;
        var guid = cuId[3..];
        await WaitNotPendingAsync(Guid.ParseExact(guid, "N"));
        // Simulate a job that was lost (restart, crash): back to Pending, created long ago.
        await F.ExecAsync($"update ai.catch_ups set ai_state = 'Pending', kind = 'Extractive', created_at = now() - interval '1 hour', " +
                          $"result = jsonb_set(result, '{{aiState}}', '\"Pending\"') where id = '{Guid.ParseExact(guid, "N")}'");
        var read = await (await a.Client.GetAsync($"/api/v1/sessions/{id}/catch-ups/{cuId}")).BodyAsync();
        Assert.Equal("Unavailable", read.GetProperty("aiState").GetString());
        Assert.Equal("Unavailable", await F.TextAsync($"select ai_state from ai.catch_ups where id = '{Guid.ParseExact(guid, "N")}'"));
    }

    [Fact]
    public async Task A_stale_pending_catch_up_does_not_block_a_new_request_or_get_replayed_as_pending()
    {
        var (a, id) = await LiveSessionAsync();
        await SeedFinalsAsync(id, a.UserId);
        var first = await (await QuickRecapAsync(a.Client, id, key: "k-stale")).BodyAsync();
        var gid = Guid.ParseExact(first.GetProperty("catchUpId").GetString()![3..], "N");
        await WaitNotPendingAsync(gid);
        await F.ExecAsync($"update ai.catch_ups set ai_state = 'Pending', created_at = now() - interval '1 hour', result = jsonb_set(result, '{{aiState}}', '\"Pending\"') where id = '{gid}'");

        var replay = await (await QuickRecapAsync(a.Client, id, key: "k-stale")).BodyAsync();
        Assert.Equal("Unavailable", replay.GetProperty("aiState").GetString());
        var fresh = await QuickRecapAsync(a.Client, id);
        Assert.Equal(HttpStatusCode.OK, fresh.StatusCode);
        Assert.NotEqual(first.GetProperty("catchUpId").GetString(), (await fresh.BodyAsync()).GetProperty("catchUpId").GetString());
    }

    [Fact]
    public async Task At_startup_every_orphaned_pending_catch_up_is_marked_unavailable()
    {
        var (a, id) = await LiveSessionAsync();
        await SeedFinalsAsync(id, a.UserId);
        var created = await (await QuickRecapAsync(a.Client, id)).BodyAsync();
        var gid = Guid.ParseExact(created.GetProperty("catchUpId").GetString()![3..], "N");
        await WaitNotPendingAsync(gid);
        await F.ExecAsync($"update ai.catch_ups set ai_state = 'Pending', kind = 'Extractive', result = jsonb_set(result, '{{aiState}}', '\"Pending\"') where id = '{gid}'");

        var swept = await F.Services.GetRequiredService<CatchUpRunner>().SweepOrphansAsync(CancellationToken.None);
        Assert.True(swept >= 1);
        Assert.Equal(0, await F.ScalarAsync("select count(*) from ai.catch_ups where ai_state = 'Pending'"));
        var read = await (await a.Client.GetAsync($"/api/v1/sessions/{id}/catch-ups/cu_{gid:N}")).BodyAsync();
        Assert.Equal("Unavailable", read.GetProperty("aiState").GetString());
    }

    private static string StripTrace(JsonElement e)
    {
        var d = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(e.GetRawText())!;
        d.Remove("traceId");
        return JsonSerializer.Serialize(d.OrderBy(k => k.Key).ToDictionary(k => k.Key, k => k.Value));
    }
}

/// <summary>Mock latency keeps the AI half of a catch-up Pending long enough to race requests against it.</summary>
public sealed class SlowAiFactory : ApiFactory
{
    protected override IEnumerable<KeyValuePair<string, string?>> Settings => [new("Ai:Mock:LatencyMs", "2500")];
}

public class AiRouteInFlightTests(SlowAiFactory factory) : AiRouteTestBase(factory), IClassFixture<SlowAiFactory>
{
    [Fact]
    public async Task Parallel_requests_without_a_key_share_one_in_flight_catch_up_and_one_charge()
    {
        var (a, id) = await LiveSessionAsync();
        await SeedFinalsAsync(id, a.UserId);
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => QuickRecapAsync(a.Client, id)));
        Assert.All(results, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        var ids = await Task.WhenAll(results.Select(async r => (await r.BodyAsync()).GetProperty("catchUpId").GetString()));
        Assert.Single(ids.Distinct());
        Assert.Equal(1, await CatchUpCountAsync(id));
    }

    [Fact]
    public async Task A_different_window_is_a_separate_catch_up()
    {
        var (a, id) = await LiveSessionAsync();
        await SeedFinalsAsync(id, a.UserId);
        await Task.WhenAll(new[] { 2, 5, 10, 5, 2 }.Select(w => QuickRecapAsync(a.Client, id, new { windowMinutes = w })));
        Assert.Equal(3, await CatchUpCountAsync(id));
    }

    [Fact]
    public async Task Deleting_the_session_while_the_job_is_in_flight_leaves_no_catch_up_or_summary_rows()
    {
        var (a, id) = await LiveSessionAsync();
        await SeedFinalsAsync(id, a.UserId);
        await SeedSummaryAsync(id, a.UserId, 1);
        var created = await QuickRecapAsync(a.Client, id);
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        Assert.Equal("Pending", (await created.BodyAsync()).GetProperty("aiState").GetString());

        Assert.Equal(HttpStatusCode.NoContent, (await a.Client.DeleteAsync($"/api/v1/sessions/{id}")).StatusCode);
        await Task.Delay(TimeSpan.FromSeconds(4)); // the slow job finishes and tries to store its result

        Assert.Equal(0, await CatchUpCountAsync(id));
        Assert.Equal(0, await F.ScalarAsync($"select count(*) from ai.summary_revisions where session_id = '{id}'"));
        await (await a.Client.GetAsync($"/api/v1/sessions/{id}/summary")).AssertProblemAsync(HttpStatusCode.NotFound, "not_found");
    }
}

/// <summary>Two per minute per session, so the third request within a minute is refused.</summary>
public sealed class LimitedAiFactory : ApiFactory
{
    protected override IEnumerable<KeyValuePair<string, string?>> Settings => [new("Ai:CatchUp:PerMinute", "2")];
}

public class AiRouteRateLimitTests(LimitedAiFactory factory) : AiRouteTestBase(factory), IClassFixture<LimitedAiFactory>
{
    [Fact]
    public async Task The_per_session_per_minute_limit_gives_429_and_other_sessions_and_the_next_minute_are_unaffected()
    {
        var (a, id) = await LiveSessionAsync();
        await SeedFinalsAsync(id, a.UserId);
        Assert.Equal(HttpStatusCode.OK, (await QuickRecapAsync(a.Client, id, new { windowMinutes = 2 })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await QuickRecapAsync(a.Client, id, new { windowMinutes = 5 })).StatusCode);
        await (await QuickRecapAsync(a.Client, id, new { windowMinutes = 10 })).AssertProblemAsync(HttpStatusCode.TooManyRequests, "rate_limited");
        Assert.Equal(2, await CatchUpCountAsync(id));

        var (b, other) = await LiveSessionAsync();
        await SeedFinalsAsync(other, b.UserId);
        Assert.Equal(HttpStatusCode.OK, (await QuickRecapAsync(b.Client, other, new { windowMinutes = 2 })).StatusCode);

        F.Clock.Advance(TimeSpan.FromSeconds(61));
        Assert.Equal(HttpStatusCode.OK, (await QuickRecapAsync(a.Client, id, new { windowMinutes = 10 })).StatusCode);
    }

}
