using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;

namespace Tether.SharedKernel;

/// <summary>Claim types shared between the module that issues the login claims and modules that only read them.</summary>
public static class AuthClaims
{
    /// <summary>Id of the server-side login session (identity.sessions) carried in the cookie principal (ADR-016).</summary>
    public const string SessionId = "cc:sid";

    public static Guid? UserIdOf(ClaimsPrincipal? p) =>
        p?.Identity?.IsAuthenticated == true && Guid.TryParse(p.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    public static Guid? SessionIdOf(ClaimsPrincipal? p) =>
        p?.Identity?.IsAuthenticated == true && Guid.TryParse(p.FindFirstValue(SessionId), out var id) ? id : null;
}

/// <summary>
/// Scoped override of the caller identity for code that runs outside an HTTP request (hub invocations, audio socket
/// pumps, background jobs). It is only ever set by server code from a validated principal or a database row, never from
/// client input. When unset, <see cref="ICurrentUser"/> falls back to the HTTP context.
/// </summary>
public sealed class UserScope
{
    public Guid? UserId { get; private set; }
    public Guid? AuthSessionId { get; private set; }

    public void Set(Guid? userId, Guid? authSessionId)
    {
        UserId = userId;
        AuthSessionId = authSessionId;
    }

    public void Set(ClaimsPrincipal? principal) => Set(AuthClaims.UserIdOf(principal), AuthClaims.SessionIdOf(principal));
}

/// <summary>Answers whether a login session is still valid (not revoked, not expired, user not deleted). Implemented by Identity.</summary>
public interface IAuthSessionValidator
{
    Task<bool> IsValidAsync(Guid userId, Guid authSessionId, CancellationToken ct = default);
}

public static class UserScopeExtensions
{
    /// <summary>Creates a DI scope in which <see cref="ICurrentUser"/> is the given user.</summary>
    public static AsyncServiceScope CreateUserScope(this IServiceScopeFactory factory, Guid userId, Guid? authSessionId = null)
    {
        var scope = factory.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<UserScope>().Set(userId, authSessionId);
        return scope;
    }
}
