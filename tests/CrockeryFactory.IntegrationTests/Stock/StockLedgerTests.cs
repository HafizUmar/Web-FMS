using System.Net;
using System.Net.Http.Json;
using CrockeryFactory.Application.Common;
using CrockeryFactory.IntegrationTests.Infrastructure;
using FluentAssertions;
using Xunit;

namespace CrockeryFactory.IntegrationTests.Stock;

/// <summary>
/// The invariants the whole system rests on: stock never goes negative, the ledger and
/// the cached balance always agree, and a rejected write leaves nothing behind.
/// </summary>
[Collection(ApiCollection.Name)]
public class StockLedgerTests
{
    private readonly FactoryApiFixture _api;
    private readonly FactoryScenario _scenario;

    public StockLedgerTests(FactoryApiFixture api)
    {
        _api = api;
        _scenario = new FactoryScenario(api);
    }

    [RequiresSqlServerFact]
    public async Task Production_puts_good_pieces_at_first_and_seconds_at_second()
    {
        var owner = await _scenario.OwnerAsync();
        var product = await _scenario.CreateProductAsync(owner, "SPLIT");

        await _scenario.RecordProductionAsync(owner, product, good: 800, seconds: 120, broken: 80);

        (await _scenario.StockAsync(owner, product, "First")).Should().Be(800);
        (await _scenario.StockAsync(owner, product, "Second")).Should().Be(120);
    }

    [RequiresSqlServerFact]
    public async Task Broken_pieces_never_enter_stock()
    {
        // BR-04. Seconds are sellable at a lower rate; broken is a shard on the floor.
        // Counting it as stock would make every figure wrong by the breakage rate.
        var owner = await _scenario.OwnerAsync();
        var product = await _scenario.CreateProductAsync(owner, "BROKEN");

        await _scenario.RecordProductionAsync(owner, product, good: 500, seconds: 0, broken: 300);

        var total = await _scenario.StockAsync(owner, product, "First")
                  + await _scenario.StockAsync(owner, product, "Second");

        total.Should().Be(500, "the 300 broken pieces must not appear anywhere in stock");
    }

    [RequiresSqlServerFact]
    public async Task An_adjustment_that_would_drive_stock_negative_is_refused()
    {
        var owner = await _scenario.OwnerAsync();
        var product = await _scenario.CreateProductAsync(owner, "NEG");

        await _scenario.RecordProductionAsync(owner, product, good: 100);

        var response = await owner.PostAsJsonAsync("/api/v1/stock/adjustments", new
        {
            productId = product,
            grade = "First",
            quantityChange = -150,
            reasonCodeId = FactoryScenario.SeededAdjustmentReason,
            adjustedOn = FactoryScenario.Today().ToString("yyyy-MM-dd"),
            notes = "Attempting to remove more than exists"
        }, ApiClientExtensions.Json);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.ErrorCodeAsync()).Should().Be(ErrorCodes.StockInsufficient);

