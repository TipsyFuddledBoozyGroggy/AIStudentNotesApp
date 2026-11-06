using System.Net;
using System.Text.Json;
using Tether.Ai.Provider;
using Tether.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Tether.IntegrationTests;

/// <summary>
/// docs.export.v1 (DX-B04 to DX-B07, DX-B10 to DX-B35): preview, confirm, idempotency, modes, failures, state, Demo Doc read and delete. Mock provider
/// only: the Demo Doc is a row in our own database and the AI is the deterministic test routine. Nothing here talks to Google or any network.
/// </summary>
[Collection(RacePool.Name)]
public class DocsExportFlowTests(DocsFactory factory) : DocsExportTestBase(factory), IClassFixture<DocsFactory>
{
    private MockAiClient Ai => F.Services.GetRequiredService<MockAiClient>();

    // ---------------- preview ----------------

    [Fact]
    public async Task B10_Preview_writes_no_row_no_audit_calls_no_provider_and_no_AI_and_matches_the_schema()
    {
        var l = await ReadyAsync();
        var callsBefore = Ai.Calls;
        var usageBefore = await F.ScalarAsync($"select count(*) from ai.ai_usage where user_id = '{l.A.UserId}'");

        var res = await PreviewAsync(l.A.Client, l.SessionId, l.NotesId);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var p = await res.BodyAsync();
        DocsSchemas.AssertValid("docs-preview", p);

        Assert.True(p.GetProperty("previewOnly").GetBoolean());
        Assert.True(p.GetProperty("canWrite").GetBoolean());
        Assert.Equal(["create"], ActionsOf(p));
        Assert.Equal(JsonValueKind.Null, p.GetProperty("existing").ValueKind);
        Assert.Equal("mock", p.GetProperty("notesSource").GetString());
        var blocks = p.GetProperty("document").GetProperty("blocks").EnumerateArray().ToList();
        Assert.Equal("heading", blocks[0].GetProperty("kind").GetString());
        Assert.Equal(1, blocks[0].GetProperty("level").GetInt32());
        Assert.Equal(p.GetProperty("document").GetProperty("title").GetString(), blocks[0].GetProperty("text").GetString());
        Assert.EndsWith(" UTC", p.GetProperty("document").GetProperty("title").GetString());
        Assert.StartsWith("Cell division - ", p.GetProperty("document").GetProperty("title").GetString());
        Assert.Equal("paragraph", blocks[^1].GetProperty("kind").GetString());
        Assert.Contains("Made by Tether", blocks[^1].GetProperty("text").GetString());
        // no row anywhere, no audit, no AI
        foreach (var t in new[] { "demo_docs", "doc_exports", "doc_targets", "idempotency_keys", "audit_events" })
            Assert.Equal(0, await DocRowsAsync(t, l.A.UserId));
        Assert.Equal(callsBefore, Ai.Calls);
        Assert.Equal(usageBefore, await F.ScalarAsync($"select count(*) from ai.ai_usage where user_id = '{l.A.UserId}'"));
        // the document carries lecture times only: no segment id, no transcript row text beyond what the notes contain
        var raw = p.GetRawText();
        Assert.DoesNotContain("segmentId", raw);
        Assert.DoesNotContain("seg_", raw);
    }

    [Fact]
    public async Task B11_Preview_is_409_for_a_live_session_and_for_notes_that_are_not_Ready_and_404_for_a_notes_id_that_is_not_current()
    {
        var (a, live) = await LiveSessionAsync();
        var any = "nt_" + Guid.NewGuid().ToString("N");
        await (await PreviewAsync(a.Client, live, any)).AssertProblemAsync(HttpStatusCode.Conflict, "invalid_state");

        var (b, stopped) = await StoppedSessionAsync();
        var pressId = Guid.NewGuid();
        await InsertPressAsync(stopped, b.UserId, pressId, "Pending");
        await (await PreviewAsync(b.Client, stopped, "nt_" + pressId.ToString("N"))).AssertProblemAsync(HttpStatusCode.Conflict, "invalid_state");

        var l = await ReadyAsync();
        var wrong = await PreviewAsync(l.A.Client, l.SessionId, "nt_" + Guid.NewGuid().ToString("N"));
        await wrong.AssertProblemAsync(HttpStatusCode.NotFound, "not_found");
        var foreign = await PreviewAsync((await NewActorAsync()).Client, l.SessionId, l.NotesId);
        await foreign.AssertProblemAsync(HttpStatusCode.NotFound, "not_found");
        // DXD-12: the wrong notes id and a foreign session look the same (status, body and shape)
        Assert.Equal(StripTrace(await foreign.BodyAsync()), StripTrace(await wrong.BodyAsync()));
        // a notes id of another session of the same user is not this session's current press either
        var other = await ReadyAsync(l.A);
        await (await PreviewAsync(l.A.Client, l.SessionId, other.NotesId)).AssertProblemAsync(HttpStatusCode.NotFound, "not_found");
    }

    [Fact]
    public async Task B12a_Upper_case_braced_urn_and_unhyphenated_session_ids_are_400_before_any_lookup_and_the_canonical_form_works()
    {
        var l = await ReadyAsync();
        var g = Guid.Parse(l.SessionId);
        foreach (var bad in new[] { g.ToString("D").ToUpperInvariant(), "{" + g.ToString("D") + "}", "urn:uuid:" + g.ToString("D"), g.ToString("N") })
        {
            var res = await l.A.Client.SendAsync(Req(HttpMethod.Post, $"/sessions/{Uri.EscapeDataString(bad)}/docs-export/preview", new { notesId = l.NotesId }));
            await res.AssertProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
            await (await l.A.Client.GetAsync($"{Api}/sessions/{Uri.EscapeDataString(bad)}/docs-export")).AssertProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        }
        Assert.Equal(HttpStatusCode.OK, (await PreviewAsync(l.A.Client, l.SessionId, l.NotesId)).StatusCode);
    }

