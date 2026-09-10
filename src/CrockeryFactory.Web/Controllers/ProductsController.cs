using CrockeryFactory.Application.Catalogue;
using CrockeryFactory.Application.Catalogue.Dtos;
using CrockeryFactory.Application.Common;
using CrockeryFactory.Shared.Authorization;
using CrockeryFactory.Web.Infrastructure.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CrockeryFactory.Web.Controllers;

[ApiController]
[Route("api/v1/products")]
public sealed class ProductsController : ControllerBase
{
    private readonly IProductService _products;

    public ProductsController(IProductService products) => _products = products;

    [HttpGet]
    [Authorize(Policy = Policies.CanViewReports)]
    public async Task<ActionResult<PagedResult<ProductListItem>>> List(
        [FromQuery] string? search,
        [FromQuery] bool includeInactive = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default) =>
        Ok(await _products.ListAsync(new ProductQuery(search, includeInactive, page, pageSize), ct));

    [HttpGet("{id:guid}", Name = nameof(GetProduct))]
    [Authorize(Policy = Policies.CanViewReports)]
    public async Task<ActionResult<ProductResponse>> GetProduct(Guid id, CancellationToken ct)
    {
        var product = await _products.GetAsync(id, ct);
        ETags.Set(Response, await _products.GetRowVersionAsync(id, ct));
        return Ok(product);
    }

    /// <summary>Owner only - BR-07.</summary>
    [HttpPost]
    [Authorize(Policy = Policies.CanManageCatalogue)]
    [Idempotent]
    public async Task<ActionResult<ProductResponse>> Create(
        [FromBody] CreateProductRequest request, CancellationToken ct)
    {
        var created = await _products.CreateAsync(request, ct);
        ETags.Set(Response, await _products.GetRowVersionAsync(created.Id, ct));

        return CreatedAtRoute(nameof(GetProduct), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Policies.CanManageCatalogue)]
    public async Task<ActionResult<ProductResponse>> Update(
        Guid id, [FromBody] UpdateProductRequest request, CancellationToken ct)
    {
        var updated = await _products.UpdateAsync(id, request, ETags.Require(Request), ct);
        ETags.Set(Response, await _products.GetRowVersionAsync(id, ct));

        return Ok(updated);
    }

    /// <summary>
    /// Prices change here rather than through the product update, because this is the
    /// path that supersedes the old rate and writes the audit row (BR-07, SE-13).
    /// </summary>
    [HttpPut("{id:guid}/prices")]
    [Authorize(Policy = Policies.CanSetPrices)]
    public async Task<ActionResult<ProductResponse>> UpdatePrices(
        Guid id, [FromBody] UpdatePricesRequest request, CancellationToken ct) =>
        Ok(await _products.UpdatePricesAsync(id, request, ct));

    [HttpPost("{id:guid}/deactivate")]
    [Authorize(Policy = Policies.CanManageCatalogue)]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct)
    {
        await _products.DeactivateAsync(id, ct);
        return NoContent();
    }
}
