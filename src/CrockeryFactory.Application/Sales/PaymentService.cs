using CrockeryFactory.Application.Abstractions;
using CrockeryFactory.Application.Common;
using CrockeryFactory.Application.Sales.Dtos;
using CrockeryFactory.Domain.Enums;
using CrockeryFactory.Modules.Sales.Entities;
using CrockeryFactory.Persistence;
using CrockeryFactory.Shared.Constants;
using Microsoft.EntityFrameworkCore;

namespace CrockeryFactory.Application.Sales;

public sealed class PaymentService : IPaymentService
{
    private const int MinimumCancellationReasonLength = 10;
    private const int MaxPageSize = 200;

    private readonly FactoryDbContext _db;
    private readonly IFactorySettings _settings;
    private readonly IDocumentNumbers _numbers;
    private readonly IAuditWriter _audit;

    public PaymentService(
        FactoryDbContext db, IFactorySettings settings, IDocumentNumbers numbers, IAuditWriter audit)
    {
        _db = db;
        _settings = settings;
        _numbers = numbers;
        _audit = audit;
    }

    public async Task<PaymentResponse> CreateAsync(
        CreatePaymentRequest request, CancellationToken ct = default)
    {
        if (request.Amount <= 0m)
        {
            throw DomainException.Validation(ErrorCodes.AmountInvalid,
                "A receipt must be for more than zero.",
                new FieldError("amount", "Must be greater than zero", ErrorCodes.AmountInvalid));
        }

        // A cash receipt is its own record. A cheque or a transfer is a claim about
        // something in a bank, and without the reference nobody can check it later.
        if (request.Method is PaymentMethod.Cheque or PaymentMethod.BankTransfer &&
            string.IsNullOrWhiteSpace(request.Reference))
        {
            throw DomainException.Validation(ErrorCodes.ReferenceRequired,
                request.Method == PaymentMethod.Cheque
                    ? "Enter the cheque number."
                    : "Enter the transfer reference.",
                new FieldError("reference", "Required for this payment method", ErrorCodes.ReferenceRequired));
        }

        var customer = await _db.Customers.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == request.CustomerId, ct)
            ?? throw DomainException.NotFound("Customer", request.CustomerId.ToString());

        // Deliberately no inactive check. A customer who has stopped trading may still
        // be paying off what he owes, and refusing the receipt would mean the money
        // arrived and the system says it did not.

        await BusinessDates.ValidateAsync(
            request.PaymentDate, "paymentDate",
            SettingKeys.BackdateDaysPayment, 30, _db.Clock, _settings, ct);

