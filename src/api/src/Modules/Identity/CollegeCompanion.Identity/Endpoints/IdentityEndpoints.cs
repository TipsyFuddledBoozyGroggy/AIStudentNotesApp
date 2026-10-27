using System.Security.Claims;
using System.Text.RegularExpressions;
using CollegeCompanion.Identity.Domain;
using CollegeCompanion.Identity.Persistence;
using CollegeCompanion.SharedKernel;
using CollegeCompanion.SharedKernel.Http;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace CollegeCompanion.Identity.Endpoints;

internal sealed record DevLoginRequest(string? Email);
internal sealed record MeResponse(Guid Id, string Email, string DisplayName);

internal static partial class IdentityEndpoints
{
    [GeneratedRegex(@"^[^\s@]+@[^\s@]+\.[^\s@]+$")]
    private static partial Regex EmailPattern();

    public static void Map(IEndpointRouteBuilder api, bool devLoginEnabled)
    {
        api.MapGet("/me", GetMe);

        // Idempotent and safe when signed out; still subject to the Origin check.
        api.MapPost("/auth/logout", async (HttpContext ctx, IdentityDbContext db, TimeProvider clock) =>
        {
            // Revoke server-side first so a copied cookie dies too (F-01); only this device's session.
            if (SessionPolicy.SessionId(ctx.User) is { } sid)
            {
                var now = clock.GetUtcNow();
                await db.Sessions.Where(s => s.Id == sid && s.RevokedAt == null)
                    .ExecuteUpdateAsync(u => u.SetProperty(s => s.RevokedAt, now), ctx.RequestAborted);
            }
            await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.NoContent();
        }).AllowAnonymous();

        if (devLoginEnabled)
        {
            api.MapPost("/dev/login", DevLogin).AllowAnonymous().RequireRateLimiting("auth");
        }
    }

    private static async Task<IResult> GetMe(ICurrentUser current, IdentityDbContext db, CancellationToken ct)
    {
        var id = current.UserId;
        if (id is null) return Problems.Unauthenticated();
        var user = await db.Users.AsNoTracking().Where(u => u.Id == id && u.DeletedAt == null).FirstOrDefaultAsync(ct);
        return user is null ? Problems.Unauthenticated() : Results.Ok(new MeResponse(user.Id, user.Email, user.DisplayName));
    }

    private static async Task<IResult> DevLogin(DevLoginRequest? body, HttpContext ctx, IdentityDbContext db,
        TimeProvider clock, CancellationToken ct)
    {
        var email = body?.Email?.Trim().ToLowerInvariant() ?? "";
        if (email.Length is 0 or > 320 || !EmailPattern().IsMatch(email))
            return Problems.Validation("email", "Enter a valid email.");

        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);
        if (user is null)
        {
            user = new User
            {
                Id = Ids.New(), ExternalSubject = "dev:" + email, Email = email,
                DisplayName = DisplayNameFrom(email), CreatedAt = clock.GetUtcNow(),
            };
            db.Users.Add(user);
            await db.SaveChangesAsync(ct);
        }
        if (user.DeletedAt is not null) return Problems.Validation("email", "Enter a valid email.");
        await SignInAsync(ctx, db, clock, user);
        return Results.NoContent();
    }

    /// <summary>Creates the server-side session row (ADR-016), then issues the cookie that points at it.</summary>
    internal static async Task SignInAsync(HttpContext ctx, IdentityDbContext db, TimeProvider clock, User user)
    {
        var session = SessionPolicy.Create(user.Id, clock);
        db.Sessions.Add(session);
        await db.SaveChangesAsync(ctx.RequestAborted);
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()), new Claim(SessionPolicy.SessionIdClaim, session.Id.ToString())],
            CookieAuthenticationDefaults.AuthenticationScheme);
        await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = false });
    }

    internal static string DisplayNameFrom(string email)
    {
        var local = email[..email.IndexOf('@')];
        return local.Length == 0 ? email : char.ToUpperInvariant(local[0]) + local[1..];
    }
}
