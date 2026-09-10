namespace CrockeryFactory.Application.Common;

/// <summary>
/// A rule the caller broke, carrying everything the response needs.
///
/// The detail string is shown to the clerk, so every factory method below names the
/// product, the numbers or the date involved. "SqlException: violation of unique
/// constraint" is not an error a clerk can act on; "Only 120 units of CUP-ESP-01
/// (First) are in stock. 300 were requested." is.
/// </summary>
public class DomainException : Exception
{
    public DomainException(
        string code,
        string title,
        int status,
        string detail,
        IReadOnlyList<FieldError>? errors = null) : base(detail)
    {
        Code = code;
        Title = title;
        Status = status;
        Detail = detail;
        Errors = errors;
    }

    public string Code { get; }
    public string Title { get; }
    public int Status { get; }
    public string Detail { get; }
    public IReadOnlyList<FieldError>? Errors { get; }

    /// <summary>400 - malformed, or fails field validation.</summary>
    public static DomainException Validation(string code, string detail, params FieldError[] errors) =>
        new(code, "Validation failed", 400, detail, errors.Length > 0 ? errors : null);

    /// <summary>404.</summary>
    public static DomainException NotFound(string what, string which) =>
        new(ErrorCodes.NotFound, "Not found", 404, $"{what} '{which}' was not found.");

    /// <summary>409 - a duplicate, or a concurrent edit.</summary>
    public static DomainException Conflict(string code, string detail) =>
        new(code, "Conflict", 409, detail);

    /// <summary>403 - authenticated, but not permitted to do this.</summary>
    public static DomainException Forbidden(string code, string detail) =>
        new(code, "Forbidden", 403, detail);

    /// <summary>422 - the input is well formed and still breaks a business rule.</summary>
    public static DomainException Unprocessable(string code, string title, string detail,
        params FieldError[] errors) =>
        new(code, title, 422, detail, errors.Length > 0 ? errors : null);
}
