using CrockeryFactory.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CrockeryFactory.Web.Infrastructure;

/// <summary>
/// Reports, once at startup, whether the database this application is pointed at actually
/// exists and is up to date.
///
/// Migrations are deliberately not applied here (spec section 2.2): with one instance a
/// startup migration would usually work, and the one time it failed it would leave the
/// application in a crash loop at a factory nobody can reach, during working hours.
///
/// But the failure this replaces was worse than a slow first run. Without it the first
/// symptom of a missing database is the login screen showing "Internal error" and a trace
/// id - because the first thing any request touches is the user table - which says nothing
/// about a database, a server name, or a migration. The check costs one connection at
/// startup and turns that into a sentence naming the server, the database and the command
/// that fixes it.
///
/// It never throws. A database that is down at startup and up a minute later is a normal
/// thing on a factory server, and refusing to start would turn a recoverable condition
/// into an outage.
/// </summary>
public static class DatabaseStartupCheck
{
    public static async Task ReportAsync(WebApplication app)
    {
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup.Database");

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FactoryDbContext>();

        var connection = db.Database.GetConnectionString();
        var builder = SafeParse(connection);
        var server = builder?.DataSource ?? "(unknown)";
        var database = builder?.InitialCatalog ?? "(unknown)";

        try
        {
            if (!await db.Database.CanConnectAsync())
            {
                logger.LogError(
                    "Cannot reach the database. Server '{Server}', database '{Database}'. " +
                    "Either SQL Server is not running or is not reachable under that name, or " +
                    "the database has not been created yet. To create it: " +
                    "dotnet ef database update --project src/CrockeryFactory.Web. " +
                    "The connection string is ConnectionStrings:FactoryDatabase in appsettings.json. " +
                    "The application will keep running, and every request needing data will fail until this is fixed.",
                    server, database);

                return;
            }

            var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();

            if (pending.Count > 0)
            {
                logger.LogError(
                    "Database '{Database}' on '{Server}' is behind by {Count} migration(s): {Migrations}. " +
                    "Run: dotnet ef database update --project src/CrockeryFactory.Web",
                    database, server, pending.Count, string.Join(", ", pending));

                return;
            }

            // A migrated database still has no account anybody can sign in with: the only
            // seeded user is the inactive 'system' account that stamps audit rows. Saying
            // so here saves the next person the same hunt, because the symptom is a
            // correct-looking "invalid username or password" on a database that is fine.
            var canSignIn = await db.Users.AnyAsync(u => u.IsActive);

            if (!canSignIn)
            {
                logger.LogWarning(
                    "Database '{Database}' on '{Server}' is up to date but has no active user, so nobody can sign in. " +
                    "To create the development logins and some data to look at: " +
                    "dotnet run --project src/CrockeryFactory.DevSeeder -- --months 3 --i-understand",
                    database, server);

                return;
            }

            logger.LogInformation(
                "Database ready: '{Database}' on '{Server}', migrations current.", database, server);
        }
        catch (Exception ex)
        {
            // Diagnostics must never be the reason the application fails to start.
            logger.LogError(ex,
                "Could not check the database. Server '{Server}', database '{Database}'.", server, database);
        }
    }

    private static SqlConnectionStringBuilder? SafeParse(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) return null;

        try
        {
            return new SqlConnectionStringBuilder(connectionString);
        }
        catch (ArgumentException)
        {
            // A malformed connection string is itself worth reporting, but not by throwing
            // out of a log line - the caller still names what it can.
            return null;
        }
    }
}
