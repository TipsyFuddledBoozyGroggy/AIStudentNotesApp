using Tether.Ai.Budget;
using Tether.Ai.CatchUp;
using Tether.Ai.Persistence;
using Tether.Ai.Provider;
using Tether.Ai.Summary;
using Tether.Lectures.Contracts;
using Tether.SharedKernel.Persistence;
using Tether.Transcript.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tether.Ai;

/// <summary>
/// Public surface of the AI module: rolling TL;DR, Quick recap, the usage ledger and the provider seam. Everything else in this
/// assembly is internal. The only provider is the deterministic mock; the module refuses to start with any other (AiProviderGuard).
/// </summary>
public static class AiModule
{
    public static WebApplicationBuilder AddAiModule(this WebApplicationBuilder builder)
    {
        var config = builder.Configuration;
        var cs = config.GetConnectionString("Default") ?? throw new InvalidOperationException("ConnectionStrings:Default is required.");
        var env = builder.Environment;
        var devOrTest = env.IsDevelopment() || env.IsEnvironment("Test") || env.IsEnvironment("Testing");

        var options = AiOptions.From(config);
        AiProviderGuard.Validate(config, options, devOrTest); // throws for any provider but the mock, and for fault injection outside dev/test
        var services = builder.Services;
        services.AddSingleton(options);
        services.AddSingleton<MockAiClient>();
        services.AddSingleton<IAiClient>(sp => sp.GetRequiredService<MockAiClient>());

        services.AddDbContext<AiDbContext>((sp, o) => o
            .UseNpgsql(cs, n => n.MigrationsHistoryTable("__ef_migrations_history", AiDbContext.Schema))
            .AddInterceptors(sp.GetRequiredService<OwnershipSaveChangesInterceptor>()));
        services.AddScoped<GlobalSpend>();
        services.AddScoped<UsageLedger>();
        services.AddScoped<BudgetGate>();
        services.AddScoped<AiGateway>();
        services.AddScoped<SummaryStore>();
        services.AddScoped<CatchUpService>();
        services.AddScoped<Notes.NotesPressProbe>();
        services.AddScoped<Notes.NotesService>();
        services.AddScoped<ISummaryJoinSource, SummaryJoinSource>();
        services.AddScoped<Tether.Ai.Contracts.INotesSnapshotReader, Notes.NotesSnapshotReader>(); // read-only, user-bound notes view for the Docs export

        services.AddSingleton<AiHealth>();
        services.AddSingleton<SessionCutoff>();
        services.AddSingleton<Tether.SharedKernel.Ai.IAiScopeCutoff>(sp => sp.GetRequiredService<SessionCutoff>());
        services.AddSingleton<ISessionLifecycleObserver>(sp => sp.GetRequiredService<SessionCutoff>());
        services.AddSingleton<SummaryEngine>();
        services.AddSingleton<SummaryScheduler>();
        services.AddSingleton<ITranscriptFinalObserver>(sp => sp.GetRequiredService<SummaryScheduler>());
        services.AddSingleton<ISessionLifecycleObserver>(sp => sp.GetRequiredService<SummaryScheduler>());
        services.AddSingleton<CatchUpQueue>();
        services.AddSingleton<CatchUpLimiter>();
        services.AddSingleton<CatchUpRunner>();
        services.AddSingleton<Notes.NotesQueue>();
        services.AddSingleton<Notes.NotesLimiter>();
        services.AddSingleton<Notes.NotesJobs>();
        services.AddSingleton<Notes.NotesRunner>();
        services.AddSingleton<ReservationReaper>();
        services.AddHostedService(sp => sp.GetRequiredService<ReservationReaper>());
        services.AddHostedService<SummaryWorker>();
        services.AddHostedService(sp => sp.GetRequiredService<CatchUpRunner>());
        services.AddHostedService(sp => sp.GetRequiredService<Notes.NotesRunner>());
        return builder;
    }

    /// <summary>REST: summary, quick-recap, catch-ups and (PO-18d) notes after Stop. Map under <c>/api/v1</c>.</summary>
    public static IEndpointRouteBuilder MapAiEndpoints(this IEndpointRouteBuilder api)
    {
        AiEndpoints.Map(api);
        return api;
    }

    public static async Task MigrateAiAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AiDbContext>().Database.MigrateAsync(ct);
    }
}
