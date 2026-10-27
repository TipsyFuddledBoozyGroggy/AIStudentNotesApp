namespace Tether.Identity;

internal sealed record ExternalIdentity(string Subject, string Email, string DisplayName);

/// <summary>
/// Port for Google OIDC (authorization code + PKCE, scopes: openid email profile only).
/// NOT IMPLEMENTED in Milestone 1: no Google OAuth client exists (PO-6). Only this stub is registered
/// and no /auth/google/* endpoints are mapped.
/// </summary>
internal interface IExternalIdentityProvider
{
    string Name { get; }
    Task<ExternalIdentity> CompleteSignInAsync(string code, string state, string codeVerifier, CancellationToken ct);
}

internal sealed class UnconfiguredGoogleIdentityProvider : IExternalIdentityProvider
{
    public string Name => "google";
    public Task<ExternalIdentity> CompleteSignInAsync(string code, string state, string codeVerifier, CancellationToken ct)
        => throw new NotSupportedException("Google sign-in is not configured.");
}
