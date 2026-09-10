namespace CrockeryFactory.Application.Abstractions;

/// <summary>
/// Allocates the next document number in the {Prefix}-{yyMM}-{seq:D4} series, e.g.
/// D-2609-0001 (spec open item BE-4).
///
/// The prefix comes from settings so a factory continuing an existing paper register
/// series is a configuration change.
/// </summary>
public interface IDocumentNumbers
{
    Task<string> NextAsync(DocumentSeries series, DateOnly documentDate, CancellationToken ct = default);
}

public enum DocumentSeries
{
    Dispatch,
    ProductionEntry,
    Payment,
    StockAdjustment
}
