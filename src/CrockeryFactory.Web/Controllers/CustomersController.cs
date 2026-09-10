using CrockeryFactory.Application.Common;
using CrockeryFactory.Application.Sales;
using CrockeryFactory.Application.Sales.Dtos;
using CrockeryFactory.Shared.Authorization;
using CrockeryFactory.Web.Infrastructure.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CrockeryFactory.Web.Controllers;

[ApiController]
[Route("api/v1/customers")]
public sealed class CustomersController : ControllerBase
{
    private readonly ICustomerService _customers;

    public CustomersController(ICustomerService customers) => _customers = customers;

    [HttpGet]
    [Authorize(Policy = Policies.CanViewReports)]
    public async Task<ActionResult<PagedResult<CustomerResponse>>> List(
        [FromQuery] string? search,
        [FromQuery] bool includeInactive = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default) =>
        Ok(await _customers.ListAsync(new CustomerQuery(search, includeInactive, page, pageSize), ct));

    /// <summary>
    /// RP-02, the report that justifies the system to the owner. One call, largest debt
    /// first without being asked.
    /// </summary>
    [HttpGet("outstanding")]
    [Authorize(Policy = Policies.CanViewReports)]
    public async Task<ActionResult<OutstandingResponse>> Outstanding(CancellationToken ct) =>
        Ok(await _customers.OutstandingAsync(ct));

    [HttpGet("{id:guid}", Name = nameof(GetCustomer))]
    [Authorize(Policy = Policies.CanViewReports)]
    public async Task<ActionResult<CustomerResponse>> GetCustomer(Guid id, CancellationToken ct)
    {
        var customer = await _customers.GetAsync(id, ct);
        ETags.Set(Response, await _customers.GetRowVersionAsync(id, ct));
        return Ok(customer);
    }

    [HttpGet("{id:guid}/statement")]
    [Authorize(Policy = Policies.CanViewReports)]
    public async Task<ActionResult<StatementResponse>> Statement(
        Guid id, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct) =>
        Ok(await _customers.StatementAsync(id, from, to, ct));

    [HttpPost]
    [Authorize(Policy = Policies.CanRecordTransactions)]
    [Idempotent]
    public async Task<ActionResult<CustomerResponse>> Create(
        [FromBody] CreateCustomerRequest request, CancellationToken ct)
    {
        var created = await _customers.CreateAsync(request, ct);
        return CreatedAtRoute(nameof(GetCustomer), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Policies.CanRecordTransactions)]
    public async Task<ActionResult<CustomerResponse>> Update(
        Guid id, [FromBody] UpdateCustomerRequest request, CancellationToken ct)
    {
        var updated = await _customers.UpdateAsync(id, request, ETags.Require(Request), ct);
        ETags.Set(Response, await _customers.GetRowVersionAsync(id, ct));
        return Ok(updated);
    }

    [HttpPost("{id:guid}/deactivate")]
    [Authorize(Policy = Policies.CanRecordTransactions)]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct)
    {
        await _customers.DeactivateAsync(id, ct);
        return NoContent();
    }
}
