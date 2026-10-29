using AssetIQ.Api.Auth;
using AssetIQ.Api.Contracts;
using AssetIQ.Api.Data;
using AssetIQ.Api.Domain;
using AssetIQ.Api.Infrastructure;

namespace AssetIQ.Api.Services;

/// <param name="Reason">Why (recorded in the audit trail).</param>
/// <param name="LostDate">GI-07, YYYY-MM-DD, with <paramref name="Reporter"/> and <paramref name="Notes"/>.</param>
public sealed record StatusChange(string To, string? Reason, string? LostDate = null, string? Reporter = null, string? Notes = null);

/// <summary>
/// Applies status changes (Phase 3). Every change goes through <see cref="AssetStatusMachine.Check"/> (409 when the
/// table does not allow it or a guard fails) and, for a person, <see cref="AccessPolicy.CanChangeStatus"/> (403);
/// the system (Business Central, the gate) is checked against the table only. The asset must be tracked; the
/// caller saves.
/// </summary>
public sealed class AssetStatusService(AuditTrail audit, AppClock clock)
{
    /// <summary>A person's change: table, guards and role.</summary>
    /// <summary>Statuses entered and left only through an approval (Phase 4): repairs, transfers, disposals.</summary>
    public static readonly IReadOnlyList<string> WorkflowOwned =
        [AssetStatuses.RepairRequested, AssetStatuses.TransferPending, AssetStatuses.PendingDisposal, AssetStatuses.Disposed];

    /// <param name="viaWorkflow">True when a workflow (repair, transfer, disposal request) makes the change.</param>
    public void Change(AuthUser user, Asset asset, StatusChange change, bool viaWorkflow = false)
    {
        if (!viaWorkflow && (WorkflowOwned.Contains(change.To) || WorkflowOwned.Contains(asset.Status)))
        {
            throw new ApiException(StatusCodes.Status409Conflict, change.To switch
            {
                AssetStatuses.RepairRequested => "Request a repair with POST /assets/{id}/repairs; SBU Finance approves it",
                AssetStatuses.TransferPending => "Request a transfer with POST /assets/{id}/transfers",
                AssetStatuses.PendingDisposal => "Recommend a disposal with POST /assets/{id}/disposals",
                _ => $"The asset is {AssetStatusMachine.Label(asset.Status)}; that is decided in the approvals inbox",
            });
        }
        if (!AssetStatuses.All.Contains(change.To)) throw new ApiException(StatusCodes.Status400BadRequest, $"status: one of {string.Join(", ", AssetStatuses.All)}");
        if (asset.Status == AssetStatuses.PendingAssetNumber && change.To != asset.Status)
            throw new ApiException(StatusCodes.Status409Conflict, "The asset is waiting for its Business Central asset number; its status changes when the number is issued");
        if (!AccessPolicy.CanChangeStatus(user, asset, asset.AssetCategory.IsIt, asset.Status, change.To))
        {
            throw AccessPolicy.Deny(user, asset.AssetCategory.IsIt && user.Role == Roles.Stores && change.To == AssetStatuses.InStore
                ? "take back an IT item that SBU IT has not recommended for disposal (ST-05: IT returns go to SBU IT)"
                : AssetStatusMachine.IsAllowed(asset.Status, change.To)
                    ? $"{AssetStatusMachine.Transitions[asset.Status][change.To]} ({AssetStatusMachine.Label(asset.Status)} → {AssetStatusMachine.Label(change.To)})"
                    : "change this asset's status");
        }
        // The role is checked first, so someone outside the asset's scope learns nothing about its status.
        var problem = AssetStatusMachine.Check(asset.Status, change.To, FactsOf(asset, change));
        if (problem is not null) throw new ApiException(StatusCodes.Status409Conflict, problem);
        Apply(user, asset, change);
    }

