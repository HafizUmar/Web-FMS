using System.Net;
using System.Net.Http.Json;
using CrockeryFactory.Application.Common;
using CrockeryFactory.IntegrationTests.Infrastructure;
using FluentAssertions;
using Xunit;

namespace CrockeryFactory.IntegrationTests.Production;

[Collection(ApiCollection.Name)]
public class ProductionEntryTests
{
    private readonly FactoryApiFixture _api;
    private readonly FactoryScenario _scenario;

    public ProductionEntryTests(FactoryApiFixture api)
    {
        _api = api;
        _scenario = new FactoryScenario(api);
    }

    private static object Entry(Guid productId, int good, int seconds, int broken,
        Guid? reason = null, DateOnly? date = null) => new
    {
        productId,
        entryDate = (date ?? FactoryScenario.Today()).ToString("yyyy-MM-dd"),
        quantityGood = good,
        quantitySeconds = seconds,
        quantityBroken = broken,
        breakageReasonCodeId = broken > 0 ? reason ?? FactoryScenario.SeededBreakageReason : (Guid?)null
    };

    [RequiresSqlServerFact]
    public async Task An_entry_reports_its_total_and_loss_percentage()
    {
        var owner = await _scenario.OwnerAsync();
        var product = await _scenario.CreateProductAsync(owner, "LOSS");

        var body = await _scenario.RecordProductionAsync(owner, product, good: 900, seconds: 60, broken: 40);

        body.GetProperty("totalFired").GetInt32().Should().Be(1000);
        body.GetProperty("lossPercentage").GetDecimal().Should().Be(4.00m);
        body.GetProperty("entryNumber").GetString().Should().StartWith("P-");
        body.GetProperty("status").GetString().Should().Be("Active");
    }

