using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Tether.IntegrationTests.Infrastructure;

namespace Tether.IntegrationTests;

/// <summary>How the fake signs or shapes the next token response (ID-token failure cases).</summary>
public enum FakeSigning { Valid, OtherKeySameKid, UnknownKid, AlgNone, Hs256WithPublicKey }

/// <summary>Per-code behaviour of <see cref="FakeGoogle"/>.</summary>
public sealed class FakeGrant
{
    public required string Challenge { get; init; }
    public Dictionary<string, object> Claims { get; } = [];
    public FakeSigning Signing { get; set; } = FakeSigning.Valid;
    public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
    public string? Scope { get; set; } = "openid https://www.googleapis.com/auth/userinfo.email https://www.googleapis.com/auth/userinfo.profile";
    public bool OmitIdToken { get; set; }
    public bool Used { get; set; }
}

/// <summary>
/// In-process stand-in for Google's token and JWKS endpoints (ADR-022 section 3). It replaces ONLY the primary handler of the
/// <c>google-oidc</c> HttpClient, so the real provider, the egress guard and all ID-token validation run unchanged. It checks the client
/// secret, the exact redirect_uri and that the PKCE verifier hashes to the challenge from the start URL; it throws on any other host.
/// There is no configuration key in the app that could point sign-in at it: tests swap it in through DI only.
/// </summary>
public sealed class FakeGoogle : HttpMessageHandler
{
    public const string Kid = "fake-kid-1";
    private readonly RsaSecurityKey _key = new(RSA.Create(2048)) { KeyId = Kid };
    private readonly RsaSecurityKey _other = new(RSA.Create(2048)) { KeyId = Kid };
    private readonly ConcurrentDictionary<string, FakeGrant> _grants = new();
    private readonly string _redirectUri;
    private readonly Func<DateTimeOffset> _now;
    private int _tokenCalls, _jwksCalls;

    public FakeGoogle(string redirectUri, Func<DateTimeOffset> now) { _redirectUri = redirectUri; _now = now; }

    public int TokenCalls => _tokenCalls;
    /// <summary>When true the JWKS endpoint answers 503 (Google's key endpoint down).</summary>
    public bool JwksFails { get; set; }
    /// <summary>The last ID token this fake signed (log canary sentinel).</summary>
    public string? LastIdToken { get; private set; }
    public int JwksCalls => _jwksCalls;
    public string ClientId { get; init; } = ApiFactory.GoogleClientId;
    public string ClientSecret { get; init; } = ApiFactory.GoogleClientSecret;

