using CrockeryFactory.Domain.Abstractions;
using CrockeryFactory.Shared.Constants;

namespace CrockeryFactory.Web.Infrastructure.Identity;

/// <summary>
/// Stands in for the caller where there is no HTTP request: design-time model building
/// for migrations, and the setup path that applies them. Not registered in the web host.
/// </summary>
public sealed class SystemCurrentUser : ICurrentUser
{
    public Guid? UserId => SeedConstants.SystemUserId;
    public string? UserName => "system";
    public string? FullName => "System";
    public IReadOnlyCollection<string> Roles => Array.Empty<string>();
    public bool IsAuthenticated => false;
    public bool IsInRole(string role) => false;
    public Guid RequireUserId() => SeedConstants.SystemUserId;
}
