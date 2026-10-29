using System.Globalization;
using System.Text.Json;
using AssetIQ.Api.Auth;
using AssetIQ.Api.Contracts;
using AssetIQ.Api.Data;
using AssetIQ.Api.Domain;
using AssetIQ.Api.Infrastructure;
using AssetIQ.Api.Services.Depreciation;
using Microsoft.EntityFrameworkCore;

namespace AssetIQ.Api.Services.Approvals;

public sealed record RepairRequestInput(string Fault, string? Action, string? Parts, string? Vendor);
public sealed record DisposalRequestInput(string Reason, string? Condition);
public sealed record TransferRequestInput(int ToSbuId, string Reason);

public static class DisposalMethods
{
    public static readonly IReadOnlyList<string> All = ["sale", "scrap", "donation", "write_off"];
}

/// <summary>
/// Phase 4 workflows that change an asset only once approved: repairs (SI-05; Finance approves, product owner
/// 29 Oct 2026), disposals (SI-08, SF-04, GF-04, GI-10) and inter-SBU transfers (GI-05, SI-10). Each opens a request
/// in the approvals engine and is completed by <see cref="IApprovalHandler"/> when the request is decided.
/// </summary>
public sealed class WorkflowService(
    AssetIqDbContext db, AppClock clock, AuditTrail audit, AssetStatusService statuses, ApprovalService approvals,
    NotificationService notifications, ValuationService valuations, DepreciationService depreciation)
{
    // ── repairs ──────────────────────────────────────────────────────────────

    public async Task<AssetRepair> RequestRepairAsync(AuthUser user, int assetId, RepairRequestInput input, CancellationToken ct)
    {
        var asset = await LoadAsync(assetId, ct);
        if (!AccessPolicy.CanEditTechnical(user, asset.AssetCategory.IsIt, asset.SbuId)) throw AccessPolicy.Deny(user, "request a repair for this asset");
        if (string.IsNullOrWhiteSpace(input.Fault)) throw new ApiException(StatusCodes.Status400BadRequest, "fault: Required");
        if (await db.AssetRepairs.AnyAsync(r => r.AssetId == assetId && (r.Status == RepairStatuses.Requested || r.Status == RepairStatuses.Approved), ct))
            throw new ApiException(StatusCodes.Status409Conflict, "The asset already has an open repair");
        var previous = asset.Status;
        statuses.Change(user, asset, new StatusChange(AssetStatuses.RepairRequested, $"Fault: {input.Fault.Trim()}"), viaWorkflow: true);
        var repair = new AssetRepair
        {
            AssetId = assetId, Fault = input.Fault.Trim(), Action = Blank(input.Action), Parts = Blank(input.Parts), Vendor = Blank(input.Vendor),
            Status = RepairStatuses.Requested, PreviousStatus = previous, RequestedBy = user.Name, CreatedAt = clock.UtcNow,
        };
        db.AssetRepairs.Add(repair);
        await db.SaveChangesAsync(ct);
        await approvals.OpenAsync(ApprovalKinds.Repair, asset, repair.Id, $"Repair {asset.AssetNumber ?? asset.Name}: {repair.Fault}",
            new { repair.Fault, repair.Action, repair.Vendor }, [new StepSpec(1, Roles.SbuFinance, asset.SbuId, null, "SBU Finance (repair approval)")], user, user.Name, ct);
        return repair;
    }

    /// <summary>Completes the open repair when the asset comes back (lifecycle "repair returned").</summary>
    public async Task CompleteRepairAsync(int assetId, int? downtimeDays, CancellationToken ct)
    {
        var repair = await db.AssetRepairs.Where(r => r.AssetId == assetId && r.Status == RepairStatuses.Approved).OrderByDescending(r => r.Id).FirstOrDefaultAsync(ct);
        if (repair is null) return;
        repair.Status = RepairStatuses.Completed;
        repair.CompletedAt = clock.UtcNow;
        repair.DowntimeDays = downtimeDays ?? (int)Math.Ceiling((clock.UtcNow - repair.CreatedAt).TotalDays);
    }

    /// <summary>The repair's cost (Finance) or its technical record (IT); money stays with Finance (product owner, 29 Oct 2026).</summary>
    public async Task<AssetRepair> UpdateRepairAsync(AuthUser user, int repairId, decimal? cost, bool costSet, string? action, string? parts, string? vendor, int? downtime, CancellationToken ct)
    {
        var repair = await db.AssetRepairs.FirstOrDefaultAsync(r => r.Id == repairId, ct) ?? throw new ApiException(StatusCodes.Status404NotFound, "Repair not found");
        var asset = await LoadAsync(repair.AssetId, ct);
        if (costSet)
        {
            if (!AccessPolicy.IsSbuFinanceOf(user, asset.SbuId) && user.Role != Roles.GroupFinance) throw AccessPolicy.Deny(user, "record repair costs (Finance only)");
            if (cost is < 0) throw new ApiException(StatusCodes.Status400BadRequest, "cost: must not be negative");
            if (audit.RecordChange(user, "repair", repair.Id, repair.AssetId, "cost", repair.Cost, cost)) repair.Cost = cost;
        }
        if (action is not null || parts is not null || vendor is not null || downtime is not null)
        {
            if (!AccessPolicy.CanEditTechnical(user, asset.AssetCategory.IsIt, asset.SbuId)) throw AccessPolicy.Deny(user, "edit this repair");
            if (action is not null && audit.RecordChange(user, "repair", repair.Id, repair.AssetId, "action", repair.Action, action)) repair.Action = action;
            if (parts is not null && audit.RecordChange(user, "repair", repair.Id, repair.AssetId, "parts", repair.Parts, parts)) repair.Parts = parts;
            if (vendor is not null && audit.RecordChange(user, "repair", repair.Id, repair.AssetId, "vendor", repair.Vendor, vendor)) repair.Vendor = vendor;
            if (downtime is not null && audit.RecordChange(user, "repair", repair.Id, repair.AssetId, "downtimeDays", repair.DowntimeDays, downtime)) repair.DowntimeDays = downtime;
        }
        await db.SaveChangesAsync(ct);
        return repair;
    }

    // ── disposals ────────────────────────────────────────────────────────────

    /// <summary>SI-08: recommend a disposal (condition and reason; photos as attachments); SBU Finance then completes it (SF-04).</summary>
    public async Task<AssetDisposal> RecommendDisposalAsync(AuthUser user, int assetId, DisposalRequestInput input, CancellationToken ct)
    {
        var asset = await LoadAsync(assetId, ct);
        if (!AccessPolicy.CanChangeStatus(user, asset, asset.AssetCategory.IsIt, asset.Status, AssetStatuses.PendingDisposal))
            throw AccessPolicy.Deny(user, "recommend this asset for disposal");
        if (string.IsNullOrWhiteSpace(input.Reason)) throw new ApiException(StatusCodes.Status400BadRequest, "reason: Required");
        var previous = asset.Status;
        statuses.Change(user, asset, new StatusChange(AssetStatuses.PendingDisposal, $"Recommended for disposal: {input.Reason.Trim()}"), viaWorkflow: true);
        var disposal = new AssetDisposal
        {
            AssetId = assetId, Reason = input.Reason.Trim(), Condition = Blank(input.Condition), RecommendedBy = user.Name, PreviousStatus = previous,
            Method = previous == AssetStatuses.LostStolen ? "write_off" : null, Status = DisposalStatuses.Pending, CreatedAt = clock.UtcNow,
        };
        db.AssetDisposals.Add(disposal);
        await db.SaveChangesAsync(ct);
        List<StepSpec> steps = [new(1, Roles.SbuFinance, asset.SbuId, null, "SBU Finance (method, proceeds, gain / loss)")];
        if (asset.AssetCategory.IsIt) steps.Add(new(2, Roles.GroupIt, null, null, "Group IT data-wipe sign-off (GI-10)"));
        await approvals.OpenAsync(ApprovalKinds.Disposal, asset, disposal.Id, $"Dispose of {asset.AssetNumber ?? asset.Name}: {disposal.Reason}",
            new { disposal.Reason, disposal.Condition }, steps, user, user.Name, ct);
        return disposal;
    }

    // ── transfers ────────────────────────────────────────────────────────────

    /// <summary>SI-10 / AM-08: ask to move an asset to another SBU; Group IT approves IT transfers, the receiving Admin Manager non-IT ones.</summary>
    public async Task<ApprovalRequest> RequestTransferAsync(AuthUser user, int assetId, TransferRequestInput input, CancellationToken ct)
    {
        var asset = await LoadAsync(assetId, ct);
        if (!AccessPolicy.CanEditTechnical(user, asset.AssetCategory.IsIt, asset.SbuId)) throw AccessPolicy.Deny(user, "request a transfer of this asset");
        if (input.ToSbuId == asset.SbuId) throw new ApiException(StatusCodes.Status400BadRequest, "toSbuId: the asset is already in that SBU");
        var target = await db.Sbus.AsNoTracking().FirstOrDefaultAsync(s => s.Id == input.ToSbuId, ct) ?? throw new ApiException(StatusCodes.Status404NotFound, "SBU not found");
        if (string.IsNullOrWhiteSpace(input.Reason)) throw new ApiException(StatusCodes.Status400BadRequest, "reason: Required");
        statuses.Change(user, asset, new StatusChange(AssetStatuses.TransferPending, $"Transfer to {target.Code} requested: {input.Reason.Trim()}"), viaWorkflow: true);
        await db.SaveChangesAsync(ct);
        StepSpec step = asset.AssetCategory.IsIt
            ? new(1, Roles.GroupIt, null, null, "Group IT (GI-05)")
            : new(1, Roles.SbuAdmin, target.Id, null, $"{target.Code} Admin Manager (AM-08)");
        return await approvals.OpenAsync(ApprovalKinds.Transfer, asset, null, $"Transfer {asset.AssetNumber ?? asset.Name} to {target.Code}: {input.Reason.Trim()}",
            new TransferDetails(asset.SbuId ?? 0, target.Id, input.Reason.Trim()), [step], user, user.Name, ct);
    }

    public sealed record TransferDetails(int FromSbuId, int ToSbuId, string Reason);

    // ── the handlers ─────────────────────────────────────────────────────────

    internal async Task<Asset> LoadAsync(int assetId, CancellationToken ct) =>
        await db.Assets.IgnoreQueryFilters().Include(a => a.AssetCategory).FirstOrDefaultAsync(a => a.Id == assetId, ct)
        ?? throw new ApiException(StatusCodes.Status404NotFound, "Asset not found");

    internal async Task RepairDecidedAsync(AuthUser user, ApprovalRequest request, bool approved, CancellationToken ct)
    {
        var repair = await db.AssetRepairs.FirstAsync(r => r.Id == request.SubjectId, ct);
        var asset = await LoadAsync(request.AssetId!.Value, ct);
        repair.Status = approved ? RepairStatuses.Approved : RepairStatuses.Rejected;
        var to = approved ? AssetStatuses.InRepair : repair.PreviousStatus;
        statuses.TrySystemChange(user, asset, new StatusChange(to, approved ? "Repair approved by Finance" : "Repair rejected by Finance"));
    }

    internal async Task RepairCostAsync(ApprovalRequest request, JsonBody input, CancellationToken ct)
    {
        var cost = input.OptionalNumber("cost");
        if (cost is null) return;
        if (cost < 0) throw new ApiException(StatusCodes.Status400BadRequest, "cost: must not be negative");
        var repair = await db.AssetRepairs.FirstAsync(r => r.Id == request.SubjectId, ct);
        repair.Cost = cost;
    }

    /// <summary>SF-04 at SBU Finance's step: method, date, proceeds, buyer; gain / loss = proceeds − NBV; GF-04 threshold.</summary>
    internal async Task DisposalFinanceStepAsync(AuthUser user, ApprovalRequest request, JsonBody input, CancellationToken ct)
    {
        var disposal = await db.AssetDisposals.FirstAsync(d => d.Id == request.SubjectId, ct);
        var method = input.OptionalEnum("method", DisposalMethods.All) ?? disposal.Method;
        var date = input.NullableDay("disposalDate").Value ?? clock.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var proceeds = input.OptionalNumber("proceeds") ?? 0m;
        var buyer = input.OptionalString("buyer");
        if (method is null) throw new ApiException(StatusCodes.Status400BadRequest, "method: sale, scrap, donation or write_off");
        if (proceeds < 0) throw new ApiException(StatusCodes.Status400BadRequest, "proceeds: must not be negative");
        if (method == "sale" && (proceeds <= 0 || string.IsNullOrWhiteSpace(buyer))) throw new ApiException(StatusCodes.Status400BadRequest, "A sale needs the proceeds and the buyer");
        var nbv = (await valuations.ValueAsync(FinanceView, new ValuationFilter(AssetId: request.AssetId), depreciation.CurrentPeriod, ct)).FirstOrDefault()?.NetBookValue ?? 0m;
        disposal.Method = method;
        disposal.DisposalDate = date;
        disposal.Proceeds = proceeds;
        disposal.Buyer = Blank(buyer);
        disposal.NbvAtDisposal = nbv;
        disposal.GainLoss = proceeds - nbv;
        audit.Record(user, "disposal", disposal.Id, request.AssetId, "disposal_details", "gainLoss", null, AuditTrail.Text(disposal.GainLoss),
            $"{method}, proceeds {AuditTrail.Text(proceeds)} − NBV {AuditTrail.Text(nbv)}");
        var threshold = await SettingDecimalAsync("disposal_group_finance_threshold_usd", 1000m, ct);
        if (nbv > threshold && request.Steps.All(s => s.Role != Roles.GroupFinance))
            approvals.AddStep(request, new StepSpec(2, Roles.GroupFinance, null, null, $"Group Finance (NBV above USD {threshold:0}, GF-04)"));
    }

    internal async Task DataWipeStepAsync(AuthUser user, ApprovalRequest request, JsonBody input, CancellationToken ct)
    {
        var note = input.OptionalString("dataWipeNote");
        if (string.IsNullOrWhiteSpace(note))
            throw new ApiException(StatusCodes.Status400BadRequest, "dataWipeNote: an IT disposal needs the data-wipe certificate or note (GI-10)");
        var disposal = await db.AssetDisposals.FirstAsync(d => d.Id == request.SubjectId, ct);
        disposal.DataWipeNote = note.Trim();
        audit.Record(user, "disposal", disposal.Id, request.AssetId, "data_wipe_signed_off", "dataWipeNote", null, disposal.DataWipeNote);
    }

    internal async Task DisposalDecidedAsync(AuthUser user, ApprovalRequest request, bool approved, CancellationToken ct)
    {
        var disposal = await db.AssetDisposals.FirstAsync(d => d.Id == request.SubjectId, ct);
        var asset = await LoadAsync(request.AssetId!.Value, ct);
        disposal.Status = approved ? DisposalStatuses.Completed : DisposalStatuses.Rejected;
        if (approved)
        {
            asset.DisposalDate = disposal.DisposalDate;
            statuses.TrySystemChange(user, asset, new StatusChange(AssetStatuses.Disposed,
                $"Disposed ({disposal.Method}), gain / loss {AuditTrail.Text(disposal.GainLoss)}"));
            await NotifySbuFinanceAsync([asset.SbuId ?? 0], "decision", $"Disposal completed: {asset.AssetNumber ?? asset.Name}",
                $"{disposal.Method}, proceeds {AuditTrail.Text(disposal.Proceeds)}, gain / loss {AuditTrail.Text(disposal.GainLoss)}", ct);
        }
        else
        {
            // Rejected requests return to the SBU (GF-04): the asset goes back to where it was.
            statuses.TrySystemChange(user, asset, new StatusChange(disposal.PreviousStatus, "Disposal rejected"));
        }
    }

    internal async Task TransferDecidedAsync(AuthUser user, ApprovalRequest request, bool approved, CancellationToken ct)
    {
        var asset = await LoadAsync(request.AssetId!.Value, ct);
        var details = JsonSerializer.Deserialize<TransferDetails>(request.Details)!;
        if (!approved)
        {
            statuses.TrySystemChange(user, asset, new StatusChange(AssetStatusService.ReturnStatus(asset), "Transfer rejected"));
            return;
        }
        var to = await db.Sbus.AsNoTracking().FirstAsync(s => s.Id == details.ToSbuId, ct);
        audit.RecordChange(user, "asset", asset.Id, asset.Id, "sbuId", asset.SbuId, to.Id, $"Transfer approved: {details.Reason}");
        asset.SbuId = to.Id;
        asset.CustodianEmployeeId = null;
        asset.DepartmentId = null;
        asset.AssignedTo = AssetStatusService.StoreHolder(asset, to.Code);
        statuses.TrySystemChange(user, asset, new StatusChange(AssetStatuses.InStore, $"Received into {to.Code}'s register"));
        db.AssetLifecycleEvents.Add(new AssetLifecycleEvent
        {
            AssetId = asset.Id, EventType = LifecycleEventTypes.SbuTransfer, PerformedBy = user.Name, FromSbuId = details.FromSbuId, ToSbuId = to.Id,
            Notes = $"Approved transfer: {details.Reason}", Timestamp = clock.UtcNow,
        });
        // GI-05: both SBU Finance teams are notified.
        await NotifySbuFinanceAsync([details.FromSbuId, to.Id], "transfer", $"Asset transferred: {asset.AssetNumber ?? asset.Name}",
            $"Moved to {to.Code}'s register: {details.Reason}", ct);
    }

    private async Task NotifySbuFinanceAsync(IReadOnlyCollection<int> sbuIds, string kind, string title, string body, CancellationToken ct)
    {
        var finance = await db.Users.Where(u => u.Active && u.Role == Roles.SbuFinance && u.SbuId != null && sbuIds.Contains(u.SbuId.Value)).ToListAsync(ct);
        notifications.Notify(finance, kind, title, body, null);
    }

    private async Task<decimal> SettingDecimalAsync(string key, decimal fallback, CancellationToken ct)
    {
        var value = await db.GroupSettings.AsNoTracking().Where(s => s.Key == key).Select(s => s.Value).FirstOrDefaultAsync(ct);
        return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : fallback;
    }

    /// <summary>NBV is read with Finance's view (the approver of the step is Finance anyway).</summary>
    private static readonly AuthUser FinanceView = new(0, "system", "System", Roles.GroupFinance);

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public static class RepairStatuses
{
    public const string Requested = "requested";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
    public const string Completed = "completed";
}

public static class DisposalStatuses
{
    public const string Pending = "pending";
    public const string Completed = "completed";
    public const string Rejected = "rejected";
}

/// <summary>The approvals engine's handlers for repairs, disposals and transfers.</summary>
public sealed class RepairApprovalHandler(WorkflowService workflows) : IApprovalHandler
{
    public string Kind => ApprovalKinds.Repair;
    public Task BeforeApproveAsync(AuthUser user, ApprovalRequest request, ApprovalStep step, JsonBody input, CancellationToken ct) => workflows.RepairCostAsync(request, input, ct);
    public Task OnApprovedAsync(AuthUser user, ApprovalRequest request, string? comment, CancellationToken ct) => workflows.RepairDecidedAsync(user, request, true, ct);
    public Task OnRejectedAsync(AuthUser user, ApprovalRequest request, string? comment, CancellationToken ct) => workflows.RepairDecidedAsync(user, request, false, ct);
}

public sealed class DisposalApprovalHandler(WorkflowService workflows) : IApprovalHandler
{
    public string Kind => ApprovalKinds.Disposal;
    public Task BeforeApproveAsync(AuthUser user, ApprovalRequest request, ApprovalStep step, JsonBody input, CancellationToken ct) => step.Role switch
    {
        Roles.SbuFinance => workflows.DisposalFinanceStepAsync(user, request, input, ct),
        Roles.GroupIt => workflows.DataWipeStepAsync(user, request, input, ct),
        _ => Task.CompletedTask,
    };
    public Task OnApprovedAsync(AuthUser user, ApprovalRequest request, string? comment, CancellationToken ct) => workflows.DisposalDecidedAsync(user, request, true, ct);
    public Task OnRejectedAsync(AuthUser user, ApprovalRequest request, string? comment, CancellationToken ct) => workflows.DisposalDecidedAsync(user, request, false, ct);
}

public sealed class TransferApprovalHandler(WorkflowService workflows) : IApprovalHandler
{
    public string Kind => ApprovalKinds.Transfer;
    public Task BeforeApproveAsync(AuthUser user, ApprovalRequest request, ApprovalStep step, JsonBody input, CancellationToken ct) => Task.CompletedTask;
    public Task OnApprovedAsync(AuthUser user, ApprovalRequest request, string? comment, CancellationToken ct) => workflows.TransferDecidedAsync(user, request, true, ct);
    public Task OnRejectedAsync(AuthUser user, ApprovalRequest request, string? comment, CancellationToken ct) => workflows.TransferDecidedAsync(user, request, false, ct);
}
