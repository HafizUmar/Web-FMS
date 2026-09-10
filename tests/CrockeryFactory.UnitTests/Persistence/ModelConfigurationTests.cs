using CrockeryFactory.Domain.Abstractions;
using CrockeryFactory.Persistence;
using CrockeryFactory.Shared.Constants;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace CrockeryFactory.UnitTests.Persistence;

/// <summary>
/// Guards the mapping itself rather than any behaviour on top of it.
///
/// These exist because the failure they catch is silent: if a module's configurations
/// are not applied, EF maps those entities by convention instead - default schema, no
/// indexes, no check constraints - and the first symptom is a wrong database at a
/// factory, not a build error.
///
/// The model is built without a connection. Nothing here touches SQL Server.
/// </summary>
public class ModelConfigurationTests
{
    private static IModel BuildModel()
    {
        var options = new DbContextOptionsBuilder<FactoryDbContext>()
            .UseSqlServer("Server=none;Database=none;Trusted_Connection=True")
            .Options;

        using var db = new FactoryDbContext(options, new StubCurrentUser(), TimeProvider.System);
        return db.Model;
    }

    /// <summary>
    /// Seed data is trimmed out of the runtime model, so HasData assertions have to read
    /// the design-time model - the same one "dotnet ef migrations add" builds from.
    /// </summary>
    private static IModel BuildDesignTimeModel()
    {
        var options = new DbContextOptionsBuilder<FactoryDbContext>()
            .UseSqlServer("Server=none;Database=none;Trusted_Connection=True")
            .Options;

        using var db = new FactoryDbContext(options, new StubCurrentUser(), TimeProvider.System);
        return db.GetService<IDesignTimeModel>().Model;
    }

    [Theory]
    [InlineData("Product", "catalogue", "Products")]
    [InlineData("ProductPrice", "catalogue", "ProductPrices")]
    [InlineData("StockMovement", "stock", "StockMovements")]
    [InlineData("StockBalance", "stock", "StockBalances")]
    [InlineData("StockAdjustment", "stock", "StockAdjustments")]
    [InlineData("ProductionEntry", "production", "ProductionEntries")]
    [InlineData("Customer", "sales", "Customers")]
    [InlineData("Dispatch", "sales", "Dispatches")]
    [InlineData("DispatchLine", "sales", "DispatchLines")]
    [InlineData("Payment", "sales", "Payments")]
    [InlineData("ReasonCode", "shared", "ReasonCodes")]
    [InlineData("FactorySetting", "shared", "FactorySettings")]
    [InlineData("AuditEntry", "shared", "AuditEntries")]
    [InlineData("IdempotencyRecord", "shared", "IdempotencyRecords")]
    [InlineData("AppUser", "auth", "Users")]
    [InlineData("AppRole", "auth", "Roles")]
    public void Every_entity_lands_in_its_module_schema(string clrName, string schema, string table)
    {
        var entity = BuildModel().GetEntityTypes()
            .SingleOrDefault(e => e.ClrType.Name == clrName);

        Assert.NotNull(entity);
        entity.GetSchema().Should().Be(schema);
        entity.GetTableName().Should().Be(table);
    }

    [Fact]
    public void No_entity_is_left_without_a_key()
    {
        BuildModel().GetEntityTypes()
            .Where(e => e.FindPrimaryKey() is null)
            .Select(e => e.ClrType.Name)
            .Should().BeEmpty();
    }

    [Fact]
    public void Nothing_is_mapped_to_the_default_schema()
    {
        // Every table belongs to a module. A row here means a configuration was skipped.
        BuildModel().GetEntityTypes()
            .Where(e => e.GetSchema() is null)
            .Select(e => e.ClrType.Name)
            .Should().BeEmpty();
    }

    [Fact]
    public void Stock_is_keyed_on_product_and_grade_together()
    {
        var key = BuildModel().GetEntityTypes()
            .Single(e => e.ClrType.Name == "StockBalance")
            .FindPrimaryKey()!;

        key.Properties.Select(p => p.Name).Should().Equal("ProductId", "Grade");
    }

    [Fact]
    public void Every_money_column_is_decimal_18_2()
    {
        // Never float. Binary floating point cannot represent 0.10 exactly.
        var offenders = BuildModel().GetEntityTypes()
            .SelectMany(e => e.GetProperties())
            .Where(p => p.ClrType == typeof(decimal) || p.ClrType == typeof(decimal?))
            .Where(p => p.GetPrecision() != 18 || p.GetScale() != 2)
            .Select(p => $"{p.DeclaringType.ClrType.Name}.{p.Name}")
            .ToList();

        offenders.Should().BeEmpty();
    }

    [Fact]
    public void The_append_only_ledger_carries_no_rowversion()
    {
        // StockMovement is never updated, so there is nothing to be concurrent about.
        BuildModel().GetEntityTypes()
            .Single(e => e.ClrType.Name == "StockMovement")
            .GetProperties()
            .Should().NotContain(p => p.IsConcurrencyToken);
    }

    [Theory]
    [InlineData("Product")]
    [InlineData("StockBalance")]
    [InlineData("StockAdjustment")]
    [InlineData("ProductionEntry")]
    [InlineData("Customer")]
    [InlineData("Dispatch")]
    [InlineData("Payment")]
    public void Everything_two_users_could_touch_has_a_concurrency_token(string clrName)
    {
        BuildModel().GetEntityTypes()
            .Single(e => e.ClrType.Name == clrName)
            .GetProperties()
            .Should().Contain(p => p.IsConcurrencyToken && p.Name == "RowVersion");
    }

    [Fact]
    public void Reference_data_is_seeded_with_the_schema()
    {
        var model = BuildDesignTimeModel();

        Seeded(model, "ReasonCode").Should().Be(18);
        Seeded(model, "FactorySetting").Should().Be(14);
        Seeded(model, "AppRole").Should().Be(3);
        Seeded(model, "AppUser").Should().Be(1);

        static int Seeded(IModel m, string clrName) =>
            m.GetEntityTypes().Single(e => e.ClrType.Name == clrName).GetSeedData().Count();
    }

    [Fact]
    public void Seeded_identifiers_are_stable_across_model_builds()
    {
        // A Guid.NewGuid() in seed data produces a different migration on every run.
        // Building the model twice and comparing catches that at the point it is written.
        static string[] Ids(IModel m) => m.GetEntityTypes()
            .Single(e => e.ClrType.Name == "ReasonCode")
            .GetSeedData()
            .Select(d => d["Id"]!.ToString()!)
            .OrderBy(s => s)
            .ToArray();

        Ids(BuildDesignTimeModel()).Should().Equal(Ids(BuildDesignTimeModel()));
    }

    private sealed class StubCurrentUser : ICurrentUser
    {
        public Guid? UserId => SeedConstants.SystemUserId;
        public string? UserName => "test";
        public string? FullName => "Test";
        public IReadOnlyCollection<string> Roles => Array.Empty<string>();
        public bool IsAuthenticated => false;
        public bool IsInRole(string role) => false;
        public Guid RequireUserId() => SeedConstants.SystemUserId;
    }
}
