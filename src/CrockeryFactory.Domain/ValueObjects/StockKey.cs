using CrockeryFactory.Domain.Enums;

namespace CrockeryFactory.Domain.ValueObjects;

/// <summary>
/// Identifies a stock line. Stock is held per product AND grade - BRD section 5.
/// Passing these as two loose parameters is how they eventually get swapped.
/// </summary>
public readonly record struct StockKey(Guid ProductId, QualityGrade Grade);
