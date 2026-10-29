using AssetIQ.Api.Domain;
using AssetIQ.Api.Services;
using AssetIQ.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace AssetIQ.Api.Tests.Integration;

/// <summary>AU-01 description, AU-02 serial, AU-03 asset number, GI-02 MAC, AM-01 non-IT ownership.</summary>
[Collection(ApiCollection.Name)]
public sealed class AssetRegistrationTests(ApiFixture api) : IAsyncLifetime
{
    private AuditWorld a = null!;

    public async Task InitializeAsync()
    {
        await api.ResetAsync();
        a = await AuditWorld.SeedAsync(api);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // ── AU-01 ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AU_01_an_asset_without_a_category_cannot_be_saved()
    {
        var res = await api.CallAsync("POST", "/assets", new { currentLocationId = a.W.Office.Id, assignedTo = "IT store", condition = "new", sbuId = a.W.Tsas.Id }, a.TsasIt);

        Assert.Equal(400, res.Status);
        Assert.Contains("categoryId: Required", res.Error());
        // The database refuses one too.
        await using var db = api.NewDbContext();
        var ex = await Assert.ThrowsAsync<DbUpdateException>(async () =>
        {
            db.Assets.Add(new Data.Asset { Name = "x", Category = "x", CreatedAt = api.Now, UpdatedAt = api.Now });
            await db.SaveChangesAsync();
        });
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, ((PostgresException)ex.InnerException!).SqlState); // category_id 0 does not exist
    }

    [Fact]
    public async Task AU_01_the_api_rejects_a_required_template_field_missing_or_a_field_from_another_category()
    {
        var body = AuditWorld.LaptopBody("SN-NEW-1", a.W.Tsas.Id, a.W.Office.Id);
        var attributes = (Dictionary<string, object?>)body.GetType().GetProperty("attributes")!.GetValue(body)!;
        attributes.Remove("processor");
        attributes["mileage"] = 1200;

        var res = await api.CallAsync("POST", "/assets", body, a.TsasIt);

        Assert.Equal(400, res.Status);
        Assert.Contains("attributes.processor: Processor is required for Computers & Laptops", res.Error());
        Assert.Contains("attributes.mileage: is not a field of Computers & Laptops", res.Error());
    }

    [Fact]
    public async Task AU_01_location_custodian_and_condition_are_mandatory_and_the_description_cannot_be_typed()
    {
        var missing = await api.CallAsync("POST", "/assets", new { categoryId = Categories.Computers }, a.TsasIt);
        var typed = await api.CallAsync("POST", "/assets", new { categoryId = Categories.Computers, name = "My laptop" }, a.TsasIt);

        Assert.Contains("currentLocationId: Required", missing.Error());
        Assert.Contains("assignedTo: Required", missing.Error());
        Assert.Contains("condition: Required", missing.Error());
        Assert.Equal(400, typed.Status);
        Assert.Contains("description is generated", typed.Error());
    }

    [Fact]
    public async Task AU_01_the_description_is_generated_rebuilt_on_change_and_every_change_is_audit_logged()
    {
        var id = await a.RegisterLaptopAsync(api, "5CG4182KXL");
        var created = await api.CallAsync("GET", $"/assets/{id}", token: a.TsasIt);
        Assert.Equal("Laptop – Dell Latitude 5440, i5-1345U, 16 GB, 512 GB SSD", created.Body.Str("name"));

        var res = await api.CallAsync("PATCH", $"/assets/{id}", new { attributes = new { ram = 32 }, reason = "RAM corrected" }, a.TsasIt);

        Assert.Equal(200, res.Status);
        Assert.Equal("Laptop – Dell Latitude 5440, i5-1345U, 32 GB, 512 GB SSD", res.Body.Str("name"));
        var audit = await AuditWorld.AuditAsync(api, id);
        Assert.Contains(audit, e => e is { Action: "updated", Field: "attributes.ram", OldValue: "16", NewValue: "32", Reason: "RAM corrected" });
        Assert.Contains(audit, e => e.Field == "description" && e.NewValue!.Contains("32 GB"));
        Assert.Contains(audit, e => e.Action == "created");
    }

