using CrockeryFactory.Application.Common;
using CrockeryFactory.Application.Reports.Dtos;
using CrockeryFactory.Application.Sales;
using CrockeryFactory.Application.Stock;
using CrockeryFactory.Application.Stock.Dtos;
using CrockeryFactory.Domain.Enums;
using CrockeryFactory.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace CrockeryFactory.Application.Reports;

public sealed class ReportService : IReportService
{
    private const string DashboardCacheKey = "dashboard";

    /// <summary>
    /// The dashboard is polled. A minute-old stock figure is not a problem; a query
    /// storm from six tablets refreshing every five seconds would be.
    /// </summary>
    private static readonly TimeSpan DashboardCacheFor = TimeSpan.FromMinutes(1);

    /// <summary>Below this a product is worth flagging on the dashboard.</summary>
    private const int LowStockThreshold = 100;

    private const int TopDebtorCount = 5;

    private readonly FactoryDbContext _db;
    private readonly IStockQueries _stock;
    private readonly ICustomerService _customers;
    private readonly IMemoryCache _cache;

    public ReportService(
        FactoryDbContext db, IStockQueries stock, ICustomerService customers, IMemoryCache cache)
    {
        _db = db;
        _stock = stock;
        _customers = customers;
        _cache = cache;
    }

    /// <summary>
    /// RP-01, built entirely from the ledger rather than from the cached balances.
    ///
    /// The point of this sheet is that opening plus movements equals closing on every
    /// row. Taking closing from StockBalances and the movements from the ledger would
    /// mean the two halves could disagree, and a stock sheet that does not add up is
    /// worse than no stock sheet.
    /// </summary>
    public async Task<DailyStockResponse> DailyStockAsync(DateOnly? date, CancellationToken ct = default)
    {
        var day = date ?? BusinessDates.Today(_db.Clock);

        var opening = await _db.StockMovements.AsNoTracking()
            .Where(m => m.OccurredOn < day)
            .GroupBy(m => new { m.ProductId, m.Grade })
            .Select(g => new { g.Key.ProductId, g.Key.Grade, Quantity = g.Sum(m => m.Quantity) })
            .ToListAsync(ct);

        var onTheDay = await _db.StockMovements.AsNoTracking()
            .Where(m => m.OccurredOn == day)
            .Select(m => new { m.ProductId, m.Grade, m.Quantity, m.MovementType })
            .ToListAsync(ct);

        var keys = opening
            .Select(o => (o.ProductId, o.Grade))
            .Concat(onTheDay.Select(m => (m.ProductId, m.Grade)))
            .Distinct()
            .ToList();

        var productIds = keys.Select(k => k.ProductId).Distinct().ToList();

        var products = await _db.Products.AsNoTracking()
            .Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => new { p.Code, p.Name }, ct);

        var rows = new List<DailyStockRow>();

        foreach (var (productId, grade) in keys)
        {
            var product = products.GetValueOrDefault(productId);

            var openingQuantity = opening
                .FirstOrDefault(o => o.ProductId == productId && o.Grade == grade)?.Quantity ?? 0;

            var movements = onTheDay
                .Where(m => m.ProductId == productId && m.Grade == grade)
                .ToList();

            // Receipts and their cancellations net off, as do dispatches and theirs, so
            // a document raised and cancelled on the same day nets to nothing rather
            // than inflating both columns.
            var received = movements
                .Where(m => m.MovementType is StockMovementType.ProductionReceipt
                    or StockMovementType.ProductionCancellation)
                .Sum(m => m.Quantity);

            var dispatched = movements
                .Where(m => m.MovementType is StockMovementType.Dispatch
                    or StockMovementType.DispatchCancellation)
                .Sum(m => m.Quantity);

            var adjusted = movements
                .Where(m => m.MovementType is StockMovementType.Adjustment
                    or StockMovementType.CountCorrection
                    or StockMovementType.SalesReturn
                    or StockMovementType.OpeningBalance)
                .Sum(m => m.Quantity);

            rows.Add(new DailyStockRow(
                productId, product?.Code ?? "unknown", product?.Name ?? "unknown", grade,
                openingQuantity,
                received,

                // Reported as a positive number - the sheet reads "dispatched 240", not
                // "dispatched minus 240".
                -dispatched,
                adjusted,
                openingQuantity + received + dispatched + adjusted));
        }

        rows = rows.OrderBy(r => r.ProductName).ThenBy(r => r.Grade).ToList();

