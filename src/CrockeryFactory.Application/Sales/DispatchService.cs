using CrockeryFactory.Application.Abstractions;
using CrockeryFactory.Application.Common;
using CrockeryFactory.Application.Sales.Dtos;
using CrockeryFactory.Application.Stock;
using CrockeryFactory.Domain.Enums;
using CrockeryFactory.Domain.ValueObjects;
using CrockeryFactory.Modules.Sales.Entities;
using CrockeryFactory.Persistence;
using CrockeryFactory.Shared.Constants;
using Microsoft.EntityFrameworkCore;

namespace CrockeryFactory.Application.Sales;

public sealed class DispatchService : IDispatchService
{
    private const int MaxLines = 50;
    private const int MinimumCancellationReasonLength = 10;
    private const int MaxPageSize = 200;

    /// <summary>
    /// Below this fraction of the list rate the clerk is warned. Discounts are normal;
    /// a rate this far below list is more often a decimal point in the wrong place.
    /// </summary>
    private const decimal RateWarningFraction = 0.5m;

    private readonly FactoryDbContext _db;
    private readonly IStockService _stock;
    private readonly IFactorySettings _settings;
    private readonly IDocumentNumbers _numbers;
    private readonly IAuditWriter _audit;

    public DispatchService(
        FactoryDbContext db, IStockService stock, IFactorySettings settings,
        IDocumentNumbers numbers, IAuditWriter audit)
    {
        _db = db;
        _stock = stock;
        _settings = settings;
        _numbers = numbers;
        _audit = audit;
    }

