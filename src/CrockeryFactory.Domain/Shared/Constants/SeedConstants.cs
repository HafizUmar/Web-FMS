namespace CrockeryFactory.Shared.Constants;

/// <summary>
/// Fixed identifiers for seeded reference data.
///
/// Every Guid here is a hard-coded literal and never Guid.NewGuid(). A generated value
/// produces a different migration on every run and an endless churn of no-op migrations
/// (spec section 2.2).
/// </summary>
public static class SeedConstants
{
    /// <summary>
    /// Attributed as the author of seeded rows. Not a login - it has no password hash
    /// and IsActive is false, so it cannot authenticate.
    /// </summary>
    public static readonly Guid SystemUserId = new("00000000-0000-0000-0000-00000000513e");

    public static class RoleIds
    {
        public static readonly Guid Owner = new("b0000000-0000-0000-0000-000000000001");
        public static readonly Guid Clerk = new("b0000000-0000-0000-0000-000000000002");
        public static readonly Guid Administrator = new("b0000000-0000-0000-0000-000000000003");
    }

    public static class BreakageReasonIds
    {
        public static readonly Guid Crack = new("a1f00000-0000-0000-0000-000000000001");
        public static readonly Guid Warp = new("a1f00000-0000-0000-0000-000000000002");
        public static readonly Guid Glaze = new("a1f00000-0000-0000-0000-000000000003");
        public static readonly Guid Handle = new("a1f00000-0000-0000-0000-000000000004");
        public static readonly Guid Other = new("a1f00000-0000-0000-0000-000000000005");
    }

    public static class StockAdjustmentReasonIds
    {
        public static readonly Guid CountCorrection = new("a2f00000-0000-0000-0000-000000000001");
        public static readonly Guid DamageInStore = new("a2f00000-0000-0000-0000-000000000002");
        public static readonly Guid Sample = new("a2f00000-0000-0000-0000-000000000003");
        public static readonly Guid OpeningBalance = new("a2f00000-0000-0000-0000-000000000004");
        public static readonly Guid Other = new("a2f00000-0000-0000-0000-000000000005");
    }

    public static class SalesReturnReasonIds
    {
        public static readonly Guid DamagedInTransit = new("a3f00000-0000-0000-0000-000000000001");
        public static readonly Guid WrongItem = new("a3f00000-0000-0000-0000-000000000002");
        public static readonly Guid QualityComplaint = new("a3f00000-0000-0000-0000-000000000003");
        public static readonly Guid Other = new("a3f00000-0000-0000-0000-000000000004");
    }

    public static class DispatchCancellationReasonIds
    {
        public static readonly Guid DataEntryError = new("a4f00000-0000-0000-0000-000000000001");
        public static readonly Guid OrderCancelled = new("a4f00000-0000-0000-0000-000000000002");
        public static readonly Guid VehicleNotLoaded = new("a4f00000-0000-0000-0000-000000000003");
        public static readonly Guid Other = new("a4f00000-0000-0000-0000-000000000004");
    }
}
