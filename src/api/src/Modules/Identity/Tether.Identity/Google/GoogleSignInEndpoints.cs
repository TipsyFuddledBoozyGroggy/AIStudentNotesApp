using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Tether.Identity.Domain;
using Tether.Identity.Endpoints;
using Tether.Identity.Persistence;
using Tether.SharedKernel;
using Tether.SharedKernel.Http;
using Tether.SharedKernel.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Tether.Identity.Google;

/// <summary>
/// Marks the sign-in routes the host's general anonymous and per-user buckets skip: the two Google routes (they have <see cref="SignInLimiter"/>)
/// and GET /auth/methods (a constant answer, no database read).
/// </summary>
internal sealed class GoogleSignInRouteMetadata
{
    public static readonly GoogleSignInRouteMetadata Instance = new();
}

internal sealed record SignInStartRequest(string Landing);

/// <summary>
/// auth.v1 (docs/API_CONTRACTS_AUTH.md, ADR-022): POST /auth/google/signin/start and GET /auth/google/callback. Hand-written flow on the
/// reviewed calendar.v1 rules; the ID token itself is checked by <see cref="GoogleOidcProvider"/>. Nothing is written for a sign-in until the
/// state is consumed, and nothing persists for a refusal. Logs carry outcome and reason codes only.
/// </summary>
internal static partial class GoogleSignInEndpoints
{
    public const string StartRoute = "/auth/google/signin/start";
    public const string CallbackRoute = "/auth/google/callback";
    public const string BinderCookie = "cc.signin";
    public const string HostBinderCookie = "__Host-cc.signin";
    public const string Scope = "openid email profile";
    private const int MaxBodyBytes = 1024;

    private static readonly JsonSerializerOptions Strict = new(JsonSerializerDefaults.Web)
    {
        AllowDuplicateProperties = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectNullableAnnotations = true,
        PropertyNameCaseInsensitive = false,
        AllowTrailingCommas = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        MaxDepth = 4,
    };

    [GeneratedRegex("^[A-Za-z0-9_-]{43}$")]
    private static partial Regex RandomTokenPattern();

    public static void Map(IEndpointRouteBuilder api)
    {
        api.MapPost(StartRoute, Start).AllowAnonymous().WithMetadata(GoogleSignInRouteMetadata.Instance);
        api.MapGet(CallbackRoute, Callback).AllowAnonymous().WithMetadata(GoogleSignInRouteMetadata.Instance);
    }

    public static string BinderCookieName(IdentityOptions o) => o.AllowDevUsers ? BinderCookie : HostBinderCookie;

    // ------------------------------------------------------------------------------------------------------------------------------
    // Start
    // ------------------------------------------------------------------------------------------------------------------------------

