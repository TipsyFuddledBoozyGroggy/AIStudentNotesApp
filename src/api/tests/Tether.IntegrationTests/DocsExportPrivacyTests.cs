using System.Net;
using System.Text;
using System.Text.Json;
using Tether.Ai.Provider;
using Tether.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Tether.IntegrationTests;

/// <summary>Every logging category at Trace, Npgsql echoing row detail, the Development-only fault flag on (so the failure paths can be driven).</summary>
public sealed class DocsCanaryFactory : ApiFactory
{
    public LogCanaryTests.CapturingProvider Logs { get; } = new();
    protected override int MaxPoolSize => RacePool.Size;
    protected override IEnumerable<KeyValuePair<string, string?>> Settings =>
        [.. LogPipelineCanary.TraceEverything(ConnectionString), new("Docs:Demo:Faults:Enabled", "true")];

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(s => s.AddSingleton<ILoggerProvider>(Logs));
    }
}

/// <summary>The shipped limits for the Docs buckets, a small shared write bucket, a 1 second body deadline, and NO fault flag.</summary>
public sealed class DocsLimitsFactory : ApiFactory
{
    protected override int MaxPoolSize => RacePool.Size;
    protected override IEnumerable<KeyValuePair<string, string?>> Settings =>
    [
        new("RateLimiting:DocsReadPermitPerMinute", "60"), new("RateLimiting:DocsWritePermitPerMinute", "10"),
        new("RateLimiting:WritePermitPerMinute", "6"), new("Docs:BodyReadSeconds", "1"),
    ];
}

/// <summary>DX-B09 (B40 to B43) and B32a: logs, the audit table, the AI boundary, the rate buckets, the body limits and the fault flag being off.</summary>
[Collection(RacePool.Name)]
public class DocsExportPrivacyTests(DocsCanaryFactory factory) : DocsExportTestBase(factory), IClassFixture<DocsCanaryFactory>
{
    private DocsCanaryFactory Fx => (DocsCanaryFactory)F;
    private const string TitleCanary = "CANARY-DOCS-TITLE-8c21";
    private const string BulletCanary = "CANARY-DOCS-BULLET-5e90";

