using Tether.SharedKernel;
using Tether.SharedKernel.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Tether.Docs.Application;

/// <summary>
/// The docs.export.v1 REST surface (docs/API_CONTRACTS_DOCS_EXPORT.md section 3). Evaluation order for every route: (1) authentication by the
/// host's fallback policy, (2) Origin, (3) syntax of the Idempotency-Key and every path id, (4) ownership as 404, (5) the idempotency lookup (done
/// under the lock, see <see cref="DocsExportService"/>), (6) the per-user rate limit (the host limiter, from counters, before any body is read),
/// (7) the body under a deadline and a size cap, (8) validation, (9) state and preconditions. Ids are strings here so a malformed id reaches this
/// code and is a 400, not swallowed by the router as a 404.
/// </summary>
internal static class DocsEndpoints
{
    public static void Map(IEndpointRouteBuilder api, DocsOptions opt, string[] allowedOrigins)
    {
        IEndpointConventionBuilder R(IEndpointConventionBuilder b) => b.WithSeparateRateBucket("docs-read", opt.ReadPermitPerMinute);
        IEndpointConventionBuilder W(IEndpointConventionBuilder b) => b.WithSeparateRateBucket("docs-write", opt.WritePermitPerMinute);
        var origins = allowedOrigins.Select(o => o.TrimEnd('/')).ToArray();

        var g = api.MapGroup("");
        g.AddEndpointFilter(async (ctx, next) =>
        {
            var http = ctx.HttpContext;
            if (!HttpMethods.IsGet(http.Request.Method) && !HttpMethods.IsHead(http.Request.Method) && !HttpMethods.IsOptions(http.Request.Method))
            {
                var h = http.Request.Headers.Origin;
                if (h.Count != 1 || string.IsNullOrEmpty(h[0]) || !origins.Contains(h[0]!, StringComparer.Ordinal)) return DocsProblem.Csrf();
            }
            return await next(ctx);
        });

        // `docs-read` for the safe-to-repeat routes, `docs-write` (10 per minute per user) for confirm and delete: a POST without this
        // metadata would land in the shared write bucket that Quick recap uses (DXD-03).
        R(g.MapPost("/sessions/{id}/docs-export/preview", Preview));
        W(g.MapPost("/sessions/{id}/docs-export", Confirm));
        R(g.MapGet("/sessions/{id}/docs-export", State));
        R(g.MapGet("/demo-docs/{docId}", ReadDoc));
        W(g.MapDelete("/demo-docs/{docId}", DeleteDoc));
    }

    /// <summary>Session ids are canonical lowercase hyphenated GUIDs; any other spelling is a 400 before any lookup (DXD-12).</summary>
    private static bool TrySession(string? raw, out Guid id) => WireId.TryParse(null, raw, out id);

    private static async Task<IResult> Preview(HttpContext http, string id, DocsExportService svc, DocsOptions opt, CancellationToken ct)
    {
        if (!TrySession(id, out var sid)) return DocsProblem.Invalid("id", "not_canonical");
        if (!await svc.OwnsAsync(sid, ct)) return DocsProblem.NotFound();
        var read = await ReadBodyAsync(http, opt, ct);
        if (read.Problem is { } p) return p;
        if (RequestParser.ParsePreview(read.Bytes!, out var req) is { } bad) return bad;
        return await svc.PreviewAsync(sid, req!.NotesId, ct);
    }

    private static async Task<IResult> Confirm(HttpContext http, string id, DocsExportService svc, DocsOptions opt, DemoFaultContext faults, CancellationToken ct)
    {
        // step 3: syntax
        var h = http.Request.Headers["Idempotency-Key"];
        if (h.Count == 0) return DocsProblem.Invalid("Idempotency-Key", "required");
        if (h.Count != 1 || !Hashing.ValidKey(h[0])) return DocsProblem.Invalid("Idempotency-Key");
        var key = h[0]!;
        if (!TrySession(id, out var sid)) return DocsProblem.Invalid("id", "not_canonical");
        // step 4: ownership
        if (!await svc.OwnsAsync(sid, ct)) return DocsProblem.NotFound();
        // steps 6-7: the rate limit already ran in the host's limiter; now the body, under a deadline and a size cap
        var read = await ReadBodyAsync(http, opt, ct);
        if (read.Problem is { } p) return p;
        // step 8: validation
        if (RequestParser.ParseConfirm(read.Bytes!, out var req) is { } bad) return bad;
        // Development-only fault injection: the header is not even read unless the flag is on, so nothing reveals the feature (DXD-07)
        if (opt.FaultsEnabled && http.Request.Headers[DemoFaultContext.Header] is { Count: 1 } fh) faults.Raw = fh[0];
        // steps 5 and 9 happen in the service, under the locks
        return await svc.ConfirmAsync(sid, key, req!, ct);
    }

    private static async Task<IResult> State(string id, DocsExportService svc, CancellationToken ct)
    {
        if (!TrySession(id, out var sid)) return DocsProblem.Invalid("id", "not_canonical");
        return await svc.StateAsync(sid, ct);
    }

    private static async Task<IResult> ReadDoc(string docId, DocsExportService svc, CancellationToken ct)
    {
        if (!WireId.TryParse(Domain.DocsConstants.DocPrefix, docId, out var did)) return DocsProblem.Invalid("docId", "not_canonical");
        return await svc.ReadDocAsync(did, ct);
    }

    private static async Task<IResult> DeleteDoc(string docId, DocsExportService svc, CancellationToken ct)
    {
        if (!WireId.TryParse(Domain.DocsConstants.DocPrefix, docId, out var did)) return DocsProblem.Invalid("docId", "not_canonical");
        return await svc.DeleteDocAsync(did, ct);
    }

    private static async Task<(byte[]? Bytes, IResult? Problem)> ReadBodyAsync(HttpContext http, DocsOptions opt, CancellationToken ct)
    {
        // a data-rate floor (Kestrel only) next to the read deadline that covers every server
        var rate = http.Features.Get<Microsoft.AspNetCore.Server.Kestrel.Core.Features.IHttpMinRequestBodyDataRateFeature>();
        if (rate is not null) rate.MinDataRate = new Microsoft.AspNetCore.Server.Kestrel.Core.MinDataRate(bytesPerSecond: opt.BodyMinBytesPerSecond, gracePeriod: TimeSpan.FromSeconds(5));
        var (bytes, failure) = await JsonBody.ReadBytesAsync(http, opt.BodyBytes, TimeSpan.FromSeconds(opt.BodyReadSeconds), ct);
        return failure switch
        {
            JsonBody.Failure.TooLarge => (null, DocsProblem.TooLarge()),
            JsonBody.Failure.Unsupported => (null, DocsProblem.UnsupportedMediaType()),
            JsonBody.Failure.TimedOut => (null, DocsProblem.InvalidBare()),     // a slow body: the same answer as an invalid one (the Syllabus precedent)
            _ => (bytes, null),
        };
    }
}
