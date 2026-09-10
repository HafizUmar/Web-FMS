using CrockeryFactory.Application.Sales.Dtos;
using CrockeryFactory.Application.Stock.Dtos;
using CrockeryFactory.Domain.Enums;

namespace CrockeryFactory.Application.Reports.Dtos;

/// <summary>
/// RP-01. The sheet the owner reads at the end of the day: what was there, what came in
/// off the kiln, what went out on a vehicle, what was corrected, and what is left.
/// </summary>
public sealed record DailyStockRow(
    Guid ProductId, string ProductCode, string ProductName, QualityGrade Grade,
    int Opening, int Received, int Dispatched, int Adjusted, int Closing);

public sealed record DailyStockResponse(
    DateOnly Date, IReadOnlyList<DailyStockRow> Rows,
    int TotalOpening, int TotalReceived, int TotalDispatched, int TotalAdjusted, int TotalClosing);

public enum SalesGroupBy
{
    Customer,
    Product,
    Month
}

public sealed record SalesSummaryRow(
    string GroupKey, string GroupLabel,
    int DispatchCount, int TotalQuantity, decimal TotalAmount);

public sealed record SalesSummaryResponse(
    DateOnly From, DateOnly To, SalesGroupBy GroupBy,
    IReadOnlyList<SalesSummaryRow> Rows, int TotalQuantity, decimal TotalAmount);

/// <summary>RP-07. Polled, so it is cached for a minute - these numbers do not need to be sub-second fresh.</summary>
public sealed record DashboardResponse(
    int TotalUnitsInStock, decimal StockValue,
    decimal TotalOutstanding, int CustomersWithBalance,
    int UnitsProducedThisMonth, decimal LossPercentageThisMonth,
    decimal SalesThisMonth, decimal PaymentsThisMonth,
    IReadOnlyList<StockLine> LowStockProducts,
    IReadOnlyList<OutstandingRow> TopDebtors,
    DateTime GeneratedAt);
