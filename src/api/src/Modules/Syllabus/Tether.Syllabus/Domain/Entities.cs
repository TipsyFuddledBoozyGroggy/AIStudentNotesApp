using Tether.SharedKernel;

namespace Tether.Syllabus.Domain;

// Every persisted class here is user owned (IUserOwned): the ownership interceptor and the per-entity query filter apply.
// docs/data/schema-syllabus-p2.md is the authority for the columns; the DDL lives in the S001/S002 migrations (raw SQL).
// String-typed enums are checked by database CHECK constraints, not by PostgreSQL enums.

internal sealed class SyllabusDoc : IUserOwned
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid CourseId { get; set; }
    public string? Title { get; set; }
    public string InputKind { get; set; } = "";
    public string? FileType { get; set; }
    public string Status { get; set; } = "";
    public string? StatusReason { get; set; }
    public string AiSource { get; set; } = "mock";
    public bool DemoSource { get; set; } = true;
    public bool OriginalRetained { get; set; }
    public string ContentSha256 { get; set; } = "";
    public short? PageCount { get; set; }
    public int CharCount { get; set; }
    public short[] UnreadablePages { get; set; } = [];
    public int HiddenContentRemoved { get; set; }
    public bool HiddenContentDropped { get; set; }
    public bool InjectionSuspect { get; set; }
    public bool BatchApproveAllowed { get; set; } = true;
    public string NoticeVersion { get; set; } = "";
    public bool NoticeAttested { get; set; } = true;
    public DateTimeOffset NoticeAttestedAt { get; set; }
    public string UnprocessedRanges { get; set; } = "[]";
    public int DroppedInvalid { get; set; }
    public DateOnly? TermStart { get; set; }
    public DateOnly? TermEnd { get; set; }
    public string DefaultTimezone { get; set; } = "";
    public string? ExtractorVersion { get; set; }
    public string? PromptVersion { get; set; }
    public string? SchemaVersion { get; set; }
    public string? Model { get; set; }
    public int RowVersion { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset LastActivityAt { get; set; }
    public DateTimeOffset? CanonicalTextPurgeAt { get; set; }
    public DateTimeOffset? CanonicalTextPurgedAt { get; set; }
    public DateTimeOffset? PurgeAfter { get; set; }
}

