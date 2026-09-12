using CrockeryFactory.Modules.Staff.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CrockeryFactory.Persistence.Configurations.Staff;

public sealed class EmployeeConfiguration : IEntityTypeConfiguration<Employee>
{
    public void Configure(EntityTypeBuilder<Employee> b)
    {
        b.ToTable("Employees", "staff");

        b.HasKey(e => e.Id);

        b.Property(e => e.Code).HasMaxLength(24).IsRequired();
        b.Property(e => e.Name).HasMaxLength(160).IsRequired();
        b.Property(e => e.FatherName).HasMaxLength(160);
        b.Property(e => e.Cnic).HasMaxLength(20);
        b.Property(e => e.Phone).HasMaxLength(32);
        b.Property(e => e.Designation).HasMaxLength(80);
        b.Property(e => e.Notes).HasMaxLength(500);

        b.Property(e => e.RowVersion).IsRowVersion();

        b.HasIndex(e => e.Code).IsUnique();

        // The roll is read by name far more often than by code.
        b.HasIndex(e => new { e.IsActive, e.Name });

        b.HasMany(e => e.WageRates)
            .WithOne(r => r.Employee)
            .HasForeignKey(r => r.EmployeeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class EmployeeWageRateConfiguration : IEntityTypeConfiguration<EmployeeWageRate>
{
    public void Configure(EntityTypeBuilder<EmployeeWageRate> b)
    {
        b.ToTable("EmployeeWageRates", "staff", t => t.HasCheckConstraint(
            "CK_EmployeeWageRate_Positive", "[DailyRate] > 0"));

        b.HasKey(r => r.Id);

        b.Property(r => r.DailyRate).HasPrecision(18, 2);

        // One rate per employee per effective date. A second rate starting the same day is
        // ambiguous about which one that day is paid at, so the database refuses it.
        b.HasIndex(r => new { r.EmployeeId, r.EffectiveFrom }).IsUnique();
    }
}

public sealed class AttendanceRecordConfiguration : IEntityTypeConfiguration<AttendanceRecord>
{
    public void Configure(EntityTypeBuilder<AttendanceRecord> b)
    {
        b.ToTable("AttendanceRecords", "staff", t => t.HasCheckConstraint(
            "CK_Attendance_Overtime", "[OvertimeHours] >= 0 AND [OvertimeHours] <= 16"));

        b.HasKey(a => a.Id);

        b.Property(a => a.OvertimeHours).HasPrecision(18, 2);
        b.Property(a => a.Notes).HasMaxLength(300);
        b.Property(a => a.RowVersion).IsRowVersion();

        // A day is marked once. Re-marking corrects the row rather than adding a second
        // opinion about the same day, and this is what makes that true even under a race.
        b.HasIndex(a => new { a.EmployeeId, a.AttendanceDate }).IsUnique();

        // The sheet is always read a day at a time.
        b.HasIndex(a => a.AttendanceDate);

        b.HasOne(a => a.Employee)
            .WithMany()
            .HasForeignKey(a => a.EmployeeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class PayrollRunConfiguration : IEntityTypeConfiguration<PayrollRun>
{
    public void Configure(EntityTypeBuilder<PayrollRun> b)
    {
        b.ToTable("PayrollRuns", "staff", t => t.HasCheckConstraint(
            "CK_PayrollRun_Period", "[PeriodEnd] >= [PeriodStart]"));

        b.HasKey(p => p.Id);

        b.Property(p => p.RunNumber).HasMaxLength(32).IsRequired();
        b.Property(p => p.TotalAmount).HasPrecision(18, 2);
        b.Property(p => p.StandardHoursPerDay).HasPrecision(18, 2);
        b.Property(p => p.OvertimeMultiplier).HasPrecision(18, 2);
        b.Property(p => p.Notes).HasMaxLength(500);
        b.Property(p => p.RowVersion).IsRowVersion();

        b.HasIndex(p => p.RunNumber).IsUnique();

        // Filtered so a cancelled run does not block a corrected one for the same week,
        // while two live runs for one week remain impossible.
        b.HasIndex(p => new { p.PeriodStart, p.PeriodEnd })
            .IsUnique()
            .HasFilter("[Status] = 0");

        b.HasMany(p => p.Lines)
            .WithOne(l => l.PayrollRun)
            .HasForeignKey(l => l.PayrollRunId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class PayrollLineConfiguration : IEntityTypeConfiguration<PayrollLine>
{
    public void Configure(EntityTypeBuilder<PayrollLine> b)
    {
        b.ToTable("PayrollLines", "staff");

        b.HasKey(l => l.Id);

        b.Property(l => l.EmployeeCodeSnapshot).HasMaxLength(24).IsRequired();
        b.Property(l => l.EmployeeNameSnapshot).HasMaxLength(160).IsRequired();

        b.Property(l => l.OvertimeHours).HasPrecision(18, 2);
        b.Property(l => l.DailyRateSnapshot).HasPrecision(18, 2);
        b.Property(l => l.WageAmount).HasPrecision(18, 2);
        b.Property(l => l.OvertimeAmount).HasPrecision(18, 2);
        b.Property(l => l.NetAmount).HasPrecision(18, 2);

        // One line per worker per run.
        b.HasIndex(l => new { l.PayrollRunId, l.EmployeeId }).IsUnique();

        b.HasOne(l => l.Employee)
            .WithMany()
            .HasForeignKey(l => l.EmployeeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