    [Fact]
    public async Task B40_B41_Titles_bullets_digests_and_tokens_never_reach_a_log_or_the_audit_table_on_any_path()
    {
        var a = await NewActorAsync();
        var (_, sid) = await StoppedSessionAsync(a, finals: 3);
        Assert.Equal(HttpStatusCode.Accepted, (await PressAsync(a.Client, sid)).StatusCode);
        var notes = await WaitSettledAsync(a.Client, sid);
        var notesId = notes.GetProperty("notesId").GetString()!;
        await F.ExecAsync($"update lectures.lecture_sessions set title = 'Lecture {TitleCanary}' where id = '{sid}'");
        await SetNotesAsync(sid, Sections(("Section " + BulletCanary, [("Bullet " + BulletCanary, [1000L])])));

        var q = "?access_token=" + LogPipelineCanary.QueryToken;
        HttpRequestMessage WithSecrets(HttpRequestMessage r)
        {
            r.Headers.TryAddWithoutValidation("Authorization", "Bearer " + LogPipelineCanary.FakeToken);
            return r;
        }
        var preview = await a.Client.SendAsync(WithSecrets(Req(HttpMethod.Post, $"/sessions/{sid}/docs-export/preview{q}", new { notesId })));
        var p = await preview.BodyAsync();
        DocsSchemas.AssertValid("docs-preview", p);
        var digest = p.GetProperty("contentDigest").GetString()!;
        Assert.Contains(TitleCanary, p.GetRawText());                      // the canary really is in the document, so a leak would be possible
        var body = ConfirmBody(p, "create");

        // 400, 404, 412, 413, 415, provider failure, success, replay, state, read, delete: every path
        await a.Client.SendAsync(WithSecrets(Req(HttpMethod.Post, $"/sessions/{sid}/docs-export{q}", "{" + LogPipelineCanary.LectureText, NewKey())));
        await a.Client.SendAsync(WithSecrets(Req(HttpMethod.Post, $"/sessions/{Guid.NewGuid()}/docs-export{q}", body, NewKey())));
        var stale = new Dictionary<string, object?>(body) { ["contentDigest"] = "cd_" + new string('C', 43) };
        await (await a.Client.SendAsync(WithSecrets(Req(HttpMethod.Post, $"/sessions/{sid}/docs-export{q}", stale, NewKey())))).AssertProblemAsync(HttpStatusCode.PreconditionFailed, "precondition_failed");
        var big = Req(HttpMethod.Post, $"/sessions/{sid}/docs-export{q}", "{\"pad\":\"" + new string('p', 3000) + BulletCanary + "\"}", NewKey());
        await (await a.Client.SendAsync(WithSecrets(big))).AssertProblemAsync(HttpStatusCode.RequestEntityTooLarge, "too_large");
        var text = Req(HttpMethod.Post, $"/sessions/{sid}/docs-export{q}", null, NewKey());
        text.Content = new StringContent(BulletCanary, Encoding.UTF8, "text/plain");
        await (await a.Client.SendAsync(WithSecrets(text))).AssertProblemAsync(HttpStatusCode.UnsupportedMediaType, "unsupported_media_type");
        var key = NewKey();
        var failed = await OkRecordAsync(await a.Client.SendAsync(WithSecrets(Req(HttpMethod.Post, $"/sessions/{sid}/docs-export{q}", body, key, "unavailable"))));
        Assert.Equal("failed", failed.GetProperty("status").GetString());
        var rec = await OkRecordAsync(await a.Client.SendAsync(WithSecrets(Req(HttpMethod.Post, $"/sessions/{sid}/docs-export{q}", body, key))));
        Assert.Equal("created", rec.GetProperty("outcome").GetString());
        Assert.True(Replayed(await a.Client.SendAsync(WithSecrets(Req(HttpMethod.Post, $"/sessions/{sid}/docs-export{q}", body, key)))));
        await a.Client.SendAsync(WithSecrets(Req(HttpMethod.Get, $"/sessions/{sid}/docs-export{q}")));
        var doc = await a.Client.SendAsync(WithSecrets(Req(HttpMethod.Get, $"/demo-docs/{DocIdOf(rec)}{q}")));
        Assert.Contains(BulletCanary, await doc.Content.ReadAsStringAsync());
        var bob = await NewActorAsync();
        await bob.Client.SendAsync(WithSecrets(Req(HttpMethod.Get, $"/demo-docs/{DocIdOf(rec)}{q}")));
        await bob.Client.SendAsync(WithSecrets(Req(HttpMethod.Delete, $"/demo-docs/{DocIdOf(rec)}{q}")));
        Assert.Equal(HttpStatusCode.NoContent, (await a.Client.SendAsync(WithSecrets(Req(HttpMethod.Delete, $"/demo-docs/{DocIdOf(rec)}{q}")))).StatusCode);
        await Task.Delay(300);

        var lines = Fx.Logs.Lines.ToArray();
        LogPipelineCanary.AssertClean(lines, requireDbCategories: true);
        foreach (var secret in new[] { TitleCanary, BulletCanary, digest, "cd_" + new string('C', 43) })
        {
            var bad = lines.Where(l => l.Contains(secret, StringComparison.Ordinal)).Select(l => l.Length > 200 ? l[..200] : l).ToArray();
            Assert.True(bad.Length == 0, $"a secret leaked into {bad.Length} log record(s), first: {bad.FirstOrDefault()}");
        }
        Assert.Contains(lines, l => l.StartsWith("Tether.Docs.", StringComparison.Ordinal)); // the module does log (codes and counts), so the absence above means something

        // B41: the audit row has exactly the content-free columns, and no stored row outside doc_exports/demo_docs carries the title or text
        var cols = new List<string>();
        await using (var conn = new Npgsql.NpgsqlConnection(F.ConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = new Npgsql.NpgsqlCommand("select column_name from information_schema.columns where table_schema = 'docs' and table_name = 'audit_events' order by column_name", conn);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) cols.Add(r.GetString(0));
        }
        Assert.Equal(new[] { "action", "at", "attempt", "block_count", "error_code", "export_id", "id", "notes_revision", "provider", "session_id", "user_id" }.Order(StringComparer.Ordinal), cols.Order(StringComparer.Ordinal));
        foreach (var t in new[] { "audit_events", "idempotency_keys", "doc_targets" })
            Assert.Equal(0, await F.ScalarAsync($"select count(*) from docs.{t} x where x::text like '%{TitleCanary}%' or x::text like '%{BulletCanary}%' or x::text like '%{digest}%'"));
        Assert.Equal(0, await F.ScalarAsync($"select count(*) from docs.doc_exports x where x::text like '%{TitleCanary}%' or x::text like '%{BulletCanary}%'"));
        Assert.True(await F.ScalarAsync($"select count(*) from docs.audit_events where user_id = '{a.UserId}' and action = 'export_failed'") >= 1);
        Assert.True(await F.ScalarAsync($"select count(*) from docs.audit_events where user_id = '{a.UserId}' and action = 'doc_deleted'") >= 1);
    }

