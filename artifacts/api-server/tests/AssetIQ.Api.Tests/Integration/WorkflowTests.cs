using AssetIQ.Api.Data;
using AssetIQ.Api.Domain;
using AssetIQ.Api.Services.Approvals;
using AssetIQ.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AssetIQ.Api.Tests.Integration;

/// <summary>
/// Phase 4a: the approvals engine (one inbox, routing by role and threshold, delegation, reminders, escalation,
/// audit) and the workflows behind it: repairs (Finance approves, product owner 29 Oct 2026), disposals (SI-08,
/// SF-04, GF-04, GI-10) and inter-SBU transfers (GI-05, SI-10).
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class WorkflowTests(ApiFixture api) : IAsyncLifetime
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

    private async Task<int> InboxRequestAsync(string role, string kind)
    {
        var inbox = await api.CallAsync("GET", "/approvals", token: r[role]);
        return inbox.Body.Items().Single(i => i.Str("type") == kind).Int("requestId");
    }

    private Task<ApiResponse> Decide(int requestId, string role, object body) => api.CallAsync("POST", $"/approval-requests/{requestId}/decision", body, r[role]);

    private async Task CapitaliseAsync(int assetId, decimal cost)
    {
        Assert.Equal(200, (await api.CallAsync("PATCH", $"/assets/{assetId}/financial",
            new { originalCost = cost, currency = "USD", exchangeRate = 1m, inUseDate = "2026-01-01", reason = "Invoice" }, r[Roles.SbuFinance])).Status);
        Assert.Equal(200, (await api.CallAsync("POST", $"/assets/{assetId}/capitalise", new { }, r[Roles.SbuFinance])).Status);
    }

    // ── repairs ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task SI_05_an_it_repair_waits_for_finance_who_records_the_cost_and_it_returns_to_its_holder()
    {
        Assert.Equal(409, (await api.CallAsync("PATCH", $"/assets/{r.W.Laptop.Id}", new { status = "in_repair" }, r[Roles.SbuIt])).Status);
        var requested = await api.CallAsync("POST", $"/assets/{r.W.Laptop.Id}/repairs", new { fault = "Keyboard not working", vendor = "Dell Service" }, r[Roles.SbuIt]);
        Assert.Equal((201, "requested"), (requested.Status, requested.Body.Str("status")));
        Assert.Equal("repair_requested", await StatusOf(r.W.Laptop.Id));

        var requestId = await InboxRequestAsync(Roles.SbuFinance, "repair");
        Assert.Equal(403, (await Decide(requestId, Roles.SbuIt, new { decision = "approve" })).Status);
        Assert.Equal(200, (await Decide(requestId, Roles.SbuFinance, new { decision = "approve", comment = "Within warranty budget", cost = 120m })).Status);
        Assert.Equal("in_repair", await StatusOf(r.W.Laptop.Id));

        // Money stays with Finance: IT sees the repair without its cost.
        var asIt = (await api.CallAsync("GET", $"/assets/{r.W.Laptop.Id}/repairs", token: r[Roles.SbuIt])).Body.Items().Single();
        var asFinance = (await api.CallAsync("GET", $"/assets/{r.W.Laptop.Id}/repairs", token: r[Roles.SbuFinance])).Body.Items().Single();
        Assert.Equal((true, 120m), (asIt.IsNull("cost"), asFinance.Prop("cost").GetDecimal()));
        Assert.Equal(403, (await api.CallAsync("PATCH", $"/repairs/{asIt.Int("id")}", new { cost = 99m }, r[Roles.SbuIt])).Status);

        Assert.Equal(201, (await api.CallAsync("POST", $"/assets/{r.W.Laptop.Id}/lifecycle", new { eventType = "repair_returned", performedBy = "Farai" }, r[Roles.SbuIt])).Status);
        Assert.Equal("in_use", await StatusOf(r.W.Laptop.Id));
        Assert.Equal("completed", (await api.CallAsync("GET", $"/assets/{r.W.Laptop.Id}/repairs", token: r[Roles.SbuIt])).Body.Items().Single().Str("status"));
    }

    [Fact]
    public async Task SI_05_a_rejected_repair_puts_the_asset_back_and_tells_the_requester()
    {
        await api.CallAsync("POST", $"/assets/{r.W.Projector.Id}/repairs", new { fault = "Lamp flickers" }, r[Roles.SbuIt]);
        var requestId = await InboxRequestAsync(Roles.SbuFinance, "repair");
        Assert.Equal(400, (await Decide(requestId, Roles.SbuFinance, new { decision = "reject" })).Status); // a rejection needs a reason
        Assert.Equal(200, (await Decide(requestId, Roles.SbuFinance, new { decision = "reject", comment = "Replace instead" })).Status);
        Assert.Equal("in_store", await StatusOf(r.W.Projector.Id));
        var notes = await api.CallAsync("GET", "/notifications", token: r[Roles.SbuIt]);
        Assert.Contains(notes.Body.Items(), n => n.Str("kind") == "decision" && n.Str("body")!.Contains("Replace instead"));
    }

    // ── disposals ────────────────────────────────────────────────────────────

    [Fact]
    public async Task SF_04_and_GF_04_finance_records_proceeds_gain_or_loss_is_computed_and_above_the_threshold_group_finance_signs_off()
    {
        await CapitaliseAsync(r.TsasDesk.Id, 1500m);
        Assert.Equal(201, (await api.CallAsync("POST", $"/assets/{r.TsasDesk.Id}/disposals", new { reason = "Office refit", condition = "fair" }, r[Roles.SbuAdmin])).Status);
        var requestId = await InboxRequestAsync(Roles.SbuFinance, "disposal");
        Assert.Equal(400, (await Decide(requestId, Roles.SbuFinance, new { decision = "approve", method = "sale" })).Status); // a sale needs proceeds and a buyer
        Assert.Equal(200, (await Decide(requestId, Roles.SbuFinance, new { decision = "approve", method = "sale", proceeds = 300m, buyer = "Staff auction", disposalDate = "2026-10-14" })).Status);

        // NBV above USD 1,000: Group Finance must approve too, so the asset is not disposed yet.
        Assert.Equal("pending_disposal", await StatusOf(r.TsasDesk.Id));
        var groupStep = await InboxRequestAsync(Roles.GroupFinance, "disposal");
        Assert.Equal(200, (await Decide(groupStep, Roles.GroupFinance, new { decision = "approve", comment = "Agreed" })).Status);
        Assert.Equal("disposed", await StatusOf(r.TsasDesk.Id));

        var disposal = (await api.CallAsync("GET", $"/assets/{r.TsasDesk.Id}/disposals", token: r[Roles.SbuFinance])).Body.Items().Single();
        var nbv = disposal.Prop("nbvAtDisposal").GetDecimal();
        Assert.True(nbv > 1000m);
        Assert.Equal(("completed", 300m - nbv), (disposal.Str("status"), disposal.Prop("gainLoss").GetDecimal()));
        Assert.True((await api.CallAsync("GET", $"/assets/{r.TsasDesk.Id}/disposals", token: r[Roles.SbuAdmin])).Body.Items().Single().IsNull("gainLoss"));
    }

    [Fact]
    public async Task GI_10_an_it_disposal_cannot_complete_without_the_data_wipe_note_and_group_it_sign_off()
    {
        Assert.Equal(201, (await api.CallAsync("POST", $"/assets/{r.W.Laptop.Id}/disposals", new { reason = "Board failure" }, r[Roles.SbuIt])).Status);
        Assert.Equal(200, (await Decide(await InboxRequestAsync(Roles.SbuFinance, "disposal"), Roles.SbuFinance, new { decision = "approve", method = "scrap" })).Status);
        Assert.Equal("pending_disposal", await StatusOf(r.W.Laptop.Id));
        var wipe = await InboxRequestAsync(Roles.GroupIt, "disposal");
        var missing = await Decide(wipe, Roles.GroupIt, new { decision = "approve" });
        Assert.Equal((400, "dataWipeNote: an IT disposal needs the data-wipe certificate or note (GI-10)"), (missing.Status, missing.Error()));
        Assert.Equal(200, (await Decide(wipe, Roles.GroupIt, new { decision = "approve", dataWipeNote = "Wiped with DBAN, certificate DW-0042" })).Status);
        Assert.Equal("disposed", await StatusOf(r.W.Laptop.Id));
    }

    [Fact]
    public async Task GF_04_a_rejected_disposal_returns_the_asset_to_the_sbu()
    {
        await api.CallAsync("POST", $"/assets/{r.W.Laptop.Id}/disposals", new { reason = "Slow" }, r[Roles.SbuIt]);
        Assert.Equal(200, (await Decide(await InboxRequestAsync(Roles.SbuFinance, "disposal"), Roles.SbuFinance, new { decision = "reject", comment = "Upgrade the RAM instead" })).Status);
        Assert.Equal("in_use", await StatusOf(r.W.Laptop.Id));
    }

    // ── transfers ────────────────────────────────────────────────────────────

    [Fact]
    public async Task GI_05_an_approved_transfer_moves_the_asset_to_the_receiving_register_and_tells_both_sbu_finance_teams()
    {
        var bakFinance = await Accounts.TokenAsync(api, Roles.SbuFinance, r.W.Bak);
        Assert.Equal(201, (await api.CallAsync("POST", $"/assets/{r.W.Projector.Id}/transfers", new { toSbuId = r.W.Bak.Id, reason = "BAK needs a projector" }, r[Roles.SbuIt])).Status);
        Assert.Equal("transfer_pending", await StatusOf(r.W.Projector.Id));
        var requestId = await InboxRequestAsync(Roles.GroupIt, "transfer");
        Assert.Equal(403, (await Decide(requestId, Roles.SbuIt, new { decision = "approve" })).Status); // SBU IT may only request (matrix)
        Assert.Equal(200, (await Decide(requestId, Roles.GroupIt, new { decision = "approve", comment = "OK" })).Status);

        var asset = (await api.CallAsync("GET", $"/assets/{r.W.Projector.Id}", token: r[Roles.GroupFinance])).Body;
        Assert.Equal((r.W.Bak.Id, "in_store", "BAK IT store"), (asset.Int("sbuId"), asset.Str("status"), asset.Str("assignedTo")));
        Assert.Equal(403, (await api.CallAsync("GET", $"/assets/{r.W.Projector.Id}", token: r[Roles.SbuIt])).Status); // no longer TSAS's
        foreach (var finance in new[] { r[Roles.SbuFinance], bakFinance })
            Assert.Contains((await api.CallAsync("GET", "/notifications", token: finance)).Body.Items(), n => n.Str("kind") == "transfer");
        Assert.Equal(409, (await api.CallAsync("POST", $"/assets/{r.W.Laptop.Id}/lifecycle", new { eventType = "sbu_transfer", performedBy = "x", toSbuId = r.W.Bak.Id }, r[Roles.GroupIt])).Status);
    }

    // ── engine: delegation, reminders, escalation, audit, notifications ──────

    [Fact]
    public async Task LM_03_an_approver_on_leave_delegates_and_the_delegate_decides_on_their_behalf()
    {
        var delegateId = (await api.CallAsync("GET", "/auth/me", token: r[Roles.Employee])).Body.Int("id");
        Assert.Equal(201, (await api.CallAsync("POST", "/delegations", new { delegateUserId = delegateId, endsAt = "2026-10-21T17:00:00Z", reason = "Annual leave" }, r[Roles.Hod])).Status);

        var inbox = (await api.CallAsync("GET", "/approvals", token: r[Roles.Employee])).Body.Items();
        var item = inbox.Single(i => i.Str("type") == "gate_pass");
        Assert.Equal("Line Manager / HOD TSAS", item.Str("onBehalfOf"));
        Assert.Equal(200, (await Decide(item.Int("requestId"), Roles.Employee, new { decision = "approve", comment = "Approved while Rudo is away" })).Status);
        var pass = await api.CallAsync("GET", $"/gate-passes/{r.TsasPass.Id}", token: r[Roles.SbuIt]);
        Assert.Equal("approved", pass.Body.Str("status"));

        await using var db = api.NewDbContext();
        var step = await db.ApprovalSteps.SingleAsync(s => s.Status == "approved");
        Assert.Equal(("Line Manager / HOD TSAS", "Employee TSAS"), (step.OnBehalfOf, step.DecidedBy));
        Assert.Contains(await db.AuditLog.Where(e => e.Action == "approval_step_approved").Select(e => e.Reason).ToListAsync(), x => x!.Contains("for Line Manager / HOD TSAS, on leave"));
    }

    [Fact]
    public async Task Phase4_an_unanswered_step_gets_a_reminder_then_escalates_to_the_next_approver()
    {
        await api.CallAsync("POST", $"/assets/{r.W.Projector.Id}/repairs", new { fault = "No picture" }, r[Roles.SbuIt]);
        async Task<int> ProcessAsync()
        {
            using var scope = api.Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<ApprovalService>().ProcessDueAsync(CancellationToken.None);
        }
        async Task<ApprovalStep> RepairStepAsync()
        {
            await using var db = api.NewDbContext();
            return await db.ApprovalSteps.SingleAsync(s => db.ApprovalRequests.Any(q => q.Id == s.RequestId && q.Kind == "repair"));
        }
        Assert.Equal(0, await ProcessAsync());
        Assert.DoesNotContain((await api.CallAsync("GET", "/approvals", token: r[Roles.GroupFinance])).Body.Items(), i => i.Str("type") == "repair");

        api.SetNow(api.Clock.GetUtcNow().AddHours(25));
        Assert.True(await ProcessAsync() >= 1); // reminder after 24 h
        Assert.Equal((true, false), ((await RepairStepAsync()).RemindedAt is not null, (await RepairStepAsync()).EscalatedAt is not null));
        Assert.Contains((await api.CallAsync("GET", "/notifications?unread=true", token: r[Roles.SbuFinance])).Body.Items(), n => n.Str("kind") == "reminder");

        api.SetNow(api.Clock.GetUtcNow().AddHours(24));
        Assert.True(await ProcessAsync() >= 1); // escalation after 48 h: SBU Finance → Group Finance
        Assert.Equal(Roles.GroupFinance, (await RepairStepAsync()).EscalatedToRole);
        var escalated = (await api.CallAsync("GET", "/approvals", token: r[Roles.GroupFinance])).Body.Items().Single(i => i.Str("type") == "repair");
        Assert.True(escalated.Bool("escalated"));
        Assert.Equal(200, (await Decide(escalated.Int("requestId"), Roles.GroupFinance, new { decision = "approve" })).Status);
        Assert.Equal("in_repair", await StatusOf(r.W.Projector.Id));

        await using var db = api.NewDbContext();
        Assert.Contains(await db.OutboundEmails.Select(e => e.Subject).ToListAsync(), s => s.StartsWith("AssetIQ: Escalated to you"));
        var marked = await api.CallAsync("POST", "/notifications/read", new { }, r[Roles.SbuFinance]);
        Assert.Equal(204, marked.Status);
        Assert.Empty((await api.CallAsync("GET", "/notifications?unread=true", token: r[Roles.SbuFinance])).Body.Items());
    }
}
