using CrockeryFactory.Application.Staff;
using FluentAssertions;
using Xunit;

namespace CrockeryFactory.UnitTests.Staff;

/// <summary>
/// The arithmetic a worker will argue about at the window on a Friday. Tested directly,
/// without a database, because that is the part that has to be right.
/// </summary>
public class PayrollMathTests
{
    private const decimal Rate = 1200m;
    private const decimal StandardHours = 8m;
    private const decimal Multiplier = 1.5m;

    [Fact]
    public void A_full_week_of_full_days_pays_the_rate_times_the_days()
    {
        var (wage, overtime, net) = PayrollMath.Calculate(6, 0, 0, Rate, StandardHours, Multiplier);

        wage.Should().Be(7200m);
        overtime.Should().Be(0m);
        net.Should().Be(7200m);
    }

    [Fact]
    public void A_half_day_pays_exactly_half()
    {
        var (wage, _, _) = PayrollMath.Calculate(0, 1, 0, Rate, StandardHours, Multiplier);

        wage.Should().Be(600m);
    }

    [Fact]
    public void An_absence_pays_nothing()
    {
        var (wage, overtime, net) = PayrollMath.Calculate(0, 0, 0, Rate, StandardHours, Multiplier);

        wage.Should().Be(0m);
        overtime.Should().Be(0m);
        net.Should().Be(0m);
    }

    [Fact]
    public void Overtime_is_priced_from_the_daily_rate_and_the_standard_day()
    {
        // 1200 over an 8 hour day is 150 an hour; at 1.5x, two hours is 450.
        var (_, overtime, _) = PayrollMath.Calculate(0, 0, 2, Rate, StandardHours, Multiplier);

        overtime.Should().Be(450m);
    }

    [Fact]
    public void A_half_day_and_overtime_are_both_paid()
    {
        // Somebody who left at midday and came back in the evening earns both.
        var (wage, overtime, net) = PayrollMath.Calculate(0, 1, 3, Rate, StandardHours, Multiplier);

        wage.Should().Be(600m);
        overtime.Should().Be(675m);
        net.Should().Be(1275m);
    }

    [Fact]
    public void The_parts_always_add_up_to_the_total()
    {
        // Rounding per component rather than once at the end is what makes this true, and
        // a payslip whose lines do not sum to the amount paid is the fastest way to lose
        // a worker's trust in the whole system.
        var (wage, overtime, net) = PayrollMath.Calculate(3, 1, 1.5m, 1133.33m, 7.5m, 1.25m);

        (wage + overtime).Should().Be(net);
        decimal.Round(net, 2).Should().Be(net);
    }

    [Fact]
    public void A_zero_standard_day_pays_no_overtime_rather_than_dividing_by_zero()
    {
        // Reachable only by mistyping a setting. Paying nothing is the safe reading; the
        // alternative is an arbitrary number appearing on a payslip.
        var (wage, overtime, net) = PayrollMath.Calculate(1, 0, 4, Rate, 0m, Multiplier);

        wage.Should().Be(1200m);
        overtime.Should().Be(0m);
        net.Should().Be(1200m);
    }

    [Theory]
    // A Saturday is the first day of its own week.
    [InlineData("2026-09-12", "2026-09-12", "2026-09-18")]
    // A Friday is the last day of the week that began the Saturday before.
    [InlineData("2026-09-18", "2026-09-12", "2026-09-18")]
    // Midweek lands in the same window from either end.
    [InlineData("2026-09-15", "2026-09-12", "2026-09-18")]
    [InlineData("2026-09-11", "2026-09-05", "2026-09-11")]
    public void The_week_runs_Saturday_to_Friday(string date, string expectedStart, string expectedEnd)
    {
        var (start, end) = PayrollMath.WeekContaining(DateOnly.Parse(date));

        start.Should().Be(DateOnly.Parse(expectedStart));
        end.Should().Be(DateOnly.Parse(expectedEnd));
        end.DayNumber.Should().Be(start.DayNumber + 6);
    }

    [Fact]
    public void Every_day_of_the_year_falls_in_exactly_one_week()
    {
        // A week boundary that drifts would double-pay or skip a day at the seam, and the
        // seam only shows up on one date a year otherwise.
        for (var day = new DateOnly(2026, 1, 1); day.Year == 2026; day = day.AddDays(1))
        {
            var (start, end) = PayrollMath.WeekContaining(day);

            start.DayOfWeek.Should().Be(DayOfWeek.Saturday);
            end.DayOfWeek.Should().Be(DayOfWeek.Friday);
            (day >= start && day <= end).Should().BeTrue($"{day:yyyy-MM-dd} should be inside its own week");
        }
    }
}
