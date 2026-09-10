using CrockeryFactory.Domain.Enums;
using Roles_ = CrockeryFactory.Shared.Authorization.Roles;
using CrockeryFactory.Shared.Constants;
using CrockeryFactory.Shared.Entities;
using CrockeryFactory.Shared.Identity;

namespace CrockeryFactory.Persistence.Configurations.Shared;

/// <summary>
/// Reference data seeded through HasData, so it is versioned with the schema and exists
/// on a fresh install (spec section 2.2).
///
/// Two rules hold everywhere in this file:
///   * Every Guid is a hard-coded literal. Guid.NewGuid() here would produce a different
///     migration on every run and an endless churn of no-op migrations.
///   * Every timestamp and concurrency stamp is a fixed literal, for the same reason.
///
/// Reference data only. Sample data belongs in CrockeryFactory.DevSeeder and must never
/// reach a factory.
/// </summary>
internal static class SeedData
{
    /// <summary>Fixed so that re-running the model build produces an identical migration.</summary>
    private static readonly DateTime SeededAt = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public static IEnumerable<ReasonCode> ReasonCodes() =>
    [
        // Breakage - PR-05.
        new() { Id = SeedConstants.BreakageReasonIds.Crack,  Type = ReasonCodeType.Breakage, Code = "CRACK",  Description = "Cracked in firing", SortOrder = 1 },
        new() { Id = SeedConstants.BreakageReasonIds.Warp,   Type = ReasonCodeType.Breakage, Code = "WARP",   Description = "Warped",            SortOrder = 2 },
        new() { Id = SeedConstants.BreakageReasonIds.Glaze,  Type = ReasonCodeType.Breakage, Code = "GLAZE",  Description = "Glaze fault",       SortOrder = 3 },
        new() { Id = SeedConstants.BreakageReasonIds.Handle, Type = ReasonCodeType.Breakage, Code = "HANDLE", Description = "Handle failure",    SortOrder = 4 },
        new() { Id = SeedConstants.BreakageReasonIds.Other,  Type = ReasonCodeType.Breakage, Code = "OTHER",  Description = "Other",             SortOrder = 99 },

        // Stock adjustment - ST-06 requires the reason to come from a fixed list.
        new() { Id = SeedConstants.StockAdjustmentReasonIds.CountCorrection, Type = ReasonCodeType.StockAdjustment, Code = "COUNT",   Description = "Physical count correction", SortOrder = 1 },
        new() { Id = SeedConstants.StockAdjustmentReasonIds.DamageInStore,   Type = ReasonCodeType.StockAdjustment, Code = "DAMAGE",  Description = "Damaged in store",          SortOrder = 2 },
        new() { Id = SeedConstants.StockAdjustmentReasonIds.Sample,          Type = ReasonCodeType.StockAdjustment, Code = "SAMPLE",  Description = "Issued as sample",          SortOrder = 3 },
        new() { Id = SeedConstants.StockAdjustmentReasonIds.OpeningBalance,  Type = ReasonCodeType.StockAdjustment, Code = "OPENING", Description = "Opening balance",           SortOrder = 4 },
        new() { Id = SeedConstants.StockAdjustmentReasonIds.Other,           Type = ReasonCodeType.StockAdjustment, Code = "OTHER",   Description = "Other",                    SortOrder = 99 },

        // Sales return - the entity is phase 2, but the list is configured now so the
        // enum and the lookup table do not disagree.
        new() { Id = SeedConstants.SalesReturnReasonIds.DamagedInTransit, Type = ReasonCodeType.SalesReturn, Code = "TRANSIT", Description = "Damaged in transit",  SortOrder = 1 },
        new() { Id = SeedConstants.SalesReturnReasonIds.WrongItem,        Type = ReasonCodeType.SalesReturn, Code = "WRONG",   Description = "Wrong item supplied", SortOrder = 2 },
        new() { Id = SeedConstants.SalesReturnReasonIds.QualityComplaint, Type = ReasonCodeType.SalesReturn, Code = "QUALITY", Description = "Quality complaint",   SortOrder = 3 },
        new() { Id = SeedConstants.SalesReturnReasonIds.Other,            Type = ReasonCodeType.SalesReturn, Code = "OTHER",   Description = "Other",               SortOrder = 99 },

        // Dispatch cancellation - BR-05.
        new() { Id = SeedConstants.DispatchCancellationReasonIds.DataEntryError,   Type = ReasonCodeType.DispatchCancellation, Code = "ENTRY",   Description = "Data entry error",     SortOrder = 1 },
        new() { Id = SeedConstants.DispatchCancellationReasonIds.OrderCancelled,   Type = ReasonCodeType.DispatchCancellation, Code = "ORDER",   Description = "Order cancelled",      SortOrder = 2 },
        new() { Id = SeedConstants.DispatchCancellationReasonIds.VehicleNotLoaded, Type = ReasonCodeType.DispatchCancellation, Code = "VEHICLE", Description = "Vehicle not loaded",   SortOrder = 3 },
        new() { Id = SeedConstants.DispatchCancellationReasonIds.Other,            Type = ReasonCodeType.DispatchCancellation, Code = "OTHER",   Description = "Other",                SortOrder = 99 }
    ];

