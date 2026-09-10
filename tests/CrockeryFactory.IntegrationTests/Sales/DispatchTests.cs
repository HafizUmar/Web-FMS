using System.Net;
using System.Net.Http.Json;
using CrockeryFactory.Application.Common;
using CrockeryFactory.IntegrationTests.Infrastructure;
using FluentAssertions;
using Xunit;

namespace CrockeryFactory.IntegrationTests.Sales;

[Collection(ApiCollection.Name)]
public class DispatchTests
{
    private readonly FactoryApiFixture _api;
    private readonly FactoryScenario _scenario;

    public DispatchTests(FactoryApiFixture api)
    {
        _api = api;
        _scenario = new FactoryScenario(api);
    }

    [RequiresSqlServerFact]
    public async Task A_dispatch_prices_its_lines_takes_the_stock_and_reports_the_new_balance()
    {
        var owner = await _scenario.OwnerAsync();
        var product = await _scenario.CreateProductAsync(owner, "SELL", firstRate: 120m);
        var customer = await _scenario.CreateCustomerAsync(owner, "SELL");

        await _scenario.RecordProductionAsync(owner, product, good: 1000);

        var response = await _scenario.DispatchAsync(owner, customer,
            [FactoryScenario.Line(product, "First", 240)]);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await response.ReadJsonAsync();
        body.GetProperty("dispatchNumber").GetString().Should().StartWith("D-");
        body.GetProperty("totalAmount").GetDecimal().Should().Be(28_800m);

        var line = body.GetProperty("lines").EnumerateArray().Single();
        line.GetProperty("unitRate").GetDecimal().Should().Be(120m);
        line.GetProperty("lineAmount").GetDecimal().Should().Be(28_800m);
        line.GetProperty("stockAfter").GetInt32().Should().Be(760);

        // Returned so the clerk can read the new balance aloud without a second request,
        // which is worth about ten seconds against PF-11.
        body.GetProperty("customerBalanceAfter").GetDecimal().Should().Be(28_800m);

        (await _scenario.StockAsync(owner, product, "First")).Should().Be(760);
    }

    [RequiresSqlServerFact]
    public async Task A_dispatch_snapshots_the_product_code_name_and_rate()
    {
        // BR-06. Renaming a product or changing its price must not rewrite a bill that
        // has already gone out of the gate.
        var owner = await _scenario.OwnerAsync();
        var product = await _scenario.CreateProductAsync(owner, "SNAP", firstRate: 100m);
        var customer = await _scenario.CreateCustomerAsync(owner, "SNAP");

        await _scenario.RecordProductionAsync(owner, product, good: 500);

        var dispatchId = (await (await _scenario.DispatchAsync(owner, customer,
            [FactoryScenario.Line(product, "First", 100)])).ReadJsonAsync())
            .GetProperty("id").GetGuid();

        await owner.PutAsJsonAsync($"/api/v1/products/{product}/prices", new
        {
            prices = new[] { new { grade = "First", unitRate = 400m } },
            effectiveFrom = FactoryScenario.Today().ToString("yyyy-MM-dd"),
            reason = "Cost of clay increased"
        }, ApiClientExtensions.Json);

        var reread = await (await owner.GetAsync($"/api/v1/dispatches/{dispatchId}")).ReadJsonAsync();

        reread.GetProperty("totalAmount").GetDecimal().Should().Be(10_000m,
            "the old bill must still read what it said when it was printed");
        reread.GetProperty("lines").EnumerateArray().Single()
            .GetProperty("unitRate").GetDecimal().Should().Be(100m);
    }