    public async Task<DispatchResponse> CreateAsync(
        CreateDispatchRequest request, CancellationToken ct = default)
    {
        var lines = request.Lines ?? [];

        if (lines.Count == 0)
        {
            throw DomainException.Validation(ErrorCodes.NoLines,
                "A dispatch with no lines records nothing leaving the factory.",
                new FieldError("lines", "At least one line is required", ErrorCodes.NoLines));
        }

        if (lines.Count > MaxLines)
        {
            throw DomainException.Validation(ErrorCodes.TooManyLines,
                $"A dispatch can carry at most {MaxLines} lines. Split this across two dispatches.",
                new FieldError("lines", $"At most {MaxLines} lines", ErrorCodes.TooManyLines));
        }

        var fieldErrors = new List<FieldError>();

        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].Quantity <= 0)
                fieldErrors.Add(new FieldError($"lines[{i}].quantity",
                    "Must be greater than zero", ErrorCodes.ValidationFailed));

            if (lines[i].UnitRate is { } rate && rate <= 0m)
                fieldErrors.Add(new FieldError($"lines[{i}].unitRate",
                    "Must be greater than zero when supplied", ErrorCodes.ValidationFailed));
        }

        // Two lines for the same product and grade would each pass the stock check on
        // their own and together take more than exists.
        var duplicates = lines
            .Select((line, index) => (line, index))
            .GroupBy(x => new StockKey(x.line.ProductId, x.line.Grade))
            .Where(g => g.Count() > 1)
            .ToList();

        foreach (var duplicate in duplicates)
        {
            foreach (var (_, index) in duplicate.Skip(1))
                fieldErrors.Add(new FieldError($"lines[{index}]",
                    "This product and grade already appears on another line", ErrorCodes.DuplicateLine));
        }

        if (fieldErrors.Count > 0)
        {
            var code = fieldErrors.Any(e => e.Code == ErrorCodes.DuplicateLine)
                ? ErrorCodes.DuplicateLine
                : ErrorCodes.ValidationFailed;

            throw DomainException.Validation(code, "The dispatch could not be saved.", fieldErrors.ToArray());
        }

        var customer = await _db.Customers.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == request.CustomerId, ct)
            ?? throw DomainException.NotFound("Customer", request.CustomerId.ToString());

        if (!customer.IsActive)
        {
            throw DomainException.Unprocessable(ErrorCodes.CustomerInactive, "Customer inactive",
                $"{customer.Name} is marked inactive. Reactivate the customer before dispatching to him.");
        }

        await BusinessDates.ValidateAsync(
            request.DispatchDate, "dispatchDate",
            SettingKeys.BackdateDaysDispatch, 7, _db.Clock, _settings, ct);

        foreach (var grade in lines.Select(l => l.Grade).Distinct())
            await _settings.RequireGradeEnabledAsync(grade, "lines", ct);

        var productIds = lines.Select(l => l.ProductId).Distinct().ToList();

        var products = await _db.Products.AsNoTracking()
            .Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, ct);

        foreach (var productId in productIds)
        {
            if (!products.ContainsKey(productId))
                throw DomainException.NotFound("Product", productId.ToString());
        }

        var listRates = await _db.ProductPrices.AsNoTracking()
            .Where(pp => productIds.Contains(pp.ProductId) && pp.EffectiveTo == null)
            .Select(pp => new { pp.ProductId, pp.Grade, pp.UnitRate })
            .ToListAsync(ct);

        var dispatch = new Dispatch
        {
            Id = Guid.NewGuid(),
            DispatchNumber = await _numbers.NextAsync(DocumentSeries.Dispatch, request.DispatchDate, ct),
            CustomerId = customer.Id,

            // BR-06. A renamed customer must not change a bill already at the gate.
            CustomerNameSnapshot = customer.Name,
            DispatchDate = request.DispatchDate,
            VehicleNumber = request.VehicleNumber?.Trim(),
            Notes = request.Notes?.Trim(),
            Status = DocumentStatus.Active,
            CreatedAt = _db.Clock.GetUtcNow().UtcDateTime,
            CreatedByUserId = _db.CurrentUser.RequireUserId()
        };

        var warnings = new List<string>();
        var movements = new List<StockMovementRequest>(lines.Count);
        var lineNumber = 0;

        foreach (var input in lines)
        {
            var product = products[input.ProductId];
            var listRate = listRates
                .FirstOrDefault(r => r.ProductId == input.ProductId && r.Grade == input.Grade)?.UnitRate;

            // Omit the rate and the current price is used; supply it and the clerk's
            // rate wins. Either way the resolved rate is written onto the line and never
            // read from ProductPrice again (BR-06).
            var unitRate = input.UnitRate ?? listRate
                ?? throw DomainException.Unprocessable(ErrorCodes.NoPriceAvailable, "No price available",
                    $"There is no price on file for {product.Code} at {input.Grade}, and none was " +
                    "entered on the line. Ask the owner to set a rate, or type one on the dispatch.");

            if (input.UnitRate is { } typed && listRate is { } list && typed < list * RateWarningFraction)
                warnings.Add(WarningCodes.RateBelowList);

            var lineAmount = unitRate * input.Quantity;

            dispatch.Lines.Add(new DispatchLine
            {
                Id = Guid.NewGuid(),
                DispatchId = dispatch.Id,
                LineNumber = ++lineNumber,
                ProductId = product.Id,
                ProductCodeSnapshot = product.Code,
                ProductNameSnapshot = product.Name,
                Grade = input.Grade,
                Quantity = input.Quantity,
                UnitRate = unitRate,
                LineAmount = lineAmount
            });

            movements.Add(new StockMovementRequest(
                product.Id, input.Grade, -input.Quantity,
                StockMovementType.Dispatch, StockReferenceType.Dispatch, dispatch.Id,
                request.DispatchDate));
        }

        dispatch.TotalAmount = dispatch.Lines.Sum(l => l.LineAmount);

        _db.Dispatches.Add(dispatch);

        // Stock for every line is checked before any line is written. A four-line
        // dispatch that fails on line three must leave nothing behind - one transaction,
        // one SaveChanges, all movements or none.
        var stockAfter = await _stock.ProjectAsync(movements, ct);
        await _stock.ApplyAsync(movements, ct);

        await _db.SaveChangesAsync(ct);

        var balanceAfter = await CustomerBalances.ForAsync(_db, customer.Id, ct);

        return BuildResponse(dispatch, customer.Name, balanceAfter, stockAfter,
            await EnteredByAsync(dispatch.CreatedByUserId, ct), warnings.Distinct().ToList());
    }

    public async Task<bool> RequiresHistoricalPermissionAsync(Guid id, CancellationToken ct = default)
    {
        var date = await _db.Dispatches.AsNoTracking()
            .Where(d => d.Id == id)
            .Select(d => (DateOnly?)d.DispatchDate)
            .FirstOrDefaultAsync(ct);

        return date is not null && date.Value != BusinessDates.Today(_db.Clock);
    }

    /// <summary>
    /// BR-05: a printed dispatch is never edited, only cancelled and re-entered, and the
    /// cancellation stays visible.
    /// </summary>
    public async Task<DispatchResponse> CancelAsync(
        Guid id, CancelDocumentRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Reason) ||
            request.Reason.Trim().Length < MinimumCancellationReasonLength)
        {
            throw DomainException.Validation(ErrorCodes.ReasonRequired,
                $"Give a reason of at least {MinimumCancellationReasonLength} characters. " +
                "A cancelled bill is what the customer will ask about.",
                new FieldError("reason", $"At least {MinimumCancellationReasonLength} characters",
                    ErrorCodes.ReasonRequired));
        }

        var dispatch = await _db.Dispatches
            .Include(d => d.Lines)
            .FirstOrDefaultAsync(d => d.Id == id, ct)
            ?? throw DomainException.NotFound("Dispatch", id.ToString());

        if (dispatch.Status == DocumentStatus.Cancelled)
            throw DomainException.Conflict(ErrorCodes.AlreadyCancelled, "This dispatch has already been cancelled.");

        var maxDays = await _settings.GetIntAsync(SettingKeys.DispatchCancellationMaxDays, 90, ct);
        var age = BusinessDates.Today(_db.Clock).DayNumber - dispatch.DispatchDate.DayNumber;

        if (age > maxDays)
        {
            // Past this point the month is closed, the customer has been billed, and a
            // reversal would move stock that was counted long ago.
            throw DomainException.Unprocessable(ErrorCodes.CancellationTooLate, "Too late to cancel",
                $"Dispatch {dispatch.DispatchNumber} is {age} days old and cannot be cancelled " +
                $"after {maxDays} days. Record a sales return or an adjustment instead.");
        }

        // Reversing movements return the stock to the godown.
        var reversals = dispatch.Lines.Select(line => new StockMovementRequest(
            line.ProductId, line.Grade, line.Quantity,
            StockMovementType.DispatchCancellation, StockReferenceType.Dispatch, dispatch.Id,
            dispatch.DispatchDate, Notes: $"Cancellation of {dispatch.DispatchNumber}")).ToList();

        await _stock.ApplyAsync(reversals, ct);

        dispatch.Status = DocumentStatus.Cancelled;
        dispatch.CancelledAt = _db.Clock.GetUtcNow().UtcDateTime;
        dispatch.CancelledByUserId = _db.CurrentUser.RequireUserId();
        dispatch.CancellationReason = request.Reason.Trim();

        _audit.Record(nameof(Dispatch), dispatch.Id, "Cancel",
            new { Status = DocumentStatus.Active.ToString(), dispatch.TotalAmount },
            new
            {
                Status = DocumentStatus.Cancelled.ToString(),
                dispatch.DispatchNumber,
                dispatch.CancellationReason,
                Returned = reversals.Select(r => new { Grade = r.Grade.ToString(), r.Quantity })
            });

        await _db.SaveChangesAsync(ct);

        return await GetAsync(id, ct);
    }

    public async Task<DispatchResponse> GetAsync(Guid id, CancellationToken ct = default)
    {
        var dispatch = await _db.Dispatches
            .AsNoTracking()
            .Include(d => d.Lines)
            .FirstOrDefaultAsync(d => d.Id == id, ct)
            ?? throw DomainException.NotFound("Dispatch", id.ToString());

        var balance = await CustomerBalances.ForAsync(_db, dispatch.CustomerId, ct);

        return BuildResponse(dispatch, dispatch.CustomerNameSnapshot, balance,
            new Dictionary<StockKey, int>(), await EnteredByAsync(dispatch.CreatedByUserId, ct), []);
    }

    public async Task<PagedResult<DispatchResponse>> ListAsync(
        DispatchQuery query, CancellationToken ct = default)
    {
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize <= 0 ? 50 : query.PageSize, 1, MaxPageSize);

        var dispatches = _db.Dispatches.AsNoTracking();

        if (!query.IncludeCancelled)
            dispatches = dispatches.Where(d => d.Status == DocumentStatus.Active);

        if (query.CustomerId is { } customerId)
            dispatches = dispatches.Where(d => d.CustomerId == customerId);

        if (query.From is { } from)
            dispatches = dispatches.Where(d => d.DispatchDate >= from);

        if (query.To is { } to)
            dispatches = dispatches.Where(d => d.DispatchDate <= to);

        var totalCount = await dispatches.CountAsync(ct);

        var rows = await dispatches
            .Include(d => d.Lines)
            .OrderByDescending(d => d.DispatchDate).ThenByDescending(d => d.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var userIds = rows.Select(r => r.CreatedByUserId).Distinct().ToList();
        var users = await _db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName, ct);

        var items = rows.Select(d => BuildResponse(
            d, d.CustomerNameSnapshot, 0m, new Dictionary<StockKey, int>(),
            users.GetValueOrDefault(d.CreatedByUserId, "unknown"), [])).ToList();

        return new PagedResult<DispatchResponse>(items, page, pageSize, totalCount);
    }

    private async Task<string> EnteredByAsync(Guid userId, CancellationToken ct) =>
        await _db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.FullName)
            .FirstOrDefaultAsync(ct) ?? "unknown";

    private static DispatchResponse BuildResponse(
        Dispatch dispatch, string customerName, decimal balanceAfter,
        IReadOnlyDictionary<StockKey, int> stockAfter, string enteredBy,
        IReadOnlyList<string> warnings) =>
        new(dispatch.Id, dispatch.DispatchNumber, dispatch.CustomerId, customerName,
            dispatch.DispatchDate,
            dispatch.Lines
                .OrderBy(l => l.LineNumber)
                .Select(l => new DispatchLineResponse(
                    l.LineNumber, l.ProductId, l.ProductCodeSnapshot, l.ProductNameSnapshot,
                    l.Grade, l.Quantity, l.UnitRate, l.LineAmount,
                    stockAfter.GetValueOrDefault(new StockKey(l.ProductId, l.Grade))))
                .ToList(),
            dispatch.TotalAmount, balanceAfter,
            dispatch.VehicleNumber, dispatch.Notes,
            dispatch.Status, enteredBy, dispatch.CreatedAt, warnings);
}
