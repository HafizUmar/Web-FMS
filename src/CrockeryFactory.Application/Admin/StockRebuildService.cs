using CrockeryFactory.Application.Abstractions;
using CrockeryFactory.Application.Admin.Dtos;
using CrockeryFactory.Domain.ValueObjects;
using CrockeryFactory.Modules.Stock.Entities;
using CrockeryFactory.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CrockeryFactory.Application.Admin;

public sealed class StockRebuildService : IStockRebuildService
{
    private readonly FactoryDbContext _db;
    private readonly IAuditWriter _audit;
    private readonly ILogger<StockRebuildService> _logger;

    public StockRebuildService(
        FactoryDbContext db, IAuditWriter audit, ILogger<StockRebuildService> logger)
    {
        _db = db;
        _audit = audit;
        _logger = logger;
    }

    public async Task<RebuildResult> RebuildAsync(CancellationToken ct = default)
    {
        var truth = (await _db.StockMovements
            .AsNoTracking()
            .GroupBy(m => new { m.ProductId, m.Grade })
            .Select(g => new
            {
                g.Key.ProductId,
                g.Key.Grade,
                Quantity = g.Sum(m => m.Quantity),
                LastMovementAt = g.Max(m => m.CreatedAt)
            })
            .ToListAsync(ct))
            .ToDictionary(r => new StockKey(r.ProductId, r.Grade));

        var cached = await _db.StockBalances.ToListAsync(ct);

        var corrections = new List<string>();
        var corrected = 0;
        var inserted = 0;
        var removed = 0;

        var products = await _db.Products.AsNoTracking()
            .ToDictionaryAsync(p => p.Id, p => p.Code, ct);

        foreach (var balance in cached)
        {
            var key = new StockKey(balance.ProductId, balance.Grade);

            if (truth.TryGetValue(key, out var actual))
            {
                if (balance.Quantity != actual.Quantity)
                {
                    corrections.Add(
                        $"{products.GetValueOrDefault(key.ProductId, key.ProductId.ToString())} " +
                        $"({key.Grade}): {balance.Quantity} -> {actual.Quantity}");

                    balance.Quantity = actual.Quantity;
                    balance.LastMovementAt = actual.LastMovementAt;
                    corrected++;
                }
            }
            else
            {
                // A balance row with no movements behind it cannot be right - the ledger
                // is the only thing that creates stock.
                _db.StockBalances.Remove(balance);
                removed++;

                corrections.Add(
                    $"{products.GetValueOrDefault(key.ProductId, key.ProductId.ToString())} " +
                    $"({key.Grade}): removed, no movements exist");
            }
        }

        foreach (var (key, actual) in truth)
        {
            if (cached.Any(b => b.ProductId == key.ProductId && b.Grade == key.Grade))
                continue;

            _db.StockBalances.Add(new StockBalance
            {
                ProductId = key.ProductId,
                Grade = key.Grade,
                Quantity = actual.Quantity,
                LastMovementAt = actual.LastMovementAt
            });

            inserted++;

            corrections.Add(
                $"{products.GetValueOrDefault(key.ProductId, key.ProductId.ToString())} " +
                $"({key.Grade}): inserted at {actual.Quantity}");
        }

        var result = new RebuildResult(
            cached.Count, corrected, inserted, removed, corrections,
            _db.Clock.GetUtcNow().UtcDateTime);

        // Recorded whether or not anything changed. "We ran the rebuild and it found
        // nothing" is exactly as useful in a support call as a list of corrections.
        _audit.Record("StockBalance", Guid.Empty, "Rebuild", null, new
        {
            result.BalancesExamined, result.BalancesCorrected,
            result.BalancesInserted, result.BalancesRemoved,
            Corrections = corrections
        });

        await _db.SaveChangesAsync(ct);

        if (corrections.Count > 0)
        {
            _logger.LogWarning(
                "Stock rebuild corrected {Corrected}, inserted {Inserted}, removed {Removed} balance rows: {Corrections}",
                corrected, inserted, removed, string.Join("; ", corrections));
        }
        else
        {
            _logger.LogInformation("Stock rebuild found no discrepancies across {Count} balances", cached.Count);
        }

        return result;
    }
}
