using AssetIQ.Api.Data;
using AssetIQ.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AssetIQ.Api.Tests.Integration;

/// <summary>Endpoints behind the screens: manual movement, location create/edit, dashboard charts, BC sync. Ported from src/routes/screens.test.ts.</summary>
[Collection(ApiCollection.Name)]
public sealed class ScreensTests(ApiFixture api) : IAsyncLifetime
{
    private TestWorld w = null!;

    public async Task InitializeAsync()
    {
        await api.ResetAsync();
        w = await TestWorld.SeedAsync(api);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task AddMovements(params Movement[] movements)
    {
        await using var db = api.NewDbContext();
        db.Movements.AddRange(movements);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Manual_movement_records_who_moved_the_asset_and_updates_its_location()
    {
        var res = await api.CallAsync("POST", "/movements", new
        {
            assetId = w.Projector.Id, toLocationId = w.BakWarehouse.Id, movedBy = "Tendai Moyo", notes = "Loaned for stock count",
        });
        Assert.Equal(201, res.Status);
        Assert.Equal(("manual", "Tendai Moyo", "TSAS IT Office", "BAK Warehouse", "2026-10-14T08:00:00.000Z"),
            (res.Body.Str("triggeredBy"), res.Body.Str("movedBy"), res.Body.Str("fromLocationName"), res.Body.Str("toLocationName"), res.Body.Str("timestamp")));
        await using var db = api.NewDbContext();
        Assert.Equal(w.BakWarehouse.Id, (await db.Assets.AsNoTracking().SingleAsync(a => a.Id == w.Projector.Id)).CurrentLocationId);
    }

    [Fact]
    public async Task Manual_movement_rejects_the_current_location_unknown_locations_and_unknown_assets()
    {
        Assert.Equal(400, (await api.CallAsync("POST", "/movements", new { assetId = w.Projector.Id, toLocationId = w.Office.Id })).Status);
        Assert.Equal(404, (await api.CallAsync("POST", "/movements", new { assetId = w.Projector.Id, toLocationId = 999 })).Status);
        Assert.Equal(404, (await api.CallAsync("POST", "/movements", new { assetId = 999, toLocationId = w.Gate.Id })).Status);
    }

    [Fact]
    public async Task The_movement_log_filters_by_sbu()
    {
        await api.CallAsync("POST", "/movements", new { assetId = w.Projector.Id, toLocationId = w.Gate.Id });
        Assert.Single((await api.CallAsync("GET", $"/movements?sbuId={w.Tsas.Id}")).Body.Items());
        Assert.Empty((await api.CallAsync("GET", $"/movements?sbuId={w.Bak.Id}")).Body.Items());
    }

    [Fact]
    public async Task Location_create_returns_the_enriched_location_and_edits_apply()
    {
        var created = await api.CallAsync("POST", "/locations", new
        {
            name = "TSAS Server Room", type = "server_room", building = "TSAS House", floor = "1", sbuId = w.Tsas.Id,
        });
        Assert.Equal(201, created.Status);
        Assert.Equal(("TSAS Server Room", "TSAS", "Tobacco Sales Administration Services", 0),
            (created.Body.Str("name"), created.Body.Str("sbuCode"), created.Body.Str("sbuName"), created.Body.Int("assetCount")));

        var edited = await api.CallAsync("PATCH", $"/locations/{created.Body.Int("id")}", new { name = "TSAS Data Centre", floor = (string?)null });
        Assert.Equal(200, edited.Status);
        Assert.Equal(("TSAS Data Centre", "server_room"), (edited.Body.Str("name"), edited.Body.Str("type")));
        Assert.True(edited.Body.IsNull("floor"));
        Assert.Equal(404, (await api.CallAsync("PATCH", "/locations/999", new { name = "x" })).Status);
        Assert.Equal(400, (await api.CallAsync("POST", "/locations", new { name = "No type", building = "X" })).Status);
    }

    [Fact]
    public async Task Movement_trend_returns_one_point_per_day()
    {
        await AddMovements(
            new Movement { AssetId = w.Laptop.Id, ToLocationId = w.Gate.Id, TriggeredBy = "rfid", Timestamp = new DateTime(2026, 10, 13, 9, 0, 0, DateTimeKind.Utc) },
            new Movement { AssetId = w.Laptop.Id, ToLocationId = w.Office.Id, TriggeredBy = "manual", Timestamp = new DateTime(2026, 10, 14, 7, 0, 0, DateTimeKind.Utc) });

        var res = await api.CallAsync("GET", "/dashboard/movement-trend?days=3");
        Assert.Equal(200, res.Status);
        Assert.Equal([("2026-10-12", 0), ("2026-10-13", 1), ("2026-10-14", 1)], res.Body.Items().Select(p => (p.Str("date"), p.Int("total"))));
        Assert.Equal(400, (await api.CallAsync("GET", "/dashboard/movement-trend?days=0")).Status);
        Assert.Equal(400, (await api.CallAsync("GET", "/dashboard/movement-trend?days=91")).Status);
        Assert.Equal(14, (await api.CallAsync("GET", "/dashboard/movement-trend")).Body.Items().Count);
    }

    [Fact]
    public async Task Gate_activity_labels_exits_and_entries()
    {
        await using (var db = api.NewDbContext())
        {
            db.GatePasses.Add(new GatePass
            {
                AssetId = w.Laptop.Id, RequestedBy = "John Doe", Reason = "Trip", Status = "approved", ApprovedBy = "R",
                DecidedAt = new DateTime(2026, 10, 13, 8, 0, 0, DateTimeKind.Utc), CreatedAt = api.Now, UpdatedAt = api.Now,
            });
            await db.SaveChangesAsync();
        }
        await api.CallAsync("POST", "/rfid/simulate", new { rfidTag = "TAG-DL-001", readerId = w.GateReader.Id });
        api.SetNow("2026-10-14T09:00:00Z");
        await api.CallAsync("POST", "/rfid/simulate", new { rfidTag = "TAG-DL-001", readerId = w.GateReader.Id });

        var res = await api.CallAsync("GET", "/dashboard/gate-activity?limit=5");
        Assert.Equal(["entry", "exit"], res.Body.Items().Select(g => g.Str("direction")));
    }

    [Fact]
    public async Task Dashboard_summary_counts_assets_passes_movements_and_sbus()
    {
        await api.CallAsync("POST", "/rfid/simulate", new { rfidTag = "TAG-DL-001", readerId = w.GateReader.Id }); // raises a pending pass
        var res = await api.CallAsync("GET", "/dashboard/summary");

        Assert.Equal(200, res.Status);
        Assert.Equal((2, 2, 0, 1, 1, 4, 1),
            (res.Body.Int("totalAssets"), res.Body.Int("activeAssets"), res.Body.Int("missingAssets"), res.Body.Int("pendingGatePasses"),
             res.Body.Int("todayMovements"), res.Body.Int("totalLocations"), res.Body.Int("unacknowledgedInsights")));
        Assert.Equal([("BAK", 0), ("TSAS", 2)], res.Body.Prop("sbuBreakdown").Items().Select(s => (s.Str("sbuCode"), s.Int("totalAssets"))));
    }

    [Fact]
    public async Task Activity_feed_merges_movements_gate_passes_and_insights_newest_first()
    {
        await api.CallAsync("POST", "/rfid/simulate", new { rfidTag = "TAG-DL-001", readerId = w.GateReader.Id });
        api.SetNow("2026-10-14T08:05:00Z");
        await api.CallAsync("POST", "/movements", new { assetId = w.Projector.Id, toLocationId = w.BakWarehouse.Id });

        var res = await api.CallAsync("GET", "/dashboard/activity?limit=12");
        var items = res.Body.Items();
        Assert.Equal("Epson EB-X51 Projector moved manually to BAK Warehouse", items[0].Str("description"));
        Assert.Contains(items, i => i.Str("type") == "gate_pass_created" && i.Str("description")!.Contains("auto-raised for Dell Latitude 5440"));
        Assert.Contains(items, i => i.Str("type") == "rfid_scan" && i.Str("description") == "RFID scan: Dell Latitude 5440 detected at TSAS Main Gate");
        Assert.Contains(items, i => i.Str("type") == "insight" && i.Str("severity") == "warning");
    }

    [Fact]
    public async Task Business_central_reports_manual_mode_no_schedule_and_the_changes_waiting_to_be_synced()
    {
        await api.CallAsync("POST", "/movements", new { assetId = w.Projector.Id, toLocationId = w.Gate.Id });
        var status = await api.CallAsync("GET", "/bc/sync-status");
        Assert.Equal((true, "manual", "idle", 1), (status.Body.Bool("connected"), status.Body.Str("mode"), status.Body.Str("status"), status.Body.Int("pendingChanges")));
        Assert.True(status.Body.IsNull("nextSyncAt"));
    }

    [Fact]
    public async Task Business_central_sync_clears_pending_changes_and_refuses_overlapping_syncs()
    {
        await api.CallAsync("POST", "/movements", new { assetId = w.Projector.Id, toLocationId = w.Gate.Id });
        api.Clock.Advance(TimeSpan.FromMilliseconds(20));

        // Phase 2: Group Finance (or the System Administrator) runs the Business Central sync.
        var started = await api.CallAsync("POST", "/bc/sync", token: w.GroupFinanceToken);
        Assert.Equal("syncing", started.Body.Str("status"));
        var overlapping = await api.CallAsync("POST", "/bc/sync", token: w.GroupFinanceToken);
        Assert.Equal(409, overlapping.Status);
        Assert.Contains("already in progress", overlapping.Error());

        api.Clock.Advance(TimeSpan.FromSeconds(2));
        ApiResponse done = default!;
        for (var attempt = 0; attempt < 50; attempt++)
        {
            done = await api.CallAsync("GET", "/bc/sync-status");
            if (done.Body.Str("status") == "success") break;
            await Task.Delay(20);
        }
        Assert.Equal(("success", 2, 0), (done.Body.Str("status"), done.Body.Int("syncedAssets"), done.Body.Int("pendingChanges")));
        Assert.Equal("2026-10-14T08:00:00.020Z", done.Body.Str("lastSyncAt"));
    }

    [Fact]
    public async Task Lifecycle_events_update_the_asset_and_appear_in_its_history()
    {
        var created = await api.CallAsync("POST", $"/assets/{w.Projector.Id}/lifecycle", new
        {
            eventType = "repair_sent", performedBy = "IT Helpdesk", vendor = "Epson Service Centre", ticketRef = "JOB-42",
        });
        Assert.Equal(201, created.Status);
        Assert.Equal(("repair_sent", "Epson EB-X51 Projector", "TAG-EP-001"), (created.Body.Str("eventType"), created.Body.Str("assetName"), created.Body.Str("rfidTag")));
        Assert.Equal("maintenance", (await api.CallAsync("GET", $"/assets/{w.Projector.Id}")).Body.Str("status"));

        api.Clock.Advance(TimeSpan.FromMinutes(1));
        var transfer = await api.CallAsync("POST", $"/assets/{w.Projector.Id}/lifecycle", new
        {
            eventType = "sbu_transfer", performedBy = "IT Helpdesk", fromSbuId = w.Tsas.Id, toSbuId = w.Bak.Id,
        });
        Assert.Equal(("Tobacco Sales Administration Services", "Bak Logistics"), (transfer.Body.Str("fromSbuName"), transfer.Body.Str("toSbuName")));
        Assert.Equal(w.Bak.Id, (await api.CallAsync("GET", $"/assets/{w.Projector.Id}")).Body.Int("sbuId"));

        Assert.Equal(400, (await api.CallAsync("POST", $"/assets/{w.Projector.Id}/lifecycle", new { eventType = "note" })).Status);
        Assert.Equal(404, (await api.CallAsync("POST", "/assets/999/lifecycle", new { eventType = "note", performedBy = "x" })).Status);

        var history = await api.CallAsync("GET", $"/assets/{w.Projector.Id}/history");
        Assert.Equal(["lc-2", "lc-1"], history.Body.Items().Select(e => e.Str("id")));
        Assert.Equal(2, (await api.CallAsync("GET", "/lifecycle")).Body.Items().Count);
        Assert.Single((await api.CallAsync("GET", "/lifecycle?eventType=sbu_transfer")).Body.Items());
        Assert.Equal(2, (await api.CallAsync("GET", $"/lifecycle?sbuId={w.Bak.Id}")).Body.Items().Count);
        Assert.Empty((await api.CallAsync("GET", $"/lifecycle?sbuId={w.Tsas.Id}")).Body.Items());
    }
}
