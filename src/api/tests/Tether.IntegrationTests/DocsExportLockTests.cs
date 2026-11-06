using System.Data.Common;
using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Tether.Docs.Application;
using Tether.Docs.Contracts;
using Tether.Docs.Persistence;
using Tether.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Tether.IntegrationTests;

/// <summary>A provider that really writes the Demo Doc and then reports a failure, so the savepoint rollback of an INSERTED document can be tested (B24).</summary>
internal sealed class FailAfterInsertProvider(IDocsProvider inner, Func<bool> armed) : IDocsProvider
{
    public string ProviderId => inner.ProviderId;

    public async Task<ProviderResult<DocRef>> CreateAsync(DocsWriteRequest request, CancellationToken ct)
    {
        var r = await inner.CreateAsync(request, ct);
        return armed() && r.Succeeded ? ProviderResult<DocRef>.Retryable("provider_unavailable", 5) : r;
    }

    public async Task<ProviderResult<DocRef>> UpdateAsync(DocRef existing, int expectedVersion, DocsWriteRequest request, CancellationToken ct)
    {
        var r = await inner.UpdateAsync(existing, expectedVersion, request, ct);
        return armed() && r.Succeeded ? ProviderResult<DocRef>.Retryable("provider_unavailable", 5) : r;
    }

    public Task<ProviderResult> DeleteAsync(DocRef existing, CancellationToken ct) => inner.DeleteAsync(existing, ct);
}

/// <summary>The Docs DbContext with a <see cref="CommandTrap"/> and a provider that can fail after its insert. A low per-user cap makes the cap race cheap to build.</summary>
public sealed class DocsLockFactory : ApiFactory
{
    internal CommandTrap Trap { get; } = new();
    public volatile bool FailAfterInsert;

    protected override int MaxPoolSize => RacePool.Size;
    protected override IEnumerable<KeyValuePair<string, string?>> Settings =>
        [new("Docs:Demo:Faults:Enabled", "true"), new("Docs:MaxDocsPerUser", "3"), new("Lectures:LifecycleJobSeconds", "3600")];

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(s =>
        {
            s.ConfigureDbContext<DocsDbContext>(o => o.AddInterceptors(Trap));
            var real = s.Single(d => d.ServiceType == typeof(IDocsProvider));
            s.Remove(real);
            s.AddScoped<IDocsProvider>(sp => new FailAfterInsertProvider(ActivatorUtilities.CreateInstance<DemoDocsProvider>(sp), () => FailAfterInsert));
        });
    }
}

/// <summary>
/// DX-B05 (B23 to B25g): races, lock order and error mapping of the confirm and delete transactions. Method as in LockOrderFixTests and
/// SyllabusLockOrderTests: a <see cref="CommandTrap"/> stops the request after it holds its locks, the other side is a raw connection, and
/// pg_blocking_pids decides when the next step runs. No sleeps. L-VICTIM: a test that builds a deadlock cannot choose the victim, so it tolerates
/// losing the race (records it, rolls back) and runs again on fresh data.
/// </summary>
[Collection(RacePool.Name)]
public class DocsExportLockTests(DocsLockFactory factory) : DocsExportTestBase(factory), IClassFixture<DocsLockFactory>
{
    private DocsLockFactory Fx => (DocsLockFactory)F;
    private CommandTrap Trap => Fx.Trap;

    /// <summary>The first EF read of the confirm after it took ALL its locks (the idempotency key lookup).</summary>
    private static bool IsKeyLookup(string sql) => sql.Contains("FROM docs.idempotency_keys", StringComparison.Ordinal) && sql.TrimStart().StartsWith("SELECT", StringComparison.Ordinal);

    private static bool IsInsert(string sql, string table) => sql.Contains("INSERT INTO docs." + table, StringComparison.Ordinal);

    private async Task<bool> WaitBlockedByAsync(int holderPid) =>
        await Rt.WaitAsync(async () => await F.ScalarAsync($"select count(*) from pg_stat_activity where {holderPid} = any(pg_blocking_pids(pid))") > 0, 10000);

    private static async Task<T> TimedAsync<T>(Task<T> t) => await t.WaitAsync(TimeSpan.FromSeconds(60));

    private static HttpRequestMessage ConfirmReq(string sid, object body, string key, string? fault = null) => Req(HttpMethod.Post, $"/sessions/{sid}/docs-export", body, key, fault);

