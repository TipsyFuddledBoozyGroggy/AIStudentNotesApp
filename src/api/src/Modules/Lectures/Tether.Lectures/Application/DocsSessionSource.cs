using System.Data.Common;
using Tether.Lectures.Contracts;
using Tether.Lectures.Domain;
using Tether.Lectures.Persistence;
using Tether.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Tether.Lectures.Application;

/// <summary>
/// <see cref="IDocsSessionSource"/>: the title, state and start of a lecture for the Docs export. The ambient user is the only user it acts
/// for; another user's session reads as missing (C-09). The locking variant runs ONE statement on the caller's transaction.
/// </summary>
internal sealed class DocsSessionSource(LecturesDbContext db, ICurrentUser currentUser) : IDocsSessionSource
{
    public async Task<DocsSessionFacts?> ReadAsync(Guid sessionId, CancellationToken ct = default)
    {
        if (currentUser.UserId is null) return null;
        var row = await db.Sessions.AsNoTracking().Where(s => s.Id == sessionId)
            .Select(s => new { s.Id, s.State, s.StartedAt, s.Title }).FirstOrDefaultAsync(ct);
        return row is null ? null : new DocsSessionFacts(row.Id, row.State.ToContract(), row.StartedAt, row.Title, null);
    }

    public async Task<DocsSessionFacts?> LockAndReadAsync(DbTransaction transaction, Guid sessionId, CancellationToken ct = default)
    {
        if (currentUser.UserId is not { } userId) return null;
        var conn = transaction.Connection ?? throw new InvalidOperationException("Transaction has no connection.");
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = "SELECT state, started_at, title FROM lectures.lecture_sessions WHERE id = @s AND user_id = @u FOR KEY SHARE";
        Add(cmd, "s", sessionId); Add(cmd, "u", userId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        var state = Enum.Parse<SessionState>(r.GetString(0));
        return new DocsSessionFacts(sessionId, state.ToContract(), r.GetFieldValue<DateTimeOffset>(1), r.IsDBNull(2) ? null : r.GetString(2), null);
    }

    private static void Add(DbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name; p.Value = value;
        cmd.Parameters.Add(p);
    }
}
