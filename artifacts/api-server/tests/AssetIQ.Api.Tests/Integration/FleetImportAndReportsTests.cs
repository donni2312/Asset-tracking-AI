using System.Net.Http.Headers;
using System.Text;
using AssetIQ.Api.Data;
using AssetIQ.Api.Domain;
using AssetIQ.Api.Services;
using AssetIQ.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AssetIQ.Api.Tests.Integration;

/// <summary>
/// Phase 4d: the fleet register with documents and service schedules (AM-02 … AM-04), bulk import with its validation
/// report (SF-10), the standard Finance reports (GF-06), the register filters (SF-02), the cost centre (SF-01), and the
/// IT inventory, refresh and status views (GI-01, GI-03, SI-06).
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class FleetImportAndReportsTests(ApiFixture api) : IAsyncLifetime
{
    private RoleWorld r = null!;
    private Asset hilux = null!;
    private Asset bakTruck = null!;

    public async Task InitializeAsync()
    {
        await api.ResetAsync();
        r = await RoleWorld.SeedAsync(api);
        hilux = await VehicleAsync("Toyota Hilux 2.4 GD-6 – AEZ 1234", "AEZ 1234", r.W.Tsas.Id, r.W.Office.Id, 10_000);
        bakTruck = await VehicleAsync("Isuzu NPR 400 – ADF 9001", "ADF 9001", r.W.Bak.Id, r.W.BakWarehouse.Id, 80_000);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<Asset> VehicleAsync(string name, string reg, int sbuId, int locationId, int mileage)
    {
        await using var db = api.NewDbContext();
        var fields = await db.CategoryTemplateFields.Where(f => f.CategoryId == Categories.Vehicles).ToDictionaryAsync(f => f.Key, f => f.Id);
        var asset = new Asset
        {
            Name = name, Category = "Light vehicle", CategoryId = Categories.Vehicles, SubCategory = "Light vehicle", SerialNumber = $"VIN-{reg.Replace(" ", "")}",
            AssetNumber = $"V-{reg.Replace(" ", "")}", Status = AssetStatuses.InUse, SbuId = sbuId, CurrentLocationId = locationId, AssignedTo = "Pool",
            CreatedAt = api.Now, UpdatedAt = api.Now,
            Attributes =
            [
                new AssetAttributeValue { FieldId = fields["registration_number"], ValueText = reg },
                new AssetAttributeValue { FieldId = fields["make"], ValueText = "Toyota" },
                new AssetAttributeValue { FieldId = fields["model"], ValueText = "Hilux" },
                new AssetAttributeValue { FieldId = fields["mileage"], ValueText = mileage.ToString(), ValueNumber = mileage },
            ],
        };
        db.Assets.Add(asset);
        await db.SaveChangesAsync();
        return asset;
    }

    private async Task<int> RemindAsync()
    {
        using var scope = api.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<FleetService>().SendRemindersAsync(CancellationToken.None);
    }

    private async Task<int> NotificationsAsync(string role, string kind) =>
        (await api.CallAsync("GET", "/notifications", token: r[role])).Body.Items().Count(n => n.Str("kind") == kind);

    [Fact]
    public async Task AM_02_the_admin_manager_sees_their_sbus_fleet_and_group_roles_a_read_only_roll_up()
    {
        var own = await api.CallAsync("GET", "/fleet", token: r[Roles.SbuAdmin]);
        Assert.Equal(200, own.Status);
        var row = own.Body.Items().Single();
        Assert.Equal(("AEZ 1234", "active", 10_000, "km", true), (row.Str("registrationNumber"), row.Str("fleetStatus"), row.Int("reading"), row.Str("readingUnit"), row.Bool("canEdit")));

        var group = await api.CallAsync("GET", "/fleet", token: r[Roles.GroupFinance]);
        Assert.Equal(2, group.Body.Items().Count);
        Assert.All(group.Body.Items(), x => Assert.False(x.Bool("canEdit")));
        Assert.Equal(403, (await api.CallAsync("GET", "/fleet", token: r[Roles.Hod])).Status);
        Assert.Equal(403, (await api.CallAsync("GET", $"/fleet/{bakTruck.Id}", token: r[Roles.SbuAdmin])).Status);

        Assert.Equal(403, (await api.CallAsync("PATCH", $"/fleet/{hilux.Id}", new { fleetStatus = "off_road" }, r[Roles.GroupFinance])).Status);
        var updated = await api.CallAsync("PATCH", $"/fleet/{hilux.Id}", new { fleetStatus = "off_road", driverEmployeeId = r.TsasStaff.Id, reading = 10_450 }, r[Roles.SbuAdmin]);
        Assert.True(updated.Status == 200, updated.Error());
        Assert.Equal(("off_road", "John Doe", 10_450), (updated.Body.Str("fleetStatus"), updated.Body.Str("driver"), updated.Body.Int("reading")));
        Assert.Equal(400, (await api.CallAsync("PATCH", $"/fleet/{hilux.Id}", new { reading = 9_000 }, r[Roles.SbuAdmin])).Status); // the clock does not go back
        Assert.Equal(400, (await api.CallAsync("PATCH", $"/fleet/{hilux.Id}", new { driverEmployeeId = r.BakStaff.Id }, r[Roles.SbuAdmin])).Status);
    }

    [Fact]
    public async Task AM_03_documents_remind_at_30_and_7_days_and_an_expired_one_flags_the_vehicle_red()
    {
        // Today is 2026-10-14.
        Assert.Equal(201, (await api.CallAsync("POST", $"/fleet/{hilux.Id}/documents", new { kind = "insurance", reference = "POL-77", expiresOn = "2026-11-03" }, r[Roles.SbuAdmin])).Status);
        var added = await api.CallAsync("POST", $"/fleet/{hilux.Id}/documents", new { kind = "zinara", expiresOn = "2026-10-10" }, r[Roles.SbuAdmin]);
        Assert.True(added.Body.Bool("documentsExpired"));
        Assert.Equal(["due_30", "expired"], added.Body.Prop("documents").Items().Select(d => d.Str("state")).ToList());
        Assert.Equal(403, (await api.CallAsync("POST", $"/fleet/{hilux.Id}/documents", new { kind = "licence", expiresOn = "2027-01-01" }, r[Roles.SbuFinance])).Status);

        Assert.Equal(2, await RemindAsync());
        Assert.Equal(0, await RemindAsync());
        Assert.Equal(2, await NotificationsAsync(Roles.SbuAdmin, "fleet"));

        api.SetNow("2026-10-29T08:00:00Z"); // the insurance now expires in 5 days
        Assert.Equal(1, await RemindAsync());
        var renewed = await api.CallAsync("POST", $"/fleet/{hilux.Id}/documents", new { kind = "zinara", expiresOn = "2027-10-10" }, r[Roles.SbuAdmin]);
        Assert.False(renewed.Body.Bool("documentsExpired"));
        Assert.Equal(2, renewed.Body.Prop("documents").Items().Count); // the renewal replaced the expired one
    }

    [Fact]
    public async Task AM_04_the_next_service_is_due_by_mileage_or_date_overdue_is_alerted_once_and_finance_records_the_cost()
    {
        var set = await api.CallAsync("PUT", $"/fleet/{hilux.Id}/service-schedule", new { intervalReading = 5_000, intervalDays = 180 }, r[Roles.SbuAdmin]);
        Assert.True(set.Status == 200, set.Error());
        var due = set.Body.Prop("nextService");
        Assert.Equal((15_000, "2027-04-12", "ok"), (due.Int("dueReading"), due.Str("dueDate"), due.Str("state")));

        var overdue = await api.CallAsync("PATCH", $"/fleet/{hilux.Id}", new { reading = 15_200 }, r[Roles.SbuAdmin]);
        Assert.Equal("overdue", overdue.Body.Prop("nextService").Str("state"));
        Assert.Equal(1, await RemindAsync());
        Assert.Equal(0, await RemindAsync());

        var logged = await api.CallAsync("POST", $"/fleet/{hilux.Id}/services", new { serviceDate = "2026-10-13", reading = 15_250, garage = "Toyota Zimbabwe", workDone = "15 000 km service" }, r[Roles.SbuAdmin]);
        Assert.True(logged.Status == 201, logged.Error());
        Assert.Equal((20_250, "ok"), (logged.Body.Prop("nextService").Int("dueReading"), logged.Body.Prop("nextService").Str("state")));
        var serviceId = logged.Body.Prop("services").Items().Single().Int("id");
        Assert.Equal(1, await NotificationsAsync(Roles.SbuFinance, "service"));
        Assert.Equal(400, (await api.CallAsync("POST", $"/fleet/{hilux.Id}/services", new { serviceDate = "2026-12-01", garage = "X Garage", workDone = "Future" }, r[Roles.SbuAdmin])).Status);

        Assert.Equal(403, (await api.CallAsync("PATCH", $"/service-logs/{serviceId}/cost", new { cost = 420m }, r[Roles.SbuAdmin])).Status);
        var costed = await api.CallAsync("PATCH", $"/service-logs/{serviceId}/cost", new { cost = 420m, currency = "usd" }, r[Roles.SbuFinance]);
        Assert.Equal((420m, "USD"), (costed.Body.Prop("services").Items().Single().Prop("cost").GetDecimal(), costed.Body.Prop("services").Items().Single().Str("currency")));
        var admin = await api.CallAsync("GET", $"/fleet/{hilux.Id}", token: r[Roles.SbuAdmin]);
        Assert.True(admin.Body.Prop("services").Items().Single().IsNull("cost")); // money stays with Finance
    }

    // ── SF-10 ───────────────────────────────────────────────────────────────

    private const string Header = "category,sub_category,sbu,location,previous_asset_number,rfid_tag,make,model,serial_number,processor,ram,storage,operating_system,purchase_date,cost";

    private string Line(string serial, string location, string tag = "", string previous = "OLD-1") =>
        $"COMP,Laptop,TSAS,{location},{previous},{tag},Dell,Latitude 5420,{serial},i5,16,512 GB SSD,Windows 11,2023-02-01,1100";

    private async Task<(int Status, System.Text.Json.JsonElement Body)> UploadAsync(string path, string csv, string role)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(csv));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        form.Add(file, "file", "register.csv");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api{path}") { Content = form };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", r[role]);
        using var response = await api.Client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        return ((int)response.StatusCode, text.Length == 0 ? default : System.Text.Json.JsonDocument.Parse(text).RootElement.Clone());
    }

    [Fact]
    public async Task SF_10_the_validation_report_lists_every_error_before_anything_is_imported()
    {
        using (var request = new HttpRequestMessage(HttpMethod.Get, $"/api/imports/assets/template.csv?categoryId={Categories.Computers}"))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", r[Roles.SbuFinance]);
            using var response = await api.Client.SendAsync(request);
            var text = await response.Content.ReadAsStringAsync();
            Assert.Equal(200, (int)response.StatusCode);
            Assert.StartsWith("category,sub_category,sbu,location", text.TrimStart('﻿'));
            Assert.Contains("serial_number", text);
        }
        Assert.Equal(403, (await UploadAsync("/imports/assets/validate", Header, Roles.SbuIt)).Status);

        var bad = string.Join("\n", Header,
            Line("SN-IMP-1", r.W.Office.Name, "TAG-IMP-1"),
            Line("SN-DL-001", r.W.Office.Name),           // the serial of the test laptop
            Line("SN-IMP-1", "Nowhere"),                   // repeats row 2, and the location does not exist
            Line("SN-IMP-4", r.W.Office.Name, "TAG-DL-001")); // the laptop's tag
        var report = await UploadAsync("/imports/assets/validate", bad, Roles.SbuFinance);
        Assert.Equal(200, report.Status);
        Assert.Equal((4, 3, false), (report.Body.Int("rowCount"), report.Body.Int("errorRowCount"), report.Body.Bool("canImport")));
        var rows = report.Body.Prop("rows").Items();
        Assert.Empty(rows[0].Prop("errors").Items());
        Assert.Contains(rows[1].Prop("errors").Items(), e => e.GetString()!.Contains("already registered"));
        Assert.Contains(rows[2].Prop("errors").Items(), e => e.GetString()!.Contains("also on row 2"));
        Assert.Contains(rows[2].Prop("errors").Items(), e => e.GetString()!.StartsWith("location:"));
        Assert.Contains(rows[3].Prop("errors").Items(), e => e.GetString()!.Contains("rfid_tag TAG-DL-001: already used"));

        var refused = await UploadAsync("/imports/assets", bad, Roles.SbuFinance);
        Assert.Equal(409, refused.Status);
        await using (var db = api.NewDbContext())
            Assert.False(await db.Assets.IgnoreQueryFilters().AnyAsync(a => a.SerialNumber == "SN-IMP-1")); // all or nothing

        var good = string.Join("\n", Header, Line("SN-IMP-1", r.W.Office.Name, "TAG-IMP-1", "TSAS/IT/0091"), Line("sn-imp-2", r.W.Office.Name));
        var imported = await UploadAsync("/imports/assets", good, Roles.SbuFinance);
        Assert.Equal(201, imported.Status);
        Assert.Equal(2, imported.Body.Int("imported"));
        await using (var db = api.NewDbContext())
        {
            var first = await db.Assets.IgnoreQueryFilters().SingleAsync(a => a.SerialNumber == "SN-IMP-1");
            Assert.Equal(("TSAS/IT/0091", "TAG-IMP-1", r.W.Tsas.Id, 1100m), (first.PreviousAssetNumber, first.RfidTag, first.SbuId!.Value, first.OriginalCost!.Value));
            Assert.True(await db.AuditLog.AnyAsync(a => a.AssetId == first.Id && a.Action == "imported"));
        }
        var bakRow = string.Join("\n", Header, Line("SN-IMP-9", r.W.BakWarehouse.Name).Replace(",TSAS,", ",BAK,"));
        Assert.Contains((await UploadAsync("/imports/assets/validate", bakRow, Roles.SbuFinance)).Body.Prop("rows").Items()[0].Prop("errors").Items(),
            e => e.GetString()!.StartsWith("sbu: you may not import"));

        // The SBU Admin Manager imports too (product owner, 30 Oct 2026): non-IT rows of their own SBU.
        var furniture = string.Join("\n", "category,sub_category,location,no_serial_reason,make,model,material,room",
            $"FF,Desk,{r.W.Office.Name},Unbranded office furniture,Generic,Executive,Wood,Board room");
        var byAdmin = await UploadAsync("/imports/assets", furniture, Roles.SbuAdmin);
        Assert.True(byAdmin.Status == 201, byAdmin.Body.GetRawText());
        Assert.Contains((await UploadAsync("/imports/assets/validate", good, Roles.SbuAdmin)).Body.Prop("rows").Items()[0].Prop("errors").Items(),
            e => e.GetString()!.Contains("is IT equipment"));
        Assert.Equal(403, (await UploadAsync("/imports/assets/validate", furniture, Roles.Stores)).Status);
    }

    // ── GF-06, SF-02, SF-01 ─────────────────────────────────────────────────

    [Fact]
    public async Task GF_06_standard_reports_export_to_excel_and_pdf_and_SF_02_the_register_filters_by_department_and_status()
    {
        foreach (var kind in new[] { "register", "additions", "disposals", "depreciation", "movements" })
        foreach (var format in new[] { "xlsx", "pdf" })
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/finance/reports.{format}?kind={kind}&period=2026-10&sbuId={r.W.Tsas.Id}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", r[Roles.GroupFinance]);
            using var response = await api.Client.SendAsync(request);
            Assert.True((int)response.StatusCode == 200, $"{kind}.{format}: {(int)response.StatusCode}");
            Assert.True((await response.Content.ReadAsByteArrayAsync()).Length > 100);
        }
        Assert.Equal(400, (await api.CallAsync("GET", "/finance/reports.xlsx?kind=bogus&period=2026-10", token: r[Roles.GroupFinance])).Status);
        Assert.Equal(403, (await api.CallAsync("GET", "/finance/reports.xlsx?kind=movements&period=2026-10", token: r[Roles.GroupIt])).Status);

        var all = (await api.CallAsync("GET", "/finance/register?period=2026-10", token: r[Roles.SbuFinance])).Body.Prop("rows").Items().Count;
        var ops = (await api.CallAsync("GET", $"/finance/register?period=2026-10&departmentId={r.TsasOps.Id}", token: r[Roles.SbuFinance])).Body.Prop("rows").Items();
        var inStore = (await api.CallAsync("GET", "/finance/register?period=2026-10&status=in_store", token: r[Roles.SbuFinance])).Body.Prop("rows").Items();
        Assert.True(ops.Count < all && ops.Count >= 1);
        Assert.All(inStore, x => Assert.Equal("in_store", x.Str("status")));

        var financial = await api.CallAsync("PATCH", $"/assets/{r.W.Laptop.Id}/financial", new { costCentre = "TSAS-IT-100", reason = "Cost centre from the GL" }, r[Roles.SbuFinance]);
        Assert.True(financial.Status == 200, financial.Error());
        Assert.Equal("TSAS-IT-100", financial.Body.Str("costCentre"));
    }

    // ── GI-01, GI-03, SI-06 ─────────────────────────────────────────────────

    [Fact]
    public async Task GI_01_GI_03_SI_06_it_inventory_refresh_warranty_alerts_and_status_board()
    {
        await using (var db = api.NewDbContext())
        {
            var laptop = await db.Assets.FindAsync(r.W.Laptop.Id);
            laptop!.PurchaseDate = "2021-06-01";           // over 4 years: due for refresh
            var projector = await db.Assets.FindAsync(r.W.Projector.Id);
            projector!.WarrantyEnds = "2026-11-20";       // within the 60-day warning
            projector.PurchaseDate = "2025-11-20";
            await db.SaveChangesAsync();
        }
        var all = await api.CallAsync("GET", "/it/inventory", token: r[Roles.GroupIt]);
        Assert.Equal(200, all.Status);
        Assert.Contains(all.Body.Items(), x => x.Str("sbuCode") == "BAK");
        Assert.DoesNotContain(all.Body.Items(), x => x.Int("assetId") == hilux.Id); // IT only
        var tsasOnly = await api.CallAsync("GET", "/it/inventory", token: r[Roles.SbuIt]);
        Assert.All(tsasOnly.Body.Items(), x => Assert.Equal("TSAS", x.Str("sbuCode")));
        Assert.Single((await api.CallAsync("GET", "/it/inventory?type=Projector", token: r[Roles.GroupIt])).Body.Items());
        Assert.Contains((await api.CallAsync("GET", "/it/inventory?minAgeYears=5", token: r[Roles.GroupIt])).Body.Items(), x => x.Int("assetId") == r.W.Laptop.Id);
        Assert.Equal(403, (await api.CallAsync("GET", "/it/inventory", token: r[Roles.GroupFinance])).Status);

        var refresh = (await api.CallAsync("GET", "/it/refresh", token: r[Roles.GroupIt])).Body.Items();
        Assert.Contains(refresh, x => x.Int("assetId") == r.W.Laptop.Id && x.Prop("refreshReasons").Items()[0].GetString()!.Contains("refresh at 4"));

        using (var scope = api.Services.CreateScope())
        {
            var it = scope.ServiceProvider.GetRequiredService<ItReportService>();
            Assert.Equal(1, await it.WarrantyAlertsAsync(CancellationToken.None));
            Assert.Equal(0, await it.WarrantyAlertsAsync(CancellationToken.None));
        }
        Assert.Equal(1, await NotificationsAsync(Roles.SbuIt, "warranty"));

        var board = await api.CallAsync("GET", "/it/status-board", token: r[Roles.SbuIt]);
        var counts = board.Body.Prop("counts").Items().ToDictionary(c => c.Str("status")!, c => c.Int("count"));
        Assert.Equal((1, 1), (counts["in_use"], counts["in_store"]));
        Assert.Equal(r.W.Projector.Id, board.Body.Prop("spare").Items().Single().Int("assetId"));
    }
}
