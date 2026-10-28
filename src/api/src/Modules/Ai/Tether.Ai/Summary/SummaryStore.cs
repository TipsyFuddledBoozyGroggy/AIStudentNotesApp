using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Tether.Ai.Domain;
using Tether.Ai.Persistence;
using Tether.Ai.Provider;
using Tether.Ai.Validation;
using Tether.Transcript.Contracts;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Tether.Ai.Summary;

internal enum AppendResult { Ok, Stale, SessionGone }

/// <summary>Wire payload of <c>summary.revision</c> / <c>GET /summary</c>: must keep validating against summary-revision.schema.json.</summary>
internal sealed record SummaryRevisionPayload(
    int Revision, string BasedOnSeq, string Tldr, IReadOnlyList<string> Bullets, long CoveredFromMs, long CoveredToMs, string Status,
    IReadOnlyList<CitationRef> Citations, string Source);

internal sealed record SummaryStatusPayload(string State, int LagSeconds, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Reason);

internal static class SummaryJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Write<T>(T value) => JsonSerializer.Serialize(value, Options);
    public static T Read<T>(string json) => JsonSerializer.Deserialize<T>(json, Options) ?? throw new JsonException();

    public static SummaryRevisionPayload ToPayload(SummaryRevisionRow r)
    {
        var topics = Read<List<TopicState>>(r.TopicsJson);
        return new SummaryRevisionPayload(r.Revision, r.BasedOnSeq.ToString(CultureInfo.InvariantCulture), r.Tldr,
            topics.Select(t => t.Text).ToList(), r.CoveredFromMs, r.CoveredToMs, r.Status, Read<List<CitationRef>>(r.CitationsJson), AiSource.Of(r.Model));
    }
}

/// <summary>Append-only revision storage. Scoped; the DbContext's query filter keeps every read inside the current user.</summary>
internal sealed class SummaryStore(AiDbContext db)
{
    public Task<SummaryRevisionRow?> LatestAsync(Guid sessionId, CancellationToken ct) =>
        db.Summaries.AsNoTracking().Where(s => s.SessionId == sessionId).OrderByDescending(s => s.Revision).FirstOrDefaultAsync(ct);

    public Task<SummaryRevisionRow?> GetAsync(Guid sessionId, int revision, CancellationToken ct) =>
        db.Summaries.AsNoTracking().FirstOrDefaultAsync(s => s.SessionId == sessionId && s.Revision == revision, ct);

    /// <summary>
    /// Appends revision <c>expectedBase + 1</c> only if <c>expectedBase</c> is still the newest. A delta computed against an older base
    /// (a concurrent writer got there first) is rejected as stale instead of overwriting newer content. If the session was deleted in
    /// the meantime the FK fails and nothing is kept (late jobs cannot recreate deleted data, C-21).
    /// </summary>
    public async Task<AppendResult> AppendAsync(SummaryRevisionRow row, int expectedBase, CancellationToken ct)
    {
        var current = await db.Summaries.AsNoTracking().Where(s => s.SessionId == row.SessionId).MaxAsync(s => (int?)s.Revision, ct) ?? 0;
        if (current != expectedBase || row.Revision != expectedBase + 1) return AppendResult.Stale;
        db.Summaries.Add(row);
        try
        {
            await db.SaveChangesAsync(ct);
            return AppendResult.Ok;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.ChangeTracker.Clear();
            return AppendResult.Stale;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation })
        {
            db.ChangeTracker.Clear();
            return AppendResult.SessionGone;
        }
    }
}

/// <summary>Feeds <c>Join</c>: the newest revision number and a ready-to-send payload (summary events are ephemeral, so a late joiner needs this).</summary>
internal sealed class SummaryJoinSource(SummaryStore store) : ISummaryJoinSource
{
    public async Task<SummarySnapshot?> LatestAsync(Guid userId, Guid sessionId, CancellationToken ct = default)
    {
        var row = await store.LatestAsync(sessionId, ct);
        return row is null ? null : new SummarySnapshot(row.Revision, SummaryJson.ToPayload(row));
    }
}
