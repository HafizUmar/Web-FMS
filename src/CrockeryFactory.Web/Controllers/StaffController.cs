using CrockeryFactory.Application.Staff;
using CrockeryFactory.Application.Staff.Dtos;
using CrockeryFactory.Shared.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CrockeryFactory.Web.Controllers;

/// <summary>
/// Employees, attendance and weekly wages.
///
/// Authorised with CanViewReports, which every signed-in role holds: the factory asked for
/// wage figures to be visible to anyone who can sign in. The spec's SE-11 anticipates a
/// tighter rule later (salary restricted at the query, not just the endpoint); when that
/// day comes it is a policy change here and a filter in the service, not a redesign.
/// </summary>
[ApiController]
[Route("api/v1/employees")]
[Authorize(Policy = Policies.CanViewReports)]
public sealed class EmployeesController : ControllerBase
{
    private readonly IStaffService _staff;

    public EmployeesController(IStaffService staff) => _staff = staff;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<EmployeeResponse>>> List(
        [FromQuery] bool includeInactive = false, CancellationToken ct = default) =>
        Ok(await _staff.ListEmployeesAsync(includeInactive, ct));

    [HttpGet("{id:guid}", Name = nameof(GetEmployee))]
    public async Task<ActionResult<EmployeeResponse>> GetEmployee(Guid id, CancellationToken ct) =>
        Ok(await _staff.GetEmployeeAsync(id, ct));

    [HttpPost]
    public async Task<ActionResult<EmployeeResponse>> Create(
        [FromBody] CreateEmployeeRequest request, CancellationToken ct)
    {
        var created = await _staff.CreateEmployeeAsync(request, ct);
        return CreatedAtRoute(nameof(GetEmployee), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<EmployeeResponse>> Update(
        Guid id, [FromBody] UpdateEmployeeRequest request, CancellationToken ct) =>
        Ok(await _staff.UpdateEmployeeAsync(id, request, ct));

    /// <summary>Marks someone as having left. Never a delete - their payslips must stay readable.</summary>
    [HttpPost("{id:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(
        Guid id, [FromBody] DeactivateEmployeeRequest request, CancellationToken ct)
    {
        await _staff.DeactivateEmployeeAsync(id, request.LeftOn, ct);
        return NoContent();
    }

    [HttpGet("{id:guid}/wage-rates")]
    public async Task<ActionResult<IReadOnlyList<WageRateResponse>>> WageRates(Guid id, CancellationToken ct) =>
        Ok(await _staff.ListWageRatesAsync(id, ct));

    [HttpPost("{id:guid}/wage-rates")]
    public async Task<ActionResult<WageRateResponse>> SetWageRate(
        Guid id, [FromBody] SetWageRateRequest request, CancellationToken ct) =>
        Ok(await _staff.SetWageRateAsync(id, request, ct));
}

public sealed record DeactivateEmployeeRequest(DateOnly LeftOn);

[ApiController]
[Route("api/v1/attendance")]
[Authorize(Policy = Policies.CanViewReports)]
public sealed class AttendanceController : ControllerBase
{
    private readonly IStaffService _staff;

    public AttendanceController(IStaffService staff) => _staff = staff;

    /// <summary>
    /// The whole day's sheet, including everyone not yet marked. Returning the unmarked as
    /// rows rather than omitting them is what lets the screen be a sheet a clerk works
    /// down, rather than a list they have to add people to.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<AttendanceSheetResponse>> Sheet(
        [FromQuery] DateOnly? date, CancellationToken ct) =>
        Ok(await _staff.GetSheetAsync(date ?? DateOnly.FromDateTime(DateTime.Now), ct));

    [HttpPost]
    public async Task<ActionResult<AttendanceSheetResponse>> Mark(
        [FromBody] MarkAttendanceRequest request, CancellationToken ct) =>
        Ok(await _staff.MarkAsync(request, ct));
}

[ApiController]
[Route("api/v1/payroll")]
[Authorize(Policy = Policies.CanViewReports)]
public sealed class PayrollController : ControllerBase
{
    private readonly IStaffService _staff;

    public PayrollController(IStaffService staff) => _staff = staff;

    /// <summary>
    /// What the week would pay, computed but not saved. Shares its whole calculation with
    /// the create path, so what the clerk approves is what gets written.
    /// </summary>
    [HttpGet("preview")]
    public async Task<ActionResult<PayrollPreviewResponse>> Preview(
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct = default)
    {
        var (start, end) = from is null || to is null
            ? PayrollMath.WeekContaining(DateOnly.FromDateTime(DateTime.Now))
            : (from.Value, to.Value);

        return Ok(await _staff.PreviewPayrollAsync(start, end, ct));
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PayrollRunResponse>>> List(
        [FromQuery] int take = 20, CancellationToken ct = default) =>
        Ok(await _staff.ListPayrollAsync(take, ct));

    [HttpGet("{id:guid}", Name = nameof(GetPayroll))]
    public async Task<ActionResult<PayrollRunResponse>> GetPayroll(Guid id, CancellationToken ct) =>
        Ok(await _staff.GetPayrollAsync(id, ct));

    [HttpPost]
    public async Task<ActionResult<PayrollRunResponse>> Create(
        [FromBody] CreatePayrollRunRequest request, CancellationToken ct)
    {
        var created = await _staff.CreatePayrollAsync(request, ct);
        return CreatedAtRoute(nameof(GetPayroll), new { id = created.Id }, created);
    }

    [HttpPost("{id:guid}/cancel")]
    public async Task<ActionResult<PayrollRunResponse>> Cancel(
        Guid id, [FromBody] CancelPayrollRequest request, CancellationToken ct) =>
        Ok(await _staff.CancelPayrollAsync(id, request.Reason, ct));
}

public sealed record CancelPayrollRequest(string Reason);
