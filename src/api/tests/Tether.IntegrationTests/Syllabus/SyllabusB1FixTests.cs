using System.Net;
using System.Text.Json;
using Npgsql;
using Tether.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Tether.Syllabus.Application;

namespace Tether.IntegrationTests.Syllabus;

// Regression tests for docs/security/p2-b1-review.md findings B1-01, B1-03, B1-04, B1-05, B1-06, B1-07 and B1-13.
// Each test was run against the unfixed code first and failed (see the "B1 fixes" section of that document).

/// <summary>Default host (worker on): deadlock retry, per-item batch audit, idempotency races and fingerprints, audit retention default.</summary>
public class SyllabusB1FixTests(ApiFactory factory) : SyllabusTestBase(factory), IClassFixture<ApiFactory>
{
    private const string Clean = "Midterm exam: Oct 19, 2026 at 10:00 AM\nFinal exam: Dec 9, 2026 at 8:00 AM\nQuiz 1 due Sept 14, 2026\nPS1 due Sept 21";

    private static Task<HttpResponseMessage> Batch(HttpClient c, string syl, string action, params JsonElement[] items) =>
        c.SendAsync(Req(HttpMethod.Post, $"{Api}/syllabi/{syl}/decisions",
            new { action, itemEtags = items.ToDictionary(i => i.GetProperty("id").GetString()!, i => i.GetProperty("etag").GetString()!) }));

    // ---------------------------------------------------------------- B1-01

    [Fact]
    public async Task B1_01_Deleting_a_syllabus_that_deadlocks_with_a_job_row_writer_is_retried_and_returns_204()
    {
        var a = await NewActorAsync(); var course = await CourseAsync(a.Client);
        var sid = (await CreateReadyAsync(a.Client, course, Paste(Clean))).GetProperty("id").GetString()!; var g = Guid32(sid);
        await using var conn = new NpgsqlConnection(F.ConnectionString);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await using (var hold = new NpgsqlCommand($"update syllabus.extraction_jobs set stage = stage where syllabus_id = '{g}'", conn, tx)) await hold.ExecuteNonQueryAsync();

        var delete = a.Client.SendAsync(Req(HttpMethod.Delete, $"{Api}/syllabi/{sid}"));
        await Task.Delay(500);                      // the delete now owns the syllabus row lock and its cascade waits on the job row we hold
        var touch = Task.Run(async () =>
        {
            await using var cmd = new NpgsqlCommand($"update syllabus.syllabi set updated_at = updated_at where id = '{g}'", conn, tx);
            await cmd.ExecuteNonQueryAsync();       // job row then syllabus row: the opposite order, a deadlock cycle
        });
        await Task.WhenAny(touch, Task.Delay(8000));
        try { await touch; } catch (PostgresException) { /* the writer was chosen as the victim: the delete is unaffected */ }
        try { await tx.CommitAsync(); } catch (Exception) { }

        Assert.Equal(HttpStatusCode.NoContent, (await delete).StatusCode);
        Assert.Equal(0, await F.ScalarAsync($"select count(*) from syllabus.syllabi where id = '{g}'"));
        Assert.Equal(0, await F.ScalarAsync($"select count(*) from syllabus.extraction_jobs where syllabus_id = '{g}'"));
    }

    // ---------------------------------------------------------------- B1-03

    [Fact]
    public async Task B1_03_Batch_approve_writes_one_audit_row_per_approved_item_with_its_revision_and_keeps_the_summary_row()
    {
        var a = await NewActorAsync(); var course = await CourseAsync(a.Client);
        var sid = (await CreateReadyAsync(a.Client, course, Paste(Clean))).GetProperty("id").GetString()!; var g = Guid32(sid);
        var looksRight = (await ItemsAsync(a.Client, sid)).Where(i => i.GetProperty("confidence").GetString() == "looks_right").ToList();
        Assert.Equal(3, looksRight.Count);
        var b = await (await Batch(a.Client, sid, "approve", [.. looksRight])).BodyAsync();
        Assert.Equal(3, b.GetProperty("counts").GetProperty("applied").GetInt32());

        Assert.Equal(3, await F.ScalarAsync($"select count(*) from syllabus.decision_audit where syllabus_id = '{g}' and action = 'approved' and user_id = '{a.UserId}' and item_id is not null and item_revision is not null"));
        Assert.Equal(3, await F.ScalarAsync($"select count(distinct item_id) from syllabus.decision_audit where syllabus_id = '{g}' and action = 'approved'"));
        foreach (var i in looksRight)
        {
            var now = await GetItemAsync(a.Client, sid, i.GetProperty("id").GetString()!);
            Assert.Equal(1, await F.ScalarAsync($"select count(*) from syllabus.decision_audit where syllabus_id = '{g}' and action = 'approved' and item_id = '{Guid32(i.GetProperty("id").GetString()!)}' and item_revision = {now.GetProperty("revision").GetInt32()}"));
        }
        Assert.Equal(1, await F.ScalarAsync($"select count(*) from syllabus.decision_audit where syllabus_id = '{g}' and action = 'batch_approved' and item_count = 3"));
    }

