using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Tether.IntegrationTests.Infrastructure;
using Tether.Syllabus.Persistence;

namespace Tether.IntegrationTests.Syllabus;

public sealed class SyllabusFlagsCheckFactory : ApiFactory { }

/// <summary>
/// SBV-17 (docs/security/sbr-fixes-verification.md, sixth pass): the schema rule "injection_suspect, supersession_cue or low_text_quality means
/// please_check" applies to ANY item (syllabus-item.schema.json allOf P2-08/P2-19), but ck_items_flags_force_check (S001) covered resolved
/// items only, and policy rows stored before bb9b124 were never corrected. S003 first corrects the non-conforming rows, then widens the check
/// to every status for those three flags (the seven-flag rule for resolved items is kept). Its own throwaway database; it rolls the
/// Syllabus module back to S002 to plant legacy rows the old check allowed.
/// </summary>
public class SyllabusFlagsCheckMigrationTests(SyllabusFlagsCheckFactory f) : SyllabusTestBase(f), IClassFixture<SyllabusFlagsCheckFactory>
{
    private const string S002 = "20261028194922_S002_SyllabusIntegrity";
    private const string Unclear = "One detail here is unclear. Please check.";

    private async Task MigrateSyllabusAsync(string? target)
    {
        // Retries only a deadlock/serialization failure with the host's own syllabus worker (see PersistenceTests.MigrateWithRetryAsync).
        for (var attempt = 1; ; attempt++)
        {
            await using var sp = F.Services.CreateAsyncScope();
            try { await sp.ServiceProvider.GetRequiredService<SyllabusDbContext>().GetService<IMigrator>().MigrateAsync(target); return; }
            catch (Exception ex) when (attempt < 5 && IsDeadlock(ex)) { await Task.Delay(Random.Shared.Next(50, 250)); }
        }
    }

    private static bool IsDeadlock(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
            if (e is PostgresException { SqlState: PostgresErrorCodes.DeadlockDetected or PostgresErrorCodes.SerializationFailure }) return true;
        return false;
    }

