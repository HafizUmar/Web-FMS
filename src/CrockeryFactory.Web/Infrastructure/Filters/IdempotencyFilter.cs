using System.Text.Json;
using CrockeryFactory.Persistence;
using CrockeryFactory.Shared.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace CrockeryFactory.Web.Infrastructure.Filters;

/// <summary>
/// Replays the original response when the same Idempotency-Key arrives twice.
///
/// This is not theoretical. A tablet on marginal factory WiFi will send a request,
/// lose the reply, and send it again - and a duplicated dispatch means stock leaves
/// the godown twice on paper that says once.
///
/// Applied by attribute rather than globally: only the POSTs that create a document
/// need it, and a retry of a GET or a cancellation is already harmless.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class IdempotentAttribute : Attribute, IFilterFactory
{
    public bool IsReusable => false;

    public IFilterMetadata CreateInstance(IServiceProvider services) =>
        ActivatorUtilities.CreateInstance<IdempotencyFilter>(services);
}

public sealed class IdempotencyFilter : IAsyncActionFilter
{
    public const string HeaderName = "Idempotency-Key";

    /// <summary>
    /// Spec section 3.1. Long enough that a tablet retrying after a night offline still
    /// gets the original answer; short enough that the table stays small.
    /// </summary>
    private static readonly TimeSpan ReplayWindow = TimeSpan.FromDays(7);

    private const int MaxKeyLength = 64;

    private readonly FactoryDbContext _db;
    private readonly ILogger<IdempotencyFilter> _logger;

    public IdempotencyFilter(FactoryDbContext db, ILogger<IdempotencyFilter> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var request = context.HttpContext.Request;

        if (!request.Headers.TryGetValue(HeaderName, out var header) ||
            string.IsNullOrWhiteSpace(header.ToString()))
        {
            // The header is recommended, not required - a clerk on a desktop on wired
            // LAN does not need it, and refusing the request would block him.
            await next();
            return;
        }

        var key = header.ToString().Trim();
        if (key.Length > MaxKeyLength)
        {
            context.Result = new BadRequestObjectResult(new ProblemDetails
            {
                Title = "Invalid idempotency key",
                Status = StatusCodes.Status400BadRequest,
                Detail = $"{HeaderName} must be {MaxKeyLength} characters or fewer."
            });
            return;
        }

        var endpoint = $"{request.Method} {request.Path}";
        var cutoff = _db.Clock.GetUtcNow().UtcDateTime - ReplayWindow;

        var existing = await _db.IdempotencyRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Key == key, context.HttpContext.RequestAborted);

        if (existing is not null && existing.CreatedAt >= cutoff)
        {
            // The same key against a different endpoint is a client bug, and replaying a
            // dispatch response to a payment request would be worse than refusing.
            if (!string.Equals(existing.Endpoint, endpoint, StringComparison.Ordinal))
            {
                context.Result = new ConflictObjectResult(new ProblemDetails
                {
                    Title = "Idempotency key reused",
                    Status = StatusCodes.Status409Conflict,
                    Detail = $"This {HeaderName} was already used for a different operation."
                });
                return;
            }

            _logger.LogInformation(
                "Replaying idempotent response for {Endpoint}, key {Key}", endpoint, key);

            await WriteReplayAsync(context.HttpContext, existing);
            context.Result = new EmptyResult();
            return;
        }

        var executed = await next();

        if (executed.Exception is not null && !executed.ExceptionHandled)
            return;

        // Only a success is recorded. A rejected request should be retryable once the
        // clerk has fixed whatever was wrong with it.
        if (executed.Result is not ObjectResult { StatusCode: >= 200 and < 300 } result)
            return;

        var body = JsonSerializer.Serialize(result.Value, JsonOptions());

        _db.IdempotencyRecords.Add(new IdempotencyRecord
        {
            Key = key,
            Endpoint = endpoint,
            CreatedResourceId = TryReadId(result.Value),
            StatusCode = result.StatusCode ?? StatusCodes.Status200OK,
            ResponseBody = body,
            Location = executed.HttpContext.Response.Headers.Location.ToString() is { Length: > 0 } loc
                ? loc
                : null,
            CreatedAt = _db.Clock.GetUtcNow().UtcDateTime
        });

        try
        {
            await _db.SaveChangesAsync(executed.HttpContext.RequestAborted);
        }
        catch (DbUpdateException)
        {
            // Two identical requests raced and both got past the read above. The document
            // is written either way; losing the idempotency row only costs the replay,
            // so this must not turn a successful write into an error for the clerk.
            _logger.LogWarning("Idempotency key {Key} was recorded concurrently for {Endpoint}", key, endpoint);
        }
    }

    private static async Task WriteReplayAsync(HttpContext http, IdempotencyRecord record)
    {
        http.Response.StatusCode = record.StatusCode;
        http.Response.ContentType = "application/json";

        if (!string.IsNullOrEmpty(record.Location))
            http.Response.Headers.Location = record.Location;

        // Says plainly that this is a replay, so a support call can tell the difference
        // between "it was submitted twice" and "it was created twice".
        http.Response.Headers["Idempotency-Replayed"] = "true";

        await http.Response.WriteAsync(record.ResponseBody, http.RequestAborted);
    }

    private static JsonSerializerOptions JsonOptions() =>
        new(JsonSerializerDefaults.Web);

    /// <summary>Best effort - used only for tracing a replay back to its document.</summary>
    private static Guid TryReadId(object? value)
    {
        if (value is null) return Guid.Empty;

        var property = value.GetType().GetProperty("Id");
        return property?.GetValue(value) is Guid id ? id : Guid.Empty;
    }
}
