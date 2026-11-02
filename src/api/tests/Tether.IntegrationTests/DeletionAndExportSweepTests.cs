using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Tether.IntegrationTests.Infrastructure;

namespace Tether.IntegrationTests;

/// <summary>
/// PO option 4: an end-to-end deletion and export check, written independently of the feature authors' tests. Mock stack only (mock AI, mock
/// calendar, dev login); no real provider, no real data. Invariant 8 (CLAUDE.md): deletion and export controls.
///
/// The core device is <see cref="DataSweep"/>: it DISCOVERS every base table of every schema from information_schema (so a table added later
/// that forgets its cascade is covered with no edit here) and counts the rows whose text form still mentions a given id or canary string.
/// A survivor is accepted only when its table is in an explicit allow-list below, and every allow-list entry carries its policy reason.
///
/// PO-15c: deleting Tether data never deletes anything already in the student's Google account. On this stack there is no Google client at
/// all (the calendar is the in-database Demo mock; <c>docs/security/google-tokens-and-data-residency.md</c> is a design only), so that rule can
/// only be checked here as "every synced event is a local Demo row and deletion is a database operation"; see the export test and the report.
/// </summary>
public class DeletionAndExportSweepTests(ApiFactory factory) : NotesTestBase(factory), IClassFixture<ApiFactory>
{
    private const string Api = "/api/v1";

    // ---------------------------------------------------------------- allow-lists: rows that policy says remain, each justified

    /// <summary>Survivors after a lecture SESSION is deleted (and, with <see cref="CourseExtra"/>, after a COURSE purge).</summary>
    private static readonly Dictionary<string, string> SessionSurvivors = new()
    {
        ["lectures.consent_records"] = "Consent evidence is content free (ids, versions, times) and append-only on the app path; kept by decision PO-20aw and removed only by lectures.purge_evidence (the erasure path). SessionService.DeleteAsync documents it.",
        ["lectures.consent_events"] = "Same as consent_records: content-free, append-only evidence of Start/Pause/Stop; no FK to the session by design.",
        ["lectures.audit_events"] = "Content-free audit trail (action name, ids, time); no FK by design; removed only by lectures.purge_evidence. Holds the session.delete row itself.",
        ["ai.ai_usage"] = "Content-free cost ledger (tokens, micros, outcome). No session FK BY DESIGN so that deleting a lecture does not refund the daily budget (docs/data/lock-order-audit.md, schema-syllabus-p2.md); rows die with the user (fk_ai_usage_user_id).",
    };

    /// <summary>Added on top of <see cref="SessionSurvivors"/> when a course (and its syllabi) is purged.</summary>
    private static readonly Dictionary<string, string> CourseExtra = new()
    {
        ["syllabus.decision_audit"] = "Content-free syllabus audit (action, ids, counts, HMAC never the plain hash); append-only; removed only by syllabus.purge_evidence (SyllabusBatchAndRetentionTests names it a survivor).",
    };

    private static Dictionary<string, string> With(Dictionary<string, string> a, Dictionary<string, string> b)
    {
        var d = new Dictionary<string, string>(a);
        foreach (var (k, v) in b) d[k] = v;
        return d;
    }

    // ---------------------------------------------------------------- helpers: lectures

    private sealed record Full(Actor A, string Course, string Session, string Canary, string CatchUpWire);

    private static string NewCanary() => "CANARY" + Guid.NewGuid().ToString("N")[..14];

