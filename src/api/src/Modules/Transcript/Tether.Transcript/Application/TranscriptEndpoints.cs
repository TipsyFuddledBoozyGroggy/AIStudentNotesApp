using System.Globalization;
using Tether.Lectures.Contracts;
using Tether.SharedKernel;
using Tether.SharedKernel.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Tether.Transcript.Application;

internal static class TranscriptEndpoints
{
    public const int DefaultLimit = 200; // section 13.1
    public const int MaxLimit = 500;     // replay cap (C-27)

    public static void Map(IEndpointRouteBuilder api) => api.MapGet("/sessions/{id:guid}/transcript", Get);

    private static async Task<IResult> Get(Guid id, string? afterSeq, int? limit, ISessionDirectory directory, TranscriptReader reader,
        ICurrentUser user, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        var take = limit ?? DefaultLimit;
        if (take is < 1 or > MaxLimit) errors["limit"] = [$"Limit must be between 1 and {MaxLimit}."];
        long after = 0;
        if (afterSeq is not null && !long.TryParse(afterSeq, NumberStyles.None, CultureInfo.InvariantCulture, out after))
            errors["afterSeq"] = ["Must be a non-negative integer."];
        if (errors.Count > 0) return Problems.Validation(errors);

        // Foreign and missing sessions both give 404; every read is keyed by (user, session), never by seq alone (C-09).
        if (user.UserId is not { } uid || await directory.FindAsync(uid, id, ct) is null) return Problems.NotFound("Session not found.");
        return Results.Ok(await reader.ReadAfterAsync(uid, id, after, take, ct));
    }
}