    [Fact]
    public async Task B12_Preview_lists_the_actions_for_none_stale_and_up_to_date()
    {
        var l = await ReadyAsync();
        var rec = await ExportOnceAsync(l);
        Assert.Equal("created", rec.GetProperty("outcome").GetString());

        var same = await PreviewOkAsync(l.A.Client, l.SessionId, l.NotesId);
        Assert.True(same.GetProperty("existing").GetProperty("upToDate").GetBoolean());
        Assert.Empty(same.GetProperty("actions").EnumerateArray());
        Assert.True(same.GetProperty("canWrite").GetBoolean());
        Assert.Equal(DocIdOf(rec), same.GetProperty("existing").GetProperty("docRef").GetProperty("docId").GetString());
        Assert.Equal(rec.GetProperty("exportId").GetString(), same.GetProperty("existing").GetProperty("exportId").GetString());

        await F.ExecAsync($"update lectures.lecture_sessions set title = 'Renamed lecture' where id = '{l.SessionId}'");
        var stale = await PreviewOkAsync(l.A.Client, l.SessionId, l.NotesId);
        Assert.False(stale.GetProperty("existing").GetProperty("upToDate").GetBoolean());
        Assert.Equal(["update", "create_another"], ActionsOf(stale));
        Assert.NotEqual(same.GetProperty("contentDigest").GetString(), stale.GetProperty("contentDigest").GetString());
    }

    // ---------------- confirm: create, replay, conflicts ----------------

    [Fact]
    public async Task B13_B14_First_confirm_creates_one_Demo_Doc_version_1_and_the_same_key_replays_the_same_record_with_the_replay_header()
    {
        var l = await ReadyAsync();
        var p = await PreviewOkAsync(l.A.Client, l.SessionId, l.NotesId);
        var body = ConfirmBody(p, "create");
        var key = NewKey();

        var first = await ConfirmAsync(l.A.Client, l.SessionId, body, key);
        Assert.False(Replayed(first));
        var rec = await OkRecordAsync(first);
        Assert.Equal("succeeded", rec.GetProperty("status").GetString());
        Assert.Equal("created", rec.GetProperty("outcome").GetString());
        Assert.Equal(1, rec.GetProperty("docRef").GetProperty("version").GetInt32());
        Assert.Equal(1, rec.GetProperty("attempt").GetInt32());
        Assert.Equal("in_app", rec.GetProperty("docRef").GetProperty("openKind").GetString());
        Assert.Equal("/demo-docs/" + DocIdOf(rec), rec.GetProperty("docRef").GetProperty("openPath").GetString());
        Assert.Equal(1, await DocRowsAsync("demo_docs", l.A.UserId));
        Assert.Equal(1, await DocRowsAsync("doc_exports", l.A.UserId));
        Assert.Equal(1, await DocRowsAsync("doc_targets", l.A.UserId));
        Assert.Equal(1, await DocRowsAsync("idempotency_keys", l.A.UserId));
        Assert.Equal(1, await DocRowsAsync("audit_events", l.A.UserId, "action = 'export_created' and attempt = 1"));

        var again = await ConfirmAsync(l.A.Client, l.SessionId, body, key);
        Assert.True(Replayed(again));
        var replay = await OkRecordAsync(again);
        Assert.Equal(rec.GetRawText(), replay.GetRawText());          // identical body on the first and the replayed path
        Assert.Equal(1, await DocRowsAsync("demo_docs", l.A.UserId));
        Assert.Equal(1, await DocRowsAsync("audit_events", l.A.UserId));
        Assert.Equal(1, (await (await l.A.Client.GetAsync($"{Api}/demo-docs/{DocIdOf(rec)}")).BodyAsync()).GetProperty("version").GetInt32());
    }

    [Fact]
    public async Task B15_The_same_key_with_a_different_body_is_400_and_changes_nothing()
    {
        var l = await ReadyAsync();
        var p = await PreviewOkAsync(l.A.Client, l.SessionId, l.NotesId);
        var key = NewKey();
        await OkRecordAsync(await ConfirmAsync(l.A.Client, l.SessionId, ConfirmBody(p, "create"), key));
        var different = ConfirmBody(p, "create");
        different["contentDigest"] = "cd_" + new string('A', 43);

        var res = await ConfirmAsync(l.A.Client, l.SessionId, different, key);
        await res.AssertProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        DocsSchemas.AssertValid("docs-problem", await res.BodyAsync());
        Assert.Equal(1, await DocRowsAsync("demo_docs", l.A.UserId));
        Assert.Equal(1, await DocRowsAsync("doc_exports", l.A.UserId));
    }

