using CrockeryFactory.Application.Abstractions;
using CrockeryFactory.Application.Common;
using CrockeryFactory.Domain.Enums;
using CrockeryFactory.Persistence;
using CrockeryFactory.Shared.Constants;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace CrockeryFactory.Application.Services;

/// <summary>
/// Settings are read on nearly every write and changed a few times a year, so they are
/// cached. The cache is dropped explicitly when settings are written rather than given a
/// short expiry: a clerk who is told a grade is not enabled, waits for the owner to
/// enable it, and is told the same thing for another minute will conclude the system is
/// broken.
/// </summary>
public sealed class FactorySettingsProvider : IFactorySettings
{
    private const string CacheKey = "factory-settings";

    private readonly FactoryDbContext _db;
    private readonly IMemoryCache _cache;

    public FactorySettingsProvider(FactoryDbContext db, IMemoryCache cache)
    {
        _db = db;
        _cache = cache;
    }

    private async Task<IReadOnlyDictionary<string, string>> AllAsync(CancellationToken ct)
    {
        if (_cache.TryGetValue(CacheKey, out IReadOnlyDictionary<string, string>? cached) && cached is not null)
            return cached;

        var loaded = await _db.FactorySettings
            .AsNoTracking()
            .ToDictionaryAsync(s => s.Key, s => s.Value, ct);

        _cache.Set(CacheKey, (IReadOnlyDictionary<string, string>)loaded, TimeSpan.FromMinutes(30));
        return loaded;
    }

    public async Task<string> GetStringAsync(string key, string fallback, CancellationToken ct = default)
    {
        var all = await AllAsync(ct);
        return all.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;
    }

    public async Task<int> GetIntAsync(string key, int fallback, CancellationToken ct = default)
    {
        var raw = await GetStringAsync(key, string.Empty, ct);
        return int.TryParse(raw, out var parsed) ? parsed : fallback;
    }

    public async Task<decimal> GetDecimalAsync(string key, decimal fallback, CancellationToken ct = default)
    {
        var raw = await GetStringAsync(key, string.Empty, ct);

        // Invariant culture on purpose: the value is stored as typed into a settings box,
        // and "1.5" must not become 15 on a machine whose locale uses a comma.
        return decimal.TryParse(raw, System.Globalization.NumberStyles.Number,
            System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;
    }

    public async Task<IReadOnlySet<QualityGrade>> GetEnabledGradesAsync(CancellationToken ct = default)
    {
        var raw = await GetStringAsync(SettingKeys.EnabledGrades, "1,2", ct);

        var grades = raw
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => int.TryParse(part, out var n) ? n : 0)
            .Where(n => Enum.IsDefined(typeof(QualityGrade), n))
            .Select(n => (QualityGrade)n)
            .ToHashSet();

        // A misconfigured setting must not lock the factory out of recording anything.
        // Firsts always work; that is the grade every factory sorts to.
        return grades.Count > 0 ? grades : new HashSet<QualityGrade> { QualityGrade.First };
    }

    public async Task RequireGradeEnabledAsync(QualityGrade grade, string field, CancellationToken ct = default)
    {
        var enabled = await GetEnabledGradesAsync(ct);
        if (enabled.Contains(grade))
            return;

        throw DomainException.Unprocessable(
            ErrorCodes.GradeNotEnabled,
            "Grade not enabled",
            $"Grade {grade} is not in use at this factory. Grades in use: " +
            $"{string.Join(", ", enabled.OrderBy(g => g))}.",
            new FieldError(field, $"{grade} is not enabled", ErrorCodes.GradeNotEnabled));
    }

    public void Invalidate() => _cache.Remove(CacheKey);
}
