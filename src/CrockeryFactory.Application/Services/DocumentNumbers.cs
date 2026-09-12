using CrockeryFactory.Application.Abstractions;
using CrockeryFactory.Persistence;
using CrockeryFactory.Shared.Constants;
using Microsoft.EntityFrameworkCore;

namespace CrockeryFactory.Application.Services;

/// <summary>
/// Allocates {Prefix}-{yyMM}-{seq:D4}, e.g. D-2609-0001 (BE-4).
///
/// The sequence restarts each month and each series counts independently, so the number
/// is readable on paper: a clerk reading "D-2609-0042" over the phone knows it is the
/// forty-second dispatch of September 2026 without looking anything up.
///
/// Allocation is highest-in-period plus one. Two clerks saving in the same second would
/// both compute the same number, so the caller relies on the unique index to reject the
/// loser and retries. At this factory's volume that collision is rare; the unique index
/// is what makes it harmless rather than a duplicated document number.
/// </summary>
public sealed class DocumentNumbers : IDocumentNumbers
{
    private readonly FactoryDbContext _db;
    private readonly IFactorySettings _settings;

    public DocumentNumbers(FactoryDbContext db, IFactorySettings settings)
    {
        _db = db;
        _settings = settings;
    }

    public async Task<string> NextAsync(DocumentSeries series, DateOnly documentDate, CancellationToken ct = default)
    {
        var prefix = await _settings.GetStringAsync(SettingKeyFor(series), DefaultPrefixFor(series), ct);
        var period = documentDate.ToString("yyMM");
        var stem = $"{prefix}-{period}-";

        var highest = await HighestInPeriodAsync(series, stem, ct);

        return $"{stem}{highest + 1:D4}";
    }

    private async Task<int> HighestInPeriodAsync(DocumentSeries series, string stem, CancellationToken ct)
    {
        // Cancelled documents keep their numbers - BR-08 forbids deletes, and a gap in
        // the series is what makes a customer think something is being hidden. So the
        // scan deliberately includes every status.
        var numbers = series switch
        {
            DocumentSeries.Dispatch => await _db.Dispatches
                .Where(d => d.DispatchNumber.StartsWith(stem))
                .Select(d => d.DispatchNumber).ToListAsync(ct),

            DocumentSeries.ProductionEntry => await _db.ProductionEntries
                .Where(p => p.EntryNumber.StartsWith(stem))
                .Select(p => p.EntryNumber).ToListAsync(ct),

            DocumentSeries.Payment => await _db.Payments
                .Where(p => p.PaymentNumber.StartsWith(stem))
                .Select(p => p.PaymentNumber).ToListAsync(ct),

            DocumentSeries.StockAdjustment => await _db.StockAdjustments
                .Where(a => a.AdjustmentNumber.StartsWith(stem))
                .Select(a => a.AdjustmentNumber).ToListAsync(ct),

            DocumentSeries.PayrollRun => await _db.PayrollRuns
                .Where(r => r.RunNumber.StartsWith(stem))
                .Select(r => r.RunNumber).ToListAsync(ct),

            _ => throw new ArgumentOutOfRangeException(nameof(series), series, "Unknown document series.")
        };

        var highest = 0;
        foreach (var number in numbers)
        {
            var tail = number[stem.Length..];
            if (int.TryParse(tail, out var parsed) && parsed > highest)
                highest = parsed;
        }

        return highest;
    }

    private static string SettingKeyFor(DocumentSeries series) => series switch
    {
        DocumentSeries.Dispatch => SettingKeys.DocumentPrefixDispatch,
        DocumentSeries.ProductionEntry => SettingKeys.DocumentPrefixProduction,
        DocumentSeries.Payment => SettingKeys.DocumentPrefixPayment,
        DocumentSeries.StockAdjustment => SettingKeys.DocumentPrefixAdjustment,
        DocumentSeries.PayrollRun => SettingKeys.DocumentPrefixPayroll,
        _ => throw new ArgumentOutOfRangeException(nameof(series), series, "Unknown document series.")
    };

    private static string DefaultPrefixFor(DocumentSeries series) => series switch
    {
        DocumentSeries.Dispatch => "D",
        DocumentSeries.ProductionEntry => "P",
        DocumentSeries.Payment => "R",
        DocumentSeries.StockAdjustment => "A",
        DocumentSeries.PayrollRun => "W",
        _ => "X"
    };
}
