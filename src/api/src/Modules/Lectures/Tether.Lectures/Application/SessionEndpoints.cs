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

    public static void Map(IEndpointRouteBuilder api)
    {
        // All routes are authenticated by the fallback policy (SEC-001).
        api.MapPost("/courses/{courseId:guid}/sessions", Create);
        api.MapGet("/courses/{courseId:guid}/sessions", List);

        var g = api.MapGroup("/sessions");
        g.MapGet("/{id:guid}", Get);
        g.MapPost("/{id:guid}/pause", (Guid id, SessionService svc, SessionETag e, CancellationToken ct) => DoTransition(id, Transition.Pause, svc, e, ct)).AsConsentReversal();
        g.MapPost("/{id:guid}/resume", (Guid id, SessionService svc, SessionETag e, CancellationToken ct) => DoTransition(id, Transition.Resume, svc, e, ct));
        g.MapPost("/{id:guid}/stop", (Guid id, SessionService svc, SessionETag e, CancellationToken ct) => DoTransition(id, Transition.Stop, svc, e, ct)).AsConsentReversal();
        g.MapDelete("/{id:guid}", (Guid id, SessionService svc, CancellationToken ct) => svc.DeleteAsync(id, ct));
        g.MapPost("/{id:guid}/audio-ticket", IssueTicket).RequireRateLimiting(AudioTicketPolicy);

        // summary, lost-track and catch-ups are served by the AI module (B3). Notes stay a placeholder until their increment.
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
        else if (title is not null && title.Any(c => char.IsControl(c) || c is (>= '‪' and <= '‮') or (>= '⁦' and <= '⁩')))
            errors["title"] = ["Must not contain control or bidirectional override characters."];
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

    // ---- placeholder until the notes increment: ownership is enforced, content does not exist yet ----

    private static async Task<IResult> GetNotes(Guid id, SessionService svc, CancellationToken ct) =>
        await svc.ExistsAsync(id, ct) ? Problems.InvalidState() : Problems.NotFound("Session not found.");
}
