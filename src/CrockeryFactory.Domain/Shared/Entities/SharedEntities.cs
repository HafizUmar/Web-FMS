using CrockeryFactory.Domain.Enums;

namespace CrockeryFactory.Shared.Entities;

/// <summary>
/// One lookup table for all reason lists, discriminated by Type. Configurable per
/// factory - Architecture section 3.3, configuration over forking.
/// </summary>
public class ReasonCode
{
    public Guid Id { get; set; }
    public ReasonCodeType Type { get; set; }

    public string Code { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>Per-factory settings. Key/value so a new setting needs no migration.</summary>
public class FactorySetting
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime UpdatedAt { get; set; }
    public Guid UpdatedByUserId { get; set; }
}

/// <summary>SE-13. Scoped to stock adjustments, price changes, cancellations, user changes.</summary>
public class AuditEntry
{
    public Guid Id { get; set; }
    public string EntityName { get; set; } = string.Empty;
    public Guid EntityId { get; set; }
    public string Action { get; set; } = string.Empty;

    /// <summary>JSON. Null for creates.</summary>
    public string? OldValues { get; set; }
    public string? NewValues { get; set; }

    public Guid UserId { get; set; }
    public string UserNameSnapshot { get; set; } = string.Empty;
    public DateTime OccurredAt { get; set; }
}

/// <summary>Guards against double submission on a flaky tablet connection.</summary>
public class IdempotencyRecord
{
    public string Key { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
    public Guid CreatedResourceId { get; set; }
    public DateTime CreatedAt { get; set; }
}
