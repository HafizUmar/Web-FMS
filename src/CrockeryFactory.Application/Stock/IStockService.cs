using CrockeryFactory.Domain.Enums;
using CrockeryFactory.Domain.ValueObjects;

namespace CrockeryFactory.Application.Stock;

/// <summary>
/// One movement to apply to the ledger. Quantity is signed: positive is a receipt into
/// the godown, negative is an issue out of it.
/// </summary>
public sealed record StockMovementRequest(
    Guid ProductId,
    QualityGrade Grade,
    int Quantity,
    StockMovementType MovementType,
    StockReferenceType ReferenceType,
    Guid ReferenceId,
    DateOnly OccurredOn,
    Guid? ReasonCodeId = null,
    string? Notes = null)
{
    public StockKey Key => new(ProductId, Grade);
}

/// <summary>
/// The only writer to the stock ledger.
///
/// StockMovement has a polymorphic ReferenceId - it points at a ProductionEntry,
/// Dispatch, StockAdjustment or StockCount depending on ReferenceType - and there is no
/// foreign key, because there cannot be one to four different tables. Referential
/// integrity there is this class's responsibility, which is only tractable because
/// everything that moves stock comes through here.
/// </summary>
public interface IStockService
{
    /// <summary>
    /// Validates every movement, then writes the ledger rows and updates the cached
    /// balances.
    ///
    /// Does NOT call SaveChanges. The caller owns the transaction, because a movement
    /// that commits without the document that caused it - or the other way round - is
    /// exactly the corruption the ledger exists to prevent.
    ///
    /// All movements are checked before any is written, so a four-line dispatch that
    /// fails on line three leaves nothing behind.
    /// </summary>
    Task ApplyAsync(IReadOnlyList<StockMovementRequest> movements, CancellationToken ct = default);

    Task<int> GetBalanceAsync(Guid productId, QualityGrade grade, CancellationToken ct = default);

    Task<IReadOnlyDictionary<StockKey, int>> GetBalancesAsync(
        IReadOnlyCollection<StockKey> keys, CancellationToken ct = default);

    /// <summary>
    /// The balance each key would hold after the given deltas, without writing anything.
    /// Used to report StockAfter on a dispatch line.
    /// </summary>
    Task<IReadOnlyDictionary<StockKey, int>> ProjectAsync(
        IReadOnlyList<StockMovementRequest> movements, CancellationToken ct = default);
}
