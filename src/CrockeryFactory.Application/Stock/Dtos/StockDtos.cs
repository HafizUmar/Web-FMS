using CrockeryFactory.Domain.Enums;

namespace CrockeryFactory.Application.Stock.Dtos;

public sealed record StockLine(
    Guid ProductId, string ProductCode, string ProductName,
    QualityGrade Grade, int Quantity, decimal? UnitRate,
    decimal? StockValue, DateTime? LastMovementAt);

public sealed record StockResponse(
    IReadOnlyList<StockLine> Lines, int TotalUnits, decimal TotalValue, DateOnly AsOf);

public sealed record StockQuery(
    string? Search = null,
    QualityGrade? Grade = null,
    bool OnlyInStock = false,
    DateOnly? AsOf = null);

public sealed record StockMovementItem(
    Guid Id, DateOnly OccurredOn, QualityGrade Grade, int Quantity,
    StockMovementType MovementType, string? ReferenceNumber,
    string? ReasonDescription, string? Notes,
    string EnteredBy, DateTime CreatedAt, int RunningBalance);

public sealed record MovementQuery(
    QualityGrade? Grade = null,
    DateOnly? From = null,
    DateOnly? To = null,
    int Page = 1,
    int PageSize = 50);

public sealed record CreateAdjustmentRequest(
    Guid ProductId, QualityGrade Grade, int QuantityChange,
    Guid ReasonCodeId, DateOnly AdjustedOn, string? Notes);

public sealed record AdjustmentResponse(
    Guid Id, string AdjustmentNumber, Guid ProductId, string ProductName,
    QualityGrade Grade, int QuantityChange, int ResultingBalance,
    string ReasonDescription, DateOnly AdjustedOn, DateTime CreatedAt);
