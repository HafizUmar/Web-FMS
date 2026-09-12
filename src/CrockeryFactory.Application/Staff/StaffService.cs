using CrockeryFactory.Application.Abstractions;
using CrockeryFactory.Application.Common;
using CrockeryFactory.Application.Staff.Dtos;
using CrockeryFactory.Domain.Enums;
using CrockeryFactory.Modules.Staff.Entities;
using CrockeryFactory.Persistence;
using CrockeryFactory.Shared.Constants;
using Microsoft.EntityFrameworkCore;

namespace CrockeryFactory.Application.Staff;

public interface IStaffService
{
    Task<IReadOnlyList<EmployeeResponse>> ListEmployeesAsync(bool includeInactive, CancellationToken ct = default);
    Task<EmployeeResponse> GetEmployeeAsync(Guid id, CancellationToken ct = default);
    Task<EmployeeResponse> CreateEmployeeAsync(CreateEmployeeRequest request, CancellationToken ct = default);
    Task<EmployeeResponse> UpdateEmployeeAsync(Guid id, UpdateEmployeeRequest request, CancellationToken ct = default);
    Task DeactivateEmployeeAsync(Guid id, DateOnly leftOn, CancellationToken ct = default);

    Task<IReadOnlyList<WageRateResponse>> ListWageRatesAsync(Guid employeeId, CancellationToken ct = default);
    Task<WageRateResponse> SetWageRateAsync(Guid employeeId, SetWageRateRequest request, CancellationToken ct = default);

    Task<AttendanceSheetResponse> GetSheetAsync(DateOnly date, CancellationToken ct = default);
    Task<AttendanceSheetResponse> MarkAsync(MarkAttendanceRequest request, CancellationToken ct = default);

    Task<PayrollPreviewResponse> PreviewPayrollAsync(DateOnly start, DateOnly end, CancellationToken ct = default);
    Task<PayrollRunResponse> CreatePayrollAsync(CreatePayrollRunRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<PayrollRunResponse>> ListPayrollAsync(int take, CancellationToken ct = default);
    Task<PayrollRunResponse> GetPayrollAsync(Guid id, CancellationToken ct = default);
    Task<PayrollRunResponse> CancelPayrollAsync(Guid id, string reason, CancellationToken ct = default);
}

public sealed class StaffService : IStaffService
{
    private const int MinimumCancellationReasonLength = 10;

    /// <summary>A sanity ceiling. Nobody works 17 hours of overtime in a day; that is a typo.</summary>
    private const decimal MaxOvertimeHoursPerDay = 16m;

    private readonly FactoryDbContext _db;
    private readonly IFactorySettings _settings;
    private readonly IDocumentNumbers _numbers;
    private readonly IAuditWriter _audit;

    public StaffService(
        FactoryDbContext db, IFactorySettings settings, IDocumentNumbers numbers, IAuditWriter audit)
    {
        _db = db;
        _settings = settings;
        _numbers = numbers;
        _audit = audit;
    }

    private DateOnly Today => DateOnly.FromDateTime(_db.Clock.GetUtcNow().ToLocalTime().DateTime);

    // ================================================================ employees

    public async Task<IReadOnlyList<EmployeeResponse>> ListEmployeesAsync(
        bool includeInactive, CancellationToken ct = default)
    {
        var employees = await _db.Employees.AsNoTracking()
            .Where(e => includeInactive || e.IsActive)
            .OrderBy(e => e.Name)
            .ToListAsync(ct);

        var ids = employees.Select(e => e.Id).ToList();
        var today = Today;

        // One query for every current rate rather than one per employee: the roll is read
        // on every attendance screen, and a hundred round trips would be felt on a LAN.
        var rates = await _db.EmployeeWageRates.AsNoTracking()
            .Where(r => ids.Contains(r.EmployeeId) && r.EffectiveFrom <= today)
            .GroupBy(r => r.EmployeeId)
            .Select(g => new
            {
                EmployeeId = g.Key,
                DailyRate = g.OrderByDescending(r => r.EffectiveFrom).First().DailyRate
            })
            .ToDictionaryAsync(x => x.EmployeeId, x => x.DailyRate, ct);

        return employees
            .Select(e => ToResponse(e, rates.TryGetValue(e.Id, out var rate) ? rate : null))
            .ToList();
    }