    [Fact]
    public async Task AU_01_only_sbu_finance_may_change_the_category_and_must_fill_in_the_new_categorys_required_fields()
    {
        var id = await a.RegisterLaptopAsync(api, "SN-CAT-1");
        var asIt = await api.CallAsync("POST", $"/assets/{id}/category", new { categoryId = Categories.Peripherals, reason = "wrong class" }, a.TsasIt);
        var incomplete = await api.CallAsync("POST", $"/assets/{id}/category", new { categoryId = Categories.Peripherals, subCategory = "Monitor", reason = "wrong class" }, a.TsasFinance);
        var done = await api.CallAsync("POST", $"/assets/{id}/category", new
        {
            categoryId = Categories.Peripherals, subCategory = "Monitor", reason = "Registered in the wrong class",
            attributes = new { make = "Dell", model = "P2425H", serial_number = "SN-CAT-1" },
        }, a.TsasFinance);

        Assert.Equal(403, asIt.Status);
        Assert.Equal(400, incomplete.Status);
        Assert.Contains("Brand is required", incomplete.Error());
        Assert.Equal(200, done.Status);
        Assert.Equal(("Printers, Projectors & UPS", "Monitor – Dell P2425H"), (done.Body.Str("categoryName"), done.Body.Str("name")));
        Assert.Contains(await AuditWorld.AuditAsync(api, id), e => e is { Action: "category_changed", OldValue: "Computers & Laptops", Reason: "Registered in the wrong class" });
    }

    // ── AU-02 / GI-02 ─────────────────────────────────────────────────────────

    [Fact]
    public async Task AU_02_rejects_a_duplicate_serial_across_sbus_names_the_holder_and_logs_the_attempt()
    {
        var tsasId = await a.RegisterLaptopAsync(api, "5CG4182KXL");
        var number = (await api.CallAsync("GET", $"/assets/{tsasId}", token: a.TsasIt)).Body.Str("assetNumber");

        // BAK tries the same serial (typed in lower case).
        var res = await api.CallAsync("POST", "/assets", AuditWorld.LaptopBody("5cg4182kxl", a.W.Bak.Id, a.W.BakWarehouse.Id), a.BakIt);

        Assert.Equal(409, res.Status);
        Assert.Contains($"already used by {number}", res.Error());
        Assert.Contains("this attempt has been logged", res.Error());
        Assert.Contains(await AuditWorld.AuditAsync(api, tsasId), e => e is { Action: "duplicate_serial_attempt", NewValue: "5CG4182KXL" });
        await using var db = api.NewDbContext();
        Assert.Equal(1, await db.Assets.CountAsync(x => x.SerialNumber == "5CG4182KXL"));
    }

    [Fact]
    public async Task AU_02_the_database_itself_refuses_a_duplicate_serial()
    {
        await using var db = api.NewDbContext();
        db.Assets.Add(new Data.Asset { Name = "x", Category = "Laptop", CategoryId = Categories.Computers, SerialNumber = "SN-DL-001", CreatedAt = api.Now, UpdatedAt = api.Now });
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal("assets_serial_number_unique", ((PostgresException)ex.InnerException!).ConstraintName);
    }

    [Fact]
    public async Task AU_02_no_serial_needs_a_reason_and_sbu_finance_approval_and_is_incomplete_until_approved()
    {
        var desk = new
        {
            categoryId = Categories.Furniture, subCategory = "Desk", sbuId = a.W.Tsas.Id, currentLocationId = a.W.Office.Id, assignedTo = "Finance office", condition = "good",
            attributes = new { make = "Generic", model = "Executive", material = "Oak", room = "Floor 2" },
        };
        var withoutReason = await api.CallAsync("POST", "/assets", desk, a.TsasAdmin);
        Assert.Equal(400, withoutReason.Status);
        Assert.Contains("noSerialReason", withoutReason.Error());

        var created = await api.CallAsync("POST", "/assets", new
        {
            desk.categoryId, desk.subCategory, desk.sbuId, desk.currentLocationId, desk.assignedTo, desk.condition, desk.attributes,
            noSerialReason = "Built-in furniture, no manufacturer label",
        }, a.TsasAdmin);
        Assert.Equal(201, created.Status);
        var id = created.Body.Int("id");
        Assert.Equal(("pending", null), (created.Body.Str("noSerialStatus"), created.Body.Str("serialNumber")));

        var gapsBefore = await Gaps(id);
        Assert.Contains("\"No serial\" reason awaiting SBU Finance approval", gapsBefore);
        Assert.Contains((await api.CallAsync("GET", "/approvals", token: a.TsasFinance)).Body.Items(), x => x.Str("type") == "no_serial" && x.Int("assetId") == id);

        Assert.Equal(403, (await api.CallAsync("POST", $"/assets/{id}/no-serial-decision", new { approve = true }, a.BakFinance)).Status);
        var approved = await api.CallAsync("POST", $"/assets/{id}/no-serial-decision", new { approve = true, comment = "Agreed" }, a.TsasFinance);
        Assert.Equal(("approved"), approved.Body.Str("noSerialStatus"));
        Assert.DoesNotContain(await Gaps(id), g => g.Contains("No serial"));
    }