    [RequiresSqlServerFact]
    public async Task An_entry_recording_nothing_is_refused()
    {
        // BR-03. An entry with every quantity zero is a mistake, not an empty firing.
        var owner = await _scenario.OwnerAsync();
        var product = await _scenario.CreateProductAsync(owner, "NOQTY");

        var response = await owner.PostAsJsonAsync("/api/v1/production-entries",
            Entry(product, 0, 0, 0), ApiClientExtensions.Json);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.ErrorCodeAsync()).Should().Be(ErrorCodes.NoQuantity);
    }

    [RequiresSqlServerFact]
    public async Task Breakage_without_a_reason_is_refused()
    {
        var owner = await _scenario.OwnerAsync();
        var product = await _scenario.CreateProductAsync(owner, "NOREASON");

        var response = await owner.PostAsJsonAsync("/api/v1/production-entries", new
        {
            productId = product,
            entryDate = FactoryScenario.Today().ToString("yyyy-MM-dd"),
            quantityGood = 500,
            quantitySeconds = 0,
            quantityBroken = 50
        }, ApiClientExtensions.Json);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.ErrorCodeAsync()).Should().Be(ErrorCodes.BreakageReasonRequired);
    }

    [RequiresSqlServerFact]
    public async Task A_high_loss_is_warned_about_but_recorded_anyway()
    {
        // The important half of this test is that it is a 201. The first time the system
        // refuses to accept what actually happened is the day the clerk goes back to the
        // paper register.
        var owner = await _scenario.OwnerAsync();
        var product = await _scenario.CreateProductAsync(owner, "HIGHLOSS");

        var response = await owner.PostAsJsonAsync("/api/v1/production-entries",
            Entry(product, good: 600, seconds: 0, broken: 400), ApiClientExtensions.Json);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await response.ReadJsonAsync();
        body.GetProperty("lossPercentage").GetDecimal().Should().Be(40.00m);
        body.GetProperty("warnings").EnumerateArray().Select(w => w.GetString())
            .Should().Contain(WarningCodes.LossUnusuallyHigh);

        // And the stock actually moved.
        (await _scenario.StockAsync(owner, product, "First")).Should().Be(600);
    }

    [RequiresSqlServerFact]
    public async Task A_normal_loss_produces_no_warning()
    {
        var owner = await _scenario.OwnerAsync();
        var product = await _scenario.CreateProductAsync(owner, "OKLOSS");

        var body = await _scenario.RecordProductionAsync(owner, product, good: 950, broken: 50);

        body.GetProperty("warnings").GetArrayLength().Should().Be(0);
    }

    [RequiresSqlServerFact]
    public async Task An_implausible_quantity_is_refused()
    {
        var owner = await _scenario.OwnerAsync();
        var product = await _scenario.CreateProductAsync(owner, "HUGE");

        var response = await owner.PostAsJsonAsync("/api/v1/production-entries",
            Entry(product, good: 900_000, seconds: 0, broken: 0), ApiClientExtensions.Json);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.ErrorCodeAsync()).Should().Be(ErrorCodes.QuantityImplausible);
    }

    [RequiresSqlServerFact]
    public async Task A_future_date_is_refused()
    {
        var owner = await _scenario.OwnerAsync();
        var product = await _scenario.CreateProductAsync(owner, "FUTURE");

        var response = await owner.PostAsJsonAsync("/api/v1/production-entries",
            Entry(product, 100, 0, 0, date: FactoryScenario.Today().AddDays(1)),
            ApiClientExtensions.Json);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.ErrorCodeAsync()).Should().Be(ErrorCodes.DateInFuture);
    }

    [RequiresSqlServerFact]
    public async Task A_date_beyond_the_backdating_window_is_refused()
    {
        var owner = await _scenario.OwnerAsync();
        var product = await _scenario.CreateProductAsync(owner, "OLD");

        var response = await owner.PostAsJsonAsync("/api/v1/production-entries",
            Entry(product, 100, 0, 0, date: FactoryScenario.Today().AddDays(-30)),
            ApiClientExtensions.Json);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.ErrorCodeAsync()).Should().Be(ErrorCodes.DateTooOld);
    }

    [RequiresSqlServerFact]
    public async Task Cancelling_reverses_the_stock_and_keeps_both_rows()
    {
        // BR-08. The original entry and its movements are never deleted or edited - the
        // history shows what happened rather than concealing it.
        var owner = await _scenario.OwnerAsync();
        var product = await _scenario.CreateProductAsync(owner, "CANCEL");

        var entry = await _scenario.RecordProductionAsync(owner, product, good: 400, seconds: 40);
        var id = entry.GetProperty("id").GetGuid();

        var response = await owner.PostAsJsonAsync($"/api/v1/production-entries/{id}/cancel",
            new { reason = "Entered against the wrong product code" }, ApiClientExtensions.Json);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.ReadJsonAsync()).GetProperty("status").GetString().Should().Be("Cancelled");

        (await _scenario.StockAsync(owner, product, "First")).Should().Be(0);
        (await _scenario.StockAsync(owner, product, "Second")).Should().Be(0);

        // Both the receipt and the reversal remain visible in the ledger.
        var movements = await (await owner.GetAsync(
            $"/api/v1/stock/{product}/movements?grade=First")).ReadJsonAsync();

        var types = movements.GetProperty("items").EnumerateArray()
            .Select(m => m.GetProperty("movementType").GetString()).ToList();

        types.Should().Contain("ProductionReceipt");
        types.Should().Contain("ProductionCancellation");
    }

    [RequiresSqlServerFact]
    public async Task Cancelling_needs_a_reason_of_some_substance()
    {
        var owner = await _scenario.OwnerAsync();
        var product = await _scenario.CreateProductAsync(owner, "SHORTREASON");

        var entry = await _scenario.RecordProductionAsync(owner, product, good: 100);
        var id = entry.GetProperty("id").GetGuid();

        var response = await owner.PostAsJsonAsync($"/api/v1/production-entries/{id}/cancel",
            new { reason = "oops" }, ApiClientExtensions.Json);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.ErrorCodeAsync()).Should().Be(ErrorCodes.ReasonRequired);
    }

    [RequiresSqlServerFact]
    public async Task Cancelling_twice_is_refused()
    {
        var owner = await _scenario.OwnerAsync();
        var product = await _scenario.CreateProductAsync(owner, "TWICE");

        var entry = await _scenario.RecordProductionAsync(owner, product, good: 100);
        var id = entry.GetProperty("id").GetGuid();

        var reason = new { reason = "Duplicate of the previous entry" };

        (await owner.PostAsJsonAsync($"/api/v1/production-entries/{id}/cancel", reason,
            ApiClientExtensions.Json)).StatusCode.Should().Be(HttpStatusCode.OK);

        var second = await owner.PostAsJsonAsync($"/api/v1/production-entries/{id}/cancel", reason,
            ApiClientExtensions.Json);

        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await second.ErrorCodeAsync()).Should().Be(ErrorCodes.AlreadyCancelled);
    }

    [RequiresSqlServerFact]
    public async Task An_entry_whose_stock_has_since_left_cannot_be_cancelled()
    {
        // Cups produced on Monday and shipped on Tuesday cannot have Monday's production
        // cancelled on Wednesday - the stock is gone. The clerk needs an adjustment, and
        // the message has to say so, because "insufficient stock" on a cancellation reads
        // as nonsense on its own.
        var owner = await _scenario.OwnerAsync();
        var product = await _scenario.CreateProductAsync(owner, "GONE");

        var entry = await _scenario.RecordProductionAsync(owner, product, good: 300);
        var id = entry.GetProperty("id").GetGuid();

        // Take the stock back out by another route, as a dispatch would.
        await owner.PostAsJsonAsync("/api/v1/stock/adjustments", new
        {
            productId = product,
            grade = "First",
            quantityChange = -300,
            reasonCodeId = FactoryScenario.SeededAdjustmentReason,
            adjustedOn = FactoryScenario.Today().ToString("yyyy-MM-dd"),
            notes = "Issued out of the godown before the entry was questioned"
        }, ApiClientExtensions.Json);

        var response = await owner.PostAsJsonAsync($"/api/v1/production-entries/{id}/cancel",
            new { reason = "Recorded against the wrong kiln load" }, ApiClientExtensions.Json);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.ErrorCodeAsync()).Should().Be(ErrorCodes.StockInsufficientForReversal);

        var detail = (await response.ReadJsonAsync()).GetProperty("detail").GetString()!;
        detail.Should().Contain("adjustment", "the message must say what to do instead");

        // And the entry is still active - a refused cancellation changes nothing.
        var entryNow = await (await owner.GetAsync($"/api/v1/production-entries/{id}")).ReadJsonAsync();
        entryNow.GetProperty("status").GetString().Should().Be("Active");
    }

    [RequiresSqlServerFact]
    public async Task A_clerk_cannot_cancel_an_older_entry()
    {
        // PR-07. The clerk fixes today's mistake; anything older is the owner's decision.
        var owner = await _scenario.OwnerAsync();
        var clerk = await _scenario.ClerkAsync();
        var product = await _scenario.CreateProductAsync(owner, "YESTERDAY");

        var entry = await _scenario.RecordProductionAsync(
            clerk, product, good: 200, entryDate: FactoryScenario.Today().AddDays(-2));
        var id = entry.GetProperty("id").GetGuid();

        var response = await clerk.PostAsJsonAsync($"/api/v1/production-entries/{id}/cancel",
            new { reason = "Wrong product selected at entry time" }, ApiClientExtensions.Json);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.ErrorCodeAsync()).Should().Be(ErrorCodes.CancellationWindowExpired);
    }

    [RequiresSqlServerFact]
    public async Task A_clerk_may_cancel_the_same_days_entry()
    {
        var owner = await _scenario.OwnerAsync();
        var clerk = await _scenario.ClerkAsync();
        var product = await _scenario.CreateProductAsync(owner, "TODAY");

        var entry = await _scenario.RecordProductionAsync(clerk, product, good: 200);
        var id = entry.GetProperty("id").GetGuid();

        var response = await clerk.PostAsJsonAsync($"/api/v1/production-entries/{id}/cancel",
            new { reason = "Keyed the quantity twice by mistake" }, ApiClientExtensions.Json);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [RequiresSqlServerFact]
    public async Task An_owner_may_cancel_an_older_entry()
    {
        var owner = await _scenario.OwnerAsync();
        var product = await _scenario.CreateProductAsync(owner, "OWNEROLD");

        var entry = await _scenario.RecordProductionAsync(
            owner, product, good: 150, entryDate: FactoryScenario.Today().AddDays(-3));
        var id = entry.GetProperty("id").GetGuid();

        var response = await owner.PostAsJsonAsync($"/api/v1/production-entries/{id}/cancel",
            new { reason = "Duplicate of the entry made the same afternoon" }, ApiClientExtensions.Json);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [RequiresSqlServerFact]
    public async Task The_summary_excludes_cancelled_entries()
    {
        // A summary that counted them would report production that was withdrawn.
        var owner = await _scenario.OwnerAsync();
        var product = await _scenario.CreateProductAsync(owner, "SUMMARY");

        await _scenario.RecordProductionAsync(owner, product, good: 500, broken: 50);
        var cancelled = await _scenario.RecordProductionAsync(owner, product, good: 700, broken: 30);

        await owner.PostAsJsonAsync(
            $"/api/v1/production-entries/{cancelled.GetProperty("id").GetGuid()}/cancel",
            new { reason = "Recorded against the wrong batch entirely" }, ApiClientExtensions.Json);

        var summary = await (await owner.GetAsync(
            $"/api/v1/production-entries/summary?productId={product}")).ReadJsonAsync();

        var row = summary.EnumerateArray().Single();
        row.GetProperty("totalGood").GetInt32().Should().Be(500);
        row.GetProperty("totalFired").GetInt32().Should().Be(550);
        row.GetProperty("entryCount").GetInt32().Should().Be(1);
    }

    [RequiresSqlServerFact]
    public async Task An_administrator_cannot_record_production()
    {
        var admin = await _api.CreateApiClient().SignedInAsAsync(FactoryApiFixture.AdminUserName);

        var response = await admin.PostAsJsonAsync("/api/v1/production-entries",
            Entry(Guid.NewGuid(), 100, 0, 0), ApiClientExtensions.Json);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
