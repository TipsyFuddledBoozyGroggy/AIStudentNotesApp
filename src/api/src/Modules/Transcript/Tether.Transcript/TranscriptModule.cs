using System.Security.Claims;
using System.Threading.RateLimiting;
using Tether.Lectures.Contracts;
using Tether.SharedKernel.Persistence;
using Tether.Transcript.Application;
using Tether.Transcript.Hub;
using Tether.Transcript.Persistence;
using Tether.Transcript.Relay;
using Tether.Transcript.Stt;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Tether.Transcript;

/// <summary>
/// Public surface of the Transcript module: source transcript persistence, the demo STT provider, the ticketed audio relay and
/// the lecture.v1 hub. Everything else in this assembly is internal.
/// </summary>
public static class TranscriptModule
{
    public const string HubPath = "/hubs/lecture";
    public const string IngestPrefix = "/ingest/";

    public static WebApplicationBuilder AddTranscriptModule(this WebApplicationBuilder builder)
    {
        var config = builder.Configuration;
        var cs = config.GetConnectionString("Default") ?? throw new InvalidOperationException("ConnectionStrings:Default is required.");
        var services = builder.Services;

        // Startup guard: M2 has exactly one provider, the deterministic demo source. A real provider needs Product Owner
        // authorization (PO-2/3/4) and gates G1 and G2 recorded PASS by a human in docs/RELEASE_GATES.md; none is implemented.
        // B2-08: fault injection and the scripted text exist for tests and drills. Outside Development or Test they would let a
        // misconfigured deployment serve scripted "lecture" text or injected failures, so the process refuses to start.
        var env = builder.Environment;
        var devOrTest = env.IsDevelopment() || env.IsEnvironment("Test") || env.IsEnvironment("Testing");
        if (!devOrTest)
        {
            if (config.GetSection("Speech:Replay:Faults").GetChildren().Any(c => !string.IsNullOrEmpty(c.Value)))
                throw new InvalidOperationException("Speech:Replay:Faults is set but the environment is not Development or Test. Fault injection is demo/test only.");
            if (!string.IsNullOrEmpty(config["Speech:Replay:Script"]))
                throw new InvalidOperationException("Speech:Replay:Script is set but the environment is not Development or Test. A scripted transcript is demo/test only.");
        }
        var provider = config["Speech:Provider"];
        if (!string.IsNullOrEmpty(provider) && !string.Equals(provider, "replay", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Speech:Provider must be 'replay'. No real speech-to-text provider is implemented or authorized (gates G1/G2 are open).");

        services.AddDbContext<TranscriptDbContext>((sp, o) => o
            .UseNpgsql(cs, n => n.MigrationsHistoryTable("__ef_migrations_history", TranscriptDbContext.Schema))
            .AddInterceptors(sp.GetRequiredService<OwnershipSaveChangesInterceptor>()));
        services.AddScoped<TranscriptWriter>();
        services.AddScoped<TranscriptReader>();
        services.AddScoped<Tether.Transcript.Contracts.ITranscriptSegmentReader, TranscriptSegmentReader>();

        var options = RealtimeOptions.From(config);
        services.AddSingleton(options);

        var script = config["Speech:Replay:Script"] is { Length: > 0 } json ? ReplaySttProvider.ParseScript(json) : ReplaySttProvider.DefaultScript;
        var replay = new ReplaySttProvider(script, ReplayFaults.From(config));
        services.AddSingleton<ISpeechToTextProvider>(replay);
        services.AddSingleton<IDemoSourceIndicator>(new DemoSourceIndicator(replay));

        services.AddSingleton<PartialStore>();
        services.AddSingleton<HubConnections>();
        services.AddSingleton<JoinLimiter>();
        services.AddSingleton<HubInvocationLimiter>();
        services.AddSingleton<FailedHelloLimiter>();
        services.AddSingleton<RelayRegistry>();
        services.AddSingleton<LiveFanout>();
        services.AddSingleton<Tether.Transcript.Contracts.ILiveEventPublisher, LiveEventPublisher>();
        services.AddSingleton<RelayServices>();
        services.AddSingleton<ISessionLifecycleObserver, RealtimeLifecycleObserver>();
        services.AddSingleton<Tether.SharedKernel.IAuthSessionRevocationObserver, RealtimeRevocationObserver>();
        services.AddHostedService<HubReaper>();

        // C-26: hub messages are tiny (Join/Leave/Ping), so 4 KB is plenty. Clients never send transcript content.
        services.AddSignalR(o =>
        {
            o.MaximumReceiveMessageSize = 4096;
            o.AddFilter<HubUserFilter>(); // a filter that is only registered in DI is never run; it must be added to HubOptions (B2-02)
        });

        services.AddRateLimiter(o => o.AddPolicy(AudioIngestEndpoint.UpgradePolicy, ctx =>
            RateLimitPartition.GetFixedWindowLimiter(
                ctx.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anon",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = options.UpgradePermitPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 })));
        return builder;
    }

    /// <summary>REST: <c>GET /sessions/{id}/transcript</c>. Map under <c>/api/v1</c>.</summary>
    public static IEndpointRouteBuilder MapTranscriptEndpoints(this IEndpointRouteBuilder api)
    {
        TranscriptEndpoints.Map(api);
        return api;
    }

    /// <summary>Realtime: the hub at <c>/hubs/lecture</c> (WebSockets transport only) and the audio socket under <c>/ingest/</c>. Map at the root.</summary>
    public static IEndpointRouteBuilder MapRealtimeEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapHub<LectureHub>(HubPath, o =>
        {
            o.Transports = HttpTransportType.WebSockets; // C-07: no long polling or SSE fallbacks
            o.ApplicationMaxBufferSize = 64 * 1024;
            o.TransportMaxBufferSize = 64 * 1024;
        });
        AudioIngestEndpoint.Map(app);
        return app;
    }

    public static async Task MigrateTranscriptAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<TranscriptDbContext>().Database.MigrateAsync(ct);
    }
}

internal sealed class DemoSourceIndicator(ISpeechToTextProvider provider) : IDemoSourceIndicator
{
    public bool IsDemoSource => provider.IsDemo;
}
