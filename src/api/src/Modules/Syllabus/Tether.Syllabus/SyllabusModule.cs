using Tether.Syllabus.Application;
using Tether.Syllabus.Extraction;
using Tether.Syllabus.Persistence;
using Tether.Syllabus.Text;
using Tether.SharedKernel.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tether.Syllabus;

/// <summary>
/// Public surface of the Syllabus module (Phase 2, slice 1, syllabus.v1). MOCK ONLY: the extractor is a deterministic rule-based mock and the
/// calendar is a database-only mock. Startup refuses any other extractor or calendar provider while the Product Owner decisions PO-4 and
/// PO-6 and the gates G1/G2 are open; a configuration flag is not a human gate, so the refusal is unconditional (R2-21).
/// Everything else in this assembly is internal.
/// </summary>
public static class SyllabusModule
{
    public static WebApplicationBuilder AddSyllabusModule(this WebApplicationBuilder builder)
    {
        var cfg = builder.Configuration;
        var cs = cfg.GetConnectionString("Default") ?? throw new InvalidOperationException("ConnectionStrings:Default is required.");

        // ---- startup guards ----
        TextPolicy.EnsureNormalizationAvailable(TextPolicy.NormalizationAvailable);
        ZoneAllowList.EnsureHostSupportsAll(); // B1-14: no tzdata means no DST-gap detection, so refuse to start
        if (!string.Equals(cfg["Syllabus:CalendarProvider"] ?? "mock", "mock", StringComparison.Ordinal))
            throw new InvalidOperationException("Syllabus:CalendarProvider must be 'mock'. A real calendar needs a new calendar.v1 contract and a security review (PO-6).");
        if (!string.Equals(cfg["Syllabus:Extractor"] ?? "mock", "mock", StringComparison.Ordinal))
            throw new InvalidOperationException("Syllabus:Extractor must be 'mock'. A model extractor needs PO-4 and gate G2 and is never enabled by configuration alone.");
        var devOrTest = builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Test");
        if (!devOrTest && (cfg.GetSection("Syllabus:Mock:Faults").GetChildren().Any() || cfg.GetValue("Syllabus:Mock:StageDelayMilliseconds", 0) > 0))
            throw new InvalidOperationException("Syllabus:Mock fault injection is allowed only in Development and Test.");

        builder.Services.AddDbContext<SyllabusDbContext>((sp, o) => o
            .UseNpgsql(cs, n => n.MigrationsHistoryTable("__ef_migrations_history", SyllabusDbContext.Schema))
            .AddInterceptors(sp.GetRequiredService<OwnershipSaveChangesInterceptor>()));

        var options = SyllabusOptions.From(cfg);
        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton(sp => SyllabusKeys.From(cfg, sp.GetRequiredService<Tether.SharedKernel.Security.DerivedKeys>()));
        builder.Services.AddSingleton<SyllabusETag>();
        builder.Services.AddSingleton<RateGate>();
        builder.Services.AddSingleton<UploadGate>();
        builder.Services.AddSingleton<JobCancellation>();
        builder.Services.AddSingleton<Tether.SharedKernel.Ai.IAiDocumentScopeProbe, SyllabusScopeProbe>();
        builder.Services.AddSingleton(new KnownNotices(cfg.GetSection("Syllabus:NoticeVersions").Get<string[]>() is { Length: > 0 } v ? v : ["draft-2026-10"]));
        builder.Services.AddSingleton<ISyllabusExtractor, MockRuleExtractor>();
        builder.Services.AddSingleton<ExtractionRunner>();
        builder.Services.AddSingleton<SyllabusMaintenance>();
        builder.Services.AddHostedService<ExtractionWorkerService>();
        builder.Services.AddHostedService<MaintenanceService>();
        builder.Services.AddScoped<SyllabusCore>();
        builder.Services.AddScoped<MockCalendar>();
        builder.Services.AddScoped<ICalendarProvider>(sp => sp.GetRequiredService<MockCalendar>());
        builder.Services.AddScoped<SyllabusService>();
        builder.Services.AddScoped<ItemService>();
        builder.Services.AddScoped<SyncService>();
        // SB-05: course purge cuts off running extraction and AI calls, audits each cascaded syllabus and removes the course's mock events
        builder.Services.AddScoped<Tether.Courses.Contracts.ICourseLifecycleHandler, SyllabusCourseLifecycleHandler>();
        return builder;
    }

    public static IEndpointRouteBuilder MapSyllabusEndpoints(this IEndpointRouteBuilder api)
    {
        var sp = api.ServiceProvider;
        var origins = sp.GetRequiredService<IConfiguration>().GetSection("Security:AllowedOrigins").Get<string[]>() ?? [];
        SyllabusEndpoints.Map(api, sp.GetRequiredService<SyllabusOptions>(), origins);
        return api;
    }

    public static async Task MigrateSyllabusAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<SyllabusDbContext>().Database.MigrateAsync(ct);
    }
}
