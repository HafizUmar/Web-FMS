using System.Net;
using System.Net.Http.Json;
using CrockeryFactory.Application.Common;
using CrockeryFactory.IntegrationTests.Infrastructure;
using FluentAssertions;
using Xunit;

namespace CrockeryFactory.IntegrationTests.CrossCutting;

[Collection(ApiCollection.Name)]
public class ConcurrencyTests
{
    private readonly FactoryApiFixture _api;

    public ConcurrencyTests(FactoryApiFixture api) => _api = api;

    private async Task<(HttpClient Client, Guid Id, string ETag)> CreateProductAsync(string tag)
    {
        var client = await _api.CreateApiClient().SignedInAsAsync(FactoryApiFixture.OwnerUserName);

        var created = await client.PostAsJsonAsync("/api/v1/products", new
        {
            code = $"CUP-{tag}-{Random.Shared.Next(1000, 9999)}",
            name = "Concurrency probe",
            prices = new[] { new { grade = "First", unitRate = 100m } }
        }, ApiClientExtensions.Json);

        var id = (await created.ReadJsonAsync()).GetProperty("id").GetGuid();

        var fetched = await client.GetAsync($"/api/v1/products/{id}");
        var etag = fetched.Headers.ETag!.ToString();

        return (client, id, etag);
    }

    [RequiresSqlServerFact]
    public async Task Reading_a_product_returns_its_version_as_an_etag()
    {
        var (_, _, etag) = await CreateProductAsync("ETAG");

        etag.Should().NotBeNullOrWhiteSpace();
        etag.Should().StartWith("\"");
    }

    [RequiresSqlServerFact]
    public async Task An_update_carrying_the_current_version_succeeds()
    {
        var (client, id, etag) = await CreateProductAsync("OK");

        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/products/{id}")
        {
            Content = JsonContent.Create(
                new { code = "CUP-OK-0001", name = "Renamed cup", capacityMl = 200 },
                options: ApiClientExtensions.Json)
        };
        request.Headers.TryAddWithoutValidation("If-Match", etag);

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.ReadJsonAsync()).GetProperty("name").GetString().Should().Be("Renamed cup");
    }

    [RequiresSqlServerFact]
    public async Task A_second_update_carrying_a_stale_version_is_rejected()
    {
        // The lost update this header exists to prevent: two people open the same
        // product, both save, and without If-Match the second silently wins.
        var (client, id, staleETag) = await CreateProductAsync("STALE");

        var first = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/products/{id}")
        {
            Content = JsonContent.Create(
                new { code = "CUP-STALE-0001", name = "First writer", capacityMl = 180 },
                options: ApiClientExtensions.Json)
        };
        first.Headers.TryAddWithoutValidation("If-Match", staleETag);
        (await client.SendAsync(first)).StatusCode.Should().Be(HttpStatusCode.OK);

        var second = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/products/{id}")
        {
            Content = JsonContent.Create(
                new { code = "CUP-STALE-0001", name = "Second writer", capacityMl = 190 },
                options: ApiClientExtensions.Json)
        };
        second.Headers.TryAddWithoutValidation("If-Match", staleETag);

        var response = await client.SendAsync(second);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.ErrorCodeAsync()).Should().Be(ErrorCodes.ConcurrencyConflict);
    }

    [RequiresSqlServerFact]
    public async Task An_update_with_no_version_at_all_is_refused()
    {
        var (client, id, _) = await CreateProductAsync("NOMATCH");

        var response = await client.PutAsJsonAsync($"/api/v1/products/{id}",
            new { code = "CUP-NOMATCH-1", name = "No If-Match", capacityMl = 180 },
            ApiClientExtensions.Json);

        response.StatusCode.Should().Be(HttpStatusCode.PreconditionRequired);
        (await response.ErrorCodeAsync()).Should().Be(ErrorCodes.IfMatchRequired);
    }

    [RequiresSqlServerFact]
    public async Task A_wildcard_version_is_refused_rather_than_treated_as_agreement()
    {
        var (client, id, _) = await CreateProductAsync("WILD");

        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/products/{id}")
        {
            Content = JsonContent.Create(
                new { code = "CUP-WILD-0001", name = "Wildcard", capacityMl = 180 },
                options: ApiClientExtensions.Json)
        };
        request.Headers.TryAddWithoutValidation("If-Match", "*");

        (await client.SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.PreconditionRequired);
    }
}
