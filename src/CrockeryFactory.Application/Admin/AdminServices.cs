using CrockeryFactory.Application.Abstractions;
using CrockeryFactory.Application.Admin.Dtos;
using CrockeryFactory.Application.Common;
using CrockeryFactory.Domain.Enums;
using CrockeryFactory.Persistence;
using CrockeryFactory.Shared.Entities;
using Microsoft.EntityFrameworkCore;

namespace CrockeryFactory.Application.Admin;

public sealed class ReasonCodeService : IReasonCodeService
{
    private readonly FactoryDbContext _db;
    private readonly IAuditWriter _audit;

    public ReasonCodeService(FactoryDbContext db, IAuditWriter audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<IReadOnlyList<ReasonCodeResponse>> ListAsync(
        ReasonCodeType? type, bool includeInactive, CancellationToken ct = default)
    {
        var codes = _db.ReasonCodes.AsNoTracking();

        if (type is { } wanted)
            codes = codes.Where(r => r.Type == wanted);

        if (!includeInactive)
            codes = codes.Where(r => r.IsActive);

        return await codes
            .OrderBy(r => r.Type).ThenBy(r => r.SortOrder).ThenBy(r => r.Code)
            .Select(r => new ReasonCodeResponse(r.Id, r.Type, r.Code, r.Description, r.SortOrder, r.IsActive))
            .ToListAsync(ct);
    }

    public async Task<ReasonCodeResponse> CreateAsync(
        CreateReasonCodeRequest request, CancellationToken ct = default)
    {
        var code = (request.Code ?? string.Empty).Trim().ToUpperInvariant();

        if (string.IsNullOrWhiteSpace(code) || code.Length > 24)
        {
            throw DomainException.Validation(ErrorCodes.ValidationFailed,
                "A reason needs a short code of up to 24 characters.",
                new FieldError("code", "Required, 24 characters or fewer", ErrorCodes.ValidationFailed));
        }

        if (string.IsNullOrWhiteSpace(request.Description))
        {
            throw DomainException.Validation(ErrorCodes.ValidationFailed,
                "A reason needs a description - this is what the clerk picks from a list.",
                new FieldError("description", "Required", ErrorCodes.ValidationFailed));
        }

        // Unique within its own list, not across all of them: "OTHER" is a legitimate
        // entry in every one.
        if (await _db.ReasonCodes.AnyAsync(r => r.Type == request.Type && r.Code == code, ct))
        {
            throw DomainException.Conflict(ErrorCodes.DuplicateCode,
                $"'{code}' already exists in the {request.Type} list.");
        }

        var reason = new ReasonCode
        {
            Id = Guid.NewGuid(),
            Type = request.Type,
            Code = code,
            Description = request.Description.Trim(),
            SortOrder = request.SortOrder,
            IsActive = true
        };

        _db.ReasonCodes.Add(reason);
        _audit.Record(nameof(ReasonCode), reason.Id, "Create", null,
            new { reason.Type, reason.Code, reason.Description });

        await _db.SaveChangesAsync(ct);

        return new ReasonCodeResponse(
            reason.Id, reason.Type, reason.Code, reason.Description, reason.SortOrder, reason.IsActive);
    }

    public async Task<ReasonCodeResponse> UpdateAsync(
        Guid id, UpdateReasonCodeRequest request, CancellationToken ct = default)
    {
        var reason = await _db.ReasonCodes.FirstOrDefaultAsync(r => r.Id == id, ct)
            ?? throw DomainException.NotFound("Reason code", id.ToString());

        if (string.IsNullOrWhiteSpace(request.Description))
        {
            throw DomainException.Validation(ErrorCodes.ValidationFailed,
                "A reason needs a description.",
                new FieldError("description", "Required", ErrorCodes.ValidationFailed));
        }

        var before = new { reason.Description, reason.SortOrder };

        // The code itself is not editable. It is stamped on documents already recorded,
        // and changing it would silently rewrite what those documents say happened.
        reason.Description = request.Description.Trim();
        reason.SortOrder = request.SortOrder;

        _audit.Record(nameof(ReasonCode), reason.Id, "Update", before,
            new { reason.Description, reason.SortOrder });

        await _db.SaveChangesAsync(ct);

        return new ReasonCodeResponse(
            reason.Id, reason.Type, reason.Code, reason.Description, reason.SortOrder, reason.IsActive);
    }

    public async Task DeactivateAsync(Guid id, CancellationToken ct = default)
    {
        var reason = await _db.ReasonCodes.FirstOrDefaultAsync(r => r.Id == id, ct)
            ?? throw DomainException.NotFound("Reason code", id.ToString());

        if (!reason.IsActive)
            return;

        // Deactivated rather than deleted: documents already carry this reason, and the
        // reports that group by it must still be able to name it.
        reason.IsActive = false;

        _audit.Record(nameof(ReasonCode), reason.Id, "Deactivate",
            new { IsActive = true }, new { IsActive = false });

        await _db.SaveChangesAsync(ct);
    }
}

public sealed class SettingsService : ISettingsService
{
    private readonly FactoryDbContext _db;
    private readonly IFactorySettings _settings;
    private readonly IAuditWriter _audit;

