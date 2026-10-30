using System.Net.Http.Headers;
using System.Text.Json;
using AssetIQ.Api.Domain;
using AssetIQ.Api.Services;
using AssetIQ.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AssetIQ.Api.Tests.Integration;

/// <summary>
/// Phase 5a: revaluations and impairments (SF-07), CAPEX budgets and alerts by financial year (SF-09, GF-05),
/// verification outcomes per SBU (GF-08). Today is 14 October 2026: the last month of FY2026 (1 Nov 2025 – 31 Oct 2026).
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class FinancePlanningTests(ApiFixture api) : IAsyncLifetime
{
    private AuditWorld a = null!;
    private int _laptop, _printer;

    public async Task InitializeAsync()
    {
        await api.ResetAsync();
        a = await AuditWorld.SeedAsync(api);
        _laptop = await a.RegisterLaptopAsync(api, "SN-P5-1");
        await a.CapitaliseAsync(api, _laptop, 1200m, "2025-10-01", a.TsasFinance); // 48 months: 25.00 a month
        var printer = await api.CallAsync("POST", "/assets", new
        {
            categoryId = Categories.Peripherals, subCategory = "Printer", sbuId = a.W.Tsas.Id, currentLocationId = a.W.Office.Id, assignedTo = "Finance", condition = "new",
            attributes = new { make = "HP", model = "LaserJet M404dn", serial_number = "SN-P5-2" },
        }, a.TsasIt);
        _printer = printer.Body.Int("id");
        await a.CapitaliseAsync(api, _printer, 480m, "2026-03-01", a.TsasFinance);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static decimal M(JsonElement e, string key) => e.Prop(key).GetDecimal();

    private async Task<int> AttachAsync(int assetId, string token)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent("%PDF-1.4\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF"u8.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(file, "file", "valuation-report.pdf");
        form.Add(new StringContent("other"), "kind");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/assets/{assetId}/attachments") { Content = form };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await api.Client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True((int)response.StatusCode == 201, text);
        return JsonDocument.Parse(text).RootElement.Int("id");
    }

    private async Task<JsonElement> RegisterRowAsync(int assetId) =>
        (await api.CallAsync("GET", "/finance/register?period=2026-10", token: a.TsasFinance)).Body.Prop("rows").Items().Single(r => r.Int("assetId") == assetId);

    [Fact]
    public async Task SF_07_an_impairment_needs_a_reason_and_document_lowers_the_nbv_and_respreads_the_depreciation()
    {
        var doc = await AttachAsync(_laptop, a.TsasFinance);
        Assert.Equal(400, (await api.CallAsync("POST", $"/assets/{_laptop}/revaluations", new { kind = "impairment", impairmentAmount = 300m, reason = "Screen damage", attachmentId = 999 }, a.TsasFinance)).Status);
        Assert.Equal(403, (await api.CallAsync("POST", $"/assets/{_laptop}/revaluations", new { kind = "impairment", impairmentAmount = 300m, reason = "Screen damage", attachmentId = doc }, a.BakFinance)).Status);
        Assert.Equal(403, (await api.CallAsync("POST", $"/assets/{_laptop}/revaluations", new { kind = "impairment", impairmentAmount = 300m, reason = "Screen damage", attachmentId = doc }, a.TsasIt)).Status);
        Assert.Equal(400, (await api.CallAsync("POST", $"/assets/{_laptop}/revaluations", new { kind = "impairment", impairmentAmount = 5000m, reason = "Too much", attachmentId = doc }, a.TsasFinance)).Status);

        var done = await api.CallAsync("POST", $"/assets/{_laptop}/revaluations", new { kind = "impairment", impairmentAmount = 300m, reason = "Screen damage, assessor's report", attachmentId = doc }, a.TsasFinance);
        Assert.True(done.Status == 201, done.Error());
        var row = done.Body.Items().Single();
        // NBV at the end of September 2026: 1,200 − 12 × 25 = 900.
        Assert.Equal(("2026-10", -300m, 900m, 600m), (row.Str("period"), M(row, "amount"), M(row, "nbvBefore"), M(row, "nbvAfter")));

        var register = await RegisterRowAsync(_laptop);
        Assert.Equal(-300m, M(register, "revaluations"));
        Assert.Equal(900m, M(register, "totalCost"));
        Assert.Equal(16.67m, M(register, "depreciationMonth")); // (900 − 300) ÷ 36 remaining months
        Assert.Equal(900m - 316.67m, M(register, "netBookValue"));

        await using var db = api.NewDbContext();
        Assert.True(await db.AuditLog.AnyAsync(x => x.AssetId == _laptop && x.Action == "impaired" && x.OldValue == "900.00" && x.NewValue == "600.00"));
    }

    [Fact]
    public async Task SF_07_a_revaluation_up_sets_the_new_value()
    {
        var doc = await AttachAsync(_printer, a.TsasFinance);
        var done = await api.CallAsync("POST", $"/assets/{_printer}/revaluations", new { kind = "revaluation", newValue = 500m, revisedLifeMonths = 24, reason = "Market valuation", attachmentId = doc }, a.TsasFinance);
        Assert.True(done.Status == 201, done.Error());
        Assert.Equal(500m, M(done.Body.Items().Single(), "nbvAfter"));
        Assert.Equal(500m - Math.Round(500m / 24, 2), M(await RegisterRowAsync(_printer), "netBookValue"));
        Assert.Equal(403, (await api.CallAsync("GET", $"/assets/{_printer}/revaluations", token: a.TsasIt)).Status);
    }

    [Fact]
    public async Task SF_09_and_GF_05_capex_budget_against_capitalised_spend_with_alerts_at_80_and_100_percent()
    {
        var set = await api.CallAsync("PUT", "/capex/budgets", new
        {
            financialYear = 2026, lines = new object[] { new { categoryId = Categories.Computers, amount = 1500m }, new { categoryId = Categories.Peripherals, amount = 400m } },
        }, a.TsasFinance);
        Assert.True(set.Status == 200, set.Error());
        Assert.Equal(("FY2026", "2025-11-01"), (set.Body.Str("label"), set.Body.Str("startsOn")));
        var lines = set.Body.Prop("lines").Items();
        var computers = lines.Single(l => l.Int("categoryId") == Categories.Computers);
        var printers = lines.Single(l => l.Int("categoryId") == Categories.Peripherals);
        Assert.Equal((1200m, -300m, 80.0m, false), (M(computers, "actual"), M(computers, "variance"), M(computers, "usedPercent"), computers.Bool("overBudget")));
        Assert.Equal((480m, true), (M(printers, "actual"), printers.Bool("overBudget")));

        Assert.Equal(403, (await api.CallAsync("PUT", "/capex/budgets", new { sbuId = a.W.Tsas.Id, financialYear = 2026, lines = new object[] { new { categoryId = 1, amount = 1m } } }, a.BakFinance)).Status);
        Assert.Equal(403, (await api.CallAsync("GET", "/capex", token: a.TsasIt)).Status);

        var group = await api.CallAsync("GET", "/capex?financialYear=2026", token: a.GroupFinance);
        Assert.Contains(group.Body.Prop("bySbu").Items(), s => s.Str("sbuCode") == "TSAS" && M(s, "budget") == 1900m && s.Bool("overBudget") == false);
        // Next financial year starts afresh.
        Assert.Empty((await api.CallAsync("GET", "/capex?financialYear=2027", token: a.GroupFinance)).Body.Prop("lines").Items());

        using (var scope = api.Services.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<FinancePlanningService>();
            Assert.Equal(2, await service.CapexAlertsAsync(CancellationToken.None)); // computers 80 %, printers 100 %
            Assert.Equal(0, await service.CapexAlertsAsync(CancellationToken.None));
        }
        Assert.Equal(2, (await api.CallAsync("GET", "/notifications", token: a.TsasFinance)).Body.Items().Count(n => n.Str("kind") == "capex"));
    }

    [Fact]
    public async Task GF_08_verification_outcomes_show_counts_values_and_the_missing_assets()
    {
        var started = await api.CallAsync("POST", "/verification-sessions", new { locationId = a.W.Office.Id }, a.TsasIt);
        Assert.True(started.Status == 201, started.Error());
        var id = started.Body.Int("id");
        await api.CallAsync("POST", $"/verification-sessions/{id}/scans", new { codes = new[] { "SN-P5-2" } }, a.TsasIt);
        await api.CallAsync("POST", $"/verification-sessions/{id}/close", null, a.TsasIt);

        var outcomes = await api.CallAsync("GET", "/verification-outcomes", token: a.GroupFinance);
        Assert.Equal(200, outcomes.Status);
        var count = outcomes.Body.Items().Single(o => o.Int("sessionId") == id);
        Assert.Equal(1, count.Int("found"));
        Assert.True(count.Int("missing") >= 1);
        var missing = count.Prop("missingAssets").Items().Single(m => m.Int("assetId") == _laptop);
        Assert.Equal(a.W.Office.Name, missing.Str("lastLocation"));
        Assert.True(M(missing, "netBookValue") > 0);
        Assert.Equal(403, (await api.CallAsync("GET", "/verification-outcomes", token: a.GroupIt)).Status);
        Assert.Empty((await api.CallAsync("GET", "/verification-outcomes", token: a.BakFinance)).Body.Items());
    }
}
