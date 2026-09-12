using CrockeryFactory.Domain.Enums;
using CrockeryFactory.Modules.Staff.Entities;
using CrockeryFactory.Persistence;
using CrockeryFactory.Shared.Constants;
using Microsoft.EntityFrameworkCore;

namespace CrockeryFactory.DevSeeder;

/// <summary>
/// A believable crew, and the attendance they would have left behind.
///
/// Generated forward day by day rather than sprinkled at random, so the sheets read like a
/// factory: most of the crew in most days, the odd half day, the occasional absence, and
/// overtime concentrated in the people who actually work it.
/// </summary>
public sealed class StaffSeeder
{
    private static readonly (string Code, string Name, string Father, string Designation, decimal Rate)[] Crew =
    {
        ("E-001", "Muhammad Aslam",  "Ghulam Rasool",  "Moulder",      1400m),
        ("E-002", "Riaz Ahmed",      "Nazir Ahmed",    "Moulder",      1350m),
        ("E-003", "Abdul Sattar",    "Muhammad Yousaf","Kiln hand",    1600m),
        ("E-004", "Ghulam Abbas",    "Karam Din",      "Kiln hand",    1550m),
        ("E-005", "Nasir Mehmood",   "Allah Ditta",    "Glazer",       1250m),
        ("E-006", "Shahid Iqbal",    "Bashir Ahmed",   "Glazer",       1250m),
        ("E-007", "Tariq Mahmood",   "Sultan Ali",     "Packer",       1100m),
        ("E-008", "Imran Ali",       "Muhammad Ashraf","Packer",       1100m),
        ("E-009", "Zafar Hussain",   "Ghulam Nabi",    "Loader",       1000m),
        ("E-010", "Muhammad Yaqoob", "Faiz Ahmed",     "Helper",        900m),
        ("E-011", "Saeed Akhtar",    "Noor Muhammad",  "Helper",        900m),
        ("E-012", "Rashid Minhas",   "Abdul Hameed",   "Mixer operator",1450m),
    };

    private readonly FactoryDbContext _db;
    private readonly Random _random;

    public StaffSeeder(FactoryDbContext db, int seed = 20260101)
    {
        _db = db;
        _random = new Random(seed);
    }

    public async Task<(int Employees, int AttendanceDays)> SeedAsync(
        DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        if (await _db.Employees.AnyAsync(ct))
            return (0, 0);

        var now = DateTime.UtcNow;
        var userId = SeedConstants.SystemUserId;
        var joined = from.AddDays(-30);

        var employees = new List<Employee>();

        foreach (var (code, name, father, designation, rate) in Crew)
        {
            var employee = new Employee
            {
                Id = Guid.NewGuid(),
                Code = code,
                Name = name,
                FatherName = father,
                Designation = designation,
                Phone = $"03{_random.Next(10, 49)}-{_random.Next(1000000, 9999999)}",
                JoinedOn = joined,
                IsActive = true,
                CreatedAt = now,
                CreatedByUserId = userId
            };

            employee.WageRates.Add(new EmployeeWageRate
            {
                Id = Guid.NewGuid(),
                EmployeeId = employee.Id,
                DailyRate = rate,
                EffectiveFrom = joined,
                CreatedAt = now,
                CreatedByUserId = userId
            });

            employees.Add(employee);
        }

        _db.Employees.AddRange(employees);

        var days = 0;

        for (var day = from; day <= to; day = day.AddDays(1))
        {
            // Sunday is the weekly day off, so the sheets have the gap a real roll has.
            if (day.DayOfWeek == DayOfWeek.Sunday) continue;

            days++;

            foreach (var employee in employees)
            {
                var roll = _random.NextDouble();

                var status = roll switch
                {
                    < 0.06 => AttendanceStatus.Absent,
                    < 0.12 => AttendanceStatus.HalfDay,
                    _ => AttendanceStatus.Present
                };

                // Overtime only on full days, and only for some of the crew - it is the
                // kiln and the loaders who stay late, not everybody equally.
                var worksLate = employee.Designation is "Kiln hand" or "Loader";
                var overtime = status == AttendanceStatus.Present && worksLate && _random.NextDouble() < 0.35
                    ? _random.Next(1, 5) * 0.5m + 1m
                    : 0m;

                _db.AttendanceRecords.Add(new AttendanceRecord
                {
                    Id = Guid.NewGuid(),
                    EmployeeId = employee.Id,
                    AttendanceDate = day,
                    Status = status,
                    OvertimeHours = overtime,
                    CreatedAt = now,
                    CreatedByUserId = userId
                });
            }
        }

        await _db.SaveChangesAsync(ct);

        return (employees.Count, days);
    }
}
