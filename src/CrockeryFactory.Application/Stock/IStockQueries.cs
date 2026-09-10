using CrockeryFactory.Application.Common;
using CrockeryFactory.Application.Stock.Dtos;

namespace CrockeryFactory.Application.Stock;

public interface IStockQueries
{
    Task<StockResponse> GetStockAsync(StockQuery query, CancellationToken ct = default);

    Task<PagedResult<StockMovementItem>> GetMovementsAsync(
        Guid productId, MovementQuery query, CancellationToken ct = default);
}

public interface IStockAdjustments
{
    Task<AdjustmentResponse> CreateAsync(CreateAdjustmentRequest request, CancellationToken ct = default);
}