    public async Task<EmployeeResponse> GetEmployeeAsync(Guid id, CancellationToken ct = default)
    {
        var employee = await _db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, ct)
            ?? throw DomainException.NotFound("Employee", id.ToString());

        return ToResponse(employee, await RateOnAsync(id, Today, ct));
    }

    public async Task<EmployeeResponse> CreateEmployeeAsync(
        CreateEmployeeRequest request, CancellationToken ct = default)
    {
        var errors = new List<FieldError>();

        var code = (request.Code ?? string.Empty).Trim().ToUpperInvariant();
        var name = (request.Name ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(code))
            errors.Add(new FieldError("code", "Required", ErrorCodes.ValidationFailed));
        if (string.IsNullOrWhiteSpace(name))
            errors.Add(new FieldError("name", "Required", ErrorCodes.ValidationFailed));
        if (request.DailyRate <= 0)
            errors.Add(new FieldError("dailyRate", "Must be more than zero", ErrorCodes.ValidationFailed));

        if (errors.Count > 0)
            throw DomainException.Validation(ErrorCodes.ValidationFailed, "The employee could not be saved.", errors.ToArray());

        if (await _db.Employees.AnyAsync(e => e.Code == code, ct))
        {
            // Conflict, not a validation failure - the same shape the catalogue uses for a
            // duplicate product code. The request is well formed; it lost a race for a name.
            throw DomainException.Conflict(ErrorCodes.DuplicateCode,
                $"Employee code {code} is already in use.");
        }

        var now = _db.Clock.GetUtcNow().UtcDateTime;
        var userId = _db.CurrentUser.RequireUserId();

        var employee = new Employee
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = name,
            FatherName = Trim(request.FatherName),
            Cnic = Trim(request.Cnic),
            Phone = Trim(request.Phone),
            Designation = Trim(request.Designation),
            JoinedOn = request.JoinedOn,
            IsActive = true,
            Notes = Trim(request.Notes),
            CreatedAt = now,
            CreatedByUserId = userId
        };

        // The opening rate runs from the joining date, not from today: a man hired last
        // week and entered today must still be payable for last week.
        employee.WageRates.Add(new EmployeeWageRate
        {
            Id = Guid.NewGuid(),
            EmployeeId = employee.Id,
            DailyRate = request.DailyRate,
            EffectiveFrom = request.JoinedOn,
            CreatedAt = now,
            CreatedByUserId = userId
        });

        _db.Employees.Add(employee);

        _audit.Record(nameof(Employee), employee.Id, "Create", null,
            new { employee.Code, employee.Name, request.DailyRate });

        await _db.SaveChangesAsync(ct);

