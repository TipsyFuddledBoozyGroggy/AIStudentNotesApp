using Tether.SharedKernel;

namespace Tether.Lectures.Domain;

internal enum SessionState { Active, Paused, Stopped, Failed }

/// <summary>Consent event kinds, one row per lifecycle transition (SEC-007).</summary>
internal enum ConsentEventKind { Start, Pause, Resume, Stop }

internal sealed class LectureSession : IUserOwned
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    /// <summary>Course in the Courses module's schema. No DB FK across schemas (ADR-014 allow-list); ownership is checked via ICourseOwnership.</summary>
    public Guid CourseId { get; set; }
    public string? Title { get; set; }
    public SessionState State { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? StoppedAt { get; set; }
    public Guid ConsentRecordId { get; set; }
    /// <summary>True if the transcript source was the demo/replay provider when the session was created (B2-09). Never recomputed.</summary>
    public bool DemoSource { get; set; }
    /// <summary>PostgreSQL xmin; optimistic concurrency token and ETag source.</summary>
    public uint Version { get; set; }
}

/// <summary>
/// Append-only consent record (ADR-015: Lectures owns it). One per session start, carrying the per-course attestation
/// (SEC-008). Content free, so it is kept after the session is deleted; <see cref="SessionId"/> is deliberately not a FK.
/// </summary>
internal sealed class ConsentRecord : IUserOwned
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid CourseId { get; set; }
    public Guid SessionId { get; set; }
    public string Kind { get; set; } = ConsentKinds.MicRecording;
    public string TextVersion { get; set; } = "";
    /// <summary>The student attested, for this course, that recording is permitted (SEC-008).</summary>
    public bool CourseAttested { get; set; }
    public DateTimeOffset AcknowledgedAt { get; set; }
}

internal static class ConsentKinds
{
    public const string MicRecording = "mic_recording";
}

/// <summary>Append-only event per start, pause, resume and stop (SEC-007). Content free; kept after session deletion.</summary>
internal sealed class ConsentEvent : IUserOwned
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid ConsentRecordId { get; set; }
    public Guid SessionId { get; set; }
    public ConsentEventKind Kind { get; set; }
    public string TextVersion { get; set; } = "";
    public DateTimeOffset OccurredAt { get; set; }
}

/// <summary>Persisted <c>session.state</c> event with its gap-free per-session seq. Deleted with the session.</summary>
internal sealed class SessionEvent : IUserOwned
{
    public Guid SessionId { get; set; }
    public long Seq { get; set; }
    public Guid UserId { get; set; }
    public SessionState State { get; set; }
    public string? Reason { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}

/// <summary>
/// Per-session event counter row (C-10). Updated in the same transaction as the event it numbers; the value is a
/// concurrency token so two writers can never hand out the same seq.
/// </summary>
internal sealed class SessionCounter : IUserOwned
{
    public Guid SessionId { get; set; }
    public Guid UserId { get; set; }
    public long LastSeq { get; set; }
}

/// <summary>Hashed single-use audio ticket (C-01). At most one row per session; deleting the row invalidates it.</summary>
internal sealed class AudioTicket : IUserOwned
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid SessionId { get; set; }
    /// <summary>The login session (identity.sessions) the ticket was issued to; logout therefore invalidates it.</summary>
    public Guid AuthSessionId { get; set; }
    public byte[] TokenHash { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}

internal sealed class IdempotencyRecord : IUserOwned
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public byte[] KeyHash { get; set; } = [];
    public byte[] Fingerprint { get; set; } = [];
    public Guid SessionId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>User id, session id, action and time only (C-30). Never content, tickets or free text.</summary>
internal sealed class AuditEvent : IUserOwned
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid? SessionId { get; set; }
    public string Action { get; set; } = "";
    public DateTimeOffset OccurredAt { get; set; }
    /// <summary>M008, NAS-08: the id of the thing the action names, when it has one (for <c>notes.requested</c>: the press, which is also on its ledger rows). Content-free.</summary>
    public Guid? SubjectId { get; set; }
    /// <summary>M008, NAS-05: the id of the DRAFT notice the student saw for this action (<c>notes.requested</c> only today). Never notice text.</summary>
    public string? NoticeVersion { get; set; }
}

internal static class AuditActions
{
    public const string SessionStart = "session.start";
    public const string SessionPause = "session.pause";
    public const string SessionResume = "session.resume";
    public const string SessionStop = "session.stop";
    public const string SessionDelete = "session.delete";
    public const string TicketIssued = "audio_ticket.issued";
    public const string TicketRejected = "audio_ticket.rejected";
    /// <summary>PO-18d: a deliberate "Make notes" press after Stop. Written through <see cref="Tether.Lectures.Contracts.ISessionAuditLog"/> by the AI module.</summary>
    public const string NotesRequested = "notes.requested";
}
