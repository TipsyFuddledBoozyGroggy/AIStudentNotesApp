using Tether.SharedKernel;

namespace Tether.Docs.Domain;

/// <summary>
/// The stand-in "document" of the Demo Doc provider (invariant 5: derived data, stored apart from the transcript). Deleted with its lecture
/// (composite FK, cascade). <see cref="OriginExportId"/> is unique: the provider-side idempotency guard.
/// </summary>
internal sealed class DemoDoc : IUserOwned
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid SessionId { get; set; }
    public string Title { get; set; } = "";
    /// <summary>The blocks as JSON (jsonb): structure, not Markdown.</summary>
    public string Blocks { get; set; } = "[]";
    public int Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid OriginExportId { get; set; }
}

/// <summary>One row per confirm key. In the demo every attempt is one transaction, so the status is only ever succeeded or failed.</summary>
internal sealed class DocExport : IUserOwned
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid SessionId { get; set; }
    public Guid NotesId { get; set; }
    public string Provider { get; set; } = DocsConstants.Provider;
    public string Mode { get; set; } = "";
    public string Status { get; set; } = ExportStatus.Succeeded;
    public string Outcome { get; set; } = "";
    public string ContentDigest { get; set; } = "";
    public Guid? DocId { get; set; }
    public int? DocVersion { get; set; }
    public int Attempt { get; set; } = 1;
    public string? ErrorCode { get; set; }
    public bool? Retryable { get; set; }
    public int? RetryAfterSeconds { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

/// <summary>The one current document per lecture and provider. <c>create_another</c> moves the pointer.</summary>
internal sealed class DocTarget : IUserOwned
{
    public Guid UserId { get; set; }
    public Guid SessionId { get; set; }
    public string Provider { get; set; } = DocsConstants.Provider;
    public Guid DocId { get; set; }
    public string LastDigest { get; set; } = "";
    public int Version { get; set; }
    public Guid LastExportId { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Idempotency record: ids and hashes only, never a response body, title or text. Cascades through its export.</summary>
internal sealed class DocIdempotencyKey : IUserOwned
{
    public Guid UserId { get; set; }
    public string Method { get; set; } = "POST";
    public string RouteTemplate { get; set; } = "";
    public byte[] KeyHash { get; set; } = [];
    public byte[] FingerprintHmac { get; set; } = [];
    public Guid ExportId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Content-free audit: ids, a fixed action, counts and codes. No title, text, digest, body or token. No FK to the session, so it outlives the lecture.</summary>
internal sealed class DocAuditEvent : IUserOwned
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid SessionId { get; set; }
    public Guid? ExportId { get; set; }
    public string Action { get; set; } = "";
    public string Provider { get; set; } = DocsConstants.Provider;
    public string? ErrorCode { get; set; }
    public int? NotesRevision { get; set; }
    public int BlockCount { get; set; }
    public int Attempt { get; set; }
    public DateTimeOffset At { get; set; }
}

internal static class ExportStatus
{
    public const string Succeeded = "succeeded", Failed = "failed";
}

internal static class AuditActions
{
    public const string Created = "export_created", Updated = "export_updated", Unchanged = "export_unchanged", Failed = "export_failed", DocDeleted = "doc_deleted";
}

internal static class DocsConstants
{
    public const string Provider = "demo_docs";
    public const string Label = "Demo Doc";
    public const string ExportPrefix = "dxp", DocPrefix = "ddc";
    public const int MaxAttempts = 5, MaxBlocks = 400, MaxDocsPerUser = 200;
}
