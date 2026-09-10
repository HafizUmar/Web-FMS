namespace CrockeryFactory.Application.Common;

/// <summary>
/// The machine-readable half of every error response. The client switches on these;
/// the human-readable detail beside them can be reworded without breaking anything.
///
/// Constants rather than literals so that a code used in a service and asserted in a
/// test cannot drift apart by a typo.
/// </summary>
public static class ErrorCodes
{
    // Transport-level
    public const string ValidationFailed = "VALIDATION_FAILED";
    public const string Unauthenticated = "UNAUTHENTICATED";
    public const string Forbidden = "FORBIDDEN";
    public const string NotFound = "NOT_FOUND";
    public const string ConcurrencyConflict = "CONCURRENCY_CONFLICT";

    /// <summary>
    /// 428. Not in the specification's table, which lists only the mismatch case (409).
    /// A missing If-Match is a different fault from a stale one - the client never read
    /// the record - and answering both with 409 would send the clerk to reload a record
    /// that is not actually stale.
    /// </summary>
    public const string IfMatchRequired = "IF_MATCH_REQUIRED";
    public const string DuplicateCode = "DUPLICATE_CODE";
    public const string InternalError = "INTERNAL_ERROR";
    public const string NotImplemented = "NOT_IMPLEMENTED";

    // Authentication
    public const string InvalidCredentials = "INVALID_CREDENTIALS";
    public const string UserInactive = "USER_INACTIVE";
    public const string AccountLocked = "ACCOUNT_LOCKED";
    public const string PasswordIncorrect = "PASSWORD_INCORRECT";

    // Catalogue
    public const string DuplicateGrade = "DUPLICATE_GRADE";
    public const string GradeNotEnabled = "GRADE_NOT_ENABLED";
    public const string CodeLocked = "CODE_LOCKED";
    public const string EffectiveDateInPast = "EFFECTIVE_DATE_IN_PAST";
    public const string ProductHasStock = "PRODUCT_HAS_STOCK";
    public const string ProductInactive = "PRODUCT_INACTIVE";

    // Stock
    public const string StockInsufficient = "STOCK_INSUFFICIENT";
    public const string QuantityZero = "QUANTITY_ZERO";
    public const string InvalidReasonCode = "INVALID_REASON_CODE";
    public const string NotesRequired = "NOTES_REQUIRED";

    // Dates
    public const string DateInFuture = "DATE_IN_FUTURE";
    public const string DateTooOld = "DATE_TOO_OLD";

    // Production
    public const string NoQuantity = "NO_QUANTITY";
    public const string QuantityImplausible = "QUANTITY_IMPLAUSIBLE";
    public const string BreakageReasonRequired = "BREAKAGE_REASON_REQUIRED";
    public const string StockInsufficientForReversal = "STOCK_INSUFFICIENT_FOR_REVERSAL";

    // Cancellation
    public const string ReasonRequired = "REASON_REQUIRED";
    public const string AlreadyCancelled = "ALREADY_CANCELLED";
    public const string CancellationWindowExpired = "CANCELLATION_WINDOW_EXPIRED";
    public const string CancellationTooLate = "CANCELLATION_TOO_LATE";

    // Sales
    public const string CustomerInactive = "CUSTOMER_INACTIVE";
    public const string NoLines = "NO_LINES";
    public const string TooManyLines = "TOO_MANY_LINES";
    public const string DuplicateLine = "DUPLICATE_LINE";
    public const string NoPriceAvailable = "NO_PRICE_AVAILABLE";
    public const string OpeningBalanceLocked = "OPENING_BALANCE_LOCKED";
    public const string AmountInvalid = "AMOUNT_INVALID";
    public const string ReferenceRequired = "REFERENCE_REQUIRED";
}

/// <summary>
/// Conditions the clerk should see but which must never stop a document being recorded.
///
/// The distinction matters: the first time the system refuses to accept what actually
/// happened on the factory floor is the day the clerk goes back to the paper register.
/// </summary>
public static class WarningCodes
{
    public const string LossUnusuallyHigh = "LOSS_UNUSUALLY_HIGH";
    public const string RateBelowList = "RATE_BELOW_LIST";
    public const string RateBelowCost = "RATE_BELOW_COST";
    public const string PaymentExceedsOutstanding = "PAYMENT_EXCEEDS_OUTSTANDING";
}
