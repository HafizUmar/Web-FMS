using CrockeryFactory.Application.Common;
using CrockeryFactory.Application.Stock;
using CrockeryFactory.Application.Stock.Dtos;
using CrockeryFactory.Domain.Enums;
using CrockeryFactory.Shared.Authorization;
using CrockeryFactory.Web.Infrastructure.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CrockeryFactory.Web.Controllers;

[ApiController]
[Route("api/v1/stock")]
public sealed class StockController : ControllerBase
{
    private readonly IStockQueries _queries;
    private readonly IStockAdjustments _adjustments;

    public StockController(IStockQueries queries, IStockAdjustments adjustments)
    {
        _queries = queries;
        _adjustments = adjustments;
    }

    [HttpGet]
    [Authorize(Policy = Policies.CanViewReports)]
    public async Task<ActionResult<StockResponse>> Get(
        [FromQuery] string? search,
        [FromQuery] QualityGrade? grade,
        [FromQuery] bool onlyInStock = false,
        [FromQuery] DateOnly? asOf = null,
        CancellationToken ct = default) =>
        Ok(await _queries.GetStockAsync(new StockQuery(search, grade, onlyInStock, asOf), ct));

    [HttpGet("{productId:guid}/movements")]
    [Authorize(Policy = Policies.CanViewReports)]
    public async Task<ActionResult<PagedResult<StockMovementItem>>> Movements(
        Guid productId,
        [FromQuery] QualityGrade? grade,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default) =>
        Ok(await _queries.GetMovementsAsync(productId, new MovementQuery(grade, from, to, page, pageSize), ct));

    /// <summary>Clerk or Owner. Audited - an unexplained stock change is the dispute this prevents.</summary>
    [HttpPost("adjustments")]
    [Authorize(Policy = Policies.CanAdjustStock)]
    [Idempotent]
    public async Task<ActionResult<AdjustmentResponse>> CreateAdjustment(
        [FromBody] CreateAdjustmentRequest request, CancellationToken ct)
    {
        var created = await _adjustments.CreateAsync(request, ct);

        return CreatedAtAction(nameof(Movements), new { productId = created.ProductId }, created);
    }
}
