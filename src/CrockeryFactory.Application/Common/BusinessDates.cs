using CrockeryFactory.Application.Abstractions;

namespace CrockeryFactory.Application.Common;

/// <summary>
/// The backdating rules, in one place because production, dispatches, payments and
/// adjustments all need them and only differ by how many days they allow.
///
/// The windows themselves come from settings (spec open item BE-5) because the right
/// number depends on how late the factory's paper slips actually arrive, and that is not
/// yet confirmed.
/// </summary>
public static class BusinessDates
{
    /// <summary>
    /// Today in the factory's local time.
    ///
    /// Not UtcNow's date. At a factory five hours ahead of UTC, a dispatch entered at
    /// 02:00 local would otherwise be dated yesterday and land outside its own
    /// backdating window - and the clerk would be told his correct date is wrong.
    /// </summary>
    public static DateOnly Today(TimeProvider clock) =>
        DateOnly.FromDateTime(clock.GetUtcNow().ToLocalTime().DateTime);

    public static async Task ValidateAsync(
        DateOnly date,
        string field,
        string settingKey,
        int fallbackDays,
        TimeProvider clock,
        IFactorySettings settings,
        CancellationToken ct = default)
    {
        var today = Today(clock);

        if (date > today)
        {
            throw DomainException.Validation(ErrorCodes.DateInFuture,
                $"The date {date:yyyy-MM-dd} is in the future. Records are entered for work already done.",
                new FieldError(field, "Cannot be in the future", ErrorCodes.DateInFuture));
        }

        var allowedDays = await settings.GetIntAsync(settingKey, fallbackDays, ct);
        var earliest = today.AddDays(-allowedDays);

        if (date < earliest)
        {
            throw DomainException.Unprocessable(ErrorCodes.DateTooOld, "Date too old",
                $"The date {date:yyyy-MM-dd} is more than {allowedDays} days ago. " +
                $"Entries can be backdated to {earliest:yyyy-MM-dd}. " +
                "Ask the owner to record anything older.",
                new FieldError(field, $"Cannot be earlier than {earliest:yyyy-MM-dd}", ErrorCodes.DateTooOld));
        }
    }
}
