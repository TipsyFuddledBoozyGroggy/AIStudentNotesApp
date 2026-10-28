using Tether.SharedKernel.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace Tether.Host;

internal sealed record OriginPolicy(string[] Allowed)
{
    public bool IsAllowed(string origin) =>
        Allowed.Any(a => string.Equals(a.TrimEnd('/'), origin.TrimEnd('/'), StringComparison.OrdinalIgnoreCase));
    /// <summary>Exact, ordinal match for WebSocket upgrades (C-03/C-07): no case folding, no trailing slash tolerance on the header.</summary>
    public bool IsExactlyAllowed(string origin) => Allowed.Any(a => string.Equals(a.TrimEnd('/'), origin, StringComparison.Ordinal));
}

/// <summary>
/// CSRF defense for cookie auth: every unsafe request (POST/PUT/PATCH/DELETE) must carry an Origin header
/// that is on the allow-list. Combined with SameSite=Lax cookies and no CORS policy. Fails closed when the
/// allow-list is empty. Applies to anonymous endpoints too (login CSRF).
/// </summary>
internal sealed class OriginCheckMiddleware(RequestDelegate next, OriginPolicy policy)
{
    public async Task InvokeAsync(HttpContext ctx)
    {
        if (!HttpMethods.IsGet(ctx.Request.Method) && !HttpMethods.IsHead(ctx.Request.Method)
            && !HttpMethods.IsOptions(ctx.Request.Method))
        {
            var origin = ctx.Request.Headers.Origin.ToString();
            if (string.IsNullOrEmpty(origin) || !policy.IsAllowed(origin))
            {
                await Problems.CsrfFailed().ExecuteAsync(ctx);
                return;
            }
        }
        await next(ctx);
    }
}

internal sealed class PostgresReadyCheck(IConfiguration config) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            await using var conn = new NpgsqlConnection(config.GetConnectionString("Default"));
            await conn.OpenAsync(ct);
            await using var cmd = new NpgsqlCommand("select 1", conn);
            await cmd.ExecuteScalarAsync(ct);
            return HealthCheckResult.Healthy();
        }
        catch (Exception)
        {
            return HealthCheckResult.Unhealthy("database unavailable"); // no connection string or details
        }
    }
}

/// <summary>
/// F-04 / ADR-017 / C-07: the hub (negotiate AND the WebSocket upgrade) and the audio ingest socket require an allow-listed
/// Origin on every method, including GET. A missing, "null" or foreign Origin is answered with HTTP 403 before any upgrade.
/// Runs before authentication so a cross-site page learns nothing about the session.
/// </summary>
internal sealed class RealtimeOriginMiddleware(RequestDelegate next, OriginPolicy policy)
{
    public async Task InvokeAsync(HttpContext ctx)
    {
        var path = ctx.Request.Path;
        if (path.StartsWithSegments("/hubs") || path.StartsWithSegments("/ingest"))
        {
            var origin = ctx.Request.Headers.Origin.ToString();
            if (string.IsNullOrEmpty(origin) || origin == "null" || ctx.Request.Headers.Origin.Count != 1 || !policy.IsExactlyAllowed(origin))
            {
                await Problems.CsrfFailed().ExecuteAsync(ctx);
                return;
            }
        }
        await next(ctx);
    }
}
