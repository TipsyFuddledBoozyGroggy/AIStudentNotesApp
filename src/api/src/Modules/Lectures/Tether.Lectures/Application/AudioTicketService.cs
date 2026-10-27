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
/// DELETE affects a row, the other gets a concurrency exception and fails. A ticket row that is visible to the caller is
/// removed whatever the outcome (expired, wrong login session). A different user cannot even see the row (query filter),
/// so a wrong-user attempt does NOT burn the owner's ticket; that is deliberate, do not "fix" it.
/// All failure paths return the same <c>false</c>. Failures write at most one content-free audit event per user per
/// minute (C-30, B1-05), and record the session id only if that session belongs to the caller, so a caller cannot
/// create unbounded rows or rows carrying ids of their choosing.
/// </summary>
internal sealed class AudioTicketService(LecturesDbContext db, ICurrentUser currentUser, TimeProvider clock) : IAudioTicketConsumer
{
    public static readonly TimeSpan RejectAuditWindow = TimeSpan.FromMinutes(1);

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
            var now = clock.GetUtcNow();
            var since = now - RejectAuditWindow;
            var recent = await db.AuditEvents.AsNoTracking()
                .AnyAsync(a => a.Action == AuditActions.TicketRejected && a.OccurredAt > since, ct);
            if (!recent)
            {
                var owned = await db.Sessions.AsNoTracking().AnyAsync(s => s.Id == sessionId, ct);
                db.AuditEvents.Add(new AuditEvent { Id = Ids.New(), SessionId = owned ? sessionId : null, Action = AuditActions.TicketRejected, OccurredAt = now });
                await db.SaveChangesAsync(ct);
            }
        }
        return ok;
    }

    /// <summary>Cheap shape check (url-safe, 16..512 chars) so garbage never reaches the hasher or the database.</summary>
    private static bool IsPlausible(string? t) =>
        t is { Length: >= 16 and <= 512 } && t.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
}
