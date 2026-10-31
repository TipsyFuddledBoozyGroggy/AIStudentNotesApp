using System.Net;
using Tether.Ai.Persistence;
using Tether.IntegrationTests.Infrastructure;
using Tether.Lectures.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace Tether.IntegrationTests;

public sealed class NotesMigrationFactory : ApiFactory { }

/// <summary>
/// NAS-R06: A006 (lecture_notes.notice_version / started_at, ai_usage.press_id) and M008 (audit_events.subject_id / notice_version) hold the
/// NAS-05/NAS-08 evidence; like A005, their Down refuses while those columns hold data, and leaves everything in place.
/// </summary>
public class NotesMigrationGuardTests(NotesMigrationFactory factory) : NotesReviewTestBase(factory), IClassFixture<NotesMigrationFactory>
{
    private const string A005 = "20261031132733_A005_LectureNotes";
    private const string M007 = "20261028120000_M007_SessionDemoSource";

    private async Task MigrateAsync<TContext>(string? target) where TContext : DbContext
    {
        // Retries ONLY a deadlock/serialization failure with the host's own background workers (see PersistenceTests.MigrateWithRetryAsync);
        // the guards' RAISE EXCEPTION (P0001) is never retried, so the assertions below still see it.
        for (var attempt = 1; ; attempt++)
        {
            await using var sp = F.Services.CreateAsyncScope();
            try { await sp.ServiceProvider.GetRequiredService<TContext>().GetService<IMigrator>().MigrateAsync(target); return; }
            catch (Exception ex) when (attempt < 5 && IsDeadlock(ex)) { await Task.Delay(Random.Shared.Next(50, 250)); }
        }
    }

    private static bool IsDeadlock(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
            if (e is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.DeadlockDetected or Npgsql.PostgresErrorCodes.SerializationFailure }) return true;
        return false;
    }

    private Task<long> HasColumnAsync(string schema, string table, string column) =>
        F.ScalarAsync($"select count(*) from information_schema.columns where table_schema = '{schema}' and table_name = '{table}' and column_name = '{column}'");

    [Fact]
    public async Task A006_and_M008_refuse_to_roll_back_while_their_evidence_columns_hold_data()
    {
        var (a, id) = await StoppedSessionAsync();
        Assert.Equal(HttpStatusCode.Accepted, (await PressAsync(a.Client, id)).StatusCode);
        Assert.Equal("Ready", (await WaitSettledAsync(a.Client, id)).GetProperty("status").GetString());
        Assert.True(await F.ScalarAsync("select count(*) from ai.ai_usage where press_id is not null") > 0);
        Assert.Equal(1, await F.ScalarAsync($"select count(*) from lectures.audit_events where user_id = '{a.UserId}' and subject_id is not null and notice_version is not null"));

        var ai = await Assert.ThrowsAnyAsync<Exception>(() => MigrateAsync<AiDbContext>(A005));
        Assert.Contains("A006 cannot be rolled back", ai.ToString());
        Assert.Equal(1, await HasColumnAsync("ai", "lecture_notes", "notice_version"));
        Assert.Equal(1, await HasColumnAsync("ai", "lecture_notes", "started_at"));
        Assert.Equal(1, await HasColumnAsync("ai", "ai_usage", "press_id"));
        Assert.Equal(1, await F.ScalarAsync($"select count(*) from ai.lecture_notes where session_id = '{id}' and notice_version = 'notes-notice-draft-1' and started_at is not null"));

        var lectures = await Assert.ThrowsAnyAsync<Exception>(() => MigrateAsync<LecturesDbContext>(M007));
        Assert.Contains("M008 cannot be rolled back", lectures.ToString());
        Assert.Equal(1, await HasColumnAsync("lectures", "audit_events", "subject_id"));
        Assert.Equal(1, await HasColumnAsync("lectures", "audit_events", "notice_version"));

        // The later migrations (A007, M009) did roll back on the way down; forward again restores the full schema.
        await MigrateAsync<AiDbContext>(null);
        await MigrateAsync<LecturesDbContext>(null);
        Assert.Equal(1, await F.ScalarAsync("select count(*) from information_schema.columns where table_schema = 'ai' and table_name = 'lecture_notes' and column_name = 'notice_version' and is_nullable = 'NO'"));
        Assert.Equal(1, await F.ScalarAsync("select count(*) from pg_constraint where conname = 'ck_audit_events_notes_requested'"));
    }
}
