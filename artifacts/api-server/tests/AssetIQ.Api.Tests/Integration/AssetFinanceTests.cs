using System.Text.Json;
using AssetIQ.Api.Tests.Infrastructure;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AssetIQ.Api.Tests.Integration;

/// <summary>AU-04 cost, AU-05 depreciation runs and locks, AU-06 audit trail, AU-07 upgrades, AU-08 asset card.</summary>
[Collection(ApiCollection.Name)]
public sealed class AssetFinanceTests(ApiFixture api) : IAsyncLifetime
{
    private AuditWorld a = null!;

    public async Task InitializeAsync()
    {
        await api.ResetAsync();
        a = await AuditWorld.SeedAsync(api);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // ── AU-04 ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AU_04_cost_usd_is_original_cost_times_the_rate_fixed_at_capitalisation_and_money_is_exact()
    {
        var id = await a.RegisterLaptopAsync(api, "SN-ZAR-1");
        await a.CapitaliseAsync(api, id, 23_456.78m, "2026-10-01", a.TsasFinance, currency: "ZAR", rate: 0.054321m);

        var asset = (await api.CallAsync("GET", $"/assets/{id}", token: a.TsasFinance)).Body;
        Assert.Equal(23_456.78m, asset.Prop("originalCost").GetDecimal());
        Assert.Equal("ZAR", asset.Str("currency"));
        Assert.Equal(1274.20m, asset.Prop("costUsd").GetDecimal()); // 23,456.78 × 0.054321 = 1,274.1958… → 1,274.20
        Assert.NotNull(asset.Str("capitalisedAt"));
        Assert.Contains(await AuditWorld.AuditAsync(api, id), e => e is { Action: "capitalised", NewValue: "1274.20" });
    }

    [Fact]
    public async Task AU_04_only_sbu_finance_of_the_assets_sbu_edits_financial_fields_with_a_reason()
    {
        var id = await a.RegisterLaptopAsync(api, "SN-FIN-1");
        var body = new { originalCost = 1000, currency = "USD", exchangeRate = 1, reason = "Invoice INV-1" };

        Assert.Equal(403, (await api.CallAsync("PATCH", $"/assets/{id}/financial", body, a.TsasIt)).Status);
        Assert.Equal(403, (await api.CallAsync("PATCH", $"/assets/{id}/financial", body, a.BakFinance)).Status);
        Assert.Equal(403, (await api.CallAsync("PATCH", $"/assets/{id}/financial", body, a.GroupFinance)).Status);
        Assert.Equal(400, (await api.CallAsync("PATCH", $"/assets/{id}/financial", new { originalCost = 1000 }, a.TsasFinance)).Status); // no reason
        Assert.Equal(400, (await api.CallAsync("PATCH", $"/assets/{id}/financial", new { currency = "USD", exchangeRate = 2, reason = "x rate" }, a.TsasFinance)).Status);
        Assert.Equal(200, (await api.CallAsync("PATCH", $"/assets/{id}/financial", body, a.TsasFinance)).Status);
        Assert.Contains(await AuditWorld.AuditAsync(api, id), e => e is { Field: "originalCost", OldValue: null, NewValue: "1000.00", Reason: "Invoice INV-1" });
    }

    // ── AU-05 / AU-07 (through the API) ───────────────────────────────────────

    [Fact]
    public async Task AU_05_worked_example_through_the_api_with_a_capitalised_upgrade_approved_in_month_13()
    {
        // $1,200 laptop in use from October 2025; 48-month straight-line (Computers & Laptops policy).
        var id = await a.RegisterLaptopAsync(api, "SN-WORKED-1");
        await a.CapitaliseAsync(api, id, 1200m, "2025-10-15", a.TsasFinance);

        api.SetNow("2026-10-14T08:00:00Z"); // month 13
        var upgrade = await api.CallAsync("POST", $"/assets/{id}/upgrades", new
        {
            upgradeDate = "2026-10-10", description = "RAM upgrade", beforeText = "16 GB", afterText = "32 GB", cost = 144.00, supplier = "Axis", invoiceNo = "INV-144",
            fieldChanges = new { ram = "32" },
        }, a.TsasIt);
        Assert.Equal(201, upgrade.Status);
        var decided = await api.CallAsync("POST", $"/upgrades/{upgrade.Body.Int("id")}/decision", new { treatment = "capitalise", comment = "Adds capacity" }, a.TsasFinance);
        Assert.Equal((200, "capitalised", "2026-10"), (decided.Status, decided.Body.Str("status"), decided.Body.Str("capitalisedPeriod")));

        var card = (await api.CallAsync("GET", $"/assets/{id}/card", token: a.TsasFinance)).Body;
        var value = card.Prop("value");
        Assert.Equal((1200.00m, 144.00m, 1344.00m), (value.Prop("originalCost").GetDecimal(), value.Prop("capitalisedUpgrades").GetDecimal(), value.Prop("totalCost").GetDecimal()));
        Assert.Equal((29.00m, 329.00m, 1015.00m), (value.Prop("depreciationMonth").GetDecimal(), value.Prop("accumulatedDepreciation").GetDecimal(), value.Prop("netBookValue").GetDecimal()));
        var schedule = card.Prop("schedule").Items();
        Assert.Equal(13, schedule.Count);
        Assert.All(schedule.Skip(1), m => Assert.Equal(25.00m, m.Prop("charge").GetDecimal()));
        // The approved upgrade updated the specification and the description.
        Assert.Contains("32 GB", card.Prop("asset").Str("name"));
    }

    [Fact]
    public async Task AU_07_an_expensed_upgrade_changes_neither_cost_nor_charge_but_stays_in_the_history()
    {
        var id = await a.RegisterLaptopAsync(api, "SN-EXP-1");
        await a.CapitaliseAsync(api, id, 1200m, "2026-01-05", a.TsasFinance);
        var upgrade = await api.CallAsync("POST", $"/assets/{id}/upgrades", new { upgradeDate = "2026-10-12", description = "Battery replacement", beforeText = "Worn", afterText = "New 54 Wh", cost = 85.00 }, a.TsasIt);

        var decided = await api.CallAsync("POST", $"/upgrades/{upgrade.Body.Int("id")}/decision", new { treatment = "expense", comment = "Like-for-like battery" }, a.TsasFinance);

        Assert.Equal("expensed", decided.Body.Str("status"));
        var card = (await api.CallAsync("GET", $"/assets/{id}/card", token: a.TsasFinance)).Body;
        Assert.Equal((1200m, 0m, 25.00m), (card.Prop("value").Prop("totalCost").GetDecimal(), card.Prop("value").Prop("capitalisedUpgrades").GetDecimal(), card.Prop("value").Prop("depreciationMonth").GetDecimal()));
        Assert.Contains(card.Prop("upgrades").Items(), u => u.Str("status") == "expensed");
        Assert.Contains(card.Prop("timeline").Items(), e => e.Str("title")!.Contains("Upgrade expensed"));
    }

    [Fact]
    public async Task AU_07_only_sbu_finance_of_the_assets_sbu_decides_and_pending_upgrades_are_in_approvals_and_au_11()
    {
        var id = await a.RegisterLaptopAsync(api, "SN-UPG-1");
        await a.CapitaliseAsync(api, id, 900m, "2026-02-01", a.TsasFinance);
        Assert.Equal(403, (await api.CallAsync("POST", $"/assets/{id}/upgrades", new { upgradeDate = "2026-10-01", description = "SSD", beforeText = "256", afterText = "512", cost = 60 }, a.TsasFinance)).Status);
        Assert.Equal(403, (await api.CallAsync("POST", $"/assets/{id}/upgrades", new { upgradeDate = "2026-10-01", description = "SSD", beforeText = "256", afterText = "512", cost = 60 }, a.BakIt)).Status);
        var logged = await api.CallAsync("POST", $"/assets/{id}/upgrades", new { upgradeDate = "2026-10-01", description = "SSD", beforeText = "256", afterText = "512", cost = 60 }, a.TsasStores);
        Assert.Equal(201, logged.Status);
        var upgradeId = logged.Body.Int("id");

        Assert.Contains((await api.CallAsync("GET", "/approvals", token: a.TsasFinance)).Body.Items(), x => x.IntOrNull("upgradeId") == upgradeId);
        Assert.Empty((await api.CallAsync("GET", "/approvals", token: a.BakFinance)).Body.Items());
        var card = (await api.CallAsync("GET", $"/assets/{id}/card", token: a.TsasFinance)).Body;
        Assert.Contains(card.Prop("completenessGaps").Items(), g => g.GetString() == "Upgrade awaiting Finance decision");

        foreach (var token in new[] { a.TsasIt, a.BakFinance, a.GroupFinance, a.W.AdminToken })
            Assert.Equal(403, (await api.CallAsync("POST", $"/upgrades/{upgradeId}/decision", new { treatment = "capitalise" }, token)).Status);
        Assert.Equal(200, (await api.CallAsync("POST", $"/upgrades/{upgradeId}/decision", new { treatment = "capitalise", revisedLifeMonths = 24 }, a.TsasFinance)).Status);
        Assert.Equal(409, (await api.CallAsync("POST", $"/upgrades/{upgradeId}/decision", new { treatment = "expense" }, a.TsasFinance)).Status);
        Assert.Contains(await AuditWorld.AuditAsync(api, id), e => e is { Action: "upgrade_capitalised", UserName: "Chipo Finance" });
    }

    [Fact]
    public async Task AU_05_the_monthly_run_stores_one_row_per_month_and_a_locked_month_gets_an_adjustment_in_the_next_open_month()
    {
        var id = await a.RegisterLaptopAsync(api, "SN-RUN-1");
        await a.CapitaliseAsync(api, id, 1200m, "2026-05-01", a.TsasFinance);

        Assert.Equal(403, (await api.CallAsync("POST", "/depreciation/run", new { period = "2026-09", sbuId = a.W.Tsas.Id }, a.BakFinance)).Status);
        var run = await api.CallAsync("POST", "/depreciation/run", new { period = "2026-09" }, a.TsasFinance);
        Assert.Equal((200, 5, 25.00m), (run.Status, run.Body.Int("monthsWritten"), run.Body.Prop("chargeForPeriod").GetDecimal()));
        Assert.Equal(201, (await api.CallAsync("POST", "/depreciation/locks", new { period = "2026-09" }, a.TsasFinance)).Status);
        Assert.Equal(409, (await api.CallAsync("POST", "/depreciation/locks", new { period = "2026-08" }, a.TsasFinance)).Status);

        // A back-dated cost correction (May–Sep are locked) is caught up in October.
        Assert.Equal(200, (await api.CallAsync("PATCH", $"/assets/{id}/financial", new { originalCost = 1440, reason = "Invoice corrected: freight capitalised" }, a.TsasFinance)).Status);
        await api.CallAsync("POST", "/depreciation/run", new { period = "2026-10" }, a.TsasFinance);

        await using var db = api.NewDbContext();
        var rows = await db.DepreciationEntries.Where(e => e.AssetId == id).OrderBy(e => e.Period).ToListAsync();
        Assert.Equal(["2026-05", "2026-06", "2026-07", "2026-08", "2026-09", "2026-10"], rows.Select(r => r.Period));
        Assert.All(rows.Take(5), r => Assert.Equal((25.00m, 0m), (r.Charge, r.Adjustment))); // locked: never recalculated
        var october = rows[5];
        Assert.Equal(30.00m, october.Charge);      // 1,440 ÷ 48
        Assert.Equal(25.00m, october.Adjustment);  // 5 months × (30 − 25)
        Assert.Equal(180.00m, october.ClosingAccumulated);
        Assert.Equal(1260.00m, october.ClosingNbv);
    }

    // ── AU-06 ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AU_06_the_audit_log_cannot_be_changed_or_deleted()
    {
        var id = await a.RegisterLaptopAsync(api, "SN-AUD-1");
        await using var db = api.NewDbContext();
        var entry = await db.AuditLog.FirstAsync(e => e.AssetId == id);
        db.AccessLog.Add(new Data.AccessLogEntry { Username = "someone", Role = "viewer", Action = "login", At = api.Now });
        await db.SaveChangesAsync();

        var update = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("UPDATE audit_log SET user_name = 'someone else' WHERE id = {0}", entry.Id));
        var delete = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("DELETE FROM audit_log WHERE id = {0}", entry.Id));
        var accessDelete = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("DELETE FROM access_log"));

        Assert.Contains("append-only", update.MessageText);
        Assert.Contains("append-only", delete.MessageText);
        Assert.Contains("append-only", accessDelete.MessageText);
        Assert.Equal(405, (await api.CallAsync("DELETE", $"/assets/{id}/audit", token: a.W.AdminToken)).Status);
    }

    [Fact]
    public async Task AU_06_financial_changes_record_old_value_new_value_user_time_and_reason_and_appear_on_the_timeline()
    {
        var id = await a.RegisterLaptopAsync(api, "SN-AUD-2");
        await a.CapitaliseAsync(api, id, 1000m, "2026-03-01", a.TsasFinance);
        api.SetNow("2026-10-20T09:30:00Z");
        await api.CallAsync("PATCH", $"/assets/{id}/financial", new { originalCost = 1100, reason = "Credit note reversed" }, a.TsasFinance);

        var audit = (await api.CallAsync("GET", $"/assets/{id}/audit", token: a.TsasFinance)).Body.Items();
        var change = audit.First(e => e.Str("field") == "originalCost" && e.Str("newValue") == "1100.00");
        Assert.Equal(("1000.00", "Credit note reversed", "Chipo Finance", "2026-10-20T09:30:00.000Z"),
            (change.Str("oldValue"), change.Str("reason"), change.Str("userName"), change.Str("at")));

        var timeline = (await api.CallAsync("GET", $"/assets/{id}/card", token: a.TsasFinance)).Body.Prop("timeline").Items();
        Assert.Contains(timeline, e => e.Str("title") == "Capitalised");
        Assert.Contains(timeline, e => e.Str("title") == "Asset number issued by Business Central");
        Assert.Contains(timeline, e => e.Str("title") == "Purchased");
        Assert.Equal(timeline.Select(e => e.Str("at")).OrderByDescending(t => t, StringComparer.Ordinal), timeline.Select(e => e.Str("at"))); // newest first
    }

    // ── AU-08 ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AU_08_the_card_opens_by_asset_number_or_tag_scan_and_exports_to_pdf_and_excel()
    {
        var id = await a.RegisterLaptopAsync(api, "SN-CARD-1");
        await a.CapitaliseAsync(api, id, 1280m, "2026-02-19", a.TsasFinance);
        await api.CallAsync("POST", $"/assets/{id}/tag", new { rfidTag = "E280 6894 0000 31" }, a.TsasIt);
        var number = (await api.CallAsync("GET", $"/assets/{id}", token: a.TsasIt)).Body.Str("assetNumber")!;

        Assert.Equal(id, (await api.CallAsync("GET", $"/assets/lookup?code={number.ToLowerInvariant()}", token: a.TsasFinance)).Body.Int("assetId"));
        Assert.Equal(id, (await api.CallAsync("GET", "/assets/lookup?code=E280%206894%200000%2031", token: a.TsasFinance)).Body.Int("assetId"));
        Assert.Equal(404, (await api.CallAsync("GET", "/assets/lookup?code=NOPE-1", token: a.TsasFinance)).Status);

        var card = (await api.CallAsync("GET", $"/assets/{id}/card", token: a.TsasFinance)).Body;
        Assert.Equal(("SN-CARD-1", number), (card.Prop("asset").Str("serialNumber"), card.Prop("asset").Str("assetNumber")));
        Assert.Contains(card.Prop("attributes").Items(), x => x.Str("key") == "processor" && x.Str("value") == "i5-1345U");
        Assert.True(card.Prop("can").Bool("editFinancial"));

        using var pdf = await Download($"/assets/{id}/card.pdf", a.TsasFinance);
        Assert.Equal("application/pdf", pdf.Content.Headers.ContentType!.MediaType);
        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(await pdf.Content.ReadAsByteArrayAsync())[..4]);

        using var xlsx = await Download($"/assets/{id}/depreciation.xlsx", a.TsasFinance);
        using var book = new XLWorkbook(await xlsx.Content.ReadAsStreamAsync());
        Assert.Contains("Depreciation schedule", book.Worksheet(1).Cell(1, 1).GetString());
    }

    private async Task<HttpResponseMessage> Download(string path, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api" + path);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var response = await api.Client.SendAsync(request);
        Assert.Equal(200, (int)response.StatusCode);
        return response;
    }
}
