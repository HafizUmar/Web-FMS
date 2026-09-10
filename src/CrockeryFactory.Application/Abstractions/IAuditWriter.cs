namespace CrockeryFactory.Application.Abstractions;

/// <summary>
/// SE-13. Scoped to stock adjustments, price changes, cancellations and user changes -
/// the things someone later disputes.
///
/// Writes are added to the current DbContext but not saved, so the audit row commits in
/// the same transaction as the change it describes. An audit trail that can commit while
/// its subject rolls back is worse than none.
/// </summary>
public interface IAuditWriter
{
    void Record(string entityName, Guid entityId, string action, object? oldValues, object? newValues);
}
