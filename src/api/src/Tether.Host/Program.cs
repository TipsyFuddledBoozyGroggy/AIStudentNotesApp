using System.Net;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.RateLimiting;
using Tether.Ai;
using Tether.Courses;
using Tether.Host;
using Tether.Identity;
using Tether.Lectures;
using Tether.Syllabus;
using Tether.Docs;
using Tether.SharedKernel;
using Tether.SharedKernel.Http;
using Tether.SharedKernel.Persistence;
using Tether.SharedKernel.Security;
using Tether.Transcript;
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

// F-08 / B1-02: framework categories that can carry student data are switched off IN CODE, so an operator raising
// Logging:LogLevel:* (or an environment variable) can never turn them back on. Code filters are added after configuration
// rules and win on equal specificity; the log canary (LogPipelineCanaryTests) forces every category to Trace and proves it.
//  - ExceptionHandlerMiddleware, EF Update: log the exception object (row values, SQL detail inside it) at Error.
//  - EF Database.Command, EF Query, Npgsql.Command: SQL text and, if ever enabled, parameter values / expression constants.
//  - Hosting.Diagnostics: "Request starting ... GET /path?query" carries the full URL, so a token or text in a query string.
//  - Routing / Http logging: Debug records echo the raw request path of unmatched (attacker-chosen) routes.
//  - Npgsql.Exception / Npgsql.Connection: with "Include Error Detail" the PostgresException detail echoes the failing row.
// EF sensitive-data logging and detailed errors must never be switched on (guarded by a source-scan architecture test).
foreach (var category in new[]
{
    "Microsoft.AspNetCore.Diagnostics.ExceptionHandlerMiddleware", "Microsoft.AspNetCore.Hosting.Diagnostics",
    "Microsoft.EntityFrameworkCore.Update", "Microsoft.EntityFrameworkCore.Database.Command", "Microsoft.EntityFrameworkCore.Query",
    "Npgsql.Command", "Npgsql.Exception", "Npgsql.Connection",
})
    builder.Logging.AddFilter(category, LogLevel.None);
builder.Logging.AddFilter("Microsoft.AspNetCore.Routing", LogLevel.Warning);
builder.Logging.AddFilter("Npgsql", LogLevel.Warning);
builder.Services.AddSharedKernel();
builder.AddRootSecret(); // B1-11: fails startup outside Development without a valid Security:RootSecret; before any module that derives keys
builder.AddIdentityModule(); // fails startup if dev login is enabled outside Development
builder.AddCoursesModule();
builder.AddLecturesModule();
builder.AddTranscriptModule();
builder.AddAiModule(); // mock provider only; refuses to start with anything else
builder.AddSyllabusModule(); // mock extractor and mock calendar only; refuses to start with anything else
builder.AddDocsModule(); // Demo Doc export (mock only); refuses to start with any other provider

