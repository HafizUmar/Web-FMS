using System.Security.Claims;
using CrockeryFactory.Shared.Authorization;

namespace CrockeryFactory.Web.Auth;

/// <summary>
/// How long a session lasts, which differs by role (SE-06, SE-07).
///
/// A clerk's session ends at close of business rather than sliding, because a clerk's
/// tablet is left on the packing bench. An owner's slides, because he is the one who
/// comes back to the screen after an hour on the floor and should not have to log in
/// again to read a number.
/// </summary>
public static class SessionPolicy
{
    /// <summary>Written at sign-in; enforced on every request thereafter.</summary>
    public const string AbsoluteExpiryClaim = "session_absolute_expiry";

    public const string FullNameClaim = "full_name";

    /// <summary>SE-07. Owner and Administrator sessions slide by this much.</summary>
    public static readonly TimeSpan SlidingWindow = TimeSpan.FromMinutes(30);

    /// <summary>SE-06. A clerk's session ends at close of business, local time.</summary>
    private static readonly TimeOnly ClerkDayEnd = new(20, 0);

    /// <summary>
    /// The moment this session must end regardless of activity, or null when the session
    /// is allowed to slide indefinitely while it is being used.
    /// </summary>
    public static DateTimeOffset? AbsoluteExpiryFor(IEnumerable<string> roles, DateTimeOffset nowLocal)
    {
        var isClerk = roles.Contains(Roles.Clerk, StringComparer.Ordinal);
        var isPrivileged = roles.Any(r =>
            r == Roles.Owner || r == Roles.Administrator);

        // Someone holding both roles gets the more permissive one: an owner who is also
        // listed as a clerk should not be logged out at eight o'clock.
        if (!isClerk || isPrivileged)
            return null;

        var endOfDay = new DateTimeOffset(
            nowLocal.Year, nowLocal.Month, nowLocal.Day,
            ClerkDayEnd.Hour, ClerkDayEnd.Minute, 0, nowLocal.Offset);

        // Logging in after eight - a late firing, an evening dispatch - gives a session
        // until eight tomorrow rather than one that has already expired.
        return nowLocal < endOfDay ? endOfDay : endOfDay.AddDays(1);
    }

    public static DateTimeOffset EffectiveExpiry(ClaimsPrincipal principal, DateTimeOffset now)
    {
        var sliding = now.Add(SlidingWindow);

        var raw = principal.FindFirstValue(AbsoluteExpiryClaim);
        if (string.IsNullOrEmpty(raw) ||
            !DateTimeOffset.TryParse(raw, out var absolute))
        {
            return sliding;
        }

        return absolute < sliding ? absolute : sliding;
    }
}
