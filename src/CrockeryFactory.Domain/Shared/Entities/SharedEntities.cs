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

/// <summary>
/// Guards against double submission on a flaky tablet connection.
///
/// The response body and status are stored alongside the created id so that a replay
/// returns the original response rather than a pointer to it. A tablet that retries
/// because it never saw the first reply needs the reply, not a second round trip - and
/// the document number in that body is what the clerk writes on the paper slip.
/// </summary>
public class IdempotencyRecord
{
    public string Key { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
    public Guid CreatedResourceId { get; set; }

    /// <summary>The status the original request returned, replayed verbatim.</summary>
    public int StatusCode { get; set; }

    /// <summary>The original response body as JSON.</summary>
    public string ResponseBody { get; set; } = string.Empty;

    /// <summary>The Location header the original response carried, if any.</summary>
    public string? Location { get; set; }

    public DateTime CreatedAt { get; set; }
}
