using System.Net;
using System.Text.Json;
using Tether.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tether.Syllabus.Application;

namespace Tether.IntegrationTests.Syllabus;

/// <summary>TOCTOU interleavings (stale tabs, sync vs withdraw, concurrent sync, delete during a running job), per-bucket rate limits and the startup guards.</summary>
[Collection(RacePool.Name)]
public class SyllabusConcurrencyTests(SyllabusConcurrencyTests.Factory f) : SyllabusTestBase(f), IClassFixture<SyllabusConcurrencyTests.Factory>
{
    public sealed class Factory : ApiFactory
    {
        // each extraction stage takes a moment, so a DELETE can land while the job is running
        protected override IEnumerable<KeyValuePair<string, string?>> Settings => [new("Syllabus:Mock:StageDelayMilliseconds", "700")];
        protected override int MaxPoolSize => RacePool.Size; // NAS-R09: 12 concurrent syncs
    }

    private const string Text = "Midterm exam: Oct 19, 2026 at 10:00 AM\nFinal exam: Dec 9, 2026 at 8:00 AM\nQuiz 1 due Sept 14, 2026";

    [Fact]
    public async Task A_stale_tab_approve_skip_pair_has_one_winner_and_the_loser_gets_412()
    {
        var a = await NewActorAsync(); var course = await CourseAsync(a.Client);
        var sid = (await CreateReadyAsync(a.Client, course, Paste(Text))).GetProperty("id").GetString()!;
        var item = ByTitle(await ItemsAsync(a.Client, sid), "Midterm");
        // sequential: the second tab holds the old tag
        Assert.Equal(HttpStatusCode.OK, (await Decide(a.Client, sid, item, "approved")).StatusCode);
        await (await Decide(a.Client, sid, item, "skipped")).AssertProblemAsync(HttpStatusCode.PreconditionFailed, "precondition_failed");
        // concurrent: both tabs fire at once with the same tag
        var fresh = await GetItemAsync(a.Client, sid, item.GetProperty("id").GetString()!);
        var both = await Task.WhenAll(Decide(a.Client, sid, fresh, "undecided"), Decide(a.Client, sid, fresh, "skipped"));
        Assert.Single(both, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(both, r => r.StatusCode == HttpStatusCode.PreconditionFailed);
    }

    [Fact]
    public async Task Concurrent_syncs_yield_exactly_one_event_per_item_and_no_event_survives_a_concurrent_withdrawal()
    {
        var a = await NewActorAsync(); var course = await CourseAsync(a.Client);
        var sid = (await CreateReadyAsync(a.Client, course, Paste(Text))).GetProperty("id").GetString()!;
        var items = await ItemsAsync(a.Client, sid);
        var approved = new List<JsonElement>();
        foreach (var i in items.Where(i => i.GetProperty("confidence").GetString() == "looks_right").Take(2))
            approved.Add(await (await Decide(a.Client, sid, i, "approved")).BodyAsync());
        var pairs = approved.Select(i => (i.GetProperty("id").GetString()!, i.GetProperty("revision").GetInt32())).ToArray();

        var syncs = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => Sync(a.Client, sid, NewKey(), pairs)));
        Assert.All(syncs, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.Equal(pairs.Length, await F.ScalarAsync($"select count(*) from syllabus.calendar_events where syllabus_id = '{Guid32(sid)}'"));
        var outcomes = new List<string>();
        foreach (var r in syncs) foreach (var x in (await r.BodyAsync()).GetProperty("results").EnumerateArray()) outcomes.Add(x.GetProperty("outcome").GetString()!);
        Assert.Equal(pairs.Length, outcomes.Count(o => o == "created"));          // each item is created exactly once; the rest are unchanged

        // withdrawal races sync: afterwards, an event exists only for an item that is still approved and synced
        for (var round = 0; round < 6; round++)
        {
            var current = new List<JsonElement>();
            foreach (var (id, _) in pairs) current.Add(await GetItemAsync(a.Client, sid, id));
            var withdraw = Decide(a.Client, sid, current[0], current[0].GetProperty("decision").GetString() == "approved" ? "skipped" : "approved");
            var sync = Sync(a.Client, sid, NewKey(), [(current[0].GetProperty("id").GetString()!, current[0].GetProperty("revision").GetInt32())]);
            await Task.WhenAll(withdraw, sync);
            var now = await GetItemAsync(a.Client, sid, current[0].GetProperty("id").GetString()!);
            var events = await F.ScalarAsync($"select count(*) from syllabus.calendar_events where item_id = '{Guid32(current[0].GetProperty("id").GetString()!)}'");
            if (now.GetProperty("decision").GetString() != "approved") Assert.Equal(0, events);               // no event for a withdrawn item, ever
            if (events > 0) Assert.Equal("synced", now.GetProperty("syncState").GetString());
            if (now.GetProperty("syncState").GetString() == "synced") Assert.Equal(1, events);
        }
    }

