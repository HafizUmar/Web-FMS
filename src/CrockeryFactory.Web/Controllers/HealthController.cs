using CrockeryFactory.Application.Admin.Dtos;
using CrockeryFactory.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrockeryFactory.Web.Controllers;

[ApiController]
[Route("api/v1/health")]
public sealed class HealthController : ControllerBase
{
    private readonly FactoryDbContext _db;
    private readonly ILogger<HealthController> _logger;

    public HealthController(FactoryDbContext db, ILogger<HealthController> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Called by the Setup tool, and useful in a support call.
    ///
    /// Anonymous, and deliberately thin: it reports whether the database answers and
    /// whether migrations are outstanding, and nothing about the data. A health endpoint
    /// that leaks row counts or a connection string to an unauthenticated caller is a
    /// reconnaissance endpoint.
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<HealthResponse>> Get(CancellationToken ct)
    {
        var checkedAt = _db.Clock.GetUtcNow().UtcDateTime;

        try
        {
            var reachable = await _db.Database.CanConnectAsync(ct);

            if (!reachable)
                return Unhealthy("Database did not answer", checkedAt);

            var pending = (await _db.Database.GetPendingMigrationsAsync(ct)).ToList();

            var status = pending.Count == 0 ? "Healthy" : "Degraded";

            return Ok(new HealthResponse(status, true, pending.Count == 0, pending, checkedAt));
        }
        catch (Exception ex)
        {
            // The detail goes to the log, where it is useful. The response says only that
            // the database is unreachable.
            _logger.LogError(ex, "Health check failed");
            return Unhealthy("Database unreachable", checkedAt);
        }
    }

    private ActionResult<HealthResponse> Unhealthy(string status, DateTime checkedAt) =>
        StatusCode(StatusCodes.Status503ServiceUnavailable,
            new HealthResponse(status, false, false, [], checkedAt));
}
