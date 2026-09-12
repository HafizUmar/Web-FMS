using CrockeryFactory.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CrockeryFactory.Web.Infrastructure;

/// <summary>
/// Applies pending migrations at startup, when explicitly asked to.
///
/// The specification rules this out for the factory server, and for a good reason: a
/// migration that fails during startup leaves the application unable to start, and an
/// application that cannot start at a factory means nobody can work until somebody
/// physically reaches the machine.
///
/// It is enabled here for Azure at the owner's decision, so the two mitigations that
/// matter are built in rather than assumed:
///
///  1. **It never prevents the application from starting.** A failed migration is logged
///     as an error and the host comes up anyway. A running app with a stale schema can be
///     reached, inspected through /api/v1/health, and fixed. One that refuses to boot
///     cannot, and on App Service it simply restart-loops until the platform gives up.
///
///  2. **It is off unless switched on.** `Database:MigrateOnStartup` defaults to false, so
///     a developer's machine and the factory server behave exactly as they always have.
///     Only the cloud deployment turns it on, through an app setting.
///
/// It is still the weaker of the two approaches. A pipeline that applies a reviewed script
/// before the new code goes live tells you what changed before it changes; this tells you
/// afterwards, in a log. The upgrade path is in docs/deployment.md.
/// </summary>
public static class DatabaseMigrator
{
    public const string EnabledKey = "Database:MigrateOnStartup";

    public static async Task ApplyAsync(WebApplication app)
    {
        if (!app.Configuration.GetValue(EnabledKey, false)) return;

        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup.Migrate");

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FactoryDbContext>();

        try
        {
            var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();

            if (pending.Count == 0)
            {
                logger.LogInformation("Database is already up to date; no migrations to apply.");
                return;
            }

            logger.LogInformation(
                "Applying {Count} migration(s): {Migrations}", pending.Count, string.Join(", ", pending));

            await db.Database.MigrateAsync();

            logger.LogInformation("Migrations applied.");
        }
        catch (Exception ex)
        {
            // Deliberately swallowed. See the note above: the alternative is a host that
            // will not start, which is strictly harder to diagnose and to recover from.
            // DatabaseStartupCheck runs next and reports the resulting state plainly.
            logger.LogError(ex,
                "Could not apply migrations at startup. The application will start anyway so it can " +
                "be reached and inspected - GET /api/v1/health reports whether the schema is current. " +
                "Until this is resolved, requests that touch the affected tables will fail.");
        }
    }
}
