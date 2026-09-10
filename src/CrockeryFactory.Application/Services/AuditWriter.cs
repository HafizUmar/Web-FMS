using System.Text.Json;
using CrockeryFactory.Application.Abstractions;
using CrockeryFactory.Persistence;
using CrockeryFactory.Shared.Entities;

namespace CrockeryFactory.Application.Services;

public sealed class AuditWriter : IAuditWriter
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    private readonly FactoryDbContext _db;

    public AuditWriter(FactoryDbContext db) => _db = db;

    public void Record(string entityName, Guid entityId, string action, object? oldValues, object? newValues)
    {
        var user = _db.CurrentUser;

        _db.AuditEntries.Add(new AuditEntry
        {
            Id = Guid.NewGuid(),
            EntityName = entityName,
            EntityId = entityId,
            Action = action,
            OldValues = oldValues is null ? null : JsonSerializer.Serialize(oldValues, Json),
            NewValues = newValues is null ? null : JsonSerializer.Serialize(newValues, Json),
            UserId = user.RequireUserId(),

            // Snapshot the name: an audit row that renders "who did this" by joining to
            // a user row would change its own answer when someone is renamed.
            UserNameSnapshot = user.FullName ?? user.UserName ?? "unknown",
            OccurredAt = _db.Clock.GetUtcNow().UtcDateTime
        });

        // Deliberately not saved here. The caller's SaveChanges commits this row in the
        // same transaction as the change it describes.
    }
}