    /// <summary>Issues a one-time code for the flow whose start URL carried <paramref name="challenge"/> and <paramref name="nonce"/>.</summary>
    public (string Code, FakeGrant Grant) Issue(string challenge, string nonce, string sub, string email, bool emailVerified = true, string? name = "Invited Person")
    {
        var code = "4/fake-" + Guid.NewGuid().ToString("N");
        var now = _now().ToUnixTimeSeconds();
        var g = new FakeGrant { Challenge = challenge };
        g.Claims["iss"] = "https://accounts.google.com";
        g.Claims["aud"] = ClientId;
        g.Claims["azp"] = ClientId;
        g.Claims["sub"] = sub;
        g.Claims["email"] = email;
        g.Claims["email_verified"] = emailVerified;
        if (name is not null) g.Claims["name"] = name;
        g.Claims["nonce"] = nonce;
        g.Claims["iat"] = now;
        g.Claims["exp"] = now + 3600;
        _grants[code] = g;
        return (code, g);
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri!;
        if (uri.Host == "www.googleapis.com" && uri.AbsolutePath == "/oauth2/v3/certs")
        {
            Interlocked.Increment(ref _jwksCalls);
            if (JwksFails) return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            var jwk = JsonWebKeyConverter.ConvertFromRSASecurityKey(new RsaSecurityKey(_key.Rsa.ExportParameters(false)) { KeyId = Kid });
            var json = JsonSerializer.Serialize(new { keys = new[] { new { kty = jwk.Kty, kid = Kid, use = "sig", alg = "RS256", n = jwk.N, e = jwk.E } } });
            var res = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
            res.Headers.CacheControl = new CacheControlHeaderValue { Public = true, MaxAge = TimeSpan.FromHours(6) };
            return res;
        }
        if (uri.Host == "oauth2.googleapis.com" && uri.AbsolutePath == "/token" && request.Method == HttpMethod.Post)
        {
            Interlocked.Increment(ref _tokenCalls);
            var form = (await request.Content!.ReadAsStringAsync(cancellationToken)).Split('&')
                .Select(p => p.Split('=', 2)).ToDictionary(p => WebUtility.UrlDecode(p[0]), p => WebUtility.UrlDecode(p[1]));
            if (form.GetValueOrDefault("grant_type") != "authorization_code" || form.GetValueOrDefault("client_id") != ClientId
                || form.GetValueOrDefault("client_secret") != ClientSecret || form.GetValueOrDefault("redirect_uri") != _redirectUri)
                return Error("invalid_client");
            if (!_grants.TryGetValue(form.GetValueOrDefault("code") ?? "", out var g) || g.Used) return Error("invalid_grant");
            g.Used = true;
            var verifier = form.GetValueOrDefault("code_verifier") ?? "";
            var challenge = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            if (challenge != g.Challenge) return Error("invalid_grant");
            if (g.Status != HttpStatusCode.OK) return new HttpResponseMessage(g.Status) { Content = new StringContent("{\"error\":\"server_error\"}") };
            var body = new Dictionary<string, object?>
            {
                ["access_token"] = "ya29.fake-access-token-never-stored", ["expires_in"] = 3599, ["token_type"] = "Bearer",
            };
            if (g.Scope is not null) body["scope"] = g.Scope;
            if (!g.OmitIdToken) body["id_token"] = LastIdToken = Sign(g);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
        }
        throw new InvalidOperationException("FakeGoogle: unexpected request " + request.Method + " " + uri.GetLeftPart(UriPartial.Path));
    }

    private static HttpResponseMessage Error(string code) =>
        new(HttpStatusCode.BadRequest) { Content = new StringContent($"{{\"error\":\"{code}\"}}", Encoding.UTF8, "application/json") };

    private string Sign(FakeGrant g)
    {
        static string B64(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var payload = JsonSerializer.Serialize(g.Claims);
        switch (g.Signing)
        {
            case FakeSigning.AlgNone:
                return B64(Encoding.UTF8.GetBytes("{\"alg\":\"none\",\"typ\":\"JWT\"}")) + "." + B64(Encoding.UTF8.GetBytes(payload)) + ".";
            case FakeSigning.Hs256WithPublicKey:
            {
                var pub = _key.Rsa.ExportSubjectPublicKeyInfo();
                var creds = new SigningCredentials(new SymmetricSecurityKey(pub) { KeyId = Kid }, SecurityAlgorithms.HmacSha256);
                return new JsonWebTokenHandler().CreateToken(payload, creds);
            }
            default:
            {
                var key = g.Signing switch
                {
                    FakeSigning.OtherKeySameKid => _other,
                    FakeSigning.UnknownKid => new RsaSecurityKey(_key.Rsa) { KeyId = "unknown-kid" },
                    _ => _key,
                };
                return new JsonWebTokenHandler().CreateToken(payload, new SigningCredentials(key, SecurityAlgorithms.RsaSha256));
            }
        }
    }

    // A handler the HttpClientFactory rotates must stay usable: never dispose the shared fake.
    protected override void Dispose(bool disposing) { }
}

/// <summary>A browser-like sign-in run against a factory that uses <see cref="FakeGoogle"/>.</summary>
public sealed record StartedSignIn(string State, string Nonce, string Challenge, string Binder, string BinderSetCookie, string AuthorizationUrl, string ExpiresAt);

public static partial class SignInDriver
{
    [GeneratedRegex("^(__Host-)?cc\\.signin=([^;]*)")]
    private static partial Regex BinderPattern();

