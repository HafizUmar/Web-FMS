using CrockeryFactory.Application.Reports.Dtos;

namespace CrockeryFactory.Application.Reports;

public interface IReportService
{
    Task<DailyStockResponse> DailyStockAsync(DateOnly? date, CancellationToken ct = default);

    Task<SalesSummaryResponse> SalesSummaryAsync(
        DateOnly? from, DateOnly? to, SalesGroupBy groupBy, CancellationToken ct = default);

    Task<DashboardResponse> DashboardAsync(CancellationToken ct = default);
}
