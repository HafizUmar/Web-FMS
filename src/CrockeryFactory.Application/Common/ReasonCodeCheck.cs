using CrockeryFactory.Domain.Enums;
using CrockeryFactory.Persistence;
using CrockeryFactory.Shared.Entities;
using Microsoft.EntityFrameworkCore;

namespace CrockeryFactory.Application.Common;

/// <summary>
/// ST-06 and PR-05: a reason must come from the configured list, and from the right list.
///
/// Checking the type as well as the id matters - a breakage reason on a stock adjustment
/// would pass a plain existence check and then read as nonsense on the report that
/// groups adjustments by reason.
/// </summary>
public static class ReasonCodeCheck
{
    public static async Task<ReasonCode> RequireAsync(
        FactoryDbContext db, Guid reasonCodeId, ReasonCodeType expectedType, string field,
        CancellationToken ct = default)
    {
        var reason = await db.ReasonCodes
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == reasonCodeId, ct);

        if (reason is null || !reason.IsActive || reason.Type != expectedType)
        {
            var available = await db.ReasonCodes
                .AsNoTracking()
                .Where(r => r.Type == expectedType && r.IsActive)
                .OrderBy(r => r.SortOrder)
                .Select(r => r.Code)
                .ToListAsync(ct);

            throw DomainException.Validation(ErrorCodes.InvalidReasonCode,
                $"That is not a valid {Describe(expectedType)} reason. " +
                $"Choose one of: {string.Join(", ", available)}.",
                new FieldError(field, "Not a valid reason for this document", ErrorCodes.InvalidReasonCode));
        }

        return reason;
    }

    private static string Describe(ReasonCodeType type) => type switch
    {
        ReasonCodeType.Breakage => "breakage",
        ReasonCodeType.StockAdjustment => "stock adjustment",
        ReasonCodeType.SalesReturn => "sales return",
        ReasonCodeType.DispatchCancellation => "dispatch cancellation",
        _ => type.ToString()
    };
}