        return ToResponse(employee, request.DailyRate);
    }

    public async Task<EmployeeResponse> UpdateEmployeeAsync(
        Guid id, UpdateEmployeeRequest request, CancellationToken ct = default)
    {
        var employee = await _db.Employees.FirstOrDefaultAsync(e => e.Id == id, ct)
            ?? throw DomainException.NotFound("Employee", id.ToString());

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw DomainException.Validation(ErrorCodes.ValidationFailed, "A name is required.",
                new FieldError("name", "Required", ErrorCodes.ValidationFailed));
        }

        var before = new { employee.Name, employee.FatherName, employee.Phone, employee.Designation };

        // The code is not editable. It is snapshotted onto every payslip already issued,
        // and changing it would leave those slips pointing at a code that no longer exists.
        employee.Name = request.Name.Trim();
        employee.FatherName = Trim(request.FatherName);
        employee.Cnic = Trim(request.Cnic);
        employee.Phone = Trim(request.Phone);
        employee.Designation = Trim(request.Designation);
        employee.Notes = Trim(request.Notes);

        _audit.Record(nameof(Employee), employee.Id, "Update", before,
            new { employee.Name, employee.FatherName, employee.Phone, employee.Designation });

        await _db.SaveChangesAsync(ct);

        return ToResponse(employee, await RateOnAsync(id, Today, ct));
    }

    public async Task DeactivateEmployeeAsync(Guid id, DateOnly leftOn, CancellationToken ct = default)
    {
        var employee = await _db.Employees.FirstOrDefaultAsync(e => e.Id == id, ct)
            ?? throw DomainException.NotFound("Employee", id.ToString());

        if (!employee.IsActive) return;

        employee.IsActive = false;
        employee.LeftOn = leftOn;

        _audit.Record(nameof(Employee), employee.Id, "Deactivate",
            new { IsActive = true }, new { IsActive = false, LeftOn = leftOn });

        await _db.SaveChangesAsync(ct);
    }

    // ================================================================ wage rates

    public async Task<IReadOnlyList<WageRateResponse>> ListWageRatesAsync(
        Guid employeeId, CancellationToken ct = default)
    {
        if (!await _db.Employees.AnyAsync(e => e.Id == employeeId, ct))
            throw DomainException.NotFound("Employee", employeeId.ToString());

        return await _db.EmployeeWageRates.AsNoTracking()
            .Where(r => r.EmployeeId == employeeId)
            .OrderByDescending(r => r.EffectiveFrom)
            .Select(r => new WageRateResponse(r.Id, r.DailyRate, r.EffectiveFrom, r.CreatedAt))
            .ToListAsync(ct);
    }

    public async Task<WageRateResponse> SetWageRateAsync(
        Guid employeeId, SetWageRateRequest request, CancellationToken ct = default)
    {
        var employee = await _db.Employees.FirstOrDefaultAsync(e => e.Id == employeeId, ct)
            ?? throw DomainException.NotFound("Employee", employeeId.ToString());

        if (request.DailyRate <= 0)
        {
            throw DomainException.Validation(ErrorCodes.ValidationFailed, "The wage must be more than zero.",
                new FieldError("dailyRate", "Must be more than zero", ErrorCodes.ValidationFailed));
        }

        var existing = await _db.EmployeeWageRates
            .FirstOrDefaultAsync(r => r.EmployeeId == employeeId && r.EffectiveFrom == request.EffectiveFrom, ct);

        var now = _db.Clock.GetUtcNow().UtcDateTime;
        var userId = _db.CurrentUser.RequireUserId();

        if (existing is not null)
        {
            // Correcting a rate entered today rather than stacking a second one on the
            // same date, which the unique index would refuse anyway.
            _audit.Record(nameof(EmployeeWageRate), existing.Id, "Update",
                new { existing.DailyRate }, new { request.DailyRate });

            existing.DailyRate = request.DailyRate;
            await _db.SaveChangesAsync(ct);

            return new WageRateResponse(existing.Id, existing.DailyRate, existing.EffectiveFrom, existing.CreatedAt);
        }

        var rate = new EmployeeWageRate
        {
            Id = Guid.NewGuid(),
            EmployeeId = employeeId,
            DailyRate = request.DailyRate,
            EffectiveFrom = request.EffectiveFrom,
            CreatedAt = now,
            CreatedByUserId = userId
        };

        _db.EmployeeWageRates.Add(rate);

        _audit.Record(nameof(EmployeeWageRate), rate.Id, "Create", null,
            new { employee.Code, request.DailyRate, request.EffectiveFrom });

        await _db.SaveChangesAsync(ct);

        return new WageRateResponse(rate.Id, rate.DailyRate, rate.EffectiveFrom, rate.CreatedAt);
    }

    // ================================================================ attendance

    public async Task<AttendanceSheetResponse> GetSheetAsync(DateOnly date, CancellationToken ct = default)
    {
        // Everyone who had joined by that date and had not yet left. Reading it from the
        // dates rather than from IsActive means yesterday's sheet still shows the man who
        // left this morning.
        var employees = await _db.Employees.AsNoTracking()
            .Where(e => e.JoinedOn <= date && (e.LeftOn == null || e.LeftOn >= date))
            .OrderBy(e => e.Name)
            .ToListAsync(ct);

        var ids = employees.Select(e => e.Id).ToList();

        var marks = await _db.AttendanceRecords.AsNoTracking()
            .Where(a => a.AttendanceDate == date && ids.Contains(a.EmployeeId))
            .ToDictionaryAsync(a => a.EmployeeId, ct);

        var rates = await RatesOnAsync(ids, date, ct);

        var lines = employees.Select(e =>
        {
            marks.TryGetValue(e.Id, out var mark);

            return new AttendanceLineResponse(
                e.Id, e.Code, e.Name, e.Designation,
                mark?.Status, mark?.OvertimeHours ?? 0m, mark?.Notes,
                rates.TryGetValue(e.Id, out var rate) ? rate : null);
        }).ToList();

        return new AttendanceSheetResponse(
            date, lines,
            lines.Count(l => l.Status == AttendanceStatus.Present),
            lines.Count(l => l.Status == AttendanceStatus.HalfDay),
            lines.Count(l => l.Status == AttendanceStatus.Absent),
            lines.Count(l => l.Status is null));
    }

    public async Task<AttendanceSheetResponse> MarkAsync(
        MarkAttendanceRequest request, CancellationToken ct = default)
    {
        var today = Today;

        if (request.Date > today)
        {
            throw DomainException.Unprocessable(ErrorCodes.ValidationFailed, "Future date",
                "Attendance cannot be marked for a day that has not happened yet.");
        }

        var window = await _settings.GetIntAsync(SettingKeys.BackdateDaysAttendance, 7, ct);

        if (request.Date < today.AddDays(-window))
        {
            throw DomainException.Unprocessable(ErrorCodes.ValidationFailed, "Too far back",
                $"Attendance can only be marked or corrected within {window} days. " +
                $"{request.Date:yyyy-MM-dd} is older than that.");
        }

        var ids = request.Lines.Select(l => l.EmployeeId).ToList();

        if (ids.Count != ids.Distinct().Count())
        {
            throw DomainException.Validation(ErrorCodes.ValidationFailed,
                "The same employee appears twice on this sheet.",
                new FieldError("lines", "Duplicate employee", ErrorCodes.ValidationFailed));
        }

        foreach (var line in request.Lines)
        {
            if (line.OvertimeHours < 0 || line.OvertimeHours > MaxOvertimeHoursPerDay)
            {
                throw DomainException.Validation(ErrorCodes.ValidationFailed,
                    $"Overtime must be between 0 and {MaxOvertimeHoursPerDay} hours.",
                    new FieldError("overtimeHours", "Out of range", ErrorCodes.ValidationFailed));
            }
        }

        var known = await _db.Employees.Where(e => ids.Contains(e.Id))
            .Select(e => e.Id).ToListAsync(ct);

        var unknown = ids.Except(known).ToList();
        if (unknown.Count > 0)
            throw DomainException.NotFound("Employee", unknown[0].ToString());

        var existing = await _db.AttendanceRecords
            .Where(a => a.AttendanceDate == request.Date && ids.Contains(a.EmployeeId))
            .ToDictionaryAsync(a => a.EmployeeId, ct);

        var now = _db.Clock.GetUtcNow().UtcDateTime;
        var userId = _db.CurrentUser.RequireUserId();
        var added = 0;
        var corrected = 0;

        foreach (var line in request.Lines)
        {
            if (existing.TryGetValue(line.EmployeeId, out var record))
            {
                if (record.Status == line.Status &&
                    record.OvertimeHours == line.OvertimeHours &&
                    record.Notes == line.Notes)
                {
                    continue;
                }

                _audit.Record(nameof(AttendanceRecord), record.Id, "Update",
                    new { record.Status, record.OvertimeHours },
                    new { line.Status, line.OvertimeHours });

                record.Status = line.Status;
                record.OvertimeHours = line.OvertimeHours;
                record.Notes = Trim(line.Notes);
                record.UpdatedAt = now;
                record.UpdatedByUserId = userId;
                corrected++;
            }
            else
            {
                _db.AttendanceRecords.Add(new AttendanceRecord
                {
                    Id = Guid.NewGuid(),
                    EmployeeId = line.EmployeeId,
                    AttendanceDate = request.Date,
                    Status = line.Status,
                    OvertimeHours = line.OvertimeHours,
                    Notes = Trim(line.Notes),
                    CreatedAt = now,
                    CreatedByUserId = userId
                });
                added++;
            }
        }

        // One audit row for the sheet rather than one per man: a crew of forty marked in a
        // morning would otherwise bury every other entry in the log.
        if (added > 0 || corrected > 0)
        {
            _audit.Record(nameof(AttendanceRecord), Guid.Empty, "MarkSheet", null,
                new { Date = request.Date, Added = added, Corrected = corrected });
        }

        await _db.SaveChangesAsync(ct);

        return await GetSheetAsync(request.Date, ct);
    }

    // ================================================================ payroll

    public async Task<PayrollPreviewResponse> PreviewPayrollAsync(
        DateOnly start, DateOnly end, CancellationToken ct = default)
    {
        var (lines, hours, multiplier) = await ComputeAsync(start, end, ct);

        var clash = await _db.PayrollRuns.AsNoTracking()
            .Where(r => r.Status == DocumentStatus.Active &&
                        r.PeriodStart == start && r.PeriodEnd == end)
            .Select(r => r.RunNumber)
            .FirstOrDefaultAsync(ct);

        return new PayrollPreviewResponse(
            start, end, lines, lines.Sum(l => l.NetAmount), lines.Count, hours, multiplier, clash);
    }

    public async Task<PayrollRunResponse> CreatePayrollAsync(
        CreatePayrollRunRequest request, CancellationToken ct = default)
    {
        if (request.PeriodEnd < request.PeriodStart)
        {
            throw DomainException.Validation(ErrorCodes.ValidationFailed,
                "The period ends before it starts.",
                new FieldError("periodEnd", "Before the start", ErrorCodes.ValidationFailed));
        }

        if (request.PeriodStart > Today)
        {
            throw DomainException.Unprocessable(ErrorCodes.ValidationFailed, "Future period",
                "A payroll cannot be created for a week that has not started.");
        }

        var existing = await _db.PayrollRuns
            .Where(r => r.Status == DocumentStatus.Active &&
                        r.PeriodStart == request.PeriodStart && r.PeriodEnd == request.PeriodEnd)
            .Select(r => r.RunNumber)
            .FirstOrDefaultAsync(ct);

        if (existing is not null)
        {
            throw DomainException.Unprocessable(ErrorCodes.ValidationFailed, "Already paid",
                $"Payroll {existing} already covers {request.PeriodStart:yyyy-MM-dd} to " +
                $"{request.PeriodEnd:yyyy-MM-dd}. Cancel it before creating another.");
        }

        var (lines, hours, multiplier) = await ComputeAsync(request.PeriodStart, request.PeriodEnd, ct);

        if (lines.Count == 0)
        {
            throw DomainException.Unprocessable(ErrorCodes.ValidationFailed, "Nothing to pay",
                "Nobody has attendance in this period, so there is nothing to pay.");
        }

        var now = _db.Clock.GetUtcNow().UtcDateTime;
        var userId = _db.CurrentUser.RequireUserId();

        var run = new PayrollRun
        {
            Id = Guid.NewGuid(),
            RunNumber = await _numbers.NextAsync(DocumentSeries.PayrollRun, request.PeriodEnd, ct),
            PeriodStart = request.PeriodStart,
            PeriodEnd = request.PeriodEnd,
            TotalAmount = lines.Sum(l => l.NetAmount),
            EmployeeCount = lines.Count,
            StandardHoursPerDay = hours,
            OvertimeMultiplier = multiplier,
            Notes = Trim(request.Notes),
            Status = DocumentStatus.Active,
            CreatedAt = now,
            CreatedByUserId = userId
        };

        foreach (var line in lines)
        {
            run.Lines.Add(new PayrollLine
            {
                Id = Guid.NewGuid(),
                PayrollRunId = run.Id,
                EmployeeId = line.EmployeeId,
                EmployeeCodeSnapshot = line.Code,
                EmployeeNameSnapshot = line.Name,
                FullDays = line.FullDays,
                HalfDays = line.HalfDays,
                AbsentDays = line.AbsentDays,
                OvertimeHours = line.OvertimeHours,
                DailyRateSnapshot = line.DailyRate,
                WageAmount = line.WageAmount,
                OvertimeAmount = line.OvertimeAmount,
                NetAmount = line.NetAmount
            });
        }

        _db.PayrollRuns.Add(run);

        _audit.Record(nameof(PayrollRun), run.Id, "Create", null,
            new { run.RunNumber, run.PeriodStart, run.PeriodEnd, run.TotalAmount, run.EmployeeCount });

        await _db.SaveChangesAsync(ct);

        return await GetPayrollAsync(run.Id, ct);
    }

    public async Task<IReadOnlyList<PayrollRunResponse>> ListPayrollAsync(
        int take, CancellationToken ct = default)
    {
        var runs = await _db.PayrollRuns.AsNoTracking()
            .Include(r => r.Lines)
            .OrderByDescending(r => r.PeriodStart)
            .Take(Math.Clamp(take, 1, 100))
            .ToListAsync(ct);

        return runs.Select(ToResponse).ToList();
    }

    public async Task<PayrollRunResponse> GetPayrollAsync(Guid id, CancellationToken ct = default)
    {
        var run = await _db.PayrollRuns.AsNoTracking()
            .Include(r => r.Lines)
            .FirstOrDefaultAsync(r => r.Id == id, ct)
            ?? throw DomainException.NotFound("PayrollRun", id.ToString());

        return ToResponse(run);
    }

    public async Task<PayrollRunResponse> CancelPayrollAsync(
        Guid id, string reason, CancellationToken ct = default)
    {
        var run = await _db.PayrollRuns.Include(r => r.Lines).FirstOrDefaultAsync(r => r.Id == id, ct)
            ?? throw DomainException.NotFound("PayrollRun", id.ToString());

        if (run.Status == DocumentStatus.Cancelled)
        {
            throw new DomainException(ErrorCodes.AlreadyCancelled, "Already cancelled", 422,
                $"Payroll {run.RunNumber} was already cancelled.");
        }

        var trimmedReason = (reason ?? string.Empty).Trim();

        if (trimmedReason.Length < MinimumCancellationReasonLength)
        {
            throw DomainException.Validation(ErrorCodes.ValidationFailed,
                $"Say why in at least {MinimumCancellationReasonLength} characters.",
                new FieldError("reason", "Too short", ErrorCodes.ValidationFailed));
        }

        // Cancelled, never deleted (BR-08). The run keeps its number and its lines, so a
        // worker who was paid from it can still be shown what they were paid and why it
        // was withdrawn.
        run.Status = DocumentStatus.Cancelled;
        run.CancelledAt = _db.Clock.GetUtcNow().UtcDateTime;
        run.CancelledByUserId = _db.CurrentUser.RequireUserId();
        run.CancellationReason = trimmedReason;

        _audit.Record(nameof(PayrollRun), run.Id, "Cancel",
            new { Status = DocumentStatus.Active },
            new { Status = DocumentStatus.Cancelled, Reason = run.CancellationReason });

        await _db.SaveChangesAsync(ct);

        return ToResponse(run);
    }

    // ================================================================ internals

    /// <summary>
    /// The whole calculation, shared by preview and create so that what a clerk was shown
    /// is by construction what gets saved.
    /// </summary>
    private async Task<(List<PayrollLineResponse> Lines, decimal Hours, decimal Multiplier)> ComputeAsync(
        DateOnly start, DateOnly end, CancellationToken ct)
    {
        var hours = await _settings.GetDecimalAsync(SettingKeys.PayrollStandardHours, 8m, ct);
        var multiplier = await _settings.GetDecimalAsync(SettingKeys.PayrollOvertimeMultiplier, 1.5m, ct);

        var records = await _db.AttendanceRecords.AsNoTracking()
            .Where(a => a.AttendanceDate >= start && a.AttendanceDate <= end)
            .Select(a => new { a.EmployeeId, a.AttendanceDate, a.Status, a.OvertimeHours })
            .ToListAsync(ct);

        if (records.Count == 0)
            return (new List<PayrollLineResponse>(), hours, multiplier);

        var ids = records.Select(r => r.EmployeeId).Distinct().ToList();

        var employees = await _db.Employees.AsNoTracking()
            .Where(e => ids.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, ct);

        // Every rate that could apply to any day in the period, fetched once.
        var rateRows = await _db.EmployeeWageRates.AsNoTracking()
            .Where(r => ids.Contains(r.EmployeeId) && r.EffectiveFrom <= end)
            .OrderBy(r => r.EffectiveFrom)
            .Select(r => new { r.EmployeeId, r.DailyRate, r.EffectiveFrom })
            .ToListAsync(ct);

        var lines = new List<PayrollLineResponse>();

        foreach (var group in records.GroupBy(r => r.EmployeeId))
        {
            if (!employees.TryGetValue(group.Key, out var employee)) continue;

            var rates = rateRows.Where(r => r.EmployeeId == group.Key).ToList();

            int fullDays = 0, halfDays = 0, absentDays = 0;
            decimal overtimeHours = 0m, wage = 0m, overtime = 0m;

            // Day by day, because the rate can change inside the week. Summing the days
            // first and multiplying once would silently pay the whole week at whichever
            // rate happened to be current.
            foreach (var day in group)
            {
                var rate = rates.LastOrDefault(r => r.EffectiveFrom <= day.AttendanceDate)?.DailyRate ?? 0m;

                var full = day.Status == AttendanceStatus.Present ? 1 : 0;
                var half = day.Status == AttendanceStatus.HalfDay ? 1 : 0;

                if (full == 1) fullDays++;
                else if (half == 1) halfDays++;
                else absentDays++;

                overtimeHours += day.OvertimeHours;

                var (dayWage, dayOvertime, _) =
                    PayrollMath.Calculate(full, half, day.OvertimeHours, rate, hours, multiplier);

                wage += dayWage;
                overtime += dayOvertime;
            }

            // Shown as the rate in force at the end of the period. Where it changed mid
            // week the amounts above are still right; this is a label, not the input.
            var displayRate = rates.LastOrDefault(r => r.EffectiveFrom <= end)?.DailyRate ?? 0m;

            lines.Add(new PayrollLineResponse(
                employee.Id, employee.Code, employee.Name,
                fullDays, halfDays, absentDays, overtimeHours,
                displayRate, wage, overtime, wage + overtime));
        }

        return (lines.OrderBy(l => l.Name).ToList(), hours, multiplier);
    }

    private async Task<decimal?> RateOnAsync(Guid employeeId, DateOnly date, CancellationToken ct)
    {
        var rates = await RatesOnAsync(new List<Guid> { employeeId }, date, ct);
        return rates.TryGetValue(employeeId, out var rate) ? rate : null;
    }

    private async Task<Dictionary<Guid, decimal>> RatesOnAsync(
        List<Guid> employeeIds, DateOnly date, CancellationToken ct)
    {
        if (employeeIds.Count == 0) return new Dictionary<Guid, decimal>();

        return await _db.EmployeeWageRates.AsNoTracking()
            .Where(r => employeeIds.Contains(r.EmployeeId) && r.EffectiveFrom <= date)
            .GroupBy(r => r.EmployeeId)
            .Select(g => new
            {
                EmployeeId = g.Key,
                DailyRate = g.OrderByDescending(r => r.EffectiveFrom).First().DailyRate
            })
            .ToDictionaryAsync(x => x.EmployeeId, x => x.DailyRate, ct);
    }

    private static EmployeeResponse ToResponse(Employee e, decimal? currentRate) => new(
        e.Id, e.Code, e.Name, e.FatherName, e.Cnic, e.Phone, e.Designation,
        e.JoinedOn, e.IsActive, e.LeftOn, currentRate, e.Notes);

    private static PayrollRunResponse ToResponse(PayrollRun run) => new(
        run.Id, run.RunNumber, run.PeriodStart, run.PeriodEnd, run.TotalAmount, run.EmployeeCount,
        run.StandardHoursPerDay, run.OvertimeMultiplier, run.Status, run.CancellationReason,
        run.CreatedAt,
        run.Lines
            .OrderBy(l => l.EmployeeNameSnapshot)
            .Select(l => new PayrollLineResponse(
                l.EmployeeId, l.EmployeeCodeSnapshot, l.EmployeeNameSnapshot,
                l.FullDays, l.HalfDays, l.AbsentDays, l.OvertimeHours,
                l.DailyRateSnapshot, l.WageAmount, l.OvertimeAmount, l.NetAmount))
            .ToList());

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
