using Bogus;
using CrockeryFactory.Domain.Enums;
using CrockeryFactory.Domain.ValueObjects;
using CrockeryFactory.Modules.Catalogue.Entities;
using CrockeryFactory.Modules.Production.Entities;
using CrockeryFactory.Modules.Sales.Entities;
using CrockeryFactory.Modules.Stock.Entities;
using CrockeryFactory.Persistence;
using CrockeryFactory.Shared.Constants;
using Microsoft.EntityFrameworkCore;

namespace CrockeryFactory.DevSeeder;

/// <summary>
/// Generates a database that the application could plausibly have produced.
///
/// The part that matters is not the Bogus syntax. Random rows written straight into the
/// tables would violate BR-01 within a day of simulated trading - a dispatch of 500 cups
/// against 80 in stock - and a database that could not have been created by the
/// application is useless for testing the rules it is supposed to exercise.
///
/// So the generator walks forward one day at a time and maintains a running balance,
/// exactly as the real StockService does. Where the stock is not there, the line is
/// skipped rather than clamped: clamping to whatever is available would produce a
/// suspiciously tidy database in which nothing is ever short.
/// </summary>
public sealed class DemoDataSeeder
{
    /// <summary>Fixed, so the same command produces the same database every time.</summary>
    private const int Seed = 20260910;

    private static readonly string[] Cities =
        ["Gujrat", "Gujranwala", "Lahore", "Sialkot", "Faisalabad", "Jhelum", "Karachi"];

    private static readonly (string Code, string Name, int Ml, decimal First, decimal Second)[] Catalogue =
    [
        ("CUP-ESP-01", "Ristretto espresso cup", 60,  85m,  50m),
        ("CUP-CAP-02", "Cappuccino cup",        180, 120m,  70m),
        ("MUG-DIN-03", "Diner mug",             340, 150m,  90m),
        ("MUG-CAM-04", "Campfire mug",          400, 175m, 105m),
        ("CUP-TEA-05", "Teacup and saucer",     200, 210m, 125m),
        ("MUG-PRO-06", "Promotional mug",       300, 130m,  78m),
        ("CUP-CHA-07", "Chai cutting cup",       90,  55m,  33m),
        ("MUG-HER-08", "Heritage mug",          350, 260m, 155m)
    ];

    private readonly FactoryDbContext _db;
    private readonly Faker _faker = new() { Random = new Randomizer(Seed) };
    private readonly Dictionary<StockKey, int> _balances = [];
    private readonly Dictionary<string, int> _sequences = [];

    public DemoDataSeeder(FactoryDbContext db) => _db = db;

    public async Task<SeedSummary> SeedAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        Randomizer.Seed = new Random(Seed);

        var products = await SeedCatalogueAsync(ct);
        var customers = SeedCustomers();

        _db.Customers.AddRange(customers);
        await _db.SaveChangesAsync(ct);

        var summary = await GenerateTransactionsAsync(products, customers, from, to, ct);

        await WriteBalancesAsync(ct);

