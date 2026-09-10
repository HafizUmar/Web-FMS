using CrockeryFactory.Application.Common;
using CrockeryFactory.Domain.Enums;
using CrockeryFactory.Domain.ValueObjects;
using CrockeryFactory.Modules.Stock.Entities;
using CrockeryFactory.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CrockeryFactory.Application.Stock;

public sealed class StockService : IStockService
{
    private readonly FactoryDbContext _db;

    public StockService(FactoryDbContext db) => _db = db;

    public async Task ApplyAsync(IReadOnlyList<StockMovementRequest> movements, CancellationToken ct = default)
    {
        if (movements.Count == 0)
            return;

        // A zero-quantity movement records nothing and would sit in the ledger looking
        // like a bug for whoever reads it later.
        if (movements.Any(m => m.Quantity == 0))
        {
            throw DomainException.Validation(ErrorCodes.QuantityZero,
                "A stock movement of zero units cannot be recorded.");
        }

        var netByKey = movements
            .GroupBy(m => m.Key)
            .ToDictionary(g => g.Key, g => g.Sum(m => m.Quantity));

        var balances = await LoadForUpdateAsync(netByKey.Keys, ct);

        // Every line is checked before any is written. Checking as we go would leave a
        // partially applied dispatch behind when line three fails.
        var shortfalls = new List<(StockKey Key, int Available, int Required)>();

        foreach (var (key, net) in netByKey)
        {
            var current = balances.TryGetValue(key, out var row) ? row.Quantity : 0;

            if (current + net < 0)
                shortfalls.Add((key, current, -net));
        }

        if (shortfalls.Count > 0)
            throw await InsufficientStockAsync(shortfalls, ct);

        var now = _db.Clock.GetUtcNow().UtcDateTime;
        var userId = _db.CurrentUser.RequireUserId();

        foreach (var movement in movements)
        {
            _db.StockMovements.Add(new StockMovement
            {
                Id = Guid.NewGuid(),
                ProductId = movement.ProductId,
                Grade = movement.Grade,
                Quantity = movement.Quantity,
                MovementType = movement.MovementType,
                ReferenceType = movement.ReferenceType,
                ReferenceId = movement.ReferenceId,
                ReasonCodeId = movement.ReasonCodeId,
                Notes = movement.Notes,
                OccurredOn = movement.OccurredOn,
                CreatedAt = now,
                CreatedByUserId = userId
            });
        }

        foreach (var (key, net) in netByKey)
        {
            if (balances.TryGetValue(key, out var balance))
            {
                balance.Quantity += net;
                balance.LastMovementAt = now;
            }
            else
            {
                _db.StockBalances.Add(new StockBalance
                {
                    ProductId = key.ProductId,
                    Grade = key.Grade,
                    Quantity = net,
                    LastMovementAt = now
                });
            }
        }

        // Nothing is saved here. The caller commits this together with the document.
    }

    public async Task<int> GetBalanceAsync(Guid productId, QualityGrade grade, CancellationToken ct = default) =>
        await _db.StockBalances
            .AsNoTracking()
            .Where(b => b.ProductId == productId && b.Grade == grade)
            .Select(b => b.Quantity)
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyDictionary<StockKey, int>> GetBalancesAsync(
        IReadOnlyCollection<StockKey> keys, CancellationToken ct = default)
    {
        if (keys.Count == 0)
            return new Dictionary<StockKey, int>();

        var productIds = keys.Select(k => k.ProductId).Distinct().ToList();

        var rows = await _db.StockBalances
            .AsNoTracking()
            .Where(b => productIds.Contains(b.ProductId))
            .Select(b => new { b.ProductId, b.Grade, b.Quantity })
            .ToListAsync(ct);

        var wanted = keys.ToHashSet();

        return rows
            .Select(r => new { Key = new StockKey(r.ProductId, r.Grade), r.Quantity })
            .Where(r => wanted.Contains(r.Key))
            .ToDictionary(r => r.Key, r => r.Quantity);
    }

    public async Task<IReadOnlyDictionary<StockKey, int>> ProjectAsync(
        IReadOnlyList<StockMovementRequest> movements, CancellationToken ct = default)
    {
        var netByKey = movements
            .GroupBy(m => m.Key)
            .ToDictionary(g => g.Key, g => g.Sum(m => m.Quantity));

        var current = await GetBalancesAsync(netByKey.Keys, ct);

        return netByKey.ToDictionary(
            entry => entry.Key,
            entry => current.GetValueOrDefault(entry.Key) + entry.Value);
    }

    /// <summary>
    /// Loads the balance rows as tracked entities so their rowversion takes part in the
    /// caller's SaveChanges. Two clerks dispatching the same product at the same instant
    /// then produce a concurrency conflict rather than a lost decrement.
    /// </summary>
    private async Task<Dictionary<StockKey, StockBalance>> LoadForUpdateAsync(
        IEnumerable<StockKey> keys, CancellationToken ct)
    {
        var productIds = keys.Select(k => k.ProductId).Distinct().ToList();

        var rows = await _db.StockBalances
            .Where(b => productIds.Contains(b.ProductId))
            .ToListAsync(ct);

        return rows.ToDictionary(r => new StockKey(r.ProductId, r.Grade));
    }

    /// <summary>
    /// BR-01, phrased so the clerk can act on it.
    ///
    /// The error names the product code and the grade, never the Guid. "Only 120 units of
    /// CUP-ESP-01 (First) are in stock" tells him to change the line; a product id tells
    /// him to telephone somebody.
    /// </summary>
    private async Task<DomainException> InsufficientStockAsync(
        IReadOnlyList<(StockKey Key, int Available, int Required)> shortfalls, CancellationToken ct)
    {
        var productIds = shortfalls.Select(s => s.Key.ProductId).Distinct().ToList();

        var names = await _db.Products
            .AsNoTracking()
            .Where(p => productIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Code })
            .ToDictionaryAsync(p => p.Id, p => p.Code, ct);

        var detail = string.Join(" ", shortfalls.Select(s =>
            $"Only {s.Available} units of {names.GetValueOrDefault(s.Key.ProductId, "this product")} " +
            $"({s.Key.Grade}) are in stock. {s.Required} were requested."));

        var errors = shortfalls
            .Select(s => new FieldError(
                "quantity",
                $"Only {s.Available} available",
                ErrorCodes.StockInsufficient))
            .ToArray();

        return DomainException.Unprocessable(
            ErrorCodes.StockInsufficient, "Insufficient stock", detail, errors);
    }
}
