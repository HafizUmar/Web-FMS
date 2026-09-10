using System.Net;
using System.Net.Http.Json;
using CrockeryFactory.Application.Common;
using CrockeryFactory.IntegrationTests.Infrastructure;
using FluentAssertions;
using Xunit;

namespace CrockeryFactory.IntegrationTests.Sales;

[Collection(ApiCollection.Name)]
public class PaymentAndStatementTests
{
    private readonly FactoryScenario _scenario;

    public PaymentAndStatementTests(FactoryApiFixture api) => _scenario = new FactoryScenario(api);

    private static object Payment(Guid customerId, decimal amount, string method = "Cash",
        string? reference = null, DateOnly? date = null) => new
    {
        customerId,
        paymentDate = (date ?? FactoryScenario.Today()).ToString("yyyy-MM-dd"),
        amount,
        method,
        reference
    };

    [RequiresSqlServerFact]
    public async Task A_payment_reduces_what_the_customer_owes()
    {
        var owner = await _scenario.OwnerAsync();
        var product = await _scenario.CreateProductAsync(owner, "PAY", firstRate: 100m);
        var customer = await _scenario.CreateCustomerAsync(owner, "PAY");

        await _scenario.RecordProductionAsync(owner, product, good: 500);
        await _scenario.DispatchAsync(owner, customer, [FactoryScenario.Line(product, "First", 200)]);

        var response = await owner.PostAsJsonAsync("/api/v1/payments",
            Payment(customer, 15_000m), ApiClientExtensions.Json);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await response.ReadJsonAsync();
        body.GetProperty("paymentNumber").GetString().Should().StartWith("R-");
        body.GetProperty("customerBalanceAfter").GetDecimal().Should().Be(5_000m);

        (await _scenario.BalanceAsync(owner, customer)).Should().Be(5_000m);
    }

    [RequiresSqlServerFact]
    public async Task An_overpayment_warns_but_is_accepted()
    {
        // Advances are normal in this trade and a negative balance is a legitimate
        // state. Blocking it would force the clerk to lie about the amount.
        var owner = await _scenario.OwnerAsync();
        var customer = await _scenario.CreateCustomerAsync(owner, "ADVANCE");

        var response = await owner.PostAsJsonAsync("/api/v1/payments",
            Payment(customer, 50_000m), ApiClientExtensions.Json);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await response.ReadJsonAsync();
        body.GetProperty("warnings").EnumerateArray().Select(w => w.GetString())
            .Should().Contain(WarningCodes.PaymentExceedsOutstanding);
        body.GetProperty("customerBalanceAfter").GetDecimal().Should().Be(-50_000m);
    }

