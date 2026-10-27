using Tether.SharedKernel.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Tether.Lectures.Application;

internal static class SessionEndpoints
{
    public const string AudioTicketPolicy = "audio-ticket";
    public const int DefaultLimit = 25;
    public const int MaxLimit = 100;           // server maximum for list endpoints (C-26)
    public const int DefaultTranscriptLimit = 100;
    public const int MaxTranscriptLimit = 500; // replay cap (section 10, C-27)

    public static void Map(IEndpointRouteBuilder api)
    {
        // All routes are authenticated by the fallback policy (SEC-001).
        api.MapPost("/courses/{courseId:guid}/sessions", Create);
        api.MapGet("/courses/{courseId:guid}/sessions", List);

        var g = api.MapGroup("/sessions");
        g.MapGet("/{id:guid}", Get);
        g.MapPost("/{id:guid}/pause", (Guid id, SessionService svc, SessionETag e, CancellationToken ct) => DoTransition(id, Transition.Pause, svc, e, ct));
        g.MapPost("/{id:guid}/resume", (Guid id, SessionService svc, SessionETag e, CancellationToken ct) => DoTransition(id, Transition.Resume, svc, e, ct));
        g.MapPost("/{id:guid}/stop", (Guid id, SessionService svc, SessionETag e, CancellationToken ct) => DoTransition(id, Transition.Stop, svc, e, ct));
        g.MapDelete("/{id:guid}", (Guid id, SessionService svc, CancellationToken ct) => svc.DeleteAsync(id, ct));
        g.MapPost("/{id:guid}/audio-ticket", IssueTicket).RequireRateLimiting(AudioTicketPolicy);

        // Present so the routes, auth boundary and 404 rules exist now; content arrives with B2 (transcript, AI).
        g.MapGet("/{id:guid}/transcript", GetTranscript);
        g.MapGet("/{id:guid}/summary", GetSummary);
        g.MapGet("/{id:guid}/catch-ups/{catchUpId}", GetCatchUp);
        g.MapGet("/{id:guid}/notes", GetNotes);
    }

    private static async Task<IResult> Create(Guid courseId, CreateSessionRequest? body, HttpContext http, SessionService svc,
        SessionETag etags, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        var title = body?.Title?.Trim();
        if (string.IsNullOrEmpty(title)) title = null;
        if (title is { Length: > SessionService.MaxTitleLength })
            errors["title"] = [$"Must be at most {SessionService.MaxTitleLength} characters."];
        if (body?.Consent?.TextVersion is { Length: > 64 })
            errors["consent.textVersion"] = ["Must be at most 64 characters."];

        string? key = null;
        if (http.Request.Headers.TryGetValue("Idempotency-Key", out var kv))
        {
            key = kv.ToString();
            if (kv.Count != 1 || key.Length is < 1 or > 128 || !key.All(c => c is > ' ' and <= '~'))
                errors["Idempotency-Key"] = ["Must be 1 to 128 printable characters."];
        }
        if (errors.Count > 0) return Problems.Validation(errors);

        var o = await svc.CreateAsync(courseId, title, body?.Consent, key, ct);
        if (o.Problem is not null) return o.Problem;
        var res = o.Row!.ToResponse(etags);
        http.Response.Headers.ETag = res.Etag;
        if (o.Replayed) http.Response.Headers["Idempotency-Replayed"] = "true";
        return Results.Created($"/api/v1/sessions/{res.Id}", res);
    }

    private static async Task<IResult> List(Guid courseId, string? cursor, int? limit, SessionService svc, SessionETag etags,
        Tether.Courses.Contracts.ICourseOwnership courses, Tether.SharedKernel.ICurrentUser user, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        var take = limit ?? DefaultLimit;
        if (take is < 1 or > MaxLimit) errors["limit"] = [$"Limit must be between 1 and {MaxLimit}."];
        Guid? after = null;
        if (cursor is not null)
        {
            if (Guid.TryParseExact(cursor, "N", out var g)) after = g;
            else errors["cursor"] = ["Cursor is not valid."];
        }
        if (errors.Count > 0) return Problems.Validation(errors);

        if (user.UserId is not { } uid || !await courses.IsOwnedByAsync(uid, courseId, ct)) return Problems.NotFound("Course not found.");
        var (items, next) = await svc.ListAsync(courseId, after, take, ct);
        return Results.Ok(new SessionListResponse(items.Select(r => r.ToResponse(etags)).ToList(), next?.ToString("N")));
    }

    private static async Task<IResult> Get(Guid id, HttpContext http, SessionService svc, SessionETag etags, CancellationToken ct)
    {
        var row = await svc.GetAsync(id, ct);
        if (row is null) return Problems.NotFound("Session not found.");
        var res = row.ToResponse(etags);
        http.Response.Headers.ETag = res.Etag;
        return Results.Ok(res);
    }

    private static async Task<IResult> DoTransition(Guid id, Transition t, SessionService svc, SessionETag etags, CancellationToken ct)
    {
        var o = await svc.TransitionAsync(id, t, ct);
        return o.Problem ?? Results.Ok(o.Row!.ToResponse(etags));
    }

    private static async Task<IResult> IssueTicket(Guid id, SessionService svc, CancellationToken ct)
    {
        var (ticket, problem) = await svc.IssueTicketAsync(id, ct);
        return problem ?? Results.Ok(ticket);
    }

    // ---- placeholders until B2: ownership is enforced, content does not exist yet ----

    private static async Task<IResult> GetTranscript(Guid id, string? afterSeq, int? limit, SessionService svc, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        var take = limit ?? DefaultTranscriptLimit;
        if (take is < 1 or > MaxTranscriptLimit) errors["limit"] = [$"Limit must be between 1 and {MaxTranscriptLimit}."];
        if (afterSeq is not null && !(long.TryParse(afterSeq, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out _)))
            errors["afterSeq"] = ["Must be a non-negative integer."];
        if (errors.Count > 0) return Problems.Validation(errors);
        if (!await svc.ExistsAsync(id, ct)) return Problems.NotFound("Session not found.");
        return Results.Ok(new TranscriptPageResponse([], false));
    }

    private static async Task<IResult> GetSummary(Guid id, int? revision, SessionService svc, CancellationToken ct)
    {
        if (revision is < 1) return Problems.Validation("revision", "Must be a positive integer.");
        _ = await svc.ExistsAsync(id, ct);
        return Problems.NotFound("No summary yet."); // missing session and "none yet" are indistinguishable by design
    }

    private static async Task<IResult> GetCatchUp(Guid id, string catchUpId, SessionService svc, CancellationToken ct)
    {
        _ = await svc.ExistsAsync(id, ct);
        return Problems.NotFound("Catch-up not found.");
    }

    private static async Task<IResult> GetNotes(Guid id, SessionService svc, CancellationToken ct) =>
        await svc.ExistsAsync(id, ct) ? Problems.InvalidState() : Problems.NotFound("Session not found.");
}