    private static async Task<IResult> Start(HttpContext ctx, IdentityDbContext db, TimeProvider clock, DerivedKeys keys,
        IdentityOptions opts, SignInLimiter limiter, CancellationToken ct)
    {
        if (!limiter.TryStart(BinderKey(ctx, opts))) return RateLimited(ctx);

        if (!IsJson(ctx.Request.ContentType)) return Problems.ForStatus(415);
        var body = await ReadBodyAsync(ctx, ct);
        if (body is null) return Problems.Validation("body", "The request was invalid.");
        if (!string.Equals(body.Landing, "home", StringComparison.Ordinal)) return Problems.Validation("landing", "Unknown landing.");

        var now = clock.GetUtcNow();
        // Opportunistic clean-up (no timer: an idle API must stay idle). Tracked deletes, at most 200 per start; the cap bounds the rest.
        var expired = await db.OAuthStates.Where(s => s.ExpiresAt <= now).OrderBy(s => s.ExpiresAt).Take(200).ToListAsync(ct);
        if (expired.Count > 0)
        {
            db.OAuthStates.RemoveRange(expired);
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { /* a concurrent start removed some of them first: nothing to do */ }
            db.ChangeTracker.Clear();
        }
        if (await db.OAuthStates.CountAsync(s => s.ConsumedAt == null && s.ExpiresAt > now, ct) >= opts.Google.MaxLiveStates) return RateLimited(ctx);

        var state = NewToken();
        var binder = NewToken();
        var expires = now + GoogleSignInOptions.StateLifetime;
        db.OAuthStates.Add(new OAuthState
        {
            StateHash = Sha256(state), BinderHash = Sha256(binder), Purpose = "signin", Landing = body.Landing,
            CreatedAt = now, ExpiresAt = expires,
        });
        await db.SaveChangesAsync(ct);

        ctx.Response.Cookies.Append(BinderCookieName(opts), binder, BinderCookieOptions(ctx, opts, GoogleSignInOptions.StateLifetime));
        var url = AuthorizationUrl(opts.Google, state, Derive(keys, DerivedKeys.Labels.OAuthNonce, state),
            Challenge(Derive(keys, DerivedKeys.Labels.OAuthPkce, state)));
        return Results.Json(new
        {
            purpose = "signin",
            authorizationUrl = url,
            expiresAt = expires.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture),
        });
    }

    internal static string AuthorizationUrl(GoogleSignInOptions g, string state, string nonce, string challenge)
    {
        static string P(string k, string v) => k + "=" + Uri.EscapeDataString(v);
        return GoogleEndpoints.Authorize + "?" + string.Join("&",
            P("client_id", g.ClientId), P("redirect_uri", g.RedirectUri), P("response_type", "code"), P("scope", Scope),
            P("state", state), P("nonce", nonce), P("code_challenge", challenge), P("code_challenge_method", "S256"),
            P("prompt", "select_account"));
    }

    private static async Task<SignInStartRequest?> ReadBodyAsync(HttpContext ctx, CancellationToken ct)
    {
        if (ctx.Request.ContentLength is > MaxBodyBytes) return null;
        using var ms = new MemoryStream();
        var buffer = new byte[512];
        int n;
        while ((n = await ctx.Request.Body.ReadAsync(buffer, ct)) > 0)
        {
            ms.Write(buffer, 0, n);
            if (ms.Length > MaxBodyBytes) return null;
        }
        try { return JsonSerializer.Deserialize<SignInStartRequest>(ms.ToArray(), Strict); }
        catch (JsonException) { return null; }
    }

    private static bool IsJson(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType)) return false;
        var semi = contentType.IndexOf(';');
        return string.Equals((semi < 0 ? contentType : contentType[..semi]).Trim(), "application/json", StringComparison.OrdinalIgnoreCase);
    }

    private static IResult RateLimited(HttpContext ctx)
    {
        ctx.Response.Headers.RetryAfter = "60";
        return Problems.ForStatus(429);
    }

    // ------------------------------------------------------------------------------------------------------------------------------
    // Callback
    // ------------------------------------------------------------------------------------------------------------------------------

    private static async Task<IResult> Callback(HttpContext ctx, IdentityDbContext db, TimeProvider clock, DerivedKeys keys,
        IdentityOptions opts, InviteList invites, SignInLimiter limiter, IExternalIdentityProvider provider, ILoggerFactory loggers)
    {
        var log = loggers.CreateLogger("Tether.Identity.GoogleSignIn");
        var binderKey = BinderKey(ctx, opts);
        var binderRaw = binderKey is null ? null : ctx.Request.Cookies[BinderCookieName(opts)];
        // Every callback response expires the binder, whatever the outcome (auth.v1 section 7).
        ctx.Response.Cookies.Delete(BinderCookieName(opts), BinderCookieOptions(ctx, opts, null));

        string outcome;
        try { outcome = await RunAsync(ctx, db, clock, keys, opts, invites, provider, limiter, log, binderRaw, binderKey); }
        catch (Exception ex) when (ex is not OperationCanceledException || !ctx.RequestAborted.IsCancellationRequested)
        {
            log.LogError("Google sign-in failed with {ExceptionType}", ex.GetType().FullName); // type only, never the message
            outcome = "provider_unavailable";
        }
        log.LogInformation("Google sign-in finished: {Outcome}", outcome);
        return Results.Redirect("/signin?purpose=signin&outcome=" + outcome);
    }

    private static async Task<string> RunAsync(HttpContext ctx, IdentityDbContext db, TimeProvider clock, DerivedKeys keys,
        IdentityOptions opts, InviteList invites, IExternalIdentityProvider provider, SignInLimiter limiter, ILogger log,
        string? binderRaw, string? binderKey)
    {
        var q = ctx.Request.Query;
        var ct = ctx.RequestAborted;
        // Anything that is not a live state with its own binder spends the small INVALID bucket (and is rate_limited beyond it); only a
        // valid return spends the bound budget, so forged callbacks with random cookie values can never starve real ones (auth.v1 section 8).
        string Invalid() => limiter.TryInvalidCallback() ? "state_invalid" : "rate_limited";
        var state = q["state"].Count == 1 ? q["state"].ToString() : "";
        if (!RandomTokenPattern().IsMatch(state)) return Invalid();

        var hash = Sha256(state);
        var now = clock.GetUtcNow();
        var row = await db.OAuthStates.FirstOrDefaultAsync(s => s.StateHash == hash, ct);
        var live = row is not null && row.ConsumedAt is null && row.ExpiresAt > now;
        var matches = live && binderRaw is not null && CryptographicOperations.FixedTimeEquals(Sha256(binderRaw), row!.BinderHash);
        if (!matches)
        {
            // A binder mismatch burns a live state (login CSRF: the attacker's state is never usable afterwards).
            if (live) await ConsumeAsync(db, row!, now, ct);
            return Invalid();
        }
        if (!limiter.TryValidCallback(binderKey!)) return "rate_limited"; // the state stays live: a retry of this return can still succeed

        // Single use: consumed_at is a concurrency token, so the UPDATE carries "and consumed_at is null" and exactly one request can flip it.
        if (!await ConsumeAsync(db, row!, now, ct)) return "state_invalid";
        if (!string.Equals(row!.Purpose, "signin", StringComparison.Ordinal)) return "state_invalid";

        if (q["error"].Count > 0)
            return string.Equals(q["error"].ToString(), "access_denied", StringComparison.Ordinal) ? "access_denied" : "provider_unavailable";
        var code = q["code"].Count == 1 ? q["code"].ToString() : "";
        if (code.Length is 0 or > 2048) return "provider_unavailable";

        var identity = await provider.RedeemAsync(code, Derive(keys, DerivedKeys.Labels.OAuthPkce, state),
            Derive(keys, DerivedKeys.Labels.OAuthNonce, state), ct);
        if (identity is null) return "provider_unavailable";

        // Admission (R3) before anything is created: a verified email on Tether's own list. Then the school-account rule (R7).
        var email = InviteList.Normalize(identity.Email);
        if (!identity.EmailVerified) return Refused(log, "email_unverified", "not_invited");
        if (email.Length > 320 || !invites.Contains(email)) return Refused(log, "not_invited", "not_invited");
        if (identity.HostedDomainPresent && opts.Google.HostedDomainPolicy == HostedDomainPolicy.Refuse) return Refused(log, "hosted_domain", "school_account");

        // One transaction for the user row and the session row (O6): a failure anywhere up to the commit leaves neither behind, so
        // "created: nothing" holds for every outcome but signed_in. EF takes a savepoint per SaveChanges, so the race catch below still works.
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var subject = User.GooglePrefix + identity.Subject;
        var user = await db.Users.FirstOrDefaultAsync(u => u.ExternalSubject == subject, ct);
        if (user?.DeletedAt is not null) return Refused(log, "account_closed", "account_closed");
        var displayName = CleanDisplayName(identity.DisplayName) ?? IdentityEndpoints.DisplayNameFrom(email);
        if (user is null)
        {
            user = new User
            {
                Id = Ids.New(), ExternalSubject = subject, Email = email, DisplayName = displayName,
                HostedDomainPresent = identity.HostedDomainPresent, CreatedAt = now,
            };
            db.Users.Add(user);
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                // Two first sign-ins of the same account raced (two tabs): the other one created the row. Use it.
                db.ChangeTracker.Clear();
                user = await db.Users.FirstAsync(u => u.ExternalSubject == subject, ct);
                if (user.DeletedAt is not null) return Refused(log, "account_closed", "account_closed");
            }
        }
        else if (user.Email != email || user.DisplayName != displayName || user.HostedDomainPresent != identity.HostedDomainPresent)
        {
            user.Email = email;
            user.DisplayName = displayName;
            user.HostedDomainPresent = identity.HostedDomainPresent;
            await db.SaveChangesAsync(ct);
        }

        await IdentityEndpoints.SignInAsync(ctx, db, clock, user);
        await tx.CommitAsync(ct);
        return "signed_in";
    }

    /// <summary>Flips consumed_at; false when another request consumed the row first (concurrency token).</summary>
    private static async Task<bool> ConsumeAsync(IdentityDbContext db, OAuthState row, DateTimeOffset now, CancellationToken ct)
    {
        row.ConsumedAt = now;
        try { await db.SaveChangesAsync(ct); return true; }
        catch (DbUpdateConcurrencyException) { return false; }
        finally { db.ChangeTracker.Clear(); }
    }

    private static string Refused(ILogger log, string reason, string outcome)
    {
        log.LogInformation("Google sign-in refused: {Reason}", reason); // no email, no subject
        return outcome;
    }

    /// <summary>The <c>name</c> claim without control or format characters, trimmed, at most 200 characters; null when nothing is left.</summary>
    internal static string? CleanDisplayName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var sb = new StringBuilder(name.Length);
        foreach (var r in name.EnumerateRunes())
        {
            var cat = Rune.GetUnicodeCategory(r);
            if (cat is System.Globalization.UnicodeCategory.Control or System.Globalization.UnicodeCategory.Format
                or System.Globalization.UnicodeCategory.LineSeparator or System.Globalization.UnicodeCategory.ParagraphSeparator) continue;
            sb.Append(r.ToString());
        }
        var clean = sb.ToString().Trim();
        if (clean.Length == 0) return null;
        if (clean.Length > 200)
        {
            var cut = 200;
            if (char.IsHighSurrogate(clean[cut - 1])) cut--;
            clean = clean[..cut].TrimEnd();
        }
        return clean;
    }

    // ------------------------------------------------------------------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------------------------------------------------------------------

    private static CookieOptions BinderCookieOptions(HttpContext ctx, IdentityOptions opts, TimeSpan? maxAge) => new()
    {
        HttpOnly = true,
        Secure = !opts.AllowDevUsers || ctx.Request.IsHttps,
        SameSite = SameSiteMode.Lax,
        Path = "/",
        MaxAge = maxAge,
        IsEssential = true,
    };

    /// <summary>Hex SHA-256 of a well-formed binder cookie (the limiter partition), or null.</summary>
    private static string? BinderKey(HttpContext ctx, IdentityOptions opts)
    {
        var raw = ctx.Request.Cookies[BinderCookieName(opts)];
        return raw is not null && RandomTokenPattern().IsMatch(raw) ? Convert.ToHexString(Sha256(raw)) : null;
    }

    private static string NewToken() => Base64Url(RandomNumberGenerator.GetBytes(32));
    internal static byte[] Sha256(string value) => SHA256.HashData(Encoding.ASCII.GetBytes(value));
    internal static string Derive(DerivedKeys keys, string label, string state) =>
        Base64Url(HMACSHA256.HashData(keys.Derive(label), Encoding.ASCII.GetBytes(state)));
    internal static string Challenge(string verifier) => Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
    private static string Base64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
