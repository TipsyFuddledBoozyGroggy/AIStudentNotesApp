namespace Tether.Identity;

/// <summary>
/// The identity a provider vouched for, after every ID-token rule passed (auth.v1 section 5). Email and name are display only; the key is the
/// subject. No token is part of it: the access and ID tokens are dropped inside the provider once validated (invariant 3).
/// </summary>
internal sealed record ExternalIdentity(string Subject, string Email, bool EmailVerified, string? DisplayName, bool HostedDomainPresent);

/// <summary>
/// Port for Google OIDC (authorization code + PKCE S256 + client secret; scopes openid email profile only). The flow code (state, binder,
/// admission, user mapping) lives in <see cref="Google.GoogleSignInEndpoints"/> and calls this only after the state was consumed.
/// </summary>
internal interface IExternalIdentityProvider
{
    string Name { get; }

    /// <summary>
    /// Exchanges <paramref name="code"/> with the PKCE <paramref name="codeVerifier"/> and validates the returned ID token, including its nonce
    /// against <paramref name="expectedNonce"/>. Returns null for every failure (network, non-2xx, malformed response, any token rule): the
    /// caller answers <c>provider_unavailable</c>. Never throws for a provider failure; never logs a token, code, email or subject.
    /// </summary>
    Task<ExternalIdentity?> RedeemAsync(string code, string codeVerifier, string expectedNonce, CancellationToken ct);
}

/// <summary>Registered when no Google client is configured (Development only). The Google routes are not mapped then.</summary>
internal sealed class UnconfiguredGoogleIdentityProvider : IExternalIdentityProvider
{
    public string Name => "google";
    public Task<ExternalIdentity?> RedeemAsync(string code, string codeVerifier, string expectedNonce, CancellationToken ct)
        => throw new NotSupportedException("Google sign-in is not configured.");
}