internal sealed class CanonicalText : IUserOwned
{
    public Guid SyllabusId { get; set; }
    public Guid UserId { get; set; }
    public string Text { get; set; } = "";
    public int Utf16Length { get; set; }
    public string Lines { get; set; } = "[]";
    public string Pages { get; set; } = "[]";
    public string ExtractorVersion { get; set; } = "";
    public string TextSha256 { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}

internal sealed class ExtractionJob : IUserOwned
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid SyllabusId { get; set; }
    public string State { get; set; } = "queued";
    public short Attempt { get; set; }
    public DateTimeOffset AvailableAt { get; set; }
    public string? LeaseOwner { get; set; }
    public DateTimeOffset? LeaseExpiresAt { get; set; }
    public DateTimeOffset? HeartbeatAt { get; set; }
    public string? Stage { get; set; }
    public short? ChunksTotal { get; set; }
    public short? ChunksDone { get; set; }
    public long JobCapMicros { get; set; }
    public string? LastErrorCode { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
}

internal sealed class SyllabusItem : IUserOwned
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid SyllabusId { get; set; }
    public Guid CourseId { get; set; }
    public string ItemKey { get; set; } = "";
    public string Origin { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Notes { get; set; }
    public string? WrittenDateText { get; set; }
    public string? WrittenTimeText { get; set; }
    public short? WrittenYear { get; set; }
    public short? WrittenMonth { get; set; }
    public short? WrittenDay { get; set; }
    public string? WrittenWeekday { get; set; }
    public string WhenStatus { get; set; } = "";
    public DateOnly? DateLocal { get; set; }
    public DateOnly? EndDateLocal { get; set; }
    public TimeOnly? TimeLocal { get; set; }
    public TimeOnly? EndTimeLocal { get; set; }
    public bool AllDay { get; set; }
    public string? Timezone { get; set; }
    public string? TimezoneSource { get; set; }
    public string? Derivation { get; set; }
    public string? UnknownReason { get; set; }
    public string[]? RecByDay { get; set; }
    public TimeOnly? RecStartTime { get; set; }
    public TimeOnly? RecEndTime { get; set; }
    public DateOnly? RecFrom { get; set; }
    public DateOnly? RecUntil { get; set; }
    public string? RecUntilSource { get; set; }
    public DateOnly[] RecExceptions { get; set; } = [];
    public string Confidence { get; set; } = "";
    public string ConfidenceReason { get; set; } = "";
    public string[] Flags { get; set; } = [];
    public int? SnippetStart { get; set; }
    public int? SnippetEnd { get; set; }
    public short? SnippetPage { get; set; }
    public string? SnippetPrecision { get; set; }
    public string? SnippetText { get; set; }
    public string? ContextText { get; set; }
    public int? ContextStart { get; set; }
    public int? ContextEnd { get; set; }
    public bool Edited { get; set; }
    public string Decision { get; set; } = "undecided";
    public string SyncState { get; set; } = "not_synced";
    public int? SyncedRevision { get; set; }
    public int Revision { get; set; } = 1;
    public int RowVersion { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateOnly SortDate { get; set; }
}

internal sealed class ItemProposal : IUserOwned
{
    public Guid ItemId { get; set; }
    public Guid UserId { get; set; }
    public Guid SyllabusId { get; set; }
    public string Proposal { get; set; } = "{}";
    public string ValidatorVersion { get; set; } = "";
    public string PromptVersion { get; set; } = "";
    public string SchemaVersion { get; set; } = "";
    public string Model { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}

internal sealed class ItemAlternative : IUserOwned
{
    public Guid ItemId { get; set; }
    public short Ordinal { get; set; }
    public Guid UserId { get; set; }
    public DateOnly DateLocal { get; set; }
    public TimeOnly? TimeLocal { get; set; }
}

internal sealed class UnclaimedDate : IUserOwned
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid SyllabusId { get; set; }
    public int SnippetStart { get; set; }
    public int SnippetEnd { get; set; }
    public short? SnippetPage { get; set; }
    public string SnippetPrecision { get; set; } = "exact";
    public string? SnippetText { get; set; }
    public string? ContextText { get; set; }
    public int? ContextStart { get; set; }
    public int? ContextEnd { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

internal sealed class SyncRun : IUserOwned
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid SyllabusId { get; set; }
    public string Provider { get; set; } = "mock";
    public string Kind { get; set; } = "sync";
    public string Status { get; set; } = "";
    public int Requested { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

internal sealed class SyncRunItem : IUserOwned
{
    public Guid RunId { get; set; }
    public Guid ItemId { get; set; }
    public Guid UserId { get; set; }
    public string Outcome { get; set; } = "";
    public string? ErrorCode { get; set; }
    public int Occurrences { get; set; }
}

internal sealed class IdempotencyRecord : IUserOwned
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid? CourseId { get; set; }
    public Guid? SyllabusId { get; set; }
    public string Method { get; set; } = "POST";
    public string RouteTemplate { get; set; } = "";
    public byte[] KeyHash { get; set; } = [];
    public byte[] Fingerprint { get; set; } = [];
    public string ResultKind { get; set; } = "";
    public Guid? ResultId { get; set; }
    public DateTimeOffset? TombstonedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}

internal sealed class CalendarEvent : IUserOwned
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid CourseId { get; set; }
    public Guid SyllabusId { get; set; }
    public Guid ItemId { get; set; }
    public string Provider { get; set; } = "mock";
    public string ItemKey { get; set; } = "";
    public DateOnly? OccurrenceDate { get; set; }
    public string Title { get; set; } = "";
    public string Kind { get; set; } = "";
    public bool AllDay { get; set; }
    public DateOnly StartDate { get; set; }
    public TimeOnly? StartTime { get; set; }
    public string StartTimezone { get; set; } = "";
    public DateOnly EndDate { get; set; }
    public TimeOnly? EndTime { get; set; }
    public bool EndDerived { get; set; }
    public int SyncedRevision { get; set; }
    public DateTimeOffset SyncedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Append-only, content-free (no titles, snippets or dates). No FK, so it survives deletes (docs/data section 10).</summary>
internal sealed class DecisionAudit : IUserOwned
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid SyllabusId { get; set; }
    public Guid? ItemId { get; set; }
    public int? ItemRevision { get; set; }
    public byte[]? ContentHmac { get; set; }
    public string? NoticeVersion { get; set; }
    public int? ItemCount { get; set; }
    public string Action { get; set; } = "";
    public DateTimeOffset OccurredAt { get; set; }
}

/// <summary>Durable per-user daily creation counter (N-08). Content-free, never decremented, no FK.</summary>
internal sealed class CreationCounter : IUserOwned
{
    public Guid UserId { get; set; }
    public DateOnly Day { get; set; }
    public short Created { get; set; }
}
