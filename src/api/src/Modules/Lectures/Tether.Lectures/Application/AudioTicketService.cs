using System.Security.Cryptography;
using System.Text;
using Tether.Lectures.Contracts;
using Tether.Lectures.Domain;
using Tether.Lectures.Persistence;
using Tether.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Tether.Lectures.Application;

/// <summary>
/// Redeems a ticket (C-01, C-02). Single use is enforced by deleting the row: of two concurrent redemptions only one
/// DELETE affects a row, the other gets a concurrency exception and fails. A ticket row that is found is always removed,
/// whatever the outcome, so a wrong-user or expired attempt also burns it.
/// All failure paths return the same <c>false</c> and write one content-free audit event (C-30).
/// </summary>
internal sealed class AudioTicketService(LecturesDbContext db, ICurrentUser currentUser, TimeProvider clock) : IAudioTicketConsumer
{
    public async Task<bool> ConsumeAsync(string ticket, Guid sessionId, CancellationToken ct = default)
    {
        var userId = currentUser.UserId;
        var authSid = currentUser.AuthSessionId;
        if (userId is null) return false; // anonymous: nothing to scope or audit against

        var ok = false;
        if (authSid is not null && IsPlausible(ticket))
        {
            var hash = SHA256.HashData(Encoding.ASCII.GetBytes(ticket));
            // The query filter already restricts to this user's rows; a foreign ticket is simply not found.
            var row = await db.AudioTickets.FirstOrDefaultAsync(t => t.TokenHash == hash && t.SessionId == sessionId, ct);
            if (row is not null)
            {
                var now = clock.GetUtcNow();
                var active = await db.Sessions.AsNoTracking().AnyAsync(s => s.Id == sessionId && s.State == SessionState.Active, ct);
                ok = active && row.UserId == userId && row.AuthSessionId == authSid && row.ExpiresAt > now;
                db.AudioTickets.Remove(row);
                try
                {
                    await db.SaveChangesAsync(ct);
                }
                catch (DbUpdateConcurrencyException)
                {
                    ok = false; // someone else redeemed or invalidated it first
                    db.ChangeTracker.Clear();
                }
            }
        }

        if (!ok)
        {
            db.AuditEvents.Add(new AuditEvent { Id = Ids.New(), SessionId = sessionId, Action = AuditActions.TicketRejected, OccurredAt = clock.GetUtcNow() });
            await db.SaveChangesAsync(ct);
        }
        return ok;
    }

    /// <summary>Cheap shape check (url-safe, 16..512 chars) so garbage never reaches the hasher or the database.</summary>
    private static bool IsPlausible(string? t) =>
        t is { Length: >= 16 and <= 512 } && t.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
}
