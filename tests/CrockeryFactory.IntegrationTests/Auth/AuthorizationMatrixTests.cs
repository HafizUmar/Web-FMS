using System.Net;
using System.Net.Http.Json;
using CrockeryFactory.IntegrationTests.Infrastructure;
using FluentAssertions;
using Xunit;

namespace CrockeryFactory.IntegrationTests.Auth;

/// <summary>
/// One case per row of spec section 4.2 that this stage has endpoints for.
///
/// These are written as tests rather than trusted to the attributes because SE-10 says
/// authorisation is enforced at the endpoint and again in the service: hiding a button
/// in the UI is not access control, and the only way to know a policy is actually
/// attached to a route is to call the route without the role.
/// </summary>
[Collection(ApiCollection.Name)]
public class AuthorizationMatrixTests
{
    private readonly FactoryApiFixture _api;

    public AuthorizationMatrixTests(FactoryApiFixture api) => _api = api;

    [RequiresSqlServerTheory]
    [InlineData(FactoryApiFixture.OwnerUserName, true)]
    [InlineData(FactoryApiFixture.ClerkUserName, true)]
    [InlineData(FactoryApiFixture.AdminUserName, true)]
    public async Task Everyone_signed_in_may_read_the_catalogue(string user, bool allowed)
    {
        var client = await _api.CreateApiClient().SignedInAsAsync(user);

        var response = await client.GetAsync("/api/v1/products");

        (response.StatusCode == HttpStatusCode.OK).Should().Be(allowed);
    }

    [RequiresSqlServerTheory]
    [InlineData(FactoryApiFixture.OwnerUserName, true)]
    [InlineData(FactoryApiFixture.ClerkUserName, false)]
    [InlineData(FactoryApiFixture.AdminUserName, false)]
    public async Task Only_the_owner_may_create_a_product(string user, bool allowed)
    {
        // BR-07. A clerk who could add products could also price them.
        var client = await _api.CreateApiClient().SignedInAsAsync(user);

        var response = await client.PostAsJsonAsync("/api/v1/products", new
        {
            code = $"CUP-AUTH-{Random.Shared.Next(1000, 9999)}",
            name = "Authorisation probe",
            prices = new[] { new { grade = "First", unitRate = 100m } }
        }, ApiClientExtensions.Json);

        if (allowed)
            response.StatusCode.Should().Be(HttpStatusCode.Created);
        else
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [RequiresSqlServerTheory]
    [InlineData(FactoryApiFixture.OwnerUserName, true)]
    [InlineData(FactoryApiFixture.ClerkUserName, false)]
    [InlineData(FactoryApiFixture.AdminUserName, false)]
    public async Task Only_the_owner_may_set_prices(string user, bool allowed)
    {
        var client = await _api.CreateApiClient().SignedInAsAsync(user);

        var response = await client.PutAsJsonAsync(
            $"/api/v1/products/{Guid.NewGuid()}/prices",
            new { prices = new[] { new { grade = "First", unitRate = 120m } }, effectiveFrom = "2026-09-01" },
            ApiClientExtensions.Json);

        if (allowed)
        {
            // The product does not exist, so the owner gets past authorisation and is
            // stopped by the lookup - which is the distinction being asserted.
            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
        else
        {
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
    }

    [RequiresSqlServerFact]
    public async Task An_anonymous_caller_reaches_nothing_but_login()
    {
        var client = _api.CreateApiClient();

        foreach (var path in new[] { "/api/v1/products", "/api/v1/auth/me" })
        {
            var response = await client.GetAsync(path);
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, $"{path} must require a session");
        }
    }
}
