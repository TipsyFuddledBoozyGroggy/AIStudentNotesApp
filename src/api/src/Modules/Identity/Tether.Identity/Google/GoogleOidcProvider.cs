using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Tether.Identity.Google;

/// <summary>Fixed Google endpoints (auth.v1 sections 3 and 5). Constants on purpose: no configuration key can point sign-in at another issuer.</summary>
internal static class GoogleEndpoints
{
    public const string Authorize = "https://accounts.google.com/o/oauth2/v2/auth";
    public const string Token = "https://oauth2.googleapis.com/token";
    public const string Jwks = "https://www.googleapis.com/oauth2/v3/certs";
    public static readonly string[] Issuers = ["https://accounts.google.com", "accounts.google.com"];
    /// <summary>The only hosts the <c>google-oidc</c> HttpClient may call.</summary>
    public static readonly string[] EgressHosts = ["oauth2.googleapis.com", "www.googleapis.com"];
    public const string HttpClientName = "google-oidc";
    public const int MaxResponseBytes = 64 * 1024;
}

/// <summary>Refuses any request from the google-oidc client to a host outside <see cref="GoogleEndpoints.EgressHosts"/> (defence in depth).</summary>
internal sealed class GoogleEgressGuard : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri;
        if (uri is null || uri.Scheme != Uri.UriSchemeHttps || !GoogleEndpoints.EgressHosts.Contains(uri.Host, StringComparer.Ordinal))
            throw new InvalidOperationException("Outbound request to a host outside the Google sign-in allow-list was refused.");
        return base.SendAsync(request, cancellationToken);
    }
}

/// <summary>
/// Google's signing keys, cached by the response's max-age (floor 5 minutes, ceiling 24 hours), refetched for an unknown kid at most every
/// 5 minutes. When a refetch fails after the cache expired, the old keys are trusted for at most <see cref="StaleGrace"/> past that expiry
/// (O5); after it no key is returned, so sign-in answers provider_unavailable until Google's key endpoint answers again. A failed refetch is
/// retried at most once a minute.
/// </summary>
internal sealed class GoogleJwksCache(IHttpClientFactory http, TimeProvider clock)
{
    private static readonly TimeSpan Floor = TimeSpan.FromMinutes(5), Ceiling = TimeSpan.FromHours(24), Default = TimeSpan.FromHours(1);
    public static readonly TimeSpan StaleGrace = TimeSpan.FromHours(24);
    private static readonly TimeSpan RetryAfterFailure = TimeSpan.FromMinutes(1);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IReadOnlyList<SecurityKey> _keys = [];
    private DateTimeOffset _expires = DateTimeOffset.MinValue, _lastFetch = DateTimeOffset.MinValue, _lastFailure = DateTimeOffset.MinValue;

    public async Task<IReadOnlyList<SecurityKey>> GetAsync(string? kid, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        if (now < _expires && (kid is null || _keys.Any(k => k.KeyId == kid))) return _keys;
        await _gate.WaitAsync(ct);
        try
        {
            now = clock.GetUtcNow();
            var fresh = now < _expires;
            if (fresh && (kid is null || _keys.Any(k => k.KeyId == kid))) return _keys;
            if (fresh && now - _lastFetch < Floor) return _keys; // unknown kid, but a refetch happened moments ago
            if (now - _lastFailure < RetryAfterFailure) return Usable(now);
            if (await FetchAsync(now, ct)) return _keys;
            _lastFailure = now;
            return Usable(now);
        }
        finally { _gate.Release(); }
    }

    /// <summary>The cached keys while fresh or within the stale grace; nothing after it.</summary>
    private IReadOnlyList<SecurityKey> Usable(DateTimeOffset now) => _expires != DateTimeOffset.MinValue && now < _expires + StaleGrace ? _keys : [];

