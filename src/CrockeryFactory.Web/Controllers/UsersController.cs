using CrockeryFactory.Application.Abstractions;
using CrockeryFactory.Application.Admin.Dtos;
using CrockeryFactory.Application.Common;
using CrockeryFactory.Persistence;
using CrockeryFactory.Shared.Authorization;
using CrockeryFactory.Shared.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrockeryFactory.Web.Controllers;

[ApiController]
[Route("api/v1/users")]
[Authorize(Policy = Policies.CanManageUsers)]
public sealed class UsersController : ControllerBase
{
    private readonly UserManager<AppUser> _users;
    private readonly FactoryDbContext _db;
    private readonly IAuditWriter _audit;

    public UsersController(UserManager<AppUser> users, FactoryDbContext db, IAuditWriter audit)
    {
        _users = users;
        _db = db;
        _audit = audit;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<UserResponse>>> List(CancellationToken ct)
    {
        var users = await _db.Users.AsNoTracking().OrderBy(u => u.FullName).ToListAsync(ct);

        var result = new List<UserResponse>(users.Count);

        foreach (var user in users)
            result.Add(await ToResponseAsync(user));

        return Ok(result);
    }

    [HttpGet("{id:guid}", Name = nameof(GetUser))]
    public async Task<ActionResult<UserResponse>> GetUser(Guid id, CancellationToken ct)
    {
        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, ct)
            ?? throw DomainException.NotFound("User", id.ToString());

        return Ok(await ToResponseAsync(user));
    }

    [HttpPost]
    public async Task<ActionResult<UserResponse>> Create(
        [FromBody] CreateUserRequest request, CancellationToken ct)
    {
        RequireKnownRole(request.Role);

        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            UserName = request.UserName?.Trim(),
            FullName = (request.FullName ?? string.Empty).Trim(),
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        if (string.IsNullOrWhiteSpace(user.UserName) || string.IsNullOrWhiteSpace(user.FullName))
        {
            throw DomainException.Validation(ErrorCodes.ValidationFailed,
                "A user needs a login name and a full name.",
                new FieldError("userName", "Required", ErrorCodes.ValidationFailed),
                new FieldError("fullName", "Required", ErrorCodes.ValidationFailed));
        }

        var created = await _users.CreateAsync(user, request.Password ?? string.Empty);

        if (!created.Succeeded)
            throw IdentityFailure(created, "The user could not be created.");

        await _users.AddToRoleAsync(user, request.Role);

        // SE-13. Who was given access, by whom, and when.
        _audit.Record(nameof(AppUser), user.Id, "Create", null,
            new { user.UserName, user.FullName, request.Role });

        await _db.SaveChangesAsync(ct);

        return CreatedAtRoute(nameof(GetUser), new { id = user.Id }, await ToResponseAsync(user));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<UserResponse>> Update(
        Guid id, [FromBody] UpdateUserRequest request, CancellationToken ct)
    {
        RequireKnownRole(request.Role);

        var user = await _users.FindByIdAsync(id.ToString())
            ?? throw DomainException.NotFound("User", id.ToString());

        var existingRoles = await _users.GetRolesAsync(user);
        var before = new { user.FullName, Roles = existingRoles.ToList() };

        user.FullName = (request.FullName ?? string.Empty).Trim();

        var updated = await _users.UpdateAsync(user);
        if (!updated.Succeeded)
            throw IdentityFailure(updated, "The user could not be updated.");

        if (!existingRoles.Contains(request.Role, StringComparer.Ordinal))
        {
            await _users.RemoveFromRolesAsync(user, existingRoles);
            await _users.AddToRoleAsync(user, request.Role);

            // A role change alters what this person can do to the factory's records, so
            // the old role is kept in the audit alongside the new one.
            await _users.UpdateSecurityStampAsync(user);
        }

        _audit.Record(nameof(AppUser), user.Id, "Update", before,
            new { user.FullName, Roles = new[] { request.Role } });

        await _db.SaveChangesAsync(ct);

        return Ok(await ToResponseAsync(user));
    }

    /// <summary>
    /// Deactivate, never delete (SE-14). Every document the user entered still points at
    /// them, and the "who recorded this" question must stay answerable.
    /// </summary>
    [HttpPost("{id:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct)
    {
        var user = await _users.FindByIdAsync(id.ToString())
            ?? throw DomainException.NotFound("User", id.ToString());

        if (id == _db.CurrentUser.UserId)
        {
            throw DomainException.Unprocessable(ErrorCodes.ValidationFailed, "Cannot deactivate yourself",
                "You cannot deactivate the account you are signed in with. " +
                "Ask another administrator to do it.");
        }

        if (!user.IsActive)
            return NoContent();

        user.IsActive = false;
        await _users.UpdateAsync(user);

        // Rolling the stamp ends their open sessions on the next request. Without it a
        // deactivated user keeps working until their cookie expires - up to a full day
        // under SE-06, which is not what "deactivate" means to whoever just clicked it.
        await _users.UpdateSecurityStampAsync(user);

        _audit.Record(nameof(AppUser), user.Id, "Deactivate",
            new { IsActive = true }, new { IsActive = false });

        await _db.SaveChangesAsync(ct);

        return NoContent();
    }

    [HttpPost("{id:guid}/reset-password")]
    public async Task<IActionResult> ResetPassword(
        Guid id, [FromBody] ResetPasswordRequest request, CancellationToken ct)
    {
        var user = await _users.FindByIdAsync(id.ToString())
            ?? throw DomainException.NotFound("User", id.ToString());

        var token = await _users.GeneratePasswordResetTokenAsync(user);
        var reset = await _users.ResetPasswordAsync(user, token, request.NewPassword ?? string.Empty);

        if (!reset.Succeeded)
            throw IdentityFailure(reset, "The password could not be reset.");

        // The reset itself is recorded; the password is not, in any form.
        _audit.Record(nameof(AppUser), user.Id, "ResetPassword", null, new { user.UserName });

        await _db.SaveChangesAsync(ct);

        return NoContent();
    }

    private static void RequireKnownRole(string? role)
    {
        if (!Roles.All.Contains(role, StringComparer.Ordinal))
        {
            throw DomainException.Validation(ErrorCodes.ValidationFailed,
                $"Role must be one of: {string.Join(", ", Roles.All)}.",
                new FieldError("role", "Not a known role", ErrorCodes.ValidationFailed));
        }
    }

    private static DomainException IdentityFailure(IdentityResult result, string detail) =>
        DomainException.Validation(ErrorCodes.ValidationFailed, detail,
            result.Errors.Select(e => new FieldError(
                e.Code.Contains("Password", StringComparison.OrdinalIgnoreCase) ? "password" : "userName",
                e.Description, ErrorCodes.ValidationFailed)).ToArray());

    private async Task<UserResponse> ToResponseAsync(AppUser user) => new(
        user.Id, user.UserName ?? string.Empty, user.FullName,
        (await _users.GetRolesAsync(user)).ToList(),
        user.IsActive, user.CreatedAt, user.LastLoginAt);
}
