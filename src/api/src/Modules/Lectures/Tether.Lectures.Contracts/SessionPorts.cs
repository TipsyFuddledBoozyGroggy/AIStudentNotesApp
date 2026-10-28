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

/// <summary>True while the transcript source is a deterministic demo/replay provider, never a real recording.</summary>
public interface IDemoSourceIndicator
{
    bool IsDemoSource { get; }
}
