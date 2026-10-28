using Tether.SharedKernel;

namespace Tether.Transcript.Domain;

internal enum TranscriptEventKind { Final, Correction, Gap }

/// <summary>
/// Source transcript event (persisted: transcript.final, transcript.correction, gap). This is the SOURCE data; summaries and
/// anything derived from it live elsewhere and are never written here (invariant 5). Its seq comes from the per-session
/// counter owned by Lectures and is assigned in the same transaction as this row. Deleted with the session (FK cascade).
/// </summary>
internal sealed class TranscriptEvent : IUserOwned
{
    public Guid SessionId { get; set; }
    public long Seq { get; set; }
    public Guid UserId { get; set; }
    public TranscriptEventKind Kind { get; set; }
    public int? SegmentNo { get; set; }
    public int? Revision { get; set; }
    public int? Supersedes { get; set; }
    public long? StartMs { get; set; }
    public long? EndMs { get; set; }
    public string? Text { get; set; }
    public double? Confidence { get; set; }
    public string? Provider { get; set; }
    public long? FromMs { get; set; }
    public long? ToMs { get; set; }
    public string? GapReason { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}

/// <summary>How much audio has been accepted for a session (excluding paused time), so timecodes continue after a resume.</summary>
internal sealed class AudioProgress : IUserOwned
{
    public Guid SessionId { get; set; }
    public Guid UserId { get; set; }
    public long AcceptedMs { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
