using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tether.Courses.Contracts;
using Tether.IntegrationTests.Infrastructure;
using Tether.Syllabus.Application;

namespace Tether.IntegrationTests.Syllabus;

/// <summary>
/// Fixes of docs/security/syllabus-built-review.md that need the real host: SB-02 (long line end to end), SB-04 (multipart and paste fingerprint),
/// SB-07 (export refuses cross-site reads), SB-08 (malformed text files are 415, never 500).
/// </summary>
public class SyllabusBuiltReviewFixTests(ApiFactory factory) : SyllabusTestBase(factory), IClassFixture<ApiFactory>
{
    internal static string Meta(string? title = null, bool omitTitle = true, string? termStart = "2026-08-24", string? termEnd = "2026-12-11", bool accepted = true,
        string? acceptedAt = null)
    {
        var d = new Dictionary<string, object?> { ["defaultTimezone"] = "America/New_York", ["noticeVersion"] = "draft-2026-10" };
        if (!omitTitle) d["title"] = title;
        if (termStart is not null) d["termStart"] = termStart;
        if (termEnd is not null) d["termEnd"] = termEnd;
        d["attestation"] = new { accepted, acceptedAt = acceptedAt ?? DateTimeOffset.UtcNow.ToString("O") };
        return JsonSerializer.Serialize(d);
    }

    internal static MultipartFormDataContent Form(byte[] file, string metadata, string fileName = "syllabus.txt", string partType = "text/plain")
    {
        var mp = new MultipartFormDataContent { { new StringContent(metadata, Encoding.UTF8, "application/json"), "metadata" } };
        var f = new ByteArrayContent(file);
        f.Headers.ContentType = MediaTypeHeaderValue.Parse(partType);
        mp.Add(f, "file", fileName);
        return mp;
    }

