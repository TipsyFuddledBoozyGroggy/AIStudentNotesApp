using System.Net;
using Tether.Identity.Domain;
using Tether.Identity.Endpoints;
using Tether.Identity.Google;
using Tether.Identity.KeyRing;
using Tether.Identity.Persistence;
using Tether.SharedKernel;
using Tether.SharedKernel.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Tether.Identity;

/// <summary>Public surface of the Identity module. Everything else in this assembly is internal.</summary>
public static class IdentityModule
{
    public const string CookieName = "cc.session";
    public const string HostCookieName = "__Host-cc.session";

    public static WebApplicationBuilder AddIdentityModule(this WebApplicationBuilder builder)
    {
        var config = builder.Configuration;
        var isDev = builder.Environment.IsDevelopment();
        var devLogin = config.GetValue<bool>("Auth:DevLogin:Enabled");
        if (devLogin && !isDev)
            throw new InvalidOperationException(
                "Auth:DevLogin:Enabled is true but the environment is not Development. Refusing to start.");

        var cs = config.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default is required.");

        // ADR-022 section 2: Google sign-in and the invite list are required outside Development; each check names its key, never a value.
        var origins = config.GetSection("Security:AllowedOrigins").Get<string[]>() ?? [];
        var google = GoogleSignInOptions.From(config, isDev, origins);
        var invites = google.Enabled ? InviteList.From(config, required: true) : InviteList.Empty;
        var warnings = new List<string>();
        if (google.Enabled && google.HostedDomainPolicy == HostedDomainPolicy.Allow)
            warnings.Add($"{GoogleSignInOptions.HostedDomainPolicyKey} is 'allow': school or work Google accounts can sign in.");

        var services = builder.Services;
        var options = new IdentityOptions(devLogin, AllowDevUsers: isDev, google, warnings);
        services.AddSingleton(options);
        services.AddSingleton(google);
        services.AddSingleton(invites);
        services.AddSingleton<SignInLimiter>();
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpContextCurrentUser>();
        services.AddScoped<IAuthSessionValidator, AuthSessionValidator>();
        services.AddDbContext<IdentityDbContext>((sp, o) => o
            .UseNpgsql(cs, n => n.MigrationsHistoryTable("__ef_migrations_history", IdentityDbContext.Schema))
            .AddInterceptors(sp.GetRequiredService<OwnershipSaveChangesInterceptor>()));

        if (google.Enabled)
        {
            // No redirects, no proxy from the environment, a hard timeout, a response cap, and only the two Google hosts (auth.v1 section 5).
            services.AddHttpClient(GoogleEndpoints.HttpClientName, c =>
                {
                    c.Timeout = TimeSpan.FromSeconds(10);
                    c.MaxResponseContentBufferSize = GoogleEndpoints.MaxResponseBytes;
                })
                .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
                {
                    AllowAutoRedirect = false, UseProxy = false, UseCookies = false,
                    AutomaticDecompression = DecompressionMethods.None,
                })
                .AddHttpMessageHandler(() => new GoogleEgressGuard());
            services.AddSingleton<GoogleJwksCache>();
            services.AddSingleton<IExternalIdentityProvider, GoogleOidcProvider>();
        }
        else services.AddSingleton<IExternalIdentityProvider, UnconfiguredGoogleIdentityProvider>();

        // Cookie keys survive restarts and redeploys: PostgreSQL, wrapped by the configured certificate (ADR-022 R4, M1-F07).
        KeyRingSetup.Configure(services, config, isDev, warnings);

