using CrockeryFactory.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace CrockeryFactory.UnitTests.Domain;

public class MoneyTests
{
    [Fact]
    public void Ten_tenths_sum_to_exactly_one()
    {
        // The reason Money wraps decimal and not double. On binary floating point this
        // sum is 0.9999999999999999, and a bill that does not add up is the one bug the
        // client is guaranteed to notice.
        var total = Money.Zero;
        for (var i = 0; i < 10; i++)
            total += new Money(0.10m);

        total.Amount.Should().Be(1.00m);
    }

    [Fact]
    public void Multiplying_by_a_quantity_gives_a_line_amount()
    {
        (new Money(37.50m) * 24).Amount.Should().Be(900.00m);
    }

    [Fact]
    public void Subtraction_can_go_negative_and_says_so()
    {
        // A negative customer balance is legitimate - advances are normal in this trade
        // (spec section 3.8), so Money reports the sign rather than refusing it.
        var balance = new Money(1_000m) - new Money(1_500m);

        balance.Amount.Should().Be(-500m);
        balance.IsNegative.Should().BeTrue();
    }

    [Fact]
    public void Zero_is_not_negative()
    {
        Money.Zero.IsNegative.Should().BeFalse();
    }

    [Theory]
    [InlineData(100, 200, -1)]
    [InlineData(200, 100, 1)]
    [InlineData(100, 100, 0)]
    public void Comparison_orders_by_amount(decimal left, decimal right, int expected)
    {
        Math.Sign(new Money(left).CompareTo(new Money(right))).Should().Be(expected);
    }

    [Fact]
    public void Comparison_operators_agree_with_CompareTo()
    {
        var small = new Money(10m);
        var large = new Money(20m);

        (small < large).Should().BeTrue();
        (large > small).Should().BeTrue();
        (small <= new Money(10m)).Should().BeTrue();
        (large >= new Money(20m)).Should().BeTrue();
    }

    [Fact]
    public void ToString_is_two_decimal_places_for_printing()
    {
        new Money(1234.5m).ToString().Should().Be("1,234.50");
    }

    [Fact]
    public void Equality_is_by_value()
    {
        new Money(42.00m).Should().Be(new Money(42.00m));
    }
}
