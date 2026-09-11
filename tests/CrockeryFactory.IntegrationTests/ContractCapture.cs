using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using CrockeryFactory.IntegrationTests.Infrastructure;
using Xunit;

namespace CrockeryFactory.IntegrationTests;

/// <summary>
/// Documentation tool, not a test. Drives the real API and writes every request and
/// response verbatim to disk so the frontend contract is transcribed from what the
/// server actually returns rather than from what the DTOs look like they return.
/// Run with: CAPTURE_DIR=/some/path dotnet test --filter ContractCapture
/// </summary>
[Collection(ApiCollection.Name)]
public class ContractCapture
{
    private readonly FactoryApiFixture _api;
    private readonly FactoryScenario _scenario;
    private static string? Dir => Environment.GetEnvironmentVariable("CAPTURE_DIR");

    public ContractCapture(FactoryApiFixture api)
    {
        _api = api;
        _scenario = new FactoryScenario(api);
    }

    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

    private static async Task DumpAsync(string name, HttpMethod verb, string path, object? request, HttpResponseMessage response)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"### {verb} {path}");
        sb.AppendLine($"STATUS: {(int)response.StatusCode} {response.StatusCode}");

        foreach (var h in new[] { "ETag", "Location", "Idempotency-Replayed" })
        {
            if (response.Headers.TryGetValues(h, out var v))
                sb.AppendLine($"HEADER {h}: {string.Join(", ", v)}");
            else if (response.Content.Headers.TryGetValues(h, out var cv))
                sb.AppendLine($"HEADER {h}: {string.Join(", ", cv)}");
        }

        if (request is not null)
        {
            sb.AppendLine("REQUEST:");
            sb.AppendLine(JsonSerializer.Serialize(request, Pretty));
        }

        var body = await response.Content.ReadAsStringAsync();
        sb.AppendLine("RESPONSE:");
        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                sb.AppendLine(JsonSerializer.Serialize(doc.RootElement, Pretty));
            }
            catch { sb.AppendLine(body); }
        }
        else sb.AppendLine("(empty)");

        Directory.CreateDirectory(Dir!);
        await File.WriteAllTextAsync(Path.Combine(Dir!, $"{name}.txt"), sb.ToString());
    }

    private static async Task<HttpResponseMessage> PostAsync(
        HttpClient c, string name, string path, object body, string? idempotencyKey = null)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body, options: ApiClientExtensions.Json)
        };
        if (idempotencyKey is not null) req.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
        var res = await c.SendAsync(req);
        await DumpAsync(name, HttpMethod.Post, path, body, res);
        return res;
    }

    private static async Task<HttpResponseMessage> GetAsync(HttpClient c, string name, string path)
    {
        var res = await c.GetAsync(path);
        await DumpAsync(name, HttpMethod.Get, path, null, res);
        return res;
    }

    [RequiresCaptureDirFact]
    public async Task Capture()
    {
        var anon = _api.CreateApiClient();

        // ---------- auth ----------
        await GetAsync(anon, "00-health", "/api/v1/health");

        var loginBody = new { userName = FactoryApiFixture.OwnerUserName, password = FactoryApiFixture.Password };
        var owner = _api.CreateApiClient();
        var loginRes = await owner.PostAsJsonAsync("/api/v1/auth/login", loginBody, ApiClientExtensions.Json);
        await DumpAsync("01-login-success", HttpMethod.Post, "/api/v1/auth/login", loginBody, loginRes);

        // Set-Cookie captured separately
        var cookieLine = loginRes.Headers.TryGetValues("Set-Cookie", out var sc) ? string.Join("\n", sc) : "(none)";
        await File.WriteAllTextAsync(Path.Combine(Dir!, "01b-login-setcookie.txt"), cookieLine);

        await PostAsync(anon, "02-login-bad-password", "/api/v1/auth/login",
            new { userName = FactoryApiFixture.OwnerUserName, password = "wrong-password-99" });
        await PostAsync(anon, "03-login-unknown-user", "/api/v1/auth/login",
            new { userName = "no-such-person", password = "wrong-password-99" });
        await PostAsync(anon, "04-login-inactive", "/api/v1/auth/login",
            new { userName = FactoryApiFixture.InactiveUserName, password = FactoryApiFixture.Password });

        await GetAsync(owner, "05-auth-me", "/api/v1/auth/me");
        await GetAsync(anon, "06-unauthenticated-401", "/api/v1/auth/me");

        var clerk = await _api.CreateApiClient().SignedInAsAsync(FactoryApiFixture.ClerkUserName);
        var admin = await _api.CreateApiClient().SignedInAsAsync(FactoryApiFixture.AdminUserName);
        await GetAsync(clerk, "07-auth-me-clerk", "/api/v1/auth/me");
        await GetAsync(admin, "08-auth-me-admin", "/api/v1/auth/me");

        // ---------- products ----------
        var code = $"CUP-PDR-{Random.Shared.Next(10000, 99999)}";
        var createProduct = new
        {
            code,
            name = "Cappuccino cup",
            capacityMl = 180,
            description = "Standard 180ml cappuccino cup",
            prices = new[]
            {
                new { grade = "First", unitRate = 120.00m },
                new { grade = "Second", unitRate = 72.00m }
            }
        };
        var created = await PostAsync(owner, "10-product-create", "/api/v1/products", createProduct, Guid.NewGuid().ToString());
        var productId = (await created.ReadJsonAsync()).GetProperty("id").GetGuid();

        await PostAsync(owner, "11-product-create-validation-error", "/api/v1/products", new
        {
            code = "bad code!",
            name = "",
            capacityMl = 9999,
            prices = new[] { new { grade = "First", unitRate = 0m } }
        });
        await PostAsync(owner, "12-product-create-duplicate", "/api/v1/products", createProduct);
        await PostAsync(clerk, "13-product-create-forbidden", "/api/v1/products", createProduct);
        await PostAsync(owner, "14-product-grade-not-enabled", "/api/v1/products", new
        {
            code = $"CUP-G3-{Random.Shared.Next(1000, 9999)}",
            name = "Third grade cup",
            prices = new[] { new { grade = "Third", unitRate = 40m } }
        });

        var getProduct = await GetAsync(owner, "15-product-get", $"/api/v1/products/{productId}");
        var etag = getProduct.Headers.ETag!.ToString();
        await File.WriteAllTextAsync(Path.Combine(Dir!, "15b-product-etag.txt"), etag);

        var putReq = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/products/{productId}")
        {
            Content = JsonContent.Create(new { code, name = "Cappuccino cup (large)", capacityMl = 200, description = "Revised" },
                options: ApiClientExtensions.Json)
        };
        putReq.Headers.TryAddWithoutValidation("If-Match", etag);
        var putRes = await owner.SendAsync(putReq);
        await DumpAsync("16-product-update", HttpMethod.Put, $"/api/v1/products/{productId}", new { code, name = "Cappuccino cup (large)", capacityMl = 200, description = "Revised" }, putRes);

        // stale ETag -> 409
        var staleReq = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/products/{productId}")
        {
            Content = JsonContent.Create(new { code, name = "Stale writer", capacityMl = 200 }, options: ApiClientExtensions.Json)
        };
        staleReq.Headers.TryAddWithoutValidation("If-Match", etag);
        await DumpAsync("17-product-update-stale-409", HttpMethod.Put, $"/api/v1/products/{productId}", new { name = "Stale writer" }, await owner.SendAsync(staleReq));

        // missing If-Match -> 428
        await DumpAsync("18-product-update-no-ifmatch-428", HttpMethod.Put, $"/api/v1/products/{productId}", new { code, name = "No If-Match", capacityMl = 200 },
            await owner.PutAsJsonAsync($"/api/v1/products/{productId}", new { code, name = "No If-Match", capacityMl = 200 }, ApiClientExtensions.Json));

        var pricesBody = new
        {
            prices = new[] { new { grade = "First", unitRate = 135.00m }, new { grade = "Second", unitRate = 81.00m } },
            effectiveFrom = FactoryScenario.Today().ToString("yyyy-MM-dd"),
            reason = "Clay and fuel both up this quarter"
        };
        await DumpAsync("19-product-prices", HttpMethod.Put, $"/api/v1/products/{productId}/prices", pricesBody,
            await owner.PutAsJsonAsync($"/api/v1/products/{productId}/prices", pricesBody, ApiClientExtensions.Json));

        await GetAsync(owner, "20-products-list-paged", "/api/v1/products?page=1&pageSize=2");
        await GetAsync(owner, "21-products-list-search", $"/api/v1/products?search={code}&includeInactive=true");

        // ---------- production ----------
        var prodBody = new
        {
            productId,
            entryDate = FactoryScenario.Today().ToString("yyyy-MM-dd"),
            quantityGood = 1800,
            quantitySeconds = 140,
            quantityBroken = 60,
            breakageReasonCodeId = FactoryScenario.SeededBreakageReason,
            batchReference = "KILN-3/2026-09",
            notes = "Morning unload"
        };
        var prodRes = await PostAsync(clerk, "30-production-create", "/api/v1/production-entries", prodBody, Guid.NewGuid().ToString());
        var entryId = (await prodRes.ReadJsonAsync()).GetProperty("id").GetGuid();

        await PostAsync(clerk, "31-production-high-loss-warning", "/api/v1/production-entries", new
        {
            productId,
            entryDate = FactoryScenario.Today().ToString("yyyy-MM-dd"),
            quantityGood = 600,
            quantitySeconds = 0,
            quantityBroken = 400,
            breakageReasonCodeId = FactoryScenario.SeededBreakageReason
        });
        await PostAsync(clerk, "32-production-no-quantity", "/api/v1/production-entries", new
        {
            productId, entryDate = FactoryScenario.Today().ToString("yyyy-MM-dd"),
            quantityGood = 0, quantitySeconds = 0, quantityBroken = 0
        });
        await PostAsync(clerk, "33-production-breakage-reason-required", "/api/v1/production-entries", new
        {
            productId, entryDate = FactoryScenario.Today().ToString("yyyy-MM-dd"),
            quantityGood = 500, quantitySeconds = 0, quantityBroken = 50
        });
        await PostAsync(clerk, "34-production-date-future", "/api/v1/production-entries", new
        {
            productId, entryDate = FactoryScenario.Today().AddDays(1).ToString("yyyy-MM-dd"),
            quantityGood = 100, quantitySeconds = 0, quantityBroken = 0
        });
        await GetAsync(owner, "35-production-list", "/api/v1/production-entries?page=1&pageSize=2");
        await GetAsync(owner, "36-production-get", $"/api/v1/production-entries/{entryId}");
        await GetAsync(owner, "37-production-summary", $"/api/v1/production-entries/summary?productId={productId}&groupBy=Product");

        // ---------- stock ----------
        await GetAsync(owner, "40-stock", "/api/v1/stock");
        await GetAsync(owner, "41-stock-asof", $"/api/v1/stock?asOf={FactoryScenario.Today():yyyy-MM-dd}&onlyInStock=true");
        await GetAsync(owner, "42-stock-movements", $"/api/v1/stock/{productId}/movements?grade=First&page=1&pageSize=5");

        var adjBody = new
        {
            productId, grade = "First", quantityChange = -35,
            reasonCodeId = FactoryScenario.SeededAdjustmentReason,
            adjustedOn = FactoryScenario.Today().ToString("yyyy-MM-dd"),
            notes = "Chipped during the monthly count"
        };
        await PostAsync(clerk, "43-stock-adjustment", "/api/v1/stock/adjustments", adjBody, Guid.NewGuid().ToString());
        await PostAsync(clerk, "44-stock-adjustment-insufficient", "/api/v1/stock/adjustments", new
        {
            productId, grade = "First", quantityChange = -999999,
            reasonCodeId = FactoryScenario.SeededAdjustmentReason,
            adjustedOn = FactoryScenario.Today().ToString("yyyy-MM-dd"),
            notes = "More than exists"
        });
        await PostAsync(admin, "45-stock-adjustment-forbidden", "/api/v1/stock/adjustments", adjBody);

        // ---------- customers ----------
        var custCode = $"C-PDR-{Random.Shared.Next(1000, 9999)}";
        var custBody = new
        {
            code = custCode, name = "Shalimar Traders", city = "Gujrat",
            phone = "03001234567", address = "Main Bazaar, Gujrat",
            openingBalance = 18500.00m,
            openingBalanceAsOf = FactoryScenario.Today().AddDays(-60).ToString("yyyy-MM-dd"),
            notes = "Long-standing account"
        };
        var custRes = await PostAsync(clerk, "50-customer-create", "/api/v1/customers", custBody, Guid.NewGuid().ToString());
        var customerId = (await custRes.ReadJsonAsync()).GetProperty("id").GetGuid();

        await GetAsync(owner, "51-customers-list", "/api/v1/customers?page=1&pageSize=2");
        var custGet = await GetAsync(owner, "52-customer-get", $"/api/v1/customers/{customerId}");
        var custEtag = custGet.Headers.ETag!.ToString();

        // ---------- dispatch ----------
        var dispatchBody = new
        {
            customerId,
            dispatchDate = FactoryScenario.Today().ToString("yyyy-MM-dd"),
            lines = new object[]
            {
                new { productId, grade = "First", quantity = 240, unitRate = (decimal?)null },
                new { productId, grade = "Second", quantity = 60, unitRate = (decimal?)70.00m }
            },
            vehicleNumber = "GJT-4417",
            notes = "Loaded at the east gate"
        };
        var dispRes = await PostAsync(clerk, "60-dispatch-create", "/api/v1/dispatches", dispatchBody, Guid.NewGuid().ToString());
        var dispatchId = (await dispRes.ReadJsonAsync()).GetProperty("id").GetGuid();

        await PostAsync(clerk, "61-dispatch-insufficient-stock", "/api/v1/dispatches", new
        {
            customerId, dispatchDate = FactoryScenario.Today().ToString("yyyy-MM-dd"),
            lines = new object[] { new { productId, grade = "First", quantity = 999999, unitRate = (decimal?)null } }
        });
        await PostAsync(clerk, "62-dispatch-duplicate-line", "/api/v1/dispatches", new
        {
            customerId, dispatchDate = FactoryScenario.Today().ToString("yyyy-MM-dd"),
            lines = new object[]
            {
                new { productId, grade = "First", quantity = 10, unitRate = (decimal?)null },
                new { productId, grade = "First", quantity = 20, unitRate = (decimal?)null }
            }
        });
        await PostAsync(clerk, "63-dispatch-no-lines", "/api/v1/dispatches", new
        {
            customerId, dispatchDate = FactoryScenario.Today().ToString("yyyy-MM-dd"), lines = Array.Empty<object>()
        });
        await GetAsync(owner, "64-dispatches-list", "/api/v1/dispatches?page=1&pageSize=2");
        await GetAsync(owner, "65-dispatch-get", $"/api/v1/dispatches/{dispatchId}");
        await GetAsync(owner, "66-dispatch-document-501", $"/api/v1/dispatches/{dispatchId}/document?copies=both");

        // ---------- payments ----------
        var payBody = new
        {
            customerId, paymentDate = FactoryScenario.Today().ToString("yyyy-MM-dd"),
            amount = 20000.00m, method = "Cheque", reference = "CHQ-889231", notes = "Part settlement"
        };
        var payRes = await PostAsync(clerk, "70-payment-create", "/api/v1/payments", payBody, Guid.NewGuid().ToString());
        await PostAsync(clerk, "71-payment-reference-required", "/api/v1/payments", new
        {
            customerId, paymentDate = FactoryScenario.Today().ToString("yyyy-MM-dd"),
            amount = 5000m, method = "BankTransfer", reference = (string?)null
        });
        await PostAsync(clerk, "72-payment-overpayment-warning", "/api/v1/payments", new
        {
            customerId, paymentDate = FactoryScenario.Today().ToString("yyyy-MM-dd"),
            amount = 5000000m, method = "Cash", reference = (string?)null
        });
        await GetAsync(owner, "73-payments-list", "/api/v1/payments?page=1&pageSize=2");

        // ---------- customer reports ----------
        await GetAsync(owner, "80-customers-outstanding", "/api/v1/customers/outstanding");
        await GetAsync(owner, "81-customer-statement",
            $"/api/v1/customers/{customerId}/statement?from={FactoryScenario.Today().AddDays(-30):yyyy-MM-dd}&to={FactoryScenario.Today():yyyy-MM-dd}");

        // cancel a dispatch so the statement shows a zero line
        await PostAsync(clerk, "82-dispatch-cancel", $"/api/v1/dispatches/{dispatchId}/cancel",
            new { reason = "Vehicle never left the yard" });
        await GetAsync(owner, "83-customer-statement-with-cancelled",
            $"/api/v1/customers/{customerId}/statement?from={FactoryScenario.Today().AddDays(-30):yyyy-MM-dd}&to={FactoryScenario.Today():yyyy-MM-dd}");
        await PostAsync(clerk, "84-production-cancel", $"/api/v1/production-entries/{entryId}/cancel",
            new { reason = "Recorded against the wrong kiln load" });

        // ---------- reports ----------
        await GetAsync(owner, "90-report-daily-stock", $"/api/v1/reports/daily-stock?date={FactoryScenario.Today():yyyy-MM-dd}");
        await GetAsync(owner, "91-report-sales-summary",
            $"/api/v1/reports/sales-summary?from={FactoryScenario.Today().AddDays(-30):yyyy-MM-dd}&to={FactoryScenario.Today():yyyy-MM-dd}&groupBy=Customer");
        await GetAsync(owner, "92-report-dashboard", "/api/v1/reports/dashboard");
        await GetAsync(owner, "93-report-xlsx-501", "/api/v1/reports/daily-stock?format=xlsx");
        await GetAsync(owner, "94-report-production-summary", "/api/v1/reports/production-summary?groupBy=Month");

        // ---------- admin ----------
        await GetAsync(clerk, "A0-reason-codes", "/api/v1/reason-codes?type=Breakage");
        await GetAsync(clerk, "A1-reason-codes-all", "/api/v1/reason-codes");
        await GetAsync(admin, "A2-settings", "/api/v1/settings");
        await DumpAsync("A3-settings-update", HttpMethod.Put, "/api/v1/settings",
            new { values = new Dictionary<string, string> { ["Factory.Name"] = "Shahbaz Crockery Works" } },
            await admin.PutAsJsonAsync("/api/v1/settings",
                new { values = new Dictionary<string, string> { ["Factory.Name"] = "Shahbaz Crockery Works" } }, ApiClientExtensions.Json));
        await DumpAsync("A4-settings-unknown-key", HttpMethod.Put, "/api/v1/settings",
            new { values = new Dictionary<string, string> { ["Factory.Nmae"] = "Typo" } },
            await admin.PutAsJsonAsync("/api/v1/settings",
                new { values = new Dictionary<string, string> { ["Factory.Nmae"] = "Typo" } }, ApiClientExtensions.Json));
        await GetAsync(admin, "A5-audit", "/api/v1/audit?page=1&pageSize=3");
        await GetAsync(clerk, "A6-audit-forbidden", "/api/v1/audit");
        await GetAsync(admin, "A7-users", "/api/v1/users");
        await PostAsync(admin, "A8-user-create", "/api/v1/users", new
        {
            userName = $"munshi{Random.Shared.Next(1000, 9999)}",
            fullName = "Abdul Rehman",
            password = "Factory!Pass99",
            role = "Clerk"
        });
        await PostAsync(admin, "A9-rebuild-balances", "/api/v1/admin/rebuild-stock-balances", new { });
        await PostAsync(owner, "B0-logout", "/api/v1/auth/logout", new { });
    }
}

/// <summary>
/// Keeps the capture out of the ordinary test run. It is a documentation tool that writes
/// to disk, not a test with an assertion, so it only runs when somebody asks for it by
/// setting CAPTURE_DIR.
/// </summary>
public sealed class RequiresCaptureDirFactAttribute : FactAttribute
{
    public RequiresCaptureDirFactAttribute()
    {
        if (!FactoryApiFixture.SqlServerAvailable)
            Skip = "Set CROCKERY_TEST_CONNECTION to run against SQL Server.";
        else if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CAPTURE_DIR")))
            Skip = "Set CAPTURE_DIR to regenerate the frontend PDR's captured API examples.";
    }
}
