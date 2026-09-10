namespace CrockeryFactory.Domain.Enums;

/// <summary>Quality grade a piece is sorted into after firing. Stored as int - grades are ordered.</summary>
public enum QualityGrade
{
    First = 1,
    Second = 2,
    Third = 3
}

/// <summary>Why stock moved. Every StockMovement has exactly one.</summary>
public enum StockMovementType
{
    ProductionReceipt,
    Dispatch,
    DispatchCancellation,
    ProductionCancellation,
    Adjustment,
    CountCorrection,
    SalesReturn,
    OpeningBalance
}

/// <summary>Which document caused a stock movement, for tracing back.</summary>
public enum StockReferenceType
{
    ProductionEntry,
    Dispatch,
    StockAdjustment,
    StockCount,
    SalesReturn,
    OpeningBalance
}

public enum DocumentStatus
{
    Active,
    Cancelled
}

public enum PaymentMethod
{
    Cash,
    BankTransfer,
    Cheque,
    Other
}

/// <summary>Discriminator for the shared ReasonCode lookup table.</summary>
public enum ReasonCodeType
{
    Breakage,
    StockAdjustment,
    SalesReturn,
    DispatchCancellation
}