    private async Task<string> RowAsync(Guid id)
    {
        await using var conn = new NpgsqlConnection(F.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand($"select confidence || '|' || confidence_reason || '|' || array_to_string(flags, ',') || '|' || row_version from syllabus.items where id = '{id}'", conn);
        return (string)(await cmd.ExecuteScalarAsync())!;
    }

    private async Task<Guid> IdAsync(string syllabusId, string where)
    {
        await using var conn = new NpgsqlConnection(F.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand($"select id from syllabus.items where syllabus_id = '{syllabusId}' and {where}", conn);
        return (Guid)(await cmd.ExecuteScalarAsync())!;
    }

    /// <summary>Copies an existing item as a new row (new id and item_key) with the given confidence and flags, by raw INSERT.</summary>
    private async Task InsertCopyAsync(Guid source, string confidence, string flags)
    {
        await using var conn = new NpgsqlConnection(F.ConnectionString);
        await conn.OpenAsync();
        var cols = new List<string>();
        await using (var q = new NpgsqlCommand("select column_name from information_schema.columns where table_schema = 'syllabus' and table_name = 'items' and is_generated = 'NEVER' order by ordinal_position", conn))
        await using (var r = await q.ExecuteReaderAsync())
            while (await r.ReadAsync()) cols.Add(r.GetString(0));
        var values = cols.Select(c => c switch
        {
            "id" => "gen_random_uuid()",
            "item_key" => $"'ik_{Guid.NewGuid():N}'",
            "confidence" => $"'{confidence}'",
            "confidence_reason" => confidence == "please_check" ? $"'{Unclear}'" : "'No date is written for this.'",
            "flags" => $"'{flags}'::text[]",
            _ => c,
        });
        await using var cmd = new NpgsqlCommand($"insert into syllabus.items ({string.Join(", ", cols)}) select {string.Join(", ", values)} from syllabus.items where id = '{source}'", conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private static void AssertForceCheckViolation(Exception ex)
    {
        var pg = Assert.IsType<PostgresException>(ex);
        Assert.Equal(PostgresErrorCodes.CheckViolation, pg.SqlState);
        Assert.Equal("ck_items_flags_force_check", pg.ConstraintName);
    }

    [Fact]
    public async Task SBV17_S003_corrects_legacy_undated_rows_then_the_database_refuses_a_forcing_flag_without_please_check_for_every_status()
    {
        var a = await NewActorAsync(); var course = await CourseAsync(a.Client);
        var done = await CreateReadyAsync(a.Client, course, Paste(
            "Midterm exam: Oct 19, 2026 at 10:00 AM\nFinal exam: TBA\nLate work policy: 10% per day.\nAttendance policy: sign in every week.\nGrading policy: the curve is set at the end."));
        Assert.Equal("ready", done.GetProperty("status").GetString());
        var sid = Guid32(done.GetProperty("id").GetString()!);
        var late = await IdAsync(sid, "kind = 'policy' and snippet_text like 'Late work%'");
        var attendance = await IdAsync(sid, "kind = 'policy' and snippet_text like 'Attendance%'");
        var grading = await IdAsync(sid, "kind = 'policy' and snippet_text like 'Grading%'");
        var final = await IdAsync(sid, "when_status = 'needs_date'");
        var midterm = await IdAsync(sid, "when_status = 'resolved' and title like 'Midterm%'");
        var midtermBefore = await RowAsync(midterm);

        // ---- the state before bb9b124 / S003: S002 is the latest Syllabus migration, the old check allows these rows ----
        await MigrateSyllabusAsync(S002);
        Assert.Equal(1, await F.ScalarAsync("select count(*) from pg_constraint where conname = 'ck_items_flags_force_check'"));
        await F.ExecAsync($"update syllabus.items set flags = array['supersession_cue'], confidence = 'probably_right', confidence_reason = 'No date is written for this.' where id = '{late}'");
        await F.ExecAsync($"update syllabus.items set flags = array['injection_suspect','low_text_quality'], confidence = 'probably_right', confidence_reason = 'No date is written for this.' where id = '{attendance}'");
        await F.ExecAsync($"update syllabus.items set flags = array['injection_suspect'], confidence = 'probably_right', confidence_reason = 'The date is to be announced.' where id = '{final}'");
        // controls: a clean policy, and a policy with a flag that only forces please_check on DATED items (the seven-flag rule stays resolved-only)
        await F.ExecAsync($"update syllabus.items set flags = array['tentative_language'], confidence = 'probably_right', confidence_reason = 'No date is written for this.' where id = '{grading}'");
        var lateBefore = await RowAsync(late); var attendanceBefore = await RowAsync(attendance); var finalBefore = await RowAsync(final); var gradingBefore = await RowAsync(grading);

        // ---- S003 up: the data is corrected first, then the check is widened (it would fail to add over a non-conforming row) ----
        await MigrateSyllabusAsync(null);
        await using (var sp = F.Services.CreateAsyncScope())
            Assert.Empty(await sp.ServiceProvider.GetRequiredService<SyllabusDbContext>().Database.GetPendingMigrationsAsync());
        static string Version(string row) => row[(row.LastIndexOf('|') + 1)..];
        static string Bumped(string row) => (int.Parse(Version(row)) + 1).ToString();
        Assert.Equal($"please_check|{Unclear}|supersession_cue|{Bumped(lateBefore)}", await RowAsync(late));
        Assert.Equal($"please_check|{Unclear}|injection_suspect,low_text_quality|{Bumped(attendanceBefore)}", await RowAsync(attendance));
        Assert.Equal($"please_check|{Unclear}|injection_suspect|{Bumped(finalBefore)}", await RowAsync(final));
        Assert.Equal(gradingBefore, await RowAsync(grading));                           // untouched: no schema-forcing flag
        Assert.Equal(midtermBefore, await RowAsync(midterm));                           // untouched: resolved and conforming
        Assert.Equal(0, await F.ScalarAsync("""
            select count(*) from syllabus.items
            where flags && array['injection_suspect','supersession_cue','low_text_quality']::text[] and confidence <> 'please_check'
            """));

        // the page loads again: every served item passes the item schema (the web parser refused the whole page before)
        var items = await ItemsAsync(a.Client, done.GetProperty("id").GetString()!);
        Assert.Equal(5, items.Count);
        foreach (var i in items) SyllabusSchemas.AssertValid("syllabus-item", i);

        // ---- the database now refuses a forcing flag without please_check on every status, by INSERT and by UPDATE ----
        AssertForceCheckViolation(await Record.ExceptionAsync(() => InsertCopyAsync(late, "probably_right", "{supersession_cue}")));
        AssertForceCheckViolation(await Record.ExceptionAsync(() => InsertCopyAsync(late, "probably_right", "{injection_suspect}")));
        AssertForceCheckViolation(await Record.ExceptionAsync(() => InsertCopyAsync(final, "probably_right", "{low_text_quality}")));
        AssertForceCheckViolation(await Record.ExceptionAsync(() => InsertCopyAsync(midterm, "looks_right", "{supersession_cue}")));
        AssertForceCheckViolation(await Record.ExceptionAsync(() => InsertCopyAsync(midterm, "probably_right", "{weekday_mismatch}")));   // the seven-flag rule is kept
        AssertForceCheckViolation(await Record.ExceptionAsync(() => F.ExecAsync($"update syllabus.items set confidence = 'probably_right' where id = '{late}'")));
        AssertForceCheckViolation(await Record.ExceptionAsync(() => F.ExecAsync($"update syllabus.items set flags = array['low_text_quality'] where id = '{grading}'")));
        // still allowed: please_check with the flags, and a non-schema flag on an undated item
        await InsertCopyAsync(late, "please_check", "{supersession_cue,injection_suspect,low_text_quality}");
        await InsertCopyAsync(final, "probably_right", "{tentative_language}");

        // ---- Down restores the old check exactly (resolved items only); Up again corrects what the old check let in ----
        await MigrateSyllabusAsync(S002);
        Assert.Equal(1, await F.ScalarAsync("select count(*) from pg_constraint where conname = 'ck_items_flags_force_check'"));
        await InsertCopyAsync(late, "probably_right", "{supersession_cue}");
        AssertForceCheckViolation(await Record.ExceptionAsync(() => InsertCopyAsync(midterm, "probably_right", "{supersession_cue}")));
        await MigrateSyllabusAsync(null);
        Assert.Equal(0, await F.ScalarAsync("""
            select count(*) from syllabus.items
            where flags && array['injection_suspect','supersession_cue','low_text_quality']::text[] and confidence <> 'please_check'
            """));
        Assert.Equal(0, await F.ScalarAsync($"select count(*) from syllabus.items where syllabus_id = '{sid}' and confidence = 'please_check' and confidence_reason = 'No date is written for this.'"));
    }
}
