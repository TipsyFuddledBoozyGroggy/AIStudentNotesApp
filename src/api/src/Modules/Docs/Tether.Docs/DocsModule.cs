using Tether.Docs.Application;
using Tether.Docs.Contracts;
using Tether.Docs.Persistence;
using Tether.SharedKernel.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tether.Docs;

/// <summary>
/// Public surface of the Docs module (finish-backlog item 3, docs.export.v1). MOCK ONLY: the destination is the Demo Doc, a row in our own database.
/// Startup refuses any other provider while real Google stays off (PO-20cb, PO-20cc); a configuration flag is not a human gate, so the refusal is
/// unconditional. There is no token, no OAuth, no HttpClient and no network call anywhere in this assembly, and no reference to the AI module:
/// the notes come in through the read-only <c>INotesSnapshotReader</c> (invariant 3). Everything else in this assembly is internal.
/// </summary>
public static class DocsModule
{
    public static WebApplicationBuilder AddDocsModule(this WebApplicationBuilder builder)
    {
        var cfg = builder.Configuration;
        var cs = cfg.GetConnectionString("Default") ?? throw new InvalidOperationException("ConnectionStrings:Default is required.");

        // ---- startup guards ----
        if (!string.Equals(cfg["Docs:Provider"] ?? "demo_docs", "demo_docs", StringComparison.Ordinal))
            throw new InvalidOperationException("Docs:Provider must be 'demo_docs'. A real Google Docs provider needs its own Product Owner go, a contract change and a security review (PO-20cb).");
        // Fault injection exists only to rehearse failures. ANY key under Docs:Demo:Faults:* (not only Enabled=true) is refused outside Development and Test (DXD-07).
        var devOrTest = builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Test");
        if (!devOrTest && cfg.GetSection("Docs:Demo:Faults").GetChildren().Any())
            throw new InvalidOperationException("Docs:Demo:Faults:* is allowed only in Development and Test.");

        builder.Services.AddDbContext<DocsDbContext>((sp, o) => o
            .UseNpgsql(cs, n => n.MigrationsHistoryTable("__ef_migrations_history", DocsDbContext.Schema))
            .AddInterceptors(sp.GetRequiredService<OwnershipSaveChangesInterceptor>()));

        builder.Services.AddSingleton(DocsOptions.From(cfg));
        builder.Services.AddSingleton(sp => DocsKeys.From(cfg, sp.GetRequiredService<Tether.SharedKernel.Security.DerivedKeys>()));
        builder.Services.AddScoped<DemoFaultContext>();
        builder.Services.AddScoped<IDocsProvider, DemoDocsProvider>();
        builder.Services.AddScoped<DocsExportService>();
        return builder;
    }

    public static IEndpointRouteBuilder MapDocsEndpoints(this IEndpointRouteBuilder api)
    {
        var sp = api.ServiceProvider;
        var origins = sp.GetRequiredService<IConfiguration>().GetSection("Security:AllowedOrigins").Get<string[]>() ?? [];
        DocsEndpoints.Map(api, sp.GetRequiredService<DocsOptions>(), origins);
        return api;
    }

    public static async Task MigrateDocsAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<DocsDbContext>().Database.MigrateAsync(ct);
    }
}
