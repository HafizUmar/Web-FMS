using CrockeryFactory.Application.Common;
using CrockeryFactory.Application.Sales;
using CrockeryFactory.Application.Sales.Dtos;
using CrockeryFactory.Shared.Authorization;
using CrockeryFactory.Web.Infrastructure.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CrockeryFactory.Web.Controllers;

[ApiController]
[Route("api/v1/dispatches")]
public sealed class DispatchesController : ControllerBase
{
    private readonly IDispatchService _dispatches;
    private readonly IAuthorizationService _authorization;

    public DispatchesController(IDispatchService dispatches, IAuthorizationService authorization)
    {
        _dispatches = dispatches;
        _authorization = authorization;
    }

    /// <summary>
    /// The transaction with the most rules, and the one PF-11 measures at 90 seconds.
    /// An Idempotency-Key is strongly recommended here: a duplicated dispatch means
    /// stock leaves the godown twice on paper that says once.
    /// </summary>
    [HttpPost]
    [Authorize(Policy = Policies.CanRecordTransactions)]
    [Idempotent]
    public async Task<ActionResult<DispatchResponse>> Create(
        [FromBody] CreateDispatchRequest request, CancellationToken ct)
    {
        var created = await _dispatches.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Policies.CanViewReports)]
    public async Task<ActionResult<DispatchResponse>> Get(Guid id, CancellationToken ct) =>
        Ok(await _dispatches.GetAsync(id, ct));

    [HttpGet]
    [Authorize(Policy = Policies.CanViewReports)]
    public async Task<ActionResult<PagedResult<DispatchResponse>>> List(
        [FromQuery] Guid? customerId,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] bool includeCancelled = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default) =>
        Ok(await _dispatches.ListAsync(
            new DispatchQuery(customerId, from, to, includeCancelled, page, pageSize), ct));

    /// <summary>Same day: clerk. Older: owner only. The rule depends on the document's date.</summary>
    [HttpPost("{id:guid}/cancel")]
    [Authorize(Policy = Policies.CanRecordTransactions)]
    public async Task<ActionResult<DispatchResponse>> Cancel(
        Guid id, [FromBody] CancelDocumentRequest request, CancellationToken ct)
    {
        if (await _dispatches.RequiresHistoricalPermissionAsync(id, ct))
        {
            var allowed = await _authorization.AuthorizeAsync(User, Policies.CanCancelHistorical);

            if (!allowed.Succeeded)
            {
                throw DomainException.Forbidden(ErrorCodes.CancellationWindowExpired,
                    "This dispatch was not made today, so only the owner can cancel it.");
            }
        }

        return Ok(await _dispatches.CancelAsync(id, request, ct));
    }
}
