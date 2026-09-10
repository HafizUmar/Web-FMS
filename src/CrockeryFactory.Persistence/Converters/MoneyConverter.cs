using CrockeryFactory.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace CrockeryFactory.Persistence.Converters;

public class MoneyConverter : ValueConverter<Money, decimal>
{
    public MoneyConverter() : base(m => m.Amount, d => new Money(d)) { }
}
