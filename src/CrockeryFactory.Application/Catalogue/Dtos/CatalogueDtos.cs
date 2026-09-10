using CrockeryFactory.Domain.Enums;

namespace CrockeryFactory.Application.Catalogue.Dtos;

public sealed record GradePrice(QualityGrade Grade, decimal UnitRate);

public sealed record GradeStock(QualityGrade Grade, int Quantity);

public sealed record GradePriceInput(QualityGrade Grade, decimal UnitRate);

/// <summary>
/// Price and availability come back with the product deliberately. The dispatch screen
/// needs all three together, and three round trips on factory WiFi is how PF-04 is missed.
/// </summary>
public sealed record ProductListItem(
    Guid Id, string Code, string Name, int? CapacityMl,
    bool IsActive, IReadOnlyList<GradePrice> Prices, IReadOnlyList<GradeStock> Stock);

public sealed record ProductResponse(
    Guid Id, string Code, string Name, int? CapacityMl, string? Description,
    bool IsActive, IReadOnlyList<GradePrice> Prices, DateTime CreatedAt);

public sealed record CreateProductRequest(
    string Code, string Name, int? CapacityMl, string? Description,
    IReadOnlyList<GradePriceInput> Prices);

public sealed record UpdateProductRequest(
    string Code, string Name, int? CapacityMl, string? Description);

public sealed record UpdatePricesRequest(
    IReadOnlyList<GradePriceInput> Prices, DateOnly EffectiveFrom, string? Reason);

public sealed record ProductQuery(
    string? Search = null,
    bool IncludeInactive = false,
    int Page = 1,
    int PageSize = 50);