    [Fact]
    public async Task GI_02_mac_is_optional_but_unique_when_entered()
    {
        await a.RegisterLaptopAsync(api, "SN-MAC-1"); // no MAC
        await a.RegisterLaptopAsync(api, "SN-MAC-2", mac: "a4-5d-36-12-ab-cd");

        var dup = await api.CallAsync("POST", "/assets", AuditWorld.LaptopBody("SN-MAC-3", a.W.Tsas.Id, a.W.Office.Id, mac: "A4:5D:36:12:AB:CD"), a.TsasIt);
        var bad = await api.CallAsync("POST", "/assets", AuditWorld.LaptopBody("SN-MAC-4", a.W.Tsas.Id, a.W.Office.Id, mac: "12:34"), a.TsasIt);

        Assert.Equal(409, dup.Status);
        Assert.Contains("MAC address A4:5D:36:12:AB:CD is already used", dup.Error());
        Assert.Equal(400, bad.Status);
    }

    // ── AU-03 ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AU_03_business_central_issues_the_asset_number_in_the_sbu_class_year_format()
    {
        var id = await a.RegisterLaptopAsync(api, "SN-BC-1");
        var asset = (await api.CallAsync("GET", $"/assets/{id}", token: a.TsasIt)).Body;

        Assert.Equal("in_store", asset.Str("status")); // Phase 3: pending asset number → in store
        Assert.Matches(@"^TSAS-IT-2026-\d{5}$", asset.Str("assetNumber")!);
        Assert.NotEqual("TSAS-IT-2026-00001", asset.Str("assetNumber")); // already used by the seeded laptop: skipped
    }

    [Fact]
    public async Task AU_03_while_bc_is_unreachable_the_asset_is_pending_cannot_be_tagged_or_capitalised_and_the_request_is_retried()
    {
        var bc = api.Services.GetRequiredService<MockBcFixedAssetClient>();
        bc.Available = false;
        var id = await a.RegisterLaptopAsync(api, "SN-BC-DOWN");

        var pending = (await api.CallAsync("GET", $"/assets/{id}", token: a.TsasIt)).Body;
        Assert.Equal(("pending_asset_number", null), (pending.Str("status"), pending.Str("assetNumber")));
        Assert.Equal(409, (await api.CallAsync("POST", $"/assets/{id}/tag", new { rfidTag = "TAG-NEW" }, a.TsasIt)).Status);
        await api.CallAsync("PATCH", $"/assets/{id}/financial", new { originalCost = 1000, currency = "USD", exchangeRate = 1, inUseDate = "2026-10-01", reason = "Invoice" }, a.TsasFinance);
        var cap = await api.CallAsync("POST", $"/assets/{id}/capitalise", new { }, a.TsasFinance);
        Assert.Equal(409, cap.Status);
        Assert.Contains("asset number is pending", cap.Error());
        Assert.Contains("Asset number pending from Business Central", await Gaps(id));
        await using (var db = api.NewDbContext())
        {
            var request = await db.BcNumberRequests.SingleAsync(r => r.AssetId == id);
            Assert.Equal(("queued", 1), (request.Status, request.Attempts));
            Assert.Contains("not reachable", request.LastError);
        }

        // BC comes back; the retry is due after the back-off.
        bc.Available = true;
        Assert.Equal(0, await AuditWorld.ProcessBcQueueAsync(api)); // not due yet
        api.SetNow(api.Clock.GetUtcNow().AddMinutes(2));
        Assert.Equal(1, await AuditWorld.ProcessBcQueueAsync(api));

        var issued = (await api.CallAsync("GET", $"/assets/{id}", token: a.TsasIt)).Body;
        Assert.Equal("in_store", issued.Str("status"));
        Assert.NotNull(issued.Str("assetNumber"));
        Assert.Equal(200, (await api.CallAsync("POST", $"/assets/{id}/tag", new { rfidTag = "TAG-NEW" }, a.TsasIt)).Status);
    }

