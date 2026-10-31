using Tether.Ai.Provider;
using Tether.Ai.Validation;

namespace Tether.Ai.Summary;

/// <summary>Applies a validated delta to the previous topic list (design section 3 step 3). Pure.</summary>
internal static class SummaryMerger
{
    public const int MaxTopics = 20;

    /// <summary>Upsert by topic key (case-insensitive) keeping position; new topics append; the oldest are dropped beyond the cap.</summary>
    public static IReadOnlyList<TopicState> Merge(IReadOnlyList<TopicState> existing, SummaryDelta delta, int maxTopics = MaxTopics)
    {
        var list = existing.ToList();
        foreach (var t in delta.Topics)
        {
            var i = list.FindIndex(x => string.Equals(x.Key, t.Key, StringComparison.OrdinalIgnoreCase));
            if (i >= 0) list[i] = t; else list.Add(t);
        }
        if (list.Count > maxTopics) list.RemoveRange(0, list.Count - maxTopics);
        return list;
    }

    /// <summary>Distinct citations in topic order; when more than <paramref name="max"/>, the newest are kept.</summary>
    public static IReadOnlyList<CitationRef> Citations(IReadOnlyList<TopicState> topics, int max = 50)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var all = new List<CitationRef>();
        foreach (var c in topics.SelectMany(t => t.Citations)) if (seen.Add(c.SegmentId)) all.Add(c);
        return all.Count > max ? all.GetRange(all.Count - max, max) : all;
    }
}

/// <summary>When a session's pending segments should be flushed to a summary update (design section 3 step 1). Pure.</summary>
internal static class FlushPolicy
{
    public static bool IsDue(int pending, DateTimeOffset? firstPendingAt, DateTimeOffset lastRunStart, DateTimeOffset now, SummaryOptions o, double intervalMultiplier = 1)
    {
        if (pending <= 0) return false;
        var enough = pending >= o.FlushSegments || (firstPendingAt is { } f && now - f >= o.FlushAfter);
        var spaced = now - lastRunStart >= TimeSpan.FromTicks((long)(o.MinInterval.Ticks * intervalMultiplier));
        return enough && spaced;
    }
}

/// <summary>
/// Groups finals, in order, into chunks of at most <c>chunkChars</c> prompt characters and keeps only the NEWEST <c>maxChunks</c> of
/// them in memory (compaction and the final summary re-derive from source in bounded calls). Pure.
/// </summary>
internal sealed class NewestChunks(int chunkChars, int maxChunks)
{
    public sealed record Result(IReadOnlyList<IReadOnlyList<Tether.Transcript.Contracts.FinalSegment>> Kept, bool Dropped);

    private readonly List<List<Tether.Transcript.Contracts.FinalSegment>> _chunks = [];
    private List<Tether.Transcript.Contracts.FinalSegment> _current = [];
    private int _used;
    private bool _dropped;

    public static int Cost(Tether.Transcript.Contracts.FinalSegment s) => Math.Min(s.Text.Length, Prompts.PromptRenderer.MaxLineChars) + 16;

    public void Add(Tether.Transcript.Contracts.FinalSegment s)
    {
        var len = Cost(s);
        if (_used + len > chunkChars && _current.Count > 0) Push();
        _current.Add(s);
        _used += len;
    }

    private void Push()
    {
        _chunks.Add(_current);
        _current = [];
        _used = 0;
        if (_chunks.Count > Math.Max(1, maxChunks)) { _chunks.RemoveAt(0); _dropped = true; }
    }

    public Result Complete()
    {
        if (_current.Count > 0) Push();
        return new Result(_chunks.Select(c => (IReadOnlyList<Tether.Transcript.Contracts.FinalSegment>)c).ToList(), _dropped);
    }
}
