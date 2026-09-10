using System.Net;
using System.Net.Http.Json;
using CrockeryFactory.Application.Common;
using CrockeryFactory.IntegrationTests.Infrastructure;
using FluentAssertions;
using Xunit;

namespace CrockeryFactory.IntegrationTests.Reports;

[Collection(ApiCollection.Name)]
public class ReportTests
{
    private readonly FactoryScenario _scenario;

    public ReportTests(FactoryApiFixture api) => _scenario = new FactoryScenario(api);

    [RequiresSqlServerFact]
    public async Task The_daily_stock_sheet_adds_up_on_every_row()
    {
        // The whole point of this sheet: opening plus movements equals closing. A stock
        // sheet that does not add up is worse than no stock sheet.
        var owner = await _scenario.OwnerAsync();
        var product = await _scenario.CreateProductAsync(owner, "DAILY", firstRate: 100m);
        var customer = await _scenario.CreateCustomerAsync(owner, "DAILY");

        await _scenario.RecordProductionAsync(owner, product, good: 1000, seconds: 100, broken: 50);
        await _scenario.DispatchAsync(owner, customer, [FactoryScenario.Line(product, "First", 240)]);

        await owner.PostAsJsonAsync("/api/v1/stock/adjustments", new
        {
            productId = product,
            grade = "First",
            quantityChange = -10,
            reasonCodeId = FactoryScenario.SeededAdjustmentReason,
            adjustedOn = FactoryScenario.Today().ToString("yyyy-MM-dd"),
            notes = "Two saucers chipped in the store"
        }, ApiClientExtensions.Json);

        var date = FactoryScenario.Today().ToString("yyyy-MM-dd");
        var body = await (await owner.GetAsync($"/api/v1/reports/daily-stock?date={date}")).ReadJsonAsync();

        var row = body.GetProperty("rows").EnumerateArray()
            .Single(r => r.GetProperty("productId").GetGuid() == product &&
                         r.GetProperty("grade").GetString() == "First");

        row.GetProperty("opening").GetInt32().Should().Be(0);
        row.GetProperty("received").GetInt32().Should().Be(1000);
        row.GetProperty("dispatched").GetInt32().Should().Be(240, "dispatched reads as a positive number");
        row.GetProperty("adjusted").GetInt32().Should().Be(-10);
        row.GetProperty("closing").GetInt32().Should().Be(750);

        // opening + received - dispatched + adjusted == closing
        (row.GetProperty("opening").GetInt32()
         + row.GetProperty("received").GetInt32()
         - row.GetProperty("dispatched").GetInt32()
         + row.GetProperty("adjusted").GetInt32())
            .Should().Be(row.GetProperty("closing").GetInt32());
    }

    [RequiresSqlServerFact]
    public async Task Broken_pieces_never_reach_the_daily_sheet()
    {
        var owner = await _scenario.OwnerAsync();
        var product = await _scenario.CreateProductAsync(owner, "DAILYBRK");

        await _scenario.RecordProductionAsync(owner, product, good: 500, broken: 200);

        var date = FactoryScenario.Today().ToString("yyyy-MM-dd");
        var body = await (await owner.GetAsync($"/api/v1/reports/daily-stock?date={date}")).ReadJsonAsync();

        var rows = body.GetProperty("rows").EnumerateArray()
            .Where(r => r.GetProperty("productId").GetGuid() == product).ToList();

        rows.Sum(r => r.GetProperty("received").GetInt32()).Should().Be(500);
    }

    [RequiresSqlServerFact]
    public async Task A_dispatch_raised_and_cancelled_the_same_day_nets_to_nothing()
    {
        var owner = await _scenario.OwnerAsync();
        var product = await _scenario.CreateProductAsync(owner, "SAMEDAY", firstRate: 100m);
        var customer = await _scenario.CreateCustomerAsync(owner, "SAMEDAY");

        await _scenario.RecordProductionAsync(owner, product, good: 500);

        var id = (await (await _scenario.DispatchAsync(owner, customer,
            [FactoryScenario.Line(product, "First", 200)])).ReadJsonAsync()).GetProperty("id").GetGuid();

        await owner.PostAsJsonAsync($"/api/v1/dispatches/{id}/cancel",
            new { reason = "Vehicle broke down before leaving" }, ApiClientExtensions.Json);

        var date = FactoryScenario.Today().ToString("yyyy-MM-dd");
        var body = await (await owner.GetAsync($"/api/v1/reports/daily-stock?date={date}")).ReadJsonAsync();

        var row = body.GetProperty("rows").EnumerateArray()
            .Single(r => r.GetProperty("productId").GetGuid() == product &&
                         r.GetProperty("grade").GetString() == "First");

        row.GetProperty("dispatched").GetInt32().Should().Be(0,
            "a dispatch and its cancellation net off rather than inflating both columns");
        row.GetProperty("closing").GetInt32().Should().Be(500);
    }