    [Fact]
    public async Task AU_03_asset_numbers_cannot_be_edited_or_freed_by_deleting_the_asset()
    {
        var id = await a.RegisterLaptopAsync(api, "SN-LOCK-1");
        await using var db = api.NewDbContext();

        var update = await Assert.ThrowsAsync<PostgresException>(() =>
            db.Database.ExecuteSqlRawAsync("UPDATE assets SET asset_number = 'TSAS-IT-2026-99999' WHERE id = {0}", id));
        var delete = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("DELETE FROM assets WHERE id = {0}", id));
        var apiDelete = await api.CallAsync("DELETE", $"/assets/{id}", token: a.W.GroupItToken);

        Assert.Contains("cannot be changed", update.MessageText);
        Assert.Contains("cannot be deleted", delete.MessageText);
        Assert.Equal(409, apiDelete.Status);
        Assert.Contains("asset numbers are never reused", apiDelete.Error());
    }

    [Fact]
    public async Task AU_03_imported_assets_keep_their_old_number_as_previous_asset_number()
    {
        var body = new
        {
            categoryId = Categories.Computers, subCategory = "Laptop", sbuId = a.W.Tsas.Id, currentLocationId = a.W.Office.Id, assignedTo = "IT store", condition = "good",
            previousAssetNumber = "TSAS/LAP/118",
            attributes = new { make = "HP", model = "ProBook 450", serial_number = "SN-IMP-1", processor = "i5", ram = 8, storage = "256 GB SSD", operating_system = "Windows 10" },
        };
        var res = await api.CallAsync("POST", "/assets", body, a.TsasIt);

        Assert.Equal(201, res.Status);
        Assert.Equal("TSAS/LAP/118", res.Body.Str("previousAssetNumber"));
        Assert.NotEqual("TSAS/LAP/118", res.Body.Str("assetNumber"));
        var lookup = await api.CallAsync("GET", "/assets/lookup?code=tsas/lap/118", token: a.TsasIt);
        Assert.Equal(res.Body.Int("id"), lookup.Body.Int("assetId"));
    }

    // ── AM-01 ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AM_01_only_the_sbus_admin_manager_may_register_or_edit_non_it_assets()
    {
        var vehicle = new
        {
            categoryId = Categories.Vehicles, subCategory = "Light vehicle", sbuId = a.W.Tsas.Id, currentLocationId = a.W.Office.Id, assignedTo = "Pool", condition = "good",
            attributes = new
            {
                registration_number = "AGR 4471", vin = "AHTFR22G406012345", engine_number = "2GD-123456", make = "Toyota", model = "Hilux", year = 2021,
                colour = "White", capacity = "1 t", mileage = 142300, fuel_type = "Diesel",
            },
        };
        var asIt = await api.CallAsync("POST", "/assets", vehicle, a.TsasIt);
        var asAdmin = await api.CallAsync("POST", "/assets", vehicle, a.W.GroupItToken);
        var otherSbu = await api.CallAsync("POST", "/assets", vehicle, a.BakAdmin);
        var ok = await api.CallAsync("POST", "/assets", vehicle, a.TsasAdmin);

        Assert.Equal((403, 403, 403, 201), (asIt.Status, asAdmin.Status, otherSbu.Status, ok.Status));
        Assert.Equal("Toyota Hilux 2021 – AGR 4471", ok.Body.Str("name"));
        Assert.Equal("AHTFR22G406012345", ok.Body.Str("serialNumber")); // the VIN is the vehicle's identifier
        Assert.Matches(@"^TSAS-MV-2026-\d{5}$", ok.Body.Str("assetNumber")!);
        var id = ok.Body.Int("id");
        Assert.Equal(403, (await api.CallAsync("PATCH", $"/assets/{id}", new { notes = "x" }, a.TsasIt)).Status);
        Assert.Equal(200, (await api.CallAsync("PATCH", $"/assets/{id}", new { attributes = new { mileage = 150000 } }, a.TsasAdmin)).Status);
    }

    [Fact]
    public async Task Scope_an_sbu_role_cannot_register_for_another_sbu()
    {
        var res = await api.CallAsync("POST", "/assets", AuditWorld.LaptopBody("SN-X-SBU", a.W.Bak.Id, a.W.BakWarehouse.Id), a.TsasIt);
        Assert.Equal(403, res.Status);
    }

    private async Task<List<string>> Gaps(int id) =>
        (await api.CallAsync("GET", $"/assets/{id}/card", token: a.TsasFinance)).Body.Prop("completenessGaps").Items().Select(g => g.GetString()!).ToList();
}
