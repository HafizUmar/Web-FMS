using CrockeryFactory.Domain.Enums;

namespace CrockeryFactory.Application.Staff.Dtos;

// ----------------------------------------------------------------- employees

public sealed record EmployeeResponse(
    Guid Id, string Code, string Name, string? FatherName, string? Cnic, string? Phone,
    string? Designation, DateOnly JoinedOn, bool IsActive, DateOnly? LeftOn,
    decimal? CurrentDailyRate, string? Notes);

public sealed record CreateEmployeeRequest(
    string Code, string Name, string? FatherName, string? Cnic, string? Phone,
    string? Designation, DateOnly JoinedOn, decimal DailyRate, string? Notes);

public sealed record UpdateEmployeeRequest(
    string Name, string? FatherName, string? Cnic, string? Phone,
    string? Designation, string? Notes);

public sealed record SetWageRateRequest(decimal DailyRate, DateOnly EffectiveFrom);

public sealed record WageRateResponse(Guid Id, decimal DailyRate, DateOnly EffectiveFrom, DateTime CreatedAt);

// ----------------------------------------------------------------- attendance

/// <summary>One row of the day's sheet, whether or not it has been marked yet.</summary>
public sealed record AttendanceLineResponse(
    Guid EmployeeId, string Code, string Name, string? Designation,
    AttendanceStatus? Status, decimal OvertimeHours, string? Notes, decimal? DailyRate);

public sealed record AttendanceSheetResponse(
    DateOnly Date, IReadOnlyList<AttendanceLineResponse> Lines,
    int PresentCount, int HalfDayCount, int AbsentCount, int UnmarkedCount);

public sealed record MarkAttendanceLine(
    Guid EmployeeId, AttendanceStatus Status, decimal OvertimeHours, string? Notes);

/// <summary>The whole sheet is saved at once - a clerk marks the crew, not one man.</summary>
public sealed record MarkAttendanceRequest(DateOnly Date, IReadOnlyList<MarkAttendanceLine> Lines);

// ----------------------------------------------------------------- payroll

public sealed record PayrollLineResponse(
    Guid EmployeeId, string Code, string Name,
    int FullDays, int HalfDays, int AbsentDays, decimal OvertimeHours,
    decimal DailyRate, decimal WageAmount, decimal OvertimeAmount, decimal NetAmount);

public sealed record PayrollPreviewResponse(
    DateOnly PeriodStart, DateOnly PeriodEnd,
    IReadOnlyList<PayrollLineResponse> Lines,
    decimal TotalAmount, int EmployeeCount,
    decimal StandardHoursPerDay, decimal OvertimeMultiplier,
    /// <summary>Set when a live run already covers this period, so the UI can refuse early.</summary>
    string? ExistingRunNumber);

public sealed record PayrollRunResponse(
    Guid Id, string RunNumber, DateOnly PeriodStart, DateOnly PeriodEnd,
    decimal TotalAmount, int EmployeeCount,
    decimal StandardHoursPerDay, decimal OvertimeMultiplier,
    DocumentStatus Status, string? CancellationReason,
    DateTime CreatedAt, IReadOnlyList<PayrollLineResponse> Lines);

public sealed record CreatePayrollRunRequest(DateOnly PeriodStart, DateOnly PeriodEnd, string? Notes);
