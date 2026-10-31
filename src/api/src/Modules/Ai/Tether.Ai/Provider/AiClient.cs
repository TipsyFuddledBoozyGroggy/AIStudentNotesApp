using Tether.SharedKernel.Ai;

namespace Tether.Ai.Provider;

/// <summary>
/// Provider-neutral AI call surface (docs/ai/live-lecture-ai-design.md section 9). The ONLY implementation in this increment is
/// <see cref="MockAiClient"/>. A real adapter is a SEAM, not code: see docs/ai/b3-implementation.md "Adapter seam notes".
/// An adapter must:
///  - send transcript and document text only inside <see cref="UntrustedBlock"/>s rendered by <see cref="Prompts.PromptRenderer"/>;
///  - register no tools (live workflows are pure structured generation) and hold no OAuth token or other user's id;
///  - return the raw JSON text; schema checks, citation resolution and repair belong to the gateway, not the adapter;
///  - throw <see cref="AiClientException"/> with a kind only; never put prompt, completion or header text in an exception message;
///  - treat a spend-cap style 429 (no retry-after) as terminal (<see cref="AiFailureKind.ProviderCap"/>), never loop on it.
/// </summary>
internal interface IAiClient
{
    string ProviderName { get; }

    /// <summary>True for the deterministic mock. The UI already shows "Demo source" for mock-backed sessions.</summary>
    bool IsMock { get; }

    Task<AiResponse> CompleteAsync(AiRequest request, CancellationToken ct);
}

/// <summary>
/// <see cref="Notes"/> and <see cref="FinalSummary"/> (PO-18d) are the only workflows that may run for a STOPPED lecture, and only while a
/// "Make notes" press is pending for that user and session (<see cref="AiRequest.PressId"/>). Every other lecture workflow needs an Active session.
/// </summary>
internal enum AiWorkflow { RollingSummary, Compaction, QuickRecap, SyllabusExtract, Notes, FinalSummary }

/// <summary>Fast maps to the cheap model, Balanced to the mid model (config). There is no Deep tier on the live path (ADR-009).</summary>
internal enum AiTier { Fast, Balanced }

/// <summary>One untrusted line of source text: a transcript segment (or a stored derived topic) with its id and start offset.</summary>
internal sealed record SourceLine(string Id, long StartMs, string Text);

/// <summary>A block of untrusted data. <paramref name="Kind"/> is one of the fixed names in <see cref="BlockKinds"/>.</summary>
internal sealed record UntrustedBlock(string Kind, IReadOnlyList<SourceLine> Lines);

internal static class BlockKinds
{
    public const string RollingSummary = "rolling_summary";
    public const string RecentWindow = "recent_window";
    public const string NewSegments = "new_segments";
    public const string WindowSegments = "window_segments";
    public const string SourceChunk = "source_chunk";
}

/// <summary>
/// What a unit of AI work belongs to: a lecture session (kind <c>lecture</c>) or a document job (kind <c>syllabus</c>). The kind is
/// also the budget pool, so lecture and syllabus spend are capped independently. Kinds are a fixed vocabulary (database CHECK).
/// </summary>
internal readonly record struct AiScope(string Kind, Guid Id)
{
    public static AiScope Lecture(Guid sessionId) => new(AiScopeKinds.Lecture, sessionId);
    public static AiScope Syllabus(Guid syllabusId) => new(AiScopeKinds.Syllabus, syllabusId);
    public string Pool => Kind;
    public bool IsLecture => Kind == AiScopeKinds.Lecture;
}

/// <summary>
/// <paramref name="Instruction"/> is trusted, server-built text (revision numbers, window length). <paramref name="RepairHint"/> is a
/// code from <see cref="Validation.ValidationCodes"/> and never echoes model output or transcript text.
/// </summary>
internal sealed record AiRequest(
    AiWorkflow Workflow, string PromptVersion, AiTier Tier, string System, string Instruction, IReadOnlyList<UntrustedBlock> Blocks,
    int MaxOutputTokens, Guid UserId, Guid SessionId, string IdempotencyKey, TimeSpan Timeout, string? RepairHint = null, AiScope? Scope = null, Guid? PressId = null)
{
    /// <summary>True for the two after-Stop workflows (PO-18d). They are refused unless <see cref="PressId"/> names a live press.</summary>
    public bool IsAfterStop => Workflow is AiWorkflow.Notes or AiWorkflow.FinalSummary;

    /// <summary>The scope the work is accounted and cut off under. Null means the lecture session in <see cref="SessionId"/> (every lecture caller).</summary>
    public AiScope EffectiveScope => Scope ?? AiScope.Lecture(SessionId);

    /// <summary>A request for document-scoped work. <see cref="SessionId"/> is empty: there is no lecture.</summary>
    public static AiRequest ForDocument(AiScope scope, AiWorkflow workflow, string promptVersion, AiTier tier, string system, string instruction,
        IReadOnlyList<UntrustedBlock> blocks, int maxOutputTokens, Guid userId, string idempotencyKey, TimeSpan timeout) =>
        new(workflow, promptVersion, tier, system, instruction, blocks, maxOutputTokens, userId, Guid.Empty, idempotencyKey, timeout, null, scope);
}

internal sealed record AiTokenUsage(int InputTokens, int CachedTokens, int OutputTokens);

internal sealed record AiResponse(string Json, AiTokenUsage Usage, string Model, string StopReason, int LatencyMs);

internal enum AiFailureKind { Timeout, RateLimited, ProviderCap, Overloaded, Unavailable }

/// <summary>Provider failure. The message is the kind name only. <see cref="RetryAfter"/> is null when the provider sent none.</summary>
internal sealed class AiClientException(AiFailureKind kind, TimeSpan? retryAfter = null) : Exception(kind.ToString())
{
    public AiFailureKind Kind { get; } = kind;
    public TimeSpan? RetryAfter { get; } = retryAfter;
}

/// <summary>
/// Marks where derived text came from on the wire (B3-08, same class as the STT <c>demoSource</c> flag): <c>mock</c> for the
/// deterministic extractive mock, <c>model</c> for a real provider. Derived from the stored model id, so no extra column is needed.
/// </summary>
internal static class AiSource
{
    public const string Mock = "mock", Model = "model";
    public static string Of(string? model) => model is not null && model.StartsWith("mock", StringComparison.OrdinalIgnoreCase) ? Mock : Model;
}
