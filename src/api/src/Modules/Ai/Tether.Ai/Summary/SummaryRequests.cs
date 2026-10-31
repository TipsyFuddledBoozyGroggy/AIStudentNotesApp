using Tether.Ai.Prompts;
using Tether.Ai.Provider;
using Tether.Ai.Validation;
using Tether.Transcript.Contracts;

namespace Tether.Ai.Summary;

/// <summary>Builds bounded, provider-neutral requests for the rolling summary. Pure, so the eval harness and the engine share it.</summary>
internal static class SummaryRequests
{
    public sealed record Built(AiRequest Request, IReadOnlyDictionary<string, SourceLine> Allowed, bool TruncatedOldest);

    public static SourceLine ToLine(FinalSegment s) => new(s.SegmentId, s.StartMs, s.Text);

    /// <summary>Newest-first selection within a character budget; returns ascending. Older lines are the ones dropped (design section 3 step 5).</summary>
    public static (List<FinalSegment> Kept, bool Truncated) TakeNewestWithin(IReadOnlyList<FinalSegment> segments, int maxChars)
    {
        var kept = new List<FinalSegment>();
        var used = 0;
        for (var i = segments.Count - 1; i >= 0; i--)
        {
            var len = Math.Min(segments[i].Text.Length, PromptRenderer.MaxLineChars) + 16;
            if (used + len > maxChars && kept.Count > 0) break;
            used += len;
            kept.Add(segments[i]);
        }
        kept.Reverse();
        return (kept, kept.Count < segments.Count);
    }

    /// <summary>The rolling summary re-presented to the model as data: newest topics first within the character budget.</summary>
    public static List<SourceLine> TopicLines(IReadOnlyList<TopicState> topics, int maxChars)
    {
        var lines = new List<SourceLine>();
        var used = 0;
        for (var i = topics.Count - 1; i >= 0; i--)
        {
            var len = topics[i].Key.Length + topics[i].Text.Length + 8;
            if (used + len > maxChars && lines.Count > 0) break;
            used += len;
            lines.Add(new SourceLine(topics[i].Key, 0, topics[i].Text));
        }
        lines.Reverse();
        return lines;
    }

    public static Built BuildDelta(Guid userId, Guid sessionId, int baseRevision, IReadOnlyList<TopicState> existing, IReadOnlyList<FinalSegment> recent,
        IReadOnlyList<FinalSegment> fresh, SummaryOptions o, TimeSpan timeout, bool compaction = false, AiWorkflow? workflow = null, Guid? pressId = null)
    {
        var (kept, truncated) = TakeNewestWithin(fresh, compaction ? o.CompactionChunkChars : o.MaxNewChars);
        var (recentKept, _) = TakeNewestWithin(recent, o.RecentWindowChars);
        var blocks = new List<UntrustedBlock>
        {
            new(BlockKinds.RollingSummary, TopicLines(existing, o.RollingSummaryChars)),
            new(BlockKinds.RecentWindow, recentKept.Select(ToLine).ToList()),
            new(compaction ? BlockKinds.SourceChunk : BlockKinds.NewSegments, kept.Select(ToLine).ToList()),
        };
        var allowed = new Dictionary<string, SourceLine>(StringComparer.Ordinal);
        foreach (var s in recentKept.Concat(kept)) allowed[s.SegmentId] = ToLine(s);
        var wf = workflow ?? (compaction ? AiWorkflow.Compaction : AiWorkflow.RollingSummary);
        var tag = wf switch { AiWorkflow.FinalSummary => "f", AiWorkflow.Compaction => "c", _ => "d" };
        var req = new AiRequest(wf, PromptLibrary.RollingSummaryVersion, AiTier.Fast, compaction ? PromptLibrary.Compaction : PromptLibrary.RollingSummary,
            $"baseRevision={baseRevision}", blocks, o.MaxOutputTokens, userId, sessionId, $"sum:{sessionId:N}:{tag}:{baseRevision}", timeout, PressId: pressId);
        return new Built(req, allowed, truncated);
    }
}