    internal static Task<HttpResponseMessage> Upload(HttpClient c, string course, HttpContent content, string key)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, $"{Api}/courses/{course}/syllabi") { Content = content };
        req.Headers.TryAddWithoutValidation("Idempotency-Key", key);
        return c.SendAsync(req);
    }

    // ---------------------------------------------------------------- SB-02

    [Fact]
    public async Task SB02_a_3000_character_line_with_dates_in_the_middle_is_ready_with_bounded_context_not_invalid_output()
    {
        var a = await NewActorAsync(); var course = await CourseAsync(a.Client);
        var line = new string('x', 1500) + " Midterm exam on Oct 19, 2026 at 10:00 AM, also see Nov 3, 2026 " + new string('y', 1440);
        Assert.True(line.Length >= 3000);
        var done = await CreateReadyAsync(a.Client, course, Paste(line));
        Assert.Equal("ready", done.GetProperty("status").GetString());                    // the old window (about 1,980 code points) failed it with invalid_output
        var g = Guid32(done.GetProperty("id").GetString()!);
        Assert.Equal(1, await F.ScalarAsync($"select count(*) from syllabus.items where syllabus_id = '{g}' and origin = 'ai'"));
        Assert.Equal(1500, await F.ScalarAsync($"select max(char_length(context_text)) from syllabus.items where syllabus_id = '{g}'"));
        Assert.Equal(1, await F.ScalarAsync($"select count(*) from syllabus.unclaimed_dates where syllabus_id = '{g}'"));
        var item = Assert.Single(await ItemsAsync(a.Client, done.GetProperty("id").GetString()!));
        SyllabusSchemas.AssertValid("syllabus-item", item);
    }

    // ---------------------------------------------------------------- SB-04

    [Fact]
    public async Task SB04_a_multipart_retry_with_any_different_metadata_is_400_and_only_the_identical_retry_replays()
    {
        var a = await NewActorAsync();
        var bytes = Encoding.UTF8.GetBytes("Quiz due Oct 20, 2026 at 9:00 AM");
        var at = DateTimeOffset.UtcNow.ToString("O");
        var cases = new (string Name, string First, string Retry)[]
        {
            // the separator collision of the old '|' join
            ("title '|' vs term '|'", Meta("X|ts=2026-08-24", omitTitle: false, termStart: null, termEnd: null, acceptedAt: at), Meta("X", omitTitle: false, termStart: "2026-08-24|ts=", termEnd: null, acceptedAt: at)),
            ("null title vs empty title", Meta(acceptedAt: at), Meta("", omitTitle: false, acceptedAt: at)),
            ("accepted true vs false", Meta(acceptedAt: at), Meta(accepted: false, acceptedAt: at)),
        };
        foreach (var (name, first, retry) in cases)
        {
            var course = await CourseAsync(a.Client, name);
            var key = NewKey();
            var r1 = await Upload(a.Client, course, Form(bytes, first), key);
            Assert.True(r1.StatusCode == HttpStatusCode.Accepted, name);
            await WaitDoneAsync(a.Client, (await r1.BodyAsync()).GetProperty("id").GetString()!);
            var r2 = await Upload(a.Client, course, Form(bytes, retry), key);
            Assert.False(r2.Headers.Contains("Idempotency-Replayed"), name);
            await r2.AssertProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
            // the identical retry is still a replay
            var r3 = await Upload(a.Client, course, Form(bytes, first), key);
            Assert.True(r3.StatusCode == HttpStatusCode.Accepted, name);
            Assert.Equal("true", r3.Headers.GetValues("Idempotency-Replayed").Single());
        }
        Assert.Equal(cases.Length, await F.ScalarAsync($"select count(*) from syllabus.syllabi where user_id = '{a.UserId}'"));
    }

    [Fact]
    public async Task SB04_a_paste_retry_with_attestation_accepted_false_is_400_not_a_replay()
    {
        var a = await NewActorAsync(); var course = await CourseAsync(a.Client);
        var key = NewKey(); var body = Paste("Quiz due Oct 20, 2026 at 9:00 AM");
        var r1 = await a.Client.SendAsync(Req(HttpMethod.Post, $"{Api}/courses/{course}/syllabi", body, key));
        Assert.Equal(HttpStatusCode.Accepted, r1.StatusCode);
        await WaitDoneAsync(a.Client, (await r1.BodyAsync()).GetProperty("id").GetString()!);
        var attestation = JsonSerializer.SerializeToElement(body["attestation"]);
        var changed = new Dictionary<string, object?>(body) { ["attestation"] = new { accepted = false, acceptedAt = attestation.GetProperty("acceptedAt").GetString() } };
        var r2 = await a.Client.SendAsync(Req(HttpMethod.Post, $"{Api}/courses/{course}/syllabi", changed, key));
        await r2.AssertProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        Assert.False(r2.Headers.Contains("Idempotency-Replayed"));
        Assert.Equal("true", (await a.Client.SendAsync(Req(HttpMethod.Post, $"{Api}/courses/{course}/syllabi", body, key))).Headers.GetValues("Idempotency-Replayed").Single());
    }

    // ---------------------------------------------------------------- SB-07

    [Fact]
    public async Task SB07_export_refuses_cross_site_and_same_site_requests_and_foreign_origins_and_writes_no_audit_for_them()
    {
        var a = await NewActorAsync(); var course = await CourseAsync(a.Client);
        var sid = (await CreateReadyAsync(a.Client, course, Paste("Midterm exam: Oct 19, 2026 at 10:00 AM"))).GetProperty("id").GetString()!;
        var g = Guid32(sid);
        HttpRequestMessage Export(string? site, string? origin = ApiFactory.Origin)
        {
            var r = new HttpRequestMessage(HttpMethod.Get, $"{Api}/syllabi/{sid}/export");
            if (site is not null) r.Headers.TryAddWithoutValidation("Sec-Fetch-Site", site);
            if (origin is not null) r.Headers.TryAddWithoutValidation("Origin", origin);
            return r;
        }
        foreach (var site in new[] { "cross-site", "same-site", "Same-Origin", "same-origin, cross-site" })
            await (await a.Client.SendAsync(Export(site))).AssertProblemAsync(HttpStatusCode.Forbidden, "csrf_failed");
        await (await a.Client.SendAsync(Export("same-origin", "https://evil.example"))).AssertProblemAsync(HttpStatusCode.Forbidden, "csrf_failed");
        await (await a.Client.SendAsync(Export(null, "null"))).AssertProblemAsync(HttpStatusCode.Forbidden, "csrf_failed");
        Assert.Equal(0, await F.ScalarAsync($"select count(*) from syllabus.decision_audit where syllabus_id = '{g}' and action = 'exported'"));

        // the web client's same-origin fetch, a typed address, and a client that sends no Fetch Metadata still work
        foreach (var site in new[] { "same-origin", "none", null })
        {
            var ok = await a.Client.SendAsync(Export(site));
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
            Assert.Equal(sid, (await ok.BodyAsync()).GetProperty("syllabus").GetProperty("id").GetString());
        }
        Assert.Equal(3, await F.ScalarAsync($"select count(*) from syllabus.decision_audit where syllabus_id = '{g}' and action = 'exported'"));
        // a foreign user's cross-site request is refused before the ownership lookup (same answer for every id)
        var b = await NewActorAsync();
        await (await b.Client.SendAsync(Export("cross-site"))).AssertProblemAsync(HttpStatusCode.Forbidden, "csrf_failed");
        await (await b.Client.SendAsync(Export("same-origin"))).AssertProblemAsync(HttpStatusCode.NotFound, "not_found");
    }

    // ---------------------------------------------------------------- SB-08

    [Fact]
    public async Task SB08_text_files_with_lone_surrogates_odd_length_utf32_or_cesu8_bytes_are_415_never_500_and_create_nothing()
    {
        var a = await NewActorAsync(); var course = await CourseAsync(a.Client);
        static byte[] Le(string s) => [0xFF, 0xFE, .. Encoding.Unicode.GetBytes(s)];
        static byte[] Be(string s) => [0xFE, 0xFF, .. Encoding.BigEndianUnicode.GetBytes(s)];
        var long1 = "Quiz due Oct 20, 2026 at 9:00 AM. This line pads the file past the 64-byte sniff window.";
        var cases = new (string Name, byte[] Bytes)[]
        {
            ("UTF-16LE lone high surrogate", [.. Le(long1), 0x00, 0xD8, .. Le("tail")[2..]]),
            ("UTF-16LE lone low surrogate", [.. Le(long1), 0x00, 0xDC, .. Le("tail")[2..]]),
            ("UTF-16BE lone high surrogate", [.. Be(long1), 0xD8, 0x00, .. Be("tail")[2..]]),
            ("UTF-16LE odd byte length", [.. Le(long1), (byte)'x']),
            ("UTF-16BE odd byte length", [.. Be(long1), (byte)'x']),
            ("UTF-32LE BOM", [0xFF, 0xFE, 0x00, 0x00, .. Encoding.UTF32.GetBytes(long1)]),
            ("UTF-32BE BOM", [0x00, 0x00, 0xFE, 0xFF, .. new UTF32Encoding(bigEndian: true, byteOrderMark: false).GetBytes(long1)]),
            ("CESU-8 astral character", [.. Encoding.UTF8.GetBytes(long1), 0xED, 0xA0, 0xBD, 0xED, 0xB8, 0x80]),
            ("UTF-8 encoded lone surrogate", [.. Encoding.UTF8.GetBytes(long1), 0xED, 0xA0, 0x80]),
        };
        foreach (var (name, bytes) in cases)
        {
            var res = await Upload(a.Client, course, Form(bytes, Meta()), NewKey());
            Assert.True(res.StatusCode == HttpStatusCode.UnsupportedMediaType, $"{name}: {(int)res.StatusCode}");
            await res.AssertProblemAsync(HttpStatusCode.UnsupportedMediaType, "unsupported_type");
        }
        Assert.Equal(0, await F.ScalarAsync($"select count(*) from syllabus.syllabi where user_id = '{a.UserId}'"));
        // control: a well-formed UTF-16BE file with an astral character is accepted
        var ok = await Upload(a.Client, course, Form(Be(long1 + " \U0001F600"), Meta()), NewKey());
        Assert.Equal(HttpStatusCode.Accepted, ok.StatusCode);
        Assert.Equal("ready", (await WaitDoneAsync(a.Client, (await ok.BodyAsync()).GetProperty("id").GetString()!)).GetProperty("status").GetString());
    }
}

