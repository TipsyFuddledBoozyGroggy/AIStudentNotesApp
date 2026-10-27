using System.Security.Claims;
using CollegeCompanion.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace CollegeCompanion.Identity;

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
}
