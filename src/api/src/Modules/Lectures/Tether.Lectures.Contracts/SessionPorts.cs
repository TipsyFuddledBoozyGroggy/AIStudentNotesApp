using System.Data.Common;

namespace Tether.Lectures.Contracts;

/// <summary>Session lifecycle state as seen by other modules (mirrors <c>Session.state</c> on the wire).</summary>
public enum LectureState { Active, Paused, Stopped, Failed }

/// <summary>Minimal, content-free session facts other modules may read.</summary>
public sealed record SessionInfo(Guid SessionId, Guid UserId, Guid CourseId, LectureState State, long LastSeq, DateTimeOffset StartedAt);

/// <summary>A persisted <c>session.state</c> event (shares the per-session seq counter with transcript events).</summary>
public sealed record SessionStateRecord(long Seq, LectureState State, string? Reason, DateTimeOffset OccurredAt);

/// <summary>Raised after a state change has been committed.</summary>
public sealed record SessionStateChange(Guid UserId, Guid SessionId, LectureState State, string? Reason, long Seq, DateTimeOffset OccurredAt);

/// <summary>
/// Owner-scoped session lookups. A session that does not exist and a session that belongs to someone else are
/// indistinguishable: both return null (C-09). The caller must run in a user scope for <c>userId</c>.
/// </summary>
public interface ISessionDirectory
{
    Task<SessionInfo?> FindAsync(Guid userId, Guid sessionId, CancellationToken ct = default);
}

/// <summary>Reads persisted <c>session.state</c> events so transcript replay can merge them in seq order.</summary>
public interface ISessionEventReader
{
    Task<IReadOnlyList<SessionStateRecord>> ReadAfterAsync(Guid userId, Guid sessionId, long afterSeq, int limit, CancellationToken ct = default);
}

/// <summary>
/// Hands out the next per-session seq (C-10) inside the caller's transaction, so the numbered event and the counter
/// move together and seq stays gap-free. Returns null, having changed nothing the caller should keep, when the session
/// no longer exists for the user or is not Active (the caller must roll back). A concurrent pause, stop or delete cannot
/// interleave: it needs the same counter row, so it commits strictly before or after the caller.
/// </summary>
public interface ISessionSequencer
{
    Task<long?> AllocateAsync(DbTransaction transaction, Guid userId, Guid sessionId, CancellationToken ct = default);
}

/// <summary>Notified after commit. Implementations must not throw for ordinary failures; exceptions are swallowed and counted.</summary>
public interface ISessionLifecycleObserver
{
    Task OnStateChangedAsync(SessionStateChange change, CancellationToken ct);

    /// <summary>The session and everything that cascades from it is gone (explicit delete). Close live connections.</summary>
    Task OnDeletedAsync(Guid userId, Guid sessionId, CancellationToken ct);
}

/// <summary>Liveness signal (hub Ping, audio frame) used by the idle auto-pause job (C-17).</summary>
public interface ISessionActivity
{
    void Touch(Guid sessionId);
}

/// <summary>
/// Content-free audit of student actions that other modules own (C-30): user id, session id, a FIXED action and the time, in the
/// append-only <c>lectures.audit_events</c> table, which survives deletion of the session. There is no free-text parameter on purpose.
/// The caller must run in a user scope for <paramref name="userId"/>; a mismatch writes nothing and returns false.
/// </summary>
public interface ISessionAuditLog
{
    /// <summary>
    /// PO-18d: the student pressed "Make notes" on a stopped session (the only thing that may send its transcript to an AI after Stop).
    /// <paramref name="pressId"/> (NAS-08) is stored as the row's subject, so the vendor calls the press caused (ledger rows carry the same id)
    /// can be linked to it. <paramref name="noticeVersion"/> (NAS-05) is the id of the DRAFT notice the student saw, never its text; it must be
    /// 1 to 64 of <c>[A-Za-z0-9._-]</c> (else <see cref="ArgumentException"/>, nothing written).
    /// </summary>
    Task<bool> RecordNotesRequestedAsync(Guid userId, Guid sessionId, Guid pressId, string noticeVersion, CancellationToken ct = default);
}

/// <summary>True while the transcript source is a deterministic demo/replay provider, never a real recording.</summary>
public interface IDemoSourceIndicator
{
    bool IsDemoSource { get; }
}

/// <summary>
/// The facts the Docs export needs about a lecture, and nothing else. <c>SessionInfo</c> is deliberately NOT changed: it is read by the AI module,
/// which must never see a title (DXD-17). <see cref="TimeZone"/> is null because the session does not store a zone today; the Docs title then
/// carries the UTC date labelled UTC.
/// </summary>
public sealed record DocsSessionFacts(Guid SessionId, LectureState State, DateTimeOffset StartedAt, string? Title, string? TimeZone);

/// <summary>
/// Docs-only port (docs/architecture/docs-export-demo-adr.md section 4). Both methods act for the AMBIENT user and have no <c>userId</c>
/// parameter: another user's session and a missing one are both null. <see cref="LockAndReadAsync"/> is the FIRST lock of every Docs writer:
/// it takes the session row <c>FOR KEY SHARE</c> on the caller's transaction (lock order, L-LOCKORDER); null means the session is gone.
/// </summary>
public interface IDocsSessionSource
{
    /// <summary>No lock; for preview and state reads.</summary>
    Task<DocsSessionFacts?> ReadAsync(Guid sessionId, CancellationToken ct = default);

    /// <summary>Locks the session row <c>FOR KEY SHARE</c> inside <paramref name="transaction"/> and returns its facts, or null if it is gone.</summary>
    Task<DocsSessionFacts?> LockAndReadAsync(DbTransaction transaction, Guid sessionId, CancellationToken ct = default);
}
