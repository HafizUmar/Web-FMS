using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace CrockeryFactory.IntegrationTests.Infrastructure;

/// <summary>
/// Builds up factory state through the API rather than by writing rows.
///
/// Seeding tables directly would produce a database the application could not have
/// created, which makes it useless for testing the rules it is supposed to exercise -
/// stock that never passed BR-01, entries with no ledger behind them. Everything here
/// goes through the same endpoints a clerk uses.
/// </summary>
public sealed class FactoryScenario
{
    private readonly FactoryApiFixture _api;

    public FactoryScenario(FactoryApiFixture api) => _api = api;

    public async Task<HttpClient> OwnerAsync() =>
        await _api.CreateApiClient().SignedInAsAsync(FactoryApiFixture.OwnerUserName);

    public async Task<HttpClient> ClerkAsync() =>
        await _api.CreateApiClient().SignedInAsAsync(FactoryApiFixture.ClerkUserName);

    public async Task<Guid> CreateProductAsync(
        HttpClient owner, string tag, decimal firstRate = 100m, decimal? secondRate = 60m)
    {
        var prices = secondRate is { } second
            ? new object[]
            {
                new { grade = "First", unitRate = firstRate },
                new { grade = "Second", unitRate = second }
            }
            : [new { grade = "First", unitRate = firstRate }];

        var response = await owner.PostAsJsonAsync("/api/v1/products", new
        {
            code = $"CUP-{tag}-{Random.Shared.Next(10000, 99999)}",
            name = $"{tag} cup",
            capacityMl = 180,
            prices
        }, ApiClientExtensions.Json);

        response.IsSuccessStatusCode.Should().BeTrue(
            $"creating the test product should succeed: {await response.Content.ReadAsStringAsync()}");

        return (await response.ReadJsonAsync()).GetProperty("id").GetGuid();
    }

    /// <summary>Puts stock in the godown the only way the application allows: by firing it.</summary>
    public async Task<JsonElement> RecordProductionAsync(
        HttpClient client, Guid productId, int good, int seconds = 0, int broken = 0,
        Guid? breakageReasonId = null, DateOnly? entryDate = null)
    {
        var response = await client.PostAsJsonAsync("/api/v1/production-entries", new
        {
            productId,
            entryDate = (entryDate ?? Today()).ToString("yyyy-MM-dd"),
            quantityGood = good,
            quantitySeconds = seconds,
            quantityBroken = broken,
            breakageReasonCodeId = broken > 0 ? breakageReasonId ?? SeededBreakageReason : (Guid?)null
        }, ApiClientExtensions.Json);

        response.IsSuccessStatusCode.Should().BeTrue(
            $"recording production should succeed: {await response.Content.ReadAsStringAsync()}");

        return await response.ReadJsonAsync();
    }

    public async Task<int> StockAsync(HttpClient client, Guid productId, string grade)
    {
        var body = await (await client.GetAsync("/api/v1/stock")).ReadJsonAsync();

        var line = body.GetProperty("lines").EnumerateArray()
            .FirstOrDefault(l =>
                l.GetProperty("productId").GetGuid() == productId &&
                l.GetProperty("grade").GetString() == grade);

        return line.ValueKind == JsonValueKind.Undefined ? 0 : line.GetProperty("quantity").GetInt32();
    }

    public async Task<Guid> CreateCustomerAsync(
        HttpClient client, string tag, decimal openingBalance = 0m)
    {
        var response = await client.PostAsJsonAsync("/api/v1/customers", new
        {
            code = $"C-{tag}-{Random.Shared.Next(10000, 99999)}",
            name = $"{tag} Traders",
            city = "Gujrat",
            phone = "03001234567",
            openingBalance,
            openingBalanceAsOf = openingBalance == 0m ? null : Today().AddDays(-60).ToString("yyyy-MM-dd")
        }, ApiClientExtensions.Json);

        response.IsSuccessStatusCode.Should().BeTrue(
            $"creating the test customer should succeed: {await response.Content.ReadAsStringAsync()}");

        return (await response.ReadJsonAsync()).GetProperty("id").GetGuid();
    }

    public async Task<HttpResponseMessage> DispatchAsync(
        HttpClient client, Guid customerId, object[] lines, DateOnly? date = null) =>
        await client.PostAsJsonAsync("/api/v1/dispatches", new
        {
            customerId,
            dispatchDate = (date ?? Today()).ToString("yyyy-MM-dd"),
            lines,
            vehicleNumber = "GJT-1234"
        }, ApiClientExtensions.Json);

    public static object Line(Guid productId, string grade, int quantity, decimal? unitRate = null) =>
        new { productId, grade, quantity, unitRate };

    public async Task<decimal> BalanceAsync(HttpClient client, Guid customerId)
    {
        var body = await (await client.GetAsync("/api/v1/customers/outstanding")).ReadJsonAsync();

        var row = body.GetProperty("rows").EnumerateArray()
            .FirstOrDefault(r => r.GetProperty("customerId").GetGuid() == customerId);

        return row.ValueKind == JsonValueKind.Undefined ? 0m : row.GetProperty("outstanding").GetDecimal();
    }

    public static DateOnly Today() => DateOnly.FromDateTime(DateTime.Now);

    /// <summary>CRACK, from the reference data the migration seeds.</summary>
    public static readonly Guid SeededBreakageReason = new("a1f00000-0000-0000-0000-000000000001");

    /// <summary>COUNT - physical count correction.</summary>
    public static readonly Guid SeededAdjustmentReason = new("a2f00000-0000-0000-0000-000000000001");

    /// <summary>A breakage reason, deliberately the wrong list for an adjustment.</summary>
    public static readonly Guid WrongListReason = new("a1f00000-0000-0000-0000-000000000002");
}
