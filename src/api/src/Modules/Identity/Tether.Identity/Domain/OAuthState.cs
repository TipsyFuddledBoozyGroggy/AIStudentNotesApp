namespace Tether.Identity.Domain;

/// <summary>
/// One Google sign-in in flight (auth.v1 section 3, ADR-022 R5). Holds no secret: the state and the binder cookie value are stored only as
/// SHA-256 hashes, and the nonce and PKCE verifier are derived from the state with root-secret keys, never stored. Single use is enforced by
/// an atomic UPDATE on <see cref="ConsumedAt"/> at the callback.
/// </summary>
internal sealed class OAuthState
{
    public byte[] StateHash { get; set; } = [];
    public byte[] BinderHash { get; set; } = [];
    public string Purpose { get; set; } = "";
    public string Landing { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? ConsumedAt { get; set; }
}
