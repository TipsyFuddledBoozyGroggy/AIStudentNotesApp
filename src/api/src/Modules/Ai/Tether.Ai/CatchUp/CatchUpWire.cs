using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Tether.Ai.Prompts;
using Tether.Ai.Provider;
using Tether.Ai.Summary;
using Tether.Ai.Validation;
using Tether.Transcript.Contracts;

namespace Tether.Ai.CatchUp;

// Wire shapes: must keep validating against src/shared/contracts/lecture.v1/catchup.schema.json (additionalProperties:false).
internal sealed record ClaimWire(string Text, string Basis, IReadOnlyList<CitationRef> Citations);
internal sealed record DetailWire(string Text, string Basis, IReadOnlyList<CitationRef> Citations, string? WhyItMatters);
internal sealed record TermWire(string Term, string? Definition, string Basis, IReadOnlyList<CitationRef> Citations);

internal sealed record CatchUpWire(
    string CatchUpId, int WindowMinutes, string Text, IReadOnlyList<CitationRef> Citations, string Kind, string AiState, long CoveredFromMs, long CoveredToMs,
    ClaimWire? NowDiscussing = null, IReadOnlyList<ClaimWire>? ShortCatchUp = null, IReadOnlyList<DetailWire>? Detail = null, IReadOnlyList<TermWire>? KeyTerms = null,
    IReadOnlyList<string>? ClarifyingQuestions = null, string? Confidence = null, IReadOnlyList<string>? Gaps = null, string? Source = null);

internal static class CatchUpJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
    public static string Write(CatchUpWire w) => JsonSerializer.Serialize(w, Options);

    public const string GapDowngraded = "Some statements could not be matched to the transcript and are marked uncertain.";
    public const string GapTruncated = "The start of this window was too long to include in full.";
    public const string NothingYet = "No transcript text in this window yet.";
}

/// <summary>Builds the stored/wire catch-up objects. The extractive form is the always-available fallback (design section 5).</summary>
internal static class CatchUpBuilder
{
    public const int MaxTextChars = 4000, MaxCitations = 50;

    public static string PublicId(Guid id) => "cu_" + id.ToString("N");

    public static bool TryParseId(string? publicId, out Guid id)
    {
        id = default;
        return publicId is { Length: 35 } && publicId.StartsWith("cu_", StringComparison.Ordinal) && Guid.TryParseExact(publicId.AsSpan(3), "N", out id);
    }

    /// <summary>Raw excerpt of the window, newest lines kept when it is too long. Nothing is inferred or rewritten.</summary>
    public static CatchUpWire Extractive(Guid id, int windowMinutes, long fromMs, long toMs, IReadOnlyList<FinalSegment> segments, string aiState)
    {
        if (segments.Count == 0)
            return new CatchUpWire(PublicId(id), windowMinutes, CatchUpJson.NothingYet, [], "Extractive", aiState, fromMs, toMs);
        var lines = segments.Select(s => $"[{PromptRenderer.Clock(s.StartMs)}] {s.Text}").ToList();
        var kept = new List<string>();
        var used = 0;
        for (var i = lines.Count - 1; i >= 0; i--)
        {
            if (used + lines[i].Length + 1 > MaxTextChars && kept.Count > 0) break;
            used += lines[i].Length + 1;
            kept.Add(lines[i]);
        }
        kept.Reverse();
        var shown = segments.Skip(segments.Count - kept.Count).ToList();
        var cites = shown.Skip(Math.Max(0, shown.Count - MaxCitations)).Select(s => new CitationRef(s.SegmentId, s.StartMs)).ToList();
        var text = string.Join('\n', kept);
        if (text.Length > MaxTextChars) text = text[..MaxTextChars];
        return new CatchUpWire(PublicId(id), windowMinutes, text, cites, "Extractive", aiState, fromMs, toMs);
    }

    public static CatchUpWire FromAi(Guid id, int windowMinutes, long fromMs, long toMs, CatchUpResult r, bool windowTruncated, string model)
    {
        ClaimWire C(Claim c) => new(c.Text, c.Basis, c.Citations);
        var sb = new StringBuilder(r.NowDiscussing.Text);
        foreach (var b in r.Short) sb.Append("\n- ").Append(b.Text);
        var text = sb.Length > MaxTextChars ? sb.ToString(0, MaxTextChars) : sb.ToString();

        var all = new[] { r.NowDiscussing }.Concat(r.Short).Concat(r.Detail.Select(d => d.Claim)).SelectMany(c => c.Citations)
            .Concat(r.KeyTerms.SelectMany(k => k.Citations));
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var cites = all.Where(c => seen.Add(c.SegmentId)).OrderBy(c => c.StartMs).Take(MaxCitations).ToList();

        var gaps = r.Gaps.ToList();
        if (r.Downgraded > 0) gaps.Add(CatchUpJson.GapDowngraded);
        if (windowTruncated) gaps.Add(CatchUpJson.GapTruncated);
        return new CatchUpWire(PublicId(id), windowMinutes, text, cites, "Ai", "Ready", fromMs, toMs,
            C(r.NowDiscussing), r.Short.Select(C).ToList(), r.Detail.Select(d => new DetailWire(d.Claim.Text, d.Claim.Basis, d.Claim.Citations, d.WhyItMatters)).ToList(),
            r.KeyTerms.Select(k => new TermWire(k.Term, k.Definition, k.Basis, k.Citations)).ToList(), r.Questions, r.Confidence, gaps.Take(CatchUpValidator.MaxGaps).ToList(), AiSource.Of(model));
    }
}

/// <summary>Pure request construction for I Lost Track, shared by the runner and the eval harness.</summary>
internal static class CatchUpRequests
{
    public sealed record Built(AiRequest Request, CatchUpContext Context, bool Truncated);

    public static Built Build(Guid userId, Guid sessionId, Guid catchUpId, int windowMinutes, IReadOnlyList<FinalSegment> windowSegments, CatchUpOptions o)
    {
        var (kept, truncated) = SummaryRequests.TakeNewestWithin(windowSegments, o.WindowMaxChars);
        var allowed = kept.ToDictionary(s => s.SegmentId, SummaryRequests.ToLine, StringComparer.Ordinal);
        var req = new AiRequest(AiWorkflow.LostTrack, PromptLibrary.LostTrackVersion, AiTier.Balanced, PromptLibrary.LostTrack, $"windowMinutes={windowMinutes}",
            [new UntrustedBlock(BlockKinds.WindowSegments, kept.Select(SummaryRequests.ToLine).ToList())], o.MaxOutputTokens, userId, sessionId,
            $"lt:{catchUpId:N}", o.Timeout);
        return new Built(req, new CatchUpContext(allowed, kept.Count > 0), truncated);
    }
}
