using System.Security.Claims;
using Tether.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace Tether.Identity;

internal sealed class HttpContextCurrentUser(IHttpContextAccessor accessor, UserScope scope) : ICurrentUser
{
    public Guid? UserId
    {
        get
        {
            if (scope.UserId is { } overridden) return overridden; // set only by server code (hub, socket, jobs)
            var user = accessor.HttpContext?.User;
            if (user?.Identity?.IsAuthenticated != true) return null;
            return Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
        }
    }

    public Guid? AuthSessionId
    {
        get
        {
            if (scope.UserId is not null) return scope.AuthSessionId;
            return accessor.HttpContext?.User is { Identity.IsAuthenticated: true } u ? SessionPolicy.SessionId(u) : null;
        }
    }
}
