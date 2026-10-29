using System.Security.Claims;
using System.Threading.RateLimiting;
using Tether.Lectures.Application;
using Tether.Lectures.Contracts;
using Tether.Lectures.Persistence;
using Tether.SharedKernel.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Tether.Lectures;

/// <summary>Public surface of the Lectures module. Everything else in this assembly is internal.</summary>
public static class LecturesModule
{
    public static WebApplicationBuilder AddLecturesModule(this WebApplicationBuilder builder)
    {
        var cs = builder.Configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default is required.");
        builder.Services.AddDbContext<LecturesDbContext>((sp, o) => o
            .UseNpgsql(cs, n => n.MigrationsHistoryTable("__ef_migrations_history", LecturesDbContext.Schema))
            .AddInterceptors(sp.GetRequiredService<OwnershipSaveChangesInterceptor>()));
        builder.Services.AddSingleton<ConsentTextRegistry>();
        builder.Services.AddSingleton(sp => new SessionETag(ETagKey(builder.Configuration, sp.GetRequiredService<Tether.SharedKernel.Security.DerivedKeys>())));
        builder.Services.AddScoped<SessionService>();
        builder.Services.AddScoped<Tether.Courses.Contracts.ICourseLifecycleHandler, CourseLifecycleHandler>();
        builder.Services.AddScoped<ExpiredRowSweeper>();
        var sweepSeconds = Math.Max(30, builder.Configuration.GetValue("Lectures:SweepIntervalSeconds", 300));
        builder.Services.AddHostedService(sp => new ExpiredRowSweeperService(
            sp.GetRequiredService<IServiceScopeFactory>(), sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<ExpiredRowSweeperService>>(), TimeSpan.FromSeconds(sweepSeconds)));
        builder.Services.AddSingleton(LectureLimits.From(builder.Configuration));
        builder.Services.AddSingleton<SessionActivityTracker>();
        builder.Services.AddSingleton<ISessionActivity>(sp => sp.GetRequiredService<SessionActivityTracker>());
        builder.Services.AddScoped<SessionPortsService>();
        builder.Services.AddScoped<ISessionDirectory>(sp => sp.GetRequiredService<SessionPortsService>());
        builder.Services.AddScoped<ISessionEventReader>(sp => sp.GetRequiredService<SessionPortsService>());
        builder.Services.AddScoped<ISessionSequencer>(sp => sp.GetRequiredService<SessionPortsService>());
        builder.Services.AddScoped<SessionLifecycleJob>();
        var jobSeconds = Math.Max(1, builder.Configuration.GetValue("Lectures:LifecycleJobSeconds", 30));
        builder.Services.AddHostedService(sp => new SessionLifecycleService(
            sp.GetRequiredService<IServiceScopeFactory>(), sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<SessionLifecycleService>>(), TimeSpan.FromSeconds(jobSeconds)));
        builder.Services.AddScoped<AudioTicketService>();
        builder.Services.AddScoped<IAudioTicketConsumer>(sp => sp.GetRequiredService<AudioTicketService>());

        // Ticket issuance: 6 per minute per user (C-04). The contract also says per session; only one session per user can
        // be Active (C-18), so a per-user bucket is at least as strict as a per-session one.
        var permits = builder.Configuration.GetValue("RateLimiting:AudioTicketPermitPerMinute", 6);
        builder.Services.AddRateLimiter(o => o.AddPolicy(SessionEndpoints.AudioTicketPolicy, ctx =>
            RateLimitPartition.GetFixedWindowLimiter(
                ctx.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anon",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = permits, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 })));
        return builder;
    }

    /// <summary>HKDF sub-key of the per-environment root secret (B1-11). <c>Security:ETagKey</c> (base64, >= 32 bytes) remains an optional explicit override.</summary>
    private static byte[] ETagKey(IConfiguration config, Tether.SharedKernel.Security.DerivedKeys keys)
    {
        if (config["Security:ETagKey"] is not { Length: > 0 } b64) return keys.Derive(Tether.SharedKernel.Security.DerivedKeys.Labels.ETag);
        var key = Convert.FromBase64String(b64);
        return key.Length >= 32 ? key : throw new InvalidOperationException("Security:ETagKey must decode to at least 32 bytes.");
    }

    public static IEndpointRouteBuilder MapLecturesEndpoints(this IEndpointRouteBuilder api)
    {
        SessionEndpoints.Map(api);
        return api;
    }

    public static async Task MigrateLecturesAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<LecturesDbContext>().Database.MigrateAsync(ct);
    }
}