    /// <summary>
    /// A change the system makes (asset number issued, gate exit / return). Returns false, leaving the asset as it is,
    /// when the table does not allow it (for example an asset in repair returning through the gate stays in repair).
    /// </summary>
    public bool TrySystemChange(AuthUser? actor, Asset asset, StatusChange change)
    {
        if (AssetStatusMachine.Check(asset.Status, change.To, FactsOf(asset, change)) is not null) return false;
        Apply(actor, asset, change);
        return true;
    }

    /// <summary>Where an item goes when it comes back: to its holder if it has one, otherwise into store.</summary>
    public static string ReturnStatus(Asset asset) =>
        asset.CustodianEmployeeId is not null || asset.DepartmentId is not null ? AssetStatuses.InUse : AssetStatuses.InStore;

    /// <summary>SI-02: an unassigned IT item stays with SBU IT; a non-IT item goes to the SBU's Stores.</summary>
    public static string StoreHolder(Asset asset, string? sbuCode) =>
        $"{sbuCode ?? "SBU"} {(asset.AssetCategory.IsIt ? "IT store" : "Stores")}";

    /// <summary>The changes a user may ask for from the asset's current status, with why each is refused (if it is).</summary>
    public static List<StatusTransitionDto> Options(AuthUser user, Asset asset) =>
        AssetStatusMachine.Next(asset.Status).Select(to =>
        {
            var workflow = to switch
            {
                AssetStatuses.RepairRequested => "repair",
                AssetStatuses.PendingDisposal => "disposal",
                AssetStatuses.TransferPending => "transfer",
                _ => null,
            };
            var guard = WorkflowOwned.Contains(asset.Status) ? "Decided in the approvals inbox"
                : to == AssetStatuses.Disposed ? "Decided in the approvals inbox"
                : to == AssetStatuses.InUse && (asset.AssetNumber is null || asset.RfidTag is null) ? "Tag the item and wait for its asset number first (ST-02)"
                : to == AssetStatuses.InRepair && asset.AssetCategory.IsIt && asset.Status != AssetStatuses.RepairRequested ? "Request the repair; Finance approves it first (SI-05)"
                : null;
            var role = AccessPolicy.CanChangeStatus(user, asset, asset.AssetCategory.IsIt, asset.Status, to) ? null : "Not for your role";
            return new StatusTransitionDto(to, AssetStatusMachine.Label(to), AssetStatusMachine.Transitions[asset.Status][to],
                guard is null && role is null, guard ?? role, to == AssetStatuses.LostStolen, workflow);
        }).ToList();

    private static AssetStatusMachine.Facts FactsOf(Asset asset, StatusChange change) =>
        new(asset.AssetNumber is not null, asset.RfidTag is not null, change.LostDate ?? asset.LostDate, change.Reporter ?? asset.LostReportedBy,
            change.Notes ?? asset.LostNotes, asset.AssetCategory?.IsIt ?? false);

    private void Apply(AuthUser? user, Asset asset, StatusChange change)
    {
        var from = asset.Status;
        audit.Record(user, "asset", asset.Id, asset.Id, "status_changed", "status", from, change.To,
            change.Reason ?? AssetStatusMachine.Transitions[from][change.To]);
        asset.Status = change.To;
        if (change.To == AssetStatuses.LostStolen && change.LostDate is not null)
        {
            asset.LostDate = change.LostDate!.Trim();
            asset.LostReportedBy = change.Reporter!.Trim();
            asset.LostNotes = change.Notes!.Trim();
            audit.Record(user, "asset", asset.Id, asset.Id, "reported_lost", "lostDate", null, asset.LostDate, $"{asset.LostReportedBy}: {asset.LostNotes}");
        }
        if (change.To == AssetStatuses.Disposed && asset.DisposalDate is null)
        {
            asset.DisposalDate = clock.UtcNow.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            audit.Record(user, "asset", asset.Id, asset.Id, "disposed", "disposalDate", null, asset.DisposalDate, change.Reason);
        }
        asset.UpdatedAt = clock.UtcNow;
    }
}
