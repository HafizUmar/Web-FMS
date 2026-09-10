namespace CrockeryFactory.Shared.Authorization;

/// <summary>The three roles from BRD section 4. Phase 2/3 roles are added here first.</summary>
public static class Roles
{
    public const string Owner = "Owner";
    public const string Clerk = "Clerk";
    public const string Administrator = "Administrator";

    public static readonly IReadOnlyList<string> All = new[] { Owner, Clerk, Administrator };
}

/// <summary>
/// Authorisation is by policy, never by role name in an attribute, so that adding an
/// Accountant role in phase 2 is a change to policy registration in one place rather
/// than an edit to every controller (spec section 4.1).
///
/// These are constants and not string literals because a typo in
/// [Authorize(Policy = "CanSetPirces")] is a silent misconfiguration, whereas a typo
/// in Policies.CanSetPirces does not compile.
/// </summary>
public static class Policies
{
    public const string CanRecordTransactions = nameof(CanRecordTransactions);
    public const string CanAdjustStock = nameof(CanAdjustStock);
    public const string CanManageCatalogue = nameof(CanManageCatalogue);
    public const string CanSetPrices = nameof(CanSetPrices);
    public const string CanCancelHistorical = nameof(CanCancelHistorical);
    public const string CanViewReports = nameof(CanViewReports);
    public const string CanManageUsers = nameof(CanManageUsers);
    public const string CanManageSettings = nameof(CanManageSettings);
    public const string CanViewAudit = nameof(CanViewAudit);
}
