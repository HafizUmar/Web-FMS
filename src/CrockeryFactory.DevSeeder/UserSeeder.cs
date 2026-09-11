using CrockeryFactory.Persistence;
using CrockeryFactory.Shared.Authorization;
using CrockeryFactory.Shared.Constants;
using CrockeryFactory.Shared.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CrockeryFactory.DevSeeder;

/// <summary>
/// Creates the three logins a developer needs to open the application at all.
///
/// Without this a freshly migrated database has no usable account: the migration seeds
/// the roles and an inactive <c>system</c> row that exists only so seeded records have an
/// author, and it has no password hash. Everything except /health and /login is then
/// unreachable, which makes the API impossible to try.
///
/// Development only. Production provisioning is the Setup tool's job (Architecture
/// section 3.1) - it must prompt for a password rather than ship a known one.
/// </summary>
public sealed class UserSeeder
{
    /// <summary>
    /// Printed on every run so nobody has to read the source to sign in. Safe precisely
    /// because the seeder refuses to run outside Development.
    /// </summary>
    public const string DevPassword = "Factory!Pass99";

    private static readonly (string UserName, string FullName, string Role, Guid RoleId)[] Accounts =
    [
        ("owner", "Owner Sahib",  Roles.Owner,         SeedConstants.RoleIds.Owner),
        ("clerk", "Munshi",       Roles.Clerk,         SeedConstants.RoleIds.Clerk),
        ("admin", "Administrator",Roles.Administrator, SeedConstants.RoleIds.Administrator)
    ];

    private readonly FactoryDbContext _db;

    public UserSeeder(FactoryDbContext db) => _db = db;

    public async Task<IReadOnlyList<(string UserName, string Role)>> SeedAsync(CancellationToken ct = default)
    {
        // Hashed with the same hasher ASP.NET Core Identity uses at sign-in, so these
        // accounts are indistinguishable from ones created through POST /api/v1/users.
        var hasher = new PasswordHasher<AppUser>();
        var created = new List<(string, string)>();

        foreach (var (userName, fullName, role, roleId) in Accounts)
        {
            var normalised = userName.ToUpperInvariant();

            if (await _db.Users.AnyAsync(u => u.NormalizedUserName == normalised, ct))
                continue;

            var user = new AppUser
            {
                Id = Guid.NewGuid(),
                UserName = userName,
                NormalizedUserName = normalised,
                FullName = fullName,
                IsActive = true,
                EmailConfirmed = false,
                PhoneNumberConfirmed = false,
                TwoFactorEnabled = false,
                LockoutEnabled = true,
                AccessFailedCount = 0,
                SecurityStamp = Guid.NewGuid().ToString("N"),
                ConcurrencyStamp = Guid.NewGuid().ToString("N"),
                CreatedAt = DateTime.UtcNow
            };

            user.PasswordHash = hasher.HashPassword(user, DevPassword);

            _db.Users.Add(user);
            _db.UserRoles.Add(new IdentityUserRole<Guid> { UserId = user.Id, RoleId = roleId });

            created.Add((userName, role));
        }

        await _db.SaveChangesAsync(ct);

        return created;
    }
}