    [RequiresSqlServerFact]
    public async Task A_dispatch_that_fails_on_one_line_writes_nothing_at_all()
    {
        // The whole point of checking every line before writing any: a four-line
        // dispatch that fails on line three must leave nothing behind.
        var owner = await _scenario.OwnerAsync();
        var plentiful = await _scenario.CreateProductAsync(owner, "PLENTY");
        var scarce = await _scenario.CreateProductAsync(owner, "SCARCE");
        var customer = await _scenario.CreateCustomerAsync(owner, "PARTIAL");

        await _scenario.RecordProductionAsync(owner, plentiful, good: 1000);
        await _scenario.RecordProductionAsync(owner, scarce, good: 50);

        var response = await _scenario.DispatchAsync(owner, customer,
        [
            FactoryScenario.Line(plentiful, "First", 100),
            FactoryScenario.Line(scarce, "First", 900)
        ]);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.ErrorCodeAsync()).Should().Be(ErrorCodes.StockInsufficient);

        // Neither line moved.
        (await _scenario.StockAsync(owner, plentiful, "First")).Should().Be(1000);
        (await _scenario.StockAsync(owner, scarce, "First")).Should().Be(50);

        // And no document was created.
        var listed = await (await owner.GetAsync($"/api/v1/dispatches?customerId={customer}")).ReadJsonAsync();
        listed.GetProperty("totalCount").GetInt32().Should().Be(0);