        // And the balance is untouched - a refused write leaves nothing behind.
        (await _scenario.StockAsync(owner, product, "First")).Should().Be(100);
    }

    [RequiresSqlServerFact]
    public async Task The_insufficient_stock_message_names_the_product_and_grade()
    {
        // The clerk cannot act on a Guid. He needs to know which line to change.
        var owner = await _scenario.OwnerAsync();
        var product = await _scenario.CreateProductAsync(owner, "NAMED");

        await _scenario.RecordProductionAsync(owner, product, good: 40);

        var listed = await (await owner.GetAsync($"/api/v1/products/{product}")).ReadJsonAsync();
        var code = listed.GetProperty("code").GetString()!;

        var response = await owner.PostAsJsonAsync("/api/v1/stock/adjustments", new
        {
            productId = product,
            grade = "First",
            quantityChange = -900,
            reasonCodeId = FactoryScenario.SeededAdjustmentReason,
            adjustedOn = FactoryScenario.Today().ToString("yyyy-MM-dd"),
            notes = "More than is there"
        }, ApiClientExtensions.Json);

        var detail = (await response.ReadJsonAsync()).GetProperty("detail").GetString()!;

        detail.Should().Contain(code);
        detail.Should().Contain("First");
        detail.Should().Contain("40");
        detail.Should().NotContain(product.ToString(), "a product id is no use to a clerk");
    }

    [RequiresSqlServerFact]
    public async Task An_adjustment_writes_a_ledger_row_and_moves_the_balance_together()
    {
        var owner = await _scenario.OwnerAsync();
        var product = await _scenario.CreateProductAsync(owner, "ADJ");

        await _scenario.RecordProductionAsync(owner, product, good: 200);

        var response = await owner.PostAsJsonAsync("/api/v1/stock/adjustments", new
        {
            productId = product,
            grade = "First",
            quantityChange = -30,
            reasonCodeId = FactoryScenario.SeededAdjustmentReason,
            adjustedOn = FactoryScenario.Today().ToString("yyyy-MM-dd"),
            notes = "Breakage found during the monthly count"
        }, ApiClientExtensions.Json);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await response.ReadJsonAsync();
        body.GetProperty("resultingBalance").GetInt32().Should().Be(170);
        body.GetProperty("adjustmentNumber").GetString().Should().StartWith("A-");

        (await _scenario.StockAsync(owner, product, "First")).Should().Be(170);
    }

    [RequiresSqlServerFact]
    public async Task The_cached_balance_always_equals_the_sum_of_the_ledger()
    {
        // StockBalances is a cache. The moment it disagrees with the ledger, every
        // report built on it is wrong and nobody can tell which number to believe.
        var owner = await _scenario.OwnerAsync();
        var product = await _scenario.CreateProductAsync(owner, "SUM");

        await _scenario.RecordProductionAsync(owner, product, good: 500, seconds: 60, broken: 40);
        await _scenario.RecordProductionAsync(owner, product, good: 300, seconds: 20);

        await owner.PostAsJsonAsync("/api/v1/stock/adjustments", new
        {
            productId = product,
            grade = "First",
            quantityChange = -25,
            reasonCodeId = FactoryScenario.SeededAdjustmentReason,
            adjustedOn = FactoryScenario.Today().ToString("yyyy-MM-dd"),
            notes = "Count correction"
        }, ApiClientExtensions.Json);

        var cached = await _scenario.StockAsync(owner, product, "First");

        var movements = await (await owner.GetAsync(
            $"/api/v1/stock/{product}/movements?grade=First&pageSize=200")).ReadJsonAsync();

        var ledgerSum = movements.GetProperty("items").EnumerateArray()
            .Sum(m => m.GetProperty("quantity").GetInt32());

        cached.Should().Be(775);
        ledgerSum.Should().Be(cached, "the cache must never disagree with the ledger it caches");
    }

    [RequiresSqlServerFact]
    public async Task The_running_balance_lets_you_find_the_day_stock_went_wrong()
    {
        var owner = await _scenario.OwnerAsync();
        var product = await _scenario.CreateProductAsync(owner, "RUN");

        await _scenario.RecordProductionAsync(owner, product, good: 100);
        await _scenario.RecordProductionAsync(owner, product, good: 250);

        var body = await (await owner.GetAsync(
            $"/api/v1/stock/{product}/movements?grade=First")).ReadJsonAsync();

        var items = body.GetProperty("items").EnumerateArray().ToList();

        // Newest first: the top row carries the closing balance.
        items[0].GetProperty("runningBalance").GetInt32().Should().Be(350);
        items[1].GetProperty("runningBalance").GetInt32().Should().Be(100);
    }

    [RequiresSqlServerFact]
    public async Task A_movement_row_names_the_document_that_caused_it()
    {
        var owner = await _scenario.OwnerAsync();
        var product = await _scenario.CreateProductAsync(owner, "TRACE");

        var entry = await _scenario.RecordProductionAsync(owner, product, good: 90);
        var entryNumber = entry.GetProperty("entryNumber").GetString();

        var body = await (await owner.GetAsync(
            $"/api/v1/stock/{product}/movements?grade=First")).ReadJsonAsync();

        var movement = body.GetProperty("items").EnumerateArray().First();

        movement.GetProperty("movementType").GetString().Should().Be("ProductionReceipt");
        movement.GetProperty("referenceNumber").GetString().Should().Be(entryNumber);
        movement.GetProperty("enteredBy").GetString().Should().Be("Owner Sahib");
    }

    [RequiresSqlServerFact]
    public async Task A_reason_from_the_wrong_list_is_rejected()
    {
        // A breakage reason on a stock adjustment passes a plain existence check and
        // then reads as nonsense on the report that groups adjustments by reason.
        var owner = await _scenario.OwnerAsync();
        var product = await _scenario.CreateProductAsync(owner, "WRONGREASON");

        await _scenario.RecordProductionAsync(owner, product, good: 100);

        var response = await owner.PostAsJsonAsync("/api/v1/stock/adjustments", new
        {
            productId = product,
            grade = "First",
            quantityChange = -10,
            reasonCodeId = FactoryScenario.WrongListReason,
            adjustedOn = FactoryScenario.Today().ToString("yyyy-MM-dd")
        }, ApiClientExtensions.Json);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.ErrorCodeAsync()).Should().Be(ErrorCodes.InvalidReasonCode);
    }

    [RequiresSqlServerFact]
    public async Task A_large_adjustment_needs_a_note()
    {
        var owner = await _scenario.OwnerAsync();
        var product = await _scenario.CreateProductAsync(owner, "BIGADJ");

        await _scenario.RecordProductionAsync(owner, product, good: 2000);

        var response = await owner.PostAsJsonAsync("/api/v1/stock/adjustments", new
        {
            productId = product,
            grade = "First",
            quantityChange = -600,
            reasonCodeId = FactoryScenario.SeededAdjustmentReason,
            adjustedOn = FactoryScenario.Today().ToString("yyyy-MM-dd")
        }, ApiClientExtensions.Json);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.ErrorCodeAsync()).Should().Be(ErrorCodes.NotesRequired);
    }

    [RequiresSqlServerFact]
    public async Task A_zero_adjustment_is_refused()
    {
        var owner = await _scenario.OwnerAsync();
        var product = await _scenario.CreateProductAsync(owner, "ZERO");

        var response = await owner.PostAsJsonAsync("/api/v1/stock/adjustments", new
        {
            productId = product,
            grade = "First",
            quantityChange = 0,
            reasonCodeId = FactoryScenario.SeededAdjustmentReason,
            adjustedOn = FactoryScenario.Today().ToString("yyyy-MM-dd")
        }, ApiClientExtensions.Json);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.ErrorCodeAsync()).Should().Be(ErrorCodes.QuantityZero);
    }

    [RequiresSqlServerFact]
    public async Task An_administrator_cannot_adjust_stock()
    {
        // Spec section 4.2. The account that can create users must not also be able to
        // move stock, or the separation between the two roles means nothing.
        var admin = await _api.CreateApiClient().SignedInAsAsync(FactoryApiFixture.AdminUserName);

        var response = await admin.PostAsJsonAsync("/api/v1/stock/adjustments", new
        {
            productId = Guid.NewGuid(),
            grade = "First",
            quantityChange = -10,
            reasonCodeId = FactoryScenario.SeededAdjustmentReason,
            adjustedOn = FactoryScenario.Today().ToString("yyyy-MM-dd")
        }, ApiClientExtensions.Json);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
