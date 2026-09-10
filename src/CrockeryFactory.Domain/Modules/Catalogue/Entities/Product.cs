using CrockeryFactory.Domain.Enums;

namespace CrockeryFactory.Modules.Catalogue.Entities;

public class Product
{
    public Guid Id { get; set; }

    /// <summary>Factory's own code, e.g. "CUP-ESP-01". Unique, user-facing.</summary>
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>Brimful capacity in ml. Nullable - not every item is measured this way.</summary>
    public int? CapacityMl { get; set; }

    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; }
    public Guid CreatedByUserId { get; set; }
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public ICollection<ProductPrice> Prices { get; set; } = new List<ProductPrice>();
}

/// <summary>
/// Current selling rate for a product at a grade. Seconds sell at a different price -
/// BRD BR-04. This is a default suggestion only; the dispatch captures its own rate.
/// </summary>
public class ProductPrice
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public QualityGrade Grade { get; set; }

    public decimal UnitRate { get; set; }

    public DateOnly EffectiveFrom { get; set; }

    /// <summary>Null means current. Superseding a price sets this on the old row.</summary>
    public DateOnly? EffectiveTo { get; set; }

    public DateTime CreatedAt { get; set; }
    public Guid CreatedByUserId { get; set; }
}
