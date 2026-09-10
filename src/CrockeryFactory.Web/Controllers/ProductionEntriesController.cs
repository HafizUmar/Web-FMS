using CrockeryFactory.Application.Common;
using CrockeryFactory.Application.Production;
using CrockeryFactory.Application.Production.Dtos;
using CrockeryFactory.Shared.Authorization;
using CrockeryFactory.Web.Infrastructure.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CrockeryFactory.Web.Controllers;

[ApiController]
[Route("api/v1/production-entries")]
public sealed class ProductionEntriesController : ControllerBase
{
    private readonly IProductionService _production;
    private readonly IAuthorizationService _authorization;

    public ProductionEntriesController(
        IProductionService production, IAuthorizationService authorization)
    {
        _production = production;
        _authorization = authorization;
    }

    /// <summary>
    /// The highest-frequency write in the system. PF-12 allows 45 seconds end to end
    /// including typing, which is why the payload is this small.
    /// </summary>
    [HttpPost]
    [Authorize(Policy = Policies.CanRecordTransactions)]
    [Idempotent]
    public async Task<ActionResult<ProductionEntryResponse>> Create(
        [FromBody] CreateProductionEntryRequest request, CancellationToken ct)
    {
        var created = await _production.CreateAsync(request, ct);

        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Policies.CanViewReports)]
    public async Task<ActionResult<ProductionEntryResponse>> Get(Guid id, CancellationToken ct) =>
        Ok(await _production.GetAsync(id, ct));

    [HttpGet]
    [Authorize(Policy = Policies.CanViewReports)]
    public async Task<ActionResult<PagedResult<ProductionEntryResponse>>> List(
        [FromQuery] Guid? productId,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] bool includeCancelled = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default) =>
        Ok(await _production.ListAsync(
            new ProductionEntryQuery(productId, from, to, includeCancelled, page, pageSize), ct));

    [HttpGet("summary")]
    [Authorize(Policy = Policies.CanViewReports)]
    public async Task<ActionResult<IReadOnlyList<ProductionSummaryRow>>> Summary(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] Guid? productId,
        [FromQuery] ProductionGroupBy groupBy = ProductionGroupBy.Product,
        CancellationToken ct = default) =>
        Ok(await _production.SummaryAsync(new ProductionSummaryQuery(from, to, productId, groupBy), ct));

    /// <summary>
    /// PR-07: a clerk may cancel the same day's entry; anything older is the owner's.
    ///
    /// The rule depends on the entry's date, so it cannot be a static policy attribute.
    /// The base policy keeps a clerk from reaching it at all; the second check below
    /// escalates to CanCancelHistorical only once we know the entry is not today's.
    /// </summary>
    [HttpPost("{id:guid}/cancel")]
    [Authorize(Policy = Policies.CanRecordTransactions)]
    public async Task<ActionResult<ProductionEntryResponse>> Cancel(
        Guid id, [FromBody] CancelRequest request, CancellationToken ct)
    {
        if (await _production.RequiresHistoricalPermissionAsync(id, ct))
        {
            var allowed = await _authorization.AuthorizeAsync(User, Policies.CanCancelHistorical);

            if (!allowed.Succeeded)
            {
                throw DomainException.Forbidden(ErrorCodes.CancellationWindowExpired,
                    "This entry was not made today, so only the owner can cancel it. " +
                    "Record a stock adjustment if the figures need correcting.");
            }
        }

        return Ok(await _production.CancelAsync(id, request, ct));
    }
}
