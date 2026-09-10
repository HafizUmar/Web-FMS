using System.Security.Claims;
using CrockeryFactory.Application.Common;
using CrockeryFactory.Persistence;
using CrockeryFactory.Shared.Identity;
using CrockeryFactory.Web.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace CrockeryFactory.Web.Controllers;

[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly UserManager<AppUser> _users;
    private readonly SignInManager<AppUser> _signIn;
    private readonly FactoryDbContext _db;
    private readonly TimeProvider _clock;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        UserManager<AppUser> users,
        SignInManager<AppUser> signIn,
        FactoryDbContext db,
        TimeProvider clock,
        ILogger<AuthController> logger)
    {
        _users = users;
        _signIn = signIn;
        _db = db;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>
    /// Sets an HttpOnly, SameSite=Strict, Secure cookie. No token is returned in the
    /// body: on a same-origin LAN application a cookie the script cannot read is
    /// strictly safer than a token the script must store somewhere (SE-05).
    /// </summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType<LoginResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<LoginResponse>> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.UserName) || string.IsNullOrWhiteSpace(request.Password))
            throw InvalidCredentials();

        var user = await _users.FindByNameAsync(request.UserName.Trim());

        if (user is null)
        {
            // Deliberately the same work and the same answer as a wrong password, so the
            // response time and the body both refuse to say whether the name exists.
            await Task.Delay(Random.Shared.Next(60, 140), ct);
            throw InvalidCredentials();
        }

        // CheckPasswordSignIn counts the failure and applies lockout (SE-04: ten
        // consecutive failures, fifteen minutes). lockoutOnFailure must stay true -
        // without it the lockout policy is configured and never enforced.
        var result = await _signIn.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);

        if (result.IsLockedOut)
        {
            _logger.LogWarning("Locked-out account attempted login: {UserName}", user.UserName);

            throw new DomainException(
                ErrorCodes.AccountLocked, "Account locked", StatusCodes.Status423Locked,
                "This account is locked after too many failed attempts. Try again in 15 minutes, " +
                "or ask an administrator to reset the password.");
        }

        if (!result.Succeeded)
            throw InvalidCredentials();

        // Checked only after the password is verified. Answering USER_INACTIVE to anyone
        // who guesses a username would turn this endpoint into a way to enumerate staff.
        if (!user.IsActive)
        {
            throw DomainException.Forbidden(ErrorCodes.UserInactive,
                "This account has been deactivated. Ask an administrator to reactivate it.");
        }

        var roles = (await _users.GetRolesAsync(user)).ToList();
        var now = _clock.GetUtcNow();
        var absoluteExpiry = SessionPolicy.AbsoluteExpiryFor(roles, now.ToLocalTime());

        var identity = new ClaimsIdentity(IdentityConstants.ApplicationScheme);
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()));
        identity.AddClaim(new Claim(ClaimTypes.Name, user.UserName!));
        identity.AddClaim(new Claim(SessionPolicy.FullNameClaim, user.FullName));
        identity.AddClaim(new Claim(ClaimTypesExtra.SecurityStamp, user.SecurityStamp ?? string.Empty));

        foreach (var role in roles)
            identity.AddClaim(new Claim(ClaimTypes.Role, role));

        if (absoluteExpiry is { } hardStop)
            identity.AddClaim(new Claim(SessionPolicy.AbsoluteExpiryClaim, hardStop.ToString("O")));

        await HttpContext.SignInAsync(
            IdentityConstants.ApplicationScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = false, IssuedUtc = now });

        user.LastLoginAt = now.UtcDateTime;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("{UserName} signed in as {Roles}", user.UserName, string.Join(", ", roles));

        var expiresAt = absoluteExpiry is { } stop && stop < now.Add(SessionPolicy.SlidingWindow)
            ? stop
            : now.Add(SessionPolicy.SlidingWindow);

        return Ok(new LoginResponse(
            user.Id, user.UserName!, user.FullName,
            roles, PolicyMap.PermissionsFor(roles), expiresAt.UtcDateTime));
    }

    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
        return NoContent();
    }

    [HttpGet("me")]
    [ProducesResponseType<CurrentUserResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<CurrentUserResponse>> Me()
    {
        var user = await _users.GetUserAsync(User)
            ?? throw new DomainException(ErrorCodes.Unauthenticated, "Not signed in",
                StatusCodes.Status401Unauthorized, "This session is no longer valid. Sign in again.");

        var roles = (await _users.GetRolesAsync(user)).ToList();

        return Ok(new CurrentUserResponse(
            user.Id, user.UserName!, user.FullName,
            roles, PolicyMap.PermissionsFor(roles),
            SessionPolicy.EffectiveExpiry(User, _clock.GetUtcNow()).UtcDateTime));
    }

    [HttpPost("change-password")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
    {
        var user = await _users.GetUserAsync(User)
            ?? throw new DomainException(ErrorCodes.Unauthenticated, "Not signed in",
                StatusCodes.Status401Unauthorized, "This session is no longer valid. Sign in again.");

        var result = await _users.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);

        if (!result.Succeeded)
        {
            var errors = result.Errors
                .Select(e => new FieldError(
                    e.Code.Contains("Password", StringComparison.OrdinalIgnoreCase) &&
                    !e.Code.Contains("Mismatch", StringComparison.OrdinalIgnoreCase)
                        ? "newPassword"
                        : "currentPassword",
                    e.Description, ErrorCodes.ValidationFailed))
                .ToArray();

            throw DomainException.Validation(ErrorCodes.ValidationFailed,
                "The password could not be changed.", errors);
        }

        // Changing the password rolls the security stamp, so every other session this
        // user has open stops working on its next request - which is the point of
        // changing a password you think someone else has seen.
        await _users.UpdateSecurityStampAsync(user);
        await HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);

        return NoContent();
    }

    private static DomainException InvalidCredentials() =>
        new(ErrorCodes.InvalidCredentials, "Invalid credentials", StatusCodes.Status401Unauthorized,
            "The username or password is not correct.");
}
