using System.Net;
using Tether.IntegrationTests.Infrastructure;
using Npgsql;

namespace Tether.IntegrationTests;

/// <summary>DX-B02 (B03a, B04, B05, B05b): the docs schema's foreign keys, the cascade with the lecture and the course, and the erasure path. Mock stack only.</summary>
[Collection(RacePool.Name)]
public class DocsExportPersistenceTests(DocsFactory factory) : DocsExportTestBase(factory), IClassFixture<DocsFactory>
{
    private async Task<List<(string Name, string Table, string RefTable, string OnDelete, string Columns)>> DocsForeignKeysAsync()
    {
        await using var conn = new NpgsqlConnection(F.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            select con.conname, c.relname, rc.relname, con.confdeltype::text,
                   (select string_agg(a.attname, ',' order by k.ord) from unnest(con.conkey) with ordinality k(attnum, ord) join pg_attribute a on a.attrelid = con.conrelid and a.attnum = k.attnum)
            from pg_constraint con
            join pg_class c on c.oid = con.conrelid join pg_namespace ns on ns.oid = c.relnamespace
            join pg_class rc on rc.oid = con.confrelid
            where con.contype = 'f' and ns.nspname = 'docs'
            order by con.conname
            """, conn);
        var list = new List<(string, string, string, string, string)>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) list.Add((r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4)));
        return list;
    }

    [Fact]
    public async Task B03a_Every_docs_foreign_key_cascades_the_key_table_cascades_through_its_export_and_nothing_between_docs_tables_is_RESTRICT()
    {
        _ = F.CreateClient();
        var fks = await DocsForeignKeysAsync();
        Assert.Equal(
        [
            ("fk_demo_docs_session_user", "demo_docs", "lecture_sessions", "c", "session_id,user_id"),
            ("fk_doc_exports_session_user", "doc_exports", "lecture_sessions", "c", "session_id,user_id"),
            ("fk_doc_targets_session_user", "doc_targets", "lecture_sessions", "c", "session_id,user_id"),
            ("fk_docs_audit_events_user", "audit_events", "users", "c", "user_id"),
            ("fk_docs_idempotency_keys_export_user", "idempotency_keys", "doc_exports", "c", "export_id,user_id"),
        ], fks.OrderBy(f => f.Name, StringComparer.Ordinal).ToList());
        Assert.DoesNotContain(fks, f => f.OnDelete != "c");
    }

    [Fact]
    public async Task B04_Deleting_a_lecture_removes_its_exports_targets_Demo_Docs_and_keys_keeps_the_content_free_audit_and_makes_a_replay_404()
    {
        var l = await ReadyAsync();
        var p = await PreviewOkAsync(l.A.Client, l.SessionId, l.NotesId);
        var body = ConfirmBody(p, "create");
        var key = NewKey();
        var rec = await OkRecordAsync(await ConfirmAsync(l.A.Client, l.SessionId, body, key));
        var other = await ReadyAsync(l.A);
        await ExportOnceAsync(other);                                            // a second lecture of the same user that must survive

        Assert.Equal(HttpStatusCode.NoContent, (await l.A.Client.DeleteAsync($"{Api}/sessions/{l.SessionId}")).StatusCode);
        foreach (var t in new[] { "demo_docs", "doc_exports", "doc_targets" })
            Assert.Equal(0, await F.ScalarAsync($"select count(*) from docs.{t} where session_id = '{l.SessionId}'"));
        Assert.Equal(1, await DocRowsAsync("idempotency_keys", l.A.UserId));      // the key of the lecture that went is gone (cascade through its export); the other lecture's key stays
        Assert.Equal(0, await F.ScalarAsync($"select count(*) from docs.idempotency_keys k where not exists (select 1 from docs.doc_exports e where e.id = k.export_id)"));
        Assert.Equal(1, await DocRowsAsync("audit_events", l.A.UserId, $"session_id = '{l.SessionId}'"));   // content free, no FK to the lecture
        Assert.Equal(1, await DocRowsAsync("demo_docs", l.A.UserId));
        // a replay after the delete is 404, never the original record; the document and the state are gone too
        await (await ConfirmAsync(l.A.Client, l.SessionId, body, key)).AssertProblemAsync(HttpStatusCode.NotFound, "not_found");
        await (await l.A.Client.GetAsync($"{Api}/demo-docs/{DocIdOf(rec)}")).AssertProblemAsync(HttpStatusCode.NotFound, "not_found");
        await (await l.A.Client.GetAsync($"{Api}/sessions/{l.SessionId}/docs-export")).AssertProblemAsync(HttpStatusCode.NotFound, "not_found");
    }

    [Fact]
    public async Task B05_Purging_a_course_does_the_same_for_all_its_lectures()
    {
        var a = await NewActorAsync();
        var (_, s1) = await StoppedSessionAsync(a);
        Assert.Equal(HttpStatusCode.Accepted, (await PressAsync(a.Client, s1)).StatusCode);
        var notes = await WaitSettledAsync(a.Client, s1);
        var l1 = new Lecture(a, s1, notes.GetProperty("notesId").GetString()!);
        await ExportOnceAsync(l1);
        var course = await F.TextAsync($"select course_id from lectures.lecture_sessions where id = '{s1}'");
        var keep = await ReadyAsync(a);
        await ExportOnceAsync(keep);

        Assert.Equal(HttpStatusCode.NoContent, (await a.Client.DeleteAsync($"{Api}/courses/{course}?purge=true")).StatusCode);
        foreach (var t in new[] { "demo_docs", "doc_exports", "doc_targets" })
            Assert.Equal(0, await F.ScalarAsync($"select count(*) from docs.{t} where session_id = '{s1}'"));
        Assert.Equal(1, await DocRowsAsync("idempotency_keys", a.UserId));        // only the other course's lecture keeps its key
        Assert.Equal(1, await DocRowsAsync("audit_events", a.UserId, $"session_id = '{s1}'"));
        Assert.Equal(1, await DocRowsAsync("demo_docs", a.UserId));            // the other course's lecture is untouched
    }

    [Fact]
    public async Task B05b_Full_account_erasure_removes_the_audit_table_too_and_touches_no_other_user()
    {
        var gone = await ReadyAsync();
        await ExportOnceAsync(gone);
        await F.ExecAsync($"update lectures.lecture_sessions set title = 'x' where id = '{gone.SessionId}'");
        var p = await PreviewOkAsync(gone.A.Client, gone.SessionId, gone.NotesId);
        await OkRecordAsync(await ConfirmAsync(gone.A.Client, gone.SessionId, ConfirmBody(p, "create_another")));
        var kept = await ReadyAsync();
        await ExportOnceAsync(kept);
        Assert.True(await DocRowsAsync("audit_events", gone.A.UserId) >= 2);

        // there is no account-deletion API yet (finish-backlog item 11); the database cascade is the erasure path (PO-20aw), as in DeletionAndExportSweepTests
        await F.ExecAsync($"delete from identity.users where id = '{gone.A.UserId}'");
        foreach (var t in new[] { "demo_docs", "doc_exports", "doc_targets", "idempotency_keys", "audit_events" })
            Assert.Equal(0, await DocRowsAsync(t, gone.A.UserId));
        Assert.Equal(1, await DocRowsAsync("audit_events", kept.A.UserId));
        Assert.Equal(1, await DocRowsAsync("demo_docs", kept.A.UserId));
    }
}
