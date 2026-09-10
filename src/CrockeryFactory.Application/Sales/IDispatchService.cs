using CrockeryFactory.Application.Common;
using CrockeryFactory.Application.Sales.Dtos;

namespace CrockeryFactory.Application.Sales;

public interface IDispatchService
{
    Task<DispatchResponse> CreateAsync(CreateDispatchRequest request, CancellationToken ct = default);
    Task<DispatchResponse> GetAsync(Guid id, CancellationToken ct = default);
    Task<PagedResult<DispatchResponse>> ListAsync(DispatchQuery query, CancellationToken ct = default);
    Task<DispatchResponse> CancelAsync(Guid id, CancelDocumentRequest request, CancellationToken ct = default);

    /// <summary>Whether cancelling this dispatch needs the historical-cancellation policy.</summary>
    Task<bool> RequiresHistoricalPermissionAsync(Guid id, CancellationToken ct = default);
}

public interface IPaymentService
{
    Task<PaymentResponse> CreateAsync(CreatePaymentRequest request, CancellationToken ct = default);
    Task<PaymentResponse> GetAsync(Guid id, CancellationToken ct = default);
    Task<PagedResult<PaymentResponse>> ListAsync(PaymentQuery query, CancellationToken ct = default);
    Task<PaymentResponse> CancelAsync(Guid id, CancelDocumentRequest request, CancellationToken ct = default);
    Task<bool> RequiresHistoricalPermissionAsync(Guid id, CancellationToken ct = default);
}
