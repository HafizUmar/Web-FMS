using CrockeryFactory.Domain.Enums;

namespace CrockeryFactory.Application.Sales.Dtos;

// ---------------------------------------------------------------------------
// Customers
// ---------------------------------------------------------------------------

public sealed record CustomerResponse(
    Guid Id, string Code, string Name, string? City, string? Phone, string? Address,
    decimal OpeningBalance, DateOnly? OpeningBalanceAsOf, bool IsActive, string? Notes,
    DateTime CreatedAt);

public sealed record CreateCustomerRequest(
    string Code, string Name, string? City, string? Phone, string? Address,
    decimal OpeningBalance, DateOnly? OpeningBalanceAsOf, string? Notes);

public sealed record UpdateCustomerRequest(
    string Code, string Name, string? City, string? Phone, string? Address,
    decimal OpeningBalance, DateOnly? OpeningBalanceAsOf, string? Notes);

public sealed record CustomerQuery(
    string? Search = null, bool IncludeInactive = false, int Page = 1, int PageSize = 50);

public sealed record OutstandingRow(
    Guid CustomerId, string Code, string Name, string? City, string? Phone,
    decimal OpeningBalance, decimal TotalDispatched, decimal TotalPaid,
    decimal Outstanding, DateOnly? LastDispatchDate, DateOnly? LastPaymentDate,
    int DaysSinceLastPayment);

public sealed record OutstandingResponse(
    IReadOnlyList<OutstandingRow> Rows, decimal TotalOutstanding, DateOnly AsOf);

public sealed record StatementLine(
    DateOnly Date, string DocumentType, string DocumentNumber,
    string Description, decimal? Debit, decimal? Credit, decimal RunningBalance);

public sealed record StatementResponse(
    Guid CustomerId, string CustomerName, DateOnly From, DateOnly To,
    decimal OpeningBalance, IReadOnlyList<StatementLine> Lines, decimal ClosingBalance);

// ---------------------------------------------------------------------------
// Dispatches
// ---------------------------------------------------------------------------

public sealed record DispatchLineInput(Guid ProductId, QualityGrade Grade, int Quantity, decimal? UnitRate);

public sealed record CreateDispatchRequest(
    Guid CustomerId, DateOnly DispatchDate,
    IReadOnlyList<DispatchLineInput> Lines,
    string? VehicleNumber, string? Notes);

public sealed record DispatchLineResponse(
    int LineNumber, Guid ProductId, string ProductCode, string ProductName,
    QualityGrade Grade, int Quantity, decimal UnitRate, decimal LineAmount,
    int StockAfter);

public sealed record DispatchResponse(
    Guid Id, string DispatchNumber, Guid CustomerId, string CustomerName,
    DateOnly DispatchDate, IReadOnlyList<DispatchLineResponse> Lines,
    decimal TotalAmount, decimal CustomerBalanceAfter,
    string? VehicleNumber, string? Notes,
    DocumentStatus Status, string EnteredBy, DateTime CreatedAt,
    IReadOnlyList<string> Warnings);

public sealed record DispatchQuery(
    Guid? CustomerId = null, DateOnly? From = null, DateOnly? To = null,
    bool IncludeCancelled = false, int Page = 1, int PageSize = 50);

// ---------------------------------------------------------------------------
// Payments
// ---------------------------------------------------------------------------

public sealed record CreatePaymentRequest(
    Guid CustomerId, DateOnly PaymentDate, decimal Amount,
    PaymentMethod Method, string? Reference, string? Notes);

public sealed record PaymentResponse(
    Guid Id, string PaymentNumber, Guid CustomerId, string CustomerName,
    DateOnly PaymentDate, decimal Amount, PaymentMethod Method,
    string? Reference, decimal CustomerBalanceAfter,
    DocumentStatus Status, string EnteredBy, DateTime CreatedAt,
    IReadOnlyList<string> Warnings);

public sealed record PaymentQuery(
    Guid? CustomerId = null, DateOnly? From = null, DateOnly? To = null,
    bool IncludeCancelled = false, int Page = 1, int PageSize = 50);

public sealed record CancelDocumentRequest(string Reason);
