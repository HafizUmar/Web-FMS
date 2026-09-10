using System.Net;
using System.Net.Http.Json;
using CrockeryFactory.Application.Common;
using CrockeryFactory.IntegrationTests.Infrastructure;
using FluentAssertions;
using Xunit;

namespace CrockeryFactory.IntegrationTests.Admin;

[Collection(ApiCollection.Name)]
public class AdministrationTests
{
    private readonly FactoryApiFixture _api;
    private readonly FactoryScenario _scenario;

    public AdministrationTests(FactoryApiFixture api)
    {
        _api = api;
        _scenario = new FactoryScenario(api);
    }

    private Task<HttpClient> AdminAsync() =>
        _api.CreateApiClient().SignedInAsAsync(FactoryApiFixture.AdminUserName);

    [RequiresSqlServerFact]
    public async Task Health_is_reachable_without_signing_in()
    {
        // Called by the Setup tool before anyone has an account, and in a support call.
        var response = await _api.CreateApiClient().GetAsync("/api/v1/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.ReadJsonAsync();
        body.GetProperty("databaseReachable").GetBoolean().Should().BeTrue();
        body.GetProperty("migrationsCurrent").GetBoolean().Should().BeTrue();
        body.GetProperty("status").GetString().Should().Be("Healthy");
    }

    [RequiresSqlServerFact]
    public async Task Health_says_nothing_about_the_data()
    {
        // An anonymous endpoint that leaks row counts or a connection string is a
        // reconnaissance endpoint.
        var raw = await (await _api.CreateApiClient().GetAsync("/api/v1/health"))
            .Content.ReadAsStringAsync();

        raw.ToLowerInvariant().Should().NotContainAny("password", "server=", "user id", "connection");
    }

    [RequiresSqlServerFact]
    public async Task Rebuilding_balances_from_an_intact_ledger_changes_nothing()
    {
        var owner = await _scenario.OwnerAsync();
        var product = await _scenario.CreateProductAsync(owner, "REBUILD", firstRate: 100m);
        var customer = await _scenario.CreateCustomerAsync(owner, "REBUILD");

        await _scenario.RecordProductionAsync(owner, product, good: 800, seconds: 90);
        await _scenario.DispatchAsync(owner, customer, [FactoryScenario.Line(product, "First", 250)]);

        var before = await _scenario.StockAsync(owner, product, "First");

        var admin = await AdminAsync();
        var response = await admin.PostAsync("/api/v1/admin/rebuild-stock-balances", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.ReadJsonAsync();
        body.GetProperty("balancesCorrected").GetInt32().Should().Be(0);
        body.GetProperty("balancesInserted").GetInt32().Should().Be(0);
        body.GetProperty("balancesRemoved").GetInt32().Should().Be(0);

        // And nothing moved.
        (await _scenario.StockAsync(owner, product, "First")).Should().Be(before);
    }

    [RequiresSqlServerFact]
    public async Task The_rebuild_is_recorded_in_the_audit_even_when_it_finds_nothing()
    {
        // "We ran the rebuild and it found nothing" is exactly as useful in a support
        // call as a list of corrections.
        var admin = await AdminAsync();

        await admin.PostAsync("/api/v1/admin/rebuild-stock-balances", null);

        var audit = await (await admin.GetAsync("/api/v1/audit?entityName=StockBalance")).ReadJsonAsync();

        audit.GetProperty("totalCount").GetInt32().Should().BeGreaterThan(0);
        audit.GetProperty("items").EnumerateArray()
            .Should().Contain(a => a.GetProperty("action").GetString() == "Rebuild");
    }

    [RequiresSqlServerFact]
    public async Task A_clerk_cannot_rebuild_balances()
    {
        var clerk = await _scenario.ClerkAsync();

        (await clerk.PostAsync("/api/v1/admin/rebuild-stock-balances", null))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [RequiresSqlServerFact]
    public async Task A_clerk_cannot_read_the_audit_trail()
    {
        var clerk = await _scenario.ClerkAsync();

        (await clerk.GetAsync("/api/v1/audit")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [RequiresSqlServerFact]
    public async Task A_price_change_leaves_an_audit_row_naming_the_old_and_new_rate()
    {
        // BR-07 with SE-13: this is the row someone reads when a bill is disputed.
        var owner = await _scenario.OwnerAsync();
        var product = await _scenario.CreateProductAsync(owner, "AUDITPRICE", firstRate: 100m);

        await owner.PutAsJsonAsync($"/api/v1/products/{product}/prices", new
        {
            prices = new[] { new { grade = "First", unitRate = 180m } },
            effectiveFrom = FactoryScenario.Today().ToString("yyyy-MM-dd"),
            reason = "Clay and fuel both up this quarter"
        }, ApiClientExtensions.Json);

        var admin = await AdminAsync();
        var audit = await (await admin.GetAsync(
            $"/api/v1/audit?entityName=ProductPrice&entityId={product}")).ReadJsonAsync();

        var entry = audit.GetProperty("items").EnumerateArray()
            .Single(a => a.GetProperty("action").GetString() == "UpdatePrices");

        entry.GetProperty("oldValues").GetString().Should().Contain("100");
        entry.GetProperty("newValues").GetString().Should().Contain("180");
        entry.GetProperty("newValues").GetString().Should().Contain("Clay and fuel");
        entry.GetProperty("userName").GetString().Should().Be("Owner Sahib");
    }

    [RequiresSqlServerFact]
    public async Task Reason_codes_come_back_grouped_by_list()
    {
        var clerk = await _scenario.ClerkAsync();

        var breakage = await (await clerk.GetAsync("/api/v1/reason-codes?type=Breakage")).ReadJsonAsync();

        breakage.GetArrayLength().Should().Be(5);
        breakage.EnumerateArray().Should().OnlyContain(r => r.GetProperty("type").GetString() == "Breakage");
    }

    [RequiresSqlServerFact]
    public async Task The_same_code_is_allowed_in_two_different_lists()
    {
        // "OTHER" is a legitimate entry in every one of them.
        var clerk = await _scenario.ClerkAsync();

        var all = await (await clerk.GetAsync("/api/v1/reason-codes")).ReadJsonAsync();

        all.EnumerateArray().Count(r => r.GetProperty("code").GetString() == "OTHER")
            .Should().BeGreaterThan(1);
    }

    [RequiresSqlServerFact]
    public async Task A_clerk_cannot_add_a_reason_code()
    {
        var clerk = await _scenario.ClerkAsync();

        var response = await clerk.PostAsJsonAsync("/api/v1/reason-codes", new
        {
            type = "Breakage",
            code = "SNEAK",
            description = "Added by a clerk",
            sortOrder = 50
        }, ApiClientExtensions.Json);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [RequiresSqlServerFact]
    public async Task An_unknown_setting_key_is_refused_rather_than_silently_stored()
    {
        // Accepting it would store a value that nothing ever reads, and the owner would
        // believe he had changed something.
        var admin = await AdminAsync();

        var response = await admin.PutAsJsonAsync("/api/v1/settings", new
        {
            values = new Dictionary<string, string> { ["Factory.Nmae"] = "Typo Ltd" }
        }, ApiClientExtensions.Json);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.ReadJsonAsync()).GetProperty("detail").GetString()
            .Should().Contain("Factory.Nmae");
    }

    [RequiresSqlServerFact]
    public async Task Changing_a_setting_takes_effect_immediately()
    {
        // A clerk told a grade is not enabled, who waits for the owner to enable it and
        // is told the same thing for another minute, concludes the system is broken.
        var owner = await _scenario.OwnerAsync();
        var admin = await AdminAsync();

        var refused = await owner.PostAsJsonAsync("/api/v1/products", new
        {
            code = $"CUP-G3-{Random.Shared.Next(1000, 9999)}",
            name = "Third grade",
            prices = new[] { new { grade = "Third", unitRate = 30m } }
        }, ApiClientExtensions.Json);

        refused.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        await admin.PutAsJsonAsync("/api/v1/settings", new
        {
            values = new Dictionary<string, string> { ["Grades.Enabled"] = "1,2,3" }
        }, ApiClientExtensions.Json);

        var accepted = await owner.PostAsJsonAsync("/api/v1/products", new
        {
            code = $"CUP-G3-{Random.Shared.Next(1000, 9999)}",
            name = "Third grade",
            prices = new[] { new { grade = "Third", unitRate = 30m } }
        }, ApiClientExtensions.Json);

        accepted.StatusCode.Should().Be(HttpStatusCode.Created);

        // Put it back so the shared database stays as the other tests expect it.
        await admin.PutAsJsonAsync("/api/v1/settings", new
        {
            values = new Dictionary<string, string> { ["Grades.Enabled"] = "1,2" }
        }, ApiClientExtensions.Json);
    }

    [RequiresSqlServerFact]
    public async Task Deactivating_a_user_ends_their_session_on_the_next_request()
    {
        // Without this a deactivated user keeps working until their cookie expires - up
        // to a full day under SE-06, which is not what "deactivate" means.
        var admin = await AdminAsync();

        var created = await admin.PostAsJsonAsync("/api/v1/users", new
        {
            userName = $"temp{Random.Shared.Next(10000, 99999)}",
            fullName = "Temporary Clerk",
            password = "Factory!Pass99",
            role = "Clerk"
        }, ApiClientExtensions.Json);

        created.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await created.ReadJsonAsync();
        var id = body.GetProperty("id").GetGuid();
        var userName = body.GetProperty("userName").GetString()!;

        var theirSession = await _api.CreateApiClient().SignedInAsAsync(userName);
        (await theirSession.GetAsync("/api/v1/auth/me")).StatusCode.Should().Be(HttpStatusCode.OK);

        (await admin.PostAsync($"/api/v1/users/{id}/deactivate", null))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await theirSession.GetAsync("/api/v1/auth/me")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized, "the open session must stop working at once");
    }

    [RequiresSqlServerFact]
    public async Task An_administrator_cannot_deactivate_their_own_account()
    {
        var admin = await AdminAsync();

        var me = await (await admin.GetAsync("/api/v1/auth/me")).ReadJsonAsync();
        var id = me.GetProperty("userId").GetGuid();

        (await admin.PostAsync($"/api/v1/users/{id}/deactivate", null))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [RequiresSqlServerFact]
    public async Task A_user_is_created_with_an_unknown_role_refused()
    {
        var admin = await AdminAsync();

        var response = await admin.PostAsJsonAsync("/api/v1/users", new
        {
            userName = "someone",
            fullName = "Someone",
            password = "Factory!Pass99",
            role = "Superuser"
        }, ApiClientExtensions.Json);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [RequiresSqlServerFact]
    public async Task A_clerk_cannot_manage_users()
    {
        var clerk = await _scenario.ClerkAsync();

        (await clerk.GetAsync("/api/v1/users")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [RequiresSqlServerFact]
    public async Task A_reset_password_never_appears_in_the_audit()
    {
        var admin = await AdminAsync();

        var id = (await (await admin.PostAsJsonAsync("/api/v1/users", new
        {
            userName = $"reset{Random.Shared.Next(10000, 99999)}",
            fullName = "Reset Target",
            password = "Factory!Pass99",
            role = "Clerk"
        }, ApiClientExtensions.Json)).ReadJsonAsync()).GetProperty("id").GetGuid();

        await admin.PostAsJsonAsync($"/api/v1/users/{id}/reset-password",
            new { newPassword = "Correct!Horse42" }, ApiClientExtensions.Json);

        var audit = await (await admin.GetAsync($"/api/v1/audit?entityId={id}")).ReadJsonAsync();
        var raw = audit.ToString();

        raw.Should().NotContain("Correct!Horse42");
        raw.Should().Contain("ResetPassword");
    }
}
