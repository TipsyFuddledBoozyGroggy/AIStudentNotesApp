using System.Globalization;
using Tether.Courses.Contracts;
using Tether.SharedKernel;
using Tether.SharedKernel.Http;
using Tether.Syllabus.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Tether.Syllabus.Application;

/// <summary>
/// The syllabus.v1 REST surface (docs/API_CONTRACTS_P2.md section 5). Evaluation order for every route: (1) authentication by the host's
/// fallback policy, (2) Origin, (3) syntax of Idempotency-Key, If-Match and every path/query/body id, (4) ownership chain as 404,
/// (5) idempotency lookup, (6) limits and quotas, (7) body, (8) validation, (9) state. Ids are strings here on purpose: a malformed id must
/// reach this code and be a 400, not be swallowed by the router as a 404.
/// </summary>
internal static class SyllabusEndpoints
{
    private const int DefaultLimit = 50, MaxLimit = 200;

    public static void Map(IEndpointRouteBuilder api, SyllabusOptions opt, string[] allowedOrigins)
    {
        IEndpointConventionBuilder R(IEndpointConventionBuilder b) => b.WithSeparateRateBucket("syllabus-read", opt.ReadPermitPerMinute);
        IEndpointConventionBuilder W(IEndpointConventionBuilder b) => b.WithSeparateRateBucket("syllabus-write", opt.WritePermitPerMinute);
        var origins = allowedOrigins.Select(o => o.TrimEnd('/')).ToArray();

        var g = api.MapGroup("");
        g.AddEndpointFilter(async (ctx, next) =>
        {
            var http = ctx.HttpContext;
            if (!HttpMethods.IsGet(http.Request.Method) && !HttpMethods.IsHead(http.Request.Method) && !HttpMethods.IsOptions(http.Request.Method))
            {
                // exact ordinal match, a missing Origin is rejected (multipart is CORS-safelisted, so no other header is a defense)
                var h = http.Request.Headers.Origin;
                if (h.Count != 1 || string.IsNullOrEmpty(h[0]) || !origins.Contains(h[0]!, StringComparer.Ordinal)) return SyllabusProblem.Csrf();
            }
            return await next(ctx);
        });

        W(g.MapPost("/courses/{courseId}/syllabi", Create));
        R(g.MapGet("/courses/{courseId}/syllabi", ListSyllabi));
        R(g.MapGet("/courses/{courseId}/demo-calendar", DemoCalendar));
        R(g.MapGet("/syllabi/{id}", GetSyllabus));
        W(g.MapDelete("/syllabi/{id}", DeleteSyllabus));
        R(g.MapGet("/syllabi/{id}/items", ListItems));
        W(g.MapPost("/syllabi/{id}/items", CreateItem));
        R(g.MapGet("/syllabi/{id}/items/{itemId}", GetItem));
        W(g.MapPatch("/syllabi/{id}/items/{itemId}", PatchItem));
        W(g.MapDelete("/syllabi/{id}/items/{itemId}", DeleteItem));
        W(g.MapPost("/syllabi/{id}/items/{itemId}/decision", Decide));
        W(g.MapPost("/syllabi/{id}/decisions", Batch));
        W(g.MapPost("/syllabi/{id}/calendar-sync", Sync));
        W(g.MapPost("/syllabi/{id}/calendar-sync/remove", Remove));
        R(g.MapGet("/syllabi/{id}/unclaimed-dates", Unclaimed));
        R(g.MapGet("/syllabi/{id}/export", Export));
    }

    // ---- step 3 helpers ----

    private static bool TryId(string? prefix, string? raw, out Guid id) => WireId.TryParse(prefix, raw, out id);

    private static IResult? IdemKey(HttpContext http, bool required, out string? key)
    {
        key = null;
        var h = http.Request.Headers["Idempotency-Key"];
        if (h.Count == 0) return required ? SyllabusProblem.Invalid("Idempotency-Key", "required") : null;
        if (h.Count != 1 || !SyllabusService.ValidKey(h[0])) return SyllabusProblem.Invalid("Idempotency-Key");
        key = h[0];
        return null;
    }

