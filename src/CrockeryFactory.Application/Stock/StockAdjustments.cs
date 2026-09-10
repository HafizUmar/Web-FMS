using CrockeryFactory.Application.Abstractions;
using CrockeryFactory.Application.Common;
using CrockeryFactory.Application.Stock.Dtos;
using CrockeryFactory.Domain.Enums;
using CrockeryFactory.Modules.Stock.Entities;
using CrockeryFactory.Persistence;
using CrockeryFactory.Shared.Constants;
using Microsoft.EntityFrameworkCore;

namespace CrockeryFactory.Application.Stock;

public sealed class StockAdjustments : IStockAdjustments
{
    /// <summary>
    /// Above this, a bare number is not an explanation. The threshold is a judgement
    /// about what looks like a typo versus what looks like a decision.
    /// </summary>
    private const int NotesRequiredAbove = 500;

    private readonly FactoryDbContext _db;
    private readonly IStockService _stock;
    private readonly IFactorySettings _settings;
    private readonly IDocumentNumbers _numbers;
    private readonly IAuditWriter _audit;

    public StockAdjustments(
        FactoryDbContext db, IStockService stock, IFactorySettings settings,
        IDocumentNumbers numbers, IAuditWriter audit)
    {
        _db = db;
        _stock = stock;
        _settings = settings;
        _numbers = numbers;
        _audit = audit;
    }

    public async Task<AdjustmentResponse> CreateAsync(
        CreateAdjustmentRequest request, CancellationToken ct = default)
    {
        if (request.QuantityChange == 0)
        {
            throw DomainException.Validation(ErrorCodes.QuantityZero,
                "An adjustment of zero units records nothing.",
                new FieldError("quantityChange", "Must not be zero", ErrorCodes.QuantityZero));
        }

        if (Math.Abs(request.QuantityChange) > NotesRequiredAbove &&
            string.IsNullOrWhiteSpace(request.Notes))
        {
            // An unexplained change of this size is the dispute this endpoint exists to
            // prevent, and the note is what someone reads six months later.
            throw DomainException.Validation(ErrorCodes.NotesRequired,
                $"An adjustment of more than {NotesRequiredAbove} units needs a note explaining it.",
                new FieldError("notes", "Required for an adjustment this large", ErrorCodes.NotesRequired));
        }

        var product = await _db.Products.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == request.ProductId, ct)
            ?? throw DomainException.NotFound("Product", request.ProductId.ToString());

        await _settings.RequireGradeEnabledAsync(request.Grade, "grade", ct);

        await BusinessDates.ValidateAsync(
            request.AdjustedOn, "adjustedOn",
            SettingKeys.BackdateDaysAdjustment, 30, _db.Clock, _settings, ct);

        var reason = await ReasonCodeCheck.RequireAsync(
            _db, request.ReasonCodeId, ReasonCodeType.StockAdjustment, "reasonCodeId", ct);

        var adjustment = new StockAdjustment
        {
            Id = Guid.NewGuid(),
            AdjustmentNumber = await _numbers.NextAsync(
                DocumentSeries.StockAdjustment, request.AdjustedOn, ct),
            ProductId = request.ProductId,
            Grade = request.Grade,
            QuantityChange = request.QuantityChange,
            ReasonCodeId = request.ReasonCodeId,
            Notes = request.Notes?.Trim(),
            AdjustedOn = request.AdjustedOn,
            Status = DocumentStatus.Active,
            CreatedAt = _db.Clock.GetUtcNow().UtcDateTime,
            CreatedByUserId = _db.CurrentUser.RequireUserId()
        };

        _db.StockAdjustments.Add(adjustment);

        // Throws STOCK_INSUFFICIENT before anything is written if this would drive the
        // balance below zero (BR-01).
        await _stock.ApplyAsync(
        [
            new StockMovementRequest(
                request.ProductId, request.Grade, request.QuantityChange,
                StockMovementType.Adjustment, StockReferenceType.StockAdjustment, adjustment.Id,
                request.AdjustedOn, request.ReasonCodeId, adjustment.Notes)
        ], ct);

        // SE-13. A stock change with no document behind it is the thing an owner asks
        // about, so the audit row carries the reason and the note as entered.
        _audit.Record(nameof(StockAdjustment), adjustment.Id, "Create", null, new
        {
            adjustment.AdjustmentNumber,
            Product = product.Code,
            Grade = request.Grade.ToString(),
            request.QuantityChange,
            Reason = reason.Code,
            adjustment.Notes,
            adjustment.AdjustedOn
        });

        // One SaveChanges: the adjustment, its ledger row, the balance update and the
        // audit entry commit together or not at all.
        await _db.SaveChangesAsync(ct);

        var resulting = await _stock.GetBalanceAsync(request.ProductId, request.Grade, ct);

        return new AdjustmentResponse(
            adjustment.Id, adjustment.AdjustmentNumber, product.Id, product.Name,
            request.Grade, request.QuantityChange, resulting,
            reason.Description, request.AdjustedOn, adjustment.CreatedAt);
    }
}
