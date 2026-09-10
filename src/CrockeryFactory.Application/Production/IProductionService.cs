using CrockeryFactory.Application.Common;
using CrockeryFactory.Application.Production.Dtos;

namespace CrockeryFactory.Application.Production;

public interface IProductionService
{
    Task<ProductionEntryResponse> CreateAsync(
        CreateProductionEntryRequest request, CancellationToken ct = default);

    Task<ProductionEntryResponse> GetAsync(Guid id, CancellationToken ct = default);

    Task<PagedResult<ProductionEntryResponse>> ListAsync(
        ProductionEntryQuery query, CancellationToken ct = default);

    /// <summary>
    /// Reverses the stock the entry put into the godown. The entry and its original
    /// movements are never deleted or edited (BR-08) - both rows stay, so the history
    /// shows what happened rather than concealing it.
    /// </summary>
    Task<ProductionEntryResponse> CancelAsync(
        Guid id, CancelRequest request, CancellationToken ct = default);

    Task<IReadOnlyList<ProductionSummaryRow>> SummaryAsync(
        ProductionSummaryQuery query, CancellationToken ct = default);

    /// <summary>Whether cancelling this entry needs the historical-cancellation policy (PR-07).</summary>
    Task<bool> RequiresHistoricalPermissionAsync(Guid id, CancellationToken ct = default);
}
