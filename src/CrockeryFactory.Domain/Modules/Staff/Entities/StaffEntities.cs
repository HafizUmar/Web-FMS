using CrockeryFactory.Domain.Enums;

namespace CrockeryFactory.Modules.Staff.Entities;

/// <summary>
/// Somebody who works at the factory for a daily wage.
///
/// Deliberately separate from AppUser. A labourer is paid but never signs in, and a clerk
/// signs in but is not on the daily-wage roll; folding the two together would put a
/// password on a mason and a wage rate on a login.
/// </summary>
public class Employee
{
    public Guid Id { get; set; }

    /// <summary>Short human key, e.g. E-001. Unique, and printed on the attendance sheet.</summary>
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>How a worker is actually identified on a Pakistani roll, alongside the name.</summary>
    public string? FatherName { get; set; }

    /// <summary>National identity number. Stored as typed, digits and dashes.</summary>
    public string? Cnic { get; set; }

    public string? Phone { get; set; }

    /// <summary>Free text - moulder, kiln hand, packer. Not an enum: every factory names these differently.</summary>
    public string? Designation { get; set; }

    public DateOnly JoinedOn { get; set; }

    /// <summary>False once they leave. Never deleted - their attendance and payroll stay readable.</summary>
    public bool IsActive { get; set; } = true;
    public DateOnly? LeftOn { get; set; }

    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; }
    public Guid CreatedByUserId { get; set; }
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public ICollection<EmployeeWageRate> WageRates { get; set; } = new List<EmployeeWageRate>();
}

/// <summary>
/// What one day's work is worth, from a given date.
///
/// Effective-dated rather than a single column on Employee, for the same reason
/// ProductPrice is (BR-11): a rise agreed in March must not silently reprice the payroll
/// already run in February. The rate that applied on a given day is the one with the
/// latest EffectiveFrom on or before it.
/// </summary>
public class EmployeeWageRate
{
    public Guid Id { get; set; }

    public Guid EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;

    /// <summary>Wage for one full day's work.</summary>
    public decimal DailyRate { get; set; }

    public DateOnly EffectiveFrom { get; set; }

    public DateTime CreatedAt { get; set; }
    public Guid CreatedByUserId { get; set; }
}

/// <summary>
/// One worker, one day.
///
/// Unique on (EmployeeId, AttendanceDate): a day is marked once, and marking it again
/// corrects the existing row rather than adding a second opinion about the same day.
/// </summary>
public class AttendanceRecord
{
    public Guid Id { get; set; }

    public Guid EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;

    public DateOnly AttendanceDate { get; set; }

    public AttendanceStatus Status { get; set; }

    /// <summary>
    /// Hours worked beyond the standard day. Paid whatever the status is - a man who
    /// worked a half day and then three hours more has both.
    /// </summary>
    public decimal OvertimeHours { get; set; }

    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedByUserId { get; set; }
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}

/// <summary>
/// A week's wages, worked out and then frozen.
///
/// The lines are a snapshot, not a view: once a run exists, editing attendance or raising
/// a wage rate must not change what a worker was told they were owed last Friday. Getting
/// a different answer requires cancelling the run and making a new one, which leaves both
/// in the record.
/// </summary>
public class PayrollRun
{
    public Guid Id { get; set; }

    public string RunNumber { get; set; } = string.Empty;

    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }

    public decimal TotalAmount { get; set; }
    public int EmployeeCount { get; set; }

    /// <summary>Snapshotted from settings, so the arithmetic on an old run stays explainable.</summary>
    public decimal StandardHoursPerDay { get; set; }
    public decimal OvertimeMultiplier { get; set; }

    public string? Notes { get; set; }

    public DocumentStatus Status { get; set; } = DocumentStatus.Active;
    public DateTime? CancelledAt { get; set; }
    public Guid? CancelledByUserId { get; set; }
    public string? CancellationReason { get; set; }

    public DateTime CreatedAt { get; set; }
    public Guid CreatedByUserId { get; set; }
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public ICollection<PayrollLine> Lines { get; set; } = new List<PayrollLine>();
}

/// <summary>One worker's pay for one run. Every figure needed to re-read it is on the row.</summary>
public class PayrollLine
{
    public Guid Id { get; set; }

    public Guid PayrollRunId { get; set; }
    public PayrollRun PayrollRun { get; set; } = null!;

    public Guid EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;

    /// <summary>Snapshots - BR-06. A renamed or re-coded worker must not rewrite an old payslip.</summary>
    public string EmployeeCodeSnapshot { get; set; } = string.Empty;
    public string EmployeeNameSnapshot { get; set; } = string.Empty;

    public int FullDays { get; set; }
    public int HalfDays { get; set; }
    public int AbsentDays { get; set; }
    public decimal OvertimeHours { get; set; }

    /// <summary>
    /// The rate actually used. Held per line rather than read back from EmployeeWageRate,
    /// so a later rate change cannot reprice a payslip already handed over.
    /// </summary>
    public decimal DailyRateSnapshot { get; set; }

    public decimal WageAmount { get; set; }
    public decimal OvertimeAmount { get; set; }
    public decimal NetAmount { get; set; }
}