/// <summary>Records every IAiScopeCutoff call the Syllabus module makes (stands in for the AI module's SessionCutoff).</summary>
public sealed class CutoffRecorder : Tether.SharedKernel.Ai.IAiScopeCutoff
{
    public ConcurrentQueue<(string Kind, Guid ScopeId)> Calls { get; } = new();
    public void Cancel(string kind, Guid scopeId) => Calls.Enqueue((kind, scopeId));
    public int CountFor(Guid id) => Calls.Count(c => c.Kind == Tether.SharedKernel.Ai.AiScopeKinds.Syllabus && c.ScopeId == id);
}

/// <summary>SB-05: the Syllabus course lifecycle handler. Each mock stage takes a while, so a course purge lands while a job is running.</summary>
public class SyllabusCoursePurgeTests(SyllabusCoursePurgeTests.Factory f) : SyllabusTestBase(f), IClassFixture<SyllabusCoursePurgeTests.Factory>
{
    public sealed class Factory : ApiFactory
    {
        public CutoffRecorder Cutoffs { get; } = new();
        public PurgeGap Gap { get; } = new();
        protected override IEnumerable<KeyValuePair<string, string?>> Settings => [new("Syllabus:Mock:StageDelayMilliseconds", "1500")];
        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(s =>
            {
                s.RemoveAll<Tether.SharedKernel.Ai.IAiScopeCutoff>();
                s.AddSingleton<Tether.SharedKernel.Ai.IAiScopeCutoff>(Cutoffs);
                s.AddSingleton(Gap);
                s.AddScoped<ICourseLifecycleHandler, PurgeGap.Handler>();   // after the module handlers: it blocks after the Syllabus pre-commit notice
            });
        }
    }

    private Factory P => (Factory)F;

    private async Task WaitStatusAsync(HttpClient c, string sid, string status)
    {
        var until = DateTime.UtcNow.AddSeconds(20);
        while ((await (await c.GetAsync($"{Api}/syllabi/{sid}")).BodyAsync()).GetProperty("status").GetString() != status)
        {
            Assert.True(DateTime.UtcNow < until, $"syllabus never reached {status}");
            await Task.Delay(30);
        }
    }

    private async Task AssertCourseLeavesNoSyllabusRowsAsync(Guid user)
    {
        foreach (var t in new[] { "syllabi", "canonical_texts", "extraction_jobs", "items", "item_proposals", "unclaimed_dates", "calendar_events", "sync_runs", "idempotency_keys" })
            Assert.Equal(0, await F.ScalarAsync($"select count(*) from syllabus.{t} where user_id = '{user}'"));
    }

    [Fact]
    public async Task A_purge_mid_job_cancels_the_job_and_its_ai_scope_audits_every_syllabus_and_leaves_nothing_behind()
    {
        var a = await NewActorAsync(); var course = await CourseAsync(a.Client);
        // a ready syllabus with a synced mock event ...
        var ready = (await CreateReadyAsync(a.Client, course, Paste("Midterm exam: Oct 19, 2026 at 10:00 AM"))).GetProperty("id").GetString()!;
        var mid = ByTitle(await ItemsAsync(a.Client, ready), "Midterm");
        var ap = await (await Decide(a.Client, ready, mid, "approved")).BodyAsync();
        Assert.Equal(HttpStatusCode.OK, (await Sync(a.Client, ready, NewKey(), (ap.GetProperty("id").GetString()!, ap.GetProperty("revision").GetInt32()))).StatusCode);
        Assert.Equal(1, await F.ScalarAsync($"select count(*) from syllabus.calendar_events where user_id = '{a.UserId}'"));
        // ... and one whose extraction job is running right now
        var running = (await (await CreateAsync(a.Client, course, Paste("Final exam: Dec 9, 2026 at 8:00 AM"))).BodyAsync()).GetProperty("id").GetString()!;
        await WaitStatusAsync(a.Client, running, "extracting");

        Assert.Equal(HttpStatusCode.NoContent, (await a.Client.DeleteAsync($"{Api}/courses/{course}?purge=true")).StatusCode);

        // job and AI scope cut off before the commit and again after it, for every syllabus of the course
        Assert.True(P.Cutoffs.CountFor(Guid.Parse(Guid32(running))) >= 2, "running syllabus: pre- and post-commit cutoff");
        Assert.True(P.Cutoffs.CountFor(Guid.Parse(Guid32(ready))) >= 2, "ready syllabus: pre- and post-commit cutoff");
        // one content-free delete audit row per cascaded syllabus
        foreach (var s in new[] { ready, running })
            Assert.Equal(1, await F.ScalarAsync($"select count(*) from syllabus.decision_audit where syllabus_id = '{Guid32(s)}' and action = 'syllabus_deleted'"));
        await AssertCourseLeavesNoSyllabusRowsAsync(a.UserId);
        // the cancelled job writes nothing later (late-writer check, C-21)
        await Task.Delay(3500);
        await AssertCourseLeavesNoSyllabusRowsAsync(a.UserId);
        await (await a.Client.GetAsync($"{Api}/syllabi/{running}")).AssertProblemAsync(HttpStatusCode.NotFound, "not_found");
    }

    [Fact]
    public async Task A_syllabus_created_in_the_purge_gap_is_cut_off_and_audited_after_the_commit()
    {
        var a = await NewActorAsync(); var course = await CourseAsync(a.Client);
        P.Gap.Reset();
        var purge = a.Client.DeleteAsync($"{Api}/courses/{course}?purge=true");
        try
        {
            await P.Gap.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));     // the Syllabus pre-commit notice ran and saw no syllabus
            var created = await CreateAsync(a.Client, course, Paste("Quiz due Oct 20, 2026 at 9:00 AM"));
            Assert.Equal(HttpStatusCode.Accepted, created.StatusCode);
            var sid = (await created.BodyAsync()).GetProperty("id").GetString()!;
            P.Gap.Release.TrySetResult();
            Assert.Equal(HttpStatusCode.NoContent, (await purge).StatusCode);
            var g = Guid.Parse(Guid32(sid));
            Assert.True(P.Cutoffs.CountFor(g) >= 1, "the gap syllabus is cut off by the post-commit notice");
            Assert.Equal(1, await F.ScalarAsync($"select count(*) from syllabus.decision_audit where syllabus_id = '{g}' and action = 'syllabus_deleted'"));
            await Task.Delay(3500);
            await AssertCourseLeavesNoSyllabusRowsAsync(a.UserId);
        }
        finally { P.Gap.Release.TrySetResult(); P.Gap.Armed = false; }
    }
}

