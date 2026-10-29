using System.Data;
using System.Data.Common;
using Tether.SharedKernel;
using Tether.Syllabus.Domain;
using Tether.Syllabus.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Tether.Syllabus.Application;

/// <summary>
/// Thin parameterized-SQL helper on the DbContext's own connection and transaction. Used for what EF cannot express (row locks, ON CONFLICT
/// upserts, set-based maintenance). Positional parameters @p0, @p1 ...; values are always parameters, never concatenated.
/// Every statement it runs names user_id explicitly (the EF query filter does not apply to raw SQL).
/// </summary>
internal sealed class Sql(SyllabusDbContext db)
{
    private DbCommand Make(string sql, object?[] ps)
    {
        var conn = db.Database.GetDbConnection();
        var cmd = conn.CreateCommand();
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

    public async Task<T?> ScalarAsync<T>(string sql, CancellationToken ct, params object?[] ps)
    {
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            await using var cmd = Make(sql, ps);
            var o = await cmd.ExecuteScalarAsync(ct);
            return o is null or DBNull ? default : (T)Convert.ChangeType(o, Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T), System.Globalization.CultureInfo.InvariantCulture);
        }
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
}

/// <summary>Request-scoped helpers shared by the syllabus services.</summary>
internal sealed class SyllabusCore(SyllabusDbContext db, ICurrentUser user, TimeProvider clock, SyllabusETag etags, SyllabusKeys keys, SyllabusOptions options)
{
    public SyllabusDbContext Db => db;
    public SyllabusETag ETags => etags;
    public SyllabusKeys Keys => keys;
    public SyllabusOptions Options => options;
    public TimeProvider Clock => clock;
    public Sql Sql { get; } = new(db);
    public Guid Uid => user.UserId ?? throw new InvalidOperationException("Authenticated user required.");

    /// <summary>UTC now truncated to microseconds so values match what PostgreSQL stores.</summary>
    public DateTimeOffset Now()
    {
        var t = clock.GetUtcNow();
        return new DateTimeOffset(t.Ticks - t.Ticks % 10, TimeSpan.Zero);
    }

    public Task<SyllabusDoc?> FindSyllabusAsync(Guid id, CancellationToken ct) =>
        db.Syllabi.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct);

    /// <summary>Locks the syllabus row (the lock DELETE, sync, batch and the job's persistence step all take). False if it does not exist for this user.</summary>
    public async Task<bool> LockSyllabusAsync(Guid id, CancellationToken ct) =>
        await Sql.ScalarAsync<int?>("SELECT 1 FROM syllabus.syllabi WHERE id = @p0 AND user_id = @p1 FOR UPDATE", ct, id, Uid) is not null;

    /// <summary>
    /// The retention clock moves ONLY for student writes (edit, decision, sync or remove, add item), in the same transaction as the change.
    /// Never for reads, polls, exports or job progress. canonical_text_purge_at = LEAST(last write + 180 days, created_at + 365 days).
    /// </summary>
    public Task TouchActivityAsync(Guid syllabusId, DateTimeOffset now, CancellationToken ct) =>
        Sql.ExecAsync("""
            UPDATE syllabus.syllabi SET last_activity_at = GREATEST(@p0, created_at), updated_at = @p0,
              canonical_text_purge_at = CASE WHEN canonical_text_purged_at IS NULL
                THEN LEAST(GREATEST(@p0, created_at) + make_interval(days => @p2), created_at + make_interval(days => @p3)) ELSE NULL END
            WHERE id = @p1 AND user_id = @p4
            """, ct, now, syllabusId, options.IdleDays, options.AbsoluteDays, Uid);

    public void Audit(Guid syllabusId, string action, DateTimeOffset now, Guid? itemId = null, int? itemRevision = null, int? count = null,
        byte[]? contentHmac = null, string? noticeVersion = null) =>
        db.Audit.Add(new DecisionAudit
        {
            Id = Ids.New(), UserId = Uid, SyllabusId = syllabusId, ItemId = itemId, ItemRevision = itemRevision, ItemCount = count,
            ContentHmac = contentHmac, NoticeVersion = noticeVersion, Action = action, OccurredAt = now,
        });

    public async Task<Dictionary<Guid, CountsDto>> CountsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        var items = await db.Items.AsNoTracking().Where(i => ids.Contains(i.SyllabusId)).GroupBy(i => i.SyllabusId)
            .Select(g => new
            {
                g.Key, Total = g.Count(), Undecided = g.Count(i => i.Decision == "undecided"), Approved = g.Count(i => i.Decision == "approved"),
                Skipped = g.Count(i => i.Decision == "skipped"), NeedsDate = g.Count(i => i.WhenStatus == "needs_date"), Synced = g.Count(i => i.SyncState == "synced"),
            }).ToListAsync(ct);
        var unclaimed = await db.Unclaimed.AsNoTracking().Where(u => ids.Contains(u.SyllabusId)).GroupBy(u => u.SyllabusId)
            .Select(g => new { g.Key, N = g.Count() }).ToListAsync(ct);
        var dropped = await db.Syllabi.AsNoTracking().Where(s => ids.Contains(s.Id)).Select(s => new { s.Id, s.DroppedInvalid }).ToListAsync(ct);
        var result = new Dictionary<Guid, CountsDto>();
        foreach (var d in dropped)
        {
            var i = items.FirstOrDefault(x => x.Key == d.Id);
            var u = unclaimed.FirstOrDefault(x => x.Key == d.Id)?.N ?? 0;
            result[d.Id] = new CountsDto(i?.Total ?? 0, i?.Undecided ?? 0, i?.Approved ?? 0, i?.Skipped ?? 0, i?.NeedsDate ?? 0, i?.Synced ?? 0, d.DroppedInvalid, u);
        }
        return result;
    }

    public async Task<SyllabusResponse> RepresentAsync(SyllabusDoc s, CancellationToken ct)
    {
        var counts = (await CountsAsync([s.Id], ct)).GetValueOrDefault(s.Id) ?? new CountsDto(0, 0, 0, 0, 0, 0, 0, 0);
        return s.ToResponse(counts, etags);
    }

    public async Task<ItemResponse> RepresentItemAsync(SyllabusItem i, CancellationToken ct)
    {
        var alts = i.WhenStatus == "needs_date" && i.UnknownReason == "conflicting"
            ? await db.Alternatives.AsNoTracking().Where(a => a.ItemId == i.Id).ToListAsync(ct) : [];
        return i.ToResponse(alts, etags);
    }

    public async Task<List<ItemResponse>> RepresentItemsAsync(IReadOnlyList<SyllabusItem> items, CancellationToken ct)
    {
        var ids = items.Where(i => i.WhenStatus == "needs_date" && i.UnknownReason == "conflicting").Select(i => i.Id).ToList();
        var alts = ids.Count == 0 ? [] : await db.Alternatives.AsNoTracking().Where(a => ids.Contains(a.ItemId)).ToListAsync(ct);
        return [.. items.Select(i => i.ToResponse([.. alts.Where(a => a.ItemId == i.Id)], etags))];
    }
}
