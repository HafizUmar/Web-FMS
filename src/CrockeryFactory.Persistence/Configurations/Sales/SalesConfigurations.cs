using CrockeryFactory.Modules.Sales.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CrockeryFactory.Persistence.Configurations.Sales;

public class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> b)
    {
        b.ToTable("Customers", ModuleSchemas.Sales);
        b.HasKey(x => x.Id);

        b.Property(x => x.Code).HasMaxLength(24).IsRequired();
        b.Property(x => x.Name).HasMaxLength(160).IsRequired();
        b.Property(x => x.City).HasMaxLength(80);
        b.Property(x => x.Phone).HasMaxLength(24);
        b.Property(x => x.Address).HasMaxLength(300);
        b.Property(x => x.Notes).HasMaxLength(500);
        b.Property(x => x.OpeningBalance).HasPrecision(18, 2);
        b.Property(x => x.RowVersion).IsRowVersion();

        b.HasIndex(x => x.Code).IsUnique();
        b.HasIndex(x => new { x.IsActive, x.Name });
    }
}

public class DispatchConfiguration : IEntityTypeConfiguration<Dispatch>
{
    public void Configure(EntityTypeBuilder<Dispatch> b)
    {
        b.ToTable("Dispatches", ModuleSchemas.Sales);
        b.HasKey(x => x.Id);

        b.Property(x => x.DispatchNumber).HasMaxLength(24).IsRequired();
        b.Property(x => x.CustomerNameSnapshot).HasMaxLength(160).IsRequired();
        b.Property(x => x.TotalAmount).HasPrecision(18, 2);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
        b.Property(x => x.VehicleNumber).HasMaxLength(24);
        b.Property(x => x.Notes).HasMaxLength(500);
        b.Property(x => x.CancellationReason).HasMaxLength(300);
        b.Property(x => x.RowVersion).IsRowVersion();

        b.HasIndex(x => x.DispatchNumber).IsUnique();
        b.HasIndex(x => new { x.CustomerId, x.DispatchDate });  // SL-07 statement
        b.HasIndex(x => x.DispatchDate);                        // RP-06 sales summary

        b.HasOne(x => x.Customer)
         .WithMany()
         .HasForeignKey(x => x.CustomerId)
         .OnDelete(DeleteBehavior.Restrict);

        b.HasMany(x => x.Lines)
         .WithOne(l => l.Dispatch)
         .HasForeignKey(l => l.DispatchId)
         .OnDelete(DeleteBehavior.Cascade);

        b.Navigation(x => x.Lines).AutoInclude(false);
    }
}

public class DispatchLineConfiguration : IEntityTypeConfiguration<DispatchLine>
{
    public void Configure(EntityTypeBuilder<DispatchLine> b)
    {
        b.ToTable(
            "DispatchLines",
            ModuleSchemas.Sales,
            t => t.HasCheckConstraint("CK_DispatchLine_PositiveQty", "[Quantity] > 0"));

        b.HasKey(x => x.Id);

        b.Property(x => x.ProductCodeSnapshot).HasMaxLength(24).IsRequired();
        b.Property(x => x.ProductNameSnapshot).HasMaxLength(160).IsRequired();
        b.Property(x => x.Grade).HasConversion<int>();
        b.Property(x => x.UnitRate).HasPrecision(18, 2);
        b.Property(x => x.LineAmount).HasPrecision(18, 2);

        b.HasIndex(x => new { x.DispatchId, x.LineNumber }).IsUnique();
    }
}

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> b)
    {
        b.ToTable(
            "Payments",
            ModuleSchemas.Sales,
            t => t.HasCheckConstraint("CK_Payment_PositiveAmount", "[Amount] > 0"));

        b.HasKey(x => x.Id);

        b.Property(x => x.PaymentNumber).HasMaxLength(24).IsRequired();
        b.Property(x => x.Amount).HasPrecision(18, 2);
        b.Property(x => x.Method).HasConversion<string>().HasMaxLength(16).IsRequired();
        b.Property(x => x.Reference).HasMaxLength(64);
        b.Property(x => x.Notes).HasMaxLength(500);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
        b.Property(x => x.CancellationReason).HasMaxLength(300);
        b.Property(x => x.RowVersion).IsRowVersion();

        b.HasIndex(x => x.PaymentNumber).IsUnique();
        b.HasIndex(x => new { x.CustomerId, x.PaymentDate });   // SL-07 statement
        b.HasIndex(x => x.PaymentDate);

        b.HasOne(x => x.Customer)
         .WithMany()
         .HasForeignKey(x => x.CustomerId)
         .OnDelete(DeleteBehavior.Restrict);
    }
}