    // ---------------------------------------------------------------- B1-06

    private static object NewItem(string title = "Lab report", string? notes = null) => new { kind = "other", title, notes, date = "2026-09-20" };

    [Fact]
    public async Task B1_06_Simultaneous_item_creates_with_one_key_replay_instead_of_failing_with_500()
    {
        var a = await NewActorAsync(); var course = await CourseAsync(a.Client);
        var sid = (await CreateReadyAsync(a.Client, course, Paste(Clean))).GetProperty("id").GetString()!; var g = Guid32(sid);
        var key = NewKey();
        var res = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => a.Client.SendAsync(Req(HttpMethod.Post, $"{Api}/syllabi/{sid}/items", NewItem("Race item"), key))));
        Assert.All(res, r => Assert.True((int)r.StatusCode < 500, "status " + (int)r.StatusCode));
        Assert.All(res, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));
        Assert.Equal(1, await F.ScalarAsync($"select count(*) from syllabus.items where syllabus_id = '{g}' and title = 'Race item'"));
    }

    [Fact]
    public async Task B1_06_Reusing_an_expired_item_create_key_creates_a_new_item_instead_of_500()
    {
        var a = await NewActorAsync(); var course = await CourseAsync(a.Client);
        var sid = (await CreateReadyAsync(a.Client, course, Paste(Clean))).GetProperty("id").GetString()!; var g = Guid32(sid);
        var key = NewKey();
        Assert.Equal(HttpStatusCode.Created, (await a.Client.SendAsync(Req(HttpMethod.Post, $"{Api}/syllabi/{sid}/items", NewItem("First"), key))).StatusCode);
        await F.ExecAsync($"update syllabus.idempotency_keys set expires_at = now() - interval '1 hour' where syllabus_id = '{g}' and result_kind = 'item'");
        var again = await a.Client.SendAsync(Req(HttpMethod.Post, $"{Api}/syllabi/{sid}/items", NewItem("Second"), key));
        Assert.Equal(HttpStatusCode.Created, again.StatusCode);
        Assert.False(again.Headers.Contains("Idempotency-Replayed"));
        Assert.Equal(2, await F.ScalarAsync($"select count(*) from syllabus.items where syllabus_id = '{g}' and origin = 'student'"));
    }

    [Fact]
    public async Task B1_06_Reusing_an_expired_sync_key_runs_the_sync_instead_of_400()
    {
        var a = await NewActorAsync(); var course = await CourseAsync(a.Client);
        var sid = (await CreateReadyAsync(a.Client, course, Paste(Clean))).GetProperty("id").GetString()!; var g = Guid32(sid);
        var item = ByTitle(await ItemsAsync(a.Client, sid), "Midterm");
        var ap = await (await Decide(a.Client, sid, item, "approved")).BodyAsync();
        var pair = (ap.GetProperty("id").GetString()!, ap.GetProperty("revision").GetInt32());
        var key = NewKey();
        Assert.Equal(HttpStatusCode.OK, (await Sync(a.Client, sid, key, pair)).StatusCode);
        await F.ExecAsync($"update syllabus.idempotency_keys set expires_at = now() - interval '1 hour' where syllabus_id = '{g}' and result_kind = 'sync_run'");
        var again = await Sync(a.Client, sid, key, pair);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.False(again.Headers.Contains("Idempotency-Replayed"));
    }

    // ---------------------------------------------------------------- B1-07

    [Fact]
    public async Task B1_07_A_key_reused_with_a_body_whose_fields_shift_across_the_separator_is_a_mismatch_not_a_replay()
    {
        var a = await NewActorAsync(); var course = await CourseAsync(a.Client);
        var sid = (await CreateReadyAsync(a.Client, course, Paste(Clean))).GetProperty("id").GetString()!;
        var key = NewKey();
        var first = await a.Client.SendAsync(Req(HttpMethod.Post, $"{Api}/syllabi/{sid}/items", NewItem("X|Y", "Z"), key));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var second = await a.Client.SendAsync(Req(HttpMethod.Post, $"{Api}/syllabi/{sid}/items", NewItem("X", "Y|Z"), key));
        await second.AssertProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        Assert.False(second.Headers.Contains("Idempotency-Replayed"));
    }

    // ---------------------------------------------------------------- B1-04 (defaults and the retention purge)

    [Fact]
    public async Task B1_04_Audit_retention_defaults_to_400_days_and_the_maintenance_purge_removes_only_older_rows()
    {
        Assert.Equal(400, F.Services.GetRequiredService<SyllabusOptions>().AuditRetentionDays);
        var a = await NewActorAsync();
        var old = Guid.NewGuid(); var keep = Guid.NewGuid();
        await F.ExecAsync($"insert into syllabus.decision_audit (id, user_id, syllabus_id, action, occurred_at) values ('{old}', '{a.UserId}', '{Guid.NewGuid()}', 'synced', now() - interval '401 days')");
        await F.ExecAsync($"insert into syllabus.decision_audit (id, user_id, syllabus_id, action, occurred_at) values ('{keep}', '{a.UserId}', '{Guid.NewGuid()}', 'synced', now() - interval '399 days')");
        var run = await F.Services.GetRequiredService<SyllabusMaintenance>().RunOnceAsync(CancellationToken.None);
        Assert.True(run.AuditPurged >= 1);
        Assert.Equal(0, await F.ScalarAsync($"select count(*) from syllabus.decision_audit where id = '{old}'"));
        Assert.Equal(1, await F.ScalarAsync($"select count(*) from syllabus.decision_audit where id = '{keep}'"));
    }
}

