using CrockeryFactory.Application.Common;
using Microsoft.Net.Http.Headers;

namespace CrockeryFactory.Web.Infrastructure.Filters;

/// <summary>
/// Optimistic concurrency across HTTP: the row's RowVersion travels out as an ETag and
/// must come back as If-Match on an update.
///
/// Two users today, ten under SC-02. Without this, the second of two people editing the
/// same product simply overwrites the first, and neither of them ever finds out.
/// </summary>
public static class ETags
{
    public static void Set(HttpResponse response, byte[]? rowVersion)
    {
        if (rowVersion is null || rowVersion.Length == 0)
            return;

        response.Headers.ETag = Format(rowVersion);
    }

    public static string Format(byte[] rowVersion) => $"\"{Convert.ToBase64String(rowVersion)}\"";

    /// <summary>
    /// Reads If-Match, or throws with an error the clerk can act on.
    /// </summary>
    public static byte[] Require(HttpRequest request)
    {
        var raw = request.Headers[HeaderNames.IfMatch].ToString();

        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new DomainException(
                ErrorCodes.IfMatchRequired, "If-Match required", StatusCodes.Status428PreconditionRequired,
                "This update needs the record's current version. Reload the record and try again.");
        }

        // A wildcard means "whatever is there now", which is exactly the lost update
        // this header exists to prevent.
        if (raw.Trim() == "*")
        {
            throw new DomainException(
                ErrorCodes.IfMatchRequired, "If-Match required", StatusCodes.Status428PreconditionRequired,
                "If-Match: * is not accepted here. Send the version you actually read.");
        }

        var token = raw.Trim().Trim('"');
        if (token.StartsWith("W/", StringComparison.Ordinal))
            token = token[2..].Trim('"');

        try
        {
            return Convert.FromBase64String(token);
        }
        catch (FormatException)
        {
            throw DomainException.Conflict(ErrorCodes.ConcurrencyConflict,
                "The record version sent with this update was not readable. Reload the record and try again.");
        }
    }
}
