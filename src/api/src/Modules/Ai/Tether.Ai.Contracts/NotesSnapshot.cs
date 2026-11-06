namespace Tether.Ai.Contracts;

/// <summary>
/// Read-only, user-bound view of the stored notes of one lecture, for modules that must not touch the AI module's internals (the Docs
/// export). It takes the AMBIENT user (<c>ICurrentUser</c> / <c>UserScope</c>) and has no <c>userId</c> parameter on purpose: an implementation
/// running in the scope of a different user finds nothing and returns null (sibling of M2B3-11). It returns stored notes fields only: no
/// segment ids, no transcript text, no prompt material, no AI client type, no token (invariant 3).
/// </summary>
public interface INotesSnapshotReader
{
    /// <summary>
    /// The session's CURRENT notes press (its newest attempt) with whatever state it is in, or null when the student never pressed or the
    /// session is not the ambient user's. Only a <c>Ready</c> snapshot carries content; every other state has no sections.
    /// </summary>
    Task<NotesSnapshot?> FindCurrentAsync(Guid sessionId, CancellationToken ct = default);
}

/// <param name="NotesId">The wire id of the press (<c>nt_</c> and 32 hex digits), as on <c>GET /sessions/{id}/notes</c>.</param>
/// <param name="Status">Pending, Ready, Unavailable, BudgetLimited or NotNeeded.</param>
/// <param name="Source"><c>mock</c> or <c>model</c>; only when Ready.</param>
/// <param name="Truncated">The lecture ran longer than the notes cover (only when Ready).</param>
/// <param name="CoveredToMs">Where the notes stop (only when Ready).</param>
/// <param name="FinalSummary">The Final summary revision this press made, only when its step succeeded.</param>
public sealed record NotesSnapshot(
    Guid SessionId, string NotesId, int Revision, string Status, string? Source, bool Truncated, long? CoveredToMs,
    IReadOnlyList<NotesSnapshotSection> Sections, FinalSummarySnapshot? FinalSummary);

public sealed record NotesSnapshotSection(string Heading, IReadOnlyList<NotesSnapshotBullet> Bullets);

/// <param name="CitationStartMs">Lecture times only. Never a segment id, never transcript text.</param>
public sealed record NotesSnapshotBullet(string Text, IReadOnlyList<long> CitationStartMs);

public sealed record FinalSummarySnapshot(string Tldr, IReadOnlyList<string> Topics, IReadOnlyList<long> CitationStartMs);
