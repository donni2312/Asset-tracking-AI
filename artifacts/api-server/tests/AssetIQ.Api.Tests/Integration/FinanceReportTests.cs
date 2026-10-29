using System.Text.Json;
using AssetIQ.Api.Domain;
using AssetIQ.Api.Tests.Infrastructure;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;

namespace AssetIQ.Api.Tests.Integration;

/// <summary>
/// AU-09 category report, AU-10 dashboard, AU-11 completeness, AU-12 auditor access, and SBU scope on every
/// new read and export. Includes the required invariants: report totals equal register totals, the dashboard
/// equals the report, and no cross-SBU data leaks.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class FinanceReportTests(ApiFixture api) : IAsyncLifetime
{
    private AuditWorld a = null!;
    private int _tsasLaptop, _tsasPrinter, _bakLaptop, _tsasDesk;

    public async Task InitializeAsync()
    {
        await api.ResetAsync();
        a = await AuditWorld.SeedAsync(api);

        _tsasLaptop = await a.RegisterLaptopAsync(api, "SN-R-1");
        await a.CapitaliseAsync(api, _tsasLaptop, 1200m, "2025-10-01", a.TsasFinance);

        var printer = await api.CallAsync("POST", "/assets", new
        {
            categoryId = Categories.Peripherals, subCategory = "Printer", sbuId = a.W.Tsas.Id, currentLocationId = a.W.Office.Id, assignedTo = "Finance", condition = "new",
            attributes = new { make = "HP", model = "LaserJet M404dn", serial_number = "SN-R-2" },
        }, a.TsasIt);
        _tsasPrinter = printer.Body.Int("id");
        await a.CapitaliseAsync(api, _tsasPrinter, 480m, "2026-03-01", a.TsasFinance);

        var bak = await api.CallAsync("POST", "/assets", AuditWorld.LaptopBody("SN-R-3", a.W.Bak.Id, a.W.BakWarehouse.Id), a.BakIt);
        _bakLaptop = bak.Body.Int("id");
        await a.CapitaliseAsync(api, _bakLaptop, 1500m, "2026-01-01", a.BakFinance);

        // Upgrade on the TSAS laptop capitalised this month.
        var up = await api.CallAsync("POST", $"/assets/{_tsasLaptop}/upgrades", new { upgradeDate = "2026-10-05", description = "RAM", beforeText = "16 GB", afterText = "32 GB", cost = 144 }, a.TsasIt);
        await api.CallAsync("POST", $"/upgrades/{up.Body.Int("id")}/decision", new { treatment = "capitalise" }, a.TsasFinance);

        // A non-IT asset recorded as "No serial" (not capitalised yet).
        var desk = await api.CallAsync("POST", "/assets", new
        {
            categoryId = Categories.Furniture, subCategory = "Desk", sbuId = a.W.Tsas.Id, currentLocationId = a.W.Office.Id, assignedTo = "Finance office", condition = "good",
            noSerialReason = "No manufacturer label", attributes = new { make = "Generic", model = "Executive", material = "Oak", room = "Floor 2" },
        }, a.TsasAdmin);
        _tsasDesk = desk.Body.Int("id");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static readonly string[] MoneyKeys = ["originalCost", "upgradesInPeriod", "capitalisedUpgrades", "totalCost", "depreciationMonth", "depreciationYtd", "accumulatedDepreciation", "netBookValue"];

    private static decimal M(JsonElement e, string key) => e.Prop(key).GetDecimal();

    [Theory]
    [InlineData(null)]
    [InlineData("sbu")]
    [InlineData("category")]
    [InlineData("location")]
    public async Task AU_09_category_subtotals_add_up_to_the_grand_total_which_equals_the_register_for_the_same_filters(string? filter)
    {
        var query = filter switch
        {
            "sbu" => $"?sbuId={a.W.Tsas.Id}",
            "category" => $"?categoryId={Categories.Computers}",
            "location" => $"?locationId={a.W.Office.Id}",
            _ => "",
        };
        var report = (await api.CallAsync("GET", "/finance/category-report" + query, token: a.GroupFinance)).Body;
        var register = (await api.CallAsync("GET", "/finance/register" + query, token: a.GroupFinance)).Body;
        var categories = report.Prop("categories").Items();
        var rows = register.Prop("rows").Items();

        Assert.NotEmpty(rows);
        foreach (var key in MoneyKeys)
        {
            var subtotal = categories.Sum(c => M(c.Prop("totals"), key));
            Assert.Equal(subtotal, M(report.Prop("totals"), key));
            Assert.Equal(rows.Sum(r => M(r, key)), M(report.Prop("totals"), key));
            Assert.Equal(M(register.Prop("totals"), key), M(report.Prop("totals"), key));
        }
        Assert.Equal(rows.Count, report.Prop("totals").Int("count"));
    }

    [Fact]
    public async Task AU_09_figures_for_october_2026()
    {
        var report = (await api.CallAsync("GET", "/finance/category-report?period=2026-10", token: a.GroupFinance)).Body;
        var computers = report.Prop("categories").Items().Single(c => c.Str("categoryName") == "Computers & Laptops").Prop("totals");

        // TSAS laptop: 13th month, 25 + 144/36 = 29.00; BAK laptop: 1,500/48 = 31.25 (10th month).
        Assert.Equal(3, computers.Int("count")); // + the seeded TSAS laptop, registered before the templates and not capitalised
        Assert.Equal(2700.00m, M(computers, "originalCost"));
        Assert.Equal(144.00m, M(computers, "upgradesInPeriod"));
        Assert.Equal(2844.00m, M(computers, "totalCost"));
        Assert.Equal(60.25m, M(computers, "depreciationMonth"));
        Assert.Equal(29m + 9 * 25m + 10 * 31.25m, M(computers, "depreciationYtd"));
        Assert.Equal(329m + 312.50m, M(computers, "accumulatedDepreciation"));
        Assert.Equal(2844m - 641.50m, M(computers, "netBookValue"));

        // A past month can be viewed: September has no upgrade yet.
        var september = (await api.CallAsync("GET", "/finance/category-report?period=2026-09", token: a.GroupFinance)).Body;
        var sepComputers = september.Prop("categories").Items().Single(c => c.Str("categoryName") == "Computers & Laptops").Prop("totals");
        Assert.Equal((2700m, 56.25m), (M(sepComputers, "totalCost"), M(sepComputers, "depreciationMonth")));
    }

    [Fact]
    public async Task AU_10_the_dashboard_equals_the_category_report_for_the_same_sbu()
    {
        foreach (var sbu in new int?[] { null, a.W.Tsas.Id, a.W.Bak.Id })
        {
            var q = sbu is null ? "" : $"?sbuId={sbu}";
            var dashboard = (await api.CallAsync("GET", "/finance/dashboard" + q, token: a.GroupFinance)).Body;
            var report = (await api.CallAsync("GET", "/finance/category-report" + q, token: a.GroupFinance)).Body;

            Assert.Equal(report.Prop("totals").GetRawText(), dashboard.Prop("totals").GetRawText());
            Assert.Equal(report.Prop("categories").GetRawText(), dashboard.Prop("categories").GetRawText());
            Assert.Equal(M(report.Prop("totals"), "netBookValue"), dashboard.Prop("bySbu").Items().Sum(s => M(s, "netBookValue")));
        }
        var all = (await api.CallAsync("GET", "/finance/dashboard", token: a.GroupFinance)).Body;
        Assert.Equal(2, all.Prop("totals").Int("additions")); // capitalised with a 2026 in-use date: the printer and the BAK laptop
    }

    [Fact]
    public async Task AU_09_exports_to_excel_and_pdf_keep_headers_subtotals_and_totals_and_are_logged()
    {
        using var xlsx = await Download($"/finance/category-report.xlsx?sbuId={a.W.Tsas.Id}", a.TsasFinance);
        using var book = new XLWorkbook(await xlsx.Content.ReadAsStreamAsync());
        var sheet = book.Worksheet(1);
        var cells = sheet.CellsUsed().Select(c => c.GetString()).ToList();
        Assert.Contains("Original cost", cells);
        Assert.Contains("Computers & Laptops subtotal", cells);
        Assert.Contains("Grand total", cells);
        var report = (await api.CallAsync("GET", $"/finance/category-report?sbuId={a.W.Tsas.Id}", token: a.TsasFinance)).Body;
        var totalRow = sheet.RowsUsed().Single(r => r.Cell(1).GetString() == "Grand total");
        Assert.Equal(M(report.Prop("totals"), "netBookValue"), totalRow.Cell(12).GetValue<decimal>());

        using var pdf = await Download("/finance/register.pdf", a.GroupFinance);
        Assert.Equal("application/pdf", pdf.Content.Headers.ContentType!.MediaType);

        await using var db = api.NewDbContext();
        var exports = await db.AccessLog.Where(e => e.Action == "export").Select(e => e.Detail).ToListAsync();
        Assert.Contains(exports, d => d!.StartsWith("category report xlsx?sbuId="));
        Assert.Contains(exports, d => d!.StartsWith("asset register pdf"));
    }

    [Fact]
    public async Task AU_11_lists_every_incomplete_asset_with_its_gaps_and_the_percent_complete_per_sbu()
    {
        var report = (await api.CallAsync("GET", "/finance/completeness", token: a.GroupFinance)).Body;
        var incomplete = report.Prop("incomplete").Items();

        var desk = incomplete.Single(r => r.Int("assetId") == _tsasDesk);
        var gaps = desk.Prop("gaps").Items().Select(g => g.Str("message")).ToList();
        Assert.Contains("\"No serial\" reason awaiting SBU Finance approval", gaps);
        Assert.Contains("Cost missing", gaps);
        // The seeded laptop predates the templates: its description fields are listed.
        Assert.Contains(incomplete.Single(r => r.Int("assetId") == a.W.Laptop.Id).Prop("gaps").Items(), g => g.Str("message") == "Description field missing: Brand");

        var tsas = report.Prop("sbus").Items().Single(s => s.Str("sbuCode") == "TSAS");
        var expected = Math.Round(tsas.Int("complete") * 100m / tsas.Int("assets"), 1);
        Assert.Equal(expected, M(tsas, "percentComplete"));
        Assert.Contains(report.Prop("sbus").Items(), s => s.Str("sbuCode") == "BAK");
    }

    // ── scope: no cross-SBU leaks on any new read or export ──────────────────

    [Fact]
    public async Task Scope_sbu_finance_sees_only_its_sbu_in_the_register_report_dashboard_completeness_and_exports()
    {
        foreach (var path in new[] { "/finance/register", "/finance/category-report", "/finance/dashboard" })
        {
            var body = (await api.CallAsync("GET", path, token: a.BakFinance)).Body;
            var count = body.Prop("totals").Int("count");
            Assert.Equal(1, count); // only the BAK laptop
        }
        var rows = (await api.CallAsync("GET", "/finance/register", token: a.BakFinance)).Body.Prop("rows").Items();
        Assert.All(rows, r => Assert.Equal("BAK", r.Str("sbuCode")));
        // Asking for another SBU's figures returns nothing of it.
        Assert.Equal(0, (await api.CallAsync("GET", $"/finance/register?sbuId={a.W.Tsas.Id}", token: a.BakFinance)).Body.Prop("totals").Int("count"));
        Assert.All((await api.CallAsync("GET", "/finance/completeness", token: a.BakFinance)).Body.Prop("sbus").Items(), s => Assert.Equal("BAK", s.Str("sbuCode")));

        using var xlsx = await Download("/finance/register.xlsx", a.BakFinance);
        using var book = new XLWorkbook(await xlsx.Content.ReadAsStreamAsync());
        Assert.DoesNotContain(book.Worksheet(1).CellsUsed(), c => c.GetString() == "TSAS");
    }

    [Theory]
    [InlineData("/assets/{tsas}/card")]
    [InlineData("/assets/{tsas}/card.pdf")]
    [InlineData("/assets/{tsas}/depreciation.xlsx")]
    [InlineData("/assets/{tsas}/audit")]
    [InlineData("/assets/{tsas}/upgrades")]
    public async Task Scope_a_bak_user_reading_or_exporting_a_tsas_asset_gets_403(string template)
    {
        var path = template.Replace("{tsas}", _tsasLaptop.ToString());
        foreach (var token in new[] { a.BakFinance, a.BakIt, a.BakAdmin })
            Assert.Equal(403, (await api.CallAsync("GET", path, token: token)).Status);
        Assert.Equal(403, (await api.CallAsync("GET", "/assets/lookup?code=SN-R-1", token: a.BakFinance)).Status);
    }

    [Fact]
    public async Task Scope_a_bak_user_editing_a_tsas_asset_gets_403()
    {
        Assert.Equal(403, (await api.CallAsync("PATCH", $"/assets/{_tsasLaptop}", new { notes = "x" }, a.BakIt)).Status);
        Assert.Equal(403, (await api.CallAsync("PATCH", $"/assets/{_tsasLaptop}/financial", new { originalCost = 1, reason = "xxx" }, a.BakFinance)).Status);
        Assert.Equal(403, (await api.CallAsync("POST", $"/assets/{_tsasLaptop}/category", new { categoryId = Categories.Peripherals, reason = "xxx" }, a.BakFinance)).Status);
        Assert.Equal(403, (await api.CallAsync("POST", $"/assets/{_tsasLaptop}/upgrades", new { upgradeDate = "2026-10-01", description = "x1", beforeText = "a", afterText = "b", cost = 1 }, a.BakIt)).Status);
        Assert.Equal(403, (await api.CallAsync("POST", "/depreciation/locks", new { period = "2026-09", sbuId = a.W.Tsas.Id }, a.BakFinance)).Status);
    }

    [Fact]
    public async Task Scope_group_it_sees_it_assets_of_every_sbu_and_no_non_it_assets()
    {
        var rows = (await api.CallAsync("GET", "/finance/register", token: a.GroupIt)).Body.Prop("rows").Items();
        Assert.Contains(rows, r => r.Str("sbuCode") == "BAK");
        Assert.DoesNotContain(rows, r => r.Int("assetId") == _tsasDesk);
        Assert.Equal(403, (await api.CallAsync("GET", $"/assets/{_tsasDesk}/card", token: a.GroupIt)).Status);
    }

    // ── AU-12 ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AU_12_group_finance_creates_a_time_limited_auditor_who_reads_and_exports_everything_but_changes_nothing()
    {
        Assert.Equal(403, (await api.CallAsync("POST", "/users/auditors", new { username = "x", name = "x", password = "long-enough-1", accessExpiresAt = "2026-12-31" }, a.GroupIt)).Status);
        var created = await api.CallAsync("POST", "/users/auditors",
            new { username = "ext.auditor", name = "External Auditor", password = "Audit-2026-secure", accessExpiresAt = "2026-11-30T23:59:00Z" }, a.GroupFinance);
        Assert.Equal((201, "auditor"), (created.Status, created.Body.Str("role")));

        var login = await api.CallAsync("POST", "/auth/login", new { username = "ext.auditor", password = "Audit-2026-secure" });
        Assert.Equal(200, login.Status);
        Assert.Equal("All SBUs (read only)", login.Body.Prop("user").Str("scope"));
        var token = login.Body.Str("token")!;

        // Reads and exports across all SBUs.
        Assert.Equal(6, (await api.CallAsync("GET", "/finance/register", token: token)).Body.Prop("totals").Int("count")); // every asset of both SBUs
        Assert.Equal(200, (await api.CallAsync("GET", $"/assets/{_bakLaptop}/card", token: token)).Status);
        using (await Download("/finance/category-report.pdf", token)) { }
        Assert.Equal(200, (await api.CallAsync("GET", "/assets", token: token)).Status);

        // Changes nothing, on the new endpoints and the older ones.
        Assert.Equal(403, (await api.CallAsync("PATCH", $"/assets/{_bakLaptop}", new { notes = "x" }, token)).Status);
        Assert.Equal(403, (await api.CallAsync("PATCH", $"/assets/{_bakLaptop}/financial", new { originalCost = 1, reason = "xxx" }, token)).Status);
        Assert.Equal(403, (await api.CallAsync("POST", "/depreciation/run", new { period = "2026-09" }, token)).Status);
        Assert.Equal(403, (await api.CallAsync("POST", "/movements", new { assetId = a.W.Projector.Id, toLocationId = a.W.Gate.Id }, token)).Status);
        Assert.Equal(403, (await api.CallAsync("POST", "/gate-passes", new { assetId = a.W.Projector.Id, requestedBy = "x", reason = "y" }, token)).Status);
        Assert.Equal(403, (await api.CallAsync("PATCH", "/settings/warranty_alert_days", new { value = 30 }, token)).Status);

        // Every login and export is logged.
        var log = (await api.CallAsync("GET", "/access-log?role=auditor", token: a.GroupFinance)).Body.Items();
        Assert.Contains(log, e => e.Str("action") == "login" && e.Str("username") == "ext.auditor");
        Assert.Contains(log, e => e.Str("action") == "export" && e.Str("detail")!.StartsWith("category report pdf"));
    }

    [Fact]
    public async Task AU_12_after_the_expiry_date_access_is_refused()
    {
        await api.CallAsync("POST", "/users/auditors",
            new { username = "int.auditor", name = "Internal Audit", password = "Audit-2026-secure", accessExpiresAt = "2026-10-31T00:00:00Z" }, a.GroupFinance);
        var token = (await api.CallAsync("POST", "/auth/login", new { username = "int.auditor", password = "Audit-2026-secure" })).Body.Str("token")!;
        Assert.Equal(200, (await api.CallAsync("GET", "/finance/register", token: token)).Status);

        api.SetNow("2026-10-31T00:00:01Z");

        Assert.Equal(401, (await api.CallAsync("GET", "/finance/register", token: token)).Status); // the token no longer signs anyone in
        var login = await api.CallAsync("POST", "/auth/login", new { username = "int.auditor", password = "Audit-2026-secure" });
        Assert.Equal(403, login.Status);
        Assert.Contains("access expired", login.Error());
        await using var db = api.NewDbContext();
        Assert.True(await db.AccessLog.AnyAsync(e => e.Username == "int.auditor" && e.Action == "login_refused"));
    }

    private async Task<HttpResponseMessage> Download(string path, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api" + path);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var response = await api.Client.SendAsync(request);
        Assert.True((int)response.StatusCode == 200, $"{path}: {(int)response.StatusCode}");
        return response;
    }
}
