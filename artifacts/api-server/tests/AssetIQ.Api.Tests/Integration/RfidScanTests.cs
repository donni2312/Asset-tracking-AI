using AssetIQ.Api.Data;
using AssetIQ.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AssetIQ.Api.Tests.Integration;

/// <summary>
/// POST /api/rfid/simulate — the RFID pipeline and the gate-pass check (plan 6.2 / 6.3). Ported from
/// src/routes/rfid.test.ts. The clock is pinned to Wednesday 14 Oct 2026, 10:00 in Harare.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class RfidScanTests(ApiFixture api) : IAsyncLifetime
{
    private const string Saturday2Am = "2026-10-17T00:00:00.000Z";
    private TestWorld w = null!;

    public async Task InitializeAsync()
    {
        await api.ResetAsync();
        w = await TestWorld.SeedAsync(api);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private Task<ApiResponse> Scan(string rfidTag, int readerId, string? scannedAt = null) =>
        api.CallAsync("POST", "/rfid/simulate", scannedAt is null ? new { rfidTag, readerId } : new { rfidTag, readerId, scannedAt });

    private async Task<Asset> AssetRow(int id)
    {
        await using var db = api.NewDbContext();
        return await db.Assets.AsNoTracking().SingleAsync(a => a.Id == id);
    }

    private async Task<List<AiInsight>> Insights()
    {
        await using var db = api.NewDbContext();
        return await db.AiInsights.AsNoTracking().ToListAsync();
    }

    private async Task<GatePass> AddApprovedPass(string? returnDate = "2026-10-20", string? destination = "Borrowdale client site")
    {
        await using var db = api.NewDbContext();
        var pass = new GatePass
        {
            AssetId = w.Laptop.Id, RequestedBy = "John Doe", Reason = "Client presentation", Destination = destination,
            Status = "approved", ApprovedBy = "Rudo Chikwanha", ReturnDate = returnDate,
            DecidedAt = new DateTime(2026, 10, 13, 8, 0, 0, DateTimeKind.Utc), CreatedAt = api.Now, UpdatedAt = api.Now,
        };
        db.GatePasses.Add(pass);
        await db.SaveChangesAsync();
        return pass;
    }

    [Fact]
    public async Task Non_gate_reader_records_an_rfid_movement_moves_the_asset_and_raises_nothing_but_a_mismatch()
    {
        await using (var db = api.NewDbContext())
        {
            await db.Assets.Where(a => a.Id == w.Projector.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.CurrentLocationId, w.BakWarehouse.Id).SetProperty(a => a.SbuId, w.Bak.Id));
        }

        var res = await Scan("TAG-EP-001", w.OfficeReader.Id);

        Assert.Equal(200, res.Status);
        var gateCheck = res.Body.Prop("gateCheck");
        Assert.Equal(["authorized", "direction", "isGate", "outcome"], gateCheck.Keys()); // no gatePass key at all
        Assert.Equal((false, "internal", "not_gate", true), (gateCheck.Bool("isGate"), gateCheck.Str("direction"), gateCheck.Str("outcome"), gateCheck.Bool("authorized")));
        var movement = res.Body.Prop("movement");
        Assert.Equal(("rfid", "BAK Warehouse", "TSAS IT Office"), (movement.Str("triggeredBy"), movement.Str("fromLocationName"), movement.Str("toLocationName")));
        Assert.True(movement.IsNull("gatePassId"));
        Assert.Equal(w.Office.Id, (await AssetRow(w.Projector.Id)).CurrentLocationId);
        // BAK asset seen in a TSAS office → location mismatch is the only insight
        Assert.Equal(["anomaly"], res.Body.Prop("insights").Items().Select(i => i.Str("type")));
    }

    [Fact]
    public async Task Gate_exit_without_a_pass_auto_creates_a_pending_pass_holds_the_asset_and_emits_unauthorized_movement()
    {
        var res = await Scan("TAG-DL-001", w.GateReader.Id);

        Assert.Equal(200, res.Status);
        var gateCheck = res.Body.Prop("gateCheck");
        Assert.Equal((true, "exit", "pending_created", false), (gateCheck.Bool("isGate"), gateCheck.Str("direction"), gateCheck.Str("outcome"), gateCheck.Bool("authorized")));
        var pass = gateCheck.Prop("gatePass");
        Assert.Equal(("pending", "rfid_gate", w.Gate.Id, "TSAS Main Gate", "Dell Latitude 5440"),
            (pass.Str("status"), pass.Str("source"), pass.Int("gateLocationId"), pass.Str("gateLocationName"), pass.Str("assetName")));
        Assert.Contains("Assigned holder: John Doe", pass.Str("reason"));
        var movement = res.Body.Prop("movement");
        Assert.Equal(("rfid", "TSAS Main Gate", pass.Int("id")), (movement.Str("triggeredBy"), movement.Str("toLocationName"), movement.Int("gatePassId")));
        Assert.Equal(w.Gate.Id, (await AssetRow(w.Laptop.Id)).CurrentLocationId);

        var insight = Assert.Single(res.Body.Prop("insights").Items());
        Assert.Equal(("unauthorized_movement", "warning", pass.Int("id"), "Dell Latitude 5440", "TSAS"),
            (insight.Str("type"), insight.Str("severity"), insight.Int("gatePassId"), insight.Str("assetName"), insight.Str("sbuCode")));

        var pending = await api.CallAsync("GET", "/gate-passes?status=pending");
        Assert.Equal([pass.Int("id")], pending.Body.Items().Select(p => p.Int("id")));
    }

    [Fact]
    public async Task Repeated_scans_reuse_the_pending_pass_instead_of_creating_duplicates_or_duplicate_alerts()
    {
        var first = await Scan("TAG-DL-001", w.GateReader.Id);
        var second = await Scan("TAG-DL-001", w.GateReader.Id);

        Assert.Equal("pending_existing", second.Body.Prop("gateCheck").Str("outcome"));
        Assert.Equal(first.Body.Prop("gateCheck").Prop("gatePass").Int("id"), second.Body.Prop("gateCheck").Prop("gatePass").Int("id"));
        await using (var db = api.NewDbContext()) Assert.Equal(1, await db.GatePasses.CountAsync());
        // first alert said "raised", the second "still awaiting approval" — each is stored once
        Assert.Equal(2, (await Insights()).Count(i => i.Type == "unauthorized_movement"));

        var third = await Scan("TAG-DL-001", w.GateReader.Id);
        Assert.Empty(third.Body.Prop("insights").Items());
        Assert.Equal(2, (await Insights()).Count(i => i.Type == "unauthorized_movement"));
    }

    [Fact]
    public async Task An_expired_approved_pass_counts_as_no_pass()
    {
        await using (var db = api.NewDbContext())
        {
            db.GatePasses.Add(new GatePass
            {
                AssetId = w.Laptop.Id, RequestedBy = "John Doe", Reason = "Client visit", Status = "approved", ApprovedBy = "Rudo Chikwanha",
                ReturnDate = "2026-10-13", DecidedAt = new DateTime(2026, 10, 12, 8, 0, 0, DateTimeKind.Utc), CreatedAt = api.Now, UpdatedAt = api.Now,
            });
            await db.SaveChangesAsync();
        }

        var res = await Scan("TAG-DL-001", w.GateReader.Id);
        Assert.Equal("pending_created", res.Body.Prop("gateCheck").Str("outcome"));
    }

    [Fact]
    public async Task A_night_time_exit_attempt_escalates_to_critical_and_adds_an_out_of_hours_insight()
    {
        api.SetNow("2026-10-17T00:05:00Z");
        var res = await Scan("TAG-DL-001", w.GateReader.Id, Saturday2Am);

        var byType = res.Body.Prop("insights").Items().ToDictionary(i => i.Str("type")!, i => i.Str("severity"));
        Assert.Equal(new Dictionary<string, string?> { ["unauthorized_movement"] = "critical", ["unusual_pattern"] = "warning" }, byType);
        Assert.Equal(Saturday2Am, res.Body.Prop("movement").Str("timestamp"));
    }

    [Fact]
    public async Task Detection_at_another_sbus_gate_flags_a_location_mismatch()
    {
        var res = await Scan("TAG-DL-001", w.BakGateReader.Id);
        var mismatch = res.Body.Prop("insights").Items().Single(i => i.Str("type") == "anomaly");
        Assert.Equal("Location mismatch: \"Dell Latitude 5440\" belongs to TSAS but was detected at gate \"BAK Depot Gate\" (BAK)", mismatch.Str("message"));
    }

    [Fact]
    public async Task An_approved_pass_records_an_authorised_gate_pass_exit_moves_the_asset_off_premises_and_raises_no_insight()
    {
        var pass = await AddApprovedPass();
        var res = await Scan("TAG-DL-001", w.GateReader.Id);

        var gateCheck = res.Body.Prop("gateCheck");
        Assert.Equal(("authorized", true, "exit"), (gateCheck.Str("outcome"), gateCheck.Bool("authorized"), gateCheck.Str("direction")));
        Assert.Equal(pass.Id, gateCheck.Prop("gatePass").Int("id"));
        Assert.False(gateCheck.Prop("gatePass").IsNull("exitedAt"));
        var movement = res.Body.Prop("movement");
        Assert.Equal(("gate_pass", pass.Id, w.GateReader.Id, "John Doe"),
            (movement.Str("triggeredBy"), movement.Int("gatePassId"), movement.Int("rfidReaderId"), movement.Str("movedBy")));
        Assert.True(movement.IsNull("toLocationId"));
        Assert.True(movement.IsNull("toLocationName"));
        Assert.Contains("destination: Borrowdale client site", movement.Str("notes"));
        Assert.Null((await AssetRow(w.Laptop.Id)).CurrentLocationId);
        // Regression: authorised gate scans used to raise "verify gate pass authorization" warnings
        Assert.Empty(res.Body.Prop("insights").Items());
        Assert.Empty(await Insights());
    }

    [Fact]
    public async Task The_next_gate_scan_of_an_off_premises_asset_is_an_entry_with_no_pass_required()
    {
        await AddApprovedPass();
        await Scan("TAG-DL-001", w.GateReader.Id);
        api.SetNow("2026-10-14T13:00:00Z");

        var back = await Scan("TAG-DL-001", w.GateReader.Id);
        var gateCheck = back.Body.Prop("gateCheck");
        Assert.Equal(("entry", "entry", true), (gateCheck.Str("direction"), gateCheck.Str("outcome"), gateCheck.Bool("authorized")));
        var movement = back.Body.Prop("movement");
        Assert.Equal(("rfid", "TSAS Main Gate"), (movement.Str("triggeredBy"), movement.Str("toLocationName")));
        Assert.True(movement.IsNull("fromLocationId"));
        Assert.Empty(back.Body.Prop("insights").Items());
        Assert.Equal(w.Gate.Id, (await AssetRow(w.Laptop.Id)).CurrentLocationId);
    }

    [Fact]
    public async Task Back_dated_scans_keep_the_current_location_correct()
    {
        await Scan("TAG-EP-001", w.OfficeReader.Id); // 10:00 — latest known position
        var earlier = await Scan("TAG-EP-001", w.BakGateReader.Id, "2026-10-14T06:00:00.000Z");
        Assert.Equal(200, earlier.Status);
        Assert.Equal(w.Office.Id, (await AssetRow(w.Projector.Id)).CurrentLocationId);
    }

    [Fact]
    public async Task Rejects_unknown_tags_and_readers_inactive_readers_and_future_timestamps()
    {
        Assert.Equal(404, (await Scan("NOPE", w.GateReader.Id)).Status);
        Assert.Equal(404, (await Scan("TAG-DL-001", 9999)).Status);
        var inactive = await Scan("TAG-DL-001", w.InactiveReader.Id);
        Assert.Equal(409, inactive.Status);
        Assert.Contains("inactive", inactive.Error());
        Assert.Equal(400, (await Scan("TAG-DL-001", w.GateReader.Id, "2026-10-15T08:00:00.000Z")).Status);
        Assert.Equal(400, (await api.CallAsync("POST", "/rfid/simulate", new { rfidTag = "TAG-DL-001" })).Status);
        await using var db = api.NewDbContext();
        Assert.Empty(await db.Movements.ToListAsync());
    }

    [Fact]
    public async Task Updates_the_readers_last_read_time()
    {
        await Scan("TAG-EP-001", w.OfficeReader.Id);
        var readers = await api.CallAsync("GET", "/rfid/readers");
        var office = readers.Body.Items().Single(r => r.Int("id") == w.OfficeReader.Id);
        Assert.Equal("2026-10-14T08:00:00.000Z", office.Str("lastReadAt"));
        Assert.Equal(("TSAS IT Office", "TSAS", "Tobacco Sales Administration Services"), (office.Str("locationName"), office.Str("sbuCode"), office.Str("sbuName")));
    }
}
