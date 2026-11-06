namespace Tether.Docs.Contracts;

/// <summary>
/// The destination of a notes export. The demo implementation keeps the document in our own database; a real provider is NOT built and
/// needs its own Product Owner go (docs/architecture/docs-export-demo-adr.md section 9). Nothing in this assembly can carry a credential,
/// a cookie or an HTTP client: a real provider resolves whatever it needs INSIDE itself, never through this interface.
/// </summary>
public interface IDocsProvider
{
    string ProviderId { get; }
    Task<ProviderResult<DocRef>> CreateAsync(DocsWriteRequest request, CancellationToken ct);
    Task<ProviderResult<DocRef>> UpdateAsync(DocRef existing, int expectedVersion, DocsWriteRequest request, CancellationToken ct);
    Task<ProviderResult> DeleteAsync(DocRef existing, CancellationToken ct);
}

/// <param name="UserId">Opaque, for provider-side lookup; a provider asserts it equals the ambient user.</param>
/// <param name="RequestKey">The export id: the provider-side idempotency key.</param>
public sealed record DocsWriteRequest(Guid UserId, Guid RequestKey, DocumentModel Document);

public sealed record DocumentModel(string Title, IReadOnlyList<DocBlock> Blocks);

/// <summary>One block of plain text. <see cref="Kind"/> is heading, bullet or paragraph; <see cref="Level"/> only for a heading; <see cref="CitationStartMs"/> only for a bullet.</summary>
public sealed record DocBlock(string Kind, string Text, int? Level, IReadOnlyList<long>? CitationStartMs);

public sealed record DocRef(Guid DocId, int Version);

/// <summary>A provider failure by code only (the contract's record error codes). Provider text is never carried.</summary>
public sealed record ProviderFailure(string Code, bool Retryable, int? RetryAfterSeconds);

public class ProviderResult
{
    protected ProviderResult(ProviderFailure? failure) => Failure = failure;
    public ProviderFailure? Failure { get; }
    public bool Succeeded => Failure is null;

    public static ProviderResult Ok() => new(null);
    public static ProviderResult Retryable(string code, int? retryAfterSeconds) => new(new ProviderFailure(code, true, retryAfterSeconds));
    public static ProviderResult Permanent(string code) => new(new ProviderFailure(code, false, null));
}

public sealed class ProviderResult<T> : ProviderResult where T : class
{
    private ProviderResult(T? value, ProviderFailure? failure) : base(failure) => Value = value;
    public T? Value { get; }

    public static ProviderResult<T> Ok(T value) => new(value, null);
    public static new ProviderResult<T> Retryable(string code, int? retryAfterSeconds) => new(null, new ProviderFailure(code, true, retryAfterSeconds));
    public static new ProviderResult<T> Permanent(string code) => new(null, new ProviderFailure(code, false, null));
}
