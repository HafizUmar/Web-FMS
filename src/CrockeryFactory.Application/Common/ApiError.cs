namespace CrockeryFactory.Application.Common;

/// <summary>RFC 7807 ProblemDetails plus a stable machine-readable code.</summary>
public sealed record ApiError(
    string Type,
    string Title,
    int Status,
    string Detail,
    string Code,
    string TraceId,
    IReadOnlyList<FieldError>? Errors = null);

public sealed record FieldError(string Field, string Message, string Code);

public sealed record PagedResult<T>(
    IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);
