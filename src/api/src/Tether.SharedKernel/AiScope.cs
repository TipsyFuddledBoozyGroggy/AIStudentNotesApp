namespace Tether.SharedKernel.Ai;

/// <summary>Fixed scope-kind vocabulary for AI work (also the budget pool name and a database CHECK on <c>ai.ai_usage</c>).</summary>
public static class AiScopeKinds
{
    public const string Lecture = "lecture";
    public const string Syllabus = "syllabus";
}

/// <summary>
/// Implemented by a module that owns document-scoped AI work (Syllabus) so the AI gateway can ask, without referencing that module,
/// whether the work is still wanted. The gateway calls it before reserving budget and again just before the vendor call; it fails
/// CLOSED (no vendor call, no spend) when this returns false, throws, or when no probe is registered for the scope kind.
/// </summary>
public interface IAiDocumentScopeProbe
{
    /// <summary>The scope kind this probe answers for (an <see cref="AiScopeKinds"/> value other than lecture).</summary>
    string Kind { get; }

    /// <summary>
    /// True only if the scope exists, belongs to <paramref name="userId"/>, and its job is still running (not cancelled, deleted, failed
    /// or finished). Must be a cheap, user-scoped read.
    /// </summary>
    Task<bool> IsLiveAsync(Guid userId, Guid scopeId, CancellationToken ct);
}

/// <summary>
/// Implemented by the AI module, called by the owning module when document work is cancelled or deleted: every in-flight provider call
/// for the scope is cancelled at once and its budget hold is given back (the document counterpart of pause, stop or delete for a lecture).
/// </summary>
public interface IAiScopeCutoff
{
    void Cancel(string kind, Guid scopeId);
}
