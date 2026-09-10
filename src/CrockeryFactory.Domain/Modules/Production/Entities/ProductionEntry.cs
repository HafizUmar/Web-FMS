using CrockeryFactory.Domain.Enums;
using CrockeryFactory.Shared.Entities;

namespace CrockeryFactory.Modules.Production.Entities;

/// <summary>
/// One kiln unload. BRD section 7 - a single entry, not stage-by-stage tracking.
/// </summary>
public class ProductionEntry
{
    public Guid Id { get; set; }
    public string EntryNumber { get; set; } = string.Empty;

    public Guid ProductId { get; set; }

    public DateOnly EntryDate { get; set; }

    public int QuantityGood { get; set; }
    public int QuantitySeconds { get; set; }
    public int QuantityBroken { get; set; }

    /// <summary>Optional - PR-05.</summary>
    public Guid? BreakageReasonCodeId { get; set; }
    public ReasonCode? BreakageReasonCode { get; set; }

    /// <summary>Optional free-text lot reference for the factory's own use - PR-04.</summary>
    public string? BatchReference { get; set; }

    public string? Notes { get; set; }

    public DocumentStatus Status { get; set; } = DocumentStatus.Active;
    public DateTime? CancelledAt { get; set; }
    public Guid? CancelledByUserId { get; set; }
    public string? CancellationReason { get; set; }

    public DateTime CreatedAt { get; set; }
    public Guid CreatedByUserId { get; set; }
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    // Computed, not mapped
    public int TotalFired => QuantityGood + QuantitySeconds + QuantityBroken;

    public decimal LossPercentage => TotalFired == 0
        ? 0m
        : Math.Round((decimal)QuantityBroken / TotalFired * 100m, 2);
}