        var outstandingBefore = await CustomerBalances.ForAsync(_db, customer.Id, ct);

        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            PaymentNumber = await _numbers.NextAsync(DocumentSeries.Payment, request.PaymentDate, ct),
            CustomerId = customer.Id,
            PaymentDate = request.PaymentDate,
            Amount = request.Amount,
            Method = request.Method,
            Reference = request.Reference?.Trim(),
            Notes = request.Notes?.Trim(),
            Status = DocumentStatus.Active,
            CreatedAt = _db.Clock.GetUtcNow().UtcDateTime,
            CreatedByUserId = _db.CurrentUser.RequireUserId()
        };

        _db.Payments.Add(payment);
        await _db.SaveChangesAsync(ct);

        var warnings = new List<string>();

        // Advances are normal in this trade and a negative balance is a legitimate
        // state. Blocking an overpayment would force the clerk to lie about the date or
        // the amount, which is worse than a balance that reads minus.
        if (request.Amount > outstandingBefore)
            warnings.Add(WarningCodes.PaymentExceedsOutstanding);

        return new PaymentResponse(
            payment.Id, payment.PaymentNumber, customer.Id, customer.Name,
            payment.PaymentDate, payment.Amount, payment.Method, payment.Reference,
            outstandingBefore - payment.Amount,
            payment.Status, await EnteredByAsync(payment.CreatedByUserId, ct),
            payment.CreatedAt, warnings);
    }

    public async Task<bool> RequiresHistoricalPermissionAsync(Guid id, CancellationToken ct = default)
    {
        var date = await _db.Payments.AsNoTracking()
            .Where(p => p.Id == id)
            .Select(p => (DateOnly?)p.PaymentDate)
            .FirstOrDefaultAsync(ct);

        return date is not null && date.Value != BusinessDates.Today(_db.Clock);
    }

    public async Task<PaymentResponse> CancelAsync(
        Guid id, CancelDocumentRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Reason) ||
            request.Reason.Trim().Length < MinimumCancellationReasonLength)
        {
            throw DomainException.Validation(ErrorCodes.ReasonRequired,
                $"Give a reason of at least {MinimumCancellationReasonLength} characters. " +
                "Cancelling a receipt puts money back on the customer's account.",
                new FieldError("reason", $"At least {MinimumCancellationReasonLength} characters",
                    ErrorCodes.ReasonRequired));
        }

        var payment = await _db.Payments.FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw DomainException.NotFound("Payment", id.ToString());

        if (payment.Status == DocumentStatus.Cancelled)
            throw DomainException.Conflict(ErrorCodes.AlreadyCancelled, "This receipt has already been cancelled.");

        payment.Status = DocumentStatus.Cancelled;
        payment.CancelledAt = _db.Clock.GetUtcNow().UtcDateTime;
        payment.CancelledByUserId = _db.CurrentUser.RequireUserId();
        payment.CancellationReason = request.Reason.Trim();

        // A cancelled receipt increases what the customer owes, which is the change most
        // likely to be disputed, so it is audited with the amount in both directions.
        _audit.Record(nameof(Payment), payment.Id, "Cancel",
            new { Status = DocumentStatus.Active.ToString(), payment.Amount },
            new
            {
                Status = DocumentStatus.Cancelled.ToString(),
                payment.PaymentNumber,
                payment.Amount,
                payment.CancellationReason
            });

        await _db.SaveChangesAsync(ct);

        return await GetAsync(id, ct);
    }

    public async Task<PaymentResponse> GetAsync(Guid id, CancellationToken ct = default)
    {
        var payment = await _db.Payments.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw DomainException.NotFound("Payment", id.ToString());

        var customer = await _db.Customers.AsNoTracking().FirstAsync(c => c.Id == payment.CustomerId, ct);

        return new PaymentResponse(
            payment.Id, payment.PaymentNumber, customer.Id, customer.Name,
            payment.PaymentDate, payment.Amount, payment.Method, payment.Reference,
            await CustomerBalances.ForAsync(_db, customer.Id, ct),
            payment.Status, await EnteredByAsync(payment.CreatedByUserId, ct),
            payment.CreatedAt, []);
    }

    public async Task<PagedResult<PaymentResponse>> ListAsync(
        PaymentQuery query, CancellationToken ct = default)
    {
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize <= 0 ? 50 : query.PageSize, 1, MaxPageSize);

        var payments = _db.Payments.AsNoTracking();

        if (!query.IncludeCancelled)
            payments = payments.Where(p => p.Status == DocumentStatus.Active);

        if (query.CustomerId is { } customerId)
            payments = payments.Where(p => p.CustomerId == customerId);

        if (query.From is { } from)
            payments = payments.Where(p => p.PaymentDate >= from);

        if (query.To is { } to)
            payments = payments.Where(p => p.PaymentDate <= to);

        var totalCount = await payments.CountAsync(ct);

        var rows = await payments
            .OrderByDescending(p => p.PaymentDate).ThenByDescending(p => p.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var customerIds = rows.Select(r => r.CustomerId).Distinct().ToList();
        var customers = await _db.Customers.AsNoTracking()
            .Where(c => customerIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name, ct);

        var userIds = rows.Select(r => r.CreatedByUserId).Distinct().ToList();
        var users = await _db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName, ct);

        var items = rows.Select(p => new PaymentResponse(
            p.Id, p.PaymentNumber, p.CustomerId, customers.GetValueOrDefault(p.CustomerId, "unknown"),
            p.PaymentDate, p.Amount, p.Method, p.Reference, 0m,
            p.Status, users.GetValueOrDefault(p.CreatedByUserId, "unknown"), p.CreatedAt, []))
            .ToList();

        return new PagedResult<PaymentResponse>(items, page, pageSize, totalCount);
    }

    private async Task<string> EnteredByAsync(Guid userId, CancellationToken ct) =>
        await _db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.FullName)
            .FirstOrDefaultAsync(ct) ?? "unknown";
}
