using CrockeryFactory.Domain.Enums;

namespace CrockeryFactory.Application.Admin.Dtos;

public sealed record UserResponse(
    Guid Id, string UserName, string FullName, IReadOnlyList<string> Roles,
    bool IsActive, DateTime CreatedAt, DateTime? LastLoginAt);

public sealed record CreateUserRequest(string UserName, string FullName, string Password, string Role);

public sealed record UpdateUserRequest(string FullName, string Role);

public sealed record ResetPasswordRequest(string NewPassword);

public sealed record ReasonCodeResponse(
    Guid Id, ReasonCodeType Type, string Code, string Description, int SortOrder, bool IsActive);

public sealed record CreateReasonCodeRequest(
    ReasonCodeType Type, string Code, string Description, int SortOrder);

public sealed record UpdateReasonCodeRequest(string Description, int SortOrder);

public sealed record SettingResponse(string Key, string Value, string? Description, DateTime UpdatedAt);

public sealed record UpdateSettingsRequest(IReadOnlyDictionary<string, string> Values);

public sealed record AuditEntryResponse(
    Guid Id, string EntityName, Guid EntityId, string Action,
    string? OldValues, string? NewValues,
    Guid UserId, string UserName, DateTime OccurredAt);

public sealed record AuditQuery(
    string? EntityName = null, Guid? EntityId = null, Guid? UserId = null,
    DateOnly? From = null, DateOnly? To = null, int Page = 1, int PageSize = 50);

public sealed record RebuildResult(
    int BalancesExamined, int BalancesCorrected, int BalancesInserted, int BalancesRemoved,
    IReadOnlyList<string> Corrections, DateTime CompletedAt);

public sealed record HealthResponse(
    string Status, bool DatabaseReachable, bool MigrationsCurrent,
    IReadOnlyList<string> PendingMigrations, DateTime CheckedAt);
