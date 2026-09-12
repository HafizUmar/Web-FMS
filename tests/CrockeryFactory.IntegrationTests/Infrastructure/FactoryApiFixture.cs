using CrockeryFactory.Persistence;
using CrockeryFactory.Shared.Constants;
using CrockeryFactory.Domain.Enums;
using CrockeryFactory.Modules.Staff.Entities;
using CrockeryFactory.Shared.Authorization;
using CrockeryFactory.Shared.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace CrockeryFactory.IntegrationTests.Infrastructure;

/// <summary>
/// Drives the real host against a real SQL Server.
///
/// Deliberately not an in-memory or SQLite provider: the rules this suite exists to
/// prove - the non-negative check constraint, the filtered unique index on current
/// prices, and rowversion concurrency - are all enforced by SQL Server and simply do
/// not exist on a fake one. A green suite on a fake provider would be worse than no
/// suite, because it would be believed.
///
/// The connection comes from CROCKERY_TEST_CONNECTION. Each run gets its own database,
/// created by migration and dropped afterwards.
/// </summary>
public sealed class FactoryApiFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string OwnerUserName = "owner";
    public const string ClerkUserName = "clerk";
    public const string AdminUserName = "admin";
    public const string InactiveUserName = "gone";
    public const string Password = "Factory!Pass99";

    private readonly string _databaseName = $"CrockeryTest_{Guid.NewGuid():N}";
    private string _connectionString = string.Empty;

    public static string? BaseConnectionString =>
        Environment.GetEnvironmentVariable("CROCKERY_TEST_CONNECTION");

    public static bool SqlServerAvailable => !string.IsNullOrWhiteSpace(BaseConnectionString);

    public async Task InitializeAsync()
    {
        if (!SqlServerAvailable)
            return;

        _connectionString = new SqlConnectionStringBuilder(BaseConnectionString!)
        {
            InitialCatalog = _databaseName
        }.ConnectionString;

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FactoryDbContext>();

        await db.Database.MigrateAsync();
        await SeedUsersAsync(scope.ServiceProvider);
    }

    /// <summary>
    /// An https base address, because the session cookie is issued with Secure=Always and
    /// the handler's cookie container will not send such a cookie over http. TestServer
    /// ignores the scheme; without it every authenticated test fails as unauthenticated
    /// for a reason that has nothing to do with the code under test.
    /// </summary>
    /// <summary>
    /// Writes attendance straight to the database, for a day the API would rightly refuse
    /// as too far back. Used only to set up an older week worth running payroll against -
    /// the backdating rule is itself covered by a test that goes through the endpoint.
    /// </summary>
    public async Task SeedAttendanceAsync(Guid employeeId, DateOnly date, AttendanceStatus status)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FactoryDbContext>();

        var already = await db.AttendanceRecords
            .AnyAsync(a => a.EmployeeId == employeeId && a.AttendanceDate == date);

        if (already) return;

        db.AttendanceRecords.Add(new AttendanceRecord
        {
            Id = Guid.NewGuid(),
            EmployeeId = employeeId,
            AttendanceDate = date,
            Status = status,
            OvertimeHours = 0m,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = SeedConstants.SystemUserId
        });

        await db.SaveChangesAsync();
    }

    public HttpClient CreateApiClient() => CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
        HandleCookies = true
    });

    public new async Task DisposeAsync()
    {
        if (SqlServerAvailable)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<FactoryDbContext>();
            await db.Database.EnsureDeletedAsync();
        }

        await base.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Development);

        // The host logs every SQL statement at Information in Development, which buries
        // an actual test failure in several hundred lines of query text.
        builder.ConfigureLogging(logging =>
        {
            logging.ClearProviders();
            logging.SetMinimumLevel(LogLevel.Warning);
        });

        builder.ConfigureServices(services =>
        {
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<FactoryDbContext>));

            if (descriptor is not null)
                services.Remove(descriptor);

            services.AddDbContext<FactoryDbContext>(options =>
                options.UseSqlServer(_connectionString,
                    sql => sql.MigrationsAssembly(typeof(Program).Assembly.FullName)));
        });
    }

    private static async Task SeedUsersAsync(IServiceProvider services)
    {
        var roles = services.GetRequiredService<RoleManager<AppRole>>();
        var users = services.GetRequiredService<UserManager<AppUser>>();

        foreach (var role in Roles.All)
        {
            if (!await roles.RoleExistsAsync(role))
                await roles.CreateAsync(new AppRole { Id = Guid.NewGuid(), Name = role });
        }

        await CreateAsync(users, OwnerUserName, "Owner Sahib", Roles.Owner, isActive: true);
        await CreateAsync(users, ClerkUserName, "Munshi", Roles.Clerk, isActive: true);
        await CreateAsync(users, AdminUserName, "Administrator", Roles.Administrator, isActive: true);
        await CreateAsync(users, InactiveUserName, "Departed Clerk", Roles.Clerk, isActive: false);
    }

    private static async Task CreateAsync(
        UserManager<AppUser> users, string userName, string fullName, string role, bool isActive)
    {
        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            UserName = userName,
            FullName = fullName,
            IsActive = isActive,
            CreatedAt = DateTime.UtcNow
        };

        var created = await users.CreateAsync(user, Password);
        if (!created.Succeeded)
        {
            throw new InvalidOperationException(
                $"Could not seed test user '{userName}': " +
                string.Join("; ", created.Errors.Select(e => e.Description)));
        }

        await users.AddToRoleAsync(user, role);
    }
}

/// <summary>Skips the whole suite cleanly when no SQL Server is configured.</summary>
public sealed class RequiresSqlServerFactAttribute : FactAttribute
{
    public RequiresSqlServerFactAttribute()
    {
        if (!FactoryApiFixture.SqlServerAvailable)
            Skip = "Set CROCKERY_TEST_CONNECTION to run integration tests against SQL Server.";
    }
}

public sealed class RequiresSqlServerTheoryAttribute : TheoryAttribute
{
    public RequiresSqlServerTheoryAttribute()
    {
        if (!FactoryApiFixture.SqlServerAvailable)
            Skip = "Set CROCKERY_TEST_CONNECTION to run integration tests against SQL Server.";
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<FactoryApiFixture>
{
    public const string Name = "api";
}