/// <summary>Stage delay on: a job is observably "running", so DELETE and lease takeover can be interleaved.</summary>
public class SyllabusB1CancelTests(SyllabusB1CancelTests.Factory f) : SyllabusTestBase(f), IClassFixture<SyllabusB1CancelTests.Factory>
{
    public sealed class Factory : ApiFactory
    {
        protected override IEnumerable<KeyValuePair<string, string?>> Settings => [new("Syllabus:Mock:StageDelayMilliseconds", "700")];
    }

    [Fact]
    public async Task B1_05_Another_users_delete_of_a_syllabus_id_does_not_cancel_the_owners_running_extraction()
    {
        var a = await NewActorAsync(); var b = await NewActorAsync(); var course = await CourseAsync(a.Client);
        var created = await (await CreateAsync(a.Client, course, Paste("Midterm exam: Oct 19, 2026 at 10:00 AM"))).BodyAsync();
        var sid = created.GetProperty("id").GetString()!;
        await Task.Delay(300);                                                  // the worker is inside its first stage delay
        Assert.Equal(HttpStatusCode.NotFound, (await b.Client.SendAsync(Req(HttpMethod.Delete, $"{Api}/syllabi/{sid}"))).StatusCode);
        var done = await WaitDoneAsync(a.Client, sid, 10);                       // the lease is 60 s: a cancelled job would not finish in time
        Assert.Equal("ready", done.GetProperty("status").GetString());
    }
}

/// <summary>Worker off, so tests drive the runner and reaper by hand.</summary>
public class SyllabusB1ManualWorkerTests(SyllabusB1ManualWorkerTests.Factory f) : SyllabusTestBase(f), IClassFixture<SyllabusB1ManualWorkerTests.Factory>
{
    public sealed class Factory : ApiFactory
    {
        protected override IEnumerable<KeyValuePair<string, string?>> Settings =>
            [new("Syllabus:Worker:Enabled", "false"), new("Syllabus:Mock:StageDelayMilliseconds", "700")];
    }

