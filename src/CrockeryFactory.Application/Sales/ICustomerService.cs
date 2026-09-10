using CrockeryFactory.Application.Common;
using CrockeryFactory.Application.Sales.Dtos;

namespace CrockeryFactory.Application.Sales;

public interface ICustomerService
{
    Task<PagedResult<CustomerResponse>> ListAsync(CustomerQuery query, CancellationToken ct = default);
    Task<CustomerResponse> GetAsync(Guid id, CancellationToken ct = default);
    Task<byte[]?> GetRowVersionAsync(Guid id, CancellationToken ct = default);
    Task<CustomerResponse> CreateAsync(CreateCustomerRequest request, CancellationToken ct = default);
    Task<CustomerResponse> UpdateAsync(Guid id, UpdateCustomerRequest request, byte[] rowVersion, CancellationToken ct = default);
    Task DeactivateAsync(Guid id, CancellationToken ct = default);
    Task<OutstandingResponse> OutstandingAsync(CancellationToken ct = default);
    Task<StatementResponse> StatementAsync(Guid id, DateOnly? from, DateOnly? to, CancellationToken ct = default);
}
