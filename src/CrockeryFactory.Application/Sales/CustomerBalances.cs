using CrockeryFactory.Domain.Enums;
using CrockeryFactory.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CrockeryFactory.Application.Sales;

/// <summary>
/// The customer balance, defined in exactly one place.
///
/// Opening balance, plus active dispatches, minus active payments. Payments are not
/// allocated to particular bills: the customer pays something against what he owes, and
/// forcing invoice-level allocation would make the clerk invent it (spec section 1.4,
/// open item BE-2).
///
/// Cancelled documents are excluded everywhere. Having this in one place is what stops
/// the outstanding report, the statement's closing balance and the figure read aloud at
/// the gate from drifting apart - three implementations would eventually disagree, and
/// the customer would find it before we did.
/// </summary>
public static class CustomerBalances
{
    public static async Task<decimal> ForAsync(
        FactoryDbContext db, Guid customerId, CancellationToken ct = default)
    {
        var opening = await db.Customers
            .AsNoTracking()
            .Where(c => c.Id == customerId)
            .Select(c => c.OpeningBalance)
            .FirstOrDefaultAsync(ct);

        var dispatched = await db.Dispatches
            .AsNoTracking()
            .Where(d => d.CustomerId == customerId && d.Status == DocumentStatus.Active)
            .SumAsync(d => (decimal?)d.TotalAmount, ct) ?? 0m;

        var paid = await db.Payments
            .AsNoTracking()
            .Where(p => p.CustomerId == customerId && p.Status == DocumentStatus.Active)
            .SumAsync(p => (decimal?)p.Amount, ct) ?? 0m;

        return opening + dispatched - paid;
    }

    /// <summary>True once anything has been posted against the customer's account.</summary>
    public static async Task<bool> HasTransactionsAsync(
        FactoryDbContext db, Guid customerId, CancellationToken ct = default) =>
        await db.Dispatches.AnyAsync(d => d.CustomerId == customerId, ct) ||
        await db.Payments.AnyAsync(p => p.CustomerId == customerId, ct);
}
