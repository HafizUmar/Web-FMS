namespace CrockeryFactory.Domain.ValueObjects;

/// <summary>
/// An amount in PKR. Single-currency by design - NFR-OQ-7 confirms whether that holds.
/// Exists to stop decimal and int being interchangeable at call sites.
/// </summary>
public readonly record struct Money(decimal Amount) : IComparable<Money>
{
    public static readonly Money Zero = new(0m);

    public static Money operator +(Money a, Money b) => new(a.Amount + b.Amount);
    public static Money operator -(Money a, Money b) => new(a.Amount - b.Amount);
    public static Money operator *(Money a, int qty) => new(a.Amount * qty);

    public bool IsNegative => Amount < 0m;

    public int CompareTo(Money other) => Amount.CompareTo(other.Amount);

    public static bool operator <(Money a, Money b) => a.CompareTo(b) < 0;
    public static bool operator >(Money a, Money b) => a.CompareTo(b) > 0;
    public static bool operator <=(Money a, Money b) => a.CompareTo(b) <= 0;
    public static bool operator >=(Money a, Money b) => a.CompareTo(b) >= 0;

    public override string ToString() => Amount.ToString("N2");
}
