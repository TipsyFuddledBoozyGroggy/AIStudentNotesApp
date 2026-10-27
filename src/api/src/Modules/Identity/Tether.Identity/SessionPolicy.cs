using System.Security.Claims;
using Tether.Identity.Domain;
using Tether.Identity.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Tether.Identity;

/// <summary>Lifetimes and the per-request server-side session check (ADR-016).</summary>
internal static class SessionPolicy
{
    public const string SessionIdClaim = "cc:sid";
    public static readonly TimeSpan SlidingLifetime = TimeSpan.FromHours(8);
    public static readonly TimeSpan AbsoluteLifetime = TimeSpan.FromDays(14);

    public static Session Create(Guid userId, TimeProvider clock)
    {
        var now = clock.GetUtcNow();
        return new Session { Id = Guid.CreateVersion7(), UserId = userId, CreatedAt = now, ExpiresAt = now + AbsoluteLifetime };
    }

    public static Guid? SessionId(ClaimsPrincipal? p) =>
        Guid.TryParse(p?.FindFirstValue(SessionIdClaim), out var id) ? id : null;

    /// <summary>
    /// One indexed read per request: the session must exist, belong to the claimed user, not be revoked or past
    /// its absolute expiry, and the user must not be soft-deleted.
    /// </summary>
    public static async Task<bool> IsValidAsync(ClaimsPrincipal? principal, IdentityDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var sid = SessionId(principal);
        if (sid is null || !Guid.TryParse(principal!.FindFirstValue(ClaimTypes.NameIdentifier), out var uid)) return false;
        var now = clock.GetUtcNow();
        return await db.Sessions.AsNoTracking().AnyAsync(s =>
            s.Id == sid && s.UserId == uid && s.RevokedAt == null && s.ExpiresAt > now
            && db.Users.Any(u => u.Id == s.UserId && u.DeletedAt == null), ct);
    }
}