    [Fact]
    public async Task B42_Neither_preview_nor_confirm_calls_the_AI_or_spends_budget()
    {
        var l = await ReadyAsync();
        var mock = F.Services.GetRequiredService<MockAiClient>();
        var calls = mock.Calls;
        var usage = await F.ScalarAsync($"select count(*) from ai.ai_usage where user_id = '{l.A.UserId}'");
        var rec = await ExportOnceAsync(l);
        Assert.Equal("created", rec.GetProperty("outcome").GetString());
        await F.ExecAsync($"update lectures.lecture_sessions set title = 'Again' where id = '{l.SessionId}'");
        var p = await PreviewOkAsync(l.A.Client, l.SessionId, l.NotesId);
        await OkRecordAsync(await ConfirmAsync(l.A.Client, l.SessionId, ConfirmBody(p, "update")));
        await Task.Delay(200);
        Assert.Equal(calls, mock.Calls);
        Assert.Equal(usage, await F.ScalarAsync($"select count(*) from ai.ai_usage where user_id = '{l.A.UserId}'"));
    }
}

[Collection(RacePool.Name)]
public class DocsExportLimitsTests(DocsLimitsFactory factory) : DocsExportTestBase(factory), IClassFixture<DocsLimitsFactory>
{
    private static readonly object BadBody = new { };

