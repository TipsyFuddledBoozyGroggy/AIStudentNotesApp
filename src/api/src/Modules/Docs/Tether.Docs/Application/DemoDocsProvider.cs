using System.Globalization;
using System.Text.Json;
using Tether.Docs.Contracts;
using Tether.Docs.Domain;
using Tether.SharedKernel;

namespace Tether.Docs.Application;

/// <summary>
/// Development-only fault selection for ONE request (ADR section 7, Lead decisions Q5 and Q-C). The endpoint fills it from a request header only
/// when <c>Docs:Demo:Faults:Enabled</c> is true; otherwise the header is not even read, so nothing reveals the feature. Faults fire inside the demo
/// provider. Values: <c>unavailable</c>, <c>rate_limited</c> or <c>rate_limited:SECONDS</c> (cap 3600), <c>rejected</c>, <c>slow</c> or <c>slow:SECONDS</c>
/// (cap 5, then the write goes through). An unknown value is ignored.
/// </summary>
internal sealed class DemoFaultContext
{
    public const string Header = "X-Demo-Docs-Fault";
    public const int SlowCapSeconds = 5;
    public const int RetryAfterCap = 3600;

    public string? Raw { get; set; }

    public static bool TryParseHeader(string? raw, out string kind, out int seconds)
    {
        kind = ""; seconds = 0;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        var parts = raw.Trim().ToLowerInvariant().Split(':', 2);
        kind = parts[0];
        var hasArg = parts.Length == 2 && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out seconds);
        switch (kind)
        {
            case "unavailable": seconds = 5; return true;
            case "rejected": seconds = 0; return true;
            case "rate_limited": seconds = Math.Clamp(hasArg ? seconds : 30, 1, RetryAfterCap); return true;
            case "slow": seconds = Math.Clamp(hasArg ? seconds : SlowCapSeconds, 0, SlowCapSeconds); return true;
            default: kind = ""; return false;
        }
    }
}

/// <summary>
/// The mock destination: the "document" is a row in <c>docs.demo_docs</c>. It runs INSIDE the confirm transaction on the caller's DbContext (the
/// caller wraps the call in a savepoint), takes no token, makes no network call, and carries no HttpClient. Create is keyed by the export id
/// (unique origin_export_id): a second call for the same export returns the first row (provider-side idempotency). It is NOT a pattern for a real
/// provider (ADR section 9): there the call leaves the transaction.
/// </summary>
internal sealed class DemoDocsProvider(Persistence.DocsDbContext db, ICurrentUser user, TimeProvider clock, DemoFaultContext faults) : IDocsProvider
{
    public string ProviderId => DocsConstants.Provider;

    private Guid Me => user.UserId ?? throw new InvalidOperationException("Authenticated user required.");

    /// <summary>The fault for this call, or null. Honours cancellation for <c>slow</c> so it cannot hold the session lock and a pooled connection past the cap.</summary>
    private async Task<ProviderFailure?> FaultAsync(CancellationToken ct)
    {
        if (!DemoFaultContext.TryParseHeader(faults.Raw, out var kind, out var seconds)) return null;
        switch (kind)
        {
            case "unavailable": return new ProviderFailure("provider_unavailable", true, seconds);
            case "rate_limited": return new ProviderFailure("rate_limited", true, seconds);
            case "rejected": return new ProviderFailure("provider_rejected", false, null);
            case "slow":
                if (seconds > 0) await Task.Delay(TimeSpan.FromSeconds(seconds), ct);
                return null;
            default: return null;
        }
    }

    private static ProviderResult<DocRef> Fail(ProviderFailure f) => f.Retryable ? ProviderResult<DocRef>.Retryable(f.Code, f.RetryAfterSeconds) : ProviderResult<DocRef>.Permanent(f.Code);

