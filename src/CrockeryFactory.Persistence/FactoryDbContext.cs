using System.Reflection;
using CrockeryFactory.Domain.Abstractions;
using CrockeryFactory.Domain.ValueObjects;
using CrockeryFactory.Modules.Catalogue.Entities;
using CrockeryFactory.Modules.Production.Entities;
using CrockeryFactory.Modules.Sales.Entities;
using CrockeryFactory.Modules.Staff.Entities;
using CrockeryFactory.Modules.Stock.Entities;
using CrockeryFactory.Persistence.Converters;
using CrockeryFactory.Shared.Entities;
using CrockeryFactory.Shared.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace CrockeryFactory.Persistence;

public class FactoryDbContext : IdentityDbContext<AppUser, AppRole, Guid>
{
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _timeProvider;

    public FactoryDbContext(
        DbContextOptions<FactoryDbContext> options,
        ICurrentUser currentUser,
        TimeProvider timeProvider) : base(options)
    {
        _currentUser = currentUser;
        _timeProvider = timeProvider;
    }

    /// <summary>The authenticated caller, for services that stamp CreatedByUserId.</summary>
    public ICurrentUser CurrentUser => _currentUser;

    /// <summary>Injected rather than DateTime.UtcNow so that date-window rules are testable.</summary>
    public TimeProvider Clock => _timeProvider;

    // Catalogue
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductPrice> ProductPrices => Set<ProductPrice>();

    // Stock
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<StockBalance> StockBalances => Set<StockBalance>();
    public DbSet<StockAdjustment> StockAdjustments => Set<StockAdjustment>();

    // Production
    public DbSet<ProductionEntry> ProductionEntries => Set<ProductionEntry>();

    // Sales
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Dispatch> Dispatches => Set<Dispatch>();
    public DbSet<DispatchLine> DispatchLines => Set<DispatchLine>();
    public DbSet<Payment> Payments => Set<Payment>();

    // Staff
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<EmployeeWageRate> EmployeeWageRates => Set<EmployeeWageRate>();
    public DbSet<AttendanceRecord> AttendanceRecords => Set<AttendanceRecord>();
    public DbSet<PayrollRun> PayrollRuns => Set<PayrollRun>();
    public DbSet<PayrollLine> PayrollLines => Set<PayrollLine>();

    // Shared
    public DbSet<ReasonCode> ReasonCodes => Set<ReasonCode>();
    public DbSet<FactorySetting> FactorySettings => Set<FactorySetting>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        var assembly = Assembly.GetExecutingAssembly();

        // Applied per module rather than as one blanket scan, so the module boundary
        // stays visible here. Adding a module is one line; forgetting one is a missing
        // table at migration time rather than a silent default mapping.
        ApplyModule(b, assembly, ModuleNamespaces.Catalogue);
        ApplyModule(b, assembly, ModuleNamespaces.Stock);
        ApplyModule(b, assembly, ModuleNamespaces.Production);
        ApplyModule(b, assembly, ModuleNamespaces.Sales);
        ApplyModule(b, assembly, ModuleNamespaces.Staff);
        ApplyModule(b, assembly, ModuleNamespaces.Shared);
    }

    private static void ApplyModule(ModelBuilder b, Assembly assembly, string moduleNamespace)
    {
        // The trailing dot makes a namespace that *ends* with the module segment match
        // too - "...Configurations.Stock" as well as "...Stock.Something".
        bool Matches(Type t) =>
            t.Namespace is not null &&
            (t.Namespace + ".").Contains(moduleNamespace, StringComparison.Ordinal);

        // A predicate that matches nothing applies nothing, silently, and EF then maps
        // those entities by convention: no schema, no indexes, no check constraints, and
        // a composite key quietly missing. That is a wrong database rather than a build
        // error, so the empty case is made loud here instead.
        var matched = assembly.GetTypes().Count(t =>
            Matches(t) &&
            !t.IsAbstract &&
            t.GetInterfaces().Any(i =>
                i.IsGenericType &&
                i.GetGenericTypeDefinition() == typeof(IEntityTypeConfiguration<>)));

        if (matched == 0)
        {
            throw new InvalidOperationException(
                $"No IEntityTypeConfiguration<> was found for module namespace '{moduleNamespace}'. " +
                "Either the module has no configurations yet and the call should be removed, " +
                "or a namespace was renamed and its entities are about to be mapped by convention.");
        }

        b.ApplyConfigurationsFromAssembly(assembly, Matches);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder c)
    {
        base.ConfigureConventions(c);

        // Every decimal is money unless a config says otherwise. decimal(18,2) rather
        // than float: binary floating point cannot represent 0.10, and a bill that does
        // not add up is the one bug the client will certainly notice.
        c.Properties<decimal>().HavePrecision(18, 2);
        c.Properties<string>().HaveMaxLength(256);
        c.Properties<Money>().HaveConversion<MoneyConverter>();
    }
}
