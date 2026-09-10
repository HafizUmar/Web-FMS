using CrockeryFactory.Domain.Enums;

namespace CrockeryFactory.Modules.Sales.Entities;

public class Customer
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    public string? City { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }

    /// <summary>
    /// Balance carried in at go-live. Positive means the customer owes the factory.
    /// SL-01 - captured once, never edited after the first dispatch.
    /// </summary>
    public decimal OpeningBalance { get; set; }
    public DateOnly? OpeningBalanceAsOf { get; set; }

    public bool IsActive { get; set; } = true;
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; }
    public Guid CreatedByUserId { get; set; }
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}

public class Dispatch
{
    public Guid Id { get; set; }
    public string DispatchNumber { get; set; } = string.Empty;

    public Guid CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;

    /// <summary>Snapshot - BR-06. A renamed customer must not change an old bill.</summary>
    public string CustomerNameSnapshot { get; set; } = string.Empty;

    public DateOnly DispatchDate { get; set; }

    public decimal TotalAmount { get; set; }

    public string? VehicleNumber { get; set; }
    public string? Notes { get; set; }

    public DocumentStatus Status { get; set; } = DocumentStatus.Active;
    public DateTime? CancelledAt { get; set; }
    public Guid? CancelledByUserId { get; set; }
    public string? CancellationReason { get; set; }

    public DateTime CreatedAt { get; set; }
    public Guid CreatedByUserId { get; set; }
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public ICollection<DispatchLine> Lines { get; set; } = new List<DispatchLine>();
}

public class DispatchLine
{
    public Guid Id { get; set; }
    public Guid DispatchId { get; set; }
    public Dispatch Dispatch { get; set; } = null!;

    public int LineNumber { get; set; }

    public Guid ProductId { get; set; }
    public string ProductCodeSnapshot { get; set; } = string.Empty;
    public string ProductNameSnapshot { get; set; } = string.Empty;

    public QualityGrade Grade { get; set; }

    public int Quantity { get; set; }

    /// <summary>Rate at the moment of dispatch - BR-06. Never re-read from ProductPrice.</summary>
    public decimal UnitRate { get; set; }

    public decimal LineAmount { get; set; }
}

public class Payment
{
    public Guid Id { get; set; }
    public string PaymentNumber { get; set; } = string.Empty;

    public Guid CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;

    public DateOnly PaymentDate { get; set; }
    public decimal Amount { get; set; }
    public PaymentMethod Method { get; set; }

    /// <summary>Cheque number, transfer reference, etc.</summary>
    public string? Reference { get; set; }
    public string? Notes { get; set; }

    public DocumentStatus Status { get; set; } = DocumentStatus.Active;
    public DateTime? CancelledAt { get; set; }
    public Guid? CancelledByUserId { get; set; }
    public string? CancellationReason { get; set; }

    public DateTime CreatedAt { get; set; }
    public Guid CreatedByUserId { get; set; }
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
