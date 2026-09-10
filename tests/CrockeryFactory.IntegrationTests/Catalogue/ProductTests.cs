using System.Net;
using System.Net.Http.Json;
using CrockeryFactory.Application.Common;
using CrockeryFactory.IntegrationTests.Infrastructure;
using FluentAssertions;
using Xunit;

namespace CrockeryFactory.IntegrationTests.Catalogue;

[Collection(ApiCollection.Name)]
public class ProductTests
{
    private readonly FactoryApiFixture _api;

    public ProductTests(FactoryApiFixture api) => _api = api;

    private static string UniqueCode(string tag) => $"CUP-{tag}-{Random.Shared.Next(1000, 9999)}";

    private async Task<HttpClient> OwnerAsync() =>
        await _api.CreateApiClient().SignedInAsAsync(FactoryApiFixture.OwnerUserName);

    private static object NewProduct(string code, string name = "Test cup", decimal rate = 100m) => new
    {
        code,
        name,
        capacityMl = 180,
        prices = new[] { new { grade = "First", unitRate = rate } }
    };

    [RequiresSqlServerFact]
    public async Task A_product_is_created_with_its_prices_and_returns_a_location()
    {
        var client = await OwnerAsync();
        var code = UniqueCode("NEW");

        var response = await client.PostAsJsonAsync("/api/v1/products", NewProduct(code), ApiClientExtensions.Json);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location.Should().NotBeNull();

        var body = await response.ReadJsonAsync();
        body.GetProperty("code").GetString().Should().Be(code);
        body.GetProperty("isActive").GetBoolean().Should().BeTrue();

        var prices = body.GetProperty("prices").EnumerateArray().ToList();
        prices.Should().HaveCount(1);
        prices[0].GetProperty("grade").GetString().Should().Be("First");
        prices[0].GetProperty("unitRate").GetDecimal().Should().Be(100m);
    }

    [RequiresSqlServerFact]
    public async Task A_lowercase_code_is_stored_in_capitals()
    {
        var client = await OwnerAsync();
        var code = UniqueCode("CASE");

        var response = await client.PostAsJsonAsync("/api/v1/products",
            NewProduct(code.ToLowerInvariant()), ApiClientExtensions.Json);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        (await response.ReadJsonAsync()).GetProperty("code").GetString().Should().Be(code);
    }

    [RequiresSqlServerFact]
    public async Task A_duplicate_code_is_rejected_as_a_conflict()
    {
        var client = await OwnerAsync();
        var code = UniqueCode("DUP");

        await client.PostAsJsonAsync("/api/v1/products", NewProduct(code), ApiClientExtensions.Json);
        var second = await client.PostAsJsonAsync("/api/v1/products", NewProduct(code), ApiClientExtensions.Json);

        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await second.ErrorCodeAsync()).Should().Be(ErrorCodes.DuplicateCode);
    }

    [RequiresSqlServerFact]
    public async Task A_code_with_illegal_characters_is_rejected_with_a_field_error()
    {
        var client = await OwnerAsync();

        var response = await client.PostAsJsonAsync("/api/v1/products",
            NewProduct("cup espresso!"), ApiClientExtensions.Json);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.ErrorCodeAsync()).Should().Be(ErrorCodes.ValidationFailed);

        // The clerk needs to know which box to fix, not just that something was wrong.
        var errors = (await response.ReadJsonAsync()).GetProperty("errors").EnumerateArray().ToList();
        errors.Should().Contain(e => e.GetProperty("field").GetString() == "code");
    }

    [RequiresSqlServerFact]
    public async Task The_same_grade_twice_is_rejected_as_a_duplicate_grade()
    {
        var client = await OwnerAsync();

        var response = await client.PostAsJsonAsync("/api/v1/products", new
        {
            code = UniqueCode("GRD"),
            name = "Two firsts",
            prices = new[]
            {
                new { grade = "First", unitRate = 100m },
                new { grade = "First", unitRate = 120m }
            }
        }, ApiClientExtensions.Json);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var errors = (await response.ReadJsonAsync()).GetProperty("errors").EnumerateArray().ToList();
        errors.Should().Contain(e => e.GetProperty("code").GetString() == ErrorCodes.DuplicateGrade);
    }

    [RequiresSqlServerFact]
    public async Task A_grade_the_factory_does_not_sort_to_is_rejected()
    {
        // Seeded settings enable grades 1 and 2. Third is a valid enum value and still
        // not something this factory records.
        var client = await OwnerAsync();

        var response = await client.PostAsJsonAsync("/api/v1/products", new
        {
            code = UniqueCode("THIRD"),
            name = "Third grade cup",
            prices = new[] { new { grade = "Third", unitRate = 40m } }
        }, ApiClientExtensions.Json);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.ErrorCodeAsync()).Should().Be(ErrorCodes.GradeNotEnabled);
    }

    [RequiresSqlServerFact]
    public async Task A_product_with_no_price_is_rejected()
    {
        var client = await OwnerAsync();

        var response = await client.PostAsJsonAsync("/api/v1/products", new
        {
            code = UniqueCode("NOPRICE"),
            name = "Unpriced",
            prices = Array.Empty<object>()
        }, ApiClientExtensions.Json);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [RequiresSqlServerTheory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(1_000_001)]
    public async Task A_rate_outside_the_allowed_range_is_rejected(decimal rate)
    {
        var client = await OwnerAsync();

        var response = await client.PostAsJsonAsync("/api/v1/products",
            NewProduct(UniqueCode("RATE"), rate: rate), ApiClientExtensions.Json);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [RequiresSqlServerTheory]
    [InlineData(0)]
    [InlineData(5_001)]
    public async Task A_capacity_outside_the_allowed_range_is_rejected(int capacityMl)
    {
        var client = await OwnerAsync();

        var response = await client.PostAsJsonAsync("/api/v1/products", new
        {
            code = UniqueCode("CAP"),
            name = "Odd capacity",
            capacityMl,
            prices = new[] { new { grade = "First", unitRate = 100m } }
        }, ApiClientExtensions.Json);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [RequiresSqlServerFact]
    public async Task A_listed_product_carries_its_prices_and_stock_in_the_same_response()
    {
        // PF-04: the dispatch screen needs product, price and availability together.
        var client = await OwnerAsync();
        var code = UniqueCode("LIST");

        await client.PostAsJsonAsync("/api/v1/products", NewProduct(code), ApiClientExtensions.Json);

        var body = await (await client.GetAsync($"/api/v1/products?search={code}")).ReadJsonAsync();
        var item = body.GetProperty("items").EnumerateArray().Single();

        item.GetProperty("prices").GetArrayLength().Should().Be(1);
        item.TryGetProperty("stock", out _).Should().BeTrue();
    }

    [RequiresSqlServerFact]
    public async Task An_inactive_product_is_hidden_unless_asked_for()
    {
        var client = await OwnerAsync();
        var code = UniqueCode("HIDE");

        var created = await (await client.PostAsJsonAsync("/api/v1/products",
            NewProduct(code), ApiClientExtensions.Json)).ReadJsonAsync();
        var id = created.GetProperty("id").GetGuid();

        (await client.PostAsync($"/api/v1/products/{id}/deactivate", null))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var hidden = await (await client.GetAsync($"/api/v1/products?search={code}")).ReadJsonAsync();
        hidden.GetProperty("items").GetArrayLength().Should().Be(0);

        var shown = await (await client.GetAsync(
            $"/api/v1/products?search={code}&includeInactive=true")).ReadJsonAsync();
        shown.GetProperty("items").GetArrayLength().Should().Be(1);
    }
}