    [RequiresSqlServerTheory]
    [InlineData("Cheque")]
    [InlineData("BankTransfer")]
    public async Task A_cheque_or_transfer_needs_a_reference(string method)
    {
        // A cash receipt is its own record. A cheque is a claim about something in a
        // bank, and without the number nobody can check it later.
        var owner = await _scenario.OwnerAsync();
        var customer = await _scenario.CreateCustomerAsync(owner, "REF");

        var response = await owner.PostAsJsonAsync("/api/v1/payments",
            Payment(customer, 5_000m, method), ApiClientExtensions.Json);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.ErrorCodeAsync()).Should().Be(ErrorCodes.ReferenceRequired);
    }

    [RequiresSqlServerFact]
    public async Task Cash_needs_no_reference()
    {
        var owner = await _scenario.OwnerAsync();
        var customer = await _scenario.CreateCustomerAsync(owner, "CASH");

        (await owner.PostAsJsonAsync("/api/v1/payments",
            Payment(customer, 5_000m), ApiClientExtensions.Json))
            .StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [RequiresSqlServerTheory]
    [InlineData(0)]
    [InlineData(-100)]
    public async Task A_payment_must_be_for_more_than_zero(decimal amount)
    {
        var owner = await _scenario.OwnerAsync();
        var customer = await _scenario.CreateCustomerAsync(owner, "ZEROPAY");

        var response = await owner.PostAsJsonAsync("/api/v1/payments",
            Payment(customer, amount), ApiClientExtensions.Json);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.ErrorCodeAsync()).Should().Be(ErrorCodes.AmountInvalid);
    }

    [RequiresSqlServerFact]
    public async Task Cancelling_a_receipt_puts_the_money_back_on_the_account()
    {
        var owner = await _scenario.OwnerAsync();
        var product = await _scenario.CreateProductAsync(owner, "PAYCANCEL", firstRate: 100m);
        var customer = await _scenario.CreateCustomerAsync(owner, "PAYCANCEL");

        await _scenario.RecordProductionAsync(owner, product, good: 500);
        await _scenario.DispatchAsync(owner, customer, [FactoryScenario.Line(product, "First", 100)]);

        var paymentId = (await (await owner.PostAsJsonAsync("/api/v1/payments",
            Payment(customer, 4_000m), ApiClientExtensions.Json)).ReadJsonAsync())
            .GetProperty("id").GetGuid();

        (await _scenario.BalanceAsync(owner, customer)).Should().Be(6_000m);

        var response = await owner.PostAsJsonAsync($"/api/v1/payments/{paymentId}/cancel",
            new { reason = "Cheque was returned unpaid by the bank" }, ApiClientExtensions.Json);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await _scenario.BalanceAsync(owner, customer)).Should().Be(10_000m);
    }

    [RequiresSqlServerFact]
    public async Task The_opening_balance_is_locked_once_trading_starts()
    {
        // SL-01. Changing it afterwards silently rewrites every historical balance,
        // including ones the customer has already been shown.
        var owner = await _scenario.OwnerAsync();
        var customer = await _scenario.CreateCustomerAsync(owner, "LOCKED", openingBalance: 20_000m);

        await owner.PostAsJsonAsync("/api/v1/payments", Payment(customer, 5_000m), ApiClientExtensions.Json);

        var current = await owner.GetAsync($"/api/v1/customers/{customer}");
        var etag = current.Headers.ETag!.ToString();
        var body = await current.ReadJsonAsync();

        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/customers/{customer}")
        {
            Content = JsonContent.Create(new
            {
                code = body.GetProperty("code").GetString(),
                name = body.GetProperty("name").GetString(),
                openingBalance = 99_000m
            }, options: ApiClientExtensions.Json)
        };
        request.Headers.TryAddWithoutValidation("If-Match", etag);

        var response = await owner.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.ErrorCodeAsync()).Should().Be(ErrorCodes.OpeningBalanceLocked);
    }

    [RequiresSqlServerFact]
    public async Task Other_details_can_still_be_corrected_after_trading_starts()
    {
        var owner = await _scenario.OwnerAsync();
        var customer = await _scenario.CreateCustomerAsync(owner, "RENAME", openingBalance: 5_000m);

        await owner.PostAsJsonAsync("/api/v1/payments", Payment(customer, 1_000m), ApiClientExtensions.Json);

        var current = await owner.GetAsync($"/api/v1/customers/{customer}");
        var body = await current.ReadJsonAsync();

        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/customers/{customer}")
        {
            Content = JsonContent.Create(new
            {
                code = body.GetProperty("code").GetString(),
                name = "Corrected Trading Name",
                phone = "03009999999",
                openingBalance = 5_000m
            }, options: ApiClientExtensions.Json)
        };
        request.Headers.TryAddWithoutValidation("If-Match", current.Headers.ETag!.ToString());

        var response = await owner.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.ReadJsonAsync()).GetProperty("name").GetString()
            .Should().Be("Corrected Trading Name");
    }

    [RequiresSqlServerFact]
    public async Task The_statement_runs_from_the_opening_balance_to_the_closing_one()
    {
        var owner = await _scenario.OwnerAsync();
        var product = await _scenario.CreateProductAsync(owner, "STMT", firstRate: 100m);
        var customer = await _scenario.CreateCustomerAsync(owner, "STMT", openingBalance: 3_000m);

        await _scenario.RecordProductionAsync(owner, product, good: 1000);
        await _scenario.DispatchAsync(owner, customer, [FactoryScenario.Line(product, "First", 100)]);
        await owner.PostAsJsonAsync("/api/v1/payments", Payment(customer, 6_000m), ApiClientExtensions.Json);

        var from = FactoryScenario.Today().AddDays(-7).ToString("yyyy-MM-dd");
        var to = FactoryScenario.Today().ToString("yyyy-MM-dd");

        var statement = await (await owner.GetAsync(
            $"/api/v1/customers/{customer}/statement?from={from}&to={to}")).ReadJsonAsync();

        statement.GetProperty("openingBalance").GetDecimal().Should().Be(3_000m);
        statement.GetProperty("closingBalance").GetDecimal().Should().Be(7_000m);

        var lines = statement.GetProperty("lines").EnumerateArray().ToList();
        lines.Should().HaveCount(2);
        lines.Should().Contain(l => l.GetProperty("documentType").GetString() == "Dispatch");
        lines.Should().Contain(l => l.GetProperty("documentType").GetString() == "Payment");

        // The closing balance must equal the last running balance, or the statement does
        // not add up in front of the customer holding it.
        lines.Last().GetProperty("runningBalance").GetDecimal().Should().Be(7_000m);
    }

    [RequiresSqlServerFact]
    public async Task A_cancelled_document_is_shown_as_a_zero_line_rather_than_omitted()
    {
        // A customer comparing this to his own file would otherwise find a gap in the
        // numbering and conclude something was being hidden from him.
        var owner = await _scenario.OwnerAsync();
        var product = await _scenario.CreateProductAsync(owner, "STMTCANCEL", firstRate: 100m);
        var customer = await _scenario.CreateCustomerAsync(owner, "STMTCANCEL");

        await _scenario.RecordProductionAsync(owner, product, good: 500);

        var dispatchId = (await (await _scenario.DispatchAsync(owner, customer,
            [FactoryScenario.Line(product, "First", 100)])).ReadJsonAsync())
            .GetProperty("id").GetGuid();

        await owner.PostAsJsonAsync($"/api/v1/dispatches/{dispatchId}/cancel",
            new { reason = "Customer refused the consignment at the gate" }, ApiClientExtensions.Json);

        var from = FactoryScenario.Today().AddDays(-7).ToString("yyyy-MM-dd");
        var to = FactoryScenario.Today().ToString("yyyy-MM-dd");

        var statement = await (await owner.GetAsync(
            $"/api/v1/customers/{customer}/statement?from={from}&to={to}")).ReadJsonAsync();

        var line = statement.GetProperty("lines").EnumerateArray().Single();

        line.GetProperty("description").GetString().Should().Be("Cancelled");
        line.GetProperty("debit").GetDecimal().Should().Be(0m);
        line.GetProperty("runningBalance").GetDecimal().Should().Be(0m);
        statement.GetProperty("closingBalance").GetDecimal().Should().Be(0m);
    }

    [RequiresSqlServerFact]
    public async Task The_outstanding_report_is_sorted_largest_debt_first()
    {
        var owner = await _scenario.OwnerAsync();

        var small = await _scenario.CreateCustomerAsync(owner, "SMALLDEBT", openingBalance: 1_000m);
        var large = await _scenario.CreateCustomerAsync(owner, "LARGEDEBT", openingBalance: 900_000m);

        var body = await (await owner.GetAsync("/api/v1/customers/outstanding")).ReadJsonAsync();
        var rows = body.GetProperty("rows").EnumerateArray().ToList();

        var largeIndex = rows.FindIndex(r => r.GetProperty("customerId").GetGuid() == large);
        var smallIndex = rows.FindIndex(r => r.GetProperty("customerId").GetGuid() == small);

        largeIndex.Should().BeLessThan(smallIndex, "the owner reads the first three rows");
        body.GetProperty("totalOutstanding").GetDecimal().Should().BeGreaterThanOrEqualTo(901_000m);
    }

    [RequiresSqlServerFact]
    public async Task The_outstanding_row_reports_dispatched_and_paid_separately()
    {
        var owner = await _scenario.OwnerAsync();
        var product = await _scenario.CreateProductAsync(owner, "OUTROW", firstRate: 100m);
        var customer = await _scenario.CreateCustomerAsync(owner, "OUTROW", openingBalance: 2_000m);

        await _scenario.RecordProductionAsync(owner, product, good: 1000);
        await _scenario.DispatchAsync(owner, customer, [FactoryScenario.Line(product, "First", 300)]);
        await owner.PostAsJsonAsync("/api/v1/payments", Payment(customer, 12_000m), ApiClientExtensions.Json);

        var body = await (await owner.GetAsync("/api/v1/customers/outstanding")).ReadJsonAsync();

        var row = body.GetProperty("rows").EnumerateArray()
            .Single(r => r.GetProperty("customerId").GetGuid() == customer);

        row.GetProperty("openingBalance").GetDecimal().Should().Be(2_000m);
        row.GetProperty("totalDispatched").GetDecimal().Should().Be(30_000m);
        row.GetProperty("totalPaid").GetDecimal().Should().Be(12_000m);
        row.GetProperty("outstanding").GetDecimal().Should().Be(20_000m);
        row.GetProperty("lastPaymentDate").GetString().Should().NotBeNull();
    }
}
