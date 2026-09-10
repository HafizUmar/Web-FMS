using System.Security.Claims;

namespace CrockeryFactory.UnitTests.Auth;

internal static class TestPrincipal
{
    public static ClaimsPrincipal Empty() =>
        new(new ClaimsIdentity(Array.Empty<Claim>(), "test"));

    public static ClaimsPrincipal With(string type, string value) =>
        new(new ClaimsIdentity([new Claim(type, value)], "test"));
}
