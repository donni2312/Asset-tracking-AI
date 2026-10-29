using AssetIQ.Api.Domain;
using AssetIQ.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AssetIQ.Api.Tests.Integration;

/// <summary>
/// Phase 3 through the API: the status endpoint, lifecycle events, the gate and Business Central all change
/// status through the one state machine, with the rules of ST-02, SI-02, SI-08, ST-05 and GI-07.
/// TSAS laptop: in use (John Doe, tagged); projector: in store (tagged); desk: in use (non-IT).
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class AssetLifecycleTests(ApiFixture api) : IAsyncLifetime
{
    private RoleWorld r = null!;

    public async Task InitializeAsync()
    {
        await api.ResetAsync();
        r = await RoleWorld.SeedAsync(api);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private Task<ApiResponse> Status(int assetId, object body, string role) => api.CallAsync("POST", $"/assets/{assetId}/status", body, r[role]);

    private async Task<string?> StatusOf(int assetId) =>
        (await api.CallAsync("GET", $"/assets/{assetId}", token: r[Roles.GroupFinance])).Body.Str("status");

    [Fact]
    public async Task Phase3_a_transition_not_in_the_table_is_refused_with_409_and_changes_nothing()
    {
        var res = await Status(r.W.Projector.Id, new { status = "disposed", reason = "Broken" }, Roles.SbuIt);
        Assert.Equal((409, "An asset cannot go from In store to Disposed"), (res.Status, res.Error()));
        Assert.Equal("in_store", await StatusOf(r.W.Projector.Id));
        var pending = await Status(r.W.Projector.Id, new { status = "pending_asset_number", reason = "x" }, Roles.SbuIt);
        Assert.Equal(400, pending.Status); // nobody asks for "pending asset number"
    }

    [Fact]
    public async Task GI_07_lost_or_stolen_needs_date_reporter_and_notes_and_leaves_the_active_count()
    {
        var before = (await api.CallAsync("GET", "/dashboard/summary", token: r[Roles.SbuIt])).Body.Int("activeAssets");
        var incomplete = await Status(r.W.Laptop.Id, new { status = "lost_stolen", reason = "Reported stolen" }, Roles.SbuIt);
        Assert.Equal((409, "Lost / stolen needs the date, who reported it, incident notes (GI-07)"), (incomplete.Status, incomplete.Error()));

        var lost = await Status(r.W.Laptop.Id, new { status = "lost_stolen", reason = "Reported stolen", lostDate = "2026-10-13", reporter = "John Doe", notes = "Taken from a car at Borrowdale" }, Roles.SbuIt);
        Assert.Equal(200, lost.Status);
        Assert.Equal(("lost_stolen", "2026-10-13", "John Doe"), (lost.Body.Str("status"), lost.Body.Str("lostDate"), lost.Body.Str("lostReportedBy")));
        var summary = (await api.CallAsync("GET", "/dashboard/summary", token: r[Roles.SbuIt])).Body;
        Assert.Equal((before - 1, 1), (summary.Int("activeAssets"), summary.Int("missingAssets")));

        Assert.Equal(200, (await Status(r.W.Laptop.Id, new { status = "in_store", reason = "Recovered by police" }, Roles.SbuIt)).Status);
    }

    [Fact]
    public async Task SI_08_and_SF_04_a_disposal_is_recommended_decided_by_finance_and_final_and_the_asset_cannot_be_reassigned_meanwhile()
    {
        Assert.Equal(200, (await Status(r.W.Laptop.Id, new { status = "pending_disposal", reason = "Cracked screen and board fault" }, Roles.SbuIt)).Status);
        Assert.Equal(403, (await Status(r.W.Laptop.Id, new { status = "disposed", reason = "Approve" }, Roles.SbuIt)).Status); // IT recommends, Finance decides

        var reassign = await api.CallAsync("POST", $"/assets/{r.W.Laptop.Id}/lifecycle", new { eventType = "deployment", performedBy = "Farai", custodianEmployeeId = r.TsasHodEmployee.Id }, r[Roles.SbuIt]);
        Assert.Equal((409, "The asset is pending disposal and cannot be reassigned (SI-08)"), (reassign.Status, reassign.Error()));
        Assert.Equal(409, (await api.CallAsync("PATCH", $"/assets/{r.W.Laptop.Id}", new { assignedTo = "Someone Else" }, r[Roles.SbuIt])).Status);

        var disposed = await Status(r.W.Laptop.Id, new { status = "disposed", reason = "Scrapped via e-waste vendor" }, Roles.SbuFinance);
        Assert.Equal(200, disposed.Status);
        Assert.Equal("2026-10-14", disposed.Body.Str("disposalDate"));
        var after = await Status(r.W.Laptop.Id, new { status = "in_store", reason = "Undo" }, Roles.SystemAdmin);
        Assert.Equal((409, "A disposed asset is final; its status cannot change"), (after.Status, after.Error()));
        await using var db = api.NewDbContext();
        Assert.Equal(["in_use→pending_disposal", "pending_disposal→disposed"],
            await db.AuditLog.Where(e => e.AssetId == r.W.Laptop.Id && e.Action == "status_changed").OrderBy(e => e.Id).Select(e => e.OldValue + "→" + e.NewValue).ToListAsync());
    }

    [Fact]
    public async Task ST_02_an_item_cannot_be_issued_while_untagged()
    {
        await using (var db = api.NewDbContext())
        {
            await db.Assets.Where(a => a.Id == r.W.Projector.Id).ExecuteUpdateAsync(s => s.SetProperty(a => a.RfidTag, (string?)null));
        }
        var issue = await api.CallAsync("POST", $"/assets/{r.W.Projector.Id}/lifecycle", new { eventType = "deployment", performedBy = "Farai", custodianEmployeeId = r.TsasStaff.Id }, r[Roles.SbuIt]);
        Assert.Equal((409, "An item cannot be issued while it is untagged or waiting for its asset number (ST-02)"), (issue.Status, issue.Error()));
        var options = await api.CallAsync("GET", $"/assets/{r.W.Projector.Id}/transitions", token: r[Roles.SbuIt]);
        var toInUse = options.Body.Items().Single(o => o.Str("status") == "in_use");
        Assert.Equal((false, "Tag the item and wait for its asset number first (ST-02)"), (toInUse.Bool("allowed"), toInUse.Str("reason")));
    }

    [Fact]
    public async Task SI_02_and_ST_05_an_it_return_goes_to_sbu_it_and_stores_takes_it_only_once_recommended_for_disposal()
    {
        var byStores = await Status(r.W.Laptop.Id, new { status = "in_store", reason = "Returned" }, Roles.Stores);
        Assert.Equal(403, byStores.Status);
        Assert.Contains("ST-05", byStores.Error());

        var returned = await api.CallAsync("POST", $"/assets/{r.W.Laptop.Id}/lifecycle", new { eventType = "return_to_it", performedBy = "Farai" }, r[Roles.SbuIt]);
        Assert.Equal(201, returned.Status);
        var laptop = (await api.CallAsync("GET", $"/assets/{r.W.Laptop.Id}", token: r[Roles.SbuIt])).Body;
        Assert.Equal(("in_store", "TSAS IT store"), (laptop.Str("status"), laptop.Str("assignedTo")));
        Assert.Equal(403, (await api.CallAsync("GET", $"/assets/{r.W.Laptop.Id}", token: r[Roles.Stores])).Status); // stays with SBU IT

        Assert.Equal(200, (await Status(r.W.Laptop.Id, new { status = "pending_disposal", reason = "Beyond economic repair" }, Roles.SbuIt)).Status);
        Assert.Equal(200, (await api.CallAsync("GET", $"/assets/{r.W.Laptop.Id}", token: r[Roles.Stores])).Status); // now Stores may receive it
    }

    [Fact]
    public async Task Phase3_the_gate_moves_an_asset_out_on_its_gate_pass_and_back_in_use_on_return()
    {
        var pass = (await api.CallAsync("POST", $"/gate-passes/{r.TsasPass.Id}/approve", new { }, r[Roles.Hod])).Body;
        Assert.Equal("approved", pass.Str("status"));
        await api.CallAsync("POST", "/movements", new { assetId = r.W.Laptop.Id, toLocationId = r.W.Gate.Id }, r[Roles.SbuIt]);
        var exit = await api.CallAsync("POST", "/rfid/simulate", new { rfidTag = "TAG-DL-001", readerId = r.W.GateReader.Id }, r[Roles.GroupIt]);
        Assert.Equal("authorized", exit.Body.Prop("gateCheck").Str("outcome"));
        Assert.Equal("out_on_gate_pass", await StatusOf(r.W.Laptop.Id));

        api.SetNow(api.Clock.GetUtcNow().AddHours(3));
        var back = await api.CallAsync("POST", "/rfid/simulate", new { rfidTag = "TAG-DL-001", readerId = r.W.GateReader.Id }, r[Roles.GroupIt]);
        Assert.Equal(200, back.Status);
        Assert.Equal("in_use", await StatusOf(r.W.Laptop.Id)); // back to John Doe
    }

    [Fact]
    public async Task Phase3_a_transfer_request_waits_for_group_it_and_repairs_return_to_the_holder()
    {
        Assert.Equal(200, (await Status(r.W.Projector.Id, new { status = "transfer_pending", reason = "Surplus, BAK needs one" }, Roles.SbuIt)).Status);
        Assert.Equal(403, (await Status(r.W.Projector.Id, new { status = "in_store", reason = "Approved" }, Roles.SbuIt)).Status); // request only
        Assert.Equal(201, (await api.CallAsync("POST", $"/assets/{r.W.Projector.Id}/lifecycle", new { eventType = "sbu_transfer", performedBy = "Group IT", fromSbuId = r.W.Tsas.Id, toSbuId = r.W.Bak.Id }, r[Roles.GroupIt])).Status);
        Assert.Equal("in_store", await StatusOf(r.W.Projector.Id));

        Assert.Equal(201, (await api.CallAsync("POST", $"/assets/{r.W.Laptop.Id}/lifecycle", new { eventType = "repair_sent", performedBy = "Farai", vendor = "Dell Service" }, r[Roles.SbuIt])).Status);
        Assert.Equal("in_repair", await StatusOf(r.W.Laptop.Id));
        Assert.Equal(201, (await api.CallAsync("POST", $"/assets/{r.W.Laptop.Id}/lifecycle", new { eventType = "repair_returned", performedBy = "Farai" }, r[Roles.SbuIt])).Status);
        Assert.Equal("in_use", await StatusOf(r.W.Laptop.Id)); // it has a holder
    }

    [Fact]
    public async Task Phase3_the_caller_sees_which_changes_they_may_make()
    {
        var it = await api.CallAsync("GET", $"/assets/{r.W.Laptop.Id}/transitions", token: r[Roles.SbuIt]);
        var allowed = it.Body.Items().Where(o => o.Bool("allowed")).Select(o => o.Str("status")).Order().ToList();
        Assert.Equal(["in_repair", "in_store", "lost_stolen", "out_on_gate_pass", "pending_disposal", "transfer_pending"], allowed);
        Assert.True(it.Body.Items().Single(o => o.Str("status") == "lost_stolen").Bool("needsIncidentDetails"));
        var hod = await api.CallAsync("GET", $"/assets/{r.W.Laptop.Id}/transitions", token: r[Roles.Hod]);
        Assert.DoesNotContain(hod.Body.Items(), o => o.Bool("allowed"));
    }
}
