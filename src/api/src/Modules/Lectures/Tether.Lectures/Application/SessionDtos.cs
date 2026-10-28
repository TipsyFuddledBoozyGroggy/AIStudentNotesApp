using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Tether.Lectures.Domain;
using Microsoft.AspNetCore.WebUtilities;

namespace Tether.Lectures.Application;

// Wire shapes below must keep validating against src/shared/contracts/lecture.v1/*.schema.json (additionalProperties:false).

internal sealed record ConsentView(string TextVersion, DateTimeOffset AcknowledgedAt);

internal sealed record SessionResponse(
    string Id,
    string CourseId,
    string? Title,
    string State,
    DateTimeOffset StartedAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? StoppedAt,
    string LastSeq,
    ConsentView Consent,
    string Etag,
    bool DemoSource);

internal sealed record SessionListResponse(IReadOnlyList<SessionResponse> Items, string? NextCursor);

internal sealed record AudioTicketResponse(string Ticket, DateTimeOffset ExpiresAt);

internal sealed record ConsentRequest(bool? Acknowledged, string? TextVersion);

internal sealed record CreateSessionRequest(string? Title, ConsentRequest? Consent);

/// <summary>Row projection used to build <see cref="SessionResponse"/>.</summary>
internal sealed record SessionRow(LectureSession Session, string TextVersion, DateTimeOffset AcknowledgedAt, long LastSeq)
{
    /// <summary>True if the transcript source was the deterministic replay provider when the session was created (API_CONTRACTS 12.2, B2-09).</summary>
    public bool DemoSource => Session.DemoSource;
}

/// <summary>
/// Opaque weak ETags (F-05): HMAC-SHA256(secret, id | xmin) truncated. Uses the same optional <c>Security:ETagKey</c> as
/// Courses; without it the secret is random per process.
/// </summary>
internal sealed class SessionETag(byte[] key)
{
    public string For(LectureSession s)
    {
        var mac = HMACSHA256.HashData(key, Encoding.ASCII.GetBytes($"lec|{s.Id:N}|{s.Version}"));
        return $"W/\"{WebEncoders.Base64UrlEncode(mac, 0, 16)}\"";
    }
}

internal static class SessionMapping
{
    public static SessionResponse ToResponse(this SessionRow r, SessionETag etags) => new(
        r.Session.Id.ToString(), r.Session.CourseId.ToString(), r.Session.Title, r.Session.State.ToString(),
        r.Session.StartedAt, r.Session.StoppedAt, r.LastSeq.ToString(System.Globalization.CultureInfo.InvariantCulture),
        new ConsentView(r.TextVersion, r.AcknowledgedAt), etags.For(r.Session), r.DemoSource);
}
