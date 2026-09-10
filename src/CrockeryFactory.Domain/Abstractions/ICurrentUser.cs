namespace CrockeryFactory.Domain.Abstractions;

/// <summary>
/// The authenticated caller. Resolved from the security principal and never from a
/// request body - a userId field in a payload is a privilege escalation waiting to be
/// found (spec section 4.3).
/// </summary>
public interface ICurrentUser
{
    /// <summary>Null when the request is anonymous (login and health only).</summary>
    Guid? UserId { get; }

    string? UserName { get; }

    string? FullName { get; }

    IReadOnlyCollection<string> Roles { get; }

    bool IsAuthenticated { get; }

    bool IsInRole(string role);

    /// <summary>The caller's id, or a throw. Use at every write - CreatedByUserId is not optional.</summary>
    Guid RequireUserId();
}
