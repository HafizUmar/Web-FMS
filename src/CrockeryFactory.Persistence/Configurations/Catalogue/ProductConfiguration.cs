using CrockeryFactory.Modules.Catalogue.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CrockeryFactory.Persistence.Configurations.Catalogue;

public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> b)
    {
        b.ToTable("Products", ModuleSchemas.Catalogue);
        b.HasKey(x => x.Id);

        b.Property(x => x.Code).HasMaxLength(24).IsRequired();
        b.Property(x => x.Name).HasMaxLength(160).IsRequired();
        b.Property(x => x.Description).HasMaxLength(500);
        b.Property(x => x.RowVersion).IsRowVersion();

        b.HasIndex(x => x.Code).IsUnique();

        // Covers the product picker on the dispatch screen (PF-04).
        b.HasIndex(x => new { x.IsActive, x.Name });

        b.HasMany(x => x.Prices)
         .WithOne(p => p.Product)
         .HasForeignKey(p => p.ProductId)
         .OnDelete(DeleteBehavior.Cascade);
    }
}

public class ProductPriceConfiguration : IEntityTypeConfiguration<ProductPrice>
{
    public void Configure(EntityTypeBuilder<ProductPrice> b)
    {
        b.ToTable("ProductPrices", ModuleSchemas.Catalogue);
        b.HasKey(x => x.Id);

        b.Property(x => x.Grade).HasConversion<int>();
        b.Property(x => x.UnitRate).HasPrecision(18, 2);

        // One current price per product and grade. Filtered unique index -
        // superseded rows have EffectiveTo set and are excluded.
        b.HasIndex(x => new { x.ProductId, x.Grade })
         .IsUnique()
         .HasFilter("[EffectiveTo] IS NULL");
    }
}