        // And the customer owes nothing.
        (await _scenario.BalanceAsync(owner, customer)).Should().Be(0m);
    }

    [RequiresSqlServerFact]
    public async Task Two_lines_for_the_same_product_and_grade_are_refused()
    {
        // Each would pass the stock check alone and together take more than exists.
        var owner = await _scenario.OwnerAsync();
        var product = await _scenario.CreateProductAsync(owner, "DUPLINE");
        var customer = await _scenario.CreateCustomerAsync(owner, "DUPLINE");

        await _scenario.RecordProductionAsync(owner, product, good: 1000);

        var response = await _scenario.DispatchAsync(owner, customer,
        [
            FactoryScenario.Line(product, "First", 100),
            FactoryScenario.Line(product, "First", 200)
        ]);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.ErrorCodeAsync()).Should().Be(ErrorCodes.DuplicateLine);
    }

    [RequiresSqlServerFact]
    public async Task The_same_product_at_two_grades_is_allowed()
    {
        var owner = await _scenario.OwnerAsync();
        var product = await _scenario.CreateProductAsync(owner, "TWOGRADE", firstRate: 100m, secondRate: 60m);
        var customer = await _scenario.CreateCustomerAsync(owner, "TWOGRADE");

        await _scenario.RecordProductionAsync(owner, product, good: 500, seconds: 200);

        var response = await _scenario.DispatchAsync(owner, customer,
        [
            FactoryScenario.Line(product, "First", 100),
            FactoryScenario.Line(product, "Second", 50)
        ]);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        (await response.ReadJsonAsync()).GetProperty("totalAmount").GetDecimal()
            .Should().Be(13_000m, "100 firsts at 100 plus 50 seconds at 60");
    }

    [RequiresSqlServerFact]
    public async Task A_dispatch_with_no_lines_is_refused()
    {
        var owner = await _scenario.OwnerAsync();
        var customer = await _scenario.CreateCustomerAsync(owner, "EMPTY");

        var response = await _scenario.DispatchAsync(owner, customer, []);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.ErrorCodeAsync()).Should().Be(ErrorCodes.NoLines);
    }

    [RequiresSqlServerFact]
    public async Task A_clerks_rate_overrides_the_list_price()
    {
        var owner = await _scenario.OwnerAsync();
        var product = await _scenario.CreateProductAsync(owner, "OVERRIDE", firstRate: 100m);
        var customer = await _scenario.CreateCustomerAsync(owner, "OVERRIDE");

        await _scenario.RecordProductionAsync(owner, product, good: 500);

        var body = await (await _scenario.DispatchAsync(owner, customer,
            [FactoryScenario.Line(product, "First", 100, unitRate: 92m)])).ReadJsonAsync();

        body.GetProperty("lines").EnumerateArray().Single()
            .GetProperty("unitRate").GetDecimal().Should().Be(92m);
        body.GetProperty("totalAmount").GetDecimal().Should().Be(9_200m);
    }

    [RequiresSqlServerFact]
    public async Task A_rate_far_below_list_warns_but_still_saves()
    {
        var owner = await _scenario.OwnerAsync();
        var product = await _scenario.CreateProductAsync(owner, "CHEAP", firstRate: 100m);
        var customer = await _scenario.CreateCustomerAsync(owner, "CHEAP");

        await _scenario.RecordProductionAsync(owner, product, good: 500);

        var response = await _scenario.DispatchAsync(owner, customer,
            [FactoryScenario.Line(product, "First", 100, unitRate: 10m)]);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        (await response.ReadJsonAsync()).GetProperty("warnings").EnumerateArray()
            .Select(w => w.GetString()).Should().Contain(WarningCodes.RateBelowList);
    }

    [RequiresSqlServerFact]
    public async Task A_product_with_no_price_and_no_rate_on_the_line_is_refused()
    {
        var owner = await _scenario.OwnerAsync();
        var product = await _scenario.CreateProductAsync(owner, "NOSECOND", firstRate: 100m, secondRate: null);
        var customer = await _scenario.CreateCustomerAsync(owner, "NOSECOND");

        await _scenario.RecordProductionAsync(owner, product, good: 100, seconds: 100);

        var response = await _scenario.DispatchAsync(owner, customer,
            [FactoryScenario.Line(product, "Second", 10)]);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.ErrorCodeAsync()).Should().Be(ErrorCodes.NoPriceAvailable);
    }

    [RequiresSqlServerFact]
    public async Task An_inactive_customer_cannot_be_dispatched_to()
    {
        var owner = await _scenario.OwnerAsync();
        var product = await _scenario.CreateProductAsync(owner, "GONECUST");
        var customer = await _scenario.CreateCustomerAsync(owner, "GONECUST");

        await _scenario.RecordProductionAsync(owner, product, good: 500);
        await owner.PostAsync($"/api/v1/customers/{customer}/deactivate", null);

        var response = await _scenario.DispatchAsync(owner, customer,
            [FactoryScenario.Line(product, "First", 10)]);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.ErrorCodeAsync()).Should().Be(ErrorCodes.CustomerInactive);
    }

    [RequiresSqlServerFact]
    public async Task Cancelling_a_dispatch_returns_the_stock_and_clears_the_balance()
    {
        var owner = await _scenario.OwnerAsync();
        var product = await _scenario.CreateProductAsync(owner, "CANCELD", firstRate: 100m);
        var customer = await _scenario.CreateCustomerAsync(owner, "CANCELD");

        await _scenario.RecordProductionAsync(owner, product, good: 500);

        var id = (await (await _scenario.DispatchAsync(owner, customer,
            [FactoryScenario.Line(product, "First", 200)])).ReadJsonAsync())
            .GetProperty("id").GetGuid();

        (await _scenario.StockAsync(owner, product, "First")).Should().Be(300);

        var response = await owner.PostAsJsonAsync($"/api/v1/dispatches/{id}/cancel",
            new { reason = "Vehicle never left the yard" }, ApiClientExtensions.Json);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.ReadJsonAsync()).GetProperty("status").GetString().Should().Be("Cancelled");

        (await _scenario.StockAsync(owner, product, "First")).Should().Be(500);
        (await _scenario.BalanceAsync(owner, customer)).Should().Be(0m);
    }

    [RequiresSqlServerFact]
    public async Task An_administrator_cannot_dispatch()
    {
        var admin = await _api.CreateApiClient().SignedInAsAsync(FactoryApiFixture.AdminUserName);

        var response = await new FactoryScenario(_api).DispatchAsync(admin, Guid.NewGuid(),
            [FactoryScenario.Line(Guid.NewGuid(), "First", 10)]);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
