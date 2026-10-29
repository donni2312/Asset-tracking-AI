using AssetIQ.Api.Domain;
using AssetIQ.Api.Services;
using AssetIQ.Api.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace AssetIQ.Api.Tests.Integration;

/// <summary>
/// Phase 4c: the Gate Officer (SG-01 … SG-03, GI-06, SI-04) — the green / red check of a pass, exits and partial returns
/// of a multi-item pass, the action recorded on an alert, overdue returns — and verification counts with their
/// reconciliation by SBU Finance (SI-07, SF-06).
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class GateAndVerificationTests(ApiFixture api) : IAsyncLifetime
{
    private RoleWorld r = null!;

    public async Task InitializeAsync()
    {
        await api.ResetAsync();
        r = await RoleWorld.SeedAsync(api);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<string?> StatusOf(int assetId) =>
        (await api.CallAsync("GET", $"/assets/{assetId}", token: r[Roles.GroupFinance])).Body.Str("status");

    private Task<ApiResponse> Scan(string code, string role = Roles.GateOfficer) =>
        api.CallAsync("GET", $"/gate/scan?code={Uri.EscapeDataString(code)}", token: r[role]);

    /// <summary>An approved pass for the projector and the laptop together (an IT pass: Group IT decides it).</summary>
    private async Task<int> ApprovedTwoItemPassAsync()
    {
        var created = await api.CallAsync("POST", "/gate-passes", new
        {
            assetId = r.W.Projector.Id, itemAssetIds = new[] { r.W.Laptop.Id }, requestedBy = "John Doe", reason = "Client site demo", destination = "Client HQ", returnDate = "2026-10-20",
        }, r[Roles.SbuIt]);
        Assert.True(created.Status == 201, created.Error());
        var id = created.Body.Int("id");
        Assert.Equal(2, created.Body.Prop("items").Items().Count);
        Assert.Equal("approved", (await api.CallAsync("POST", $"/gate-passes/{id}/approve", new { }, r[Roles.GroupIt])).Body.Str("status"));
        return id;
    }

    [Fact]
    public async Task SG_01_the_gate_check_is_red_for_a_pending_pass_or_no_pass_and_another_sbus_pass_is_not_shown()
    {
        var pending = await Scan($"GP-{r.TsasPass.Id}");
        Assert.Equal((200, "stop"), (pending.Status, pending.Body.Str("verdict")));
        Assert.Contains("pending", pending.Body.Str("message"));
        Assert.Equal("stop", (await Scan("TAG-SN-DESK-1")).Body.Str("verdict")); // no pass at all
        Assert.Equal(403, (await Scan($"GP-{r.BakPass.Id}")).Status);
        Assert.Equal(404, (await Scan("GP-99999")).Status);
        Assert.Equal(403, (await Scan("TAG-DL-001", Roles.Employee)).Status);
        Assert.Equal(409, (await api.CallAsync("POST", $"/gate-passes/{r.TsasPass.Id}/exit", new { gateLocationId = r.W.Gate.Id }, r[Roles.GateOfficer])).Status);
    }

    [Fact]
    public async Task SG_03_a_multi_item_pass_goes_out_and_comes_back_in_parts()
    {
        var passId = await ApprovedTwoItemPassAsync();
        var check = await Scan($"GP-{passId}");
        Assert.Equal("clear", check.Body.Str("verdict"));
        Assert.Contains("2 item(s) may leave", check.Body.Str("message"));

        Assert.Equal(400, (await api.CallAsync("POST", $"/gate-passes/{passId}/exit", new { gateLocationId = r.W.Office.Id }, r[Roles.GateOfficer])).Status); // not a gate
        Assert.Equal(403, (await api.CallAsync("POST", $"/gate-passes/{passId}/exit", new { gateLocationId = r.W.BakGate.Id }, r[Roles.GateOfficer])).Status);
        var exit = await api.CallAsync("POST", $"/gate-passes/{passId}/exit", new { gateLocationId = r.W.Gate.Id, notes = "Checked against the pass" }, r[Roles.GateOfficer]);
        Assert.True(exit.Status == 200, exit.Error());
        Assert.All(exit.Body.Prop("items").Items(), i => Assert.False(i.IsNull("exitedAt")));
        Assert.Equal("out_on_gate_pass", await StatusOf(r.W.Laptop.Id));
        Assert.Equal(409, (await api.CallAsync("POST", $"/gate-passes/{passId}/exit", new { gateLocationId = r.W.Gate.Id }, r[Roles.GateOfficer])).Status);

        api.SetNow(api.Clock.GetUtcNow().AddDays(1));
        Assert.Equal("return", (await Scan("TAG-EP-001")).Body.Str("verdict"));
        var partial = await api.CallAsync("POST", $"/gate-passes/{passId}/return",
            new { gateLocationId = r.W.Gate.Id, items = new[] { new { assetId = r.W.Projector.Id, condition = "fair", notes = "Lens cap missing" } } }, r[Roles.GateOfficer]);
        Assert.True(partial.Status == 200, partial.Error());
        var items = partial.Body.Prop("items").Items();
        Assert.Equal(("fair", true), (items.Single(i => i.Int("assetId") == r.W.Projector.Id).Str("returnCondition"), items.Single(i => i.Int("assetId") == r.W.Laptop.Id).IsNull("returnedAt")));
        Assert.Equal("in_store", await StatusOf(r.W.Projector.Id));
        Assert.Equal("out_on_gate_pass", await StatusOf(r.W.Laptop.Id));
        Assert.Equal(409, (await api.CallAsync("POST", $"/gate-passes/{passId}/return",
            new { gateLocationId = r.W.Gate.Id, items = new[] { new { assetId = r.W.Projector.Id } } }, r[Roles.GateOfficer])).Status);

        var rest = await api.CallAsync("POST", $"/gate-passes/{passId}/return", new { gateLocationId = r.W.Gate.Id }, r[Roles.GateOfficer]);
        Assert.All(rest.Body.Prop("items").Items(), i => Assert.False(i.IsNull("returnedAt")));
        Assert.Equal("in_use", await StatusOf(r.W.Laptop.Id)); // back to John Doe
    }

    [Fact]
    public async Task SG_02_the_officer_records_what_was_done_about_an_alert()
    {
        // The projector is read at the gate without a pass: a pending pass and an unauthorised-movement alert.
        await api.CallAsync("POST", "/rfid/simulate", new { rfidTag = "TAG-EP-001", readerId = r.W.GateReader.Id }, r[Roles.GroupIt]);
        var alert = (await api.CallAsync("GET", "/gate/alerts", token: r[Roles.GateOfficer])).Body.Items().Single(a => a.Int("assetId") == r.W.Projector.Id);
        var insightId = alert.Int("insightId");

        Assert.Equal(400, (await api.CallAsync("POST", $"/gate/alerts/{insightId}/actions", new { action = "escalated" }, r[Roles.GateOfficer])).Status);
        Assert.Equal(201, (await api.CallAsync("POST", $"/gate/alerts/{insightId}/actions", new { action = "stopped", gateLocationId = r.W.Gate.Id }, r[Roles.GateOfficer])).Status);
        var escalated = await api.CallAsync("POST", $"/gate/alerts/{insightId}/actions", new { action = "escalated", notes = "Holder insists; calling the Admin Manager" }, r[Roles.GateOfficer]);
        Assert.Equal((201, "escalated"), (escalated.Status, escalated.Body.Str("action")));
        Assert.Contains((await api.CallAsync("GET", "/notifications", token: r[Roles.SbuAdmin])).Body.Items(), n => n.Str("kind") == "gate");
        Assert.Equal(["stopped", "escalated"], (await api.CallAsync("GET", "/gate/alerts", token: r[Roles.GateOfficer])).Body.Items()
            .Single(a => a.Int("insightId") == insightId).Prop("actions").Items().Select(a => a.Str("action")).ToList());

        Assert.Equal(201, (await api.CallAsync("POST", $"/gate/alerts/{insightId}/actions",
            new { action = "released_with_override", notes = "Released on the Admin Manager's phone approval" }, r[Roles.GateOfficer])).Status);
        Assert.DoesNotContain((await api.CallAsync("GET", "/gate/alerts", token: r[Roles.GateOfficer])).Body.Items(), a => a.Int("insightId") == insightId);
        Assert.Equal(409, (await api.CallAsync("POST", $"/gate/alerts/{insightId}/actions", new { action = "stopped" }, r[Roles.GateOfficer])).Status);
        var history = await api.CallAsync("GET", $"/assets/{r.W.Projector.Id}/lifecycle", token: r[Roles.GroupIt]);
        Assert.Contains(history.Body.Items(), e => (e.Str("notes") ?? "").StartsWith("Released at the gate with an override", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GI_06_an_item_not_back_by_its_return_date_is_flagged_once_until_it_returns()
    {
        var passId = await ApprovedTwoItemPassAsync();
        await api.CallAsync("POST", $"/gate-passes/{passId}/exit", new { gateLocationId = r.W.Gate.Id }, r[Roles.GateOfficer]);
        Assert.Empty((await api.CallAsync("GET", "/gate/overdue", token: r[Roles.GateOfficer])).Body.Items());

        api.SetNow("2026-10-22T08:00:00Z");
        Assert.Equal(2, (await api.CallAsync("GET", "/gate/overdue", token: r[Roles.GateOfficer])).Body.Items().Count);
        async Task<int> FlagAsync()
        {
            using var scope = api.Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<GateService>().FlagOverdueAsync(CancellationToken.None);
        }
        Assert.Equal(2, await FlagAsync());
        Assert.Equal(0, await FlagAsync());
        Assert.Contains((await api.CallAsync("GET", "/notifications", token: r[Roles.SbuAdmin])).Body.Items(), n => n.Str("kind") == "gate");

        await api.CallAsync("POST", $"/gate-passes/{passId}/return", new { gateLocationId = r.W.Gate.Id }, r[Roles.GateOfficer]);
        Assert.Empty((await api.CallAsync("GET", "/gate/overdue", token: r[Roles.GateOfficer])).Body.Items());
        Assert.DoesNotContain((await api.CallAsync("GET", "/gate/alerts", token: r[Roles.GateOfficer])).Body.Items(), a => a.Int("gatePassId") == passId);
    }

    [Fact]
    public async Task SI_07_and_SF_06_a_count_finds_differences_and_finance_reconciles_each_with_a_comment()
    {
        Assert.Equal(403, (await api.CallAsync("POST", "/verification-sessions", new { locationId = r.W.Office.Id, scope = "it" }, r[Roles.Stores])).Status);
        Assert.Equal(403, (await api.CallAsync("POST", "/verification-sessions", new { locationId = r.W.Office.Id }, r[Roles.GateOfficer])).Status);
        var started = await api.CallAsync("POST", "/verification-sessions", new { locationId = r.W.Office.Id }, r[Roles.SbuIt]);
        Assert.True(started.Status == 201, started.Error());
        var id = started.Body.Int("id");
        Assert.Equal(("it", 2), (started.Body.Str("scope"), started.Body.Int("expectedCount"))); // laptop and projector; the desk is not IT
        Assert.Equal(409, (await api.CallAsync("POST", "/verification-sessions", new { locationId = r.W.Office.Id }, r[Roles.SbuIt])).Status);

        var scanned = await api.CallAsync("POST", $"/verification-sessions/{id}/scans", new { codes = new[] { "tag-dl-001", "TAG-DL-001", "TAG-SN-BAK-1", "UNKNOWN-77" } }, r[Roles.SbuIt]);
        Assert.Equal(3, scanned.Body.Int("scanCount"));
        var closed = await api.CallAsync("POST", $"/verification-sessions/{id}/close", null, r[Roles.SbuIt]);
        Assert.Equal(("closed", 1, 1, 2, 3), (closed.Body.Str("status"), closed.Body.Int("foundCount"), closed.Body.Int("missingCount"), closed.Body.Int("unrecordedCount"), closed.Body.Int("unresolvedCount")));
        Assert.Contains((await api.CallAsync("GET", "/notifications", token: r[Roles.SbuFinance])).Body.Items(), n => n.Str("kind") == "verification");

        var results = closed.Body.Prop("results").Items();
        var missing = results.Single(x => x.Str("result") == "missing").Int("id");
        var unknown = results.Single(x => x.Str("code") == "UNKNOWN-77").Int("id");
        var elsewhere = results.Single(x => x.Str("code") == "TAG-SN-BAK-1").Int("id");
        Task<ApiResponse> Resolve(int resultId, string resolution, string role = Roles.SbuFinance, string comment = "Checked with the holder") =>
            api.CallAsync("POST", $"/verification-sessions/{id}/results/{resultId}/resolve", new { resolution, comment }, r[role]);

        Assert.Equal(403, (await Resolve(missing, "write_off", Roles.SbuIt)).Status);
        Assert.Equal(400, (await Resolve(missing, "register")).Status);
        Assert.Equal(400, (await Resolve(missing, "write_off", comment: "")).Status);
        Assert.Equal(200, (await Resolve(missing, "write_off", comment: "Not found after a search of the floor")).Status);
        Assert.Equal("lost_stolen", await StatusOf(r.W.Projector.Id));
        Assert.Equal(409, (await Resolve(missing, "investigate")).Status);

        Assert.Equal(200, (await Resolve(unknown, "register", comment: "Unlabelled monitor; to be tagged")).Status);
        Assert.Contains((await api.CallAsync("GET", "/notifications", token: r[Roles.Stores])).Body.Items(), n => n.Str("kind") == "verification");
        var done = await Resolve(elsewhere, "investigate", comment: "BAK laptop seen at TSAS; asked BAK IT");
        Assert.Equal(("reconciled", 0), (done.Body.Str("status"), done.Body.Int("unresolvedCount")));

        Assert.Single((await api.CallAsync("GET", "/verification-sessions", token: r[Roles.Auditor])).Body.Items());
    }
}
