using System.Net;
using System.Net.Http.Json;
using CrockeryFactory.Application.Common;
using CrockeryFactory.IntegrationTests.Infrastructure;
using FluentAssertions;
using Xunit;

namespace CrockeryFactory.IntegrationTests.Auth;

[Collection(ApiCollection.Name)]
public class LoginTests
{
    private readonly FactoryApiFixture _api;

    public LoginTests(FactoryApiFixture api) => _api = api;

    [RequiresSqlServerFact]
    public async Task Correct_credentials_return_the_user_roles_and_permissions()
    {
        var client = _api.CreateApiClient();

        var response = await client.PostAsJsonAsync("/api/v1/auth/login",
            new { userName = FactoryApiFixture.OwnerUserName, password = FactoryApiFixture.Password },
            ApiClientExtensions.Json);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.ReadJsonAsync();
        body.GetProperty("userName").GetString().Should().Be(FactoryApiFixture.OwnerUserName);
        body.GetProperty("roles").EnumerateArray().Select(r => r.GetString()).Should().Contain("Owner");

        // The permission list is what the client hides menus by, so it must be present
        // and must include the owner-only ones.
        body.GetProperty("permissions").EnumerateArray().Select(p => p.GetString())
            .Should().Contain("CanSetPrices");
    }

    [RequiresSqlServerFact]
    public async Task The_session_cookie_is_httponly_and_not_readable_by_script()
    {
        var client = _api.CreateApiClient();

        var response = await client.PostAsJsonAsync("/api/v1/auth/login",
            new { userName = FactoryApiFixture.OwnerUserName, password = FactoryApiFixture.Password },
            ApiClientExtensions.Json);

        var setCookie = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("crockery.session"));

        setCookie.Should().Contain("httponly", "the cookie must not be readable from script (SE-05)");
        setCookie.Should().Contain("secure");
        setCookie.Should().Contain("samesite=strict", AtCase());

        static string AtCase() => "SameSite must be Strict";
    }

    [RequiresSqlServerFact]
    public async Task No_token_is_returned_in_the_body()
    {
        var client = _api.CreateApiClient();

        var response = await client.PostAsJsonAsync("/api/v1/auth/login",
            new { userName = FactoryApiFixture.OwnerUserName, password = FactoryApiFixture.Password },
            ApiClientExtensions.Json);

        var raw = await response.Content.ReadAsStringAsync();

        raw.Should().NotContainAny("token", "jwt", "bearer");
    }

    [RequiresSqlServerFact]
    public async Task An_unknown_user_and_a_wrong_password_are_indistinguishable()
    {
        var client = _api.CreateApiClient();

        var unknownUser = await client.PostAsJsonAsync("/api/v1/auth/login",
            new { userName = "no-such-person", password = "whatever-it-is-99" }, ApiClientExtensions.Json);

        var wrongPassword = await client.PostAsJsonAsync("/api/v1/auth/login",
            new { userName = FactoryApiFixture.OwnerUserName, password = "definitely-wrong-99" },
            ApiClientExtensions.Json);

        // Any difference here - status, code or wording - enumerates valid usernames.
        unknownUser.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        wrongPassword.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        (await unknownUser.ErrorCodeAsync()).Should().Be(ErrorCodes.InvalidCredentials);
        (await wrongPassword.ErrorCodeAsync()).Should().Be(ErrorCodes.InvalidCredentials);

        var unknownBody = await unknownUser.Content.ReadAsStringAsync();
        var wrongBody = await wrongPassword.Content.ReadAsStringAsync();

        ScrubTraceId(unknownBody).Should().Be(ScrubTraceId(wrongBody));

        static string ScrubTraceId(string body)
        {
            var index = body.IndexOf("\"traceId\"", StringComparison.Ordinal);
            return index < 0 ? body : body[..index];
        }
    }

    [RequiresSqlServerFact]
    public async Task A_deactivated_user_cannot_sign_in()
    {
        var client = _api.CreateApiClient();

        var response = await client.PostAsJsonAsync("/api/v1/auth/login",
            new { userName = FactoryApiFixture.InactiveUserName, password = FactoryApiFixture.Password },
            ApiClientExtensions.Json);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.ErrorCodeAsync()).Should().Be(ErrorCodes.UserInactive);
    }

    [RequiresSqlServerFact]
    public async Task A_deactivated_user_is_not_revealed_by_a_wrong_password()
    {
        var client = _api.CreateApiClient();

        // USER_INACTIVE tells the caller the account exists. It must therefore only be
        // reachable by someone who already proved they know the password.
        var response = await client.PostAsJsonAsync("/api/v1/auth/login",
            new { userName = FactoryApiFixture.InactiveUserName, password = "wrong-password-99" },
            ApiClientExtensions.Json);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await response.ErrorCodeAsync()).Should().Be(ErrorCodes.InvalidCredentials);
    }

    [RequiresSqlServerFact]
    public async Task Me_returns_the_signed_in_user()
    {
        var client = await _api.CreateApiClient().SignedInAsAsync(FactoryApiFixture.ClerkUserName);

        var body = await (await client.GetAsync("/api/v1/auth/me")).ReadJsonAsync();

        body.GetProperty("userName").GetString().Should().Be(FactoryApiFixture.ClerkUserName);
        body.GetProperty("fullName").GetString().Should().Be("Munshi");
    }

    [RequiresSqlServerFact]
    public async Task Me_requires_a_session()
    {
        var response = await _api.CreateApiClient().GetAsync("/api/v1/auth/me");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await response.ErrorCodeAsync()).Should().Be(ErrorCodes.Unauthenticated);
    }

    [RequiresSqlServerFact]
    public async Task Logout_ends_the_session()
    {
        var client = await _api.CreateApiClient().SignedInAsAsync(FactoryApiFixture.OwnerUserName);

        (await client.PostAsync("/api/v1/auth/logout", null)).StatusCode
            .Should().Be(HttpStatusCode.NoContent);

        (await client.GetAsync("/api/v1/auth/me")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
    }
}