    private static HttpRequestMessage BadConfirm(string sid) => Req(HttpMethod.Post, $"/sessions/{sid}/docs-export", BadBody, "k" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task B43_Confirms_have_their_own_bucket_of_10_a_minute_the_11th_is_429_with_Retry_After_and_nothing_else_is_starved()
    {
        var l = await ReadyAsync();
        for (var i = 0; i < 10; i++)
            await (await l.A.Client.SendAsync(BadConfirm(l.SessionId))).AssertProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        var limited = await l.A.Client.SendAsync(BadConfirm(l.SessionId));
        await limited.AssertProblemAsync(HttpStatusCode.TooManyRequests, "rate_limited");
        Assert.True(limited.Headers.Contains("Retry-After"));
        DocsSchemas.AssertValid("docs-problem", await limited.BodyAsync());
        // the delete shares the write bucket; the reads and the shared write bucket (course creation) are untouched
        Assert.Equal(HttpStatusCode.TooManyRequests, (await l.A.Client.SendAsync(Req(HttpMethod.Delete, $"/demo-docs/ddc_{Guid.NewGuid():D}"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PreviewAsync(l.A.Client, l.SessionId, l.NotesId)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await l.A.Client.GetAsync($"{Api}/sessions/{l.SessionId}/docs-export")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await l.A.Client.PostAsync($"{Api}/courses", Json("{\"name\":\"Still allowed\"}"))).StatusCode);
    }

    [Fact]
    public async Task B43_Previews_and_reads_have_their_own_bucket_and_cannot_exhaust_the_write_bucket_or_the_shared_one()
    {
        var l = await ReadyAsync();
        for (var i = 0; i < 60; i++)
            Assert.Equal(HttpStatusCode.OK, (await l.A.Client.GetAsync($"{Api}/sessions/{l.SessionId}/docs-export")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await l.A.Client.GetAsync($"{Api}/sessions/{l.SessionId}/docs-export")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await PreviewAsync(l.A.Client, l.SessionId, l.NotesId)).StatusCode);   // the preview shares the read bucket
        // confirm (docs-write) and the shared write bucket still answer
        await (await l.A.Client.SendAsync(BadConfirm(l.SessionId))).AssertProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        Assert.Equal(HttpStatusCode.Created, (await l.A.Client.PostAsync($"{Api}/courses", Json("{\"name\":\"Still allowed\"}"))).StatusCode);
    }

    [Fact]
    public async Task B43_The_shared_write_bucket_that_Quick_recap_uses_can_be_full_while_confirm_and_preview_still_work()
    {
        var l = await ReadyAsync();    // a few of the 6 shared writes are used (course, start, stop)
        var refused = false;
        for (var i = 0; i < 8 && !refused; i++)
            refused = (await l.A.Client.PostAsync($"{Api}/courses", Json("{\"name\":\"fill " + i + "\"}"))).StatusCode == HttpStatusCode.TooManyRequests;
        Assert.True(refused, "the shared write bucket never filled");      // it is empty now
        var p = await PreviewOkAsync(l.A.Client, l.SessionId, l.NotesId);
        var rec = await OkRecordAsync(await ConfirmAsync(l.A.Client, l.SessionId, ConfirmBody(p, "create")));
        Assert.Equal("created", rec.GetProperty("outcome").GetString());
    }

    [Fact]
    public void B43_Every_Docs_route_carries_its_separate_rate_bucket_metadata_so_none_can_fall_into_the_shared_write_bucket()
    {
        _ = F.CreateClient();
        var routes = F.Services.GetRequiredService<Microsoft.AspNetCore.Routing.EndpointDataSource>().Endpoints.OfType<Microsoft.AspNetCore.Routing.RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText is { } t && (t.Contains("/docs-export", StringComparison.Ordinal) || t.Contains("/demo-docs", StringComparison.Ordinal))).ToList();
        Assert.Equal(5, routes.Count);
        foreach (var e in routes)
        {
            var method = e.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.HttpMethodMetadata>()!.HttpMethods.Single();
            var bucket = e.Metadata.GetMetadata<Tether.SharedKernel.Http.SeparateRateBucketMetadata>();
            Assert.NotNull(bucket);
            var writes = method == "DELETE" || (method == "POST" && !e.RoutePattern.RawText!.EndsWith("/preview", StringComparison.Ordinal));
            Assert.Equal(writes ? "docs-write" : "docs-read", bucket!.Bucket);
            Assert.Equal(writes ? 10 : 60, bucket.PermitsPerMinute);
        }
    }

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    [Fact]
    public async Task B43_A_body_over_2_KB_is_413_too_large_a_wrong_media_type_is_415_and_neither_leaks_for_a_session_that_is_not_yours()
    {
        var l = await ReadyAsync();
        var pad = "{\"notesId\":\"" + l.NotesId + "\",\"pad\":\"" + new string('p', 2100) + "\"}";
        var big = await l.A.Client.SendAsync(Req(HttpMethod.Post, $"/sessions/{l.SessionId}/docs-export/preview", pad));
        await big.AssertProblemAsync(HttpStatusCode.RequestEntityTooLarge, "too_large");
        DocsSchemas.AssertValid("docs-problem", await big.BodyAsync());
        // no Content-Length: the cap is counted while reading
        var chunked = new HttpRequestMessage(HttpMethod.Post, $"{Api}/sessions/{l.SessionId}/docs-export/preview") { Content = new UnknownLengthContent(Encoding.UTF8.GetBytes(pad)) };
        await (await l.A.Client.SendAsync(chunked)).AssertProblemAsync(HttpStatusCode.RequestEntityTooLarge, "too_large");

        var wrong = Req(HttpMethod.Post, $"/sessions/{l.SessionId}/docs-export/preview");
        wrong.Content = new StringContent("{}", Encoding.UTF8, "text/plain");
        var res = await l.A.Client.SendAsync(wrong);
        await res.AssertProblemAsync(HttpStatusCode.UnsupportedMediaType, "unsupported_media_type");
        DocsSchemas.AssertValid("docs-problem", await res.BodyAsync());

        var bob = await NewActorAsync();
        await (await bob.Client.SendAsync(Req(HttpMethod.Post, $"/sessions/{l.SessionId}/docs-export/preview", pad))).AssertProblemAsync(HttpStatusCode.NotFound, "not_found");
    }

    [Fact]
    public async Task B43_A_slow_body_trips_the_read_deadline()
    {
        var l = await ReadyAsync();
        var slow = new HttpRequestMessage(HttpMethod.Post, $"{Api}/sessions/{l.SessionId}/docs-export/preview") { Content = new SlowContent() };
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var res = await l.A.Client.SendAsync(slow);
        await res.AssertProblemAsync(HttpStatusCode.BadRequest, "validation_failed");   // the deadline is 1 s here; the client would have kept going for 4 s
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(3.5), $"the server waited {watch.Elapsed.TotalSeconds:F1} s for a body that never finished");
    }

    [Fact]
    public async Task B32a_With_the_fault_flag_off_the_fault_header_is_ignored_without_a_400_and_without_a_delay()
    {
        var l = await ReadyAsync();
        var p = await PreviewOkAsync(l.A.Client, l.SessionId, l.NotesId);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var res = await ConfirmAsync(l.A.Client, l.SessionId, ConfirmBody(p, "create"), fault: "unavailable");
        var rec = await OkRecordAsync(res);
        Assert.Equal("created", rec.GetProperty("outcome").GetString());     // not "failed": the header did nothing
        var slow = await ConfirmAsync(l.A.Client, l.SessionId, ConfirmBody(await PreviewOkAsync(l.A.Client, l.SessionId, l.NotesId), "update"), fault: "slow:5");
        Assert.Equal(HttpStatusCode.OK, slow.StatusCode);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(4), "the slow fault ran although the flag is off");
    }

    private sealed class UnknownLengthContent : HttpContent
    {
        private readonly byte[] _bytes;
        public UnknownLengthContent(byte[] bytes)
        {
            _bytes = bytes;
            Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        }
        protected override Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context) => stream.WriteAsync(_bytes, 0, _bytes.Length);
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
    }

    private sealed class SlowContent : HttpContent
    {
        public SlowContent() => Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        protected override async Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context)
        {
            await stream.WriteAsync("{\"notes"u8.ToArray());
            await stream.FlushAsync();
            await Task.Delay(4000);
            await stream.WriteAsync("Id\":\"x\"}"u8.ToArray());
        }
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
    }
}
