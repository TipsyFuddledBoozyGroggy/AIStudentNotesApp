using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;

namespace Tether.Identity.Google;

internal enum HostedDomainPolicy { Refuse, Allow }

/// <summary>
/// Google sign-in configuration (ADR-022 section 2). Validated at startup; every message names the configuration key, never a value.
/// Outside Development a complete configuration is required (R1). In Development an absent client id leaves Google sign-in off.
/// </summary>
internal sealed partial record GoogleSignInOptions(
    bool Enabled, string ClientId, string ClientSecret, string RedirectUri, HostedDomainPolicy HostedDomainPolicy, int MaxLiveStates)
{
    public const string ClientIdKey = "Auth:Google:ClientId";
    public const string ClientSecretKey = "Auth:Google:ClientSecret";
    public const string RedirectUriKey = "Auth:Google:RedirectUri";
    public const string AdmissionKey = "Auth:Google:Admission";
    public const string HostedDomainPolicyKey = "Auth:Google:HostedDomainPolicy";
    public const string MaxLiveStatesKey = "Auth:Google:MaxLiveStates";
    public const string CallbackPath = "/api/v1/auth/google/callback";
    public static readonly TimeSpan StateLifetime = TimeSpan.FromMinutes(10);

    public static readonly GoogleSignInOptions Disabled = new(false, "", "", "", HostedDomainPolicy.Refuse, 3000);

    [GeneratedRegex(@"^[0-9]+-[a-z0-9]+\.apps\.googleusercontent\.com$")]
    private static partial Regex ClientIdPattern();

    public static GoogleSignInOptions From(IConfiguration config, bool isDevelopment, IReadOnlyCollection<string> allowedOrigins)
    {
        var clientId = config[ClientIdKey]?.Trim() ?? "";
        var secret = config[ClientSecretKey] ?? "";
        if (clientId.Length == 0)
        {
            if (!isDevelopment)
                throw new InvalidOperationException($"{ClientIdKey} is required outside Development (Google sign-in is the only sign-in there).");
            if (secret.Length > 0)
                throw new InvalidOperationException($"{ClientSecretKey} is set but {ClientIdKey} is not. Set both or neither.");
            return Disabled;
        }
        if (!ClientIdPattern().IsMatch(clientId))
            throw new InvalidOperationException($"{ClientIdKey} is not a Google OAuth client id (<digits>-<letters and digits>.apps.googleusercontent.com).");
        if (string.IsNullOrWhiteSpace(secret))
            throw new InvalidOperationException($"{ClientSecretKey} is required when {ClientIdKey} is set.");

        var redirect = ValidateRedirectUri(config[RedirectUriKey], isDevelopment, allowedOrigins);

        var admission = config[AdmissionKey];
        if (string.IsNullOrEmpty(admission) && isDevelopment) admission = "allowlist";
        if (!string.Equals(admission, "allowlist", StringComparison.Ordinal))
            throw new InvalidOperationException($"{AdmissionKey} must be 'allowlist' (invite-only, PO-20cg). Open sign-up is not available.");

        var policy = config[HostedDomainPolicyKey] switch
        {
            null or "" or "refuse" => HostedDomainPolicy.Refuse,
            "allow" => HostedDomainPolicy.Allow,
            _ => throw new InvalidOperationException($"{HostedDomainPolicyKey} must be 'refuse' or 'allow'."),
        };

        var maxLive = config.GetValue(MaxLiveStatesKey, 3000);
        if (maxLive <= 0) throw new InvalidOperationException($"{MaxLiveStatesKey} must be a positive number.");

        return new GoogleSignInOptions(true, clientId, secret, redirect, policy, maxLive);
    }

    private static string ValidateRedirectUri(string? value, bool isDevelopment, IReadOnlyCollection<string> allowedOrigins)
    {
        if (string.IsNullOrWhiteSpace(value) || !Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri))
            throw new InvalidOperationException($"{RedirectUriKey} must be an absolute URL ending in {CallbackPath}.");
        var localHttp = isDevelopment && uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback;
        if (uri.Scheme != Uri.UriSchemeHttps && !localHttp)
            throw new InvalidOperationException($"{RedirectUriKey} must use https (plain http only for localhost in Development).");
        if (!string.Equals(uri.AbsolutePath, CallbackPath, StringComparison.Ordinal) || uri.Query.Length > 0 || uri.Fragment.Length > 0
            || uri.UserInfo.Length > 0 || value.Trim().Contains('#') || value.Trim().Contains('?'))
            throw new InvalidOperationException($"{RedirectUriKey} must be exactly <web origin>{CallbackPath}, with no query or fragment.");
        var origin = uri.GetLeftPart(UriPartial.Authority);
        if (!allowedOrigins.Any(a => string.Equals(a.TrimEnd('/'), origin, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"The origin of {RedirectUriKey} must be one of Security:AllowedOrigins (the web origin the browser uses).");
        return origin + CallbackPath;
    }
}
