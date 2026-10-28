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