    [Fact]
    public async Task B16_A_new_key_with_the_same_digest_and_mode_update_writes_nothing_and_reports_unchanged()
    {
        var l = await ReadyAsync();
        var rec = await ExportOnceAsync(l);
        var p = await PreviewOkAsync(l.A.Client, l.SessionId, l.NotesId);
        Assert.True(p.GetProperty("existing").GetProperty("upToDate").GetBoolean());

        var again = await OkRecordAsync(await ConfirmAsync(l.A.Client, l.SessionId, ConfirmBody(p, "update")));
        Assert.Equal("succeeded", again.GetProperty("status").GetString());
        Assert.Equal("unchanged", again.GetProperty("outcome").GetString());
        Assert.Equal(DocIdOf(rec), DocIdOf(again));
        Assert.Equal(1, again.GetProperty("docRef").GetProperty("version").GetInt32());
        Assert.Equal(1, await DocRowsAsync("demo_docs", l.A.UserId));
        Assert.Equal(1, await F.ScalarAsync($"select version from docs.demo_docs where user_id = '{l.A.UserId}'"));
        Assert.Equal(1, await DocRowsAsync("audit_events", l.A.UserId, "action = 'export_unchanged'"));
        // create_another of identical content is not offered, so it is refused
        await (await ConfirmAsync(l.A.Client, l.SessionId, ConfirmBody(p, "create_another"))).AssertProblemAsync(HttpStatusCode.Conflict, "invalid_state");
        Assert.Equal(1, await DocRowsAsync("demo_docs", l.A.UserId));
    }

    [Fact]
    public async Task B17_A_changed_title_then_update_keeps_the_document_and_raises_the_version()
    {
        var l = await ReadyAsync();
        var created = await ExportOnceAsync(l);
        await F.ExecAsync($"update lectures.lecture_sessions set title = 'Mitosis, revised' where id = '{l.SessionId}'");
        var p = await PreviewOkAsync(l.A.Client, l.SessionId, l.NotesId);

        var rec = await OkRecordAsync(await ConfirmAsync(l.A.Client, l.SessionId, ConfirmBody(p, "update")));
        Assert.Equal("updated", rec.GetProperty("outcome").GetString());
        Assert.Equal(DocIdOf(created), DocIdOf(rec));
        Assert.Equal(2, rec.GetProperty("docRef").GetProperty("version").GetInt32());
        var doc = await (await l.A.Client.GetAsync($"{Api}/demo-docs/{DocIdOf(rec)}")).BodyAsync();
        DocsSchemas.AssertValid("docs-demo-doc", doc);
        Assert.StartsWith("Mitosis, revised - ", doc.GetProperty("document").GetProperty("title").GetString());
        Assert.Equal(2, doc.GetProperty("version").GetInt32());
        Assert.Equal(1, await DocRowsAsync("demo_docs", l.A.UserId));
        var after = await PreviewOkAsync(l.A.Client, l.SessionId, l.NotesId);
        Assert.True(after.GetProperty("existing").GetProperty("upToDate").GetBoolean());
        Assert.Equal(2, after.GetProperty("existing").GetProperty("docRef").GetProperty("version").GetInt32());
    }

    [Fact]
    public async Task B18_Create_another_makes_a_second_document_leaves_the_first_untouched_and_moves_the_pointer()
    {
        var l = await ReadyAsync();
        var first = await ExportOnceAsync(l);
        var firstDoc = await (await l.A.Client.GetAsync($"{Api}/demo-docs/{DocIdOf(first)}")).BodyAsync();
        await F.ExecAsync($"update lectures.lecture_sessions set title = 'Second look' where id = '{l.SessionId}'");
        var p = await PreviewOkAsync(l.A.Client, l.SessionId, l.NotesId);

        var rec = await OkRecordAsync(await ConfirmAsync(l.A.Client, l.SessionId, ConfirmBody(p, "create_another")));
        Assert.Equal("created", rec.GetProperty("outcome").GetString());
        Assert.NotEqual(DocIdOf(first), DocIdOf(rec));
        Assert.Equal(1, rec.GetProperty("docRef").GetProperty("version").GetInt32());
        Assert.Equal(2, await DocRowsAsync("demo_docs", l.A.UserId));
        var stillFirst = await (await l.A.Client.GetAsync($"{Api}/demo-docs/{DocIdOf(first)}")).BodyAsync();
        Assert.Equal(firstDoc.GetRawText(), stillFirst.GetRawText());      // the old document is exactly as it was
        var state = await (await l.A.Client.GetAsync($"{Api}/sessions/{l.SessionId}/docs-export")).BodyAsync();
        Assert.Equal(DocIdOf(rec), state.GetProperty("target").GetProperty("docId").GetString());
        Assert.Equal(1, await DocRowsAsync("doc_targets", l.A.UserId));
    }

