using AssetIQ.Api.Data;
using AssetIQ.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AssetIQ.Api.Tests.Integration;

/// <summary>Gate pass workflow: request → approve (gate_pass movement) / reject (flag the asset). Ported from src/routes/gate-passes.test.ts.</summary>
[Collection(ApiCollection.Name)]
public sealed class GatePassWorkflowTests(ApiFixture api) : IAsyncLifetime
{
    private TestWorld w = null!;

    public async Task InitializeAsync()
    {
        await api.ResetAsync();
        w = await TestWorld.SeedAsync(api);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private Task<ApiResponse> Request(object body) => api.CallAsync("POST", "/gate-passes", body);

    private Task<ApiResponse> Decide(int id, string action, object body) => api.CallAsync("POST", $"/gate-passes/{id}/{action}", body);

    private Task<ApiResponse> ScanAtGate(string rfidTag = "TAG-DL-001") =>
        api.CallAsync("POST", "/rfid/simulate", new { rfidTag, readerId = w.GateReader.Id });

    private async Task<Asset> AssetRow(int id)
    {
        await using var db = api.NewDbContext();
        return await db.Assets.AsNoTracking().SingleAsync(a => a.Id == id);
    }

    private async Task<List<Movement>> MovementsOf(int assetId)
    {
        await using var db = api.NewDbContext();
        return await db.Movements.AsNoTracking().Where(m => m.AssetId == assetId).ToListAsync();
    }

    private async Task<List<AiInsight>> AlertsFor(int gatePassId)
    {
        await using var db = api.NewDbContext();
        return await db.AiInsights.AsNoTracking().Where(i => i.GatePassId == gatePassId).ToListAsync();
    }

    [Fact]
    public async Task Creates_a_manual_pending_pass()
    {
        var res = await Request(new
        {
            assetId = w.Projector.Id, requestedBy = "Tendai Moyo", reason = "Board meeting at head office",
            destination = "TSL Head Office", returnDate = "2026-10-16",
        });
        Assert.Equal(201, res.Status);
        Assert.Equal(("pending", "manual", "Tendai Moyo", "2026-10-16", "Epson EB-X51 Projector", "Tobacco Sales Administration Services"),
            (res.Body.Str("status"), res.Body.Str("source"), res.Body.Str("requestedBy"), res.Body.Str("returnDate"), res.Body.Str("assetName"), res.Body.Str("sbuName")));
        Assert.True(res.Body.IsNull("exitedAt"));
    }

    [Fact]
    public async Task Allows_only_one_pending_pass_per_asset()
    {
        await Request(new { assetId = w.Projector.Id, requestedBy = "Tendai Moyo", reason = "First" });
        var duplicate = await Request(new { assetId = w.Projector.Id, requestedBy = "Tendai Moyo", reason = "Second" });
        Assert.Equal(409, duplicate.Status);
        Assert.Contains("already has a pending gate pass", duplicate.Error());
    }

    [Fact]
    public async Task Validates_the_request()
    {
        Assert.Equal(404, (await Request(new { assetId = 999, requestedBy = "A", reason = "B" })).Status);
        Assert.Equal(400, (await Request(new { assetId = w.Projector.Id, requestedBy = "", reason = "B" })).Status);
        Assert.Equal(400, (await Request(new { assetId = w.Projector.Id, requestedBy = "   ", reason = "B" })).Status);
        var past = await Request(new { assetId = w.Projector.Id, requestedBy = "A", reason = "B", returnDate = "2026-10-01" });
        Assert.Equal(400, past.Status);
        Assert.Contains("past", past.Error());
    }

    [Fact]
    public async Task Approval_records_the_approver_releases_a_held_asset_as_a_gate_pass_movement_and_resolves_the_alert()
    {
        var scan = await ScanAtGate();
        var passId = scan.Body.Prop("gateCheck").Prop("gatePass").Int("id");
        api.SetNow("2026-10-14T08:20:00Z");

        var res = await Decide(passId, "approve", new { decidedBy = "Rudo Chikwanha", note = "Client visit confirmed" });

        Assert.Equal(200, res.Status);
        Assert.Equal(("approved", "Rudo Chikwanha", "Client visit confirmed", "2026-10-14T08:20:00.000Z", "2026-10-14T08:20:00.000Z"),
            (res.Body.Str("status"), res.Body.Str("approvedBy"), res.Body.Str("decisionNote"), res.Body.Str("decidedAt"), res.Body.Str("exitedAt")));

        var exit = Assert.Single(await MovementsOf(w.Laptop.Id), m => m.TriggeredBy == "gate_pass");
        Assert.Equal((w.Gate.Id, (int?)null, passId, "John Doe"), (exit.FromLocationId, exit.ToLocationId, exit.GatePassId, exit.MovedBy));
        Assert.Null((await AssetRow(w.Laptop.Id)).CurrentLocationId);
        Assert.All(await AlertsFor(passId), alert => Assert.True(alert.Dismissed));
    }

    [Fact]
    public async Task A_pass_approved_in_advance_does_not_invent_an_exit_the_gate_reader_records_it_later()
    {
        var created = await Request(new { assetId = w.Laptop.Id, requestedBy = "John Doe", reason = "Home office", returnDate = "2026-10-20" });
        var approved = await Decide(created.Body.Int("id"), "approve", new { decidedBy = "Rudo Chikwanha" });

        Assert.True(approved.Body.IsNull("exitedAt"));
        Assert.Empty(await MovementsOf(w.Laptop.Id));
        Assert.Equal(w.Office.Id, (await AssetRow(w.Laptop.Id)).CurrentLocationId);

        api.SetNow("2026-10-14T15:30:00Z");
        var exit = await ScanAtGate();
        Assert.Equal("authorized", exit.Body.Prop("gateCheck").Str("outcome"));
        Assert.Equal(("gate_pass", created.Body.Int("id")), (exit.Body.Prop("movement").Str("triggeredBy"), exit.Body.Prop("movement").Int("gatePassId")));

        var pass = await api.CallAsync("GET", $"/gate-passes/{created.Body.Int("id")}");
        Assert.Equal("2026-10-14T15:30:00.000Z", pass.Body.Str("exitedAt"));
    }

    [Fact]
    public async Task Refuses_to_decide_a_pass_twice()
    {
        var created = await Request(new { assetId = w.Projector.Id, requestedBy = "Tendai Moyo", reason = "Event" });
        var id = created.Body.Int("id");
        Assert.Equal(200, (await Decide(id, "approve", new { decidedBy = "Rudo Chikwanha" })).Status);

        var again = await Decide(id, "approve", new { decidedBy = "Someone Else" });
        Assert.Equal(409, again.Status);
        Assert.Equal($"Gate pass #{id} has already been approved", again.Error());
        Assert.Equal(409, (await Decide(id, "reject", new { decidedBy = "Someone Else" })).Status);
    }

    [Fact]
    public async Task Requires_a_real_approver_name()
    {
        var created = await Request(new { assetId = w.Projector.Id, requestedBy = "Tendai Moyo", reason = "Event" });
        var id = created.Body.Int("id");
        Assert.Equal(400, (await Decide(id, "approve", new { })).Status);
        Assert.Equal(400, (await Decide(id, "approve", new { decidedBy = "" })).Status);
        Assert.Equal(400, (await Decide(id, "approve", new { decidedBy = "   " })).Status);
        Assert.Equal(404, (await Decide(9999, "approve", new { decidedBy = "Rudo Chikwanha" })).Status);
        Assert.Equal(400, (await api.CallAsync("POST", "/gate-passes/abc/approve", new { decidedBy = "Rudo Chikwanha" })).Status);
    }

    [Fact]
    public async Task Rejection_flags_an_asset_caught_at_the_gate_with_a_critical_alert_and_an_audit_note()
    {
        var scan = await ScanAtGate();
        var passId = scan.Body.Prop("gateCheck").Prop("gatePass").Int("id");

        var res = await Decide(passId, "reject", new { decidedBy = "Rudo Chikwanha", note = "No business reason" });
        Assert.Equal(200, res.Status);
        Assert.Equal(("rejected", "Rudo Chikwanha", "No business reason"), (res.Body.Str("status"), res.Body.Str("approvedBy"), res.Body.Str("decisionNote")));

        var open = Assert.Single(await AlertsFor(passId), a => !a.Dismissed);
        Assert.Equal(("unauthorized_movement", "critical", (int?)w.Laptop.Id), (open.Type, open.Severity, open.AssetId));
        Assert.Contains("the asset is at \"TSAS Main Gate\" and must not leave the premises", open.Message);

        await using (var db = api.NewDbContext())
        {
            var note = Assert.Single(await db.AssetLifecycleEvents.AsNoTracking().Where(e => e.AssetId == w.Laptop.Id).ToListAsync());
            Assert.Equal(("note", "Rudo Chikwanha"), (note.EventType, note.PerformedBy));
        }

        Assert.Equal(w.Gate.Id, (await AssetRow(w.Laptop.Id)).CurrentLocationId);
        Assert.DoesNotContain(await MovementsOf(w.Laptop.Id), m => m.TriggeredBy == "gate_pass");

        var history = await api.CallAsync("GET", $"/assets/{w.Laptop.Id}/history");
        Assert.Contains(history.Body.Items(), e => e.Str("notes")?.Contains($"gate pass #{passId} rejected") == true);
    }

    [Fact]
    public async Task Rejecting_a_request_made_in_advance_raises_a_warning_not_a_critical_alert()
    {
        var created = await Request(new { assetId = w.Projector.Id, requestedBy = "Tendai Moyo", reason = "Weekend use" });
        await Decide(created.Body.Int("id"), "reject", new { decidedBy = "Rudo Chikwanha" });
        var flag = Assert.Single(await AlertsFor(created.Body.Int("id")));
        Assert.Equal(("warning", false), (flag.Severity, flag.Dismissed));
    }

    [Fact]
    public async Task Lists_gate_passes_filtered_by_status_and_sbu()
    {
        await ScanAtGate();
        var manual = await Request(new { assetId = w.Projector.Id, requestedBy = "Tendai Moyo", reason = "Event" });
        await Decide(manual.Body.Int("id"), "approve", new { decidedBy = "Rudo Chikwanha" });

        Assert.Single((await api.CallAsync("GET", "/gate-passes?status=pending")).Body.Items());
        Assert.Single((await api.CallAsync("GET", "/gate-passes?status=approved")).Body.Items());
        Assert.Equal(2, (await api.CallAsync("GET", $"/gate-passes?sbuId={w.Tsas.Id}")).Body.Items().Count);
        Assert.Empty((await api.CallAsync("GET", $"/gate-passes?sbuId={w.Bak.Id}")).Body.Items());
    }
}