    // ---------------- B23: the same key at the same time ----------------

    [Fact]
    public async Task B23_Two_requests_with_the_same_key_at_the_same_time_make_one_document_and_the_second_replays_the_first()
    {
        var l = await ReadyAsync();
        var body = ConfirmBody(await PreviewOkAsync(l.A.Client, l.SessionId, l.NotesId), "create");
        var key = NewKey();
        Task<HttpResponseMessage>? second = null;
        int firstPid = 0;
        Trap.Arm(IsKeyLookup, async cmd =>
        {
            firstPid = LockRace.Pid(cmd);
            second = Task.Run(() => l.A.Client.SendAsync(ConfirmReq(l.SessionId, body, key)));
            Assert.True(await WaitBlockedByAsync(firstPid), "the second request never waited on the first one's advisory lock");
        });

        var first = await TimedAsync(l.A.Client.SendAsync(ConfirmReq(l.SessionId, body, key)));
        var other = await TimedAsync(second!);
        Assert.True(Trap.Fired);
        var a = await OkRecordAsync(first);
        var b = await OkRecordAsync(other);
        Assert.False(Replayed(first));
        Assert.True(Replayed(other));
        Assert.Equal(a.GetRawText(), b.GetRawText());                   // one named outcome: identical bodies
        Assert.Equal(1, await DocRowsAsync("demo_docs", l.A.UserId));
        Assert.Equal(1, await DocRowsAsync("doc_exports", l.A.UserId));
        Assert.Equal(1, await DocRowsAsync("audit_events", l.A.UserId));
    }