var allowedOrigins = builder.Configuration.GetSection("Security:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddSingleton(new OriginPolicy(allowedOrigins));

// F-03: X-Forwarded-* is honoured ONLY from explicitly listed proxies. With none configured the middleware is not
// added at all, so a client can never choose its own IP or scheme.
var trustedProxies = builder.Configuration.GetSection("Security:TrustedProxies").Get<string[]>() ?? [];
var trustedNetworks = builder.Configuration.GetSection("Security:TrustedProxyNetworks").Get<string[]>() ?? [];
var (parsedProxies, parsedNetworks) = EdgeConfig.ParseTrustedProxies(trustedProxies, trustedNetworks); // M1R-N01: refuses 0.0.0.0/0 and friends
var useForwardedHeaders = parsedProxies.Length + parsedNetworks.Length > 0;
EdgeConfig.EnsureAllowedHosts(builder.Configuration["AllowedHosts"], builder.Environment.IsDevelopment()); // M1R-N02
if (useForwardedHeaders)
{
    builder.Services.Configure<ForwardedHeadersOptions>(o =>
    {
        o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        o.ForwardLimit = 1; // the nearest trusted hop only
        o.KnownProxies.Clear();
        o.KnownIPNetworks.Clear(); // drops the implicit loopback trust
        foreach (var p in parsedProxies) o.KnownProxies.Add(p);
        foreach (var n in parsedNetworks) o.KnownIPNetworks.Add(n);
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
// N-03: Pause and Stop (consent reversal, ADR-019b) are exempt from the budgets above but not from every limit. They get their own
// generous per-user bucket: far more than a student ever needs, so write/api exhaustion can never lock them out, yet finite.
var reversalPermits = limits.GetValue("ConsentReversalPermitPerMinute", 600);
// N-05: every permit must be positive, and anything above the shipped default outside Development is logged at startup.
var permitDefaults = new (string Key, int Value, int Default)[]
{
    ("AuthPermitPerMinute", authPermits, 10), ("ApiPermitPerMinute", apiPermits, 120), ("WritePermitPerMinute", writePermits, 30),
    ("AnonymousPermitPerMinute", anonPermits, 60), ("ConsentReversalPermitPerMinute", reversalPermits, 600),
    ("AudioTicketPermitPerMinute", limits.GetValue("AudioTicketPermitPerMinute", 6), 6),
};
foreach (var p in permitDefaults)
    if (p.Value <= 0) throw new InvalidOperationException($"RateLimiting:{p.Key} must be a positive number (was {p.Value}).");
static FixedWindowRateLimiterOptions PerMinute(int permits) =>
    new() { PermitLimit = permits, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 };
// SignalR negotiate is a POST that changes nothing. Reconnect attempts (two per page) must not spend the write budget, or a
// network blip would leave the student's own Pause/Stop/Resume answered 429. It still counts against the general per-user
// bucket, the hub's per-user connection cap and its own Origin and cookie checks.
static bool IsHubNegotiate(HttpContext ctx) =>
    ctx.Request.Path.StartsWithSegments("/hubs") && ctx.Request.Path.Value!.EndsWith("/negotiate", StringComparison.OrdinalIgnoreCase);
// ADR-019b: consent must stay reversible, so a signed-in user's Pause and Stop are never answered 429 by the general or write
// budgets (the E2E flake: a Stop refused with 429 left the lecture "Not recording" but not stopped). The routes carry
// ConsentReversalMetadata (SessionEndpoints); they still need the session cookie and a matching Origin and are counted in the
// separate per-user "rev:" bucket (ConsentReversalPermitPerMinute), so the exemption is wide but not unlimited (N-03).
static bool IsConsentReversal(HttpContext ctx) => ctx.IsConsentReversal();
static string? UserKey(HttpContext ctx) =>
    ctx.User.Identity?.IsAuthenticated == true ? ctx.User.FindFirstValue(ClaimTypes.NameIdentifier) : null;
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = 429;
    o.GlobalLimiter = PartitionedRateLimiter.CreateChained(
        PartitionedRateLimiter.Create<HttpContext, string>(ctx => UserKey(ctx) is { } uid
            ? (ctx.SeparateRateBucket() is { } sep
                // API_CONTRACTS_P2 section 11 (R2-14): syllabus routes have their own per-user buckets, never the shared ones.
                ? RateLimitPartition.GetFixedWindowLimiter("sx:" + sep.Bucket + ":" + uid, _ => PerMinute(sep.PermitsPerMinute))
                : IsConsentReversal(ctx)
                    ? RateLimitPartition.GetFixedWindowLimiter("rev:" + uid, _ => PerMinute(reversalPermits))
                    : RateLimitPartition.GetFixedWindowLimiter("u:" + uid, _ => PerMinute(apiPermits)))
            : RateLimitPartition.GetFixedWindowLimiter("ip:" + ClientIpKey.For(ctx.Connection.RemoteIpAddress),
                _ => PerMinute(anonPermits))),
        PartitionedRateLimiter.Create<HttpContext, string>(ctx => UserKey(ctx) is { } uid && !HttpMethods.IsGet(ctx.Request.Method)
            && !HttpMethods.IsHead(ctx.Request.Method) && !HttpMethods.IsOptions(ctx.Request.Method) && !IsHubNegotiate(ctx) && !IsConsentReversal(ctx) && ctx.SeparateRateBucket() is null
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
    var raised = permitDefaults.Where(p => p.Value > p.Default).Select(p => $"RateLimiting:{p.Key}={p.Value} (default {p.Default})").ToList();
    if (raised.Count > 0)
        app.Logger.LogWarning("RateLimiting overrides above the shipped defaults outside Development: {Overrides}. The raised values belong in the dev overlay only.", string.Join(", ", raised));
    if (string.IsNullOrEmpty(app.Configuration["DataProtection:KeysPath"]))
        app.Logger.LogWarning("DataProtection:KeysPath is not set: cookie keys are ephemeral, so every restart signs everyone out and instances cannot share sessions.");
}
foreach (var warning in app.Services.GetRequiredService<Tether.SharedKernel.Security.DerivedKeys>().Warnings)
    app.Logger.LogWarning("{Warning}", warning);

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
        // M1R-N02: not conditional on Request.IsHttps. Behind a TLS terminator the app sees http, and the scheme is only as trustworthy as the
        // forwarded-header config. Browsers ignore HSTS received over plain http, so sending it always is harmless and cannot be switched off by a client.
        if (!app.Environment.IsDevelopment())
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
app.UseMiddleware<RealtimeOriginMiddleware>(); // F-04: GET upgrades of the hub and audio socket too
app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(30) });

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
api.MapTranscriptEndpoints();
api.MapAiEndpoints();
api.MapSyllabusEndpoints();
api.MapDocsEndpoints();
app.MapRealtimeEndpoints();

if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await app.Services.MigrateIdentityAsync();
    await app.Services.MigrateCoursesAsync();
    await app.Services.MigrateLecturesAsync();
    await app.Services.MigrateTranscriptAsync(); // after Lectures: FKs to lectures.lecture_sessions
    await app.Services.MigrateAiAsync(); // after Lectures and Identity: FKs to lectures.lecture_sessions and identity.users
    await app.Services.MigrateSyllabusAsync(); // after Identity and Courses: FKs to courses.courses and identity.users
    await app.Services.MigrateDocsAsync(); // after Identity and Lectures: FKs to lectures.lecture_sessions and identity.users
    await app.Services.SeedDevUsersAsync(); // no-op unless Development and dev login enabled
}

app.Run();

public partial class Program;