    public SettingsService(FactoryDbContext db, IFactorySettings settings, IAuditWriter audit)
    {
        _db = db;
        _settings = settings;
        _audit = audit;
    }

    public async Task<IReadOnlyList<SettingResponse>> ListAsync(CancellationToken ct = default) =>
        await _db.FactorySettings.AsNoTracking()
            .OrderBy(s => s.Key)
            .Select(s => new SettingResponse(s.Key, s.Value, s.Description, s.UpdatedAt))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<SettingResponse>> UpdateAsync(
        UpdateSettingsRequest request, CancellationToken ct = default)
    {
        if (request.Values is null || request.Values.Count == 0)
        {
            throw DomainException.Validation(ErrorCodes.ValidationFailed,
                "No settings were supplied.",
                new FieldError("values", "At least one setting is required", ErrorCodes.ValidationFailed));
        }

        var keys = request.Values.Keys.ToList();

        var existing = await _db.FactorySettings.Where(s => keys.Contains(s.Key)).ToListAsync(ct);

        var unknown = keys.Except(existing.Select(s => s.Key), StringComparer.Ordinal).ToList();

        if (unknown.Count > 0)
        {
            // Settings are created by migration, not by whoever spells one wrong in a
            // request body. Accepting an unknown key would silently store a value that
            // nothing ever reads.
            throw DomainException.Validation(ErrorCodes.ValidationFailed,
                $"Unknown setting(s): {string.Join(", ", unknown)}.",
                unknown.Select(k => new FieldError($"values.{k}", "No such setting", ErrorCodes.ValidationFailed))
                    .ToArray());
        }

        var now = _db.Clock.GetUtcNow().UtcDateTime;
        var userId = _db.CurrentUser.RequireUserId();
        var changes = new List<object>();

        foreach (var setting in existing)
        {
            var newValue = request.Values[setting.Key];

            if (string.Equals(setting.Value, newValue, StringComparison.Ordinal))
                continue;

            changes.Add(new { setting.Key, From = setting.Value, To = newValue });

            setting.Value = newValue;
            setting.UpdatedAt = now;
            setting.UpdatedByUserId = userId;
        }

        if (changes.Count > 0)
            _audit.Record(nameof(FactorySetting), Guid.Empty, "Update", null, changes);

        await _db.SaveChangesAsync(ct);

        // Dropped rather than left to expire: a clerk told a grade is not enabled, who
        // waits for the owner to enable it and is told the same thing for another
        // minute, concludes the system is broken.
        _settings.Invalidate();

        return await ListAsync(ct);
    }
}

public sealed class AuditService : IAuditService
{
    private const int MaxPageSize = 200;

    private readonly FactoryDbContext _db;

    public AuditService(FactoryDbContext db) => _db = db;

    public async Task<PagedResult<AuditEntryResponse>> QueryAsync(
        AuditQuery query, CancellationToken ct = default)
    {
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize <= 0 ? 50 : query.PageSize, 1, MaxPageSize);

        var entries = _db.AuditEntries.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.EntityName))
            entries = entries.Where(a => a.EntityName == query.EntityName);

        if (query.EntityId is { } entityId)
            entries = entries.Where(a => a.EntityId == entityId);

        if (query.UserId is { } userId)
            entries = entries.Where(a => a.UserId == userId);

        if (query.From is { } from)
            entries = entries.Where(a => a.OccurredAt >= from.ToDateTime(TimeOnly.MinValue));

        if (query.To is { } to)
            entries = entries.Where(a => a.OccurredAt < to.AddDays(1).ToDateTime(TimeOnly.MinValue));

        var totalCount = await entries.CountAsync(ct);

        var items = await entries
            .OrderByDescending(a => a.OccurredAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new AuditEntryResponse(
                a.Id, a.EntityName, a.EntityId, a.Action,
                a.OldValues, a.NewValues, a.UserId, a.UserNameSnapshot, a.OccurredAt))
            .ToListAsync(ct);

        return new PagedResult<AuditEntryResponse>(items, page, pageSize, totalCount);
    }
}
