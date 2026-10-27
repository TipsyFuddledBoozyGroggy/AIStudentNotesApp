using System.Text.Json;
using System.Threading.RateLimiting;
using CollegeCompanion.Courses;
using CollegeCompanion.Host;
using CollegeCompanion.Identity;
using CollegeCompanion.SharedKernel;
using CollegeCompanion.SharedKernel.Http;
using CollegeCompanion.SharedKernel.Persistence;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 64 * 1024); // SEC-034

builder.Services.AddSharedKernel();
builder.AddIdentityModule(); // fails startup if dev login is enabled outside Development
builder.AddCoursesModule();

var allowedOrigins = builder.Configuration.GetSection("Security:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddSingleton(new OriginPolicy(allowedOrigins));

var authPermits = builder.Configuration.GetValue("RateLimiting:AuthPermitPerMinute", 10);
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = 429;
    o.AddPolicy("auth", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = authPermits, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    o.OnRejected = async (ctx, ct) =>
    {
        ctx.HttpContext.Response.Headers.RetryAfter = "60";
        await Problems.ForStatus(429).ExecuteAsync(ctx.HttpContext);
    };
});

builder.Services.AddHealthChecks().AddCheck<PostgresReadyCheck>("postgres", tags: ["ready"]);

var app = builder.Build();

app.UseExceptionHandler(errorApp => errorApp.Run(async ctx =>
{
    var ex = ctx.Features.Get<IExceptionHandlerFeature>()?.Error;
    IResult result;
    switch (ex)
    {
        case BadHttpRequestException b:
            result = b.StatusCode == 413 ? Problems.ForStatus(413) : Problems.ForStatus(400);
            break;
        case JsonException:
            result = Problems.ForStatus(400);
            break;
        case DbUpdateConcurrencyException:
            result = Problems.ConcurrencyConflict();
            break;
        default:
            // Log the exception type and trace id only; the response never carries details (SEC-029).
            ctx.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Unhandled")
                .LogError(ex, "Unhandled exception for trace {TraceId}", ctx.TraceIdentifier);
            result = new ApiProblemResult(500, "internal_error", "Something went wrong on our side.");
            break;
    }
    await result.ExecuteAsync(ctx);
}));
app.UseStatusCodePages(ctx => Problems.ForStatus(ctx.HttpContext.Response.StatusCode).ExecuteAsync(ctx.HttpContext));

app.Use(async (ctx, next) =>
{
    var h = ctx.Response.Headers;
    h.XContentTypeOptions = "nosniff";
    h["Referrer-Policy"] = "no-referrer";
    h["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'";
    h.CacheControl = "no-store";
    await next();
});

// SEC-031 / SEC-030: state-changing requests must come from an allow-listed Origin.
app.UseMiddleware<OriginCheckMiddleware>();

app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health/live", new() { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready", new() { Predicate = r => r.Tags.Contains("ready") }).AllowAnonymous();

var api = app.MapGroup("/api/v1");
api.MapIdentityEndpoints(app.Services);
api.MapCoursesEndpoints();

if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await app.Services.MigrateIdentityAsync();
    await app.Services.MigrateCoursesAsync();
    await app.Services.SeedDevUsersAsync(); // no-op unless Development and dev login enabled
}

app.Run();

public partial class Program;