    [Fact]
    public async Task A_patch_racing_a_sync_either_syncs_the_old_revision_or_refuses_never_a_torn_state()
    {
        var a = await NewActorAsync(); var course = await CourseAsync(a.Client);
        var sid = (await CreateReadyAsync(a.Client, course, Paste(Text))).GetProperty("id").GetString()!;
        var item = ByTitle(await ItemsAsync(a.Client, sid), "Midterm");
        var approved = await (await Decide(a.Client, sid, item, "approved")).BodyAsync();
        var id = approved.GetProperty("id").GetString()!; var rev = approved.GetProperty("revision").GetInt32();
        var patch = a.Client.SendAsync(Req(HttpMethod.Patch, $"{Api}/syllabi/{sid}/items/{id}", new { time = "11:00" }, ifMatch: approved.GetProperty("etag").GetString()));
        var sync = Sync(a.Client, sid, NewKey(), (id, rev));
        await Task.WhenAll(patch, sync);
        var final = await GetItemAsync(a.Client, sid, id);
        var patched = (await patch).StatusCode == HttpStatusCode.OK;
        if (!patched) Assert.Equal(HttpStatusCode.PreconditionFailed, (await patch).StatusCode);       // sync committed first: the stale tag loses
        Assert.Equal(patched ? "11:00" : "10:00", final.GetProperty("when").GetProperty("time").GetString());
        var res = await (await sync).BodyAsync();
        var outcome = res.GetProperty("results")[0].GetProperty("outcome").GetString();
        if (outcome == "created")
        {
            if (!patched) { Assert.Equal("synced", final.GetProperty("syncState").GetString()); return; }
            // sync won: the event carries the OLD revision and the item says changed_since_sync (approved, calendar untouched by the edit)
            Assert.Equal("changed_since_sync", final.GetProperty("syncState").GetString());
            Assert.Equal("approved", final.GetProperty("decision").GetString());
            Assert.Equal("10:00", await F.TextAsync($"select to_char(start_time, 'HH24:MI') from syllabus.calendar_events where item_id = '{Guid32(id)}'"));
        }
        else
        {
            // the edit won: the revision moved, sync refused with zero writes, and the edit returned the unsynced approval to undecided
            Assert.Contains(outcome, new[] { "failed", "not_eligible" });
            Assert.Equal(0, await F.ScalarAsync($"select count(*) from syllabus.calendar_events where item_id = '{Guid32(id)}'"));
            Assert.Equal("undecided", final.GetProperty("decision").GetString());
        }
    }