        return new DailyStockResponse(
            day, rows,
            rows.Sum(r => r.Opening), rows.Sum(r => r.Received),
            rows.Sum(r => r.Dispatched), rows.Sum(r => r.Adjusted), rows.Sum(r => r.Closing));
    }

    /// <summary>RP-06. Cancelled dispatches are excluded - they are not sales.</summary>
    public async Task<SalesSummaryResponse> SalesSummaryAsync(
        DateOnly? from, DateOnly? to, SalesGroupBy groupBy, CancellationToken ct = default)
    {
        var today = BusinessDates.Today(_db.Clock);
        var start = from ?? new DateOnly(today.Year, today.Month, 1);
        var end = to ?? today;

        var lines = await _db.DispatchLines.AsNoTracking()
            .Join(_db.Dispatches.AsNoTracking()
                    .Where(d => d.Status == DocumentStatus.Active &&
                                d.DispatchDate >= start && d.DispatchDate <= end),
                line => line.DispatchId,
                dispatch => dispatch.Id,
                (line, dispatch) => new
                {
                    dispatch.Id,
                    dispatch.CustomerId,
                    dispatch.CustomerNameSnapshot,
                    dispatch.DispatchDate,
                    line.ProductId,
                    line.ProductCodeSnapshot,
                    line.ProductNameSnapshot,
                    line.Quantity,
                    line.LineAmount
                })
            .ToListAsync(ct);

        var grouped = groupBy switch
        {
            SalesGroupBy.Product => lines.GroupBy(l =>
                (Key: l.ProductId.ToString(), Label: $"{l.ProductCodeSnapshot} - {l.ProductNameSnapshot}")),

            SalesGroupBy.Month => lines.GroupBy(l =>
                (Key: l.DispatchDate.ToString("yyyy-MM"), Label: l.DispatchDate.ToString("MMMM yyyy"))),

            _ => lines.GroupBy(l => (Key: l.CustomerId.ToString(), Label: l.CustomerNameSnapshot))
        };

        var rows = grouped
            .Select(g => new SalesSummaryRow(
                g.Key.Key, g.Key.Label,

                // Distinct because a four-line dispatch is one dispatch, not four.
                g.Select(l => l.Id).Distinct().Count(),
                g.Sum(l => l.Quantity),
                g.Sum(l => l.LineAmount)))
            .OrderByDescending(r => r.TotalAmount)
            .ToList();

        return new SalesSummaryResponse(
            start, end, groupBy, rows, rows.Sum(r => r.TotalQuantity), rows.Sum(r => r.TotalAmount));
    }

    public async Task<DashboardResponse> DashboardAsync(CancellationToken ct = default)
    {
        if (_cache.TryGetValue(DashboardCacheKey, out DashboardResponse? cached) && cached is not null)
            return cached;

        var today = BusinessDates.Today(_db.Clock);
        var monthStart = new DateOnly(today.Year, today.Month, 1);

        var stock = await _stock.GetStockAsync(new StockQuery(), ct);
        var outstanding = await _customers.OutstandingAsync(ct);

        var production = await _db.ProductionEntries.AsNoTracking()
            .Where(e => e.Status == DocumentStatus.Active && e.EntryDate >= monthStart && e.EntryDate <= today)
            .Select(e => new { e.QuantityGood, e.QuantitySeconds, e.QuantityBroken })
            .ToListAsync(ct);

        var produced = production.Sum(p => p.QuantityGood + p.QuantitySeconds);
        var fired = production.Sum(p => p.QuantityGood + p.QuantitySeconds + p.QuantityBroken);
        var broken = production.Sum(p => p.QuantityBroken);

        var sales = await _db.Dispatches.AsNoTracking()
            .Where(d => d.Status == DocumentStatus.Active && d.DispatchDate >= monthStart && d.DispatchDate <= today)
            .SumAsync(d => (decimal?)d.TotalAmount, ct) ?? 0m;

        var payments = await _db.Payments.AsNoTracking()
            .Where(p => p.Status == DocumentStatus.Active && p.PaymentDate >= monthStart && p.PaymentDate <= today)
            .SumAsync(p => (decimal?)p.Amount, ct) ?? 0m;

        var response = new DashboardResponse(
            stock.TotalUnits,
            stock.TotalValue,
            outstanding.TotalOutstanding,
            outstanding.Rows.Count(r => r.Outstanding != 0m),
            produced,
            fired == 0 ? 0m : Math.Round((decimal)broken / fired * 100m, 2),
            sales,
            payments,

            // Zero-stock lines are omitted: a product that is out is a different problem
            // from one that is running low, and mixing them makes the list unreadable.
            stock.Lines.Where(l => l.Quantity is > 0 and < LowStockThreshold)
                .OrderBy(l => l.Quantity)
                .Take(10)
                .ToList(),
            outstanding.Rows.Take(TopDebtorCount).ToList(),
            _db.Clock.GetUtcNow().UtcDateTime);

        _cache.Set(DashboardCacheKey, response, DashboardCacheFor);

        return response;
    }
}