    [Fact]
    public async Task B19_B20_B20a_A_digest_a_version_or_a_document_that_moved_is_412_writes_nothing_and_stores_no_key()
    {
        var l = await ReadyAsync();
        var p = await PreviewOkAsync(l.A.Client, l.SessionId, l.NotesId);
        var key = NewKey();
        var wrong = ConfirmBody(p, "create"); wrong["contentDigest"] = "cd_" + new string('B', 43);
        var res = await ConfirmAsync(l.A.Client, l.SessionId, wrong, key);
        await res.AssertProblemAsync(HttpStatusCode.PreconditionFailed, "precondition_failed");
        DocsSchemas.AssertValid("docs-problem", await res.BodyAsync());
        Assert.Equal(0, await DocRowsAsync("demo_docs", l.A.UserId));
        Assert.Equal(0, await DocRowsAsync("idempotency_keys", l.A.UserId));   // a 412 leaves the key free
        Assert.Equal(0, await DocRowsAsync("doc_exports", l.A.UserId));
        var ok = await ConfirmAsync(l.A.Client, l.SessionId, ConfirmBody(p, "create"), key);   // the same key, the right digest: a first write, not a replay
        Assert.False(Replayed(ok));
        var a1 = await OkRecordAsync(ok);

        // B20: the document moved on since the preview (here: edited by an update) so the expected version is stale
        await F.ExecAsync($"update lectures.lecture_sessions set title = 'Edited once' where id = '{l.SessionId}'");
        var p2 = await PreviewOkAsync(l.A.Client, l.SessionId, l.NotesId);
        var stale = ConfirmBody(p2, "update"); stale["expectedExistingVersion"] = 7;
        await (await ConfirmAsync(l.A.Client, l.SessionId, stale)).AssertProblemAsync(HttpStatusCode.PreconditionFailed, "precondition_failed");

        // B20a: previewed against A at version 1, then A is deleted and B is created at version 1: a confirm for A must not touch B
        var againstA = ConfirmBody(p2, "update");
        Assert.Equal(HttpStatusCode.NoContent, (await l.A.Client.SendAsync(Req(HttpMethod.Delete, $"/demo-docs/{DocIdOf(a1)}"))).StatusCode);
        var p3 = await PreviewOkAsync(l.A.Client, l.SessionId, l.NotesId);
        var b = await OkRecordAsync(await ConfirmAsync(l.A.Client, l.SessionId, ConfirmBody(p3, "create")));
        Assert.Equal(1, b.GetProperty("docRef").GetProperty("version").GetInt32());
        Assert.NotEqual(DocIdOf(a1), DocIdOf(b));
        var bBefore = await (await l.A.Client.GetAsync($"{Api}/demo-docs/{DocIdOf(b)}")).BodyAsync();
        await (await ConfirmAsync(l.A.Client, l.SessionId, againstA)).AssertProblemAsync(HttpStatusCode.PreconditionFailed, "precondition_failed");
        var bAfter = await (await l.A.Client.GetAsync($"{Api}/demo-docs/{DocIdOf(b)}")).BodyAsync();
        Assert.Equal(bBefore.GetRawText(), bAfter.GetRawText());
        Assert.Equal(1, bAfter.GetProperty("version").GetInt32());
    }

    [Fact]
    public async Task B21_B22_A_mode_that_is_not_offered_is_409_and_a_malformed_confirm_is_400()
    {
        var l = await ReadyAsync();
        var p = await PreviewOkAsync(l.A.Client, l.SessionId, l.NotesId);
        await OkRecordAsync(await ConfirmAsync(l.A.Client, l.SessionId, ConfirmBody(p, "create")));
        // a create when a document already exists: the view the student confirmed is stale
        await (await ConfirmAsync(l.A.Client, l.SessionId, ConfirmBody(p, "create"))).AssertProblemAsync(HttpStatusCode.PreconditionFailed, "precondition_failed");
        Assert.Equal(1, await DocRowsAsync("demo_docs", l.A.UserId));

        var fresh = await ReadyAsync();
        var fp = await PreviewOkAsync(fresh.A.Client, fresh.SessionId, fresh.NotesId);
        var cases = new Dictionary<string, Action<Dictionary<string, object?>>>
        {
            ["no acknowledgement"] = b => b["confirm"] = new Dictionary<string, object?> { ["provider"] = "demo_docs", ["acknowledgedDemo"] = false },
            ["missing acknowledgement"] = b => b["confirm"] = new Dictionary<string, object?> { ["provider"] = "demo_docs" },
            ["a real provider"] = b => b["confirm"] = new Dictionary<string, object?> { ["provider"] = "google_docs", ["acknowledgedDemo"] = true },
            ["update without a document id"] = b => { b["mode"] = "update"; b["expectedExistingVersion"] = 1; b["expectedExistingDocId"] = null; },
            ["create with a version"] = b => b["expectedExistingVersion"] = 1,
            ["missing digest"] = b => b.Remove("contentDigest"),
            ["bad mode"] = b => b["mode"] = "replace",
            ["client sends a document"] = b => b["document"] = new { title = "x", blocks = new object[0] },
            ["bad notes id"] = b => b["notesId"] = "nt_XYZ",
            ["alias document id"] = b => { b["mode"] = "update"; b["expectedExistingVersion"] = 1; b["expectedExistingDocId"] = "DDC_" + Guid.NewGuid(); },
        };
        foreach (var (name, mutate) in cases)
        {
            var body = ConfirmBody(fp, "create");
            mutate(body);
            var res = await ConfirmAsync(fresh.A.Client, fresh.SessionId, body);
            Assert.True(res.StatusCode == HttpStatusCode.BadRequest, name + ": " + (int)res.StatusCode);
            DocsSchemas.AssertValid("docs-problem", await res.BodyAsync());
        }
        foreach (var raw in new[] { "{", "[]", "null", "{\"notesId\":\"nt_" + new string('a', 32) + "\",\"notesId\":\"nt_" + new string('b', 32) + "\"}" })
            await (await fresh.A.Client.SendAsync(Req(HttpMethod.Post, $"/sessions/{fresh.SessionId}/docs-export", raw, NewKey()))).AssertProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        Assert.Equal(0, await DocRowsAsync("doc_exports", fresh.A.UserId));       // none of them stored anything
        Assert.Equal(0, await DocRowsAsync("idempotency_keys", fresh.A.UserId));
    }

