using CrockeryFactory.Modules.Production.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CrockeryFactory.Persistence.Configurations.Production;

public class ProductionEntryConfiguration : IEntityTypeConfiguration<ProductionEntry>
{
    public void Configure(EntityTypeBuilder<ProductionEntry> b)
    {
        b.ToTable(
            "ProductionEntries",
            ModuleSchemas.Production,
            t => t.HasCheckConstraint(
                "CK_ProductionEntry_NonNegative",
                "[QuantityGood] >= 0 AND [QuantitySeconds] >= 0 AND [QuantityBroken] >= 0"));

        b.HasKey(x => x.Id);

        b.Property(x => x.EntryNumber).HasMaxLength(24).IsRequired();
        b.Property(x => x.BatchReference).HasMaxLength(48);
        b.Property(x => x.Notes).HasMaxLength(500);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
        b.Property(x => x.CancellationReason).HasMaxLength(300);
        b.Property(x => x.RowVersion).IsRowVersion();

        b.HasIndex(x => x.EntryNumber).IsUnique();
        b.HasIndex(x => new { x.EntryDate, x.ProductId });   // PR-06 summary

        b.Ignore(x => x.TotalFired);
        b.Ignore(x => x.LossPercentage);

        b.HasOne(x => x.BreakageReasonCode)
         .WithMany()
         .HasForeignKey(x => x.BreakageReasonCodeId)
         .OnDelete(DeleteBehavior.Restrict);
    }
}