    public static IEnumerable<FactorySetting> FactorySettings() =>
    [
        Setting(SettingKeys.FactoryName,    "Crockery Factory",  "Printed on every document header."),
        Setting(SettingKeys.FactoryAddress, "",                  "Printed under the factory name."),
        Setting(SettingKeys.FactoryPhone,   "",                  "Printed on dispatch documents."),

        // BE-3: the enum allows three grades; the factory enables the subset it sorts to.
        Setting(SettingKeys.EnabledGrades, "1,2",
            "Comma-separated QualityGrade values in use. A grade not listed is rejected with GRADE_NOT_ENABLED."),

        // BE-4: document numbering is {Prefix}-{yyMM}-{seq:D4}, e.g. D-2609-0001.
        // A factory continuing an existing register series changes the prefix here.
        Setting(SettingKeys.DocumentPrefixDispatch,   "D", "Dispatch number prefix."),
        Setting(SettingKeys.DocumentPrefixProduction, "P", "Production entry number prefix."),
        Setting(SettingKeys.DocumentPrefixPayment,    "R", "Payment receipt number prefix."),
        Setting(SettingKeys.DocumentPrefixAdjustment, "A", "Stock adjustment number prefix."),

        // BE-5: backdating windows. Held here rather than in code because the right
        // answer depends on how late the factory's paper slips actually arrive.
        Setting(SettingKeys.BackdateDaysProduction, "7",  "Days a production entry may be backdated."),
        Setting(SettingKeys.BackdateDaysDispatch,   "7",  "Days a dispatch may be backdated."),
        Setting(SettingKeys.BackdateDaysPayment,    "30", "Days a payment may be backdated."),
        Setting(SettingKeys.BackdateDaysAdjustment, "30", "Days a stock adjustment may be backdated."),

        Setting(SettingKeys.DispatchCancellationMaxDays, "90",
            "A dispatch older than this cannot be cancelled - CANCELLATION_TOO_LATE."),

        Setting(SettingKeys.ProductionLossWarningPercent, "25",
            "Loss above this returns LOSS_UNUSUALLY_HIGH as a warning. It never blocks the entry.")
    ];

    private static FactorySetting Setting(string key, string value, string description) => new()
    {
        Key = key,
        Value = value,
        Description = description,
        UpdatedAt = SeededAt,
        UpdatedByUserId = SeedConstants.SystemUserId
    };

    public static IEnumerable<AppRole> Roles() =>
    [
        new()
        {
            Id = SeedConstants.RoleIds.Owner,
            Name = Roles_.Owner,
            NormalizedName = "OWNER",
            Description = "Full access, including prices, catalogue and historical cancellations.",
            ConcurrencyStamp = "b0000000-role-owner-stamp-000000000001"
        },
        new()
        {
            Id = SeedConstants.RoleIds.Clerk,
            Name = Roles_.Clerk,
            NormalizedName = "CLERK",
            Description = "Records production, dispatches, payments and stock adjustments.",
            ConcurrencyStamp = "b0000000-role-clerk-stamp-000000000002"
        },
        new()
        {
            Id = SeedConstants.RoleIds.Administrator,
            Name = Roles_.Administrator,
            NormalizedName = "ADMINISTRATOR",
            Description = "Manages users, settings and audit. No transaction entry.",
            ConcurrencyStamp = "b0000000-role-admin-stamp-000000000003"
        }
    ];

    /// <summary>
    /// The account seeded rows are attributed to. It is not a login: IsActive is false,
    /// there is no password hash, and lockout is set past any plausible date, so all
    /// three independent checks refuse it.
    /// </summary>
    public static IEnumerable<AppUser> SystemUser() =>
    [
        new()
        {
            Id = SeedConstants.SystemUserId,
            UserName = "system",
            NormalizedUserName = "SYSTEM",
            FullName = "System",
            IsActive = false,
            EmailConfirmed = false,
            PasswordHash = null,
            SecurityStamp = "00000000-system-security-stamp-0001",
            ConcurrencyStamp = "00000000-system-concurrency-stamp-01",
            LockoutEnabled = true,
            LockoutEnd = new DateTimeOffset(9999, 12, 31, 23, 59, 59, TimeSpan.Zero),
            CreatedAt = SeededAt
        }
    ];
}
