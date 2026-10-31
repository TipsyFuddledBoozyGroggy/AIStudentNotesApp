using Tether.SharedKernel;

namespace Tether.Ai.Domain;

/// <summary>
/// One DERIVED rolling-summary revision (invariant 5: never stored with the source transcript). Append-only per session; a
/// session delete removes every row by FK cascade. <c>SourceFromSeq..SourceToSeq</c> is the range of transcript events this
/// revision was computed from, so it can be re-derived or audited. Topics, citations and terms are JSON because they are read and
/// written as a unit and never queried by field.
/// </summary>
internal sealed class SummaryRevisionRow : IUserOwned
{
    public Guid SessionId { get; set; }
    public int Revision { get; set; }
    public Guid UserId { get; set; }
    public string Kind { get; set; } = "Delta";       // Delta | Compaction
    public string Status { get; set; } = "Draft";     // Draft | Final
    public long BasedOnSeq { get; set; }
    public long SourceFromSeq { get; set; }
    public long SourceToSeq { get; set; }
    public long CoveredFromMs { get; set; }
    public long CoveredToMs { get; set; }
    public string Tldr { get; set; } = "";
    public string TopicsJson { get; set; } = "[]";
    public string TermsJson { get; set; } = "[]";
    public string CitationsJson { get; set; } = "[]";
    public string Model { get; set; } = "";
    public string PromptVersion { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// A stored Quick recap result: the wire object (<c>catchup</c> schema) as JSON, starting extractive and replaced by the AI
/// version when it arrives (never the reverse). Derived data, deleted with the session.
/// </summary>
internal sealed class CatchUpRow : IUserOwned
{
    public Guid Id { get; set; }
    public Guid SessionId { get; set; }
    public Guid UserId { get; set; }
    public int WindowMinutes { get; set; }
    public string Kind { get; set; } = "Extractive";  // Extractive | Ai
    public string AiState { get; set; } = "Pending";  // Pending | Ready | Unavailable | BudgetLimited | NotNeeded
    public long FromMs { get; set; }
    public long ToMs { get; set; }
    public string ResultJson { get; set; } = "{}";
    public string? IdempotencyKey { get; set; }
    public string? Model { get; set; }
    public string? PromptVersion { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// PO-18d: ONE deliberate "Make notes" press on a stopped lecture, and what it produced. It is both the durable request record (the
/// only thing the gateway accepts as permission to send a stopped session's transcript to a provider, while it is <c>Pending</c>) and
/// the derived notes (invariant 5: stored apart from the transcript, with the seq range and model that produced them). Deleted with
/// the session (composite FK cascade). One row per attempt; a new attempt exists only after an Unavailable or BudgetLimited one.
/// The press itself is also audited, content-free, in <c>lectures.audit_events</c>, which outlives the session.
/// </summary>
internal sealed class LectureNotesRow : IUserOwned
{
    public Guid Id { get; set; }
    public Guid SessionId { get; set; }
    public Guid UserId { get; set; }
    public int Revision { get; set; }
    public string State { get; set; } = NotesStates.Pending;
    public string IdempotencyKey { get; set; } = "";
    public DateTimeOffset RequestedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string SectionsJson { get; set; } = "[]";
    public long? CoveredFromMs { get; set; }
    public long? CoveredToMs { get; set; }
    public long? SourceFromSeq { get; set; }
    public long? SourceToSeq { get; set; }
    public bool Truncated { get; set; }
    public int? FinalSummaryRevision { get; set; }
    public string? Model { get; set; }
    public string? PromptVersion { get; set; }
}

/// <summary>Fixed vocabulary for <see cref="LectureNotesRow.State"/> (also a database CHECK and the wire <c>status</c>).</summary>
internal static class NotesStates
{
    public const string Pending = "Pending", Ready = "Ready", Unavailable = "Unavailable", BudgetLimited = "BudgetLimited", NotNeeded = "NotNeeded";
    public static readonly string[] All = [Pending, Ready, Unavailable, BudgetLimited, NotNeeded];
}

/// <summary>
/// Usage ledger row (design section 10, SEC-037). Content-free by construction: ids, counts, a fixed vocabulary and an estimate.
/// It deliberately has no foreign key to the session, so deleting a lecture does not hand the student their daily budget back;
/// it is removed with the user (cascade).
/// </summary>
internal sealed class AiUsageRow : IUserOwned
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    /// <summary>Legacy lecture key: set for lecture rows (equal to <see cref="ScopeId"/>), null for document rows. Dropped by a later contract migration (A004).</summary>
    public Guid? SessionId { get; set; }
    public string ScopeKind { get; set; } = "lecture";   // lecture | syllabus
    public Guid ScopeId { get; set; }
    public string Pool { get; set; } = "lecture";         // budget pool; equal to ScopeKind today
    public string Workflow { get; set; } = "";
    public string PromptVersion { get; set; } = "";
    public string Model { get; set; } = "";
    public int InputTokens { get; set; }
    public int CachedTokens { get; set; }
    public int OutputTokens { get; set; }
    public long EstCostMicros { get; set; }
    public int LatencyMs { get; set; }
    public string Outcome { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Fixed outcome vocabulary for <see cref="AiUsageRow.Outcome"/> (also a database CHECK).</summary>
internal static class Outcomes
{
    public const string Ok = "ok", Repaired = "repaired", Invalid = "invalid", Degraded = "degraded", Error = "error",
        Timeout = "timeout", RateLimited = "rate_limited", BudgetDenied = "budget_denied", Stale = "stale", Reserved = "reserved";

    public static readonly string[] All = [Ok, Repaired, Invalid, Degraded, Error, Timeout, RateLimited, BudgetDenied, Stale, Reserved];
}
