namespace CrockeryFactory.Web.Auth;

public sealed record LoginRequest(string UserName, string Password);

public sealed record LoginResponse(
    Guid UserId, string UserName, string FullName,
    IReadOnlyList<string> Roles, IReadOnlyList<string> Permissions,
    DateTime SessionExpiresAt);

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public sealed record CurrentUserResponse(
    Guid UserId, string UserName, string FullName,
    IReadOnlyList<string> Roles, IReadOnlyList<string> Permissions,
    DateTime SessionExpiresAt);
