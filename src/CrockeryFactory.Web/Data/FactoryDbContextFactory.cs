using CrockeryFactory.Persistence;
using CrockeryFactory.Web.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace CrockeryFactory.Web.Data;

/// <summary>
/// Used by "dotnet ef" only. It builds the model without starting the web host, so
/// generating a migration never depends on a reachable database.
///
/// It reads the connection string from exactly the same places the running application
/// does, and in the same order. An earlier version read only an environment variable and
/// fell back to LocalDB when that was unset - so "dotnet ef database update" reported
/// success while creating the schema in a completely different instance from the one the
/// application would then talk to. That failure is silent and costs an afternoon, which
/// is why the fallback is now a hard error instead.
/// </summary>
public sealed class FactoryDbContextFactory : IDesignTimeDbContextFactory<FactoryDbContext>
{
    /// <summary>Explicit override, for pointing a migration at a database on purpose.</summary>
    public const string OverrideVariable = "CROCKERY_DESIGNTIME_CONNECTION";

    private const string ConnectionName = "FactoryDatabase";

    public FactoryDbContext CreateDbContext(string[] args)
    {
        var connectionString = ResolveConnectionString(out var source);

        // Printed because the whole class of bug this replaces was not knowing which
        // server was about to be written to.
        Console.WriteLine($"[ef] Using connection from {source}: {Describe(connectionString)}");

        var options = new DbContextOptionsBuilder<FactoryDbContext>()
            .UseSqlServer(
                connectionString,
                sql => sql.MigrationsAssembly(typeof(FactoryDbContextFactory).Assembly.FullName))
            .Options;

        return new FactoryDbContext(options, new SystemCurrentUser(), TimeProvider.System);
    }

    private static string ResolveConnectionString(out string source)
    {
        var fromEnvironment = Environment.GetEnvironmentVariable(OverrideVariable);

        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            source = OverrideVariable;
            return fromEnvironment;
        }

        var environmentName =
            Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            ?? "Development";

        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile($"appsettings.{environmentName}.json", optional: true)
            .AddUserSecrets<FactoryDbContextFactory>(optional: true)
            .AddEnvironmentVariables()
            .Build();

        var fromConfiguration = configuration.GetConnectionString(ConnectionName);

        if (!string.IsNullOrWhiteSpace(fromConfiguration))
        {
            source = $"appsettings ({environmentName})";
            return fromConfiguration;
        }

        throw new InvalidOperationException(
            $"""
             No connection string found.

             'dotnet ef' looked for, in order:
               1. the {OverrideVariable} environment variable
               2. ConnectionStrings:{ConnectionName} in appsettings.json / appsettings.{environmentName}.json
                  (resolved relative to {Directory.GetCurrentDirectory()})

             Set one of them. For example:

               export {OverrideVariable}="Server=.\\SQLEXPRESS;Database=CrockeryFactory;Trusted_Connection=True;TrustServerCertificate=True"

             There is deliberately no default: silently creating the database on a
             different server from the one the application uses is worse than failing.
             """);
    }

    /// <summary>Server and database only. The rest of a connection string may hold a password.</summary>
    private static string Describe(string connectionString)
    {
        try
        {
            var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connectionString);
            return $"server '{builder.DataSource}', database '{builder.InitialCatalog}'";
        }
        catch
        {
            return "(unreadable connection string)";
        }
    }
}