    [Fact]
    public async Task B25b_B25a_A_4xx_after_validation_stores_no_key_and_two_users_with_the_same_key_and_body_run_independently()
    {
        var l = await ReadyAsync();
        var p = await PreviewOkAsync(l.A.Client, l.SessionId, l.NotesId);
        var key = NewKey();
        var wrongMode = ConfirmBody(p, "create"); wrongMode["notesId"] = "nt_" + Guid.NewGuid().ToString("N");
        await (await ConfirmAsync(l.A.Client, l.SessionId, wrongMode, key)).AssertProblemAsync(HttpStatusCode.NotFound, "not_found");
        Assert.Equal(0, await DocRowsAsync("idempotency_keys", l.A.UserId));

        var other = await ReadyAsync();
        var p2 = await PreviewOkAsync(other.A.Client, other.SessionId, other.NotesId);
        var shared = NewKey();
        var r1 = await OkRecordAsync(await ConfirmAsync(l.A.Client, l.SessionId, ConfirmBody(p, "create"), shared));
        var r2res = await ConfirmAsync(other.A.Client, other.SessionId, ConfirmBody(p2, "create"), shared);
        Assert.False(Replayed(r2res));
        var r2 = await OkRecordAsync(r2res);
        Assert.NotEqual(r1.GetProperty("exportId").GetString(), r2.GetProperty("exportId").GetString());
        Assert.Equal(1, await DocRowsAsync("demo_docs", l.A.UserId));
        Assert.Equal(1, await DocRowsAsync("demo_docs", other.A.UserId));
    }

    // ---------------- failures, retry, fault injection ----------------

    [Fact]
    public async Task B26_B27_A_provider_failure_is_a_200_failed_record_that_writes_nothing_and_the_same_key_retries_into_one_document()
    {
        var l = await ReadyAsync();
        var p = await PreviewOkAsync(l.A.Client, l.SessionId, l.NotesId);
        var body = ConfirmBody(p, "create");
        var key = NewKey();

        var res = await ConfirmAsync(l.A.Client, l.SessionId, body, key, fault: "unavailable");
        var failed = await OkRecordAsync(res);
        Assert.Equal("failed", failed.GetProperty("status").GetString());
        Assert.Equal("failed", failed.GetProperty("outcome").GetString());
        Assert.Equal("provider_unavailable", failed.GetProperty("error").GetProperty("code").GetString());
        Assert.True(failed.GetProperty("error").GetProperty("retryable").GetBoolean());
        Assert.Equal(5, failed.GetProperty("error").GetProperty("retryAfterSeconds").GetInt32());
        Assert.Equal(JsonValueKind.Null, failed.GetProperty("docRef").ValueKind);
        Assert.True(failed.GetProperty("retry").GetProperty("allowed").GetBoolean());
        Assert.Equal(4, failed.GetProperty("retry").GetProperty("attemptsLeft").GetInt32());
        Assert.Equal(0, await DocRowsAsync("demo_docs", l.A.UserId));
        Assert.Equal(0, await DocRowsAsync("doc_targets", l.A.UserId));
        Assert.Equal(1, await DocRowsAsync("idempotency_keys", l.A.UserId));                // the key is stored together with the failed row
        Assert.Equal(1, await DocRowsAsync("doc_exports", l.A.UserId, "status = 'failed'"));
        Assert.Equal(1, await DocRowsAsync("audit_events", l.A.UserId, "action = 'export_failed' and error_code = 'provider_unavailable'"));

        // the state read shows the failed attempt and no document
        var state = await (await l.A.Client.GetAsync($"{Api}/sessions/{l.SessionId}/docs-export")).BodyAsync();
        DocsSchemas.AssertValid("docs-state", state);
        Assert.Equal(JsonValueKind.Null, state.GetProperty("target").ValueKind);
        Assert.Equal("failed", state.GetProperty("lastExport").GetProperty("status").GetString());

        // the same key and body, the fault cleared: a new write, attempt 2, ONE document
        var ok = await ConfirmAsync(l.A.Client, l.SessionId, body, key);
        Assert.False(Replayed(ok));
        var rec = await OkRecordAsync(ok);
        Assert.Equal("created", rec.GetProperty("outcome").GetString());
        Assert.Equal(2, rec.GetProperty("attempt").GetInt32());
        Assert.Equal(failed.GetProperty("exportId").GetString(), rec.GetProperty("exportId").GetString());
        Assert.Equal(1, await DocRowsAsync("demo_docs", l.A.UserId));
        Assert.Equal(1, await DocRowsAsync("doc_exports", l.A.UserId));
        Assert.Equal(1, await DocRowsAsync("idempotency_keys", l.A.UserId));
        // and now it is a success, so the key replays
        Assert.True(Replayed(await ConfirmAsync(l.A.Client, l.SessionId, body, key)));
    }

    [Fact]
    public async Task B27_A_retry_re_runs_the_checks_so_notes_that_changed_in_between_make_it_412()
    {
        var l = await ReadyAsync();
        var p = await PreviewOkAsync(l.A.Client, l.SessionId, l.NotesId);
        var body = ConfirmBody(p, "create");
        var key = NewKey();
        await OkRecordAsync(await ConfirmAsync(l.A.Client, l.SessionId, body, key, fault: "unavailable"));
        await F.ExecAsync($"update lectures.lecture_sessions set title = 'Changed meanwhile' where id = '{l.SessionId}'");

        await (await ConfirmAsync(l.A.Client, l.SessionId, body, key)).AssertProblemAsync(HttpStatusCode.PreconditionFailed, "precondition_failed");
        Assert.Equal(0, await DocRowsAsync("demo_docs", l.A.UserId));
        Assert.Equal(1, await DocRowsAsync("doc_exports", l.A.UserId, "status = 'failed' and attempt = 1"));   // the failed row is untouched
    }

