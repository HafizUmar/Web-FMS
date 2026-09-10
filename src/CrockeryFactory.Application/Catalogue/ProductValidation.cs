using System.Text.RegularExpressions;
using CrockeryFactory.Application.Catalogue.Dtos;
using CrockeryFactory.Application.Common;

namespace CrockeryFactory.Application.Catalogue;

/// <summary>
/// Field-level rules from spec section 3.3, kept beside the service rather than in the
/// controller so that a second caller cannot reach the service without them.
/// </summary>
internal static partial class ProductValidation
{
    private const int MaxCodeLength = 24;
    private const int MaxNameLength = 160;
    private const int MinCapacityMl = 1;
    private const int MaxCapacityMl = 5_000;
    private const decimal MaxUnitRate = 1_000_000m;

    [GeneratedRegex(@"^[A-Z0-9\-]+$")]
    private static partial Regex CodePattern();

    public static string NormaliseCode(string? code) => (code ?? string.Empty).Trim().ToUpperInvariant();

    public static void ValidateCode(string code, List<FieldError> errors)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            errors.Add(new FieldError("code", "Code is required", ErrorCodes.ValidationFailed));
            return;
        }

        if (code.Length > MaxCodeLength)
            errors.Add(new FieldError("code", $"Code must be {MaxCodeLength} characters or fewer", ErrorCodes.ValidationFailed));

        if (!CodePattern().IsMatch(code))
            errors.Add(new FieldError("code",
                "Code may contain only capital letters, digits and hyphens", ErrorCodes.ValidationFailed));
    }

    public static void ValidateName(string? name, List<FieldError> errors)
    {
        if (string.IsNullOrWhiteSpace(name))
            errors.Add(new FieldError("name", "Name is required", ErrorCodes.ValidationFailed));
        else if (name.Length > MaxNameLength)
            errors.Add(new FieldError("name", $"Name must be {MaxNameLength} characters or fewer", ErrorCodes.ValidationFailed));
    }

    public static void ValidateCapacity(int? capacityMl, List<FieldError> errors)
    {
        if (capacityMl is { } ml && (ml < MinCapacityMl || ml > MaxCapacityMl))
            errors.Add(new FieldError("capacityMl",
                $"Capacity must be between {MinCapacityMl} and {MaxCapacityMl} ml", ErrorCodes.ValidationFailed));
    }

    /// <summary>
    /// At least one price, one per grade, no duplicate grades, each rate in range.
    /// A duplicate grade is its own code because the fix differs: the clerk has to decide
    /// which of the two rates he meant.
    /// </summary>
    public static void ValidatePrices(IReadOnlyList<GradePriceInput>? prices, List<FieldError> errors)
    {
        if (prices is null || prices.Count == 0)
        {
            errors.Add(new FieldError("prices", "At least one price is required", ErrorCodes.ValidationFailed));
            return;
        }

        var duplicates = prices
            .GroupBy(p => p.Grade)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        foreach (var grade in duplicates)
            errors.Add(new FieldError("prices", $"Grade {grade} appears more than once", ErrorCodes.DuplicateGrade));

        for (var i = 0; i < prices.Count; i++)
        {
            var rate = prices[i].UnitRate;
            if (rate <= 0m)
                errors.Add(new FieldError($"prices[{i}].unitRate", "Rate must be greater than zero", ErrorCodes.ValidationFailed));
            else if (rate > MaxUnitRate)
                errors.Add(new FieldError($"prices[{i}].unitRate",
                    $"Rate must be {MaxUnitRate:N0} or less", ErrorCodes.ValidationFailed));
        }
    }

    public static void ThrowIfAny(List<FieldError> errors, string detail)
    {
        if (errors.Count > 0)
            throw DomainException.Validation(ErrorCodes.ValidationFailed, detail, errors.ToArray());
    }
}
