namespace Tether.Transcript.Contracts;

/// <summary>
/// A persisted FINAL transcript segment with the latest correction applied. Partials are never exposed through this contract
/// (ADR-003): only source text that was committed with a gap-free seq can be summarized or cited.
/// </summary>
public sealed record FinalSegment(Guid SessionId, long Seq, int SegmentNo, long StartMs, long EndMs, string Text, double? Confidence)
{
    /// <summary>Server-assigned id, <c>seg_{n}</c>; the only citation key the AI module accepts.</summary>
    public string SegmentId => $"seg_{SegmentNo}";
}

/// <summary>A window read: segments ascending by time; <paramref name="Truncated"/> is true when older ones were left out to respect the limit.</summary>
public sealed record SegmentSlice(IReadOnlyList<FinalSegment> Segments, bool Truncated);

/// <summary>
/// Read access to the SOURCE transcript for other modules. Every call is user-scoped: the caller must run in a user scope for
/// <c>userId</c>, and a session that does not exist for that user yields empty results (indistinguishable from "no segments").
/// </summary>
public interface ITranscriptSegmentReader
{
    /// <summary>Finals with <c>seq &gt; afterSeq</c>, ascending, at most <paramref name="limit"/> (oldest first).</summary>
    Task<IReadOnlyList<FinalSegment>> ReadFinalsAfterAsync(Guid userId, Guid sessionId, long afterSeq, int limit, CancellationToken ct = default);

    /// <summary>The newest <paramref name="limit"/> finals with <c>seq &lt;= upToSeq</c>, returned ascending (continuity overlap).</summary>
    Task<IReadOnlyList<FinalSegment>> ReadFinalsUpToAsync(Guid userId, Guid sessionId, long upToSeq, int limit, CancellationToken ct = default);

    /// <summary>Finals that have content inside the window: <c>EndMs &gt; fromMs &amp;&amp; EndMs &lt;= toMs</c>. The newest <paramref name="limit"/> are kept.</summary>
    Task<SegmentSlice> ReadWindowAsync(Guid userId, Guid sessionId, long fromMs, long toMs, int limit, CancellationToken ct = default);

    /// <summary>End offset (session clock, paused time excluded) of the newest final, or null when there is none.</summary>
    Task<long?> LatestEndMsAsync(Guid userId, Guid sessionId, CancellationToken ct = default);
}

/// <summary>
/// Called after a final segment has been committed. MUST return immediately and MUST NOT await anything slow: ingest and
/// transcription never wait on AI. Implementations only record that work exists.
/// </summary>
public interface ITranscriptFinalObserver
{
    void OnFinalPersisted(Guid userId, Guid sessionId, long seq);
}

/// <summary>
/// Publishes an ephemeral (seq null) event to the hub group of one session. Only <c>summary.revision</c>, <c>summary.status</c>
/// and <c>catchup.ready</c> are accepted; anything else is refused so another module cannot forge transcript or state events.
/// </summary>
public interface ILiveEventPublisher
{
    Task PublishEphemeralAsync(Guid userId, Guid sessionId, string type, object payload, CancellationToken ct = default);
}

/// <summary>The newest summary revision of a session as a ready-to-send <c>summary.revision</c> payload.</summary>
public sealed record SummarySnapshot(int Revision, object Payload);

/// <summary>Lets <c>Join</c> report <c>currentSummaryRevision</c> and replay the latest summary without the hub knowing the AI module.</summary>
public interface ISummaryJoinSource
{
    Task<SummarySnapshot?> LatestAsync(Guid userId, Guid sessionId, CancellationToken ct = default);
}