    /// <summary>A stopped session with EVERY kind of derived row populated: ticket, key, finals (one carrying the canary), audio progress, summary revisions (rolling and Final), a catch-up, notes, usage rows and evidence rows.</summary>
    private async Task<Full> FullSessionAsync(Actor? actor = null, string? courseId = null, string? canary = null)
    {
        var a = actor ?? await NewActorAsync();
        canary ??= NewCanary();
        var course = courseId ?? await CourseAsync(a.Client, "Course " + Guid.NewGuid().ToString("N")[..8]);
        var started = await StartAsync(a.Client, course, "k-" + Guid.NewGuid(), "Lecture " + canary);
        Assert.Equal(HttpStatusCode.Created, started.StatusCode);
        var id = (await started.BodyAsync()).GetProperty("id").GetString()!;
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(a.Client, $"/sessions/{id}/audio-ticket")).StatusCode);
        await SeedFinalsAsync(id, a.UserId, 3);
        await F.ExecAsync("insert into transcript.transcript_events (session_id, seq, user_id, kind, segment_no, start_ms, end_ms, text, provider, occurred_at) " +
                          $"values ('{id}', 4, '{a.UserId}', 'Final', 4, 8000, 9500, 'Spoken {canary} words.', 'replay', now())");
        await F.ExecAsync($"insert into transcript.audio_progress (session_id, user_id, accepted_ms, updated_at) values ('{id}', '{a.UserId}', 9500, now())");
        var recap = await QuickRecapAsync(a.Client, id);
        Assert.True(recap.IsSuccessStatusCode, "quick recap setup: " + recap.StatusCode);
        var cuWire = (await recap.BodyAsync()).GetProperty("catchUpId").GetString()!;
        await WaitNotPendingAsync(Guid.ParseExact(cuWire[(cuWire.IndexOf('_') + 1)..], "N"));
        await SeedSummaryAsync(id, a.UserId, 1);
        await F.ExecAsync($"update ai.summary_revisions set tldr = 'Summary about {canary}.' where session_id = '{id}' and revision = 1");
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(a.Client, $"/sessions/{id}/stop")).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await PressAsync(a.Client, id)).StatusCode);
        var notes = await WaitSettledAsync(a.Client, id);
        Assert.Equal("Ready", notes.GetProperty("status").GetString());
        return new Full(a, course, id, canary, cuWire);
    }

    /// <summary>A session that is still Active with a live ticket and finals (deleting it must also end it).</summary>
    private async Task<Full> LiveFullAsync(Actor? actor = null, string? courseId = null)
    {
        var a = actor ?? await NewActorAsync();
        var canary = NewCanary();
        var course = courseId ?? await CourseAsync(a.Client, "Course " + Guid.NewGuid().ToString("N")[..8]);
        var started = await StartAsync(a.Client, course, "k-" + Guid.NewGuid(), "Live " + canary);
        Assert.Equal(HttpStatusCode.Created, started.StatusCode);
        var id = (await started.BodyAsync()).GetProperty("id").GetString()!;
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(a.Client, $"/sessions/{id}/audio-ticket")).StatusCode);
        await SeedFinalsAsync(id, a.UserId, 3);
        await F.ExecAsync($"insert into transcript.audio_progress (session_id, user_id, accepted_ms, updated_at) values ('{id}', '{a.UserId}', 5000, now())");
        return new Full(a, course, id, canary, "");
    }

    // The tables a fully built session must have populated BEFORE deletion, or the "gone" assertions would be vacuous (negative control).
    private static readonly string[] SessionTablesPopulated =
    [
        "lectures.lecture_sessions", "lectures.session_events", "lectures.session_counters", "lectures.idempotency_keys",
        "lectures.consent_records", "lectures.consent_events", "lectures.audit_events",
        "transcript.transcript_events", "transcript.audio_progress",
        "ai.summary_revisions", "ai.catch_ups", "ai.lecture_notes", "ai.ai_usage",
    ];

    private async Task AssertSessionRoutesGoneAsync(Full s)
    {
        foreach (var path in new[] { $"{Api}/sessions/{s.Session}", $"{Api}/sessions/{s.Session}/transcript", $"{Api}/sessions/{s.Session}/summary", $"{Api}/sessions/{s.Session}/notes", $"{Api}/sessions/{s.Session}/catch-ups/{s.CatchUpWire}" })
            await (await s.A.Client.GetAsync(path)).AssertProblemAsync(HttpStatusCode.NotFound, "not_found");
    }

    // ---------------------------------------------------------------- helpers: syllabus

    private sealed record Syl(string Course, string Id, string N, string ApprovedItem, string Canary);

    private static HttpRequestMessage Req(HttpMethod m, string path, object? body = null, string? key = null, string? ifMatch = null)
    {
        var r = new HttpRequestMessage(m, path);
        if (body is not null) r.Content = body is string s ? new StringContent(s, Encoding.UTF8, "application/json") : System.Net.Http.Json.JsonContent.Create(body);
        if (key is not null) r.Headers.TryAddWithoutValidation("Idempotency-Key", key);
        if (ifMatch is not null) r.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        return r;
    }

    private static string Text(string canary) =>
        $"BIO 101 Fall 2026 {canary}\nMidterm exam {canary}: Oct 19, 2026 at 10:00 AM\nQuiz 2 due Oct 21, 2026, also Nov 3, 2026\nOffice hours Tuesdays 2:00-3:00 PM\nLate work policy: {canary}\n";

    private static Dictionary<string, object?> Meta(string title) => new()
    {
        ["title"] = title, ["termStart"] = "2026-08-24", ["termEnd"] = "2026-12-11", ["defaultTimezone"] = "America/New_York", ["allowDuplicate"] = false,
        ["noticeVersion"] = "draft-2026-10", ["attestation"] = new { accepted = true, acceptedAt = DateTimeOffset.UtcNow.ToString("O") },
    };

    private async Task<JsonElement> WaitSylDoneAsync(HttpClient c, string syllabusId)
    {
        var until = DateTime.UtcNow.AddSeconds(30);
        while (true)
        {
            var b = await (await c.GetAsync($"{Api}/syllabi/{syllabusId}")).BodyAsync();
            if (b.GetProperty("pollAfterSeconds").ValueKind == JsonValueKind.Null) return b;
            Assert.True(DateTime.UtcNow < until, "extraction did not finish in time");
            await Task.Delay(50);
        }
    }

    private async Task<List<JsonElement>> SylItemsAsync(HttpClient c, string syllabusId)
    {
        var page = await (await c.GetAsync($"{Api}/syllabi/{syllabusId}/items?limit=200")).BodyAsync();
        return [.. page.GetProperty("items").EnumerateArray()];
    }

    /// <summary>A ready syllabus with items, a student-authored item, an approved+synced item (mock calendar events), unclaimed dates and one export. <paramref name="upload"/> sends it as a multipart text file whose FILE NAME carries the canary.</summary>
    private async Task<Syl> SyllabusAsync(Actor a, string? course = null, string? canary = null, bool upload = false)
    {
        canary ??= NewCanary();
        course ??= await CourseAsync(a.Client, "Course " + Guid.NewGuid().ToString("N")[..8]);
        var key = "k" + Guid.NewGuid().ToString("N");
        HttpRequestMessage req;
        if (upload)
        {
            var mp = new MultipartFormDataContent { { new StringContent(JsonSerializer.Serialize(Meta("BIO " + canary)), Encoding.UTF8, "application/json"), "metadata" } };
            var f = new ByteArrayContent(Encoding.UTF8.GetBytes(Text(canary))); f.Headers.ContentType = MediaTypeHeaderValue.Parse("text/plain");
            mp.Add(f, "file", canary + ".txt");
            req = new HttpRequestMessage(HttpMethod.Post, $"{Api}/courses/{course}/syllabi") { Content = mp };
            req.Headers.TryAddWithoutValidation("Idempotency-Key", key);
        }
        else
        {
            var body = Meta("BIO " + canary); body["text"] = Text(canary);
            req = Req(HttpMethod.Post, $"{Api}/courses/{course}/syllabi", body, key);
        }
        var created = await a.Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Accepted, created.StatusCode);
        var sid = (await created.BodyAsync()).GetProperty("id").GetString()!;
        var done = await WaitSylDoneAsync(a.Client, sid);
        Assert.Equal("ready", done.GetProperty("status").GetString());
        var items = await SylItemsAsync(a.Client, sid);
        var mid = items.First(i => i.GetProperty("title").GetString()!.StartsWith("Midterm", StringComparison.Ordinal));
        var approved = await (await a.Client.SendAsync(Req(HttpMethod.Post, $"{Api}/syllabi/{sid}/items/{mid.GetProperty("id").GetString()}/decision", new { decision = "approved" }, ifMatch: mid.GetProperty("etag").GetString()))).BodyAsync();
        var itemId = approved.GetProperty("id").GetString()!;
        var student = await a.Client.SendAsync(Req(HttpMethod.Post, $"{Api}/syllabi/{sid}/items", new { kind = "other", title = "Mine " + canary, notes = canary, date = "2026-09-20" }));
        Assert.Equal(HttpStatusCode.Created, student.StatusCode);
        var sync = await a.Client.SendAsync(Req(HttpMethod.Post, $"{Api}/syllabi/{sid}/calendar-sync", new Dictionary<string, object>
        {
            ["itemRevisions"] = new Dictionary<string, object> { [itemId] = approved.GetProperty("revision").GetInt32() },
            ["confirm"] = new { provider = "mock", acknowledgedDemo = true },
        }, "k" + Guid.NewGuid().ToString("N")));
        Assert.True(sync.IsSuccessStatusCode, "calendar sync setup: " + sync.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await a.Client.GetAsync($"{Api}/syllabi/{sid}/export")).StatusCode);
        return new Syl(course, sid, sid[(sid.IndexOf('_') + 1)..], itemId, canary);
    }

    private static readonly string[] SyllabusTablesPopulated =
    [
        "syllabus.syllabi", "syllabus.canonical_texts", "syllabus.extraction_jobs", "syllabus.items", "syllabus.unclaimed_dates",
        "syllabus.sync_runs", "syllabus.sync_run_items", "syllabus.calendar_events", "syllabus.idempotency_keys", "syllabus.decision_audit",
    ];

    // ---------------------------------------------------------------- sweep plumbing

    /// <summary>Lower-case search strings: a Guid or a guid-shaped string yields both its hyphenated and its 32-hex form (wire ids use the latter); any other string is used as is.</summary>
    private static string[] Needles(params object[] ids)
    {
        var all = new List<string>();
        foreach (var i in ids)
        {
            if (i is Guid g0) { all.Add(g0.ToString("D")); all.Add(g0.ToString("N")); }
            else if (i is string s && Guid.TryParse(s, out var g1)) { all.Add(g1.ToString("D")); all.Add(g1.ToString("N")); }
            else if (i is string raw) all.Add(raw);
            else throw new ArgumentException("id");
        }
        return [.. all.Select(x => x.ToLowerInvariant()).Distinct()];
    }

    private async Task<object[]> ItemIdsAsync(string syllabusN)
    {
        await using var conn = new Npgsql.NpgsqlConnection(F.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new Npgsql.NpgsqlCommand($"select id from syllabus.items where syllabus_id = '{syllabusN}'", conn);
        await using var r = await cmd.ExecuteReaderAsync();
        var ids = new List<object>();
        while (await r.ReadAsync()) ids.Add(r.GetGuid(0));
        return [.. ids];
    }

    private async Task AssertSweptAsync(string what, string[] needles, Dictionary<string, string> allowed)
    {
        var hits = await DataSweep.HitsAsync(F, needles);
        var offenders = hits.Where(h => !allowed.ContainsKey(h.Key)).Select(h => $"{h.Key} ({h.Value} row(s))").ToList();
        Assert.True(offenders.Count == 0, $"{what}: rows still carry the deleted id in tables that policy says must be empty: {string.Join(", ", offenders)}");
    }

    private async Task AssertPopulatedAsync(string what, string[] needles, IEnumerable<string> tables)
    {
        var hits = await DataSweep.HitsAsync(F, needles);
        var missing = tables.Where(t => !hits.ContainsKey(t)).ToList();
        Assert.True(missing.Count == 0, $"{what}: setup did not populate {string.Join(", ", missing)}; the 'gone' assertions would be vacuous");
    }

    private async Task AssertNoCanaryAnywhereAsync(params string[] canaries)
    {
        foreach (var c in canaries)
        {
            var hits = await DataSweep.HitsAsync(F, [c.ToLowerInvariant()]);
            Assert.True(hits.Count == 0, $"text '{c}' still present in: {string.Join(", ", hits.Select(h => h.Key + " x" + h.Value))} (including content-free survivors, which must hold no content)");
        }
    }

    // ================================================================ (1) delete a lecture session

    [Fact]
    public async Task Deleting_a_session_removes_every_derived_row_and_touches_no_other_session_or_user()
    {
        var alice = await NewActorAsync();
        var s1 = await FullSessionAsync(alice);
        var s2 = await FullSessionAsync(alice);                       // same user, another course and session
        var bob = await FullSessionAsync();                           // another user, same shape
        var gone = Needles(s1.Session);
        var others = Needles(s2.Session, s2.Course, bob.Session, bob.Course, bob.A.UserId);

        await AssertPopulatedAsync("session setup", gone, SessionTablesPopulated);
        var othersBefore = await DataSweep.HitsAsync(F, others);
        Assert.NotEmpty(othersBefore);

        // another user cannot delete it (404, not 403), and nothing changes
        var goneBefore = await DataSweep.HitsAsync(F, gone);
        await (await bob.A.Client.DeleteAsync($"{Api}/sessions/{s1.Session}")).AssertProblemAsync(HttpStatusCode.NotFound, "not_found");
        Assert.Equal(goneBefore, await DataSweep.HitsAsync(F, gone));

        Assert.Equal(HttpStatusCode.NoContent, (await alice.Client.DeleteAsync($"{Api}/sessions/{s1.Session}")).StatusCode);

        await AssertSweptAsync("session delete", gone, SessionSurvivors);
        await AssertNoCanaryAnywhereAsync(s1.Canary);                 // title, transcript text and summary text are gone from EVERY table, survivors included
        await AssertSessionRoutesGoneAsync(s1);
        Assert.True(await CountAsync("audit_events", $"session_id = '{s1.Session}' and action = 'session.delete'") == 1, "the content-free survivor trail records the deletion");

        Assert.Equal(othersBefore, await DataSweep.HitsAsync(F, others));
        Assert.Equal(HttpStatusCode.OK, (await alice.Client.GetAsync($"{Api}/sessions/{s2.Session}/transcript")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await bob.A.Client.GetAsync($"{Api}/sessions/{bob.Session}/notes")).StatusCode);
        await (await alice.Client.DeleteAsync($"{Api}/sessions/{s1.Session}")).AssertProblemAsync(HttpStatusCode.NotFound, "not_found");
        // the course the session lived in is still there (deleting a session is not deleting the course)
        Assert.Equal(1, await F.ScalarAsync($"select count(*) from courses.courses where id = '{s1.Course}'"));
    }

    [Fact]
    public async Task Deleting_a_live_session_ends_it_and_leaves_no_ticket_transcript_or_progress()
    {
        var live = await LiveFullAsync();
        var bystander = await LiveFullAsync();
        var gone = Needles(live.Session);
        var bystanderBefore = await DataSweep.HitsAsync(F, Needles(bystander.Session, bystander.Course, bystander.A.UserId));
        await AssertPopulatedAsync("live setup", gone, ["lectures.lecture_sessions", "lectures.audio_tickets", "lectures.session_events", "transcript.transcript_events", "transcript.audio_progress", "lectures.consent_records"]);

        Assert.Equal(HttpStatusCode.NoContent, (await live.A.Client.DeleteAsync($"{Api}/sessions/{live.Session}")).StatusCode);

        await AssertSweptAsync("live session delete", gone, SessionSurvivors);
        await AssertNoCanaryAnywhereAsync(live.Canary);
        // the stop was recorded as consent evidence before the rows went (content free)
        Assert.True(await CountAsync("consent_events", $"session_id = '{live.Session}' and kind = 'Stop'") >= 1);
        // a ticket minted before the delete cannot be redeemed afterwards: there is no row to redeem
        Assert.Equal(0, await CountAsync("audio_tickets", $"session_id = '{live.Session}'"));
        Assert.Equal(bystanderBefore, await DataSweep.HitsAsync(F, Needles(bystander.Session, bystander.Course, bystander.A.UserId)));
    }

    // ================================================================ (2) delete a course: archive versus purge

    [Fact]
    public async Task Archiving_a_course_stops_recording_but_deletes_nothing_then_purge_cascades_everything()
    {
        var a = await NewActorAsync();
        var course = await CourseAsync(a.Client, "Archive me");
        var keep = await FullSessionAsync(a);                         // another course of the same user stays (first: one live session per user)
        var syl = await SyllabusAsync(a, course);
        var live = await LiveFullAsync(a, course);
        var keepSyl = await SyllabusAsync(a, keep.Course);
        var bob = await FullSessionAsync();
        var bobSyl = await SyllabusAsync(bob.A, bob.Course);
        var courseNeedles = Needles([course, live.Session, syl.N, .. await ItemIdsAsync(syl.N)]);
        var others = Needles(keep.Course, keep.Session, keepSyl.N, bob.Course, bob.Session, bobSyl.N, bob.A.UserId);

        var before = await DataSweep.HitsAsync(F, courseNeedles);
        var othersBefore = await DataSweep.HitsAsync(F, others);

        // another user cannot archive or purge it
        await (await bob.A.Client.DeleteAsync($"{Api}/courses/{course}")).AssertProblemAsync(HttpStatusCode.NotFound, "not_found");
        await (await bob.A.Client.DeleteAsync($"{Api}/courses/{course}?purge=true")).AssertProblemAsync(HttpStatusCode.NotFound, "not_found");
        Assert.Equal(before, await DataSweep.HitsAsync(F, courseNeedles));

        // ARCHIVE: reversible; nothing is deleted; recording must end
        Assert.Equal(HttpStatusCode.NoContent, (await a.Client.DeleteAsync($"{Api}/courses/{course}")).StatusCode);
        Assert.Equal(1, await F.ScalarAsync($"select count(*) from courses.courses where id = '{course}' and archived_at is not null"));
        Assert.Equal("Stopped", await F.TextAsync($"select state from lectures.lecture_sessions where id = '{live.Session}'"));
        var afterArchive = await DataSweep.HitsAsync(F, courseNeedles);
        foreach (var table in before.Keys.Where(k => k is not ("lectures.audio_tickets" or "lectures.consent_events" or "lectures.audit_events" or "lectures.lecture_sessions" or "lectures.session_events" or "lectures.session_counters" or "syllabus.decision_audit" or "ai.ai_usage")))
            Assert.True(afterArchive.TryGetValue(table, out var n) && n >= before[table], $"archive must not remove rows from {table}");
        Assert.True(await F.ScalarAsync($"select count(*) from syllabus.items where syllabus_id = '{syl.N}'") > 0);
        Assert.True(await F.ScalarAsync($"select count(*) from transcript.transcript_events where session_id = '{live.Session}'") >= 3);
        Assert.Equal(HttpStatusCode.NoContent, (await a.Client.DeleteAsync($"{Api}/courses/{course}")).StatusCode); // archiving twice is harmless

        // PURGE of the archived course
        Assert.Equal(HttpStatusCode.NoContent, (await a.Client.DeleteAsync($"{Api}/courses/{course}?purge=true")).StatusCode);
        await AssertSweptAsync("course purge after archive", courseNeedles, With(SessionSurvivors, CourseExtra));
        Assert.Equal(0, await F.ScalarAsync($"select count(*) from courses.courses where id = '{course}'"));
        Assert.Equal(othersBefore, await DataSweep.HitsAsync(F, others));
        await (await a.Client.GetAsync($"{Api}/syllabi/{syl.Id}")).AssertProblemAsync(HttpStatusCode.NotFound, "not_found");
        await (await a.Client.GetAsync($"{Api}/sessions/{live.Session}")).AssertProblemAsync(HttpStatusCode.NotFound, "not_found");
    }

    [Fact]
    public async Task Purging_a_course_cascades_to_sessions_syllabi_items_dates_texts_events_runs_and_keys()
    {
        var a = await NewActorAsync();
        var courseCanary = NewCanary();
        var course = await CourseAsync(a.Client, "Purge me " + courseCanary);
        var s = await FullSessionAsync(a, course);
        var syl = await SyllabusAsync(a, course);
        var keep = await FullSessionAsync(a);
        var keepSyl = await SyllabusAsync(a, keep.Course);
        var bob = await FullSessionAsync();
        var bobSyl = await SyllabusAsync(bob.A, bob.Course);
        var gone = Needles([course, s.Session, syl.N, .. await ItemIdsAsync(syl.N)]);
        var others = Needles(keep.Course, keep.Session, keepSyl.N, bob.Course, bob.Session, bobSyl.N, bob.A.UserId);

        await AssertPopulatedAsync("course setup", gone, [.. SessionTablesPopulated, .. SyllabusTablesPopulated, "courses.courses"]);
        var othersBefore = await DataSweep.HitsAsync(F, others);

        Assert.Equal(HttpStatusCode.NoContent, (await a.Client.DeleteAsync($"{Api}/courses/{course}?purge=true")).StatusCode);

        await AssertSweptAsync("course purge", gone, With(SessionSurvivors, CourseExtra));
        await AssertNoCanaryAnywhereAsync(s.Canary, syl.Canary, courseCanary);
        Assert.Equal(othersBefore, await DataSweep.HitsAsync(F, others));
        // the content-free survivors say what happened without saying what was in the course
        Assert.True(await F.ScalarAsync($"select count(*) from syllabus.decision_audit where syllabus_id = '{syl.N}' and action = 'syllabus_deleted'") >= 1, "the content-free audit survivor records the syllabus deletion");
        // the live objects of the other course and the other user still answer
        Assert.Equal(HttpStatusCode.OK, (await a.Client.GetAsync($"{Api}/syllabi/{keepSyl.Id}/export")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await bob.A.Client.GetAsync($"{Api}/sessions/{bob.Session}/summary")).StatusCode);
    }

    // ================================================================ (3) delete a syllabus

    [Fact]
    public async Task Deleting_a_syllabus_removes_text_items_dates_events_runs_jobs_and_keys_and_leaves_no_trace_of_the_upload()
    {
        var a = await NewActorAsync();
        var since = DateTime.UtcNow.AddMinutes(-1);
        var syl = await SyllabusAsync(a, upload: true);                // multipart text file; the file NAME carries the canary
        var other = await SyllabusAsync(a);                             // same user, another course
        var bob = await SyllabusAsync((await NewActorAsync()));
        var gone = Needles([syl.N, .. await ItemIdsAsync(syl.N)]);
        var others = Needles(other.N, other.Course, bob.N, bob.Course);

        var meta = await (await a.Client.GetAsync($"{Api}/syllabi/{syl.Id}")).BodyAsync();
        Assert.Equal("upload", meta.GetProperty("inputKind").GetString());
        Assert.False(meta.GetProperty("originalRetained").GetBoolean(), "the uploaded original is not kept");
        await AssertPopulatedAsync("syllabus setup", gone, SyllabusTablesPopulated);
        var othersBefore = await DataSweep.HitsAsync(F, others);
        Assert.NotEmpty(await DataSweep.HitsAsync(F, [syl.Canary.ToLowerInvariant()]));   // negative control for the canary scan

        // a stranger cannot delete it, and nothing moves
        var goneBefore = await DataSweep.HitsAsync(F, gone);
        var stranger = await NewActorAsync();
        await (await stranger.Client.SendAsync(Req(HttpMethod.Delete, $"{Api}/syllabi/{syl.Id}"))).AssertProblemAsync(HttpStatusCode.NotFound, "not_found");
        Assert.Equal(goneBefore, await DataSweep.HitsAsync(F, gone));

        Assert.Equal(HttpStatusCode.NoContent, (await a.Client.SendAsync(Req(HttpMethod.Delete, $"{Api}/syllabi/{syl.Id}"))).StatusCode);

        await AssertSweptAsync("syllabus delete", gone, CourseExtra);
        await AssertNoCanaryAnywhereAsync(syl.Canary);               // text, snippets, titles, notes, file name and events: gone everywhere, audit included
        Assert.Equal(0, await F.ScalarAsync($"select count(*) from syllabus.idempotency_keys where user_id = '{a.UserId}' and (syllabus_id = '{syl.N}' or result_id = '{syl.N}')"));
        Assert.True(TempFilesContaining(syl.Canary, since) == 0, "a temp file still holds the uploaded text (sampling: top level of the temp directory only)");
        Assert.Equal(othersBefore, await DataSweep.HitsAsync(F, others));
        await (await a.Client.GetAsync($"{Api}/syllabi/{syl.Id}/export")).AssertProblemAsync(HttpStatusCode.NotFound, "not_found");
        await (await a.Client.SendAsync(Req(HttpMethod.Delete, $"{Api}/syllabi/{syl.Id}"))).AssertProblemAsync(HttpStatusCode.NotFound, "not_found");
    }

    // ================================================================ (4) export

    [Fact]
    public async Task Syllabus_export_is_on_demand_owner_scoped_complete_and_carries_no_secret_or_foreign_data()
    {
        var alice = await NewActorAsync(); var bobActor = await NewActorAsync();
        var a = await SyllabusAsync(alice);
        var bCourse = await CourseAsync(bobActor.Client);
        var b = await SyllabusAsync(bobActor, bCourse);
        // "on demand": setup created, approved and synced; the only export so far is the one SyllabusAsync made deliberately
        Assert.Equal(1, await F.ScalarAsync($"select count(*) from syllabus.decision_audit where syllabus_id = '{a.N}' and action = 'exported'"));
        await alice.Client.GetAsync($"{Api}/syllabi/{a.Id}"); await SylItemsAsync(alice.Client, a.Id); await alice.Client.GetAsync($"{Api}/syllabi/{a.Id}/unclaimed-dates");
        Assert.Equal(1, await F.ScalarAsync($"select count(*) from syllabus.decision_audit where syllabus_id = '{a.N}' and action = 'exported'"));   // reads never export

        var sylTablesBefore = (await DataSweep.HitsAsync(F, Needles(a.N))).Where(h => h.Key != "syllabus.decision_audit").ToDictionary();
        var res = await alice.Client.GetAsync($"{Api}/syllabi/{a.Id}/export");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("no-store", res.Headers.CacheControl!.ToString());
        Assert.Contains("attachment", res.Content.Headers.ContentDisposition!.ToString());
        Assert.Equal("nosniff", res.Headers.GetValues("X-Content-Type-Options").Single());
        var raw = await res.Content.ReadAsStringAsync();
        var body = JsonDocument.Parse(raw).RootElement;
        SyllabusSchemas.AssertValid("syllabus-export", body);

        // expected sections, populated, and consistent with the database
        Assert.Equal("syllabus.v1", body.GetProperty("schemaVersion").GetString());
        Assert.Equal(a.Id, body.GetProperty("syllabus").GetProperty("id").GetString());
        Assert.True(body.GetProperty("items").GetArrayLength() >= 2);
        Assert.True(body.GetProperty("unclaimedDates").GetArrayLength() >= 1);
        var events = body.GetProperty("calendarEvents");
        Assert.Equal(await F.ScalarAsync($"select count(*) from syllabus.calendar_events where syllabus_id = '{a.N}'"), events.GetArrayLength());
        Assert.All(events.EnumerateArray(), e => Assert.True(e.GetProperty("demo").GetBoolean(), "PO-15c on the mock stack: every exported calendar event is a local Demo row, not something in a Google account"));
        Assert.Equal(2, await F.ScalarAsync($"select count(*) from syllabus.decision_audit where syllabus_id = '{a.N}' and action = 'exported'"));
        Assert.Equal(sylTablesBefore, (await DataSweep.HitsAsync(F, Needles(a.N))).Where(h => h.Key != "syllabus.decision_audit").ToDictionary());   // export changes no data

        // no token, secret, cookie, owner id, owner email, or any of the other user's data
        var lower = raw.ToLowerInvariant();
        foreach (var banned in new[] { "token", "secret", "cookie", "password", "authorization", "bearer", "refresh", alice.UserId.ToString("D"), alice.UserId.ToString("N"), alice.Email.ToLowerInvariant(),
                                       bobActor.UserId.ToString("D"), bobActor.UserId.ToString("N"), bobActor.Email.ToLowerInvariant(), b.N, b.Canary.ToLowerInvariant(), bCourse.ToLowerInvariant(), bCourse.Replace("-", "") })
            Assert.DoesNotContain(banned, lower);
        Assert.Contains(a.Canary.ToLowerInvariant(), lower);                      // control: the owner's own content IS there

        // another user: 404 identical to a missing syllabus; never data
        var theirs = await bobActor.Client.GetAsync($"{Api}/syllabi/{a.Id}/export");
        var missing = await bobActor.Client.GetAsync($"{Api}/syllabi/syl_{Guid.NewGuid():D}/export");
        await theirs.AssertProblemAsync(HttpStatusCode.NotFound, "not_found");
        await missing.AssertProblemAsync(HttpStatusCode.NotFound, "not_found");
        Assert.DoesNotContain(a.Canary, await theirs.Content.ReadAsStringAsync());
        Assert.Equal((await missing.BodyAsync()).GetProperty("detail").GetString(), (await theirs.BodyAsync()).GetProperty("detail").GetString());
        Assert.Equal(2, await F.ScalarAsync($"select count(*) from syllabus.decision_audit where syllabus_id = '{a.N}' and action = 'exported'"));   // the refused call audited nothing
        // and anonymous
        await (await F.NewClient().GetAsync($"{Api}/syllabi/{a.Id}/export")).AssertProblemAsync(HttpStatusCode.Unauthorized, "unauthenticated");
    }

    [Fact]
    public async Task Syllabus_export_csrf_guard_refuses_cross_site_and_foreign_origin_and_audits_nothing_for_them()
    {
        var a = await NewActorAsync();
        var syl = await SyllabusAsync(a);
        var exportsBefore = await F.ScalarAsync($"select count(*) from syllabus.decision_audit where syllabus_id = '{syl.N}' and action = 'exported'");
        HttpRequestMessage Export(string? site, string? origin = ApiFactory.Origin)
        {
            var r = new HttpRequestMessage(HttpMethod.Get, $"{Api}/syllabi/{syl.Id}/export");
            if (site is not null) r.Headers.TryAddWithoutValidation("Sec-Fetch-Site", site);
            if (origin is not null) r.Headers.TryAddWithoutValidation("Origin", origin);
            return r;
        }
        foreach (var site in new[] { "cross-site", "same-site", "Same-Origin", "same-origin, cross-site" })
            await (await a.Client.SendAsync(Export(site))).AssertProblemAsync(HttpStatusCode.Forbidden, "csrf_failed");
        await (await a.Client.SendAsync(Export("same-origin", "https://evil.example"))).AssertProblemAsync(HttpStatusCode.Forbidden, "csrf_failed");
        await (await a.Client.SendAsync(Export(null, "null"))).AssertProblemAsync(HttpStatusCode.Forbidden, "csrf_failed");
        Assert.Equal(exportsBefore, await F.ScalarAsync($"select count(*) from syllabus.decision_audit where syllabus_id = '{syl.N}' and action = 'exported'"));
        // a cross-site request is refused BEFORE the ownership lookup, so it cannot be used to probe ids
        var stranger = await NewActorAsync();
        await (await stranger.Client.SendAsync(Export("cross-site"))).AssertProblemAsync(HttpStatusCode.Forbidden, "csrf_failed");
        // control: the legitimate same-origin and typed-address requests still work
        foreach (var site in new[] { "same-origin", "none" })
            Assert.Equal(HttpStatusCode.OK, (await a.Client.SendAsync(Export(site))).StatusCode);
    }

    [Fact]
    public async Task Lecture_data_reads_are_owner_scoped_and_leak_no_owner_id_or_secret_there_is_no_lecture_export_route()
    {
        var alice = await FullSessionAsync();
        var bob = await NewActorAsync();
        var paths = new[] { $"{Api}/sessions/{alice.Session}", $"{Api}/sessions/{alice.Session}/transcript", $"{Api}/sessions/{alice.Session}/summary", $"{Api}/sessions/{alice.Session}/notes", $"{Api}/sessions/{alice.Session}/catch-ups/{alice.CatchUpWire}" };
        foreach (var p in paths)
        {
            var mine = await alice.A.Client.GetAsync(p);
            Assert.Equal(HttpStatusCode.OK, mine.StatusCode);
            var text = (await mine.Content.ReadAsStringAsync()).ToLowerInvariant();
            foreach (var banned in new[] { alice.A.UserId.ToString("D"), alice.A.UserId.ToString("N"), alice.A.Email.ToLowerInvariant(), "refresh_token", "access_token", "set-cookie", "password" })
                Assert.DoesNotContain(banned, text);
            var theirs = await bob.Client.GetAsync(p);
            await theirs.AssertProblemAsync(HttpStatusCode.NotFound, "not_found");
            Assert.DoesNotContain(alice.Canary.ToLowerInvariant(), (await theirs.Content.ReadAsStringAsync()).ToLowerInvariant());
        }
        // Documented gap, not a pass: no route exports a lecture, transcript or notes as a file. These probes must stay 404 until one is built (then this test must be extended).
        foreach (var p in new[] { $"{Api}/sessions/{alice.Session}/export", $"{Api}/sessions/{alice.Session}/notes/export", $"{Api}/me/export", $"{Api}/export" })
            Assert.Equal(HttpStatusCode.NotFound, (await alice.A.Client.GetAsync(p)).StatusCode);
    }

    // ================================================================ (5) account-level erasure (database path) and schema invariants

    [Fact]
    public async Task Erasing_a_user_leaves_nothing_in_any_table_once_the_two_evidence_purge_paths_have_run_and_touches_no_other_user()
    {
        var a = await FullSessionAsync();
        var aSyl = await SyllabusAsync(a.A, a.Course);                                    // second course for the syllabus keeps shapes simple
        var aLive = await LiveFullAsync(a.A);
        var b = await FullSessionAsync();
        var bSyl = await SyllabusAsync(b.A, b.Course);
        var mine = Needles([a.A.UserId, a.Session, a.Course, aSyl.N, aLive.Session, .. await ItemIdsAsync(aSyl.N)]);
        var bobs = Needles(b.A.UserId, b.Session, b.Course, bSyl.N);
        await AssertPopulatedAsync("user setup", Needles(a.A.UserId), [.. SessionTablesPopulated, .. SyllabusTablesPopulated, "courses.courses", "identity.sessions", "identity.users"]);
        var bobBefore = await DataSweep.HitsAsync(F, bobs);

        // There is no account-deletion API yet (reported as a gap); the database cascade is the only erasure path, so that is what is checked.
        await F.ExecAsync($"delete from identity.users where id = '{a.A.UserId}'");
        var mid = new Dictionary<string, string>(SessionSurvivors.Where(k => k.Key != "ai.ai_usage")) { ["syllabus.decision_audit"] = CourseExtra["syllabus.decision_audit"], ["syllabus.creation_counters"] = "Per-user daily creation quota counter (user, day, count); content free; no FK to the user by design (never refunded); removed by syllabus.purge_evidence, which this test then requires to empty it." };
        await AssertSweptAsync("user delete, before the evidence purge", mine, mid);
        Assert.Equal(0, await F.ScalarAsync($"select count(*) from ai.ai_usage where user_id = '{a.A.UserId}'"));
        await AssertNoCanaryAnywhereAsync(a.Canary, aSyl.Canary, aLive.Canary);

        await F.ExecAsync($"select lectures.purge_evidence('{a.A.UserId}')");
        await F.ExecAsync($"select syllabus.purge_evidence('{a.A.UserId}')");
        var left = await DataSweep.HitsAsync(F, mine);
        Assert.True(left.Count == 0, "after the cascade and both purge paths NOTHING may mention the user, their sessions, course or syllabus: " + string.Join(", ", left.Select(h => h.Key + " x" + h.Value)));
        Assert.Equal(bobBefore, await DataSweep.HitsAsync(F, bobs));
        Assert.Equal(HttpStatusCode.OK, (await b.A.Client.GetAsync($"{Api}/sessions/{b.Session}/notes")).StatusCode);
    }

    [Fact]
    public async Task The_sweep_covers_every_schema_and_table_and_no_table_stores_a_secret_in_a_column_named_like_one()
    {
        var tables = await DataSweep.TablesAsync(F);
        foreach (var schema in new[] { "ai", "courses", "identity", "lectures", "syllabus", "transcript" })
            Assert.Contains(tables, t => t.StartsWith(schema + ".", StringComparison.Ordinal));
        Assert.True(tables.Count >= 25, "discovery found only " + tables.Count + " tables; the sweep would be vacuous");
        // a Google/integration schema must not exist on the mock stack; when it does, this test must grow token-ciphertext and disconnect checks (PO-15c, SEC-027)
        Assert.DoesNotContain(tables, t => t.StartsWith("integrations.", StringComparison.Ordinal));

        var secretish = new List<string>();
        await using (var conn = new Npgsql.NpgsqlConnection(F.ConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = new Npgsql.NpgsqlCommand("select table_schema || '.' || table_name || '.' || column_name from information_schema.columns where table_schema not in ('pg_catalog','information_schema') and column_name ~* '(token|secret|password|credential|cookie)'", conn);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) secretish.Add(r.GetString(0));
        }
        // allowed: token COUNTS in the cost ledger, and the SHA-256 hash of the one-time audio ticket (never the token itself)
        var unexpected = secretish.Where(c => c is not ("ai.ai_usage.input_tokens" or "ai.ai_usage.output_tokens" or "ai.ai_usage.cached_tokens" or "lectures.audio_tickets.token_hash")).ToList();
        Assert.True(unexpected.Count == 0, "columns that look like stored secrets (classify them or remove them): " + string.Join(", ", unexpected));
    }

    [Fact]
    public async Task The_sweep_fails_on_a_new_table_that_forgot_the_cascade_and_on_a_content_leak_in_a_survivor()
    {
        var s = await FullSessionAsync();
        Assert.Equal(HttpStatusCode.NoContent, (await s.A.Client.DeleteAsync($"{Api}/sessions/{s.Session}")).StatusCode);
        await AssertSweptAsync("baseline", Needles(s.Session), SessionSurvivors);                // clean before the plant

        await F.ExecAsync("create table lectures.zz_future_table (note text)");
        try
        {
            // 1) a table added later whose rows name the session but have no cascade: the discovery-based sweep must catch it without any edit here
            await F.ExecAsync($"insert into lectures.zz_future_table values ('derived from {s.Session}')");
            var ex = await Assert.ThrowsAnyAsync<Xunit.Sdk.XunitException>(() => AssertSweptAsync("planted", Needles(s.Session), SessionSurvivors));
            Assert.Contains("lectures.zz_future_table", ex.Message);
            // 2) a survivor table (allow-listed for ids) that leaks CONTENT is still caught by the canary sweep
            await F.ExecAsync("delete from lectures.zz_future_table");
            await F.ExecAsync($"insert into lectures.zz_future_table values ('{s.Canary}')");
            await Assert.ThrowsAnyAsync<Xunit.Sdk.XunitException>(() => AssertNoCanaryAnywhereAsync(s.Canary));
        }
        finally { await F.ExecAsync("drop table if exists lectures.zz_future_table"); }
        await AssertNoCanaryAnywhereAsync(s.Canary);
    }

    // ---------------------------------------------------------------- temp files (best effort)

    private static int TempFilesContaining(string needle, DateTime sinceUtc)
    {
        var n = 0;
        var bytes = Encoding.UTF8.GetBytes(needle);
        foreach (var f in Directory.EnumerateFiles(Path.GetTempPath()))
        {
            try
            {
                var info = new FileInfo(f);
                if (info.LastWriteTimeUtc < sinceUtc || info.Length is 0 or > 5_000_000) continue;
                if (File.ReadAllBytes(f).AsSpan().IndexOf(bytes) >= 0) n++;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return n;
    }
}

/// <summary>Discovers every base table in every non-system schema and counts rows whose whole-row text mentions a needle (ids in both D and N forms, or a canary).</summary>
internal static class DataSweep
{
    public static async Task<List<string>> TablesAsync(ApiFactory f)
    {
        var list = new List<string>();
        await using var conn = new Npgsql.NpgsqlConnection(f.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new Npgsql.NpgsqlCommand(
            "select table_schema || '.' || table_name from information_schema.tables where table_type = 'BASE TABLE' and table_schema not in ('pg_catalog', 'information_schema') and table_name <> '__ef_migrations_history' order by 1", conn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) list.Add(r.GetString(0));
        return list;
    }

    /// <summary>table -> matching rows (only tables with at least one). Needles must be lower-case ids, hex or alphanumeric canaries.</summary>
    public static async Task<Dictionary<string, long>> HitsAsync(ApiFactory f, string[] needles)
    {
        foreach (var n in needles) Assert.Matches(new Regex("^[a-z0-9-]+$"), n);   // never interpolate anything else into SQL
        var any = "array[" + string.Join(",", needles.Select(n => $"'%{n}%'")) + "]";
        var hits = new Dictionary<string, long>();
        foreach (var t in await TablesAsync(f))
        {
            var (s, n) = (t[..t.IndexOf('.')], t[(t.IndexOf('.') + 1)..]);
            var count = await f.ScalarAsync($"select count(*) from \"{s}\".\"{n}\" x where lower(x::text) like any ({any})");
            if (count > 0) hits[t] = count;
        }
        return hits;
    }
}
