using CrockeryFactory.Persistence;
using CrockeryFactory.Shared.Authorization;
using CrockeryFactory.Shared.Constants;
using CrockeryFactory.Shared.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CrockeryFactory.Web.Infrastructure;

/// <summary>
/// Creates the first administrator, once, on a database that has none.
///
/// A freshly migrated database contains exactly one user - the inactive 'system' account
/// that stamps audit rows - so without this a cloud deployment comes up perfectly healthy
/// and nobody can get in. The development seeder cannot fill the gap: it refuses to run
/// outside Development and refuses a database that already has data, both deliberately.
///
/// The rules that make this safe to leave switched on:
///
///  - **It runs only when there is no active user at all.** Once anybody can sign in, this
///    does nothing, ever again. It cannot be used to add a second way in later, and
///    changing the configured password does not reopen it.
///  - **The password comes from configuration, never from source.** On Azure that is an
///    app setting; there is no default, and with nothing configured the bootstrapper
///    simply declines and says so.
///  - **The password is never logged**, at any level.
///
/// It is a bootstrap, not an account manager. Change the password from inside the
/// application after the first sign-in, and delete the app setting once you have.
/// </summary>
public static class AdminBootstrapper
{
    public const string UserNameKey = "Bootstrap:AdminUserName";
    public const string PasswordKey = "Bootstrap:AdminPassword";
    public const string FullNameKey = "Bootstrap:AdminFullName";

    public static async Task RunAsync(WebApplication app)
    {
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup.Bootstrap");

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FactoryDbContext>();

        try
        {
            if (!await db.Database.CanConnectAsync()) return;

            // The whole guard. Anything signable-in means this has already been done, or
            // the database was set up another way, and either way it is not ours to touch.
            if (await db.Users.AnyAsync(u => u.IsActive)) return;

            var userName = app.Configuration[UserNameKey];
            var password = app.Configuration[PasswordKey];
            var fullName = app.Configuration[FullNameKey] ?? "Administrator";

            if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(password))
            {
                logger.LogWarning(
                    "No active user exists and no bootstrap account is configured, so nobody can sign in. " +
                    "Set {UserNameKey} and {PasswordKey} in the application settings and restart.",
                    UserNameKey, PasswordKey);

                return;
            }

            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();

            var user = new AppUser
            {
                Id = Guid.NewGuid(),
                UserName = userName.Trim(),
                FullName = fullName.Trim(),
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            var created = await users.CreateAsync(user, password);

            if (!created.Succeeded)
            {
                // Errors are descriptions of the rules, not of the password, so they are
                // safe to log and are exactly what somebody needs to fix the setting.
                logger.LogError(
                    "Could not create the bootstrap administrator: {Errors}",
                    string.Join("; ", created.Errors.Select(e => e.Description)));

                return;
            }

            await users.AddToRoleAsync(user, Roles.Administrator);
            await db.SaveChangesAsync();

            logger.LogWarning(
                "Created the first administrator '{UserName}' because the database had no active user. " +
                "Sign in, change this password from inside the application, then remove the {PasswordKey} " +
                "setting. This will not run again now that an account exists.",
                user.UserName, PasswordKey);
        }
        catch (Exception ex)
        {
            // Same reasoning as the migrator: never stop the host from starting over this.
            logger.LogError(ex, "The administrator bootstrap failed. The application will start anyway.");
        }
    }
}