    [Fact]
    public async Task B1_01_The_reaper_takes_the_syllabus_lock_before_the_job_row_so_it_cannot_deadlock_with_a_delete()
    {
        var a = await NewActorAsync(); var course = await CourseAsync(a.Client);
        var sid = (await (await CreateAsync(a.Client, course, Paste("Quiz 1 due Sept 14, 2026"))).BodyAsync()).GetProperty("id").GetString()!;
        var g = Guid32(sid);
        await F.ExecAsync($"update syllabus.syllabi set status = 'extracting' where id = '{g}'");
        await F.ExecAsync($"update syllabus.extraction_jobs set state = 'running', lease_owner = 'dead', lease_expires_at = now() - interval '1 hour', attempt = 3 where syllabus_id = '{g}'");

        await using var conn = new NpgsqlConnection(F.ConnectionString);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await using (var lockSyl = new NpgsqlCommand($"select 1 from syllabus.syllabi where id = '{g}' for update", conn, tx)) await lockSyl.ExecuteScalarAsync();     // what a DELETE holds
        var reap = Task.Run(() => F.Services.GetRequiredService<ExtractionRunner>().ReapAsync(CancellationToken.None));
        await Task.Delay(500);
        var cascade = Task.Run(async () =>
        {
            await using var cmd = new NpgsqlCommand($"update syllabus.extraction_jobs set stage = stage where syllabus_id = '{g}'", conn, tx);   // the delete cascade reaching the job row
            await cmd.ExecuteNonQueryAsync();
        });
        await Task.WhenAny(cascade, Task.Delay(8000));
        try { await cascade; } catch (PostgresException) { }
        try { await tx.CommitAsync(); } catch (Exception) { }
        await reap;                                                              // throws on the old lock order (deadlock victim)
        Assert.Equal("failed", await F.TextAsync($"select status from syllabus.syllabi where id = '{g}'"));
    }

    [Theory]
    [InlineData(350)]     // takeover while the first stage runs
    [InlineData(1050)]    // takeover after the second SetStage, immediately before the persistence step
    public async Task B1_13_A_result_from_a_worker_that_lost_its_lease_is_not_persisted(int takeoverAfterMs)
    {
        var a = await NewActorAsync(); var course = await CourseAsync(a.Client);
        var created = await (await CreateAsync(a.Client, course, Paste("Midterm exam: Oct 19, 2026 at 10:00 AM\nQuiz 1 due Sept 14, 2026"))).BodyAsync();
        var g = Guid32(created.GetProperty("id").GetString()!);
        var run = Task.Run(() => F.Services.GetRequiredService<ExtractionRunner>().RunOnceAsync(CancellationToken.None));
        await Task.Delay(takeoverAfterMs);
        // another worker has taken the job over (as after a reaper re-queue): a new owner and a new attempt
        await F.ExecAsync($"update syllabus.extraction_jobs set lease_owner = 'other-worker', attempt = attempt + 1 where syllabus_id = '{g}' and state = 'running'");
        await run;
        Assert.Equal(0, await F.ScalarAsync($"select count(*) from syllabus.items where syllabus_id = '{g}'"));
        Assert.Equal("running", await F.TextAsync($"select state from syllabus.extraction_jobs where syllabus_id = '{g}'"));
        Assert.Equal("other-worker", await F.TextAsync($"select lease_owner from syllabus.extraction_jobs where syllabus_id = '{g}'"));
        Assert.NotEqual("ready", await F.TextAsync($"select status from syllabus.syllabi where id = '{g}'"));
    }
}

/// <summary>Small caps so growth bounds are observable.</summary>
public class SyllabusB1GrowthTests(SyllabusB1GrowthTests.Factory f) : SyllabusTestBase(f), IClassFixture<SyllabusB1GrowthTests.Factory>
{
    public sealed class Factory : ApiFactory
    {
        protected override IEnumerable<KeyValuePair<string, string?>> Settings =>
        [
            new("Syllabus:SyncRunsPerSyllabus", "3"), new("Syllabus:AuditNoisyCapPerUser", "3"), new("Syllabus:AuditRetentionDays", "30"),
            new("RateLimiting:SyllabusSyncPerSyllabusPerMinute", "1000"), new("RateLimiting:SyllabusSyncPerUserPerMinute", "1000"),
            new("RateLimiting:SyllabusExportPerUserPerMinute", "1000"), new("RateLimiting:SyllabusWritePermitPerMinute", "1000"),
            new("RateLimiting:WritePermitPerMinute", "100000"),
        ];
    }

