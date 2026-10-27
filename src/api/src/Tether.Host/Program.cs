using System.Net;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.RateLimiting;
using Tether.Courses;
using Tether.Host;
using Tether.Identity;
using Tether.Lectures;
using Tether.SharedKernel;
using Tether.SharedKernel.Http;
using Tether.SharedKernel.Persistence;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(o =>
{
    o.Limits.MaxRequestBodySize = 64 * 1024; // SEC-034
    o.AddServerHeader = false;
});

// F-08: these framework categories log the exception object (and so any row values / SQL detail inside it) at Error.
builder.Logging.AddFilter("Microsoft.AspNetCore.Diagnostics.ExceptionHandlerMiddleware", LogLevel.None);
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Update", LogLevel.None);
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.None);
builder.Services.AddSharedKernel();
builder.AddIdentityModule(); // fails startup if dev login is enabled outside Development
builder.AddCoursesModule();
builder.AddLecturesModule();

var allowedOrigins = builder.Configuration.GetSection("Security:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddSingleton(new OriginPolicy(allowedOrigins));

// F-03: X-Forwarded-* is honoured ONLY from explicitly listed proxies. With none configured the middleware is not
// added at all, so a client can never choose its own IP or scheme.
var trustedProxies = builder.Configuration.GetSection("Security:TrustedProxies").Get<string[]>() ?? [];
var trustedNetworks = builder.Configuration.GetSection("Security:TrustedProxyNetworks").Get<string[]>() ?? [];
var useForwardedHeaders = trustedProxies.Length + trustedNetworks.Length > 0;
if (useForwardedHeaders)
{
    builder.Services.Configure<ForwardedHeadersOptions>(o =>
    {
        o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        o.ForwardLimit = 1; // the nearest trusted hop only
        o.KnownProxies.Clear();
        o.KnownIPNetworks.Clear(); // drops the implicit loopback trust
        foreach (var p in trustedProxies) o.KnownProxies.Add(IPAddress.Parse(p));
        foreach (var n in trustedNetworks) o.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(n));
    });
}

// F-03 / SEC-036. Budgets are per minute and configurable (RateLimiting:*).
//  - authenticated callers: per user (ApiPermitPerMinute) plus a stricter bucket for unsafe methods (WritePermitPerMinute)
//  - anonymous callers (incl. health probes): per client IP (AnonymousPermitPerMinute)
//  - "auth" policy on login: per client IP (AuthPermitPerMinute)
var limits = builder.Configuration.GetSection("RateLimiting");
var authPermits = limits.GetValue("AuthPermitPerMinute", 10);
var apiPermits = limits.GetValue("ApiPermitPerMinute", 120);
var writePermits = limits.GetValue("WritePermitPerMinute", 30);
var anonPermits = limits.GetValue("AnonymousPermitPerMinute", 60);
static FixedWindowRateLimiterOptions PerMinute(int permits) =>
    new() { PermitLimit = permits, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 };
static string? UserKey(HttpContext ctx) =>
    ctx.User.Identity?.IsAuthenticated == true ? ctx.User.FindFirstValue(ClaimTypes.NameIdentifier) : null;
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = 429;
    o.GlobalLimiter = PartitionedRateLimiter.CreateChained(
        PartitionedRateLimiter.Create<HttpContext, string>(ctx => UserKey(ctx) is { } uid
            ? RateLimitPartition.GetFixedWindowLimiter("u:" + uid, _ => PerMinute(apiPermits))
            : RateLimitPartition.GetFixedWindowLimiter("ip:" + ClientIpKey.For(ctx.Connection.RemoteIpAddress),
                _ => PerMinute(anonPermits))),
        PartitionedRateLimiter.Create<HttpContext, string>(ctx => UserKey(ctx) is { } uid && !HttpMethods.IsGet(ctx.Request.Method)
            && !HttpMethods.IsHead(ctx.Request.Method) && !HttpMethods.IsOptions(ctx.Request.Method)
            ? RateLimitPartition.GetFixedWindowLimiter("w:" + uid, _ => PerMinute(writePermits))
            : RateLimitPartition.GetNoLimiter("none")));
    o.AddPolicy("auth", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ClientIpKey.For(ctx.Connection.RemoteIpAddress), _ => PerMinute(authPermits)));
    o.OnRejected = async (ctx, ct) =>
    {
        ctx.HttpContext.Response.Headers.RetryAfter = "60";
        await Problems.ForStatus(429).ExecuteAsync(ctx.HttpContext);
    };
});

builder.Services.AddHealthChecks().AddCheck<PostgresReadyCheck>("postgres", tags: ["ready"]);

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    if (string.IsNullOrEmpty(app.Configuration["DataProtection:KeysPath"]))
        app.Logger.LogWarning("DataProtection:KeysPath is not set: cookie keys are ephemeral, so every restart signs everyone out and instances cannot share sessions.");
    if (string.IsNullOrEmpty(app.Configuration["Security:ETagKey"]))
        app.Logger.LogWarning("Security:ETagKey is not set: ETags are random per process and will not match across restarts or instances.");
}

if (useForwardedHeaders) app.UseForwardedHeaders();

// F-06: registered first and applied in OnStarting, so the headers are present on every response, including
// 500 problem responses (the exception handler clears headers when it re-executes) and 429/401 short-circuits.
app.Use((ctx, next) =>
{
    ctx.Response.OnStarting(() =>
    {
        var h = ctx.Response.Headers;
        h.XContentTypeOptions = "nosniff";
        h["Referrer-Policy"] = "no-referrer";
        h["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'";
        h["Permissions-Policy"] = "microphone=(self), camera=(), geolocation=(), payment=()";
        h.CacheControl = "no-store";
        if (!app.Environment.IsDevelopment() && ctx.Request.IsHttps)
            h.StrictTransportSecurity = "max-age=31536000; includeSubDomains";
        return Task.CompletedTask;
    });
    return next();
});

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
            // F-08: exception type and trace id ONLY. Never pass the exception object: DB exceptions embed row values
            // ("Failing row contains ...") and parse errors can echo request content. Framework loggers that would
            // log the exception themselves are silenced via the filters near the top of this file.
            ctx.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Unhandled")
                .LogError("Unhandled exception of type {ExceptionType} for trace {TraceId}",
                    ex?.GetType().FullName ?? "unknown", ctx.TraceIdentifier);
            result = new ApiProblemResult(500, "internal_error", "Something went wrong on our side.");
            break;
    }
    await result.ExecuteAsync(ctx);
}));
app.UseStatusCodePages(ctx => Problems.ForStatus(ctx.HttpContext.Response.StatusCode).ExecuteAsync(ctx.HttpContext));


// SEC-031 / SEC-030: state-changing requests must come from an allow-listed Origin.
app.UseMiddleware<OriginCheckMiddleware>();

app.UseRouting();
app.UseAuthentication(); // before the limiter so it can key on the user
app.UseRateLimiter();
app.UseAuthorization();

app.MapHealthChecks("/health/live", new() { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready", new() { Predicate = r => r.Tags.Contains("ready") }).AllowAnonymous();

var api = app.MapGroup("/api/v1");
api.MapIdentityEndpoints(app.Services);
api.MapCoursesEndpoints();
api.MapLecturesEndpoints();

if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await app.Services.MigrateIdentityAsync();
    await app.Services.MigrateCoursesAsync();
    await app.Services.MigrateLecturesAsync();
    await app.Services.SeedDevUsersAsync(); // no-op unless Development and dev login enabled
}

app.Run();

public partial class Program;
