using CrockeryFactory.Application.Admin.Dtos;
using CrockeryFactory.Application.Common;
using CrockeryFactory.Domain.Enums;

namespace CrockeryFactory.Application.Admin;

public interface IReasonCodeService
{
    Task<IReadOnlyList<ReasonCodeResponse>> ListAsync(
        ReasonCodeType? type, bool includeInactive, CancellationToken ct = default);

    Task<ReasonCodeResponse> CreateAsync(CreateReasonCodeRequest request, CancellationToken ct = default);
    Task<ReasonCodeResponse> UpdateAsync(Guid id, UpdateReasonCodeRequest request, CancellationToken ct = default);
    Task DeactivateAsync(Guid id, CancellationToken ct = default);
}

public interface ISettingsService
{
    Task<IReadOnlyList<SettingResponse>> ListAsync(CancellationToken ct = default);
    Task<IReadOnlyList<SettingResponse>> UpdateAsync(UpdateSettingsRequest request, CancellationToken ct = default);
}

public interface IAuditService
{
    Task<PagedResult<AuditEntryResponse>> QueryAsync(AuditQuery query, CancellationToken ct = default);
}

public interface IStockRebuildService
{
    /// <summary>
    /// Recomputes StockBalances from the ledger. Safe to run at any time, and logged.
    ///
    /// This is the reconciliation tool for the day the cache and the ledger disagree.
    /// The ledger wins: it is append-only and every row is attributable, whereas the
    /// balance is a running total that could have been left wrong by a bug.
    /// </summary>
    Task<RebuildResult> RebuildAsync(CancellationToken ct = default);
}