        return summary;
    }

    private async Task<List<Product>> SeedCatalogueAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var systemUserId = SeedConstants.SystemUserId;

        // Fixed rather than faked. The catalogue is the first thing the client looks at,
        // and "Gorgeous Rubber Chair" in a crockery factory ends the demo.
        var products = Catalogue.Select(p => new Product
        {
            Id = Guid.NewGuid(),
            Code = p.Code,
            Name = p.Name,
            CapacityMl = p.Ml,
            IsActive = true,
            CreatedAt = now,
            CreatedByUserId = systemUserId
        }).ToList();

        _db.Products.AddRange(products);

        for (var i = 0; i < products.Count; i++)
        {
            foreach (var (grade, rate) in new[]
                     {
                         (QualityGrade.First, Catalogue[i].First),
                         (QualityGrade.Second, Catalogue[i].Second)
                     })
            {
                _db.ProductPrices.Add(new ProductPrice
                {
                    Id = Guid.NewGuid(),
                    ProductId = products[i].Id,
                    Grade = grade,
                    UnitRate = rate,
                    EffectiveFrom = DateOnly.FromDateTime(now).AddYears(-1),
                    CreatedAt = now,
                    CreatedByUserId = systemUserId
                });
            }
        }

        await _db.SaveChangesAsync(ct);

        return products;
    }

    private List<Customer> SeedCustomers()
    {
        var now = DateTime.UtcNow;

        var faker = new Faker<Customer>()
            .UseSeed(Seed)
            .RuleFor(c => c.Id, _ => Guid.NewGuid())
            .RuleFor(c => c.Name, f => $"{f.Company.CompanyName()} Traders")
            .RuleFor(c => c.City, f => f.PickRandom(Cities))
            .RuleFor(c => c.Phone, f => $"03{f.Random.Int(0, 4)}{f.Random.Number(10000000, 99999999)}")
            .RuleFor(c => c.OpeningBalance, f => f.Random.Bool(0.3f)
                ? Math.Round(f.Random.Decimal(5_000, 150_000), 2)
                : 0m)
            .RuleFor(c => c.IsActive, f => f.Random.Bool(0.92f))
            .RuleFor(c => c.CreatedAt, _ => now)
            .RuleFor(c => c.CreatedByUserId, _ => SeedConstants.SystemUserId);

        var customers = faker.Generate(45);

        for (var i = 0; i < customers.Count; i++)
        {
            customers[i].Code = $"C-{i + 1:D4}";

            if (customers[i].OpeningBalance != 0m)
                customers[i].OpeningBalanceAsOf = DateOnly.FromDateTime(now).AddYears(-1);
        }

        return customers;
    }

    private async Task<SeedSummary> GenerateTransactionsAsync(
        List<Product> products, List<Customer> customers, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var rates = await _db.ProductPrices.AsNoTracking()
            .Where(pp => pp.EffectiveTo == null)
            .ToDictionaryAsync(pp => new StockKey(pp.ProductId, pp.Grade), pp => pp.UnitRate, ct);

        var activeCustomers = customers.Where(c => c.IsActive).ToList();

        var productionCount = 0;
        var dispatchCount = 0;
        var paymentCount = 0;

        for (var day = from; day <= to; day = day.AddDays(1))
        {
            if (day.DayOfWeek == DayOfWeek.Sunday)
                continue;

            // Wedding season and the run-up to Eid are the busy months (NFR SC-07). A
            // flat year would hide exactly the load the performance target is about.
            var seasonal = day.Month is 10 or 11 or 12 or 3 or 4 ? 2.5 : 1.0;

            foreach (var _ in Enumerable.Range(0, _faker.Random.Int(2, 4)))
            {
                AddProduction(_faker.PickRandom(products), day);
                productionCount++;
            }

            foreach (var _ in Enumerable.Range(0, (int)(_faker.Random.Int(3, 8) * seasonal)))
            {
                if (AddDispatch(_faker.PickRandom(activeCustomers), products, rates, day))
                    dispatchCount++;
            }

            // Money comes in more slowly than goods go out, which is what makes the
            // outstanding report worth having.
            foreach (var _ in Enumerable.Range(0, _faker.Random.Int(1, 4)))
            {
                AddPayment(_faker.PickRandom(activeCustomers), day);
                paymentCount++;
            }

            // Batched. Saving per row turns a five-year dataset into an afternoon.
            if (day.Day % 7 == 0)
                await _db.SaveChangesAsync(ct);
        }

        await _db.SaveChangesAsync(ct);

        return new SeedSummary(products.Count, customers.Count, productionCount, dispatchCount, paymentCount);
    }

    private void AddProduction(Product product, DateOnly day)
    {
        var fired = _faker.Random.Int(400, 2_400);

        // Loss rates that look like a kiln rather than a uniform distribution.
        var broken = (int)(fired * _faker.Random.Double(0.02, 0.09));
        var seconds = (int)(fired * _faker.Random.Double(0.04, 0.14));
        var good = fired - broken - seconds;

        var entry = new ProductionEntry
        {
            Id = Guid.NewGuid(),
            EntryNumber = NextNumber("P", day),
            ProductId = product.Id,
            EntryDate = day,
            QuantityGood = good,
            QuantitySeconds = seconds,
            QuantityBroken = broken,
            BreakageReasonCodeId = broken > 0 ? SeedConstants.BreakageReasonIds.Crack : null,
            Status = DocumentStatus.Active,
            CreatedAt = day.ToDateTime(new TimeOnly(9, 0)),
            CreatedByUserId = SeedConstants.SystemUserId
        };

        _db.ProductionEntries.Add(entry);

        Move(product.Id, QualityGrade.First, good, StockMovementType.ProductionReceipt,
            StockReferenceType.ProductionEntry, entry.Id, day);

        Move(product.Id, QualityGrade.Second, seconds, StockMovementType.ProductionReceipt,
            StockReferenceType.ProductionEntry, entry.Id, day);
    }

    private bool AddDispatch(
        Customer customer, List<Product> products,
        Dictionary<StockKey, decimal> rates, DateOnly day)
    {
        var dispatch = new Dispatch
        {
            Id = Guid.NewGuid(),
            DispatchNumber = NextNumber("D", day),
            CustomerId = customer.Id,
            CustomerNameSnapshot = customer.Name,
            DispatchDate = day,
            VehicleNumber = $"GJT-{_faker.Random.Number(1000, 9999)}",
            Status = DocumentStatus.Active,
            CreatedAt = day.ToDateTime(new TimeOnly(14, 0)),
            CreatedByUserId = SeedConstants.SystemUserId
        };

        var lineNumber = 0;
        var used = new HashSet<StockKey>();

        foreach (var _ in Enumerable.Range(0, _faker.Random.Int(1, 4)))
        {
            var product = _faker.PickRandom(products);
            var grade = _faker.Random.Bool(0.8f) ? QualityGrade.First : QualityGrade.Second;
            var key = new StockKey(product.Id, grade);

            // The real endpoint refuses a duplicate product and grade on one dispatch.
            if (!used.Add(key))
                continue;

            var available = _balances.GetValueOrDefault(key);

            // Respect BR-01. Skip the line rather than clamping it: a database in which
            // stock is never short is not the database whose rules we want to test.
            if (available < 50)
                continue;

            var quantity = _faker.Random.Int(24, Math.Min(available, 600));
            var rate = rates.GetValueOrDefault(key, 100m);

            dispatch.Lines.Add(new DispatchLine
            {
                Id = Guid.NewGuid(),
                DispatchId = dispatch.Id,
                LineNumber = ++lineNumber,
                ProductId = product.Id,
                ProductCodeSnapshot = product.Code,
                ProductNameSnapshot = product.Name,
                Grade = grade,
                Quantity = quantity,
                UnitRate = rate,
                LineAmount = rate * quantity
            });

            Move(product.Id, grade, -quantity, StockMovementType.Dispatch,
                StockReferenceType.Dispatch, dispatch.Id, day);
        }

        if (dispatch.Lines.Count == 0)
            return false;

        dispatch.TotalAmount = dispatch.Lines.Sum(l => l.LineAmount);
        _db.Dispatches.Add(dispatch);

        return true;
    }

    private void AddPayment(Customer customer, DateOnly day)
    {
        var method = _faker.PickRandom(
            PaymentMethod.Cash, PaymentMethod.Cash, PaymentMethod.BankTransfer, PaymentMethod.Cheque);

        _db.Payments.Add(new Payment
        {
            Id = Guid.NewGuid(),
            PaymentNumber = NextNumber("R", day),
            CustomerId = customer.Id,
            PaymentDate = day,
            Amount = Math.Round(_faker.Random.Decimal(5_000, 180_000), 2),
            Method = method,
            Reference = method == PaymentMethod.Cash ? null : _faker.Random.Number(100000, 999999).ToString(),
            Status = DocumentStatus.Active,
            CreatedAt = day.ToDateTime(new TimeOnly(16, 0)),
            CreatedByUserId = SeedConstants.SystemUserId
        });
    }

    private void Move(
        Guid productId, QualityGrade grade, int quantity, StockMovementType type,
        StockReferenceType referenceType, Guid referenceId, DateOnly day)
    {
        if (quantity == 0)
            return;

        _db.StockMovements.Add(new StockMovement
        {
            Id = Guid.NewGuid(),
            ProductId = productId,
            Grade = grade,
            Quantity = quantity,
            MovementType = type,
            ReferenceType = referenceType,
            ReferenceId = referenceId,
            OccurredOn = day,
            CreatedAt = day.ToDateTime(new TimeOnly(12, 0)),
            CreatedByUserId = SeedConstants.SystemUserId
        });

        var key = new StockKey(productId, grade);
        _balances[key] = _balances.GetValueOrDefault(key) + quantity;
    }

    /// <summary>Mirrors the live numbering: {Prefix}-{yyMM}-{seq:D4}, restarting each month.</summary>
    private string NextNumber(string prefix, DateOnly day)
    {
        var stem = $"{prefix}-{day:yyMM}-";
        var next = _sequences.GetValueOrDefault(stem) + 1;
        _sequences[stem] = next;

        return $"{stem}{next:D4}";
    }

    /// <summary>
    /// Written from the running balances the generator maintained, which are by
    /// construction the sum of the movements it wrote - the same invariant the
    /// application maintains and the rebuild endpoint restores.
    /// </summary>
    private async Task WriteBalancesAsync(CancellationToken ct)
    {
        foreach (var (key, quantity) in _balances)
        {
            _db.StockBalances.Add(new StockBalance
            {
                ProductId = key.ProductId,
                Grade = key.Grade,
                Quantity = quantity,
                LastMovementAt = DateTime.UtcNow
            });
        }

        await _db.SaveChangesAsync(ct);
    }
}

public sealed record SeedSummary(
    int Products, int Customers, int ProductionEntries, int Dispatches, int Payments);
