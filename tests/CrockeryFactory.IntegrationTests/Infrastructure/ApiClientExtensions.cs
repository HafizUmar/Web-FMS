using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;

namespace CrockeryFactory.IntegrationTests.Infrastructure;

public static class ApiClientExtensions
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public static async Task<HttpClient> SignedInAsAsync(this HttpClient client, string userName)
    {
        var response = await client.PostAsJsonAsync("/api/v1/auth/login",
            new { userName, password = FactoryApiFixture.Password }, Json);

        response.IsSuccessStatusCode.Should().BeTrue(
            $"login for '{userName}' should succeed but returned {(int)response.StatusCode}: " +
            await response.Content.ReadAsStringAsync());

        return client;
    }

    /// <summary>Reads the stable machine-readable code out of a ProblemDetails body.</summary>
    public static async Task<string?> ErrorCodeAsync(this HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(body))
            return null;

        using var document = JsonDocument.Parse(body);
        return document.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    public static async Task<JsonElement> ReadJsonAsync(this HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(body).RootElement.Clone();
    }
}
