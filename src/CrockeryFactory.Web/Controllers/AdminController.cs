using CrockeryFactory.Application.Admin;
using CrockeryFactory.Application.Admin.Dtos;
using CrockeryFactory.Application.Common;
using CrockeryFactory.Domain.Enums;
using CrockeryFactory.Shared.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CrockeryFactory.Web.Controllers;

[ApiController]
[Route("api/v1/reason-codes")]
public sealed class ReasonCodesController : ControllerBase
{
    private readonly IReasonCodeService _reasons;

    public ReasonCodesController(IReasonCodeService reasons) => _reasons = reasons;

    [HttpGet]
    [Authorize(Policy = Policies.CanViewReports)]
    public async Task<ActionResult<IReadOnlyList<ReasonCodeResponse>>> List(
        [FromQuery] ReasonCodeType? type,
        [FromQuery] bool includeInactive = false,
        CancellationToken ct = default) =>
        Ok(await _reasons.ListAsync(type, includeInactive, ct));

    [HttpPost]
    [Authorize(Policy = Policies.CanManageSettings)]
    public async Task<ActionResult<ReasonCodeResponse>> Create(
        [FromBody] CreateReasonCodeRequest request, CancellationToken ct) =>
        Ok(await _reasons.CreateAsync(request, ct));

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Policies.CanManageSettings)]
    public async Task<ActionResult<ReasonCodeResponse>> Update(
        Guid id, [FromBody] UpdateReasonCodeRequest request, CancellationToken ct) =>
        Ok(await _reasons.UpdateAsync(id, request, ct));

    [HttpPost("{id:guid}/deactivate")]
    [Authorize(Policy = Policies.CanManageSettings)]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct)
    {
        await _reasons.DeactivateAsync(id, ct);
        return NoContent();
    }
}

[ApiController]
[Route("api/v1/settings")]
[Authorize(Policy = Policies.CanManageSettings)]
public sealed class SettingsController : ControllerBase
{
    private readonly ISettingsService _settings;

    public SettingsController(ISettingsService settings) => _settings = settings;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SettingResponse>>> List(CancellationToken ct) =>
        Ok(await _settings.ListAsync(ct));

    [HttpPut]
    public async Task<ActionResult<IReadOnlyList<SettingResponse>>> Update(
        [FromBody] UpdateSettingsRequest request, CancellationToken ct) =>
        Ok(await _settings.UpdateAsync(request, ct));
}

[ApiController]
[Route("api/v1/audit")]
[Authorize(Policy = Policies.CanViewAudit)]
public sealed class AuditController : ControllerBase
{
    private readonly IAuditService _audit;

    public AuditController(IAuditService audit) => _audit = audit;

    /// <summary>Read-only by design. An audit trail somebody can edit is not an audit trail.</summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<AuditEntryResponse>>> Query(
        [FromQuery] string? entityName,
        [FromQuery] Guid? entityId,
        [FromQuery] Guid? userId,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default) =>
        Ok(await _audit.QueryAsync(new AuditQuery(entityName, entityId, userId, from, to, page, pageSize), ct));
}

[ApiController]
[Route("api/v1/admin")]
[Authorize(Policy = Policies.CanManageSettings)]
public sealed class AdminController : ControllerBase
{
    private readonly IStockRebuildService _rebuild;

    public AdminController(IStockRebuildService rebuild) => _rebuild = rebuild;

    /// <summary>
    /// The reconciliation tool for the day the cache and the ledger disagree. Safe to
    /// run at any time - it only ever makes the balances match the movements.
    /// </summary>
    [HttpPost("rebuild-stock-balances")]
    public async Task<ActionResult<RebuildResult>> RebuildStockBalances(CancellationToken ct) =>
        Ok(await _rebuild.RebuildAsync(ct));
}
