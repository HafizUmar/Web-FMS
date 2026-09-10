namespace CrockeryFactory.Shared.Constants;

/// <summary>
/// Keys for the FactorySetting table. Held as constants so that a setting rename is a
/// compile error rather than a lookup that silently returns the default.
///
/// The backdating windows and document prefixes live here rather than in code because
/// spec open items BE-4 and BE-5 are unconfirmed - a different answer from the factory
/// must be a settings change, not a deploy.
/// </summary>
public static class SettingKeys
{
    public const string FactoryName = "Factory.Name";
    public const string FactoryAddress = "Factory.Address";
    public const string FactoryPhone = "Factory.Phone";

    /// <summary>Comma-separated QualityGrade int values that are in use (BE-3).</summary>
    public const string EnabledGrades = "Grades.Enabled";

    public const string DocumentPrefixDispatch = "Document.Prefix.Dispatch";
    public const string DocumentPrefixProduction = "Document.Prefix.Production";
    public const string DocumentPrefixPayment = "Document.Prefix.Payment";
    public const string DocumentPrefixAdjustment = "Document.Prefix.Adjustment";

    /// <summary>Backdating windows in days (BE-5).</summary>
    public const string BackdateDaysProduction = "Backdate.Days.Production";
    public const string BackdateDaysDispatch = "Backdate.Days.Dispatch";
    public const string BackdateDaysPayment = "Backdate.Days.Payment";
    public const string BackdateDaysAdjustment = "Backdate.Days.Adjustment";

    /// <summary>Cancellation ceiling for a dispatch, in days.</summary>
    public const string DispatchCancellationMaxDays = "Dispatch.Cancellation.MaxDays";

    /// <summary>Loss percentage above which a production entry returns a warning.</summary>
    public const string ProductionLossWarningPercent = "Production.LossWarningPercent";
}
