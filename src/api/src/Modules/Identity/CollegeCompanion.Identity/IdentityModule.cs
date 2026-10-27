using CollegeCompanion.Identity.Domain;
using CollegeCompanion.Identity.Endpoints;
using CollegeCompanion.Identity.Persistence;
using CollegeCompanion.SharedKernel;
using CollegeCompanion.SharedKernel.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace CollegeCompanion.Identity;

/// <summary>Public surface of the Identity module. Everything else in this assembly is internal.</summary>
public static class IdentityModule
{
    public const string CookieName = "cc.session";
    public const string HostCookieName = "__Host-cc.session";

    public static WebApplicationBuilder AddIdentityModule(this WebApplicationBuilder builder)
    {
        var devLogin = builder.Configuration.GetValue<bool>("Auth:DevLogin:Enabled");
        if (devLogin && !builder.Environment.IsDevelopment())
            throw new InvalidOperationException(
                "Auth:DevLogin:Enabled is true but the environment is not Development. Refusing to start.");

        var cs = builder.Configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default is required.");
        var services = builder.Services;
        services.AddSingleton(new IdentityOptions(devLogin));
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpContextCurrentUser>();
        services.AddSingleton<IExternalIdentityProvider, UnconfiguredGoogleIdentityProvider>();
        services.AddDbContext<IdentityDbContext>((sp, o) => o
            .UseNpgsql(cs, n => n.MigrationsHistoryTable("__ef_migrations_history", IdentityDbContext.Schema))
            .AddInterceptors(sp.GetRequiredService<OwnershipSaveChangesInterceptor>()));

        var isDev = builder.Environment.IsDevelopment();

        // Cookie encryption keys must survive restarts and be shared by instances (F-07). No keys live in the repo:
        // the directory comes from configuration (mount a volume / secret store). Keys are NOT encrypted at rest here;
        // protect the directory (devops dependency, SEC-019).
        var dp = services.AddDataProtection().SetApplicationName("CollegeCompanion");
        if (builder.Configuration["DataProtection:KeysPath"] is { Length: > 0 } keysPath)
            dp.PersistKeysToFileSystem(new DirectoryInfo(keysPath));

        services.AddOptions<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme)
            .Configure<TimeProvider>((o, clock) => o.TimeProvider = clock);
        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, o =>
            {
                // __Host- (Secure, Path=/, no Domain) pins the cookie to this host. Browsers reject it without Secure,
                // so Development (plain http on localhost) keeps the unprefixed name.
                o.Cookie.Name = isDev ? CookieName : HostCookieName;
                o.Cookie.HttpOnly = true;
                o.Cookie.SameSite = SameSiteMode.Lax;
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
                    // expiry that sliding refresh cannot extend, soft-deleted users).
                    var sp = ctx.HttpContext.RequestServices;
                    var ok = await SessionPolicy.IsValidAsync(ctx.Principal, sp.GetRequiredService<IdentityDbContext>(),
                        sp.GetRequiredService<TimeProvider>(), ctx.HttpContext.RequestAborted);
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
        IdentityEndpoints.Map(api, services.GetRequiredService<IdentityOptions>().DevLoginEnabled);
        return api;
    }

    public static async Task MigrateIdentityAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database.MigrateAsync(ct);
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
            if (await db.Users.AnyAsync(u => u.Email == email, ct)) continue;
            db.Users.Add(new User
            {
                Id = id, ExternalSubject = "dev:" + email, Email = email,
                DisplayName = IdentityEndpoints.DisplayNameFrom(email), CreatedAt = clock.GetUtcNow(),
            });
        }
        await db.SaveChangesAsync(ct);
    }
}

internal sealed record IdentityOptions(bool DevLoginEnabled);
