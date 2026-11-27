using System.Security.Claims;
using Tether.Identity.Domain;
using Tether.Identity.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Tether.Identity;

/// <summary>Lifetimes and the per-request server-side session check (ADR-016).</summary>
internal static class SessionPolicy
{
    public const string SessionIdClaim = Tether.SharedKernel.AuthClaims.SessionId;
    public static readonly TimeSpan SlidingLifetime = TimeSpan.FromHours(8);
    public static readonly TimeSpan AbsoluteLifetime = TimeSpan.FromDays(14);

    /// <summary>
    /// A new session row. <see cref="Session.CreatedAt"/> is the sign-in time (auth.v1 section 6): written once here and never changed by the
    /// sliding refresh, so ADR-021's recent-sign-in check can read it.
    /// </summary>
    public static Session Create(Guid userId, TimeProvider clock)
    {
        var now = clock.GetUtcNow();
        return new Session { Id = Guid.CreateVersion7(), UserId = userId, CreatedAt = now, ExpiresAt = now + AbsoluteLifetime };
    }

    public static Guid? SessionId(ClaimsPrincipal? p) =>
        Guid.TryParse(p?.FindFirstValue(SessionIdClaim), out var id) ? id : null;

    /// <summary>Validity check for callers that hold ids rather than a principal (hub and socket revalidation, C-06).</summary>
    public static Task<bool> IsValidAsync(Guid userId, Guid sessionId, IdentityDbContext db, TimeProvider clock, bool allowDevUsers, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        return db.Sessions.AsNoTracking().AnyAsync(s =>
            s.Id == sessionId && s.UserId == userId && s.RevokedAt == null && s.ExpiresAt > now
            && db.Users.Any(u => u.Id == s.UserId && u.DeletedAt == null && (allowDevUsers || !u.ExternalSubject.StartsWith(User.DevPrefix))), ct);
    }

    /// <summary>
    /// One indexed read per request: the session must exist, belong to the claimed user, not be revoked or past its absolute expiry, and the
    /// user must not be soft-deleted. Outside Development (<paramref name="allowDevUsers"/> false) a dev-login user is never valid (ADR-022 R8).
    /// </summary>
    public static async Task<bool> IsValidAsync(ClaimsPrincipal? principal, IdentityDbContext db, TimeProvider clock, bool allowDevUsers, CancellationToken ct)
    {
        var sid = SessionId(principal);
        if (sid is null || !Guid.TryParse(principal!.FindFirstValue(ClaimTypes.NameIdentifier), out var uid)) return false;
        return await IsValidAsync(uid, sid.Value, db, clock, allowDevUsers, ct);
    }
}
