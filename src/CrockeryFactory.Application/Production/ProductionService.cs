using CrockeryFactory.Application.Abstractions;
using CrockeryFactory.Application.Common;
using CrockeryFactory.Application.Production.Dtos;
using CrockeryFactory.Application.Stock;
using CrockeryFactory.Domain.Enums;
using CrockeryFactory.Modules.Production.Entities;
using CrockeryFactory.Persistence;
using CrockeryFactory.Shared.Constants;
using Microsoft.EntityFrameworkCore;

namespace CrockeryFactory.Application.Production;

public sealed class ProductionService : IProductionService
{
    /// <summary>
    /// A sanity ceiling, not a capacity limit. Nothing this factory fires in one unload
    /// approaches it; a number above it is a slipped keystroke.
    /// </summary>
    private const int ImplausibleTotal = 50_000;

    private const int MinimumCancellationReasonLength = 10;
    private const int MaxPageSize = 200;

    private readonly FactoryDbContext _db;
    private readonly IStockService _stock;
    private readonly IFactorySettings _settings;
    private readonly IDocumentNumbers _numbers;
    private readonly IAuditWriter _audit;

    public ProductionService(
        FactoryDbContext db, IStockService stock, IFactorySettings settings,
        IDocumentNumbers numbers, IAuditWriter audit)
    {
        _db = db;
        _stock = stock;
        _settings = settings;
        _numbers = numbers;
        _audit = audit;
    }

    public async Task<ProductionEntryResponse> CreateAsync(
        CreateProductionEntryRequest request, CancellationToken ct = default)
    {
        var errors = new List<FieldError>();

        if (request.QuantityGood < 0)
            errors.Add(new FieldError("quantityGood", "Cannot be negative", ErrorCodes.ValidationFailed));
        if (request.QuantitySeconds < 0)
            errors.Add(new FieldError("quantitySeconds", "Cannot be negative", ErrorCodes.ValidationFailed));
        if (request.QuantityBroken < 0)
            errors.Add(new FieldError("quantityBroken", "Cannot be negative", ErrorCodes.ValidationFailed));

        if (errors.Count > 0)
            throw DomainException.Validation(ErrorCodes.ValidationFailed, "The entry could not be saved.", errors.ToArray());

        var totalFired = request.QuantityGood + request.QuantitySeconds + request.QuantityBroken;

        if (totalFired == 0)
        {
            // BR-03. An entry recording nothing is a mistake, not an empty firing.
            throw DomainException.Validation(ErrorCodes.NoQuantity,
                "This entry records no pieces at all. Enter what came out of the kiln.",
                new FieldError("quantityGood", "At least one quantity must be greater than zero", ErrorCodes.NoQuantity));
        }

        if (totalFired > ImplausibleTotal)
        {
            throw DomainException.Unprocessable(ErrorCodes.QuantityImplausible, "Quantity implausible",
                $"{totalFired:N0} pieces in a single entry is beyond anything this kiln fires. " +
                "Check the figures - a digit has probably been added.");
        }

        var product = await _db.Products.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == request.ProductId, ct)
            ?? throw DomainException.NotFound("Product", request.ProductId.ToString());

        if (!product.IsActive)
        {
            throw DomainException.Unprocessable(ErrorCodes.ProductInactive, "Product inactive",
                $"{product.Code} is no longer in the catalogue and cannot take new production.");
        }

        await BusinessDates.ValidateAsync(
            request.EntryDate, "entryDate",
            SettingKeys.BackdateDaysProduction, 7, _db.Clock, _settings, ct);

        string? breakageReason = null;

        if (request.QuantityBroken > 0)
        {
            if (request.BreakageReasonCodeId is not { } reasonId)
            {
                throw DomainException.Validation(ErrorCodes.BreakageReasonRequired,
                    "Breakages need a reason, so that the loss report can say what is going wrong.",
                    new FieldError("breakageReasonCodeId", "Required when anything is broken",
                        ErrorCodes.BreakageReasonRequired));
            }

            var reason = await ReasonCodeCheck.RequireAsync(
                _db, reasonId, ReasonCodeType.Breakage, "breakageReasonCodeId", ct);

            breakageReason = reason.Description;
        }

