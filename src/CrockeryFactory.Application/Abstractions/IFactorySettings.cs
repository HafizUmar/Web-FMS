using CrockeryFactory.Domain.Enums;

namespace CrockeryFactory.Application.Abstractions;

/// <summary>
/// Reads the FactorySetting table.
///
/// The values behind this interface are the answers to spec section 5's open items -
/// which grades are in use, how far back a document may be dated, what a document number
/// looks like. They are settings rather than constants precisely because those answers
/// are not confirmed, and a different answer must not require a deploy.
/// </summary>
public interface IFactorySettings
{
    Task<string> GetStringAsync(string key, string fallback, CancellationToken ct = default);

    Task<int> GetIntAsync(string key, int fallback, CancellationToken ct = default);

    /// <summary>Grades the factory actually sorts to. A grade outside this set is rejected.</summary>
    Task<IReadOnlySet<QualityGrade>> GetEnabledGradesAsync(CancellationToken ct = default);

    /// <summary>Throws GRADE_NOT_ENABLED rather than returning a bool - every call site wants the throw.</summary>
    Task RequireGradeEnabledAsync(QualityGrade grade, string field, CancellationToken ct = default);

    /// <summary>Drops the cache after a settings write.</summary>
    void Invalidate();
}
