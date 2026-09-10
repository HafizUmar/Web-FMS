using System.Diagnostics;
using CrockeryFactory.Application.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace CrockeryFactory.Web.Infrastructure.Errors;

/// <summary>
/// Turns anything thrown out of a controller into RFC 7807 ProblemDetails carrying a
/// stable machine-readable code.
///
/// The rule that matters here is the last branch: an unexpected exception is logged in
/// full with the trace id and reported to the caller as INTERNAL_ERROR with nothing else.
/// A stack trace or a SQL error text in an HTTP response is an information leak, and it
/// is also useless to the clerk reading it.
/// </summary>
public sealed class DomainExceptionHandler : IExceptionHandler
{
    private const string TypeBase = "https://crockeryfactory/errors/";

    private readonly ILogger<DomainExceptionHandler> _logger;

    public DomainExceptionHandler(ILogger<DomainExceptionHandler> logger) => _logger = logger;

    public async ValueTask<bool> TryHandleAsync(
        HttpContext context, Exception exception, CancellationToken ct)
    {
        var traceId = Activity.Current?.Id ?? context.TraceIdentifier;

        var (status, code, title, detail, errors) = exception switch
        {
            DomainException domain =>
                (domain.Status, domain.Code, domain.Title, domain.Detail, domain.Errors),

            // A cancelled request is the client hanging up, not a server fault.
            OperationCanceledException when context.RequestAborted.IsCancellationRequested =>
                (499, "CLIENT_CLOSED_REQUEST", "Client closed request", "The request was cancelled.", null),

            _ => (500, ErrorCodes.InternalError, "Internal error",
                  "Something went wrong. The problem has been logged - quote the trace id when reporting it.",
                  (IReadOnlyList<FieldError>?)null)
        };

        if (status >= 500)
            _logger.LogError(exception, "Unhandled exception. TraceId {TraceId}", traceId);
        else
            _logger.LogInformation("{Code} on {Path}: {Detail}", code, context.Request.Path, detail);

        if (status == 499)
            return true;   // Nothing to write: the client is already gone.

        var problem = new ProblemDetails
        {
            Type = TypeBase + Slugify(code),
            Title = title,
            Status = status,
            Detail = detail
        };

        problem.Extensions["code"] = code;
        problem.Extensions["traceId"] = traceId;

        if (errors is { Count: > 0 })
        {
            problem.Extensions["errors"] = errors
                .Select(e => new { field = e.Field, message = e.Message, code = e.Code })
                .ToList();
        }

        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(problem, ct);

        return true;
    }

    private static string Slugify(string code) => code.ToLowerInvariant().Replace('_', '-');
}