/// <summary>SB-09 (pastes go through the create gate) and SB-10 (debris re-check under the lock). The worker is off: jobs run by hand.</summary>
public class SyllabusGateAndDebrisTests(SyllabusGateAndDebrisTests.Factory f) : SyllabusTestBase(f), IClassFixture<SyllabusGateAndDebrisTests.Factory>
{
    public sealed class Factory : ApiFactory
    {
        protected override IEnumerable<KeyValuePair<string, string?>> Settings => [new("Syllabus:Worker:Enabled", "false")];
    }

    private async Task RunJobsAsync() { var r = F.Services.GetRequiredService<ExtractionRunner>(); while (await r.RunOnceAsync(CancellationToken.None)) { } }

    /// <summary>A JSON body that sends a few bytes and then stalls until released, so the request holds the create gate while its body is read.</summary>
    private sealed class StallingJson : HttpContent
    {
        private readonly byte[] _body;
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public StallingJson(byte[] body) { _body = body; Headers.ContentType = new MediaTypeHeaderValue("application/json"); }
        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            await stream.WriteAsync(_body.AsMemory(0, 8)); await stream.FlushAsync();
            await Release.Task;
            await stream.WriteAsync(_body.AsMemory(8)); await stream.FlushAsync();
        }
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
    }

    [Fact]
    public async Task SB09_a_paste_holds_the_per_user_create_gate_another_create_is_429_and_a_same_key_retry_waits_and_replays()
    {
        var a = await NewActorAsync(); var course = await CourseAsync(a.Client);
        var gate = F.Services.GetRequiredService<UploadGate>();
        var body = Paste("Quiz due Oct 20, 2026 at 9:00 AM");
        var json = JsonSerializer.Serialize(body);
        var key = NewKey();
        var stall = new StallingJson(Encoding.UTF8.GetBytes(json));
        var holderReq = new HttpRequestMessage(HttpMethod.Post, $"{Api}/courses/{course}/syllabi") { Content = stall };
        holderReq.Headers.TryAddWithoutValidation("Idempotency-Key", key);
        var holder = a.Client.SendAsync(holderReq);
        try
        {
            var until = DateTime.UtcNow.AddSeconds(10);
            while (!gate.IsHeld(a.UserId)) { Assert.True(DateTime.UtcNow < until, "the paste never entered the create gate"); await Task.Delay(10); }

            // another paste (another key) by the same user is refused at once, before its body is read
            await (await CreateAsync(a.Client, course, Paste("Exam due Oct 21, 2026"))).AssertProblemAsync((HttpStatusCode)429, "rate_limited");
            // ... and so is an upload: one create body in flight per user, whichever path
            var form = SyllabusBuiltReviewFixTests.Form(Encoding.UTF8.GetBytes("Exam due Oct 22, 2026"), SyllabusBuiltReviewFixTests.Meta());
            await (await SyllabusBuiltReviewFixTests.Upload(a.Client, course, form, NewKey())).AssertProblemAsync((HttpStatusCode)429, "rate_limited");
            // another user is not affected
            var b = await NewActorAsync();
            Assert.Equal(HttpStatusCode.Accepted, (await CreateAsync(b.Client, await CourseAsync(b.Client), Paste("Quiz due Oct 23, 2026"))).StatusCode);

            // a retry of the SAME create waits for the slot instead of failing
            var retry = a.Client.SendAsync(Req(HttpMethod.Post, $"{Api}/courses/{course}/syllabi", json, key));
            await Task.Delay(300);
            Assert.False(retry.IsCompleted, "the same-key retry waits while the first create holds the gate");
            stall.Release.TrySetResult();
            var first = await holder;
            Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
            var second = await retry;
            Assert.Equal(HttpStatusCode.Accepted, second.StatusCode);
            Assert.Equal("true", second.Headers.GetValues("Idempotency-Replayed").Single());
            Assert.Equal((await first.BodyAsync()).GetProperty("id").GetString(), (await second.BodyAsync()).GetProperty("id").GetString());
            Assert.False(gate.IsHeld(a.UserId));
            Assert.Equal(1, await F.ScalarAsync($"select count(*) from syllabus.syllabi where user_id = '{a.UserId}'"));
        }
        finally { stall.Release.TrySetResult(); await RunJobsAsync(); }
    }

    private async Task<string> FailedPdfAsync(Actor a, string course, string marker)
    {
        var meta = SyllabusBuiltReviewFixTests.Meta(termStart: null, termEnd: null);
        var pdf = Encoding.ASCII.GetBytes("%PDF-1.7\n% " + marker + " padding padding padding padding padding padding\n");
        var res = await SyllabusBuiltReviewFixTests.Upload(a.Client, course, SyllabusBuiltReviewFixTests.Form(pdf, meta, "s.pdf", "application/pdf"), NewKey());
        Assert.Equal(HttpStatusCode.Accepted, res.StatusCode);
        var sid = (await res.BodyAsync()).GetProperty("id").GetString()!;
        await RunJobsAsync();
        Assert.Equal("failed", (await (await a.Client.GetAsync($"{Api}/syllabi/{sid}")).BodyAsync()).GetProperty("status").GetString());
        await F.ExecAsync($"update syllabus.syllabi set purge_after = now() - interval '1 minute' where id = '{Guid32(sid)}'");
        return Guid32(sid);
    }

    private async Task<long> ExistsAsync(string g) => await F.ScalarAsync($"select count(*) from syllabus.syllabi where id = '{g}'");

    [Fact]
    public async Task SB10_the_debris_purge_re_checks_still_debris_under_the_row_lock_and_never_deletes_what_stopped_being_debris()
    {
        var a = await NewActorAsync(); var course = await CourseAsync(a.Client);
        // a ready syllabus with a student item, whose item is copied into a failed syllabus in the race window
        var readyRes = await CreateAsync(a.Client, course, Paste("Midterm exam: Oct 19, 2026 at 10:00 AM"));
        Assert.Equal(HttpStatusCode.Accepted, readyRes.StatusCode);
        var ready = (await readyRes.BodyAsync()).GetProperty("id").GetString()!;
        await RunJobsAsync();
        var studentRes = await a.Client.SendAsync(Req(HttpMethod.Post, $"{Api}/syllabi/{ready}/items", new { kind = "other", title = "My study group", date = "2026-09-20" }));
        Assert.Equal(HttpStatusCode.Created, studentRes.StatusCode);
        var studentItem = Guid32((await studentRes.BodyAsync()).GetProperty("id").GetString()!);

        var withStudentWork = await FailedPdfAsync(a, course, "one");
        var deferred = await FailedPdfAsync(a, course, "two");
        var reExtracting = await FailedPdfAsync(a, course, "three");
        var stillDebris = await FailedPdfAsync(a, course, "four");

        var cols = (await F.TextAsync("select string_agg(column_name, ',' order by ordinal_position) from information_schema.columns where table_schema = 'syllabus' and table_name = 'items' and is_generated = 'NEVER'"))!.Split(',');
        var select = string.Join(",", cols.Select(c => c switch
        {
            "id" => "gen_random_uuid()", "syllabus_id" => $"'{withStudentWork}'::uuid", "item_key" => "'ik_' || substr(md5(random()::text), 1, 32)", _ => c,
        }));
        var maintenance = F.Services.GetRequiredService<SyllabusMaintenance>();
        maintenance.BeforeDebrisDelete = async _ =>
        {
            // all four were selected as debris; now three of them stop being debris before the delete takes the row lock
            await F.ExecAsync($"insert into syllabus.items ({string.Join(",", cols)}) select {select} from syllabus.items where id = '{studentItem}'");
            await F.ExecAsync($"update syllabus.syllabi set purge_after = now() + interval '7 days' where id = '{deferred}'");
            await F.ExecAsync($"update syllabus.syllabi set status = 'extracting', status_reason = null where id = '{reExtracting}'");
        };
        try
        {
            var result = await maintenance.RunOnceAsync(CancellationToken.None);
            Assert.Equal(1, await F.ScalarAsync($"select count(*) from syllabus.items where syllabus_id = '{withStudentWork}' and origin = 'student'"));
            Assert.Equal(1, await ExistsAsync(withStudentWork));      // student work is never deleted
            Assert.Equal(1, await ExistsAsync(deferred));
            Assert.Equal(1, await ExistsAsync(reExtracting));
            Assert.Equal(0, await ExistsAsync(stillDebris));          // real debris is still purged in the same pass
            Assert.Equal(1, result.DebrisDeleted);
            Assert.Equal(0, await F.ScalarAsync($"select count(*) from syllabus.decision_audit where syllabus_id in ('{withStudentWork}','{deferred}','{reExtracting}') and action = 'syllabus_deleted'"));
            Assert.Equal(1, await F.ScalarAsync($"select count(*) from syllabus.decision_audit where syllabus_id = '{stillDebris}' and action = 'syllabus_deleted'"));
        }
        finally { maintenance.BeforeDebrisDelete = null; }
    }
}
