using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Tether.Docs.Domain;
using Tether.Docs.Persistence;
using Tether.SharedKernel;
using Tether.SharedKernel.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Tether.Docs.Application;

/// <summary>Limits and switches. Every default is a contract proposal (section 6), not a measured value; devops and the PO own the numbers.</summary>
internal sealed class DocsOptions
{
    public int BodyBytes { get; init; } = 2048;
    public int BodyReadSeconds { get; init; } = 10;
    public int BodyMinBytesPerSecond { get; init; } = 200;
    public int ReadPermitPerMinute { get; init; } = 60;
    public int WritePermitPerMinute { get; init; } = 10;
    public int MaxDocsPerUser { get; init; } = DocsConstants.MaxDocsPerUser;
    public int DeadlockRepeats { get; init; } = 3;
    public int KeyHours { get; init; } = 24;
    public bool FaultsEnabled { get; init; }


    public static DocsOptions From(IConfiguration c) => new()
    {
        BodyReadSeconds = Math.Clamp(c.GetValue("Docs:BodyReadSeconds", 10), 1, 60),
        ReadPermitPerMinute = Math.Max(1, c.GetValue("RateLimiting:DocsReadPermitPerMinute", 60)),
        WritePermitPerMinute = Math.Max(1, c.GetValue("RateLimiting:DocsWritePermitPerMinute", 10)),
        MaxDocsPerUser = Math.Max(1, c.GetValue("Docs:MaxDocsPerUser", DocsConstants.MaxDocsPerUser)),
        FaultsEnabled = c.GetValue("Docs:Demo:Faults:Enabled", false),
    };
}

/// <summary>Server keys. The digest key has its own HKDF label and is never the idempotency or audit key (DXD-06).</summary>
internal sealed class DocsKeys(byte[] digest, byte[][] idempotencyRing)
{
    public byte[] Digest { get; } = digest;
    /// <summary>Active key first, previous key second (rotation overlap), the same ring and overrides as the Syllabus module's.</summary>
    public byte[][] IdempotencyRing { get; } = idempotencyRing;

    public static DocsKeys From(IConfiguration c, DerivedKeys derived)
    {
        static byte[] Override(IConfiguration c, string name, Func<byte[]> derive)
        {
            if (c[name] is not { Length: > 0 } b64) return derive();
            var k = Convert.FromBase64String(b64);
            return k.Length >= 32 ? k : throw new InvalidOperationException($"{name} must decode to at least 32 bytes.");
        }
        var ring = new List<byte[]> { Override(c, "Security:IdempotencyHmacKey", () => derived.Derive(DerivedKeys.Labels.Idempotency)) };
        if (c["Security:IdempotencyHmacKeyPrevious"] is { Length: > 0 }) ring.Add(Override(c, "Security:IdempotencyHmacKeyPrevious", () => throw new InvalidOperationException()));
        return new DocsKeys(derived.Derive(DerivedKeys.Labels.DocsContentDigest), [.. ring]);
    }
}

internal static class Routes
{
    public const string ConfirmTemplate = "/sessions/{id}/docs-export";
}

/// <summary>Idempotency material, shared shape with the Syllabus module: every field length-prefixed, null distinct from empty (B1-07).</summary>
internal static class Hashing
{
    public static byte[] Sha256(string s) => SHA256.HashData(Encoding.UTF8.GetBytes(s));
    public static byte[] Hmac(byte[] key, string data) => HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(data));
    public static string Material(params string?[] fields) =>
        string.Join("|", fields.Select(f => f is null ? "~" : f.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + f));
    public static bool ValidKey(string? key) => key is { Length: >= 8 and <= 128 } && key.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-');
}

/// <summary>Reads a JSON request body with a streaming cap, a read deadline and strict parsing (API_CONTRACTS_P2 section 2.1; the contract's step 7).</summary>
internal static class JsonBody
{
    public enum Failure { None, TooLarge, Unsupported, TimedOut }