    [Fact]
    public async Task B23_Six_identical_requests_at_once_make_exactly_one_document_with_one_body_for_all()
    {
        var l = await ReadyAsync();
        var body = ConfirmBody(await PreviewOkAsync(l.A.Client, l.SessionId, l.NotesId), "create");
        var key = NewKey();
        var all = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Task.Run(() => l.A.Client.SendAsync(ConfirmReq(l.SessionId, body, key)))));
        var bodies = new List<string>();
        foreach (var r in all) bodies.Add((await OkRecordAsync(r)).GetRawText());
        Assert.Single(bodies.Distinct());
        Assert.Equal(5, all.Count(Replayed));
        Assert.Equal(1, await DocRowsAsync("demo_docs", l.A.UserId));
        Assert.Equal(1, await DocRowsAsync("doc_exports", l.A.UserId));
    }

    [Fact]
    public async Task B23a_Two_creates_with_different_keys_for_one_lecture_make_one_document_and_the_loser_is_412()
    {
        var l = await ReadyAsync();
        var body = ConfirmBody(await PreviewOkAsync(l.A.Client, l.SessionId, l.NotesId), "create");
        var all = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => Task.Run(() => l.A.Client.SendAsync(ConfirmReq(l.SessionId, body, NewKey())))));
        Assert.Equal(1, all.Count(r => r.StatusCode == HttpStatusCode.OK));
        foreach (var r in all.Where(r => r.StatusCode != HttpStatusCode.OK))
            Assert.True(r.StatusCode is HttpStatusCode.PreconditionFailed or HttpStatusCode.Conflict, "loser got " + (int)r.StatusCode);
        Assert.Equal(1, await DocRowsAsync("demo_docs", l.A.UserId));
        Assert.Equal(1, await DocRowsAsync("doc_exports", l.A.UserId));       // the losers stored nothing
        Assert.Equal(1, await DocRowsAsync("idempotency_keys", l.A.UserId));
    }

    // ---------------- B24: one transaction, a savepoint around the provider ----------------

    [Fact]
    public async Task B24_A_provider_failure_after_the_document_insert_rolls_the_document_back_and_stores_the_failed_row_with_the_key_and_audit()
    {
        var l = await ReadyAsync();
        var body = ConfirmBody(await PreviewOkAsync(l.A.Client, l.SessionId, l.NotesId), "create");
        var key = NewKey();
        Fx.FailAfterInsert = true;
        JsonElement failed;
        try { failed = await OkRecordAsync(await ConfirmAsync(l.A.Client, l.SessionId, body, key)); }
        finally { Fx.FailAfterInsert = false; }

        Assert.Equal("failed", failed.GetProperty("status").GetString());
        Assert.Equal(0, await DocRowsAsync("demo_docs", l.A.UserId));              // the INSERT happened, then the savepoint rolled it back
        Assert.Equal(0, await DocRowsAsync("doc_targets", l.A.UserId));
        Assert.Equal(1, await DocRowsAsync("doc_exports", l.A.UserId, "status = 'failed'"));
        Assert.Equal(1, await DocRowsAsync("idempotency_keys", l.A.UserId));
        Assert.Equal(1, await DocRowsAsync("audit_events", l.A.UserId, "action = 'export_failed'"));
        // the retry (same key and body) creates the document once; the aborted insert left no orphan behind a unique key
        var ok = await OkRecordAsync(await ConfirmAsync(l.A.Client, l.SessionId, body, key));
        Assert.Equal("created", ok.GetProperty("outcome").GetString());
        Assert.Equal(1, await DocRowsAsync("demo_docs", l.A.UserId));
    }

    // ---------------- B25d: lock order ----------------

    [Fact]
    public async Task B25d_A_lecture_delete_waits_for_a_running_confirm_which_holds_the_session_first_and_the_document_is_then_cascaded()
    {
        var l = await ReadyAsync();
        var body = ConfirmBody(await PreviewOkAsync(l.A.Client, l.SessionId, l.NotesId), "create");
        var before = await LockRace.DeadlocksAsync(F);
        Task? delete = null;
        Trap.Arm(IsKeyLookup, async cmd =>
        {
            // the confirm holds the session row FOR KEY SHARE now; a delete needs that row exclusively, so it must queue BEHIND the confirm
            delete = F.ExecAsync($"delete from lectures.lecture_sessions where id = '{l.SessionId}'");
            Assert.True(await WaitBlockedByAsync(LockRace.Pid(cmd)), "the delete did not wait for the confirm's session lock");
        });

        var res = await TimedAsync(ConfirmAsync(l.A.Client, l.SessionId, body));
        var rec = await OkRecordAsync(res);                               // finished first, so the delete cascades it away
        Assert.Equal("created", rec.GetProperty("outcome").GetString());
        await TimedAsync(Task.Run(async () => { await delete!; return 0; }));
        foreach (var t in new[] { "demo_docs", "doc_exports", "doc_targets", "idempotency_keys" })
            Assert.Equal(0, await DocRowsAsync(t, l.A.UserId));
        Assert.Equal(1, await DocRowsAsync("audit_events", l.A.UserId));  // content free, outlives the lecture
        Assert.Equal(before, await LockRace.DeadlocksAsync(F));            // no lock cycle: the order is session, advisory, rows
    }

    [Fact]
    public async Task B25d_A_confirm_that_arrives_after_a_lecture_delete_began_waits_at_the_session_lock_and_answers_404_with_nothing_left()
    {
        var l = await ReadyAsync();
        var body = ConfirmBody(await PreviewOkAsync(l.A.Client, l.SessionId, l.NotesId), "create");
        await using var holder = new NpgsqlConnection(F.ConnectionString);
        await holder.OpenAsync();
        await using var tx = await holder.BeginTransactionAsync();
        await using (var cmd = new NpgsqlCommand($"delete from lectures.lecture_sessions where id = '{l.SessionId}'", holder, tx)) await cmd.ExecuteNonQueryAsync();   // delete in flight, uncommitted

        var pending = Task.Run(() => ConfirmAsync(l.A.Client, l.SessionId, body));
        Assert.True(await WaitBlockedByAsync(holder.ProcessID), "the confirm did not wait at the session lock");
        await tx.CommitAsync();

        await (await TimedAsync(pending)).AssertProblemAsync(HttpStatusCode.NotFound, "not_found");
        foreach (var t in new[] { "demo_docs", "doc_exports", "doc_targets", "idempotency_keys", "audit_events" })
            Assert.Equal(0, await DocRowsAsync(t, l.A.UserId));
    }

    [Fact]
    public async Task B25d_A_course_purge_that_waits_for_a_confirm_cascades_everything_and_never_deadlocks()
    {
        var l = await ReadyAsync();
        var course = await F.TextAsync($"select course_id from lectures.lecture_sessions where id = '{l.SessionId}'");
        var body = ConfirmBody(await PreviewOkAsync(l.A.Client, l.SessionId, l.NotesId), "create");
        var before = await LockRace.DeadlocksAsync(F);
        Task? purge = null;
        Trap.Arm(IsKeyLookup, async cmd =>
        {
            purge = F.ExecAsync($"delete from courses.courses where id = '{course}'");
            Assert.True(await WaitBlockedByAsync(LockRace.Pid(cmd)), "the purge did not wait for the confirm");
        });
        var res = await TimedAsync(ConfirmAsync(l.A.Client, l.SessionId, body));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        await TimedAsync(Task.Run(async () => { await purge!; return 0; }));
        foreach (var t in new[] { "demo_docs", "doc_exports", "doc_targets", "idempotency_keys" })
            Assert.Equal(0, await DocRowsAsync(t, l.A.UserId));
        Assert.Equal(before, await LockRace.DeadlocksAsync(F));
    }

    [Fact]
    public async Task B25d_A_student_delete_and_a_lecture_delete_take_the_session_first_so_neither_deadlocks()
    {
        var l = await ReadyAsync();
        var rec = await ExportOnceAsync(l);
        var before = await LockRace.DeadlocksAsync(F);
        Task? delete = null;
        // the student delete runs the same transaction shape as confirm; the lecture delete must queue behind its session lock
        Trap.Arm(sql => sql.Contains("INSERT INTO docs.audit_events", StringComparison.Ordinal), async cmd =>
        {
            delete = F.ExecAsync($"delete from lectures.lecture_sessions where id = '{l.SessionId}'");
            Assert.True(await WaitBlockedByAsync(LockRace.Pid(cmd)), "the lecture delete did not wait for the student delete");
        });
        var res = await TimedAsync(l.A.Client.SendAsync(Req(HttpMethod.Delete, $"/demo-docs/{DocIdOf(rec)}")));
        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);
        await TimedAsync(Task.Run(async () => { await delete!; return 0; }));
        Assert.Equal(0, await DocRowsAsync("demo_docs", l.A.UserId));
        Assert.Equal(before, await LockRace.DeadlocksAsync(F));
    }

    // ---------------- B25d: a writer in the FORBIDDEN order is the deadlock victim and repeats the whole attempt ----------------

    private static async Task RunUntilWriterLosesAsync(Func<Task<bool>> attempt)
    {
        for (var i = 0; i < 4; i++)
            if (await attempt()) return;
        Assert.Fail("the test's own side was chosen as the deadlock victim in 4 attempts in a row");
    }

    /// <summary>
    /// The other side takes the doc_targets row FOR UPDATE first (what a writer that locks docs rows before the session would do, forbidden by DXD-01) and
    /// then asks for the session row, which the confirm holds FOR KEY SHARE: the cycle. The confirm started waiting first (on the target row), so its
    /// deadlock check fires first and it is the victim; it must repeat the whole attempt and finish, without raising <c>attempt</c>.
    /// </summary>
    private async Task<bool> ConfirmVictimAttemptAsync(bool lectureGoesAway)
    {
        var l = await ReadyAsync();
        var created = await ExportOnceAsync(l);
        await F.ExecAsync($"update lectures.lecture_sessions set title = 'Deadlock {Guid.NewGuid():N}' where id = '{l.SessionId}'");
        var body = ConfirmBody(await PreviewOkAsync(l.A.Client, l.SessionId, l.NotesId), "update");
        var sides = new List<OtherSide>();
        Trap.Arm(IsKeyLookup, async cmd =>
        {
            var side = await OtherSide.HoldAsync(F, $"select 1 from docs.doc_targets where user_id = '{l.A.UserId}' and session_id = '{l.SessionId}' for update");
            side.TolerateLosing = true;                                          // L-VICTIM
            side.CloseCycleWhenWaiting(F, LockRace.Pid(cmd), $"select 1 from lectures.lecture_sessions where id = '{l.SessionId}' for update",
                lectureGoesAway ? [$"delete from lectures.lecture_sessions where id = '{l.SessionId}'"] : []);
            lock (sides) sides.Add(side);
        });
        var before = await LockRace.DeadlocksAsync(F);

        var res = await TimedAsync(ConfirmAsync(l.A.Client, l.SessionId, body));
        Assert.Single(sides);
        foreach (var s in sides) await using (s) await s.Done;
        if (sides.Any(s => s.LostTheRace)) return false;                          // the test's own side lost: nothing happened, run it again

        await LockRace.AssertDeadlockCountedAsync(F, before);
        if (lectureGoesAway)
        {
            await res.AssertProblemAsync(HttpStatusCode.NotFound, "not_found");
            foreach (var t in new[] { "demo_docs", "doc_exports", "doc_targets", "idempotency_keys" })
                Assert.Equal(0, await DocRowsAsync(t, l.A.UserId));               // no orphan row
        }
        else
        {
            var rec = await OkRecordAsync(res);
            Assert.Equal("updated", rec.GetProperty("outcome").GetString());
            Assert.Equal(1, rec.GetProperty("attempt").GetInt32());                // a deadlock retry does not raise attempt
            Assert.Equal(DocIdOf(created), DocIdOf(rec));
            Assert.Equal(2, rec.GetProperty("docRef").GetProperty("version").GetInt32());   // the aborted attempt's update was rolled back: 2, not 3
            Assert.Equal(1, await DocRowsAsync("audit_events", l.A.UserId, "action = 'export_updated'"));
            Assert.Equal(2, await DocRowsAsync("doc_exports", l.A.UserId));         // the create and this update, no duplicate from the aborted attempt
        }
        return true;
    }

    [Fact]
    public Task B25d_A_confirm_chosen_as_the_deadlock_victim_repeats_the_whole_attempt_without_raising_attempt() => RunUntilWriterLosesAsync(() => ConfirmVictimAttemptAsync(false));

    [Fact]
    public Task B25d_A_confirm_chosen_as_the_deadlock_victim_of_a_lecture_delete_answers_404_and_leaves_no_orphan() => RunUntilWriterLosesAsync(() => ConfirmVictimAttemptAsync(true));

    // ---------------- B25e: insert order and error mapping ----------------

    [Fact]
    public async Task B25e_The_first_confirm_inserts_doc_exports_then_idempotency_keys_then_the_document_then_the_target_then_audit()
    {
        var l = await ReadyAsync();
        await F.ExecAsync("create table if not exists public.zz_docs_order (seq bigserial primary key, tbl text not null, user_id uuid not null)");
        await F.ExecAsync("create or replace function public.zz_docs_order_fn() returns trigger language plpgsql as $f$ begin insert into public.zz_docs_order (tbl, user_id) values (tg_table_name, new.user_id); return new; end $f$");
        foreach (var t in new[] { "doc_exports", "idempotency_keys", "demo_docs", "doc_targets", "audit_events" })
            await F.ExecAsync($"create or replace trigger zz_order_{t} after insert on docs.{t} for each row execute function public.zz_docs_order_fn()");
        var body = ConfirmBody(await PreviewOkAsync(l.A.Client, l.SessionId, l.NotesId), "create");

        var rec = await OkRecordAsync(await ConfirmAsync(l.A.Client, l.SessionId, body));
        Assert.Equal("created", rec.GetProperty("outcome").GetString());
        var order = new List<string>();
        await using (var conn = new NpgsqlConnection(F.ConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand($"select tbl from public.zz_docs_order where user_id = '{l.A.UserId}' order by seq", conn);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) order.Add(r.GetString(0));
        }
        Assert.Equal(["doc_exports", "idempotency_keys", "demo_docs", "doc_targets", "audit_events"], order);
    }

    [Fact]
    public async Task B25e_A_23503_from_the_idempotency_key_foreign_key_is_a_server_error_never_a_calm_404()
    {
        var l = await ReadyAsync();
        var body = ConfirmBody(await PreviewOkAsync(l.A.Client, l.SessionId, l.NotesId), "create");
        // a wrong insert order, simulated inside the request's own transaction: the parent export row is gone when the key is inserted
        Trap.Arm(sql => IsInsert(sql, "idempotency_keys"), async cmd =>
        {
            await using var del = cmd.Connection!.CreateCommand();
            del.Transaction = cmd.Transaction;
            del.CommandText = $"delete from docs.doc_exports where user_id = '{l.A.UserId}'";
            await del.ExecuteNonQueryAsync();
        });
        var res = await ConfirmAsync(l.A.Client, l.SessionId, body);
        await res.AssertProblemAsync(HttpStatusCode.InternalServerError, "internal_error");
        foreach (var t in new[] { "demo_docs", "doc_exports", "doc_targets", "idempotency_keys", "audit_events" })
            Assert.Equal(0, await DocRowsAsync(t, l.A.UserId));
        // and the lecture is untouched: a server bug must not look like "not found"
        Assert.Equal(HttpStatusCode.OK, (await PreviewAsync(l.A.Client, l.SessionId, l.NotesId)).StatusCode);
    }

    [Fact]
    public async Task B25e_B25c_A_23503_from_a_session_foreign_key_means_the_lecture_vanished_mid_write_and_is_404()
    {
        var l = await ReadyAsync();
        var body = ConfirmBody(await PreviewOkAsync(l.A.Client, l.SessionId, l.NotesId), "create");
        // the lecture disappears inside the transaction (the case the session lock exists to prevent) just before the target row is written
        Trap.Arm(sql => IsInsert(sql, "doc_targets"), async cmd =>
        {
            await using var del = cmd.Connection!.CreateCommand();
            del.Transaction = cmd.Transaction;
            del.CommandText = $"delete from lectures.lecture_sessions where id = '{l.SessionId}' and user_id = '{l.A.UserId}'";
            await del.ExecuteNonQueryAsync();
        });
        var res = await ConfirmAsync(l.A.Client, l.SessionId, body);
        await res.AssertProblemAsync(HttpStatusCode.NotFound, "not_found");
        foreach (var t in new[] { "demo_docs", "doc_exports", "doc_targets", "idempotency_keys" })
            Assert.Equal(0, await DocRowsAsync(t, l.A.UserId));
    }

    // ---------------- B25f: the same key for two lectures ----------------

    [Fact]
    public async Task B25f_One_key_for_two_lectures_at_once_lets_one_win_and_the_other_gets_400_never_the_winners_record_and_never_a_500()
    {
        var one = await ReadyAsync();
        var two = await ReadyAsync(one.A);
        var b1 = ConfirmBody(await PreviewOkAsync(one.A.Client, one.SessionId, one.NotesId), "create");
        var b2 = ConfirmBody(await PreviewOkAsync(two.A.Client, two.SessionId, two.NotesId), "create");
        var key = NewKey();
        Task<HttpResponseMessage>? second = null;
        Trap.Arm(IsKeyLookup, async cmd =>
        {
            second = Task.Run(() => two.A.Client.SendAsync(ConfirmReq(two.SessionId, b2, key)));
            Assert.True(await WaitBlockedByAsync(LockRace.Pid(cmd)), "the second lecture's confirm did not wait for the user lock");
        });

        var winner = await OkRecordAsync(await TimedAsync(one.A.Client.SendAsync(ConfirmReq(one.SessionId, b1, key))));
        var loser = await TimedAsync(second!);
        await loser.AssertProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        Assert.DoesNotContain(winner.GetProperty("exportId").GetString()!, await loser.Content.ReadAsStringAsync());
        Assert.Equal(1, await DocRowsAsync("demo_docs", one.A.UserId));
        Assert.Equal(1, await DocRowsAsync("idempotency_keys", one.A.UserId));
        Assert.Equal(0, await F.ScalarAsync($"select count(*) from docs.doc_exports where session_id = '{two.SessionId}'"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task B25f_A_23505_on_the_key_repeats_the_attempt_and_compares_fingerprints_400_for_another_lecture_a_replay_for_the_same_one(bool sameRequest)
    {
        var one = await ReadyAsync();
        var two = await ReadyAsync(one.A);
        var b2 = ConfirmBody(await PreviewOkAsync(two.A.Client, two.SessionId, two.NotesId), "create");
        var key = NewKey();
        var keys = F.Services.GetRequiredService<DocsKeys>();
        var req = new ConfirmRequest((string)b2["notesId"]!, (string)b2["contentDigest"]!, "create", null, null);
        // the competing request: a committed key row (and its export) that appears between our lookup and our insert
        var exportId = Guid.NewGuid();
        var sid = sameRequest ? two.SessionId : one.SessionId;
        var fp = Hashing.Hmac(keys.IdempotencyRing[0], Hashing.Material("POST", $"/sessions/{sid}/docs-export", req.Canonical()));
        var notesGuid = Guid.ParseExact(((string)b2["notesId"]!)[3..], "N");
        var now = DateTimeOffset.UtcNow;
        Trap.Arm(sql => IsInsert(sql, "idempotency_keys"), async _ =>
        {
            await using var conn = new NpgsqlConnection(F.ConnectionString);
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            await using (var c1 = new NpgsqlCommand(
                "insert into docs.doc_exports (id, user_id, session_id, notes_id, provider, mode, status, outcome, content_digest, doc_id, doc_version, attempt, created_at, completed_at) " +
                $"values (@e, @u, @s, @n, 'demo_docs', 'create', 'succeeded', 'created', @d, gen_random_uuid(), 1, 1, @t, @t)", conn, tx))
            {
                c1.Parameters.AddWithValue("e", exportId); c1.Parameters.AddWithValue("u", two.A.UserId); c1.Parameters.AddWithValue("s", Guid.Parse(sameRequest ? two.SessionId : one.SessionId));
                c1.Parameters.AddWithValue("n", notesGuid); c1.Parameters.AddWithValue("d", (string)b2["contentDigest"]!); c1.Parameters.AddWithValue("t", now);
                await c1.ExecuteNonQueryAsync();
            }
            await using (var c2 = new NpgsqlCommand(
                "insert into docs.idempotency_keys (user_id, method, route_template, key_hash, fingerprint_hmac, export_id, created_at) values (@u, 'POST', @r, @k, @f, @e, @t)", conn, tx))
            {
                c2.Parameters.AddWithValue("u", two.A.UserId); c2.Parameters.AddWithValue("r", Routes.ConfirmTemplate);
                c2.Parameters.AddWithValue("k", Hashing.Sha256(key)); c2.Parameters.AddWithValue("f", fp); c2.Parameters.AddWithValue("e", exportId); c2.Parameters.AddWithValue("t", now);
                await c2.ExecuteNonQueryAsync();
            }
            await tx.CommitAsync();
        });

        var res = await TimedAsync(two.A.Client.SendAsync(ConfirmReq(two.SessionId, b2, key)));
        Assert.True(Trap.Fired);
        if (sameRequest)
        {
            Assert.True(Replayed(res));                                                   // the winner's record, as a replay
            var rec = await OkRecordAsync(res);
            Assert.Equal("dxp_" + exportId.ToString("D"), rec.GetProperty("exportId").GetString());
            Assert.Equal(0, await DocRowsAsync("demo_docs", two.A.UserId));
        }
        else
        {
            await res.AssertProblemAsync(HttpStatusCode.BadRequest, "validation_failed");   // never the other lecture's record, never a 500
            Assert.DoesNotContain(exportId.ToString("D"), await res.Content.ReadAsStringAsync());
        }
        Assert.Equal(1, await DocRowsAsync("idempotency_keys", two.A.UserId));
    }

    // ---------------- B25g: the per-user cap and the user lock ----------------

    [Fact]
    public async Task B25g_Many_lectures_of_one_user_confirming_at_once_never_pass_the_per_user_cap()
    {
        // The factory's cap is 3 (the shipped one is 200; DocsExportFlowTests fills 199 and refuses the 201st). Eight lectures race for three places.
        var first = await ReadyAsync();
        var lectures = new List<Lecture> { first };
        for (var i = 0; i < 7; i++) lectures.Add(await ReadyBySqlAsync(first.A));
        var bodies = new List<(Lecture L, Dictionary<string, object?> Body)>();
        foreach (var l in lectures) bodies.Add((l, ConfirmBody(await PreviewOkAsync(l.A.Client, l.SessionId, l.NotesId), "create")));

        var results = await Task.WhenAll(bodies.Select(b => Task.Run(() => b.L.A.Client.SendAsync(ConfirmReq(b.L.SessionId, b.Body, NewKey())))));
        Assert.Equal(3, results.Count(r => r.StatusCode == HttpStatusCode.OK));
        foreach (var r in results.Where(r => r.StatusCode != HttpStatusCode.OK))
            await r.AssertProblemAsync(HttpStatusCode.Conflict, "invalid_state");
        Assert.Equal(3, await DocRowsAsync("demo_docs", first.A.UserId));
        Assert.Equal(3, await DocRowsAsync("doc_exports", first.A.UserId));
    }

    [Fact]
    public async Task B25g_Two_lectures_of_one_user_take_the_user_lock_after_their_own_session_locks_and_a_lecture_delete_in_between_neither_deadlocks_nor_leaves_an_orphan()
    {
        var one = await ReadyAsync();
        var two = await ReadyAsync(one.A);
        var b1 = ConfirmBody(await PreviewOkAsync(one.A.Client, one.SessionId, one.NotesId), "create");
        var b2 = ConfirmBody(await PreviewOkAsync(two.A.Client, two.SessionId, two.NotesId), "create");
        var before = await LockRace.DeadlocksAsync(F);
        Task<HttpResponseMessage>? second = null;
        Task? delete = null;
        int firstPid = 0;
        Trap.Arm(IsKeyLookup, async cmd =>
        {
            firstPid = LockRace.Pid(cmd);
            second = Task.Run(() => two.A.Client.SendAsync(ConfirmReq(two.SessionId, b2, NewKey())));
            Assert.True(await WaitBlockedByAsync(firstPid), "the second lecture did not wait for the user lock");
            // the second confirm already holds ITS session row (KEY SHARE) and waits for the user lock; a delete of that lecture queues behind it
            var secondPid = await F.ScalarAsync($"select pid from pg_stat_activity where {firstPid} = any(pg_blocking_pids(pid)) limit 1");
            delete = F.ExecAsync($"delete from lectures.lecture_sessions where id = '{two.SessionId}'");
            Assert.True(await WaitBlockedByAsync((int)secondPid), "the lecture delete did not wait for the second confirm's session lock");
        });

        var r1 = await OkRecordAsync(await TimedAsync(one.A.Client.SendAsync(ConfirmReq(one.SessionId, b1, NewKey()))));
        var r2 = await TimedAsync(second!);
        await TimedAsync(Task.Run(async () => { await delete!; return 0; }));
        Assert.Equal("created", r1.GetProperty("outcome").GetString());
        Assert.Equal(HttpStatusCode.OK, r2.StatusCode);                                  // the second finished first, then its lecture went
        Assert.Equal(before, await LockRace.DeadlocksAsync(F));                          // no inverted wait: the chain was delete -> second -> first
        Assert.Equal(1, await DocRowsAsync("demo_docs", one.A.UserId));                  // only the first lecture's document is left
        Assert.Equal(0, await F.ScalarAsync($"select count(*) from docs.demo_docs where session_id = '{two.SessionId}'"));
        Assert.Equal(0, await F.ScalarAsync($"select count(*) from docs.doc_exports where session_id = '{two.SessionId}'"));
    }

    // ---------------- B32b: the slow fault is capped and cancellable ----------------

    [Fact]
    public async Task B32b_The_slow_fault_is_capped_at_five_seconds_and_a_lecture_delete_waits_no_longer_than_that()
    {
        var l = await ReadyAsync();
        var body = ConfirmBody(await PreviewOkAsync(l.A.Client, l.SessionId, l.NotesId), "create");
        Task? delete = null;
        var watch = Stopwatch.StartNew();
        Trap.Arm(IsKeyLookup, cmd =>
        {
            delete = Task.Run(async () => { await Task.Delay(300); await F.ExecAsync($"delete from lectures.lecture_sessions where id = '{l.SessionId}'"); });
            return Task.CompletedTask;
        });
        var res = await TimedAsync(ConfirmAsync(l.A.Client, l.SessionId, body, fault: "slow:600"));   // asks for ten minutes
        var confirmed = watch.Elapsed;
        var rec = await OkRecordAsync(res);
        Assert.Equal("created", rec.GetProperty("outcome").GetString());
        Assert.InRange(confirmed.TotalSeconds, 4.5, 9.0);                                    // the cap is 5 s
        await TimedAsync(Task.Run(async () => { await delete!; return 0; }));
        Assert.InRange(watch.Elapsed.TotalSeconds, 4.5, 12.0);                               // the delete waited for the lock, but only up to the cap
        Assert.Equal(0, await DocRowsAsync("demo_docs", l.A.UserId));
    }

    [Fact]
    public async Task B32b_A_cancelled_request_releases_the_session_lock_at_once_so_the_slow_fault_cannot_hold_it()
    {
        var l = await ReadyAsync();
        var body = ConfirmBody(await PreviewOkAsync(l.A.Client, l.SessionId, l.NotesId), "create");
        using var cts = new CancellationTokenSource();
        var req = ConfirmReq(l.SessionId, body, NewKey(), "slow:5");
        var inFlight = l.A.Client.SendAsync(req, cts.Token);
        Assert.True(await Rt.WaitAsync(async () => await F.ScalarAsync("select count(*) from pg_locks where locktype = 'advisory' and granted and database = (select oid from pg_database where datname = current_database())") > 0, 10000), "the confirm never took its locks");
        var watch = Stopwatch.StartNew();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => inFlight);
        // the request's transaction ends when the client goes away: a delete of the lecture is not held for the rest of the five seconds
        await F.ExecAsync($"delete from lectures.lecture_sessions where id = '{l.SessionId}'");
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(3.5), $"the delete waited {watch.Elapsed.TotalSeconds:F1} s for a cancelled request");
    }
}