        services.AddOptions<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme)
            .Configure<TimeProvider>((o, clock) => o.TimeProvider = clock);
        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, o =>
            {
                // __Host- (Secure, Path=/, no Domain) pins the cookie to this host. Browsers reject it without Secure,
                // so Development (plain http on localhost) keeps the unprefixed name.
                o.Cookie.Name = isDev ? CookieName : HostCookieName;
                o.Cookie.HttpOnly = true;
                o.Cookie.SameSite = SameSiteMode.Lax; // Lax, not Strict: the return from Google is a cross-site top-level GET (auth.v1 section 7)
                // Secure everywhere except local http development (browsers treat localhost as secure anyway).
                o.Cookie.SecurePolicy = isDev ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
                o.Cookie.Path = "/";
                o.ExpireTimeSpan = SessionPolicy.SlidingLifetime;
                o.SlidingExpiration = true;
                o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = 401; return Task.CompletedTask; };
                o.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = 403; return Task.CompletedTask; };
                o.Events.OnValidatePrincipal = async ctx =>
                {
                    // ADR-016: the cookie is only a pointer; the sessions row decides (revocation, 14-day absolute
                    // expiry that sliding refresh cannot extend, soft-deleted users, dev users outside Development).
                    var sp = ctx.HttpContext.RequestServices;
                    var ok = await SessionPolicy.IsValidAsync(ctx.Principal, sp.GetRequiredService<IdentityDbContext>(),
                        sp.GetRequiredService<TimeProvider>(), sp.GetRequiredService<IdentityOptions>().AllowDevUsers, ctx.HttpContext.RequestAborted);
                    if (!ok)
                    {
                        ctx.RejectPrincipal();
                        await ctx.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                    }
                };
            });
        // SEC-001: everything requires authentication unless explicitly AllowAnonymous.
        services.AddAuthorization(o =>
            o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
        return builder;
    }

    public static IEndpointRouteBuilder MapIdentityEndpoints(this IEndpointRouteBuilder api, IServiceProvider services)
    {
        IdentityEndpoints.Map(api, services.GetRequiredService<IdentityOptions>());
        return api;
    }

    /// <summary>
    /// True for the two Google sign-in routes and GET /auth/methods. The host's limiter skips its general buckets for them: the Google routes have
    /// their own global, per-binder and invalid-callback buckets (auth.v1 section 8), /auth/methods is a constant answer, and in Azure the per-IP
    /// anonymous bucket would be one shared bucket for every visitor.
    /// </summary>
    public static bool IsGoogleSignInRoute(HttpContext ctx) =>
        ctx.GetEndpoint()?.Metadata.GetMetadata<GoogleSignInRouteMetadata>() is not null;

    /// <summary>The sign-in buckets' configuration keys (with the RateLimiting: prefix) and shipped defaults, for the host's N-05 warning.</summary>
    public static IReadOnlyList<(string Key, int Default)> SignInRateLimitDefaults => SignInLimiter.Defaults;

    public static async Task MigrateIdentityAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database.MigrateAsync(ct);
    }

    /// <summary>
    /// Runs once at startup, after migrations (ADR-022 R3, R8). Logs the module's configuration warnings and, outside Development, revokes
    /// every live session of a dev-login user and of a Google user whose stored email is no longer on the invite list. Logs counts only.
    /// A database whose identity schema was never migrated (an operator-managed schema not yet applied) skips the revocation with a warning.
    /// </summary>
    public static async Task RunIdentityStartupActionsAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        var opts = services.GetRequiredService<IdentityOptions>();
        var log = services.GetRequiredService<ILoggerFactory>().CreateLogger("Tether.Identity");
        foreach (var w in opts.Warnings) log.LogWarning("{Warning}", w);
        if (opts.Google.Enabled)
            log.LogInformation("Google sign-in is on; the invite list has {Count} entries.", services.GetRequiredService<InviteList>().Count);
        if (opts.AllowDevUsers) return;

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var now = scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow();
        var invites = services.GetRequiredService<InviteList>();
        try
        {
            var dev = await db.Sessions
                .Where(s => s.RevokedAt == null && db.Users.Any(u => u.Id == s.UserId && u.ExternalSubject.StartsWith(User.DevPrefix)))
                .ExecuteUpdateAsync(u => u.SetProperty(s => s.RevokedAt, now), ct);
            var live = await db.Users
                .Where(u => u.ExternalSubject.StartsWith(User.GooglePrefix)
                            && db.Sessions.Any(s => s.UserId == u.Id && s.RevokedAt == null && s.ExpiresAt > now))
                .Select(u => new { u.Id, u.Email })
                .ToListAsync(ct);
            var removed = live.Where(u => !invites.Contains(u.Email)).Select(u => u.Id).ToList();
            var uninvited = removed.Count == 0 ? 0 : await db.Sessions
                .Where(s => s.RevokedAt == null && removed.Contains(s.UserId))
                .ExecuteUpdateAsync(u => u.SetProperty(s => s.RevokedAt, now), ct);
            log.LogInformation("Startup revocation: {DevSessions} dev-login sessions and {UninvitedSessions} sessions of people no longer invited.", dev, uninvited);
        }
        catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            log.LogWarning("Startup revocation skipped: the identity schema has not been migrated.");
        }
    }

    /// <summary>Development only: two fixed users so cross-user behaviour is trivial to exercise.</summary>
    public static async Task SeedDevUsersAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        var opts = services.GetRequiredService<IdentityOptions>();
        var env = services.GetRequiredService<IHostEnvironment>();
        if (!opts.DevLoginEnabled || !env.IsDevelopment()) return;
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<TimeProvider>();
        foreach (var (id, email) in new[]
        {
            (Guid.Parse("00000000-0000-7000-8000-00000000000a"), "alice@dev.local"),
            (Guid.Parse("00000000-0000-7000-8000-00000000000b"), "bob@dev.local"),
        })
        {
            var subject = User.DevPrefix + email;
            if (await db.Users.AnyAsync(u => u.ExternalSubject == subject, ct)) continue;
            db.Users.Add(new User
            {
                Id = id, ExternalSubject = subject, Email = email,
                DisplayName = IdentityEndpoints.DisplayNameFrom(email), CreatedAt = clock.GetUtcNow(),
            });
        }
        await db.SaveChangesAsync(ct);
    }
}

/// <param name="AllowDevUsers">True only in Development: dev-login users and their sessions are valid (ADR-022 R8).</param>
internal sealed record IdentityOptions(bool DevLoginEnabled, bool AllowDevUsers, GoogleSignInOptions Google, IReadOnlyList<string> Warnings);

internal sealed class AuthSessionValidator(IdentityDbContext db, TimeProvider clock, IdentityOptions options) : IAuthSessionValidator
{
    public Task<bool> IsValidAsync(Guid userId, Guid authSessionId, CancellationToken ct = default) =>
        SessionPolicy.IsValidAsync(userId, authSessionId, db, clock, options.AllowDevUsers, ct);
}
