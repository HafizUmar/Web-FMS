using CrockeryFactory.Application.Common;
using CrockeryFactory.Application.Stock.Dtos;
using CrockeryFactory.Domain.Enums;
using System.Data;
using CrockeryFactory.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CrockeryFactory.Application.Stock;

public sealed class StockQueries : IStockQueries
{
    private const int MaxPageSize = 200;

    private readonly FactoryDbContext _db;

    public StockQueries(FactoryDbContext db) => _db = db;

    /// <summary>
    /// Two different queries behind one endpoint.
    ///
    /// Without asOf this reads StockBalances directly, which is what makes PF-05's two
    /// second target reachable. With asOf it sums the ledger up to that date, which is
    /// slower and correct - and is why the historical path is separate rather than the
    /// default. Making every read take the slow path to avoid having two would mean the
    /// screen the clerk opens forty times a day pays for the report he opens twice a month.
    /// </summary>
    public async Task<StockResponse> GetStockAsync(StockQuery query, CancellationToken ct = default)
    {
        var asOf = query.AsOf ?? BusinessDates.Today(_db.Clock);

        var products = _db.Products.AsNoTracking().Where(p => p.IsActive);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            products = products.Where(p => p.Code.Contains(term) || p.Name.Contains(term));
        }

        var catalogue = await products
            .Select(p => new { p.Id, p.Code, p.Name })
            .ToListAsync(ct);

        var ids = catalogue.Select(p => p.Id).ToList();

        var quantities = query.AsOf is null
            ? await CurrentQuantitiesAsync(ids, ct)
            : await QuantitiesAsOfAsync(ids, asOf, ct);

        var rates = await _db.ProductPrices.AsNoTracking()
            .Where(pp => ids.Contains(pp.ProductId) && pp.EffectiveTo == null)
            .Select(pp => new { pp.ProductId, pp.Grade, pp.UnitRate })
            .ToListAsync(ct);

        var lines = new List<StockLine>();

        foreach (var row in quantities)
        {
            if (query.Grade is { } grade && row.Grade != grade)
                continue;

            if (query.OnlyInStock && row.Quantity == 0)
                continue;

            var product = catalogue.First(p => p.Id == row.ProductId);
            var rate = rates.FirstOrDefault(r => r.ProductId == row.ProductId && r.Grade == row.Grade)?.UnitRate;

            lines.Add(new StockLine(
                product.Id, product.Code, product.Name, row.Grade, row.Quantity,
                rate,
                rate is { } r ? r * row.Quantity : null,
                row.LastMovementAt));
        }

        lines = lines
            .OrderBy(l => l.ProductName)
            .ThenBy(l => l.Grade)
            .ToList();