    private static IResult? IfMatch(HttpContext http, out string value)
    {
        value = "";
        var h = http.Request.Headers.IfMatch;
        if (h.Count == 0 || string.IsNullOrWhiteSpace(h[0])) return SyllabusProblem.PreconditionRequired();
        if (h.Count != 1) return SyllabusProblem.Invalid("If-Match");
        value = h[0]!;
        return null;
    }

    private static IResult? Limit(string? raw, out int limit)
    {
        limit = DefaultLimit;
        if (raw is null) return null;
        if (!int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out limit) || limit < 1 || limit > MaxLimit) return SyllabusProblem.Invalid("limit", "out_of_range");
        return null;
    }

    private static IResult? NoQuery(HttpContext http) => http.Request.Query.Count > 0 ? SyllabusProblem.Invalid("query", "not_allowed") : null;

    // ---- routes ----

    private static async Task<IResult> Create(HttpContext http, string courseId, SyllabusService svc, CancellationToken ct)
    {
        if (IdemKey(http, required: true, out var key) is { } e1) return e1;
        if (!TryId(null, courseId, out var cid)) return SyllabusProblem.Invalid("courseId", "not_canonical");
        var ctype = http.Request.ContentType ?? "";
        var multipart = ctype.StartsWith("multipart/form-data", StringComparison.OrdinalIgnoreCase);
        if (!multipart && !ctype.StartsWith("application/json", StringComparison.OrdinalIgnoreCase)) return SyllabusProblem.Unsupported();
        if (multipart)
        {
            // Kestrel's global 64 KB cap does not fit an upload: lift it for this request only, never for the whole app.
            var f = http.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>();
            if (f is { IsReadOnly: false }) f.MaxRequestBodySize = 10L * 1024 * 1024 + 64 * 1024;
            var rate = http.Features.Get<Microsoft.AspNetCore.Server.Kestrel.Core.Features.IHttpMinRequestBodyDataRateFeature>();
            if (rate is not null) rate.MinDataRate = new Microsoft.AspNetCore.Server.Kestrel.Core.MinDataRate(bytesPerSecond: 200 * 1024, gracePeriod: TimeSpan.FromSeconds(10));
        }
        else
        {
            var f = http.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>();
            if (f is { IsReadOnly: false }) f.MaxRequestBodySize = 1024 * 1024 + 1024;
        }
        return await svc.CreateAsync(http, key!, cid, multipart, ct);
    }

    private static async Task<IResult> ListSyllabi(HttpContext http, string courseId, string? cursor, string? limit, SyllabusService svc, CancellationToken ct)
    {
        if (!TryId(null, courseId, out var cid)) return SyllabusProblem.Invalid("courseId", "not_canonical");
        if (Limit(limit, out var l) is { } e) return e;
        return await svc.ListAsync(cid, cursor, l, ct);
    }

    private static async Task<IResult> GetSyllabus(HttpContext http, string id, SyllabusService svc, CancellationToken ct)
    {
        if (!TryId(Ids2.Syllabus, id, out var sid)) return SyllabusProblem.Invalid("id", "not_canonical");
        return await svc.GetAsync(http, sid, ct);
    }

    private static async Task<IResult> DeleteSyllabus(HttpContext http, string id, SyllabusService svc, CancellationToken ct)
    {
        if (!TryId(Ids2.Syllabus, id, out var sid)) return SyllabusProblem.Invalid("id", "not_canonical");
        if (NoQuery(http) is { } e) return e;
        return await svc.DeleteAsync(sid, ct);
    }

    private static async Task<IResult> ListItems(HttpContext http, string id, string? cursor, string? limit, string? decision, string? kind, string? needsDate, ItemService svc, CancellationToken ct)
    {
        if (!TryId(Ids2.Syllabus, id, out var sid)) return SyllabusProblem.Invalid("id", "not_canonical");
        if (Limit(limit, out var l) is { } e) return e;
        return await svc.ListAsync(sid, cursor, l, decision, kind, needsDate, ct);
    }

    private static async Task<IResult> CreateItem(HttpContext http, string id, ItemService svc, CancellationToken ct)
    {
        if (IdemKey(http, required: false, out var key) is { } e1) return e1;
        if (!TryId(Ids2.Syllabus, id, out var sid)) return SyllabusProblem.Invalid("id", "not_canonical");
        return await svc.CreateAsync(http, sid, key, ct);
    }

    private static async Task<IResult> GetItem(HttpContext http, string id, string itemId, ItemService svc, CancellationToken ct)
    {
        if (!TryId(Ids2.Syllabus, id, out var sid) || !TryId(Ids2.Item, itemId, out var iid)) return SyllabusProblem.Invalid("id", "not_canonical");
        return await svc.GetAsync(http, sid, iid, ct);
    }

    private static async Task<IResult> PatchItem(HttpContext http, string id, string itemId, ItemService svc, CancellationToken ct)
    {
        if (IfMatch(http, out var im) is { } e0) return e0;
        if (!TryId(Ids2.Syllabus, id, out var sid) || !TryId(Ids2.Item, itemId, out var iid)) return SyllabusProblem.Invalid("id", "not_canonical");
        return await svc.PatchAsync(http, sid, iid, im, ct);
    }

    private static async Task<IResult> DeleteItem(HttpContext http, string id, string itemId, ItemService svc, CancellationToken ct)
    {
        if (IfMatch(http, out var im) is { } e0) return e0;
        if (!TryId(Ids2.Syllabus, id, out var sid) || !TryId(Ids2.Item, itemId, out var iid)) return SyllabusProblem.Invalid("id", "not_canonical");
        return await svc.DeleteAsync(sid, iid, im, ct);
    }

    private static async Task<IResult> Decide(HttpContext http, string id, string itemId, ItemService svc, CancellationToken ct)
    {
        if (IfMatch(http, out var im) is { } e0) return e0;
        if (!TryId(Ids2.Syllabus, id, out var sid) || !TryId(Ids2.Item, itemId, out var iid)) return SyllabusProblem.Invalid("id", "not_canonical");
        return await svc.DecideAsync(http, sid, iid, im, ct);
    }

    private static async Task<IResult> Batch(HttpContext http, string id, ItemService svc, RateGate rates, SyllabusOptions opt, ICurrentUser user, CancellationToken ct)
    {
        if (!TryId(Ids2.Syllabus, id, out var sid)) return SyllabusProblem.Invalid("id", "not_canonical");
        var (bytes, failure) = await JsonBody.ReadBytesAsync(http, opt.JsonBodyBytes, ct);
        if (failure == JsonBody.Failure.TooLarge) return SyllabusProblem.TooLarge();
        if (failure == JsonBody.Failure.Unsupported) return SyllabusProblem.Unsupported();
        if (!JsonBody.TryBind<BatchRequest>(bytes!, out var req) || req is null) return SyllabusProblem.InvalidBare();
        if (req.Action is not ("approve" or "skip" or "undecide")) return SyllabusProblem.Invalid("action");
        if (req.ItemEtags.Count is < 1 or > 100) return SyllabusProblem.Invalid("itemEtags", "out_of_range");
        var map = new Dictionary<Guid, string>();
        foreach (var (k, v) in req.ItemEtags)
        {
            // canonical form only, in a body key too: an alias can never reach a lookup or become a second key for one item
            if (!TryId(Ids2.Item, k, out var iid)) return SyllabusProblem.Invalid("itemEtags", "not_canonical");
            if (string.IsNullOrWhiteSpace(v) || v.Length > 128) return SyllabusProblem.Invalid("itemEtags");
            if (!map.TryAdd(iid, v)) return SyllabusProblem.Invalid("itemEtags", "duplicate");
        }
        if (!rates.TryAcquire("batch:usr:" + user.UserId, opt.BatchPerUserPerMinute)) return SyllabusProblem.RateLimited();
        return await svc.BatchAsync(sid, req, map, ct);
    }

    private static async Task<IResult> Sync(HttpContext http, string id, SyncService svc, SyllabusOptions opt, CancellationToken ct)
    {
        if (IdemKey(http, required: true, out var key) is { } e1) return e1;
        if (!TryId(Ids2.Syllabus, id, out var sid)) return SyllabusProblem.Invalid("id", "not_canonical");
        var (bytes, failure) = await JsonBody.ReadBytesAsync(http, opt.JsonBodyBytes, ct);
        if (failure == JsonBody.Failure.TooLarge) return SyllabusProblem.TooLarge();
        if (failure == JsonBody.Failure.Unsupported) return SyllabusProblem.Unsupported();
        if (!JsonBody.TryBind<SyncRequest>(bytes!, out var req) || req is null) return SyllabusProblem.InvalidBare();
        if (req.ItemRevisions.Count is < 1 or > 100) return SyllabusProblem.Invalid("itemRevisions", "out_of_range");
        var revs = new Dictionary<Guid, int>();
        foreach (var (k, v) in req.ItemRevisions)
        {
            if (!TryId(Ids2.Item, k, out var iid)) return SyllabusProblem.Invalid("itemRevisions", "not_canonical");
            if (v < 1) return SyllabusProblem.Invalid("itemRevisions", "out_of_range");
            if (!revs.TryAdd(iid, v)) return SyllabusProblem.Invalid("itemRevisions", "duplicate");
        }
        return await svc.SyncAsync(http, sid, key!, req, revs, ct);
    }

    private static async Task<IResult> Remove(HttpContext http, string id, SyncService svc, SyllabusOptions opt, CancellationToken ct)
    {
        if (!TryId(Ids2.Syllabus, id, out var sid)) return SyllabusProblem.Invalid("id", "not_canonical");
        var (bytes, failure) = await JsonBody.ReadBytesAsync(http, opt.JsonBodyBytes, ct);
        if (failure == JsonBody.Failure.TooLarge) return SyllabusProblem.TooLarge();
        if (failure == JsonBody.Failure.Unsupported) return SyllabusProblem.Unsupported();
        if (!JsonBody.TryBind<RemoveRequest>(bytes!, out var req) || req is null) return SyllabusProblem.InvalidBare();
        if (req.ItemIds.Count is < 1 or > 100) return SyllabusProblem.Invalid("itemIds", "out_of_range");
        var ids = new List<Guid>();
        foreach (var raw in req.ItemIds)
        {
            if (!TryId(Ids2.Item, raw, out var iid)) return SyllabusProblem.Invalid("itemIds", "not_canonical");
            if (ids.Contains(iid)) return SyllabusProblem.Invalid("itemIds", "duplicate");
            ids.Add(iid);
        }
        return await svc.RemoveAsync(sid, req, ids, ct);
    }

    private static async Task<IResult> DemoCalendar(HttpContext http, string courseId, string? from, string? to, string? cursor, string? limit, SyncService svc,
        ICourseOwnership courses, CancellationToken ct)
    {
        if (!TryId(null, courseId, out var cid)) return SyllabusProblem.Invalid("courseId", "not_canonical");
        if (Limit(limit, out var l) is { } e) return e;
        return await svc.DemoCalendarAsync(cid, from, to, cursor, l, courses, ct);
    }

    private static async Task<IResult> Unclaimed(HttpContext http, string id, string? cursor, string? limit, SyllabusCore core, CancellationToken ct)
    {
        if (!TryId(Ids2.Syllabus, id, out var sid)) return SyllabusProblem.Invalid("id", "not_canonical");
        if (Limit(limit, out var l) is { } e) return e;
        if (await core.FindSyllabusAsync(sid, ct) is null) return SyllabusProblem.NotFound();
        int cs = -1; Guid cid = default;
        if (cursor is not null && !TryUnclaimedCursor(cursor, out cs, out cid)) return SyllabusProblem.Invalid("cursor");
        var q = core.Db.Unclaimed.AsNoTracking().Where(u => u.SyllabusId == sid);
        if (cursor is not null) q = q.Where(u => u.SnippetStart > cs || (u.SnippetStart == cs && u.Id.CompareTo(cid) > 0));
        var rows = await q.OrderBy(u => u.SnippetStart).ThenBy(u => u.Id).Take(l + 1).ToListAsync(ct);
        var more = rows.Count > l;
        if (more) rows.RemoveAt(rows.Count - 1);
        var items = rows.Select(ToUnclaimed).ToList();
        var next = more ? Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(System.Text.Encoding.UTF8.GetBytes(rows[^1].SnippetStart.ToString(CultureInfo.InvariantCulture) + "|" + rows[^1].Id.ToString("N"))) : null;
        return Results.Json(new Page<UnclaimedDto>(items, next), JsonBody.Out);
    }

    private static bool TryUnclaimedCursor(string s, out int start, out Guid id)
    {
        start = 0; id = default;
        try
        {
            var parts = System.Text.Encoding.UTF8.GetString(Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlDecode(s)).Split('|');
            return parts.Length == 2 && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out start) && Guid.TryParseExact(parts[1], "N", out id);
        }
        catch (FormatException) { return false; }
    }

    private static UnclaimedDto ToUnclaimed(UnclaimedDate u) =>
        new(WireId.Format(Ids2.Unclaimed, u.Id), Mapping.Snippet(u.SnippetStart, u.SnippetEnd, u.SnippetPage, u.SnippetPrecision, u.SnippetText, u.ContextText, u.ContextStart, u.ContextEnd)!);

    private static async Task<IResult> Export(HttpContext http, string id, SyllabusCore core, RateGate rates, SyllabusOptions opt, CancellationToken ct)
    {
        if (!TryId(Ids2.Syllabus, id, out var sid)) return SyllabusProblem.Invalid("id", "not_canonical");
        var s = await core.FindSyllabusAsync(sid, ct);
        if (s is null) return SyllabusProblem.NotFound();
        if (!rates.TryAcquire("export:usr:" + core.Uid, opt.ExportPerUserPerMinute)) return SyllabusProblem.RateLimited();
        var items = await core.Db.Items.AsNoTracking().Where(i => i.SyllabusId == sid).OrderBy(i => i.SortDate).ThenBy(i => i.Id).Take(opt.MaxItems).ToListAsync(ct);
        var unclaimed = await core.Db.Unclaimed.AsNoTracking().Where(u => u.SyllabusId == sid).OrderBy(u => u.SnippetStart).ThenBy(u => u.Id).Take(200).ToListAsync(ct);
        var events = await core.Db.Events.AsNoTracking().Where(e => e.SyllabusId == sid).OrderBy(e => e.StartDate).ThenBy(e => e.Id).Take(opt.MaxEventsPerSyllabus).ToListAsync(ct);
        var now = core.Now();
        await core.AuditNoisyAsync(sid, "exported", now, items.Count, ct);
        await core.Db.SaveChangesAsync(ct);
        var h = http.Response.Headers;
        h.CacheControl = "no-store"; h.XContentTypeOptions = "nosniff"; h.ContentDisposition = "attachment; filename=\"syllabus-export.json\"";
        var dto = new ExportDto("syllabus.v1", now, await core.RepresentAsync(s, ct), await core.RepresentItemsAsync(items, ct), unclaimed.Select(ToUnclaimed).ToList(), events.Select(e => e.ToDto()).ToList());
        return Results.Json(dto, JsonBody.Out);
    }
}
