using CrockeryFactory.Application.Catalogue.Dtos;
using CrockeryFactory.Application.Common;

namespace CrockeryFactory.Application.Catalogue;

public interface IProductService
{
    Task<PagedResult<ProductListItem>> ListAsync(ProductQuery query, CancellationToken ct = default);

    Task<ProductResponse> GetAsync(Guid id, CancellationToken ct = default);

    Task<ProductResponse> CreateAsync(CreateProductRequest request, CancellationToken ct = default);

    Task<ProductResponse> UpdateAsync(Guid id, UpdateProductRequest request, byte[] rowVersion, CancellationToken ct = default);

    Task<ProductResponse> UpdatePricesAsync(Guid id, UpdatePricesRequest request, CancellationToken ct = default);

    Task DeactivateAsync(Guid id, CancellationToken ct = default);

    /// <summary>The concurrency token for the ETag on a single-product response.</summary>
    Task<byte[]?> GetRowVersionAsync(Guid id, CancellationToken ct = default);
}
