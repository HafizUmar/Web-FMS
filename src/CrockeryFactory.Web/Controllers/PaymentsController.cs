using CrockeryFactory.Application.Common;
using CrockeryFactory.Application.Sales;
using CrockeryFactory.Application.Sales.Dtos;
using CrockeryFactory.Shared.Authorization;
using CrockeryFactory.Web.Infrastructure.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CrockeryFactory.Web.Controllers;

[ApiController]
[Route("api/v1/payments")]
public sealed class PaymentsController : ControllerBase
{
    private readonly IPaymentService _payments;
    private readonly IAuthorizationService _authorization;

    public PaymentsController(IPaymentService payments, IAuthorizationService authorization)
    {
        _payments = payments;
        _authorization = authorization;
    }

    [HttpPost]
    [Authorize(Policy = Policies.CanRecordTransactions)]
    [Idempotent]
    public async Task<ActionResult<PaymentResponse>> Create(
        [FromBody] CreatePaymentRequest request, CancellationToken ct)
    {
        var created = await _payments.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Policies.CanViewReports)]
    public async Task<ActionResult<PaymentResponse>> Get(Guid id, CancellationToken ct) =>
        Ok(await _payments.GetAsync(id, ct));

    [HttpGet]
    [Authorize(Policy = Policies.CanViewReports)]
    public async Task<ActionResult<PagedResult<PaymentResponse>>> List(
        [FromQuery] Guid? customerId,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] bool includeCancelled = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default) =>
        Ok(await _payments.ListAsync(
            new PaymentQuery(customerId, from, to, includeCancelled, page, pageSize), ct));

    [HttpPost("{id:guid}/cancel")]
    [Authorize(Policy = Policies.CanRecordTransactions)]
    public async Task<ActionResult<PaymentResponse>> Cancel(
        Guid id, [FromBody] CancelDocumentRequest request, CancellationToken ct)
    {
        if (await _payments.RequiresHistoricalPermissionAsync(id, ct))
        {
            var allowed = await _authorization.AuthorizeAsync(User, Policies.CanCancelHistorical);

            if (!allowed.Succeeded)
            {
                throw DomainException.Forbidden(ErrorCodes.CancellationWindowExpired,
                    "This receipt was not entered today, so only the owner can cancel it.");
            }
        }

        return Ok(await _payments.CancelAsync(id, request, ct));
    }
}
