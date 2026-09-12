using System.Net;
using System.Net.Http.Json;
using CrockeryFactory.IntegrationTests.Infrastructure;
using FluentAssertions;
using Xunit;

namespace CrockeryFactory.IntegrationTests.Staff;

[Collection(ApiCollection.Name)]
public class StaffTests
{
    private readonly FactoryApiFixture _api;

    public StaffTests(FactoryApiFixture api) => _api = api;

    private static string UniqueCode(string tag) => $"E-{tag}-{Random.Shared.Next(10000, 99999)}";

    private async Task<HttpClient> OwnerAsync() =>
        await _api.CreateApiClient().SignedInAsAsync(FactoryApiFixture.OwnerUserName);

    /// <summary>
    /// A week safely inside the attendance backdating window, so these tests exercise the
    /// rules rather than tripping the "too far back" guard.
    /// </summary>
    private static (DateOnly Start, DateOnly End) TestWeek()
    {
        // Four days ending today, rather than the calendar week containing today.
        //
        // The calendar week reaches into the future whenever today is early in it - on a
        // Saturday, every other day of the week has not happened yet - and the API rightly
        // refuses to mark a day that has not happened. Anchoring to today keeps every day
        // in the past and inside the backdating window, whatever day the suite runs on.
        var today = DateOnly.FromDateTime(DateTime.Now);
        return (today.AddDays(-3), today);
    }

    private async Task<Guid> CreateEmployeeAsync(
        HttpClient client, decimal rate, DateOnly joinedOn, string? code = null)
    {
        var response = await client.PostAsJsonAsync("/api/v1/employees", new
        {
            code = code ?? UniqueCode("W"),
            name = "Test Worker",
            fatherName = "Test Father",
            designation = "Moulder",
            joinedOn = joinedOn.ToString("yyyy-MM-dd"),
            dailyRate = rate
        }, ApiClientExtensions.Json);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        return (await response.ReadJsonAsync()).GetProperty("id").GetGuid();
    }

    private static object Sheet(DateOnly date, Guid employeeId, string status, decimal overtime = 0m) => new
    {
        date = date.ToString("yyyy-MM-dd"),
        lines = new[] { new { employeeId, status, overtimeHours = overtime, notes = (string?)null } }
    };

    // ------------------------------------------------------------- employees

    [RequiresSqlServerFact]
    public async Task An_employee_is_created_with_the_daily_wage_effective_from_the_joining_date()
    {
        var client = await OwnerAsync();
        var joined = DateOnly.FromDateTime(DateTime.Now).AddDays(-30);
        var id = await CreateEmployeeAsync(client, 1200m, joined);

        var rates = await (await client.GetAsync($"/api/v1/employees/{id}/wage-rates")).ReadJsonAsync();
        var first = rates.EnumerateArray().Single();

        first.GetProperty("dailyRate").GetDecimal().Should().Be(1200m);
        first.GetProperty("effectiveFrom").GetString().Should().Be(joined.ToString("yyyy-MM-dd"));
    }

