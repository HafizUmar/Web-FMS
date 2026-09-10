using CrockeryFactory.Domain.Enums;

namespace CrockeryFactory.Application.Production.Dtos;

public sealed record CreateProductionEntryRequest(
    Guid ProductId, DateOnly EntryDate,
    int QuantityGood, int QuantitySeconds, int QuantityBroken,
    Guid? BreakageReasonCodeId, string? BatchReference, string? Notes);

public sealed record ProductionEntryResponse(
    Guid Id, string EntryNumber, Guid ProductId, string ProductCode, string ProductName,
    DateOnly EntryDate, int QuantityGood, int QuantitySeconds, int QuantityBroken,
    int TotalFired, decimal LossPercentage,
    string? BreakageReason, string? BatchReference, string? Notes,
    DocumentStatus Status, string EnteredBy, DateTime CreatedAt,
    IReadOnlyList<string> Warnings);

public sealed record CancelRequest(string Reason);

public sealed record ProductionEntryQuery(
    Guid? ProductId = null,
    DateOnly? From = null,
    DateOnly? To = null,
    bool IncludeCancelled = false,
    int Page = 1,
    int PageSize = 50);

public sealed record ProductionSummaryRow(
    string GroupKey, string GroupLabel,
    int TotalFired, int TotalGood, int TotalSeconds, int TotalBroken,
    decimal LossPercentage, decimal SecondsPercentage, int EntryCount);

public enum ProductionGroupBy
{
    Product,
    Day,
    Month
}

public sealed record ProductionSummaryQuery(
    DateOnly? From = null,
    DateOnly? To = null,
    Guid? ProductId = null,
    ProductionGroupBy GroupBy = ProductionGroupBy.Product);
