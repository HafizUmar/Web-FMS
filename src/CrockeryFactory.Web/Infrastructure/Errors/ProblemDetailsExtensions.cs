using System.Diagnostics;
using CrockeryFactory.Application.Common;
using Microsoft.AspNetCore.Mvc;

namespace CrockeryFactory.Web.Infrastructure.Errors;

public static class ProblemDetailsExtensions
{
    /// <summary>
    /// Used where a response is produced without an exception - model binding failures,
    /// and the authentication and authorisation handlers - so that every error body in
    /// the API has the same shape and carries a code.
    /// </summary>
    public static ProblemDetails Build(
        HttpContext context, int status, string code, string title, string detail,
        IReadOnlyList<FieldError>? errors = null)
    {
        var problem = new ProblemDetails
        {
            Type = "https://crockeryfactory/errors/" + code.ToLowerInvariant().Replace('_', '-'),
            Title = title,
            Status = status,
            Detail = detail
        };

        problem.Extensions["code"] = code;
        problem.Extensions["traceId"] = Activity.Current?.Id ?? context.TraceIdentifier;

        if (errors is { Count: > 0 })
        {
            problem.Extensions["errors"] = errors
                .Select(e => new { field = e.Field, message = e.Message, code = e.Code })
                .ToList();
        }

        return problem;
    }
}
