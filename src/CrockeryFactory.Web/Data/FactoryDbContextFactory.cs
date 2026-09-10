using CrockeryFactory.Persistence;
using CrockeryFactory.Web.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CrockeryFactory.Web.Data;

/// <summary>
/// Used by "dotnet ef" only. It builds the model without starting the web host, so
/// generating a migration never depends on a reachable database or on configuration
/// that only exists at a factory site.
/// </summary>
public sealed class FactoryDbContextFactory : IDesignTimeDbContextFactory<FactoryDbContext>
{
    public FactoryDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<FactoryDbContext>()
            .UseSqlServer(
                Environment.GetEnvironmentVariable("CROCKERY_DESIGNTIME_CONNECTION")
                    ?? "Server=(localdb)\\MSSQLLocalDB;Database=CrockeryFactory;Trusted_Connection=True;",
                sql => sql.MigrationsAssembly(typeof(FactoryDbContextFactory).Assembly.FullName))
            .Options;

        return new FactoryDbContext(options, new SystemCurrentUser(), TimeProvider.System);
    }
}
