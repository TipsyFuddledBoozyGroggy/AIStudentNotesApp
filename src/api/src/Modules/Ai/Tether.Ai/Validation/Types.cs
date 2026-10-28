using Tether.Ai.Provider;

namespace Tether.Ai.Validation;

/// <summary>Fixed vocabulary for why a model output was rejected. Never includes model or transcript text (SEC-027).</summary>
internal static class ValidationCodes
{
    public const string NotJson = "not_json";
    public const string SchemaInvalid = "schema_invalid";
    public const string Overlong = "overlong";
    public const string Ungrounded = "ungrounded";
    public const string StaleBase = "stale_base";
    public const string Truncated = "truncated_output";
}

internal sealed record Validated<T>(T? Value, string? Error) where T : class
{
    public bool Ok => Error is null;
    public static Validated<T> Success(T value) => new(value, null);
    public static Validated<T> Fail(string code) => new(null, code);
}

internal static class Basis
{
    public const string Lecture = "lecture";
    public const string GeneralKnowledge = "general_knowledge";
    public const string Uncertain = "uncertain";
    public static bool IsValid(string? b) => b is Lecture or GeneralKnowledge or Uncertain;
}

/// <summary>A citation whose timecode was taken from the stored segment, never from the model.</summary>
internal sealed record CitationRef(string SegmentId, long StartMs);

// ---------------- rolling summary ----------------

/// <summary>What the validator may accept for one call: the revision the model was told to build on, and the only citable segments.</summary>
internal sealed record SummaryContext(int BaseRevision, IReadOnlyDictionary<string, SourceLine> Allowed);

internal sealed record TopicState(string Key, string Text, IReadOnlyList<CitationRef> Citations);

internal sealed record SummaryDelta(string Tldr, IReadOnlyList<TopicState> Topics, IReadOnlyList<string> NewTerms, int DroppedCitations, int DroppedTopics, bool Sanitized);

// ---------------- I Lost Track ----------------

internal sealed record CatchUpContext(IReadOnlyDictionary<string, SourceLine> Allowed, bool WindowHasSegments);

internal sealed record Claim(string Text, string Basis, IReadOnlyList<CitationRef> Citations);

internal sealed record DetailItem(Claim Claim, string? WhyItMatters);

internal sealed record KeyTerm(string Term, string? Definition, string Basis, IReadOnlyList<CitationRef> Citations);

/// <param name="Downgraded">Lecture claims that carried no valid citation and were relabelled <c>uncertain</c>.</param>
internal sealed record CatchUpResult(
    Claim NowDiscussing, IReadOnlyList<Claim> Short, IReadOnlyList<DetailItem> Detail, IReadOnlyList<KeyTerm> KeyTerms,
    IReadOnlyList<string> Questions, string Confidence, IReadOnlyList<string> Gaps, int Downgraded, bool Sanitized);
