using CrockeryFactory.Domain.Enums;
using CrockeryFactory.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace CrockeryFactory.UnitTests.Domain;

public class StockKeyTests
{
    private static readonly Guid ProductId = new("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void Same_product_at_different_grades_are_different_stock_lines()
    {
        // Stock is held per product AND grade. Firsts and seconds of the same cup are
        // two independent balances - conflating them is how a dispatch of seconds
        // silently draws down the firsts.
        new StockKey(ProductId, QualityGrade.First)
            .Should().NotBe(new StockKey(ProductId, QualityGrade.Second));
    }

    [Fact]
    public void Equality_is_by_value_so_it_works_as_a_dictionary_key()
    {
        var balances = new Dictionary<StockKey, int>
        {
            [new StockKey(ProductId, QualityGrade.First)] = 120
        };

        balances[new StockKey(ProductId, QualityGrade.First)].Should().Be(120);
    }

    [Fact]
    public void Grades_are_ordered_so_they_can_be_compared_and_indexed_as_int()
    {
        ((int)QualityGrade.First).Should().Be(1);
        ((int)QualityGrade.Second).Should().Be(2);
        ((int)QualityGrade.Third).Should().Be(3);
    }
}