        var entry = new ProductionEntry
        {
            Id = Guid.NewGuid(),
            EntryNumber = await _numbers.NextAsync(DocumentSeries.ProductionEntry, request.EntryDate, ct),
            ProductId = request.ProductId,
            EntryDate = request.EntryDate,
            QuantityGood = request.QuantityGood,
            QuantitySeconds = request.QuantitySeconds,
            QuantityBroken = request.QuantityBroken,
            BreakageReasonCodeId = request.QuantityBroken > 0 ? request.BreakageReasonCodeId : null,
            BatchReference = request.BatchReference?.Trim(),
            Notes = request.Notes?.Trim(),
            Status = DocumentStatus.Active,
            CreatedAt = _db.Clock.GetUtcNow().UtcDateTime,
            CreatedByUserId = _db.CurrentUser.RequireUserId()
        };

        _db.ProductionEntries.Add(entry);

        await _stock.ApplyAsync(BuildReceiptMovements(entry), ct);
        await _db.SaveChangesAsync(ct);

        var warnings = await BuildWarningsAsync(entry, ct);

        return await ToResponseAsync(entry, product.Code, product.Name, breakageReason, warnings, ct);
    }

    /// <summary>
    /// Good pieces enter stock at First and seconds at Second.
    ///
    /// Broken pieces never enter stock at all (BR-04). Seconds are sellable at a lower
    /// rate; broken is a shard on the floor, and putting it in the godown would make
    /// every stock figure wrong by the breakage rate.
    /// </summary>
    private static List<StockMovementRequest> BuildReceiptMovements(ProductionEntry entry)
    {
        var movements = new List<StockMovementRequest>(2);

        if (entry.QuantityGood > 0)
        {
            movements.Add(new StockMovementRequest(
                entry.ProductId, QualityGrade.First, entry.QuantityGood,
                StockMovementType.ProductionReceipt, StockReferenceType.ProductionEntry, entry.Id,
                entry.EntryDate));
        }

        if (entry.QuantitySeconds > 0)
        {
            movements.Add(new StockMovementRequest(
                entry.ProductId, QualityGrade.Second, entry.QuantitySeconds,
                StockMovementType.ProductionReceipt, StockReferenceType.ProductionEntry, entry.Id,
                entry.EntryDate));
        }

        return movements;
    }

    /// <summary>
    /// A high loss is reported and recorded, never refused.
    ///
    /// Forty per cent is usually a typo and occasionally a bad firing. Blocking it would
    /// mean the clerk cannot record what actually happened, and the first time the system
    /// refuses to accept reality is the day he goes back to the paper register. Warn,
    /// record, and let the loss report surface it.
    /// </summary>
    private async Task<List<string>> BuildWarningsAsync(ProductionEntry entry, CancellationToken ct)
    {
        var warnings = new List<string>();

        var threshold = await _settings.GetIntAsync(SettingKeys.ProductionLossWarningPercent, 25, ct);

        if (entry.LossPercentage > threshold)
            warnings.Add(WarningCodes.LossUnusuallyHigh);

        return warnings;
    }

    public async Task<bool> RequiresHistoricalPermissionAsync(Guid id, CancellationToken ct = default)
    {
        var entryDate = await _db.ProductionEntries.AsNoTracking()
            .Where(e => e.Id == id)
            .Select(e => (DateOnly?)e.EntryDate)
            .FirstOrDefaultAsync(ct);

        // An entry that does not exist is not a permission question - let the cancel
        // path answer it with a 404 rather than a misleading 403.
        if (entryDate is null)
            return false;

        return entryDate.Value != BusinessDates.Today(_db.Clock);
    }

    public async Task<ProductionEntryResponse> CancelAsync(
        Guid id, CancelRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Reason) ||
            request.Reason.Trim().Length < MinimumCancellationReasonLength)
        {
            throw DomainException.Validation(ErrorCodes.ReasonRequired,
                $"Give a reason of at least {MinimumCancellationReasonLength} characters. " +
                "This is what the owner reads when he asks why the figures changed.",
                new FieldError("reason", $"At least {MinimumCancellationReasonLength} characters",
                    ErrorCodes.ReasonRequired));
        }

        var entry = await _db.ProductionEntries.FirstOrDefaultAsync(e => e.Id == id, ct)
            ?? throw DomainException.NotFound("Production entry", id.ToString());

        if (entry.Status == DocumentStatus.Cancelled)
            throw DomainException.Conflict(ErrorCodes.AlreadyCancelled, "This entry has already been cancelled.");

        var product = await _db.Products.AsNoTracking()
            .FirstAsync(p => p.Id == entry.ProductId, ct);

        var reversals = BuildReversalMovements(entry);

        try
        {
            await _stock.ApplyAsync(reversals, ct);
        }
        catch (DomainException insufficient) when (insufficient.Code == ErrorCodes.StockInsufficient)
        {
            // Cups produced on Monday and dispatched on Tuesday cannot have Monday's
            // production cancelled on Wednesday - the stock is gone. Reversing anyway
            // would drive the balance negative and make the ledger describe a godown
            // that cannot exist. The clerk needs an adjustment instead, and the message
            // has to say so, because "insufficient stock" on a cancellation reads as
            // nonsense otherwise.
            throw DomainException.Unprocessable(
                ErrorCodes.StockInsufficientForReversal, "Cannot reverse this entry",
                $"Entry {entry.EntryNumber} cannot be cancelled: some of the {product.Code} it " +
                "produced has already been dispatched, so reversing it would leave negative stock. " +
                "Record a stock adjustment instead, with the reason for the correction.");
        }

        entry.Status = DocumentStatus.Cancelled;
        entry.CancelledAt = _db.Clock.GetUtcNow().UtcDateTime;
        entry.CancelledByUserId = _db.CurrentUser.RequireUserId();
        entry.CancellationReason = request.Reason.Trim();

        _audit.Record(nameof(ProductionEntry), entry.Id, "Cancel",
            new { Status = DocumentStatus.Active.ToString() },
            new
            {
                Status = DocumentStatus.Cancelled.ToString(),
                entry.EntryNumber,
                entry.CancellationReason,
                Reversed = reversals.Select(r => new { Grade = r.Grade.ToString(), r.Quantity })
            });

        await _db.SaveChangesAsync(ct);

        return await GetAsync(id, ct);
    }

    /// <summary>
    /// Mirrors the receipt movements with the sign flipped. The originals are left
    /// untouched: BR-08 forbids deletes, and a cancellation that erased its own cause
    /// would leave the ledger unable to explain itself.
    /// </summary>
    private static List<StockMovementRequest> BuildReversalMovements(ProductionEntry entry)
    {
        var movements = new List<StockMovementRequest>(2);

        if (entry.QuantityGood > 0)
        {
            movements.Add(new StockMovementRequest(
                entry.ProductId, QualityGrade.First, -entry.QuantityGood,
                StockMovementType.ProductionCancellation, StockReferenceType.ProductionEntry, entry.Id,
                entry.EntryDate, Notes: $"Cancellation of {entry.EntryNumber}"));
        }

        if (entry.QuantitySeconds > 0)
        {
            movements.Add(new StockMovementRequest(
                entry.ProductId, QualityGrade.Second, -entry.QuantitySeconds,
                StockMovementType.ProductionCancellation, StockReferenceType.ProductionEntry, entry.Id,
                entry.EntryDate, Notes: $"Cancellation of {entry.EntryNumber}"));
        }

        return movements;
    }

    public async Task<ProductionEntryResponse> GetAsync(Guid id, CancellationToken ct = default)
    {
        var entry = await _db.ProductionEntries.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, ct)
            ?? throw DomainException.NotFound("Production entry", id.ToString());

        var product = await _db.Products.AsNoTracking().FirstAsync(p => p.Id == entry.ProductId, ct);

        var breakageReason = entry.BreakageReasonCodeId is { } reasonId
            ? await _db.ReasonCodes.AsNoTracking()
                .Where(r => r.Id == reasonId).Select(r => r.Description).FirstOrDefaultAsync(ct)
            : null;

        return await ToResponseAsync(entry, product.Code, product.Name, breakageReason, [], ct);
    }

    public async Task<PagedResult<ProductionEntryResponse>> ListAsync(
        ProductionEntryQuery query, CancellationToken ct = default)
    {
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize <= 0 ? 50 : query.PageSize, 1, MaxPageSize);

        var entries = _db.ProductionEntries.AsNoTracking();

        if (!query.IncludeCancelled)
            entries = entries.Where(e => e.Status == DocumentStatus.Active);

        if (query.ProductId is { } productId)
            entries = entries.Where(e => e.ProductId == productId);

        if (query.From is { } from)
            entries = entries.Where(e => e.EntryDate >= from);

        if (query.To is { } to)
            entries = entries.Where(e => e.EntryDate <= to);

        var totalCount = await entries.CountAsync(ct);

        var rows = await entries
            .OrderByDescending(e => e.EntryDate).ThenByDescending(e => e.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var items = new List<ProductionEntryResponse>(rows.Count);

        var productIds = rows.Select(r => r.ProductId).Distinct().ToList();
        var products = await _db.Products.AsNoTracking()
            .Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => new { p.Code, p.Name }, ct);

        var reasonIds = rows.Where(r => r.BreakageReasonCodeId is not null)
            .Select(r => r.BreakageReasonCodeId!.Value).Distinct().ToList();
        var reasons = await _db.ReasonCodes.AsNoTracking()
            .Where(r => reasonIds.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, r => r.Description, ct);

        var userIds = rows.Select(r => r.CreatedByUserId).Distinct().ToList();
        var users = await _db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName, ct);

        foreach (var entry in rows)
        {
            var product = products.GetValueOrDefault(entry.ProductId);

            items.Add(new ProductionEntryResponse(
                entry.Id, entry.EntryNumber, entry.ProductId,
                product?.Code ?? "unknown", product?.Name ?? "unknown",
                entry.EntryDate, entry.QuantityGood, entry.QuantitySeconds, entry.QuantityBroken,
                entry.TotalFired, entry.LossPercentage,
                entry.BreakageReasonCodeId is { } id ? reasons.GetValueOrDefault(id) : null,
                entry.BatchReference, entry.Notes, entry.Status,
                users.GetValueOrDefault(entry.CreatedByUserId, "unknown"),
                entry.CreatedAt, []));
        }

        return new PagedResult<ProductionEntryResponse>(items, page, pageSize, totalCount);
    }

    /// <summary>
    /// RP-04. Cancelled entries are excluded: a summary that counted them would report
    /// production that was withdrawn.
    /// </summary>
    public async Task<IReadOnlyList<ProductionSummaryRow>> SummaryAsync(
        ProductionSummaryQuery query, CancellationToken ct = default)
    {
        var entries = _db.ProductionEntries.AsNoTracking()
            .Where(e => e.Status == DocumentStatus.Active);

        if (query.ProductId is { } productId)
            entries = entries.Where(e => e.ProductId == productId);

        if (query.From is { } from)
            entries = entries.Where(e => e.EntryDate >= from);

        if (query.To is { } to)
            entries = entries.Where(e => e.EntryDate <= to);

        var rows = await entries
            .Select(e => new
            {
                e.ProductId,
                e.EntryDate,
                e.QuantityGood,
                e.QuantitySeconds,
                e.QuantityBroken
            })
            .ToListAsync(ct);

        var productNames = await _db.Products.AsNoTracking()
            .ToDictionaryAsync(p => p.Id, p => $"{p.Code} - {p.Name}", ct);

        var grouped = query.GroupBy switch
        {
            ProductionGroupBy.Day => rows.GroupBy(r =>
                (Key: r.EntryDate.ToString("yyyy-MM-dd"), Label: r.EntryDate.ToString("yyyy-MM-dd"))),

            ProductionGroupBy.Month => rows.GroupBy(r =>
                (Key: r.EntryDate.ToString("yyyy-MM"), Label: r.EntryDate.ToString("MMMM yyyy"))),

            _ => rows.GroupBy(r =>
                (Key: r.ProductId.ToString(), Label: productNames.GetValueOrDefault(r.ProductId, "unknown")))
        };

        return grouped
            .Select(g =>
            {
                var good = g.Sum(r => r.QuantityGood);
                var seconds = g.Sum(r => r.QuantitySeconds);
                var broken = g.Sum(r => r.QuantityBroken);
                var fired = good + seconds + broken;

                return new ProductionSummaryRow(
                    g.Key.Key, g.Key.Label,
                    fired, good, seconds, broken,
                    Percentage(broken, fired),
                    Percentage(seconds, fired),
                    g.Count());
            })
            .OrderBy(r => r.GroupLabel, StringComparer.Ordinal)
            .ToList();

        static decimal Percentage(int part, int whole) =>
            whole == 0 ? 0m : Math.Round((decimal)part / whole * 100m, 2);
    }

    private async Task<ProductionEntryResponse> ToResponseAsync(
        ProductionEntry entry, string productCode, string productName,
        string? breakageReason, IReadOnlyList<string> warnings, CancellationToken ct)
    {
        var enteredBy = await _db.Users.AsNoTracking()
            .Where(u => u.Id == entry.CreatedByUserId)
            .Select(u => u.FullName)
            .FirstOrDefaultAsync(ct) ?? "unknown";

        return new ProductionEntryResponse(
            entry.Id, entry.EntryNumber, entry.ProductId, productCode, productName,
            entry.EntryDate, entry.QuantityGood, entry.QuantitySeconds, entry.QuantityBroken,
            entry.TotalFired, entry.LossPercentage,
            breakageReason, entry.BatchReference, entry.Notes,
            entry.Status, enteredBy, entry.CreatedAt, warnings);
    }
}
