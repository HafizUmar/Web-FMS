using System.Net;
using System.Net.Http.Json;
using CrockeryFactory.IntegrationTests.Infrastructure;
using FluentAssertions;
using Xunit;

namespace CrockeryFactory.IntegrationTests.CrossCutting;

/// <summary>
/// A tablet on marginal factory WiFi sends a request, loses the reply, and sends it
/// again. Without this the factory ends up with two of whatever was created.
/// </summary>
[Collection(ApiCollection.Name)]
public class IdempotencyTests
{
    private readonly FactoryApiFixture _api;

    public IdempotencyTests(FactoryApiFixture api) => _api = api;

    private static HttpRequestMessage Post(string path, object body, string? key)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body, options: ApiClientExtensions.Json)
        };

        if (key is not null)
            request.Headers.TryAddWithoutValidation("Idempotency-Key", key);

        return request;
    }

    [RequiresSqlServerFact]
    public async Task The_same_key_twice_creates_one_product_and_replays_the_first_response()
    {
        var client = await _api.CreateApiClient().SignedInAsAsync(FactoryApiFixture.OwnerUserName);
        var key = Guid.NewGuid().ToString();
        var code = $"CUP-IDEM-{Random.Shared.Next(1000, 9999)}";

        var body = new
        {
            code,
            name = "Retried product",
            prices = new[] { new { grade = "First", unitRate = 100m } }
        };

        var first = await client.SendAsync(Post("/api/v1/products", body, key));
        var second = await client.SendAsync(Post("/api/v1/products", body, key));

        first.StatusCode.Should().Be(HttpStatusCode.Created);

        // The replay returns the original response, not a fresh 409 and not a second
        // product - the clerk needs the document details back, whichever attempt won.
        second.StatusCode.Should().Be(HttpStatusCode.Created);
        second.Headers.Contains("Idempotency-Replayed").Should().BeTrue();

        var firstId = (await first.ReadJsonAsync()).GetProperty("id").GetGuid();
        var secondId = (await second.ReadJsonAsync()).GetProperty("id").GetGuid();
        secondId.Should().Be(firstId);

        var listed = await (await client.GetAsync($"/api/v1/products?search={code}")).ReadJsonAsync();
        listed.GetProperty("items").GetArrayLength().Should().Be(1, "only one product should exist");
    }

    [RequiresSqlServerFact]
    public async Task Without_a_key_a_repeat_creates_a_second_document()
    {
        // Establishes that the replay above is the header's doing and not an accident of
        // the duplicate-code rule.
        var client = await _api.CreateApiClient().SignedInAsAsync(FactoryApiFixture.OwnerUserName);

        var first = await client.SendAsync(Post("/api/v1/products", new
        {
            code = $"CUP-NOKEY-{Random.Shared.Next(1000, 9999)}",
            name = "No key",
            prices = new[] { new { grade = "First", unitRate = 100m } }
        }, key: null));

        var second = await client.SendAsync(Post("/api/v1/products", new
        {
            code = $"CUP-NOKEY-{Random.Shared.Next(1000, 9999)}",
            name = "No key",
            prices = new[] { new { grade = "First", unitRate = 100m } }
        }, key: null));

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        second.StatusCode.Should().Be(HttpStatusCode.Created);

        (await first.ReadJsonAsync()).GetProperty("id").GetGuid()
            .Should().NotBe((await second.ReadJsonAsync()).GetProperty("id").GetGuid());
    }

    [RequiresSqlServerFact]
    public async Task A_rejected_request_does_not_burn_the_key()
    {
        // The clerk fixes what was wrong and retries with the same key from the same
        // screen. Replaying the failure would leave him unable to save at all.
        var client = await _api.CreateApiClient().SignedInAsAsync(FactoryApiFixture.OwnerUserName);
        var key = Guid.NewGuid().ToString();

        var rejected = await client.SendAsync(Post("/api/v1/products", new
        {
            code = "invalid code!",
            name = "Bad",
            prices = new[] { new { grade = "First", unitRate = 100m } }
        }, key));

        rejected.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var corrected = await client.SendAsync(Post("/api/v1/products", new
        {
            code = $"CUP-FIXED-{Random.Shared.Next(1000, 9999)}",
            name = "Corrected",
            prices = new[] { new { grade = "First", unitRate = 100m } }
        }, key));

        corrected.StatusCode.Should().Be(HttpStatusCode.Created);
    }
}
