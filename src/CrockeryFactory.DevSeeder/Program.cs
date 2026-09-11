using CrockeryFactory.Domain.Abstractions;
using CrockeryFactory.DevSeeder;
using CrockeryFactory.Persistence;
using CrockeryFactory.Shared.Constants;
using Microsoft.EntityFrameworkCore;

var options = SeederOptions.Parse(args);

if (args.Length == 0 || args.Contains("--help") || args.Contains("-h"))
{
    Console.WriteLine(SeederOptions.Usage);
    return 0;
}

// Three independent refusals, because only one of them has to fail open for a demo
// database to reach a factory.

var environment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
                  ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");

if (!string.Equals(environment, "Development", StringComparison.OrdinalIgnoreCase))
{
    Console.Error.WriteLine(
        $"Refusing to run: DOTNET_ENVIRONMENT is '{environment ?? "not set"}', not 'Development'.");
    return 1;
}

if (!options.Acknowledged)
{
    Console.Error.WriteLine(
        $"Refusing to run without {SeederOptions.AcknowledgeFlag}. This writes fabricated data.");
    return 1;
}

var connectionString = options.ConnectionString
                       ?? Environment.GetEnvironmentVariable("CROCKERY_SEED_CONNECTION");

if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine(
        "No connection string. Pass --connection or set CROCKERY_SEED_CONNECTION.");
    return 1;
}

var dbOptions = new DbContextOptionsBuilder<FactoryDbContext>()
    .UseSqlServer(connectionString)
    .Options;

await using var db = new FactoryDbContext(dbOptions, new SeedUser(), TimeProvider.System);

if (!await db.Database.CanConnectAsync())
{
    Console.Error.WriteLine("Cannot reach the database. Check the connection string.");
    return 1;
}

// Logins first, so that a database seeded with transactions is one somebody can actually
// sign in to and look at. Existing accounts are left alone.
var seededUsers = await new UserSeeder(db).SeedAsync();

// The last and most important guard: a database with a dispatch in it is somebody's
// records, whatever the environment variable says.
if (await db.Dispatches.AnyAsync())
{
    Console.Error.WriteLine(
        "Refusing to run: this database already contains dispatches. Seed only an empty database.");
    return 1;
}

if (await db.Products.AnyAsync())
{
    Console.Error.WriteLine(
        "Refusing to run: this database already contains products. Seed only an empty database.");
    return 1;
}

PrintLogins(seededUsers);

var to = DateOnly.FromDateTime(DateTime.Now);
var from = to.AddMonths(-options.Months);

Console.WriteLine($"Seeding {options.Months} months of trading, {from:yyyy-MM-dd} to {to:yyyy-MM-dd}...");

var started = DateTime.UtcNow;
var summary = await new DemoDataSeeder(db).SeedAsync(from, to);
var elapsed = DateTime.UtcNow - started;

Console.WriteLine($"""

    Done in {elapsed.TotalSeconds:N1}s.

      Products           {summary.Products,8:N0}
      Customers          {summary.Customers,8:N0}
      Production entries {summary.ProductionEntries,8:N0}
      Dispatches         {summary.Dispatches,8:N0}
      Payments           {summary.Payments,8:N0}

    Stock balances were written from the running totals the generator maintained, so
    they agree with the ledger by construction. Verify with POST /api/v1/admin/rebuild-stock-balances.
    """);

PrintLogins(seededUsers);

return 0;

static void PrintLogins(IReadOnlyList<(string UserName, string Role)> users)
{
    if (users.Count == 0)
    {
        Console.WriteLine("Logins: already present, left unchanged.");
        return;
    }

    Console.WriteLine("\nSign in with any of these (development only):\n");
    foreach (var (userName, role) in users)
        Console.WriteLine($"  {userName,-8} {role,-14} password: {UserSeeder.DevPassword}");
}

/// <summary>
/// Attributes seeded rows to the system account. The seeder runs without an HTTP
/// request, so there is no principal to read one from.
/// </summary>
file sealed class SeedUser : ICurrentUser
{
    public Guid? UserId => SeedConstants.SystemUserId;
    public string? UserName => "seeder";
    public string? FullName => "Development seeder";
    public IReadOnlyCollection<string> Roles => Array.Empty<string>();
    public bool IsAuthenticated => false;
    public bool IsInRole(string role) => false;
    public Guid RequireUserId() => SeedConstants.SystemUserId;
}
