namespace CollegeCompanion.Identity.Domain;

/// <summary>
/// Server-side session (ADR-016). The cookie carries only the session id; this row is the authority, so logout,
/// account deletion and the absolute lifetime are enforced by the server, not by the cookie's contents.
/// </summary>
internal sealed class Session
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    /// <summary>Immutable absolute expiry (created + 14 days). Sliding cookie refresh never changes it (SEC-004).</summary>
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}