    private async Task<bool> FetchAsync(DateTimeOffset now, CancellationToken ct)
    {
        try
        {
            using var res = await http.CreateClient(GoogleEndpoints.HttpClientName).GetAsync(GoogleEndpoints.Jwks, HttpCompletionOption.ResponseHeadersRead, ct);
            _lastFetch = now;
            if (!res.IsSuccessStatusCode) return false;
            var json = await GoogleOidcProvider.ReadCappedAsync(res.Content, ct);
            if (json is null) return false;
            var keys = new JsonWebKeySet(json).GetSigningKeys().ToList();
            if (keys.Count == 0) return false;
            _keys = keys;
            var maxAge = res.Headers.CacheControl?.MaxAge ?? Default;
            _expires = now + (maxAge < Floor ? Floor : maxAge > Ceiling ? Ceiling : maxAge);
            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or ArgumentException or System.Text.Json.JsonException
            || (ex is OperationCanceledException && !ct.IsCancellationRequested))
        {
            return false; // network, timeout, egress guard or a malformed key set: treated like any failed fetch
        }
    }
}

/// <summary>
/// Real Google provider: code exchange at the token endpoint, then ID-token validation with Microsoft.IdentityModel (RS256 only, pinned
/// JWKS). Every failure returns null and logs a reason code only. The access token and the ID token are dropped here.
/// </summary>
internal sealed partial class GoogleOidcProvider(
    IHttpClientFactory http, GoogleJwksCache jwks, GoogleSignInOptions options, TimeProvider clock, ILogger<GoogleOidcProvider> logger)
    : IExternalIdentityProvider
{
    public static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(2);
    private static readonly HashSet<string> EmailScopes = ["email", "https://www.googleapis.com/auth/userinfo.email"];
    private static readonly HashSet<string> ProfileScopes = ["profile", "https://www.googleapis.com/auth/userinfo.profile"];
    private readonly JsonWebTokenHandler _handler = new() { MapInboundClaims = false };

    [GeneratedRegex(@"^[\x21-\x7E]{1,200}$")]
    private static partial Regex SubjectPattern();

    public string Name => "google";

    public async Task<ExternalIdentity?> RedeemAsync(string code, string codeVerifier, string expectedNonce, CancellationToken ct)
    {
        string? idToken, scope;
        try
        {
            using var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = options.RedirectUri,
                ["client_id"] = options.ClientId,
                ["client_secret"] = options.ClientSecret,
                ["code_verifier"] = codeVerifier,
            });
            using var req = new HttpRequestMessage(HttpMethod.Post, GoogleEndpoints.Token) { Content = form };
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var res = await http.CreateClient(GoogleEndpoints.HttpClientName).SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!res.IsSuccessStatusCode) return Fail("token_http_" + (int)res.StatusCode); // the body is never read or logged
            var body = await ReadCappedAsync(res.Content, ct);
            if (body is null) return Fail("token_response_too_large");
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return Fail("token_response_malformed");
            idToken = doc.RootElement.TryGetProperty("id_token", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
            scope = doc.RootElement.TryGetProperty("scope", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return Fail("token_timeout"); }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException) { return Fail("token_" + ex.GetType().Name); }

        if (string.IsNullOrEmpty(idToken)) return Fail("token_missing_id_token");
        // auth.v1 section 5 (O4, decided): a PRESENT scope must be exactly the sign-in set, nothing more and nothing less. An ABSENT scope is
        // accepted: Tether uses only the validated ID token's claims and drops the access token unread, so a grant it cannot see is never used.
        if (scope is not null && !ScopeIsExactlySignIn(scope)) return Fail("token_scope_unexpected");
        return await ValidateAsync(idToken, expectedNonce, ct);
    }

    internal static bool ScopeIsExactlySignIn(string scope)
    {
        var items = scope.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var openid = items.Count(i => i == "openid");
        var email = items.Count(EmailScopes.Contains);
        var profile = items.Count(ProfileScopes.Contains);
        return openid == 1 && email == 1 && profile == 1 && items.Length == 3;
    }

    private async Task<ExternalIdentity?> ValidateAsync(string idToken, string expectedNonce, CancellationToken ct)
    {
        JsonWebToken jwt;
        try { jwt = new JsonWebToken(idToken); }
        catch (Exception ex) when (ex is ArgumentException or SecurityTokenException) { return Fail("id_token_malformed"); }

        var keys = await jwks.GetAsync(string.IsNullOrEmpty(jwt.Kid) ? null : jwt.Kid, ct);
        if (keys.Count == 0) return Fail("jwks_unavailable");
        var now = clock.GetUtcNow();
        var parameters = new TokenValidationParameters
        {
            ValidIssuers = GoogleEndpoints.Issuers,
            ValidAudience = options.ClientId,
            IssuerSigningKeys = keys,
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
            RequireSignedTokens = true,
            RequireExpirationTime = true,
            ValidateIssuerSigningKey = true,
            TryAllIssuerSigningKeys = false,
            ClockSkew = ClockSkew,
            // The app clock, not the machine clock (tests move it; production uses TimeProvider.System).
            LifetimeValidator = (notBefore, expires, _, _) =>
                expires is { } exp && now <= new DateTimeOffset(DateTime.SpecifyKind(exp, DateTimeKind.Utc)) + ClockSkew
                && (notBefore is not { } nbf || now + ClockSkew >= new DateTimeOffset(DateTime.SpecifyKind(nbf, DateTimeKind.Utc))),
        };
        var result = await _handler.ValidateTokenAsync(jwt, parameters);
        if (!result.IsValid) return Fail("id_token_" + (result.Exception?.GetType().Name ?? "invalid"));

        if (!jwt.TryGetPayloadValue<long>("iat", out _)) return Fail("id_token_no_iat");
        if (!jwt.TryGetPayloadValue<string>("nonce", out var nonce) || !FixedEquals(nonce, expectedNonce)) return Fail("id_token_nonce");
        if (jwt.TryGetPayloadValue<string>("azp", out var azp) && !string.Equals(azp, options.ClientId, StringComparison.Ordinal)) return Fail("id_token_azp");
        var sub = jwt.Subject;
        if (string.IsNullOrEmpty(sub) || !SubjectPattern().IsMatch(sub)) return Fail("id_token_sub");
        jwt.TryGetPayloadValue<string>("email", out var email);
        var verified = jwt.TryGetPayloadValue<bool>("email_verified", out var v) ? v
            : jwt.TryGetPayloadValue<string>("email_verified", out var vs) && string.Equals(vs, "true", StringComparison.Ordinal);
        jwt.TryGetPayloadValue<string>("name", out var name);
        var hd = jwt.TryGetPayloadValue<string>("hd", out var hdValue) && !string.IsNullOrEmpty(hdValue);
        return new ExternalIdentity(sub, email ?? "", verified, name, hd);
    }

    private static bool FixedEquals(string? a, string b) =>
        a is not null && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));

    private ExternalIdentity? Fail(string reason)
    {
        logger.LogWarning("Google sign-in provider step failed: {Reason}", reason); // a reason code only: never a token, code, email or subject
        return null;
    }

    /// <summary>Reads at most <see cref="GoogleEndpoints.MaxResponseBytes"/>; null when the body is larger.</summary>
    internal static async Task<string?> ReadCappedAsync(HttpContent content, CancellationToken ct)
    {
        await using var stream = await content.ReadAsStreamAsync(ct);
        var buffer = new byte[GoogleEndpoints.MaxResponseBytes + 1];
        var total = 0;
        int n;
        while (total < buffer.Length && (n = await stream.ReadAsync(buffer.AsMemory(total), ct)) > 0) total += n;
        return total > GoogleEndpoints.MaxResponseBytes ? null : Encoding.UTF8.GetString(buffer, 0, total);
    }
}
