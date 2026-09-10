using System.Text.RegularExpressions;
using CrockeryFactory.Application.Abstractions;
using CrockeryFactory.Application.Common;
using CrockeryFactory.Application.Sales.Dtos;
using CrockeryFactory.Domain.Enums;
using CrockeryFactory.Modules.Sales.Entities;
using CrockeryFactory.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CrockeryFactory.Application.Sales;

public sealed partial class CustomerService : ICustomerService
{
    private const int MaxPageSize = 200;

    [GeneratedRegex(@"^[A-Z0-9\-]+$")]
    private static partial Regex CodePattern();

    private readonly FactoryDbContext _db;
    private readonly IAuditWriter _audit;

    public CustomerService(FactoryDbContext db, IAuditWriter audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<PagedResult<CustomerResponse>> ListAsync(
        CustomerQuery query, CancellationToken ct = default)
    {
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize <= 0 ? 50 : query.PageSize, 1, MaxPageSize);

        var customers = _db.Customers.AsNoTracking();

        if (!query.IncludeInactive)
            customers = customers.Where(c => c.IsActive);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            customers = customers.Where(c =>
                c.Code.Contains(term) || c.Name.Contains(term) ||
                (c.City != null && c.City.Contains(term)) ||
                (c.Phone != null && c.Phone.Contains(term)));
        }

        var totalCount = await customers.CountAsync(ct);

        var items = await customers
            .OrderBy(c => c.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(c => new CustomerResponse(
                c.Id, c.Code, c.Name, c.City, c.Phone, c.Address,
                c.OpeningBalance, c.OpeningBalanceAsOf, c.IsActive, c.Notes, c.CreatedAt))
            .ToListAsync(ct);

        return new PagedResult<CustomerResponse>(items, page, pageSize, totalCount);
    }

    public async Task<CustomerResponse> GetAsync(Guid id, CancellationToken ct = default)
    {
        var customer = await _db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw DomainException.NotFound("Customer", id.ToString());

        return ToResponse(customer);
    }

    public Task<byte[]?> GetRowVersionAsync(Guid id, CancellationToken ct = default) =>
        _db.Customers.AsNoTracking().Where(c => c.Id == id).Select(c => c.RowVersion).FirstOrDefaultAsync(ct)!;

    public async Task<CustomerResponse> CreateAsync(
        CreateCustomerRequest request, CancellationToken ct = default)
    {
        var code = (request.Code ?? string.Empty).Trim().ToUpperInvariant();

        Validate(code, request.Name, request.Phone, request.OpeningBalance);

        if (await _db.Customers.AnyAsync(c => c.Code == code, ct))
            throw DomainException.Conflict(ErrorCodes.DuplicateCode, $"Customer code '{code}' is already in use.");

        var customer = new Customer
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = request.Name.Trim(),
            City = request.City?.Trim(),
            Phone = request.Phone?.Trim(),
            Address = request.Address?.Trim(),
            OpeningBalance = request.OpeningBalance,
            OpeningBalanceAsOf = request.OpeningBalanceAsOf,
            IsActive = true,
            Notes = request.Notes?.Trim(),
            CreatedAt = _db.Clock.GetUtcNow().UtcDateTime,
            CreatedByUserId = _db.CurrentUser.RequireUserId()
        };

        _db.Customers.Add(customer);
        await _db.SaveChangesAsync(ct);

