using System.Security.Claims;
using CrockeryFactory.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CrockeryFactory.Web.Auth;

/// <summary>
/// Runs on every authenticated request and can end a session that should no longer exist.
///
/// Two things are checked that a cookie alone cannot express:
///
///   * the clerk's absolute end-of-day expiry, which must hold even though the cookie
///     itself slides;
///   * that the account is still active and its security stamp still matches. Without
///     this, deactivating a user leaves them working normally until their cookie expires
///     - up to a full day under SE-06, which is not what "deactivate" means to the owner
///     who just clicked it.
///
/// It costs one primary-key lookup per request. On a LAN with a handful of users that is
/// the right trade against a revoked account that keeps writing stock movements.
/// </summary>
public sealed class SessionValidator
{
    public static async Task ValidateAsync(CookieValidatePrincipalContext context)
    {
        var principal = context.Principal;
        if (principal?.Identity?.IsAuthenticated != true)
        {
            await RejectAsync(context);
            return;
        }

        var services = context.HttpContext.RequestServices;
        var clock = services.GetRequiredService<TimeProvider>();
        var now = clock.GetUtcNow();

        var absoluteRaw = principal.FindFirstValue(SessionPolicy.AbsoluteExpiryClaim);
        if (!string.IsNullOrEmpty(absoluteRaw) &&
            DateTimeOffset.TryParse(absoluteRaw, out var absolute) &&
            now >= absolute)
        {
            await RejectAsync(context);
            return;
        }

        if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            await RejectAsync(context);
            return;
        }

        var db = services.GetRequiredService<FactoryDbContext>();

        var account = await db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.IsActive, u.SecurityStamp })
            .FirstOrDefaultAsync(context.HttpContext.RequestAborted);

        if (account is null || !account.IsActive)
        {
            await RejectAsync(context);
            return;
        }

        // The stamp changes on a password change or a forced sign-out, so an old cookie
        // issued before either stops working immediately.
        var stampInCookie = principal.FindFirstValue(ClaimTypesExtra.SecurityStamp);
        if (!string.IsNullOrEmpty(stampInCookie) &&
            !string.Equals(stampInCookie, account.SecurityStamp, StringComparison.Ordinal))
        {
            await RejectAsync(context);
        }
    }

    private static async Task RejectAsync(CookieValidatePrincipalContext context)
    {
        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
    }
}

public static class ClaimTypesExtra
{
    public const string SecurityStamp = "security_stamp";
}
