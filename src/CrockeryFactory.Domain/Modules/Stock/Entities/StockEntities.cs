using CrockeryFactory.Domain.Enums;
using CrockeryFactory.Shared.Entities;

namespace CrockeryFactory.Modules.Stock.Entities;

/// <summary>
/// Append-only ledger. Never updated, never deleted. This is the source of truth
/// for stock - Architecture section 1.3, BRD BR-02.
/// </summary>
public class StockMovement
{
    public Guid Id { get; set; }

    public Guid ProductId { get; set; }
    public QualityGrade Grade { get; set; }

    /// <summary>Signed. Positive is a receipt, negative is an issue.</summary>
    public int Quantity { get; set; }

    public StockMovementType MovementType { get; set; }

    public StockReferenceType ReferenceType { get; set; }
    public Guid ReferenceId { get; set; }

    /// <summary>Required for Adjustment and CountCorrection. Null otherwise.</summary>
    public Guid? ReasonCodeId { get; set; }
    public ReasonCode? ReasonCode { get; set; }

    public string? Notes { get; set; }

    /// <summary>Business date the movement happened - may differ from CreatedAt.</summary>
    public DateOnly OccurredOn { get; set; }

    public DateTime CreatedAt { get; set; }
    public Guid CreatedByUserId { get; set; }
}

/// <summary>
/// Cached sum of movements, updated inside the same transaction as the movement.
/// A denormalisation for read performance (PF-05), never the source of truth.
/// Rebuildable from StockMovement at any time.
/// </summary>
public class StockBalance
{
    public Guid ProductId { get; set; }
    public QualityGrade Grade { get; set; }

    public int Quantity { get; set; }

    public DateTime LastMovementAt { get; set; }
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}

/// <summary>Manual correction. ST-06 requires a reason from a fixed list.</summary>
public class StockAdjustment
{
    public Guid Id { get; set; }
    public string AdjustmentNumber { get; set; } = string.Empty;

    public Guid ProductId { get; set; }
    public QualityGrade Grade { get; set; }

    /// <summary>Signed change applied to stock.</summary>
    public int QuantityChange { get; set; }

    public Guid ReasonCodeId { get; set; }
    public ReasonCode ReasonCode { get; set; } = null!;

    public string? Notes { get; set; }
    public DateOnly AdjustedOn { get; set; }

    public DocumentStatus Status { get; set; } = DocumentStatus.Active;

    public DateTime CreatedAt { get; set; }
    public Guid CreatedByUserId { get; set; }
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