    [Fact]
    public async Task B28_The_fifth_failed_attempt_ends_retries_and_a_sixth_send_is_409_retry_limit_reached()
    {
        var l = await ReadyAsync();
        var p = await PreviewOkAsync(l.A.Client, l.SessionId, l.NotesId);
        var body = ConfirmBody(p, "create");
        var key = NewKey();
        JsonElement last = default;
        for (var n = 1; n <= 5; n++)
        {
            last = await OkRecordAsync(await ConfirmAsync(l.A.Client, l.SessionId, body, key, fault: "unavailable"));
            Assert.Equal(n, last.GetProperty("attempt").GetInt32());
            Assert.Equal(5 - n, last.GetProperty("retry").GetProperty("attemptsLeft").GetInt32());
        }
        Assert.False(last.GetProperty("retry").GetProperty("allowed").GetBoolean());
        var sixth = await ConfirmAsync(l.A.Client, l.SessionId, body, key);
        await sixth.AssertProblemAsync(HttpStatusCode.Conflict, "retry_limit_reached");
        DocsSchemas.AssertValid("docs-problem", await sixth.BodyAsync());
        Assert.Equal(0, await DocRowsAsync("demo_docs", l.A.UserId));
        // starting over from a new preview with a new key works
        var fresh = await OkRecordAsync(await ConfirmAsync(l.A.Client, l.SessionId, body, NewKey()));
        Assert.Equal("created", fresh.GetProperty("outcome").GetString());
    }

