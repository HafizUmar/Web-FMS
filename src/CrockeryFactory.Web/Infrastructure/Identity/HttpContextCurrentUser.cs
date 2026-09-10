using System.Security.Claims;
using CrockeryFactory.Domain.Abstractions;

namespace CrockeryFactory.Web.Infrastructure.Identity;

/// <summary>
/// The caller, read from the authenticated principal and from nowhere else.
///
/// There is deliberately no constructor or setter that accepts a user id: reading it
/// from a request body would let a caller name someone else as the author of a stock
/// adjustment (spec section 4.3).
/// </summary>
public sealed class HttpContextCurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _accessor;

    public HttpContextCurrentUser(IHttpContextAccessor accessor) => _accessor = accessor;

    private ClaimsPrincipal? Principal => _accessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public Guid? UserId =>
        Guid.TryParse(Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    public string? UserName => Principal?.FindFirstValue(ClaimTypes.Name);

    public string? FullName => Principal?.FindFirstValue("full_name") ?? UserName;

    public IReadOnlyCollection<string> Roles =>
        Principal?.FindAll(ClaimTypes.Role).Select(c => c.Value).ToArray() ?? Array.Empty<string>();

    public bool IsInRole(string role) => Principal?.IsInRole(role) == true;

    public Guid RequireUserId() =>
        UserId ?? throw new InvalidOperationException(
            "No authenticated user. Every write records who made it - a write reaching " +
            "this point unauthenticated is a missing [Authorize], not a case to default away.");
}
