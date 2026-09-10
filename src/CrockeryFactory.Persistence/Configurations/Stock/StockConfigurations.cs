using CrockeryFactory.Modules.Stock.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CrockeryFactory.Persistence.Configurations.Stock;

public class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    public void Configure(EntityTypeBuilder<StockMovement> b)
    {
        b.ToTable("StockMovements", ModuleSchemas.Stock);
        b.HasKey(x => x.Id);

        b.Property(x => x.Grade).HasConversion<int>();
        b.Property(x => x.MovementType).HasConversion<string>().HasMaxLength(32).IsRequired();
        b.Property(x => x.ReferenceType).HasConversion<string>().HasMaxLength(32).IsRequired();
        b.Property(x => x.Notes).HasMaxLength(500);

        // ST-05: movement history for a product and grade, newest first.
        b.HasIndex(x => new { x.ProductId, x.Grade, x.OccurredOn });

        // Tracing a document back to the rows it created (cancellation, PR-07).
        b.HasIndex(x => new { x.ReferenceType, x.ReferenceId });

        // Daily stock sheet (RP-01) and the PF-14 five-year test.
        b.HasIndex(x => x.OccurredOn);

        b.HasOne(x => x.ReasonCode)
         .WithMany()
         .HasForeignKey(x => x.ReasonCodeId)
         .OnDelete(DeleteBehavior.Restrict);

        // ReferenceId is polymorphic - it points at a ProductionEntry, Dispatch,
        // StockAdjustment or StockCount depending on ReferenceType. There is no foreign
        // key because there cannot be one to four different tables; integrity there is
        // the responsibility of StockService, which is the only writer to this table.

        // No RowVersion: this table is append-only and never updated.
    }
}

public class StockBalanceConfiguration : IEntityTypeConfiguration<StockBalance>
{
    public void Configure(EntityTypeBuilder<StockBalance> b)
    {
        b.ToTable(
            "StockBalances",
            ModuleSchemas.Stock,
            // BR-01 at the database level. The application checks first and returns a
            // clean error; this catches anything that gets past it.
            t => t.HasCheckConstraint("CK_StockBalance_NonNegative", "[Quantity] >= 0"));

        b.HasKey(x => new { x.ProductId, x.Grade });
        b.Property(x => x.Grade).HasConversion<int>();
        b.Property(x => x.RowVersion).IsRowVersion();
    }
}

public class StockAdjustmentConfiguration : IEntityTypeConfiguration<StockAdjustment>
{
    public void Configure(EntityTypeBuilder<StockAdjustment> b)
    {
        b.ToTable("StockAdjustments", ModuleSchemas.Stock);
        b.HasKey(x => x.Id);

        b.Property(x => x.AdjustmentNumber).HasMaxLength(24).IsRequired();
        b.Property(x => x.Grade).HasConversion<int>();
        b.Property(x => x.Notes).HasMaxLength(500);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
        b.Property(x => x.RowVersion).IsRowVersion();

        b.HasIndex(x => x.AdjustmentNumber).IsUnique();
        b.HasIndex(x => new { x.ProductId, x.Grade, x.AdjustedOn });

        b.HasOne(x => x.ReasonCode)
         .WithMany()
         .HasForeignKey(x => x.ReasonCodeId)
         .OnDelete(DeleteBehavior.Restrict);
    }
}
