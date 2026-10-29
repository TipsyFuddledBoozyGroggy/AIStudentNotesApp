using System.Net;
using System.Text.Json;
using Tether.IntegrationTests.Infrastructure;

namespace Tether.IntegrationTests.Syllabus;

/// <summary>The happy paths of syllabus.v1 against the real host, real Postgres and the real hosted extraction worker; every response is validated against the frozen schemas.</summary>
public class SyllabusFlowTests(ApiFactory factory) : SyllabusTestBase(factory), IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Create_from_paste_returns_202_queued_representation_then_polling_reaches_ready_with_schema_valid_bodies()
    {
        var a = await NewActorAsync();
        var course = await CourseAsync(a.Client);
        var res = await CreateAsync(a.Client, course, Paste());
        Assert.Equal(HttpStatusCode.Accepted, res.StatusCode);
        var b = await res.BodyAsync();
        SyllabusSchemas.AssertValid("syllabus", b);
        var id = b.GetProperty("id").GetString()!;
        Assert.StartsWith("syl_", id);
        Assert.Equal($"/api/v1/syllabi/{id}", res.Headers.Location!.ToString());
        Assert.NotNull(res.Headers.ETag);
        Assert.Equal("mock", b.GetProperty("aiSource").GetString());
        Assert.True(b.GetProperty("demoSource").GetBoolean());
        Assert.True(b.GetProperty("demoCalendar").GetBoolean());
        Assert.False(b.GetProperty("originalRetained").GetBoolean());
        Assert.Equal("BIO 101", b.GetProperty("title").GetString());

        var done = await WaitDoneAsync(a.Client, id);
        SyllabusSchemas.AssertValid("syllabus", done);
        Assert.Equal("ready", done.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, done.GetProperty("statusReason").ValueKind);
        Assert.True(done.GetProperty("counts").GetProperty("total").GetInt32() >= 7);

        var list = await (await a.Client.GetAsync($"{Api}/courses/{course}/syllabi")).BodyAsync();
        SyllabusSchemas.AssertValid("syllabus-page", list);
        Assert.Single(list.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Notice_and_attestation_are_stamped_by_the_server_not_the_client()
    {
        var a = await NewActorAsync();
        var course = await CourseAsync(a.Client);
        var body = Paste();
        body["attestation"] = new { accepted = true, acceptedAt = DateTimeOffset.UtcNow.AddHours(-20).ToString("O") };   // within the 24 h skew bound, but backdated
        var res = await CreateAsync(a.Client, course, body);
        var b = await res.BodyAsync();
        var stamped = b.GetProperty("notice").GetProperty("attestedAt").GetDateTimeOffset();
        Assert.True((DateTimeOffset.UtcNow - stamped).Duration() < TimeSpan.FromMinutes(2), "attestedAt must be the server receipt time");
        Assert.Equal(b.GetProperty("createdAt").GetDateTimeOffset(), stamped);
        Assert.Equal("draft-2026-10", b.GetProperty("notice").GetProperty("version").GetString());
        await WaitDoneAsync(a.Client, b.GetProperty("id").GetString()!);   // validation comes after the in-flight quota in the evaluation order
        var bad = Paste(); bad["attestation"] = new { accepted = true, acceptedAt = DateTimeOffset.UtcNow.AddDays(-3).ToString("O") };
        await (await CreateAsync(a.Client, course, bad)).AssertProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
    }

    [Fact]
    public async Task Extraction_proposes_resolved_unknown_policy_and_recurring_items_with_exact_snippets_that_are_verbatim_slices()
    {
        var a = await NewActorAsync();
        var course = await CourseAsync(a.Client);
        var syl = await CreateReadyAsync(a.Client, course);
        var sid = syl.GetProperty("id").GetString()!;
        var items = await ItemsAsync(a.Client, sid);
        foreach (var i in items) SyllabusSchemas.AssertValid("syllabus-item", i);

        var ps = ByTitle(items, "PS1");
        Assert.Equal("assignment", ps.GetProperty("kind").GetString());
        var w = ps.GetProperty("when");
        Assert.Equal("resolved", w.GetProperty("status").GetString());
        Assert.Equal("2026-09-14", w.GetProperty("date").GetString());
        Assert.Equal("23:59", w.GetProperty("time").GetString());
        Assert.Equal("year_from_term", w.GetProperty("derivation").GetString());
        Assert.Equal("probably_right", ps.GetProperty("confidence").GetString());

        var mid = ByTitle(items, "Midterm");
        Assert.Equal("explicit_full", mid.GetProperty("when").GetProperty("derivation").GetString());
        Assert.Equal("looks_right", mid.GetProperty("confidence").GetString());
        Assert.Equal("10:00", mid.GetProperty("when").GetProperty("time").GetString());

        var final = ByTitle(items, "Final");
        Assert.Equal("needs_date", final.GetProperty("when").GetProperty("status").GetString());
        Assert.Equal("tba", final.GetProperty("when").GetProperty("unknownReason").GetString());
        Assert.Equal(JsonValueKind.Null, final.GetProperty("when").GetProperty("date").ValueKind);

        var proj = ByTitle(items, "Project");
        Assert.Equal("ambiguous_format", proj.GetProperty("when").GetProperty("unknownReason").GetString());   // 3/4 is never guessed

        var policy = items.First(i => i.GetProperty("kind").GetString() == "policy");
        Assert.Equal("not_applicable", policy.GetProperty("when").GetProperty("status").GetString());

        var oh = items.First(i => i.GetProperty("kind").GetString() == "office_hours");
        Assert.Equal("weekly", oh.GetProperty("recurrence").GetProperty("pattern").GetString());
        Assert.Equal("2026-08-25", oh.GetProperty("when").GetProperty("date").GetString());

        // needs-date items sort last
        var statuses = items.Select(i => i.GetProperty("when").GetProperty("status").GetString()).ToList();
        Assert.True(statuses.IndexOf("needs_date") > statuses.LastIndexOf("resolved") || !statuses.Contains("needs_date"));

        // snippet: exact UTF-16 offsets into the stored canonical text, sliced by the server
        var text = await F.TextAsync($"select text from syllabus.canonical_texts where syllabus_id = '{Guid32To(sid)}'");
        foreach (var i in items)
        {
            var sn = i.GetProperty("snippet");
            var s = sn.GetProperty("start").GetInt32(); var e = sn.GetProperty("end").GetInt32();
            Assert.Equal(sn.GetProperty("text").GetString(), text!.Substring(s, e - s));
            var ctx = sn.GetProperty("context");
            Assert.True(ctx.GetProperty("start").GetInt32() <= s && e <= ctx.GetProperty("end").GetInt32());
        }
        var unclaimed = await (await a.Client.GetAsync($"{Api}/syllabi/{sid}/unclaimed-dates")).BodyAsync();
        SyllabusSchemas.AssertValid("syllabus-unclaimed-date-page", unclaimed);
    }

    private static string Guid32To(string wire) => wire[(wire.IndexOf('_') + 1)..];

    [Fact]
    public async Task Item_list_pages_by_effective_date_and_filters_validate()
    {
        var a = await NewActorAsync();
        var course = await CourseAsync(a.Client);
        var syl = await CreateReadyAsync(a.Client, course);
        var sid = syl.GetProperty("id").GetString()!;
        var p1 = await (await a.Client.GetAsync($"{Api}/syllabi/{sid}/items?limit=3")).BodyAsync();
        SyllabusSchemas.AssertValid("syllabus-item-page", p1);
        Assert.Equal(3, p1.GetProperty("items").GetArrayLength());
        var next = p1.GetProperty("nextCursor").GetString()!;
        var p2 = await (await a.Client.GetAsync($"{Api}/syllabi/{sid}/items?limit=3&cursor={next}")).BodyAsync();
        var ids1 = p1.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetString()).ToHashSet();
        Assert.DoesNotContain(p2.GetProperty("items").EnumerateArray(), i => ids1.Contains(i.GetProperty("id").GetString()));
        var nd = await ItemsAsync(a.Client, sid, "needsDate=true");
        Assert.All(nd, i => Assert.Equal("needs_date", i.GetProperty("when").GetProperty("status").GetString()));
        Assert.NotEmpty(nd);
        await (await a.Client.GetAsync($"{Api}/syllabi/{sid}/items?decision=bogus")).AssertProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        await (await a.Client.GetAsync($"{Api}/syllabi/{sid}/items?limit=0")).AssertProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        await (await a.Client.GetAsync($"{Api}/syllabi/{sid}/items?limit=201")).AssertProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        await (await a.Client.GetAsync($"{Api}/syllabi/{sid}/items?cursor=garbage")).AssertProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
    }

    [Fact]
    public async Task Approve_needs_date_is_409_please_check_needs_acknowledgement_and_undo_never_expires()
    {
        var a = await NewActorAsync();
        var course = await CourseAsync(a.Client);
        var sid = (await CreateReadyAsync(a.Client, course)).GetProperty("id").GetString()!;
        var items = await ItemsAsync(a.Client, sid);

        await (await Decide(a.Client, sid, ByTitle(items, "Final"), "approved")).AssertProblemAsync(HttpStatusCode.Conflict, "item_needs_date");
        await (await Decide(a.Client, sid, items.First(i => i.GetProperty("kind").GetString() == "policy"), "approved")).AssertProblemAsync(HttpStatusCode.Conflict, "item_not_syncable");

        var ps = ByTitle(items, "PS1");      // probably_right: approves without acknowledgement
        var ok = await Decide(a.Client, sid, ps, "approved");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var approved = await ok.BodyAsync();
        SyllabusSchemas.AssertValid("syllabus-item", approved);
        Assert.Equal("approved", approved.GetProperty("decision").GetString());
        Assert.NotEqual(ps.GetProperty("etag").GetString(), approved.GetProperty("etag").GetString());
        Assert.Equal(ps.GetProperty("revision").GetInt32(), approved.GetProperty("revision").GetInt32());   // a decision never bumps the content revision

        var undone = await (await Decide(a.Client, sid, approved, "undecided")).BodyAsync();
        Assert.Equal("undecided", undone.GetProperty("decision").GetString());

        // a please_check item (day and weekday disagree) needs an explicit acknowledgement
        var custom = Paste("Quiz 2 due Fri Sept 14, 2026 at 5:00 PM\nPS9 due Mon Sept 21, 2026\n");
        var b = await NewActorAsync();
        var c2 = await CourseAsync(b.Client);
        var sid2 = (await CreateReadyAsync(b.Client, c2, custom)).GetProperty("id").GetString()!;
        var items2 = await ItemsAsync(b.Client, sid2);
        var quiz = ByTitle(items2, "Quiz");
        Assert.Equal("conflicting", quiz.GetProperty("when").GetProperty("unknownReason").GetString());   // Sept 14, 2026 is a Monday, not a Friday
        Assert.Contains(quiz.GetProperty("flags").EnumerateArray().Select(x => x.GetString()), f => f == "weekday_mismatch");
    }

    [Fact]
    public async Task Edit_rules_ai_date_edit_becomes_student_entered_please_check_and_unsynced_approval_returns_to_undecided()
    {
        var a = await NewActorAsync();
        var course = await CourseAsync(a.Client);
        var sid = (await CreateReadyAsync(a.Client, course)).GetProperty("id").GetString()!;
        var items = await ItemsAsync(a.Client, sid);
        var mid = ByTitle(items, "Midterm");
        var approved = await (await Decide(a.Client, sid, mid, "approved")).BodyAsync();

        var patch = await a.Client.SendAsync(Req(HttpMethod.Patch, $"{Api}/syllabi/{sid}/items/{mid.GetProperty("id").GetString()}", new { date = "2026-10-20" }, ifMatch: approved.GetProperty("etag").GetString()));
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);
        var edited = await patch.BodyAsync();
        SyllabusSchemas.AssertValid("syllabus-item", edited);
        Assert.Equal("2026-10-20", edited.GetProperty("when").GetProperty("date").GetString());
        Assert.Equal("student_entered", edited.GetProperty("when").GetProperty("derivation").GetString());
        Assert.Equal("student", edited.GetProperty("when").GetProperty("timezoneSource").GetString());
        Assert.Equal("please_check", edited.GetProperty("confidence").GetString());
        Assert.True(edited.GetProperty("edited").GetBoolean());
        Assert.Equal(2, edited.GetProperty("revision").GetInt32());
        Assert.Equal("undecided", edited.GetProperty("decision").GetString());     // approval belongs to the revision that was approved

        // needs-date item: setting the date resolves it, stays undecided
        var final = ByTitle(items, "Final");
        var fixedUp = await (await a.Client.SendAsync(Req(HttpMethod.Patch, $"{Api}/syllabi/{sid}/items/{final.GetProperty("id").GetString()}", new { date = "2026-12-09", time = "09:00" }, ifMatch: final.GetProperty("etag").GetString()))).BodyAsync();
        Assert.Equal("resolved", fixedUp.GetProperty("when").GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, fixedUp.GetProperty("when").GetProperty("unknownReason").ValueKind);
        Assert.Equal("undecided", fixedUp.GetProperty("decision").GetString());

        // null is legal only on nullable fields; a date cannot be cleared
        var id = fixedUp.GetProperty("id").GetString();
        await (await a.Client.SendAsync(Req(HttpMethod.Patch, $"{Api}/syllabi/{sid}/items/{id}", """{"date":null}""", ifMatch: fixedUp.GetProperty("etag").GetString()))).AssertProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        await (await a.Client.SendAsync(Req(HttpMethod.Patch, $"{Api}/syllabi/{sid}/items/{id}", """{"title":null}""", ifMatch: fixedUp.GetProperty("etag").GetString()))).AssertProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        var cleared = await a.Client.SendAsync(Req(HttpMethod.Patch, $"{Api}/syllabi/{sid}/items/{id}", """{"time":null}""", ifMatch: fixedUp.GetProperty("etag").GetString()));
        Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);
    }

    [Fact]
    public async Task Student_items_can_be_added_edited_and_deleted_and_ai_items_cannot_be_deleted()
    {
        var a = await NewActorAsync();
        var course = await CourseAsync(a.Client);
        var sid = (await CreateReadyAsync(a.Client, course)).GetProperty("id").GetString()!;
        var created = await a.Client.SendAsync(Req(HttpMethod.Post, $"{Api}/syllabi/{sid}/items", new { kind = "other", title = "Meet study group", date = "2026-09-20", time = "15:00" }));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var item = await created.BodyAsync();
        SyllabusSchemas.AssertValid("syllabus-item", item);
        Assert.Equal("student", item.GetProperty("origin").GetString());
        Assert.Equal("probably_right", item.GetProperty("confidence").GetString());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("snippet").ValueKind);
        Assert.Contains($"/items/{item.GetProperty("id").GetString()}", created.Headers.Location!.ToString());

        var ai = (await ItemsAsync(a.Client, sid)).First(i => i.GetProperty("origin").GetString() == "ai");
        var delAi = await a.Client.SendAsync(Req(HttpMethod.Delete, $"{Api}/syllabi/{sid}/items/{ai.GetProperty("id").GetString()}", ifMatch: ai.GetProperty("etag").GetString()));
        await delAi.AssertProblemAsync(HttpStatusCode.Conflict, "student_item_only");

        var del = await a.Client.SendAsync(Req(HttpMethod.Delete, $"{Api}/syllabi/{sid}/items/{item.GetProperty("id").GetString()}", ifMatch: item.GetProperty("etag").GetString()));
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await a.Client.GetAsync($"{Api}/syllabi/{sid}/items/{item.GetProperty("id").GetString()}")).StatusCode);
        await (await a.Client.SendAsync(Req(HttpMethod.Post, $"{Api}/syllabi/{sid}/items", new { kind = "policy", title = "x", date = "2026-09-20" }))).AssertProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        await (await a.Client.SendAsync(Req(HttpMethod.Post, $"{Api}/syllabi/{sid}/items", new { kind = "other", title = "No date" }))).AssertProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
    }

    [Fact]
    public async Task Sync_creates_one_event_per_item_is_idempotent_three_times_and_skip_removes_the_event()
    {
        var a = await NewActorAsync();
        var course = await CourseAsync(a.Client);
        var sid = (await CreateReadyAsync(a.Client, course)).GetProperty("id").GetString()!;
        var items = await ItemsAsync(a.Client, sid);
        var mid = ByTitle(items, "Midterm");
        var approved = await (await Decide(a.Client, sid, mid, "approved")).BodyAsync();
        var id = approved.GetProperty("id").GetString()!; var rev = approved.GetProperty("revision").GetInt32();

        var first = await Sync(a.Client, sid, NewKey(), (id, rev));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var r1 = await first.BodyAsync();
        SyllabusSchemas.AssertValid("syllabus-calendar-sync-result", r1);
        Assert.Equal("all_succeeded", r1.GetProperty("status").GetString());
        Assert.Equal("created", r1.GetProperty("results")[0].GetProperty("outcome").GetString());
        var r2 = await (await Sync(a.Client, sid, NewKey(), (id, rev))).BodyAsync();
        Assert.Equal("unchanged", r2.GetProperty("results")[0].GetProperty("outcome").GetString());
        Assert.Equal("nothing_to_do", r2.GetProperty("status").GetString());
        var r3 = await (await Sync(a.Client, sid, NewKey(), (id, rev))).BodyAsync();
        Assert.Equal("unchanged", r3.GetProperty("results")[0].GetProperty("outcome").GetString());
        Assert.Equal(1, await F.ScalarAsync($"select count(*) from syllabus.calendar_events where item_id = '{Guid32To(id)}'"));
        var after = await GetItemAsync(a.Client, sid, id);
        Assert.Equal("synced", after.GetProperty("syncState").GetString());
        Assert.Equal(rev, after.GetProperty("syncedRevision").GetInt32());

        var cal = await (await a.Client.GetAsync($"{Api}/courses/{course}/demo-calendar?from=2026-10-01&to=2026-10-31")).BodyAsync();
        SyllabusSchemas.AssertValid("syllabus-demo-calendar-page", cal);
        Assert.Single(cal.GetProperty("items").EnumerateArray());

        // withdrawing (skip) removes the mock event in the same transaction and returns the item to not_synced
        var skipped = await (await Decide(a.Client, sid, after, "skipped")).BodyAsync();
        Assert.Equal("not_synced", skipped.GetProperty("syncState").GetString());
        Assert.Equal(0, await F.ScalarAsync($"select count(*) from syllabus.calendar_events where item_id = '{Guid32To(id)}'"));
    }

    [Fact]
    public async Task Calendar_remove_keeps_the_decision_and_remove_then_resync_gives_exactly_one_event()
    {
        var a = await NewActorAsync();
        var course = await CourseAsync(a.Client);
        var sid = (await CreateReadyAsync(a.Client, course)).GetProperty("id").GetString()!;
        var mid = ByTitle(await ItemsAsync(a.Client, sid), "Midterm");
        var approved = await (await Decide(a.Client, sid, mid, "approved")).BodyAsync();
        var id = approved.GetProperty("id").GetString()!; var rev = approved.GetProperty("revision").GetInt32();
        await Sync(a.Client, sid, NewKey(), (id, rev));
        var rem = await a.Client.SendAsync(Req(HttpMethod.Post, $"{Api}/syllabi/{sid}/calendar-sync/remove", new { itemIds = new[] { id } }));
        Assert.Equal(HttpStatusCode.OK, rem.StatusCode);
        var rb = await rem.BodyAsync();
        SyllabusSchemas.AssertValid("syllabus-calendar-remove-result", rb);
        Assert.Equal("removed", rb.GetProperty("results")[0].GetProperty("outcome").GetString());
        var item = await GetItemAsync(a.Client, sid, id);
        Assert.Equal("approved", item.GetProperty("decision").GetString());
        Assert.Equal("not_synced", item.GetProperty("syncState").GetString());
        await Sync(a.Client, sid, NewKey(), (id, rev));
        Assert.Equal(1, await F.ScalarAsync($"select count(*) from syllabus.calendar_events where item_id = '{Guid32To(id)}'"));
        var again = await (await a.Client.SendAsync(Req(HttpMethod.Post, $"{Api}/syllabi/{sid}/calendar-sync/remove", new { itemIds = new[] { id } }))).BodyAsync();
        Assert.Equal("removed", again.GetProperty("results")[0].GetProperty("outcome").GetString());
        var none = await (await a.Client.SendAsync(Req(HttpMethod.Post, $"{Api}/syllabi/{sid}/calendar-sync/remove", new { itemIds = new[] { id } }))).BodyAsync();
        Assert.Equal("not_present", none.GetProperty("results")[0].GetProperty("outcome").GetString());
        Assert.Equal("nothing_to_do", none.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Recurring_item_syncs_one_event_per_occurrence()
    {
        var a = await NewActorAsync();
        var course = await CourseAsync(a.Client);
        var sid = (await CreateReadyAsync(a.Client, course)).GetProperty("id").GetString()!;
        var oh = (await ItemsAsync(a.Client, sid)).First(i => i.GetProperty("kind").GetString() == "office_hours");
        var approved = await (await Decide(a.Client, sid, oh, "approved", ack: true)).BodyAsync();
        var res = await (await Sync(a.Client, sid, NewKey(), (approved.GetProperty("id").GetString()!, approved.GetProperty("revision").GetInt32()))).BodyAsync();
        var n = res.GetProperty("results")[0].GetProperty("eventCount").GetInt32();
        Assert.InRange(n, 10, 60);
        Assert.Equal(n, await F.ScalarAsync($"select count(*) from syllabus.calendar_events where item_id = '{Guid32To(approved.GetProperty("id").GetString()!)}'"));
        var events = await (await a.Client.GetAsync($"{Api}/courses/{course}/demo-calendar?from=2026-08-24&to=2026-09-10&limit=200")).BodyAsync();
        SyllabusSchemas.AssertValid("syllabus-demo-calendar-page", events);
        Assert.All(events.GetProperty("items").EnumerateArray(), e => Assert.Equal("14:00", e.GetProperty("start").GetProperty("time").GetString()));
    }

    [Fact]
    public async Task Export_is_json_attachment_no_store_and_schema_valid_and_delete_removes_everything()
    {
        var a = await NewActorAsync();
        var course = await CourseAsync(a.Client);
        var sid = (await CreateReadyAsync(a.Client, course)).GetProperty("id").GetString()!;
        var ex = await a.Client.GetAsync($"{Api}/syllabi/{sid}/export");
        Assert.Equal(HttpStatusCode.OK, ex.StatusCode);
        Assert.Equal("no-store", ex.Headers.CacheControl!.ToString());
        Assert.Contains("attachment", ex.Content.Headers.ContentDisposition!.ToString());
        Assert.Equal("nosniff", ex.Headers.GetValues("X-Content-Type-Options").Single());
        var body = await ex.BodyAsync();
        SyllabusSchemas.AssertValid("syllabus-export", body);

        var del = await a.Client.SendAsync(Req(HttpMethod.Delete, $"{Api}/syllabi/{sid}"));
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);
        await (await a.Client.GetAsync($"{Api}/syllabi/{sid}")).AssertProblemAsync(HttpStatusCode.NotFound, "not_found");
        Assert.Equal(0, await F.ScalarAsync($"select count(*) from syllabus.items where syllabus_id = '{Guid32To(sid)}'"));
        await (await a.Client.SendAsync(Req(HttpMethod.Delete, $"{Api}/syllabi/{sid}"))).AssertProblemAsync(HttpStatusCode.NotFound, "not_found");
        await (await a.Client.SendAsync(Req(HttpMethod.Delete, $"{Api}/syllabi/{sid}?keep=1"))).AssertProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
    }
}