        return new StockResponse(
            lines,
            lines.Sum(l => l.Quantity),

            // Unpriced stock contributes nothing rather than silently counting as zero
            // value in a total that claims to be complete.
            lines.Sum(l => l.StockValue ?? 0m),
            asOf);
    }

    private sealed record QuantityRow(Guid ProductId, QualityGrade Grade, int Quantity, DateTime? LastMovementAt);

    private async Task<List<QuantityRow>> CurrentQuantitiesAsync(List<Guid> ids, CancellationToken ct) =>
        await _db.StockBalances.AsNoTracking()
            .Where(b => ids.Contains(b.ProductId))
            .Select(b => new QuantityRow(b.ProductId, b.Grade, b.Quantity, b.LastMovementAt))
            .ToListAsync(ct);

    private async Task<List<QuantityRow>> QuantitiesAsOfAsync(List<Guid> ids, DateOnly asOf, CancellationToken ct) =>
        await _db.StockMovements.AsNoTracking()
            .Where(m => ids.Contains(m.ProductId) && m.OccurredOn <= asOf)
            .GroupBy(m => new { m.ProductId, m.Grade })
            .Select(g => new QuantityRow(
                g.Key.ProductId,
                g.Key.Grade,
                g.Sum(m => m.Quantity),
                g.Max(m => (DateTime?)m.CreatedAt)))
            .ToListAsync(ct);

    /// <summary>
    /// The movement history for one product and grade, with a running balance.
    ///
    /// RunningBalance is the column that turns "the stock is wrong" into "the stock went
    /// wrong on the fourteenth", which is the whole reason for keeping a ledger. It is
    /// computed with a window function in SQL rather than in memory, because the page the
    /// clerk asks for is the last one and summing in C# would mean fetching every row
    /// before it.
    /// </summary>
    public async Task<PagedResult<StockMovementItem>> GetMovementsAsync(
        Guid productId, MovementQuery query, CancellationToken ct = default)
    {
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize <= 0 ? 50 : query.PageSize, 1, MaxPageSize);

        var movements = _db.StockMovements.AsNoTracking().Where(m => m.ProductId == productId);

        if (query.Grade is { } grade)
            movements = movements.Where(m => m.Grade == grade);

        if (query.From is { } from)
            movements = movements.Where(m => m.OccurredOn >= from);

        if (query.To is { } to)
            movements = movements.Where(m => m.OccurredOn <= to);

        var totalCount = await movements.CountAsync(ct);

        var rows = await RunLedgerQueryAsync(productId, query, page, pageSize, ct);

        var reasonIds = rows.Where(r => r.ReasonCodeId is not null)
            .Select(r => r.ReasonCodeId!.Value).Distinct().ToList();

        var reasons = await _db.ReasonCodes.AsNoTracking()
            .Where(rc => reasonIds.Contains(rc.Id))
            .ToDictionaryAsync(rc => rc.Id, rc => rc.Description, ct);

        var userIds = rows.Select(r => r.CreatedByUserId).Distinct().ToList();

        var users = await _db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName, ct);

        var referenceNumbers = await ResolveReferenceNumbersAsync(
            rows.Select(r => ((StockReferenceType)Enum.Parse(typeof(StockReferenceType), r.ReferenceType),
                              r.ReferenceId)).Distinct().ToList(), ct);

        var items = rows.Select(r => new StockMovementItem(
            r.Id,
            DateOnly.FromDateTime(r.OccurredOn),
            (QualityGrade)r.Grade,
            r.Quantity,
            Enum.Parse<StockMovementType>(r.MovementType),
            referenceNumbers.GetValueOrDefault(
                (Enum.Parse<StockReferenceType>(r.ReferenceType), r.ReferenceId)),
            r.ReasonCodeId is { } id ? reasons.GetValueOrDefault(id) : null,
            r.Notes,
            users.GetValueOrDefault(r.CreatedByUserId, "unknown"),
            r.CreatedAt,
            r.RunningBalance)).ToList();

        return new PagedResult<StockMovementItem>(items, page, pageSize, totalCount);
    }

    /// <summary>
    /// Shape returned by the ledger query. Column names must match the SELECT list.
    /// </summary>
    private sealed record LedgerRow(
        Guid Id, DateTime OccurredOn, int Grade, int Quantity,
        string MovementType, string ReferenceType, Guid ReferenceId,
        Guid? ReasonCodeId, string? Notes, DateTime CreatedAt, Guid CreatedByUserId,
        int RunningBalance);

    /// <summary>
    /// The running balance is computed by SQL Server, partitioned by grade, over the
    /// product's whole history - not over the page and not over the date filter.
    ///
    /// Partitioned by grade because stock is held per product AND grade; a balance that
    /// mixed firsts and seconds would be a number that describes nothing. Computed over
    /// the whole history because a balance is only a balance if everything before it is
    /// counted: restricting the window to the filtered dates would show a figure that
    /// starts from zero in March.
    ///
    /// Paging happens in the database. Accumulating in C# would mean fetching every
    /// movement ever recorded for the product to render the last page of it, which is
    /// precisely the shape PF-14 asks about at five years of data.
    /// </summary>
    private async Task<List<LedgerRow>> RunLedgerQueryAsync(
        Guid productId, MovementQuery query, int page, int pageSize, CancellationToken ct)
    {
        const string sql = """
            WITH ledger AS (
                SELECT
                    m.Id, m.OccurredOn, m.Grade, m.Quantity, m.MovementType,
                    m.ReferenceType, m.ReferenceId, m.ReasonCodeId, m.Notes,
                    m.CreatedAt, m.CreatedByUserId,
                    SUM(m.Quantity) OVER (
                        PARTITION BY m.Grade
                        ORDER BY m.OccurredOn, m.CreatedAt, m.Id
                        ROWS UNBOUNDED PRECEDING) AS RunningBalance
                FROM stock.StockMovements AS m
                WHERE m.ProductId = @productId
                  AND (@grade IS NULL OR m.Grade = @grade)
            )
            SELECT Id, OccurredOn, Grade, Quantity, MovementType, ReferenceType,
                   ReferenceId, ReasonCodeId, Notes, CreatedAt, CreatedByUserId, RunningBalance
            FROM ledger
            WHERE (@from IS NULL OR OccurredOn >= @from)
              AND (@to   IS NULL OR OccurredOn <= @to)
            ORDER BY OccurredOn DESC, CreatedAt DESC, Id DESC
            OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY
            """;

        var parameters = new[]
        {
            new SqlParameter("@productId", SqlDbType.UniqueIdentifier) { Value = productId },
            new SqlParameter("@grade", SqlDbType.Int)
                { Value = query.Grade is { } g ? (int)g : DBNull.Value },
            new SqlParameter("@from", SqlDbType.Date)
                { Value = query.From is { } f ? f.ToDateTime(TimeOnly.MinValue) : DBNull.Value },
            new SqlParameter("@to", SqlDbType.Date)
                { Value = query.To is { } t ? t.ToDateTime(TimeOnly.MinValue) : DBNull.Value },
            new SqlParameter("@skip", SqlDbType.Int) { Value = (page - 1) * pageSize },
            new SqlParameter("@take", SqlDbType.Int) { Value = pageSize }
        };

        return await _db.Database
            .SqlQueryRaw<LedgerRow>(sql, parameters)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Turns the polymorphic reference into the document number a clerk would recognise.
    /// A row saying "Dispatch" is not much use without saying which one.
    /// </summary>
    private async Task<Dictionary<(StockReferenceType, Guid), string>> ResolveReferenceNumbersAsync(
        IReadOnlyList<(StockReferenceType Type, Guid Id)> references, CancellationToken ct)
    {
        var resolved = new Dictionary<(StockReferenceType, Guid), string>();

        foreach (var group in references.GroupBy(r => r.Type))
        {
            var ids = group.Select(r => r.Id).ToList();

            switch (group.Key)
            {
                case StockReferenceType.Dispatch:
                    foreach (var d in await _db.Dispatches.AsNoTracking()
                                 .Where(x => ids.Contains(x.Id))
                                 .Select(x => new { x.Id, Number = x.DispatchNumber }).ToListAsync(ct))
                        resolved[(group.Key, d.Id)] = d.Number;
                    break;

                case StockReferenceType.ProductionEntry:
                    foreach (var p in await _db.ProductionEntries.AsNoTracking()
                                 .Where(x => ids.Contains(x.Id))
                                 .Select(x => new { x.Id, Number = x.EntryNumber }).ToListAsync(ct))
                        resolved[(group.Key, p.Id)] = p.Number;
                    break;

                case StockReferenceType.StockAdjustment:
                    foreach (var a in await _db.StockAdjustments.AsNoTracking()
                                 .Where(x => ids.Contains(x.Id))
                                 .Select(x => new { x.Id, Number = x.AdjustmentNumber }).ToListAsync(ct))
                        resolved[(group.Key, a.Id)] = a.Number;
                    break;

                // StockCount, SalesReturn and OpeningBalance have no table yet - the enum
                // carries them so the ledger can be written before those features exist.
                default:
                    break;
            }
        }

        return resolved;
    }
}