    [RequiresSqlServerFact]
    public async Task The_sales_summary_counts_a_multi_line_dispatch_once()
    {
        var owner = await _scenario.OwnerAsync();
        var product = await _scenario.CreateProductAsync(owner, "SALESUM", firstRate: 100m, secondRate: 50m);
        var customer = await _scenario.CreateCustomerAsync(owner, "SALESUM");

        await _scenario.RecordProductionAsync(owner, product, good: 1000, seconds: 500);

        await _scenario.DispatchAsync(owner, customer,
        [
            FactoryScenario.Line(product, "First", 100),
            FactoryScenario.Line(product, "Second", 200)
        ]);

        var from = FactoryScenario.Today().AddDays(-7).ToString("yyyy-MM-dd");
        var to = FactoryScenario.Today().ToString("yyyy-MM-dd");

        var body = await (await owner.GetAsync(
            $"/api/v1/reports/sales-summary?from={from}&to={to}&groupBy=Customer")).ReadJsonAsync();

        var row = body.GetProperty("rows").EnumerateArray()
            .Single(r => r.GetProperty("groupKey").GetString() == customer.ToString());

        row.GetProperty("dispatchCount").GetInt32().Should().Be(1, "a two-line dispatch is one dispatch");
        row.GetProperty("totalQuantity").GetInt32().Should().Be(300);
        row.GetProperty("totalAmount").GetDecimal().Should().Be(20_000m);
    }

    [RequiresSqlServerFact]
    public async Task A_cancelled_dispatch_is_not_a_sale()
    {
        var owner = await _scenario.OwnerAsync();
        var product = await _scenario.CreateProductAsync(owner, "NOTSALE", firstRate: 100m);
        var customer = await _scenario.CreateCustomerAsync(owner, "NOTSALE");

        await _scenario.RecordProductionAsync(owner, product, good: 500);

        var id = (await (await _scenario.DispatchAsync(owner, customer,
            [FactoryScenario.Line(product, "First", 100)])).ReadJsonAsync()).GetProperty("id").GetGuid();

        await owner.PostAsJsonAsync($"/api/v1/dispatches/{id}/cancel",
            new { reason = "Customer cancelled the order by telephone" }, ApiClientExtensions.Json);

        var from = FactoryScenario.Today().AddDays(-7).ToString("yyyy-MM-dd");
        var to = FactoryScenario.Today().ToString("yyyy-MM-dd");

        var body = await (await owner.GetAsync(
            $"/api/v1/reports/sales-summary?from={from}&to={to}")).ReadJsonAsync();

        body.GetProperty("rows").EnumerateArray()
            .Should().NotContain(r => r.GetProperty("groupKey").GetString() == customer.ToString());
    }

    [RequiresSqlServerFact]
    public async Task The_dashboard_answers_in_one_call()
    {
        var owner = await _scenario.OwnerAsync();
        var product = await _scenario.CreateProductAsync(owner, "DASH", firstRate: 100m);
        var customer = await _scenario.CreateCustomerAsync(owner, "DASH");

        await _scenario.RecordProductionAsync(owner, product, good: 1000, broken: 100);
        await _scenario.DispatchAsync(owner, customer, [FactoryScenario.Line(product, "First", 300)]);

        var body = await (await owner.GetAsync("/api/v1/reports/dashboard")).ReadJsonAsync();

        body.GetProperty("totalUnitsInStock").GetInt32().Should().BeGreaterThan(0);
        body.GetProperty("unitsProducedThisMonth").GetInt32().Should().BeGreaterThanOrEqualTo(1000);
        body.GetProperty("salesThisMonth").GetDecimal().Should().BeGreaterThanOrEqualTo(30_000m);
        body.GetProperty("lossPercentageThisMonth").GetDecimal().Should().BeGreaterThan(0m);
        body.TryGetProperty("topDebtors", out _).Should().BeTrue();
        body.TryGetProperty("lowStockProducts", out _).Should().BeTrue();
    }

    [RequiresSqlServerTheory]
    [InlineData("daily-stock")]
    [InlineData("sales-summary")]
    [InlineData("production-summary")]
    [InlineData("outstanding")]
    public async Task Asking_for_a_spreadsheet_refuses_honestly_rather_than_returning_json(string report)
    {
        // Returning JSON to a caller who asked for xlsx looks like success and produces
        // a file nothing can open.
        var owner = await _scenario.OwnerAsync();

        var response = await owner.GetAsync($"/api/v1/reports/{report}?format=xlsx");

        response.StatusCode.Should().Be(HttpStatusCode.NotImplemented);
        (await response.ErrorCodeAsync()).Should().Be(ErrorCodes.NotImplemented);
    }

    [RequiresSqlServerFact]
    public async Task A_clerk_may_read_the_reports()
    {
        var clerk = await _scenario.ClerkAsync();

        (await clerk.GetAsync("/api/v1/reports/dashboard")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await clerk.GetAsync("/api/v1/reports/outstanding")).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