    [Fact]
    public async Task B29_B30_Rate_limited_carries_the_provider_seconds_capped_at_3600_and_a_rejection_is_not_retryable()
    {
        var l = await ReadyAsync();
        var p = await PreviewOkAsync(l.A.Client, l.SessionId, l.NotesId);
        var body = ConfirmBody(p, "create");

        var limited = await OkRecordAsync(await ConfirmAsync(l.A.Client, l.SessionId, body, NewKey(), fault: "rate_limited:99999"));
        Assert.Equal("rate_limited", limited.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(3600, limited.GetProperty("error").GetProperty("retryAfterSeconds").GetInt32());
        Assert.True(limited.GetProperty("error").GetProperty("retryable").GetBoolean());
        var thirty = await OkRecordAsync(await ConfirmAsync(l.A.Client, l.SessionId, body, NewKey(), fault: "rate_limited"));
        Assert.Equal(30, thirty.GetProperty("error").GetProperty("retryAfterSeconds").GetInt32());

        var rejected = await OkRecordAsync(await ConfirmAsync(l.A.Client, l.SessionId, body, NewKey(), fault: "rejected"));
        Assert.Equal("provider_rejected", rejected.GetProperty("error").GetProperty("code").GetString());
        Assert.False(rejected.GetProperty("error").GetProperty("retryable").GetBoolean());
        Assert.Equal(JsonValueKind.Null, rejected.GetProperty("error").GetProperty("retryAfterSeconds").ValueKind);
        Assert.False(rejected.GetProperty("retry").GetProperty("allowed").GetBoolean());
        Assert.Equal(0, await DocRowsAsync("demo_docs", l.A.UserId));
    }

    [Fact]
    public async Task B26_A_failed_update_keeps_the_existing_document_at_its_unchanged_version_and_changes_nothing()
    {
        var l = await ReadyAsync();
        var created = await ExportOnceAsync(l);
        var before = await (await l.A.Client.GetAsync($"{Api}/demo-docs/{DocIdOf(created)}")).BodyAsync();
        await F.ExecAsync($"update lectures.lecture_sessions set title = 'Will not land' where id = '{l.SessionId}'");
        var p = await PreviewOkAsync(l.A.Client, l.SessionId, l.NotesId);

        var failed = await OkRecordAsync(await ConfirmAsync(l.A.Client, l.SessionId, ConfirmBody(p, "update"), NewKey(), fault: "unavailable"));
        Assert.Equal("failed", failed.GetProperty("status").GetString());
        Assert.Equal(DocIdOf(created), DocIdOf(failed));
        Assert.Equal(1, failed.GetProperty("docRef").GetProperty("version").GetInt32());
        var after = await (await l.A.Client.GetAsync($"{Api}/demo-docs/{DocIdOf(created)}")).BodyAsync();
        Assert.Equal(before.GetRawText(), after.GetRawText());
    }

    [Fact]
    public async Task B31_The_demo_never_emits_export_in_progress_or_a_pending_status()
    {
        var l = await ReadyAsync();
        var rec = await ExportOnceAsync(l);
        Assert.DoesNotContain("pending", rec.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, await F.ScalarAsync($"select count(*) from docs.doc_exports where status not in ('succeeded', 'failed')"));
        Assert.Equal(0, await F.ScalarAsync("select count(*) from docs.doc_exports where error_code in ('reconnect_required', 'doc_changed_outside', 'quota_exceeded')"));
    }

    // ---------------- state, Demo Doc read, delete ----------------

    [Fact]
    public async Task B33_B34_The_state_read_and_the_Demo_Doc_read_match_the_preview()
    {
        var l = await ReadyAsync();
        var empty = await (await l.A.Client.GetAsync($"{Api}/sessions/{l.SessionId}/docs-export")).BodyAsync();
        DocsSchemas.AssertValid("docs-state", empty);
        Assert.Equal(JsonValueKind.Null, empty.GetProperty("target").ValueKind);
        Assert.Equal(JsonValueKind.Null, empty.GetProperty("lastExport").ValueKind);

        var p = await PreviewOkAsync(l.A.Client, l.SessionId, l.NotesId);
        var rec = await OkRecordAsync(await ConfirmAsync(l.A.Client, l.SessionId, ConfirmBody(p, "create")));
        var state = await (await l.A.Client.GetAsync($"{Api}/sessions/{l.SessionId}/docs-export")).BodyAsync();
        DocsSchemas.AssertValid("docs-state", state);
        Assert.Equal(DocIdOf(rec), state.GetProperty("target").GetProperty("docId").GetString());
        Assert.Equal(rec.GetRawText(), state.GetProperty("lastExport").GetRawText());

        var res = await l.A.Client.GetAsync($"{Api}/demo-docs/{DocIdOf(rec)}");
        var doc = await res.BodyAsync();
        DocsSchemas.AssertValid("docs-demo-doc", doc);
        Assert.Equal(p.GetProperty("document").GetRawText(), doc.GetProperty("document").GetRawText());   // blocks identical to what was previewed
        Assert.True(doc.GetProperty("demoDoc").GetBoolean());
    }

    [Fact]
    public async Task B35_A_student_delete_removes_the_document_clears_the_pointer_audits_and_lets_a_create_another_leftover_be_deleted_by_id()
    {
        var l = await ReadyAsync();
        var first = await ExportOnceAsync(l);
        await F.ExecAsync($"update lectures.lecture_sessions set title = 'Another copy' where id = '{l.SessionId}'");
        var p = await PreviewOkAsync(l.A.Client, l.SessionId, l.NotesId);
        var second = await OkRecordAsync(await ConfirmAsync(l.A.Client, l.SessionId, ConfirmBody(p, "create_another")));

        // the first one is a leftover (the pointer moved to the second): deleting it leaves the pointer alone
        Assert.Equal(HttpStatusCode.NoContent, (await l.A.Client.SendAsync(Req(HttpMethod.Delete, $"/demo-docs/{DocIdOf(first)}"))).StatusCode);
        Assert.Equal(1, await DocRowsAsync("doc_targets", l.A.UserId));
        await (await l.A.Client.GetAsync($"{Api}/demo-docs/{DocIdOf(first)}")).AssertProblemAsync(HttpStatusCode.NotFound, "not_found");
        await (await l.A.Client.SendAsync(Req(HttpMethod.Delete, $"/demo-docs/{DocIdOf(first)}"))).AssertProblemAsync(HttpStatusCode.NotFound, "not_found");

        // the current one: the pointer is cleared and the next export is a create
        Assert.Equal(HttpStatusCode.NoContent, (await l.A.Client.SendAsync(Req(HttpMethod.Delete, $"/demo-docs/{DocIdOf(second)}"))).StatusCode);
        Assert.Equal(0, await DocRowsAsync("demo_docs", l.A.UserId));
        Assert.Equal(0, await DocRowsAsync("doc_targets", l.A.UserId));
        Assert.Equal(2, await DocRowsAsync("audit_events", l.A.UserId, "action = 'doc_deleted'"));
        var again = await PreviewOkAsync(l.A.Client, l.SessionId, l.NotesId);
        Assert.Equal(["create"], ActionsOf(again));
        var state = await (await l.A.Client.GetAsync($"{Api}/sessions/{l.SessionId}/docs-export")).BodyAsync();
        Assert.Equal(JsonValueKind.Null, state.GetProperty("target").ValueKind);
    }

    // ---------------- the document itself ----------------

    [Fact]
    public async Task B09a_Truncated_notes_say_where_they_stop_in_the_preview_and_in_the_Demo_Doc()
    {
        var l = await ReadyAsync();
        await SetNotesAsync(l.SessionId, Sections(("Mitosis", [("Cells divide.", [2000L])])), truncated: true, coveredToMs: 754_000);
        var p = await PreviewOkAsync(l.A.Client, l.SessionId, l.NotesId);
        Assert.True(p.GetProperty("notesTruncated").GetBoolean());
        var texts = p.GetProperty("document").GetProperty("blocks").EnumerateArray().Select(b => b.GetProperty("text").GetString()).ToList();
        Assert.Contains("These notes stop at 12:34. The lecture ran longer than the notes can cover.", texts);

        var rec = await OkRecordAsync(await ConfirmAsync(l.A.Client, l.SessionId, ConfirmBody(p, "create")));
        var doc = await (await l.A.Client.GetAsync($"{Api}/demo-docs/{DocIdOf(rec)}")).BodyAsync();
        Assert.Contains(doc.GetProperty("document").GetProperty("blocks").EnumerateArray().Select(b => b.GetProperty("text").GetString()), t => t!.StartsWith("These notes stop at 12:34."));
    }

    [Fact]
    public async Task B08_Over_400_blocks_cannot_be_written_nothing_is_cut_silently_and_the_confirm_is_refused()
    {
        var l = await ReadyAsync();
        var many = Enumerable.Range(1, 50).Select(i => ($"Section {i}", Enumerable.Range(1, 8).Select(j => ($"Point {i}.{j}", new[] { (long)(i * 1000 + j) })).ToArray())).ToArray();
        await SetNotesAsync(l.SessionId, Sections(many));
        var p = await PreviewOkAsync(l.A.Client, l.SessionId, l.NotesId);
        Assert.False(p.GetProperty("canWrite").GetBoolean());
        Assert.Equal("too_large", p.GetProperty("blockedReason").GetString());
        Assert.Empty(p.GetProperty("actions").EnumerateArray());
        Assert.Equal(50, p.GetProperty("counts").GetProperty("sections").GetInt32());
        Assert.Equal(400, p.GetProperty("counts").GetProperty("bullets").GetInt32());   // counted over ALL the notes, not the 400 blocks shown
        Assert.Equal(400, p.GetProperty("document").GetProperty("blocks").GetArrayLength());

        var body = ConfirmBody(p, "create");
        await (await ConfirmAsync(l.A.Client, l.SessionId, body)).AssertProblemAsync(HttpStatusCode.Conflict, "invalid_state");
        Assert.Equal(0, await DocRowsAsync("demo_docs", l.A.UserId));

        await SetNotesAsync(l.SessionId, "[]");
        var none = await PreviewOkAsync(l.A.Client, l.SessionId, l.NotesId);
        Assert.Equal("nothing_to_export", none.GetProperty("blockedReason").GetString());
        Assert.False(none.GetProperty("canWrite").GetBoolean());
    }

    private static string C(int codePoint) => char.ConvertFromUtf32(codePoint);

    [Fact]
    public async Task B07_Hostile_text_in_the_notes_is_cleaned_to_one_plain_line_and_a_bullet_without_a_lecture_time_is_dropped_and_counted()
    {
        var l = await ReadyAsync();
        // right-to-left override, zero-width space, isolates, a tag character, CR LF TAB and a control character, all inside one bullet
        var evil = string.Concat("Safe ", C(0x202E), "evil", C(0x202C), " ", C(0x200B), "text", C(0x2066), "x", C(0x2069), "\r\n\ttail ", C(0xE0041), " ", C(1), "end\n");
        await SetNotesAsync(l.SessionId, Sections(("Head" + C(0x202E) + " ing\n", [(evil, [1000L]), ("[x](javascript:alert(1)) <img onerror=x>", [2000L]), ("No time at all", Array.Empty<long>())])));
        var p = await PreviewOkAsync(l.A.Client, l.SessionId, l.NotesId);
        var blocks = p.GetProperty("document").GetProperty("blocks").EnumerateArray().ToList();
        var bullets = blocks.Where(b => b.GetProperty("kind").GetString() == "bullet").Select(b => b.GetProperty("text").GetString()).ToList();
        Assert.Equal(2, bullets.Count);                                             // the one without a time is not a notes bullet
        Assert.Equal("Safe evil textx tail end", bullets[0]);
        Assert.Equal("[x](javascript:alert(1)) <img onerror=x>", bullets[1]);       // stays literal text; the page renders text only
        Assert.Contains(blocks, b => b.GetProperty("kind").GetString() == "heading" && b.GetProperty("text").GetString() == "Head ing");
        Assert.Equal(2, p.GetProperty("counts").GetProperty("bullets").GetInt32());
        foreach (var b in blocks)
            foreach (var ch in b.GetProperty("text").GetString()!)
                Assert.False(char.IsControl(ch) || ch == (char)0x202E || ch == (char)0x200B || ch == (char)0x2066, "control or bidi character left in the document");
    }

    [Fact]
    public async Task B06_The_Final_summary_is_in_the_document_only_when_this_press_made_it()
    {
        var l = await ReadyAsync();
        var notes = await (await l.A.Client.GetAsync($"{Api}/sessions/{l.SessionId}/notes")).BodyAsync();
        var p = await PreviewOkAsync(l.A.Client, l.SessionId, l.NotesId);
        var headings = p.GetProperty("document").GetProperty("blocks").EnumerateArray().Where(b => b.GetProperty("kind").GetString() == "heading").Select(b => b.GetProperty("text").GetString()).ToList();
        var ready = notes.GetProperty("finalSummaryStatus").GetString() == "Ready";
        Assert.Equal(ready, headings.Contains("Final summary"));
        // simulate a press whose Final step failed: the revision is not in the document
        await F.ExecAsync($"update ai.lecture_notes set final_summary_state = 'Unavailable', final_summary_revision = null where session_id = '{l.SessionId}'");
        var p2 = await PreviewOkAsync(l.A.Client, l.SessionId, l.NotesId);
        Assert.DoesNotContain("Final summary", p2.GetProperty("document").GetProperty("blocks").EnumerateArray().Where(b => b.GetProperty("kind").GetString() == "heading").Select(b => b.GetProperty("text").GetString()));
    }

    [Fact]
    public async Task The_200th_document_is_the_last_one_the_201st_is_refused_by_the_cap()
    {
        var l = await ReadyAsync();
        // 199 stand-in rows for this user (the cap counts rows, not lectures), inserted directly so the test does not need 200 lectures
        await F.ExecAsync($"insert into docs.demo_docs (id, user_id, session_id, title, blocks, version, created_at, updated_at, origin_export_id) " +
                          $"select gen_random_uuid(), '{l.A.UserId}', '{l.SessionId}', 'filler', '[{{\"kind\":\"paragraph\",\"text\":\"x\"}}]'::jsonb, 1, now(), now(), gen_random_uuid() from generate_series(1, 199)");
        var p = await PreviewOkAsync(l.A.Client, l.SessionId, l.NotesId);
        var last = await OkRecordAsync(await ConfirmAsync(l.A.Client, l.SessionId, ConfirmBody(p, "create")));   // the 200th
        Assert.Equal("created", last.GetProperty("outcome").GetString());
        Assert.Equal(200, await DocRowsAsync("demo_docs", l.A.UserId));

        await F.ExecAsync($"update lectures.lecture_sessions set title = 'one more' where id = '{l.SessionId}'");
        var p2 = await PreviewOkAsync(l.A.Client, l.SessionId, l.NotesId);
        await (await ConfirmAsync(l.A.Client, l.SessionId, ConfirmBody(p2, "create_another"))).AssertProblemAsync(HttpStatusCode.Conflict, "invalid_state");
        Assert.Equal(200, await DocRowsAsync("demo_docs", l.A.UserId));
        // an update is still allowed at the cap: it adds no document
        var upd = await OkRecordAsync(await ConfirmAsync(l.A.Client, l.SessionId, ConfirmBody(p2, "update")));
        Assert.Equal("updated", upd.GetProperty("outcome").GetString());
        Assert.Equal(200, await DocRowsAsync("demo_docs", l.A.UserId));
    }
}