        return ToResponse(customer);
    }

    public async Task<CustomerResponse> UpdateAsync(
        Guid id, UpdateCustomerRequest request, byte[] rowVersion, CancellationToken ct = default)
    {
        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw DomainException.NotFound("Customer", id.ToString());

        var code = (request.Code ?? string.Empty).Trim().ToUpperInvariant();

        Validate(code, request.Name, request.Phone, request.OpeningBalance);

        if (code != customer.Code && await _db.Customers.AnyAsync(c => c.Code == code && c.Id != id, ct))
            throw DomainException.Conflict(ErrorCodes.DuplicateCode, $"Customer code '{code}' is already in use.");

        if (request.OpeningBalance != customer.OpeningBalance &&
            await CustomerBalances.HasTransactionsAsync(_db, id, ct))
        {
            // SL-01. The opening balance is the foundation every later figure sits on.
            // Changing it after trading has started silently rewrites every historical
            // balance, including ones the customer has already been shown.
            throw DomainException.Unprocessable(ErrorCodes.OpeningBalanceLocked, "Opening balance locked",
                $"{customer.Name} already has dispatches or payments, so the opening balance " +
                "cannot be changed. Record an adjusting payment or dispatch instead.");
        }

        var before = new
        {
            customer.Code, customer.Name, customer.City, customer.Phone,
            customer.Address, customer.OpeningBalance
        };

        customer.Code = code;
        customer.Name = request.Name.Trim();
        customer.City = request.City?.Trim();
        customer.Phone = request.Phone?.Trim();
        customer.Address = request.Address?.Trim();
        customer.OpeningBalance = request.OpeningBalance;
        customer.OpeningBalanceAsOf = request.OpeningBalanceAsOf;
        customer.Notes = request.Notes?.Trim();

        _db.Entry(customer).Property(c => c.RowVersion).OriginalValue = rowVersion;

        _audit.Record(nameof(Customer), customer.Id, "Update", before, new
        {
            customer.Code, customer.Name, customer.City, customer.Phone,
            customer.Address, customer.OpeningBalance
        });

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw DomainException.Conflict(ErrorCodes.ConcurrencyConflict,
                "Someone else changed this customer while you were editing it. " +
                "Reload the record and apply your change again.");
        }

        return ToResponse(customer);
    }

    public async Task DeactivateAsync(Guid id, CancellationToken ct = default)
    {
        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw DomainException.NotFound("Customer", id.ToString());

        if (!customer.IsActive)
            return;

        customer.IsActive = false;

        _audit.Record(nameof(Customer), customer.Id, "Deactivate",
            new { IsActive = true }, new { IsActive = false });

        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// RP-02, the report that justifies the system to the owner.
    ///
    /// One call, sorted largest debt first without being asked, because that is the
    /// order the owner reads it in and the first three rows are the whole point.
    /// </summary>
    public async Task<OutstandingResponse> OutstandingAsync(CancellationToken ct = default)
    {
        var today = BusinessDates.Today(_db.Clock);

        var customers = await _db.Customers.AsNoTracking()
            .Select(c => new { c.Id, c.Code, c.Name, c.City, c.Phone, c.OpeningBalance, c.IsActive })
            .ToListAsync(ct);

        var dispatches = await _db.Dispatches.AsNoTracking()
            .Where(d => d.Status == DocumentStatus.Active)
            .GroupBy(d => d.CustomerId)
            .Select(g => new
            {
                CustomerId = g.Key,
                Total = g.Sum(d => d.TotalAmount),
                Last = g.Max(d => (DateOnly?)d.DispatchDate)
            })
            .ToListAsync(ct);

        var payments = await _db.Payments.AsNoTracking()
            .Where(p => p.Status == DocumentStatus.Active)
            .GroupBy(p => p.CustomerId)
            .Select(g => new
            {
                CustomerId = g.Key,
                Total = g.Sum(p => p.Amount),
                Last = g.Max(p => (DateOnly?)p.PaymentDate)
            })
            .ToListAsync(ct);

        var rows = new List<OutstandingRow>();

        foreach (var customer in customers)
        {
            var dispatched = dispatches.FirstOrDefault(d => d.CustomerId == customer.Id);
            var paid = payments.FirstOrDefault(p => p.CustomerId == customer.Id);

            var outstanding = customer.OpeningBalance + (dispatched?.Total ?? 0m) - (paid?.Total ?? 0m);

            // An inactive customer who settled up is not news; one who owes money is,
            // however inactive he has been marked.
            if (!customer.IsActive && outstanding == 0m)
                continue;

            rows.Add(new OutstandingRow(
                customer.Id, customer.Code, customer.Name, customer.City, customer.Phone,
                customer.OpeningBalance, dispatched?.Total ?? 0m, paid?.Total ?? 0m,
                outstanding, dispatched?.Last, paid?.Last,
                paid?.Last is { } lastPaid ? today.DayNumber - lastPaid.DayNumber : 0));
        }

        rows = rows.OrderByDescending(r => r.Outstanding).ThenBy(r => r.Name).ToList();

        return new OutstandingResponse(rows, rows.Sum(r => r.Outstanding), today);
    }

    /// <summary>
    /// SL-07. Opening balance, then every document in date order, with a running balance.
    ///
    /// Cancelled documents are excluded from the running balance but still shown, as
    /// zero-value lines. A customer comparing this to his own file would otherwise find a
    /// gap in the numbering and conclude something was being hidden from him.
    /// </summary>
    public async Task<StatementResponse> StatementAsync(
        Guid id, DateOnly? from, DateOnly? to, CancellationToken ct = default)
    {
        var customer = await _db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw DomainException.NotFound("Customer", id.ToString());

        var today = BusinessDates.Today(_db.Clock);
        var start = from ?? new DateOnly(today.Year, today.Month, 1);
        var end = to ?? today;

        var dispatches = await _db.Dispatches.AsNoTracking()
            .Where(d => d.CustomerId == id)
            .Select(d => new
            {
                d.DispatchDate, d.DispatchNumber, d.TotalAmount, d.Status, d.VehicleNumber
            })
            .ToListAsync(ct);

        var payments = await _db.Payments.AsNoTracking()
            .Where(p => p.CustomerId == id)
            .Select(p => new { p.PaymentDate, p.PaymentNumber, p.Amount, p.Status, p.Method, p.Reference })
            .ToListAsync(ct);

        // Everything before the window collapses into the opening figure, so the
        // statement balances even when the customer asks for one month in isolation.
        var openingBalance = customer.OpeningBalance
            + dispatches.Where(d => d.Status == DocumentStatus.Active && d.DispatchDate < start)
                        .Sum(d => d.TotalAmount)
            - payments.Where(p => p.Status == DocumentStatus.Active && p.PaymentDate < start)
                      .Sum(p => p.Amount);

        var entries = new List<(DateOnly Date, string Type, string Number, string Description,
            decimal? Debit, decimal? Credit, bool IsActive)>();

        foreach (var d in dispatches.Where(d => d.DispatchDate >= start && d.DispatchDate <= end))
        {
            var cancelled = d.Status == DocumentStatus.Cancelled;

            entries.Add((d.DispatchDate, "Dispatch", d.DispatchNumber,
                cancelled
                    ? "Cancelled"
                    : string.IsNullOrWhiteSpace(d.VehicleNumber) ? "Goods dispatched" : $"Vehicle {d.VehicleNumber}",
                cancelled ? 0m : d.TotalAmount, null, !cancelled));
        }

        foreach (var p in payments.Where(p => p.PaymentDate >= start && p.PaymentDate <= end))
        {
            var cancelled = p.Status == DocumentStatus.Cancelled;

            entries.Add((p.PaymentDate, "Payment", p.PaymentNumber,
                cancelled
                    ? "Cancelled"
                    : string.IsNullOrWhiteSpace(p.Reference) ? p.Method.ToString() : $"{p.Method} {p.Reference}",
                null, cancelled ? 0m : p.Amount, !cancelled));
        }

        var running = openingBalance;
        var lines = new List<StatementLine>();

        foreach (var entry in entries.OrderBy(e => e.Date).ThenBy(e => e.Number, StringComparer.Ordinal))
        {
            if (entry.IsActive)
                running += (entry.Debit ?? 0m) - (entry.Credit ?? 0m);

            lines.Add(new StatementLine(
                entry.Date, entry.Type, entry.Number, entry.Description,
                entry.Debit, entry.Credit, running));
        }

        return new StatementResponse(
            customer.Id, customer.Name, start, end, openingBalance, lines, running);
    }

    private static void Validate(string code, string? name, string? phone, decimal openingBalance)
    {
        var errors = new List<FieldError>();

        if (string.IsNullOrWhiteSpace(code))
            errors.Add(new FieldError("code", "Code is required", ErrorCodes.ValidationFailed));
        else if (code.Length > 24)
            errors.Add(new FieldError("code", "Code must be 24 characters or fewer", ErrorCodes.ValidationFailed));
        else if (!CodePattern().IsMatch(code))
            errors.Add(new FieldError("code",
                "Code may contain only capital letters, digits and hyphens", ErrorCodes.ValidationFailed));

        if (string.IsNullOrWhiteSpace(name))
            errors.Add(new FieldError("name", "Name is required", ErrorCodes.ValidationFailed));
        else if (name.Length > 160)
            errors.Add(new FieldError("name", "Name must be 160 characters or fewer", ErrorCodes.ValidationFailed));

        if (phone is { Length: > 24 })
            errors.Add(new FieldError("phone", "Phone must be 24 characters or fewer", ErrorCodes.ValidationFailed));

        // A negative opening balance means the factory owes the customer, which is a
        // legitimate advance. An implausible magnitude is a slipped keystroke.
        if (Math.Abs(openingBalance) > 100_000_000m)
            errors.Add(new FieldError("openingBalance", "Amount is implausibly large", ErrorCodes.ValidationFailed));

        if (errors.Count > 0)
            throw DomainException.Validation(ErrorCodes.ValidationFailed,
                "The customer could not be saved.", errors.ToArray());
    }

    private static CustomerResponse ToResponse(Customer c) => new(
        c.Id, c.Code, c.Name, c.City, c.Phone, c.Address,
        c.OpeningBalance, c.OpeningBalanceAsOf, c.IsActive, c.Notes, c.CreatedAt);
}