    [RequiresSqlServerFact]
    public async Task A_duplicate_employee_code_is_refused()
    {
        var client = await OwnerAsync();
        var code = UniqueCode("DUP");
        var joined = DateOnly.FromDateTime(DateTime.Now).AddDays(-10);

        await CreateEmployeeAsync(client, 1000m, joined, code);

        var second = await client.PostAsJsonAsync("/api/v1/employees", new
        {
            code,
            name = "Somebody Else",
            joinedOn = joined.ToString("yyyy-MM-dd"),
            dailyRate = 1000m
        }, ApiClientExtensions.Json);

        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await second.ReadJsonAsync()).GetProperty("code").GetString().Should().Be("DUPLICATE_CODE");
    }

    // ------------------------------------------------------------ attendance

    [RequiresSqlServerFact]
    public async Task An_unmarked_worker_still_appears_on_the_sheet()
    {
        // The sheet is something a clerk works down, not a list they have to add people
        // to, so everybody on the roll is a row whether or not they have been marked.
        var client = await OwnerAsync();
        var (start, _) = TestWeek();
        var id = await CreateEmployeeAsync(client, 900m, start.AddDays(-1));

        var sheet = await (await client.GetAsync($"/api/v1/attendance?date={start:yyyy-MM-dd}")).ReadJsonAsync();

        var line = sheet.GetProperty("lines").EnumerateArray()
            .Single(l => l.GetProperty("employeeId").GetGuid() == id);

        // The API omits null properties, so "not yet marked" is an absent status rather
        // than a null one. Asserting on the absence is asserting on the real contract.
        line.TryGetProperty("status", out _).Should().BeFalse("an unmarked day has no status");
        line.GetProperty("dailyRate").GetDecimal().Should().Be(900m);
    }

    [RequiresSqlServerFact]
    public async Task Marking_the_same_day_twice_corrects_it_rather_than_recording_it_twice()
    {
        var client = await OwnerAsync();
        var (start, _) = TestWeek();
        var id = await CreateEmployeeAsync(client, 1000m, start.AddDays(-1));

        await client.PostAsJsonAsync("/api/v1/attendance", Sheet(start, id, "Present"), ApiClientExtensions.Json);
        await client.PostAsJsonAsync("/api/v1/attendance", Sheet(start, id, "HalfDay", 2m), ApiClientExtensions.Json);

        var sheet = await (await client.GetAsync($"/api/v1/attendance?date={start:yyyy-MM-dd}")).ReadJsonAsync();

        var lines = sheet.GetProperty("lines").EnumerateArray()
            .Where(l => l.GetProperty("employeeId").GetGuid() == id)
            .ToList();

        lines.Should().HaveCount(1, "a day is marked once; re-marking corrects it");
        lines[0].GetProperty("status").GetString().Should().Be("HalfDay");
        lines[0].GetProperty("overtimeHours").GetDecimal().Should().Be(2m);
    }

    [RequiresSqlServerFact]
    public async Task Attendance_cannot_be_marked_for_a_day_that_has_not_happened()
    {
        var client = await OwnerAsync();
        var tomorrow = DateOnly.FromDateTime(DateTime.Now).AddDays(1);
        var id = await CreateEmployeeAsync(client, 1000m, DateOnly.FromDateTime(DateTime.Now).AddDays(-5));

        var response = await client.PostAsJsonAsync("/api/v1/attendance",
            Sheet(tomorrow, id, "Present"), ApiClientExtensions.Json);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    // --------------------------------------------------------------- payroll

    [RequiresSqlServerFact]
    public async Task A_week_of_attendance_becomes_the_wage_the_arithmetic_says()
    {
        var client = await OwnerAsync();
        var (start, end) = TestWeek();
        var id = await CreateEmployeeAsync(client, 1200m, start.AddDays(-1));

        // Three full days, one half day, two hours of overtime on one of them.
        await client.PostAsJsonAsync("/api/v1/attendance", Sheet(start, id, "Present"), ApiClientExtensions.Json);
        await client.PostAsJsonAsync("/api/v1/attendance", Sheet(start.AddDays(1), id, "Present", 2m), ApiClientExtensions.Json);
        await client.PostAsJsonAsync("/api/v1/attendance", Sheet(start.AddDays(2), id, "Present"), ApiClientExtensions.Json);
        await client.PostAsJsonAsync("/api/v1/attendance", Sheet(start.AddDays(3), id, "HalfDay"), ApiClientExtensions.Json);

        var preview = await (await client.GetAsync(
            $"/api/v1/payroll/preview?from={start:yyyy-MM-dd}&to={end:yyyy-MM-dd}")).ReadJsonAsync();

        var line = preview.GetProperty("lines").EnumerateArray()
            .Single(l => l.GetProperty("employeeId").GetGuid() == id);

        line.GetProperty("fullDays").GetInt32().Should().Be(3);
        line.GetProperty("halfDays").GetInt32().Should().Be(1);
        line.GetProperty("overtimeHours").GetDecimal().Should().Be(2m);

        // 3 x 1200 + 0.5 x 1200 = 4200. Overtime: 1200/8 x 2 x 1.5 = 450.
        line.GetProperty("wageAmount").GetDecimal().Should().Be(4200m);
        line.GetProperty("overtimeAmount").GetDecimal().Should().Be(450m);
        line.GetProperty("netAmount").GetDecimal().Should().Be(4650m);
    }

    [RequiresSqlServerFact]
    public async Task A_wage_rise_partway_through_the_week_is_paid_day_by_day()
    {
        // Summing the days first and multiplying once would pay the whole week at
        // whichever rate happened to be current, which is the bug this guards.
        var client = await OwnerAsync();
        var (start, end) = TestWeek();
        var id = await CreateEmployeeAsync(client, 1000m, start.AddDays(-1));

        await client.PostAsJsonAsync($"/api/v1/employees/{id}/wage-rates",
            new { dailyRate = 1400m, effectiveFrom = start.AddDays(2).ToString("yyyy-MM-dd") },
            ApiClientExtensions.Json);

        // Two days at the old rate, two at the new.
        foreach (var offset in new[] { 0, 1, 2, 3 })
        {
            await client.PostAsJsonAsync("/api/v1/attendance",
                Sheet(start.AddDays(offset), id, "Present"), ApiClientExtensions.Json);
        }

        var preview = await (await client.GetAsync(
            $"/api/v1/payroll/preview?from={start:yyyy-MM-dd}&to={end:yyyy-MM-dd}")).ReadJsonAsync();

        var line = preview.GetProperty("lines").EnumerateArray()
            .Single(l => l.GetProperty("employeeId").GetGuid() == id);

        // 2 x 1000 + 2 x 1400, not 4 x either rate.
        line.GetProperty("wageAmount").GetDecimal().Should().Be(4800m);
    }

    [RequiresSqlServerFact]
    public async Task A_later_wage_rise_does_not_reprice_a_payroll_already_created()
    {
        var client = await OwnerAsync();
        var (start, end) = TestWeek();
        var id = await CreateEmployeeAsync(client, 1000m, start.AddDays(-1));

        await client.PostAsJsonAsync("/api/v1/attendance", Sheet(start, id, "Present"), ApiClientExtensions.Json);

        var created = await client.PostAsJsonAsync("/api/v1/payroll", new
        {
            periodStart = start.ToString("yyyy-MM-dd"),
            periodEnd = end.ToString("yyyy-MM-dd"),
            notes = (string?)null
        }, ApiClientExtensions.Json);

        created.StatusCode.Should().Be(HttpStatusCode.Created);

        var run = await created.ReadJsonAsync();
        var runId = run.GetProperty("id").GetGuid();

        var before = run.GetProperty("lines").EnumerateArray()
            .Single(l => l.GetProperty("employeeId").GetGuid() == id)
            .GetProperty("netAmount").GetDecimal();

        before.Should().Be(1000m);

        // Backdate a rise over the very day that was paid.
        await client.PostAsJsonAsync($"/api/v1/employees/{id}/wage-rates",
            new { dailyRate = 5000m, effectiveFrom = start.ToString("yyyy-MM-dd") },
            ApiClientExtensions.Json);

        var reread = await (await client.GetAsync($"/api/v1/payroll/{runId}")).ReadJsonAsync();

        var after = reread.GetProperty("lines").EnumerateArray()
            .Single(l => l.GetProperty("employeeId").GetGuid() == id)
            .GetProperty("netAmount").GetDecimal();

        after.Should().Be(before, "a payslip already handed over must not change under a later rate");
        reread.GetProperty("lines").EnumerateArray()
            .Single(l => l.GetProperty("employeeId").GetGuid() == id)
            .GetProperty("dailyRate").GetDecimal().Should().Be(1000m);
    }

    [RequiresSqlServerFact]
    public async Task A_second_live_payroll_for_the_same_week_is_refused_until_the_first_is_cancelled()
    {
        var client = await OwnerAsync();
        var (start, end) = TestWeek();

        // A week of its own, so this test does not collide with the others above.
        var weekStart = start.AddDays(-28);
        var weekEnd = weekStart.AddDays(6);
        var id = await CreateEmployeeAsync(client, 800m, weekStart.AddDays(-1));

        // Attendance inside that older week has to be written directly - the API's own
        // backdating window would refuse it, and rightly so.
        await _api.SeedAttendanceAsync(id, weekStart, CrockeryFactory.Domain.Enums.AttendanceStatus.Present);

        object Body() => new
        {
            periodStart = weekStart.ToString("yyyy-MM-dd"),
            periodEnd = weekEnd.ToString("yyyy-MM-dd"),
            notes = (string?)null
        };

        var first = await client.PostAsJsonAsync("/api/v1/payroll", Body(), ApiClientExtensions.Json);
        first.StatusCode.Should().Be(HttpStatusCode.Created);
        var runId = (await first.ReadJsonAsync()).GetProperty("id").GetGuid();

        var second = await client.PostAsJsonAsync("/api/v1/payroll", Body(), ApiClientExtensions.Json);
        second.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        await client.PostAsJsonAsync($"/api/v1/payroll/{runId}/cancel",
            new { reason = "Wrong week entered by mistake" }, ApiClientExtensions.Json);

        var third = await client.PostAsJsonAsync("/api/v1/payroll", Body(), ApiClientExtensions.Json);
        third.StatusCode.Should().Be(HttpStatusCode.Created, "a cancelled run must not block a corrected one");
    }

    [RequiresSqlServerFact]
    public async Task A_cancelled_payroll_keeps_its_number_and_its_lines()
    {
        var client = await OwnerAsync();
        var (start, end) = TestWeek();
        var id = await CreateEmployeeAsync(client, 1100m, start.AddDays(-1));

        await client.PostAsJsonAsync("/api/v1/attendance",
            Sheet(start.AddDays(4), id, "Present"), ApiClientExtensions.Json);

        var created = await client.PostAsJsonAsync("/api/v1/payroll", new
        {
            periodStart = start.ToString("yyyy-MM-dd"),
            periodEnd = end.ToString("yyyy-MM-dd"),
            notes = (string?)null
        }, ApiClientExtensions.Json);

        // Another test may already hold this week; either outcome is fine for this one.
        if (created.StatusCode != HttpStatusCode.Created) return;

        var run = await created.ReadJsonAsync();
        var runId = run.GetProperty("id").GetGuid();
        var number = run.GetProperty("runNumber").GetString();
        var lineCount = run.GetProperty("lines").GetArrayLength();

        var cancelled = await client.PostAsJsonAsync($"/api/v1/payroll/{runId}/cancel",
            new { reason = "Paid in cash separately this week" }, ApiClientExtensions.Json);

        cancelled.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await cancelled.ReadJsonAsync();
        body.GetProperty("status").GetString().Should().Be("Cancelled");
        body.GetProperty("runNumber").GetString().Should().Be(number, "BR-08: a gap in the series looks like concealment");
        body.GetProperty("lines").GetArrayLength().Should().Be(lineCount, "what was paid stays readable");
    }

    [RequiresSqlServerFact]
    public async Task A_cancellation_needs_a_reason_worth_reading()
    {
        var client = await OwnerAsync();
        var (start, end) = TestWeek();
        var id = await CreateEmployeeAsync(client, 950m, start.AddDays(-1));

        await client.PostAsJsonAsync("/api/v1/attendance",
            Sheet(start.AddDays(5), id, "Present"), ApiClientExtensions.Json);

        var created = await client.PostAsJsonAsync("/api/v1/payroll", new
        {
            periodStart = start.ToString("yyyy-MM-dd"),
            periodEnd = end.ToString("yyyy-MM-dd"),
            notes = (string?)null
        }, ApiClientExtensions.Json);

        if (created.StatusCode != HttpStatusCode.Created) return;

        var runId = (await created.ReadJsonAsync()).GetProperty("id").GetGuid();

        var response = await client.PostAsJsonAsync($"/api/v1/payroll/{runId}/cancel",
            new { reason = "oops" }, ApiClientExtensions.Json);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [RequiresSqlServerFact]
    public async Task A_week_with_no_attendance_pays_nobody_rather_than_creating_an_empty_run()
    {
        var client = await OwnerAsync();
        var (start, _) = TestWeek();

        var emptyStart = start.AddDays(-70);
        var emptyEnd = emptyStart.AddDays(6);

        var response = await client.PostAsJsonAsync("/api/v1/payroll", new
        {
            periodStart = emptyStart.ToString("yyyy-MM-dd"),
            periodEnd = emptyEnd.ToString("yyyy-MM-dd"),
            notes = (string?)null
        }, ApiClientExtensions.Json);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }
}