    [Fact]
    public async Task B1_04_Only_the_newest_sync_runs_per_syllabus_are_kept_and_their_items_go_with_the_pruned_runs()
    {
        var a = await NewActorAsync(); var course = await CourseAsync(a.Client);
        var sid = (await CreateReadyAsync(a.Client, course, Paste("Midterm exam: Oct 19, 2026 at 10:00 AM"))).GetProperty("id").GetString()!; var g = Guid32(sid);
        var ap = await (await Decide(a.Client, sid, (await ItemsAsync(a.Client, sid)).First(), "approved")).BodyAsync();
        var pair = (ap.GetProperty("id").GetString()!, ap.GetProperty("revision").GetInt32());
        var firstKey = NewKey();
        for (var i = 0; i < 7; i++) Assert.Equal(HttpStatusCode.OK, (await Sync(a.Client, sid, i == 0 ? firstKey : NewKey(), pair)).StatusCode);
        Assert.Equal(3, await F.ScalarAsync($"select count(*) from syllabus.sync_runs where syllabus_id = '{g}'"));
        Assert.Equal(0, await F.ScalarAsync($"select count(*) from syllabus.sync_run_items i where i.user_id = '{a.UserId}' and not exists (select 1 from syllabus.sync_runs r where r.id = i.run_id)"));
        Assert.Equal(3, await F.ScalarAsync($"select count(*) from syllabus.sync_run_items where user_id = '{a.UserId}'"));
        // the key of a pruned run is gone with it: the retry re-runs the (idempotent) sync, it does not fail
        var retry = await Sync(a.Client, sid, firstKey, pair);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal(3, await F.ScalarAsync($"select count(*) from syllabus.sync_runs where syllabus_id = '{g}'"));
    }

    [Fact]
    public async Task B1_04_High_volume_audit_rows_stop_at_the_per_user_cap_while_decision_rows_are_always_written()
    {
        var a = await NewActorAsync(); var course = await CourseAsync(a.Client);
        var sid = (await CreateReadyAsync(a.Client, course, Paste("Midterm exam: Oct 19, 2026 at 10:00 AM\nQuiz 1 due Sept 14, 2026"))).GetProperty("id").GetString()!;
        for (var i = 0; i < 6; i++) Assert.Equal(HttpStatusCode.OK, (await a.Client.GetAsync($"{Api}/syllabi/{sid}/export")).StatusCode);
        Assert.Equal(3, await F.ScalarAsync($"select count(*) from syllabus.decision_audit where user_id = '{a.UserId}' and action = 'exported'"));
        foreach (var it in await ItemsAsync(a.Client, sid)) Assert.Equal(HttpStatusCode.OK, (await Decide(a.Client, sid, it, "skipped")).StatusCode);
        Assert.Equal(2, await F.ScalarAsync($"select count(*) from syllabus.decision_audit where user_id = '{a.UserId}' and action = 'skipped'"));
    }

    [Fact]
    public async Task B1_04_The_configured_audit_retention_period_is_honoured()
    {
        Assert.Equal(30, F.Services.GetRequiredService<SyllabusOptions>().AuditRetentionDays);
        var a = await NewActorAsync();
        var old = Guid.NewGuid(); var keep = Guid.NewGuid();
        await F.ExecAsync($"insert into syllabus.decision_audit (id, user_id, syllabus_id, action, occurred_at) values ('{old}', '{a.UserId}', '{Guid.NewGuid()}', 'approved', now() - interval '31 days')");
        await F.ExecAsync($"insert into syllabus.decision_audit (id, user_id, syllabus_id, action, occurred_at) values ('{keep}', '{a.UserId}', '{Guid.NewGuid()}', 'approved', now() - interval '29 days')");
        await F.Services.GetRequiredService<SyllabusMaintenance>().RunOnceAsync(CancellationToken.None);
        Assert.Equal(0, await F.ScalarAsync($"select count(*) from syllabus.decision_audit where id = '{old}'"));
        Assert.Equal(1, await F.ScalarAsync($"select count(*) from syllabus.decision_audit where id = '{keep}'"));
    }
}