    public static readonly JsonSerializerOptions Out = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Default,
    };

    public static bool IsJson(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType)) return false;
        var semi = contentType.IndexOf(';');
        return string.Equals((semi < 0 ? contentType : contentType[..semi]).Trim(), "application/json", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Reads at most <paramref name="cap"/> bytes (counted while reading; Content-Length is never trusted) within <paramref name="readTimeout"/>.</summary>
    public static async Task<(byte[]? Bytes, Failure Failure)> ReadBytesAsync(HttpContext http, int cap, TimeSpan readTimeout, CancellationToken ct)
    {
        if (!IsJson(http.Request.ContentType)) return (null, Failure.Unsupported);
        if (http.Request.ContentLength is { } cl && cl > cap) return (null, Failure.TooLarge);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(readTimeout);
        using var ms = new MemoryStream();
        var buffer = new byte[1024];
        try
        {
            int n;
            while ((n = await http.Request.Body.ReadAsync(buffer, deadline.Token)) > 0)
            {
                if (ms.Length + n > cap) return (null, Failure.TooLarge);
                ms.Write(buffer, 0, n);
            }
        }
        catch (BadHttpRequestException b) when (b.StatusCode == 413) { return (null, Failure.TooLarge); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return (null, Failure.TimedOut); }
        catch (BadHttpRequestException b) when (b.StatusCode == 408) { return (null, Failure.TimedOut); }
        return (ms.ToArray(), Failure.None);
    }

    /// <summary>Strict parse: duplicate members, comments, trailing commas, invalid UTF-8 and deep nesting are all a failure (null), never an exception.</summary>
    public static JsonDocument? TryParse(byte[] body)
    {
        if (body.Length == 0) return null;
        try
        {
            return JsonDocument.Parse(body, new JsonDocumentOptions { MaxDepth = 8, AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow, AllowDuplicateProperties = false });
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException) { return null; }
    }
}

/// <summary>
/// Thin parameterized-SQL helper on the DbContext's own connection and transaction, for what EF cannot express (advisory locks, the provider's
/// ON CONFLICT upsert, a versioned update). Values are always parameters. EVERY statement that reads or writes a table carries a user_id
/// predicate (the EF query filter and the ownership interceptor do not see raw SQL); a source-scan test enforces it (DXD-09, B05a).
/// </summary>
internal sealed class Sql(DocsDbContext db)
{
    private DbCommand Make(string sql, object?[] ps)
    {
        var cmd = db.Database.GetDbConnection().CreateCommand();
        cmd.CommandText = sql;
        cmd.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
        for (var i = 0; i < ps.Length; i++) cmd.Parameters.Add(new NpgsqlParameter("p" + i, ps[i] ?? DBNull.Value));
        return cmd;
    }

    public async Task<int> ExecAsync(string sql, CancellationToken ct, params object?[] ps)
    {
        await db.Database.OpenConnectionAsync(ct);
        try { await using var cmd = Make(sql, ps); return await cmd.ExecuteNonQueryAsync(ct); }
        finally { await db.Database.CloseConnectionAsync(); }
    }

    public async Task<List<object?[]>> RowsAsync(string sql, CancellationToken ct, params object?[] ps)
    {
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            await using var cmd = Make(sql, ps);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            var rows = new List<object?[]>();
            while (await r.ReadAsync(ct))
            {
                var row = new object?[r.FieldCount];
                for (var i = 0; i < row.Length; i++) row[i] = r.IsDBNull(i) ? null : r.GetValue(i);
                rows.Add(row);
            }
            return rows;
        }
        finally { await db.Database.CloseConnectionAsync(); }
    }

    /// <summary>Like <see cref="RowsAsync"/> but a jsonb parameter is typed as such.</summary>
    public async Task<int> ExecJsonAsync(string sql, CancellationToken ct, string json, params object?[] ps)
    {
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            await using var cmd = Make(sql, ps);
            cmd.Parameters.Add(new NpgsqlParameter("j", NpgsqlTypes.NpgsqlDbType.Jsonb) { Value = json });
            return await cmd.ExecuteNonQueryAsync(ct);
        }
        finally { await db.Database.CloseConnectionAsync(); }
    }
}
