namespace CrockeryFactory.Application.Staff;

/// <summary>
/// How a week of attendance becomes an amount.
///
/// Pulled out of the service and made static so it can be tested directly, without a
/// database. This is the arithmetic a worker will argue about at the window on a Friday,
/// and it is worth being able to check it in isolation.
/// </summary>
public static class PayrollMath
{
    /// <summary>
    /// A full day pays the daily rate, a half day pays half of it, an absence pays
    /// nothing. Overtime is priced from the same daily rate rather than a separate hourly
    /// figure: a daily-wage factory agrees one number per man, and deriving the hour from
    /// it means a rise never has to be entered twice.
    /// </summary>
    public static (decimal Wage, decimal Overtime, decimal Net) Calculate(
        int fullDays,
        int halfDays,
        decimal overtimeHours,
        decimal dailyRate,
        decimal standardHoursPerDay,
        decimal overtimeMultiplier)
    {
        var wage = Round(dailyRate * fullDays + dailyRate * 0.5m * halfDays);

        // A zero or negative standard day would divide by zero. Treating it as "no
        // overtime is payable" is the safe reading: the alternative is an arbitrary
        // number appearing on a payslip because a setting was mistyped.
        var overtime = standardHoursPerDay > 0 && overtimeHours > 0
            ? Round(dailyRate / standardHoursPerDay * overtimeHours * overtimeMultiplier)
            : 0m;

        return (wage, overtime, wage + overtime);
    }

    /// <summary>
    /// Rounded per component rather than once at the end, so the payslip's own lines add
    /// up to its total. A slip whose parts do not sum to the figure paid is the fastest
    /// way to lose a worker's trust in the whole system.
    /// </summary>
    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    /// <summary>
    /// The Saturday-to-Friday week containing a date.
    ///
    /// Saturday because that is how the working week is usually counted here, and Friday
    /// is the day wages are handed out - a week that ends on payday is the one a worker
    /// can check against their own memory.
    /// </summary>
    public static (DateOnly Start, DateOnly End) WeekContaining(DateOnly date)
    {
        var offset = ((int)date.DayOfWeek - (int)DayOfWeek.Saturday + 7) % 7;
        var start = date.AddDays(-offset);

        return (start, start.AddDays(6));
    }
}
