using CrockeryFactory.Application.Common;
using CrockeryFactory.Application.Reports;
using CrockeryFactory.Application.Reports.Dtos;
using CrockeryFactory.Application.Production;
using CrockeryFactory.Application.Production.Dtos;
using CrockeryFactory.Application.Sales;
using CrockeryFactory.Application.Sales.Dtos;
using CrockeryFactory.Shared.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CrockeryFactory.Web.Controllers;

[ApiController]
[Route("api/v1/reports")]
[Authorize(Policy = Policies.CanViewReports)]
public sealed class ReportsController : ControllerBase
{
    private readonly IReportService _reports;
    private readonly ICustomerService _customers;
    private readonly IProductionService _production;

    public ReportsController(
        IReportService reports, ICustomerService customers, IProductionService production)
    {
        _reports = reports;
        _customers = customers;
        _production = production;
    }

    /// <summary>RP-01.</summary>
    [HttpGet("daily-stock")]
    public async Task<ActionResult<DailyStockResponse>> DailyStock(
        [FromQuery] DateOnly? date, [FromQuery] string? format, CancellationToken ct)
    {
        RequireJson(format);
        return Ok(await _reports.DailyStockAsync(date, ct));
    }

    /// <summary>RP-02. The same figures as /customers/outstanding, reached from the reports menu.</summary>
    [HttpGet("outstanding")]
    public async Task<ActionResult<OutstandingResponse>> Outstanding(
        [FromQuery] string? format, CancellationToken ct)
    {
        RequireJson(format);
        return Ok(await _customers.OutstandingAsync(ct));
    }

    /// <summary>RP-04.</summary>
    [HttpGet("production-summary")]
    public async Task<ActionResult<IReadOnlyList<ProductionSummaryRow>>> ProductionSummary(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] Guid? productId,
        [FromQuery] ProductionGroupBy groupBy = ProductionGroupBy.Product,
        [FromQuery] string? format = null,
        CancellationToken ct = default)
    {
        RequireJson(format);
        return Ok(await _production.SummaryAsync(new ProductionSummaryQuery(from, to, productId, groupBy), ct));
    }

    /// <summary>RP-06.</summary>
    [HttpGet("sales-summary")]
    public async Task<ActionResult<SalesSummaryResponse>> SalesSummary(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] SalesGroupBy groupBy = SalesGroupBy.Customer,
        [FromQuery] string? format = null,
        CancellationToken ct = default)
    {
        RequireJson(format);
        return Ok(await _reports.SalesSummaryAsync(from, to, groupBy, ct));
    }

    /// <summary>RP-07. Cached for a minute - it is polled, and these numbers need not be sub-second fresh.</summary>
    [HttpGet("dashboard")]
    public async Task<ActionResult<DashboardResponse>> Dashboard(CancellationToken ct) =>
        Ok(await _reports.DashboardAsync(ct));

    /// <summary>
    /// RP-08 asks for PDF and XLSX from every report. Both are routed and authorised so
    /// the shape of the API is settled, and both refuse honestly until the renderer is
    /// wired in. Returning JSON to a caller who asked for a spreadsheet would be worse:
    /// it looks like success and produces a file nothing can open.
    /// </summary>
    private static void RequireJson(string? format)
    {
        if (string.IsNullOrWhiteSpace(format) ||
            string.Equals(format, "json", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        throw new DomainException(
            ErrorCodes.NotImplemented, "Format not available yet",
            StatusCodes.Status501NotImplemented,
            $"This report is not available as '{format}' yet - only JSON. " +
            "PDF and spreadsheet output are a later piece of work.");
    }
}