    public async Task<ProviderResult<DocRef>> CreateAsync(DocsWriteRequest request, CancellationToken ct)
    {
        if (request.UserId != Me) return ProviderResult<DocRef>.Permanent("provider_rejected"); // the user in the request must be the ambient user
        if (await FaultAsync(ct) is { } fault) return Fail(fault);
        var sql = new Sql(db);
        var now = Now();
        var id = Ids.New();
        var json = BlocksJson(request.Document.Blocks);
        // The lecture comes from the export row this call is keyed by (inserted before the call, so it exists in this transaction).
        var owner = await sql.RowsAsync("SELECT session_id FROM docs.doc_exports WHERE id = @p0 AND user_id = @p1", ct, request.RequestKey, Me);
        if (owner.Count != 1) return ProviderResult<DocRef>.Permanent("provider_rejected");
        var sessionId = (Guid)owner[0][0]!;
        // Keyed by the export id: a repeated call returns the first row. user_id is in the predicate and in the row.
        await sql.ExecJsonAsync("INSERT INTO docs.demo_docs (id, user_id, session_id, title, blocks, version, created_at, updated_at, origin_export_id) " +
                                "VALUES (@p0, @p1, @p2, @p3, @j, 1, @p4, @p4, @p5) ON CONFLICT (origin_export_id) DO NOTHING", ct, json,
            id, Me, sessionId, request.Document.Title, now, request.RequestKey);
        var rows = await sql.RowsAsync("SELECT id, version FROM docs.demo_docs WHERE origin_export_id = @p0 AND user_id = @p1", ct, request.RequestKey, Me);
        if (rows.Count != 1) return ProviderResult<DocRef>.Permanent("provider_rejected");
        return ProviderResult<DocRef>.Ok(new DocRef((Guid)rows[0][0]!, Convert.ToInt32(rows[0][1], CultureInfo.InvariantCulture)));
    }

    public async Task<ProviderResult<DocRef>> UpdateAsync(DocRef existing, int expectedVersion, DocsWriteRequest request, CancellationToken ct)
    {
        if (request.UserId != Me) return ProviderResult<DocRef>.Permanent("provider_rejected");
        if (await FaultAsync(ct) is { } fault) return Fail(fault);
        var sql = new Sql(db);
        var json = BlocksJson(request.Document.Blocks);
        // optimistic: only the version the caller reviewed is replaced
        var n = await sql.ExecJsonAsync("UPDATE docs.demo_docs SET title = @p0, blocks = @j, version = version + 1, updated_at = @p1 " +
                                        "WHERE id = @p2 AND user_id = @p3 AND version = @p4", ct, json, request.Document.Title, Now(), existing.DocId, Me, expectedVersion);
        return n == 1 ? ProviderResult<DocRef>.Ok(new DocRef(existing.DocId, expectedVersion + 1)) : ProviderResult<DocRef>.Permanent("provider_rejected");
    }

    public async Task<ProviderResult> DeleteAsync(DocRef existing, CancellationToken ct)
    {
        var n = await new Sql(db).ExecAsync("DELETE FROM docs.demo_docs WHERE id = @p0 AND user_id = @p1", ct, existing.DocId, Me);
        return n == 1 ? ProviderResult.Ok() : ProviderResult.Permanent("provider_rejected");
    }

    private DateTimeOffset Now() => new(clock.GetUtcNow().UtcTicks / 10 * 10, TimeSpan.Zero);

    public static string BlocksJson(IReadOnlyList<DocBlock> blocks)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartArray();
            foreach (var b in blocks) WriteBlock(w, b);
            w.WriteEndArray();
        }
        return System.Text.Encoding.UTF8.GetString(ms.ToArray());
    }

    public static void WriteBlock(Utf8JsonWriter w, DocBlock b)
    {
        w.WriteStartObject();
        w.WriteString("kind", b.Kind);
        if (b.Kind == "heading") w.WriteNumber("level", b.Level ?? 1);
        w.WriteString("text", b.Text);
        if (b.Kind == "bullet")
        {
            w.WriteStartArray("citations");
            foreach (var ms in b.CitationStartMs ?? []) { w.WriteStartObject(); w.WriteNumber("startMs", ms); w.WriteEndObject(); }
            w.WriteEndArray();
        }
        w.WriteEndObject();
    }

    public static IReadOnlyList<DocBlock> ReadBlocks(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var list = new List<DocBlock>();
        foreach (var e in doc.RootElement.EnumerateArray())
        {
            var kind = e.GetProperty("kind").GetString()!;
            var text = e.GetProperty("text").GetString()!;
            int? level = e.TryGetProperty("level", out var l) ? l.GetInt32() : null;
            List<long>? cites = e.TryGetProperty("citations", out var cs) ? cs.EnumerateArray().Select(c => c.GetProperty("startMs").GetInt64()).ToList() : null;
            list.Add(new DocBlock(kind, text, level, cites));
        }
        return list;
    }
}