    [Fact]
    public async Task Edit_after_sync_marks_changed_since_sync_does_not_touch_the_calendar_and_the_next_sync_updates_it()
    {
        var a = await NewActorAsync(); var course = await CourseAsync(a.Client);
        var sid = (await CreateReadyAsync(a.Client, course, Paste(Text))).GetProperty("id").GetString()!;
        var item = ByTitle(await ItemsAsync(a.Client, sid), "Midterm");
        var ap = await (await Decide(a.Client, sid, item, "approved")).BodyAsync();
        var id = ap.GetProperty("id").GetString()!;
        await Sync(a.Client, sid, NewKey(), (id, ap.GetProperty("revision").GetInt32()));
        var edit = await (await a.Client.SendAsync(Req(HttpMethod.Patch, $"{Api}/syllabi/{sid}/items/{id}", new { time = "13:30" }, ifMatch: (await GetItemAsync(a.Client, sid, id)).GetProperty("etag").GetString()))).BodyAsync();
        Assert.Equal("changed_since_sync", edit.GetProperty("syncState").GetString());
        Assert.Equal("approved", edit.GetProperty("decision").GetString());
        Assert.Equal("10:00", await F.TextAsync($"select to_char(start_time, 'HH24:MI') from syllabus.calendar_events where item_id = '{Guid32(id)}'"));
        var res = await (await Sync(a.Client, sid, NewKey(), (id, edit.GetProperty("revision").GetInt32()))).BodyAsync();
        Assert.Equal("updated", res.GetProperty("results")[0].GetProperty("outcome").GetString());
        Assert.Equal("13:30", await F.TextAsync($"select to_char(start_time, 'HH24:MI') from syllabus.calendar_events where item_id = '{Guid32(id)}'"));
        Assert.Equal(1, await F.ScalarAsync($"select count(*) from syllabus.calendar_events where item_id = '{Guid32(id)}'"));
        // syncing the old revision is refused (the student must review the new one)
        var stale = await (await Sync(a.Client, sid, NewKey(), (id, ap.GetProperty("revision").GetInt32()))).BodyAsync();
        Assert.Equal("failed", stale.GetProperty("results")[0].GetProperty("outcome").GetString());
        Assert.Equal("precondition_failed", stale.GetProperty("results")[0].GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task Sync_replay_returns_the_original_body_after_a_later_skip_and_a_key_on_another_syllabus_is_400()
    {
        var a = await NewActorAsync(); var course = await CourseAsync(a.Client);
        var sid = (await CreateReadyAsync(a.Client, course, Paste(Text))).GetProperty("id").GetString()!;
        var ap = await (await Decide(a.Client, sid, ByTitle(await ItemsAsync(a.Client, sid), "Midterm"), "approved")).BodyAsync();
        var id = ap.GetProperty("id").GetString()!; var rev = ap.GetProperty("revision").GetInt32();
        var key = NewKey();
        var first = await Sync(a.Client, sid, key, (id, rev));
        var original = await first.BodyAsync();
        await Decide(a.Client, sid, await GetItemAsync(a.Client, sid, id), "skipped");
        var replay = await Sync(a.Client, sid, key, (id, rev));
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal("true", replay.Headers.GetValues("Idempotency-Replayed").Single());
        var again = await replay.BodyAsync();
        SyllabusSchemas.AssertValid("syllabus-calendar-sync-result", again);
        Assert.Equal(original.GetProperty("syncId").GetString(), again.GetProperty("syncId").GetString());
        Assert.Equal(original.GetProperty("results")[0].GetProperty("outcome").GetString(), again.GetProperty("results")[0].GetProperty("outcome").GetString());
        Assert.Equal(original.GetProperty("completedAt").GetDateTimeOffset(), again.GetProperty("completedAt").GetDateTimeOffset());
        Assert.Equal(0, await F.ScalarAsync($"select count(*) from syllabus.calendar_events where item_id = '{Guid32(id)}'"));      // the replay did not re-execute anything
        // the same key on another syllabus of the same user is a different fingerprint
        var c2 = await CourseAsync(a.Client, "Other");
        var s2 = (await CreateReadyAsync(a.Client, c2, Paste("Quiz 2 due Sept 15, 2026"))).GetProperty("id").GetString()!;
        await (await Sync(a.Client, s2, key, (id, rev))).AssertProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        // another user's identical key is independent
        var b = await NewActorAsync(); var bc = await CourseAsync(b.Client);
        var bs = (await CreateReadyAsync(b.Client, bc, Paste(Text))).GetProperty("id").GetString()!;
        var bi = await (await Decide(b.Client, bs, ByTitle(await ItemsAsync(b.Client, bs), "Midterm"), "approved")).BodyAsync();
        var br = await Sync(b.Client, bs, key, (bi.GetProperty("id").GetString()!, bi.GetProperty("revision").GetInt32()));
        Assert.Equal(HttpStatusCode.OK, br.StatusCode);
        Assert.False(br.Headers.Contains("Idempotency-Replayed"));
    }

    [Fact]
    public async Task Deleting_while_the_job_is_running_cancels_it_and_a_late_result_recreates_nothing()
    {
        var a = await NewActorAsync(); var course = await CourseAsync(a.Client);
        var created = await (await CreateAsync(a.Client, course, Paste(Text))).BodyAsync();
        var sid = created.GetProperty("id").GetString()!; var g = Guid32(sid);
        await Task.Delay(300);                                                  // the worker has claimed the job and is inside a stage delay
        Assert.Equal(HttpStatusCode.NoContent, (await a.Client.SendAsync(Req(HttpMethod.Delete, $"{Api}/syllabi/{sid}"))).StatusCode);
        await Task.Delay(2500);                                                 // longer than every remaining stage
        foreach (var t in new[] { "syllabi", "canonical_texts", "items", "item_proposals", "unclaimed_dates", "extraction_jobs", "sync_runs", "calendar_events" })
            Assert.Equal(0, await F.ScalarAsync($"select count(*) from syllabus.{t} where user_id = '{a.UserId}'"));
        Assert.Equal(HttpStatusCode.NotFound, (await a.Client.GetAsync($"{Api}/syllabi/{sid}")).StatusCode);
        Assert.Equal(0, await F.ScalarAsync($"select count(*) from syllabus.items where syllabus_id = '{g}'"));
        // the user's single in-flight slot is free again
        Assert.Equal(HttpStatusCode.Accepted, (await CreateAsync(a.Client, course, Paste(Text))).StatusCode);
    }

    [Fact]
    public async Task A_crashed_worker_lease_expires_and_the_job_is_requeued_then_failed_with_timeout_after_the_attempt_cap()
    {
        var a = await NewActorAsync(); var course = await CourseAsync(a.Client);
        var sid = (await (await CreateAsync(a.Client, course, Paste(Text))).BodyAsync()).GetProperty("id").GetString()!; var g = Guid32(sid);
        await WaitDoneAsync(a.Client, sid);
        // simulate a crash: a running job whose lease is long past, attempt already at the cap
        await F.ExecAsync($"update syllabus.syllabi set status = 'extracting' where id = '{g}'");
        await F.ExecAsync($"update syllabus.extraction_jobs set state = 'running', lease_owner = 'dead', lease_expires_at = now() - interval '1 hour', attempt = 3, finished_at = null where syllabus_id = '{g}'");
        await F.Services.GetRequiredService<ExtractionRunner>().ReapAsync(CancellationToken.None);
        var s = await (await a.Client.GetAsync($"{Api}/syllabi/{sid}")).BodyAsync();
        Assert.Equal("failed", s.GetProperty("status").GetString());
        Assert.Equal("timeout", s.GetProperty("statusReason").GetString());
        Assert.Equal(JsonValueKind.Null, s.GetProperty("pollAfterSeconds").ValueKind);
        Assert.Equal(1, await F.ScalarAsync($"select count(*) from syllabus.syllabi where id = '{g}' and purge_after is not null"));       // debris: purged 7 days after the last state change
    }
}

public class SyllabusRateLimitTests(SyllabusRateLimitTests.Factory f) : SyllabusTestBase(f), IClassFixture<SyllabusRateLimitTests.Factory>
{
    public sealed class Factory : ApiFactory
    {
        protected override IEnumerable<KeyValuePair<string, string?>> Settings =>
        [
            new("RateLimiting:SyllabusWritePermitPerMinute", "4"), new("RateLimiting:SyllabusReadPermitPerMinute", "12"),
            new("RateLimiting:SyllabusSyncPerSyllabusPerMinute", "2"), new("RateLimiting:SyllabusSyncPerUserPerMinute", "3"),
            new("RateLimiting:SyllabusBatchPerUserPerMinute", "2"), new("RateLimiting:SyllabusExportPerUserPerMinute", "2"),
            new("RateLimiting:WritePermitPerMinute", "100000"),
        ];
    }

    [Fact]
    public async Task Syllabus_writes_have_their_own_bucket_and_never_spend_or_exhaust_the_general_write_budget()
    {
        var a = await NewActorAsync(); var course = await CourseAsync(a.Client);
        var sid = (await CreateReadyAsync(a.Client, course, Paste("Quiz due Oct 20, 2026"))).GetProperty("id").GetString()!;     // 1 write
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 6; i++) statuses.Add((await a.Client.SendAsync(Req(HttpMethod.Post, $"{Api}/syllabi/{sid}/items", new { kind = "other", title = "T" + i, date = "2026-09-20" }))).StatusCode);
        Assert.Contains(HttpStatusCode.TooManyRequests, statuses);
        var limited = await a.Client.SendAsync(Req(HttpMethod.Post, $"{Api}/syllabi/{sid}/items", new { kind = "other", title = "Tx", date = "2026-09-20" }));
        await limited.AssertProblemAsync((HttpStatusCode)429, "rate_limited");
        Assert.True(limited.Headers.Contains("Retry-After"));
        // the syllabus bucket is spent, but a course write (the general write bucket, shared with live-lecture writes) is untouched
        Assert.Equal(HttpStatusCode.Created, (await a.Client.PostAsync(Api + "/courses", System.Net.Http.Json.JsonContent.Create(new { name = "Still works" }))).StatusCode);
    }

    [Fact]
    public async Task Sync_batch_and_export_have_their_own_limits()
    {
        var a = await NewActorAsync(); var course = await CourseAsync(a.Client);
        var sid = (await CreateReadyAsync(a.Client, course, Paste("Midterm exam: Oct 19, 2026 at 10:00 AM"))).GetProperty("id").GetString()!;
        var item = (await ItemsAsync(a.Client, sid)).First();
        var ap = await (await Decide(a.Client, sid, item, "approved")).BodyAsync();
        var pair = (ap.GetProperty("id").GetString()!, ap.GetProperty("revision").GetInt32());
        var codes = new List<HttpStatusCode>();
        for (var i = 0; i < 3; i++) codes.Add((await Sync(a.Client, sid, NewKey(), pair)).StatusCode);
        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests], codes);        // 2 per syllabus per minute
        var exports = new List<HttpStatusCode>();
        for (var i = 0; i < 3; i++) exports.Add((await a.Client.GetAsync($"{Api}/syllabi/{sid}/export")).StatusCode);
        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests], exports);
    }
}

