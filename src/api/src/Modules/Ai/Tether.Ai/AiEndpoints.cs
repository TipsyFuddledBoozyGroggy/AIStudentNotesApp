using System.Text.Json;
using Tether.Ai.CatchUp;
using Tether.Ai.Persistence;
using Tether.Ai.Summary;
using Tether.Lectures.Contracts;
using Tether.SharedKernel;
using Tether.SharedKernel.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Tether.Ai;

/// <summary>
/// REST surface of the AI module (API_CONTRACTS section 2). Every route looks the session up through the Lectures directory first,
/// keyed by the authenticated user, so another user's session and a session that does not exist both give the same 404.
/// </summary>
internal static class AiEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        var g = api.MapGroup("/sessions");
        g.MapGet("/{id:guid}/summary", GetSummary);
        g.MapPost("/{id:guid}/quick-recap", PostQuickRecap);
        g.MapGet("/{id:guid}/catch-ups/{catchUpId}", GetCatchUp);
    }

    private static async Task<IResult> GetSummary(Guid id, int? revision, ISessionDirectory directory, SummaryStore store, ICurrentUser user, CancellationToken ct)
    {
        if (revision is < 1) return Problems.Validation("revision", "Must be a positive integer.");
        if (user.UserId is not { } uid || await directory.FindAsync(uid, id, ct) is null) return Problems.NotFound("Session not found.");
        var row = revision is { } r ? await store.GetAsync(id, r, ct) : await store.LatestAsync(id, ct);
        return row is null ? Problems.NotFound("No summary yet.") : Results.Ok(SummaryJson.ToPayload(row));
    }

    private static async Task<IResult> PostQuickRecap(Guid id, HttpRequest request, CatchUpService svc, ICurrentUser user, CancellationToken ct)
    {
        if (!request.HasJsonContentType()) return Problems.ForStatus(415);
        int window;
        try
        {
            using var doc = await JsonDocument.ParseAsync(request.Body, cancellationToken: ct);
            // Closed shape: exactly {"windowMinutes": 2|5|10} (quick-recap-request.schema.json).
            var props = doc.RootElement.ValueKind == JsonValueKind.Object ? doc.RootElement.EnumerateObject().ToList() : [];
            if (props.Count != 1 || props[0].Name != "windowMinutes" || props[0].Value.ValueKind != JsonValueKind.Number
                || !props[0].Value.TryGetInt32(out window) || !CatchUpWindow.IsAllowed(window))
                return Problems.Validation("windowMinutes", "Must be 2, 5 or 10.");
        }
        catch (JsonException) { return Problems.Validation("body", "The request body must be a JSON object."); }

        string? key = null;
        if (request.Headers.TryGetValue("Idempotency-Key", out var k))
        {
            key = k.ToString();
            if (key.Length is < 1 or > 64 || !key.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
                return Problems.Validation("Idempotency-Key", "Must be 1 to 64 letters, digits, '-' or '_'.");
        }

        if (user.UserId is not { } uid) return Problems.Unauthenticated();
        var result = await svc.StartAsync(uid, id, window, key, ct);
        switch (result.Kind)
        {
            case StartKind.NotFound: return Problems.NotFound("Session not found.");
            case StartKind.InvalidState: return Problems.InvalidState();
            case StartKind.RateLimited: return Problems.ForStatus(429);
            case StartKind.KeyConflict: return Problems.Validation("Idempotency-Key", "This key was already used with different parameters.");
        }
        if (result.Replayed) request.HttpContext.Response.Headers["Idempotency-Replayed"] = "true";
        return Results.Content(result.Json!, "application/json");
    }

    private static async Task<IResult> GetCatchUp(Guid id, string catchUpId, ISessionDirectory directory, CatchUpService svc, ICurrentUser user, CancellationToken ct)
    {
        if (user.UserId is not { } uid || await directory.FindAsync(uid, id, ct) is null) return Problems.NotFound("Session not found.");
        if (!CatchUpBuilder.TryParseId(catchUpId, out var cid)) return Problems.NotFound("Catch-up not found.");
        var row = await svc.GetAsync(id, cid, ct);
        return row is null ? Problems.NotFound("Catch-up not found.") : Results.Content(row.ResultJson, "application/json");
    }
}
