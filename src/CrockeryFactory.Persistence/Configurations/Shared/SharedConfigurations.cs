using CrockeryFactory.Shared.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CrockeryFactory.Persistence.Configurations.Shared;

public class ReasonCodeConfiguration : IEntityTypeConfiguration<ReasonCode>
{
    public void Configure(EntityTypeBuilder<ReasonCode> b)
    {
        b.ToTable("ReasonCodes", ModuleSchemas.Shared);
        b.HasKey(x => x.Id);

        b.Property(x => x.Type).HasConversion<string>().HasMaxLength(32).IsRequired();
        b.Property(x => x.Code).HasMaxLength(24).IsRequired();
        b.Property(x => x.Description).HasMaxLength(160).IsRequired();

        // A code is unique within its list, not across all of them - "OTHER" is a
        // legitimate entry in every one.
        b.HasIndex(x => new { x.Type, x.Code }).IsUnique();
        b.HasIndex(x => new { x.Type, x.IsActive, x.SortOrder });

        b.HasData(SeedData.ReasonCodes());
    }
}

public class FactorySettingConfiguration : IEntityTypeConfiguration<FactorySetting>
{
    public void Configure(EntityTypeBuilder<FactorySetting> b)
    {
        b.ToTable("FactorySettings", ModuleSchemas.Shared);
        b.HasKey(x => x.Key);

        b.Property(x => x.Key).HasMaxLength(64);
        b.Property(x => x.Value).HasMaxLength(512).IsRequired();
        b.Property(x => x.Description).HasMaxLength(300);

        b.HasData(SeedData.FactorySettings());
    }
}

public class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> b)
    {
        b.ToTable("AuditEntries", ModuleSchemas.Shared);
        b.HasKey(x => x.Id);

        b.Property(x => x.EntityName).HasMaxLength(64).IsRequired();
        b.Property(x => x.Action).HasMaxLength(32).IsRequired();
        b.Property(x => x.UserNameSnapshot).HasMaxLength(160).IsRequired();
        b.Property(x => x.OldValues).HasColumnType("nvarchar(max)");
        b.Property(x => x.NewValues).HasColumnType("nvarchar(max)");

        b.HasIndex(x => new { x.EntityName, x.EntityId });
        b.HasIndex(x => x.OccurredAt);
    }
}

public class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> b)
    {
        b.ToTable("IdempotencyRecords", ModuleSchemas.Shared);
        b.HasKey(x => x.Key);

        b.Property(x => x.Key).HasMaxLength(64);
        b.Property(x => x.Endpoint).HasMaxLength(128).IsRequired();
        b.Property(x => x.ResponseBody).HasColumnType("nvarchar(max)").IsRequired();
        b.Property(x => x.Location).HasMaxLength(512);

        b.HasIndex(x => x.CreatedAt);   // for the 7-day cleanup job
    }
}
