using System.Security.Claims;
using Tether.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace Tether.Identity;

internal sealed class HttpContextCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public Guid? UserId
    {
        get
        {
            var user = accessor.HttpContext?.User;
            if (user?.Identity?.IsAuthenticated != true) return null;
            return Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
        }
    }

    public Guid? AuthSessionId =>
        accessor.HttpContext?.User is { Identity.IsAuthenticated: true } u ? SessionPolicy.SessionId(u) : null;
}