public class SyllabusStartupGuardTests
{
    private sealed class Guarded(params (string K, string V)[] settings) : ApiFactory
    {
        protected override IEnumerable<KeyValuePair<string, string?>> Settings => settings.Select(s => new KeyValuePair<string, string?>(s.K, s.V));
    }

    private sealed class ProdWithFault : ApiFactory
    {
        protected override string EnvironmentName => "Production";
        protected override IEnumerable<KeyValuePair<string, string?>> Settings => [new("Syllabus:Mock:Faults:Calendar", "unavailable"), new("Security:ETagKey", Convert.ToBase64String(new byte[32]))];
    }

    private static async Task AssertRefusesToStart(ApiFactory f, string mention)
    {
        await ((IAsyncLifetime)f).InitializeAsync();
        try
        {
            var ex = Assert.ThrowsAny<Exception>(() => f.CreateClient());
            var all = new List<string>();
            for (var e = ex; e is not null; e = e.InnerException!) { all.Add(e.Message); if (e.InnerException is null) break; }
            Assert.Contains(all, m => m.Contains(mention, StringComparison.Ordinal));
        }
        finally { await ((IAsyncLifetime)f).DisposeAsync(); }
    }

    [Fact] public Task Startup_refuses_any_calendar_provider_other_than_mock() => AssertRefusesToStart(new Guarded(("Syllabus:CalendarProvider", "google")), "Syllabus:CalendarProvider");
    [Fact] public Task Startup_refuses_a_model_extractor_whatever_the_configuration() => AssertRefusesToStart(new Guarded(("Syllabus:Extractor", "model")), "Syllabus:Extractor");
    [Fact] public Task Startup_refuses_fault_injection_outside_development_and_test() => AssertRefusesToStart(new ProdWithFault(), "fault injection");

    [Fact]
    public async Task The_only_calendar_provider_registered_is_the_mock()
    {
        var f = new ApiFactory();
        await ((IAsyncLifetime)f).InitializeAsync();
        try
        {
            _ = f.CreateClient();
            using var scope = f.Services.CreateScope();
            var providers = scope.ServiceProvider.GetServices<ICalendarProvider>().ToList();
            Assert.Single(providers);
            Assert.Equal("mock", providers[0].Name);
        }
        finally { await ((IAsyncLifetime)f).DisposeAsync(); }
    }
}
