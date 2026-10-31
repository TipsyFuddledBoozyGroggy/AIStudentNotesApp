using System.Collections.Concurrent;
using System.Data.Common;
using Tether.Lectures.Contracts;
using Tether.Lectures.Domain;
using Tether.Lectures.Persistence;
using Tether.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Tether.Lectures.Application;

internal static class StateMapping
{
    public static LectureState ToContract(this SessionState s) => s switch
    {
        SessionState.Active => LectureState.Active,
        SessionState.Paused => LectureState.Paused,
        SessionState.Stopped => LectureState.Stopped,
        _ => LectureState.Failed,
    };
}

/// <summary>The public ports other modules use (see Tether.Lectures.Contracts). Reads go through the user-scoped query filter.</summary>
internal sealed class SessionPortsService(LecturesDbContext db, ICurrentUser currentUser) : ISessionDirectory, ISessionEventReader, ISessionSequencer
{
    public async Task<SessionInfo?> FindAsync(Guid userId, Guid sessionId, CancellationToken ct = default)
    {
        if (currentUser.UserId != userId) return null; // wrong scope behaves like "not found"
        var row = await (from s in db.Sessions.AsNoTracking()
                         join k in db.Counters.AsNoTracking() on s.Id equals k.SessionId
                         where s.Id == sessionId
                         select new { s.Id, s.UserId, s.CourseId, s.State, k.LastSeq, s.StartedAt }).FirstOrDefaultAsync(ct);
        return row is null ? null : new SessionInfo(row.Id, row.UserId, row.CourseId, row.State.ToContract(), row.LastSeq, row.StartedAt);
    }

    public async Task<IReadOnlyList<SessionStateRecord>> ReadAfterAsync(Guid userId, Guid sessionId, long afterSeq, int limit, CancellationToken ct = default)
    {
        if (currentUser.UserId != userId) return [];
        var rows = await db.SessionEvents.AsNoTracking()
            .Where(e => e.SessionId == sessionId && e.Seq > afterSeq)
            .OrderBy(e => e.Seq).Take(limit).ToListAsync(ct);
        return rows.Select(e => new SessionStateRecord(e.Seq, e.State.ToContract(), e.Reason, e.OccurredAt)).ToList();
    }

    public async Task<long?> AllocateAsync(DbTransaction transaction, Guid userId, Guid sessionId, CancellationToken ct = default)
    {
        var conn = transaction.Connection ?? throw new InvalidOperationException("Transaction has no connection.");
        // Statement 1 takes the counter row lock. Any pause/stop/delete bumps or deletes the same row, so it serialises with us.
        await using (var bump = conn.CreateCommand())
        {
            bump.Transaction = transaction;
            bump.CommandText = "UPDATE lectures.session_counters SET last_seq = last_seq + 1 WHERE session_id = @s AND user_id = @u RETURNING last_seq";
            AddParam(bump, "s", sessionId); AddParam(bump, "u", userId);
            var seq = await bump.ExecuteScalarAsync(ct);
            if (seq is null) return null; // session deleted (or never the user's)
            // Statement 2 runs with a fresh snapshot, so it sees any pause/stop that committed while we waited for the lock.
            await using var check = conn.CreateCommand();
            check.Transaction = transaction;
            check.CommandText = "SELECT state FROM lectures.lecture_sessions WHERE id = @s AND user_id = @u";
            AddParam(check, "s", sessionId); AddParam(check, "u", userId);
            return (await check.ExecuteScalarAsync(ct)) as string == nameof(SessionState.Active) ? Convert.ToInt64(seq) : null;
        }
    }

    private static void AddParam(DbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name; p.Value = value;
        cmd.Parameters.Add(p);
    }
}

/// <summary>
/// <see cref="ISessionAuditLog"/>: one content-free row per call (C-30). The table is append-only (M006 trigger) and keeps the row after
/// the session is deleted, like every other session audit event.
/// </summary>
internal sealed class SessionAuditLog(LecturesDbContext db, ICurrentUser currentUser, TimeProvider clock) : ISessionAuditLog
{
    public async Task<bool> RecordNotesRequestedAsync(Guid userId, Guid sessionId, Guid pressId, string noticeVersion, CancellationToken ct = default)
    {
        if (noticeVersion is not { Length: >= 1 and <= 64 } || !noticeVersion.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '.' or '_' or '-'))
            throw new ArgumentException("A notice id is 1 to 64 of [A-Za-z0-9._-].", nameof(noticeVersion)); // programming error: the API validates first
        if (currentUser.UserId != userId) return false; // wrong scope behaves like "nothing to audit against"
        var row = new AuditEvent
        {
            Id = Ids.New(), UserId = userId, SessionId = sessionId, Action = AuditActions.NotesRequested, OccurredAt = clock.GetUtcNow(),
            SubjectId = pressId, NoticeVersion = noticeVersion,
        };
        db.AuditEvents.Add(row);
        try { await db.SaveChangesAsync(ct); }
        finally { db.Entry(row).State = EntityState.Detached; }
        return true;
    }
}

/// <summary>In-memory last-activity marks (hub Ping, audio frames). Lost on restart; the job then falls back to the last state event.</summary>
internal sealed class SessionActivityTracker(TimeProvider clock) : ISessionActivity
{
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _last = new();
    public void Touch(Guid sessionId) => _last[sessionId] = clock.GetUtcNow();
    public DateTimeOffset? LastActivity(Guid sessionId) => _last.TryGetValue(sessionId, out var t) ? t : null;
    public void Forget(Guid sessionId) => _last.TryRemove(sessionId, out _);
}

/// <summary>
/// Limits. Defaults for the unattended-session timings are the C-17 proposals and are UNCONFIRMED Product Owner decisions
/// (docs/API_CONTRACTS.md section 7); the transition caps are B1-06 proposals.
/// </summary>
internal sealed record LectureLimits(
    int MaxTransitionsPerMinute, int MaxTransitionsPerSession,
    TimeSpan AutoPauseAfterNoActivity, TimeSpan AutoStopAfterPaused, TimeSpan HardCap)
{
    public static LectureLimits From(Microsoft.Extensions.Configuration.IConfiguration c) => new(
        c.GetValue("Lectures:MaxTransitionsPerMinute", 12),
        c.GetValue("Lectures:MaxTransitionsPerSession", 200),
        TimeSpan.FromSeconds(c.GetValue("Lectures:AutoPauseNoActivitySeconds", 300)),
        TimeSpan.FromSeconds(c.GetValue("Lectures:AutoStopPausedSeconds", 1800)),
        TimeSpan.FromSeconds(c.GetValue("Lectures:HardCapSeconds", 3 * 3600)));
}