    public static Dictionary<string, string> Query(string url) =>
        url[(url.IndexOf('?') + 1)..].Split('&').Select(p => p.Split('=', 2)).ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1]));

    public static async Task<StartedSignIn> StartAsync(HttpClient c, string? binderCookieHeader = null)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/google/signin/start") { Content = new StringContent("{\"landing\":\"home\"}", Encoding.UTF8, "application/json") };
        if (binderCookieHeader is not null) req.Headers.TryAddWithoutValidation("Cookie", binderCookieHeader);
        var res = await c.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.BodyAsync();
        var url = body.GetProperty("authorizationUrl").GetString()!;
        var q = Query(url);
        var setCookie = res.Headers.GetValues("Set-Cookie").Single(h => BinderPattern().IsMatch(h));
        return new StartedSignIn(q["state"], q["nonce"], q["code_challenge"], setCookie.Split(';')[0], setCookie, url, body.GetProperty("expiresAt").GetString()!);
    }

    public static Task<HttpResponseMessage> CallbackAsync(HttpClient c, string query, string? cookieHeader)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/google/callback?" + query);
        if (cookieHeader is not null) req.Headers.TryAddWithoutValidation("Cookie", cookieHeader);
        return c.SendAsync(req);
    }

    public static string Location(HttpResponseMessage r)
    {
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        return r.Headers.Location!.OriginalString;
    }

    public static string? SessionCookie(HttpResponseMessage r) =>
        r.Headers.TryGetValues("Set-Cookie", out var v) ? v.Where(ApiFactory.IsSessionCookie).Select(h => h.Split(';')[0]).LastOrDefault(h => !h.EndsWith('=')) : null;
}

/// <summary>Production-mode API with migrations on and the Google HTTP handler replaced by <see cref="Fake"/>.</summary>
public class GoogleFactory : ApiFactory
{
    public FakeGoogle Fake { get; }
    /// <summary>The client secret the fake expects; a fixture that configures another Auth:Google:ClientSecret overrides this to match.</summary>
    protected virtual string FakeClientSecret => GoogleClientSecret;
    public GoogleFactory() => Fake = new FakeGoogle(WebOrigin + "/api/v1/auth/google/callback", () => Clock.GetUtcNow()) { ClientSecret = FakeClientSecret };
    protected override string EnvironmentName => "Production";
    protected virtual IEnumerable<KeyValuePair<string, string?>> ExtraSettings => [];
    protected sealed override IEnumerable<KeyValuePair<string, string?>> Settings => [new("Database:MigrateOnStartup", "true"), .. ExtraSettings];

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(s => s.AddHttpClient("google-oidc").ConfigurePrimaryHttpMessageHandler(() => Fake));
    }

    /// <summary>Start, let the fake issue a code for the given account, and return the callback response.</summary>
    public async Task<(HttpResponseMessage Response, StartedSignIn Started, FakeGrant Grant)> SignInAsync(
        string sub, string email, bool emailVerified = true, Action<FakeGrant>? shape = null, string? name = "Invited Person")
    {
        var c = NewClient();
        var s = await SignInDriver.StartAsync(c);
        var (code, grant) = Fake.Issue(s.Challenge, s.Nonce, sub, email, emailVerified, name);
        shape?.Invoke(grant);
        var res = await SignInDriver.CallbackAsync(c, $"state={s.State}&code={Uri.EscapeDataString(code)}&scope=openid%20email%20profile&authuser=0&prompt=none", s.Binder);
        return (res, s, grant);
    }

    public HttpClient WithCookie(string cookie)
    {
        var c = NewClient();
        c.DefaultRequestHeaders.Add("Cookie", cookie);
        return c;
    }
}
