using System.Linq.Expressions;
using AssetIQ.Api.Data;
using AssetIQ.Api.Domain;
using AssetIQ.Api.Infrastructure;

namespace AssetIQ.Api.Auth;

/// <summary>
/// Who may see and change which assets: the Permissions matrix of docs/user-stories.md (Phase 2). Endpoints
/// first check the role group (<see cref="RequireRoleAttribute"/>); these record-level rules then check the
/// SBU, IT / non-IT and department. Every rule is applied by the API, never only by the dashboard. Rules are
/// pure so they can be unit tested; <see cref="Deny"/> turns a "no" into a 403.
/// </summary>
public static class AccessPolicy
{
    /// <summary>The assets <paramref name="user"/> may see (the same rule as the global query filter).</summary>
    public static Expression<Func<Asset, bool>> Visible(AuthUser user)
    {
        var s = AccessScope.For(user);
        return a => s.Unrestricted || (!s.Nothing
            && (s.SbuId == null || a.SbuId == s.SbuId)
            && (s.It == null || a.AssetCategory.IsIt == s.It)
            && (!s.InStoreOnly || a.Status == AssetStatuses.PendingAssetNumber
            || (a.Status == AssetStatuses.InStore && (!a.AssetCategory.IsIt || a.RfidTag == null))
            || (a.Status == AssetStatuses.PendingDisposal && a.AssetCategory.IsIt))
            && (s.DepartmentId == null || a.DepartmentId == s.DepartmentId)
            && (s.EmployeeId == null || a.CustodianEmployeeId == s.EmployeeId)
            && (!s.HeldByEmployee || a.CustodianEmployeeId != null));
    }

    public static bool CanView(AuthUser user, Asset asset, bool isIt) => AccessScope.For(user).Allows(asset, isIt);

    /// <summary>
    /// Register an asset (matrix "Register / tag asset"; AM-01). IT assets: Group IT, and SBU IT or Stores of the
    /// SBU. Non-IT assets: the SBU's Admin Manager, or its Stores at receipt (product owner, 29 Oct 2026).
    /// </summary>
    public static bool CanRegister(AuthUser user, bool isIt, int sbuId) => Sys(user) || (isIt
        ? user.Role == Roles.GroupIt || (user.Role is Roles.SbuIt or Roles.Stores && OwnSbu(user, sbuId))
        : user.Role is Roles.SbuAdmin or Roles.Stores && OwnSbu(user, sbuId));

    /// <summary>Edit technical fields (matrix "Edit technical fields"; AM-01: only the Admin Manager edits non-IT records).</summary>
    public static bool CanEditTechnical(AuthUser user, bool isIt, int? sbuId) => Sys(user) || (isIt
        ? user.Role == Roles.GroupIt || (user.Role == Roles.SbuIt && OwnSbu(user, sbuId))
        : user.Role == Roles.SbuAdmin && OwnSbu(user, sbuId));

    /// <summary>AU-07: SBU IT (IT assets), the SBU Admin Manager (non-IT) or Stores of the asset's SBU log upgrades.</summary>
    public static bool CanLogUpgrade(AuthUser user, bool isIt, int? sbuId) => Sys(user) ||
        OwnSbu(user, sbuId) && (user.Role == Roles.Stores || (isIt ? user.Role == Roles.SbuIt : user.Role == Roles.SbuAdmin));

    /// <summary>
    /// Financial decisions on an asset of <paramref name="sbuId"/>: cost, capitalisation, upgrade treatment,
    /// "No serial" reasons, category changes, month locks. Only SBU Finance of that SBU.
    /// </summary>
    public static bool IsSbuFinanceOf(AuthUser user, int? sbuId) => Sys(user) || user.Role == Roles.SbuFinance && OwnSbu(user, sbuId);

    /// <summary>Run depreciation: SBU Finance for its SBU, Group Finance for any SBU or the whole Group.</summary>
    public static bool CanRunDepreciation(AuthUser user, int? sbuId) =>
        Sys(user) || user.Role == Roles.GroupFinance || (sbuId is not null && IsSbuFinanceOf(user, sbuId));

    /// <summary>
    /// Matrix "Set categories &amp; depreciation rates": Group Finance, and the System Administrator, who sets the
    /// final rates (product owner, 29 Oct 2026).
    /// </summary>
    public static bool CanManageCategories(AuthUser user) => user.Role is Roles.GroupFinance or Roles.SystemAdmin;

    /// <summary>Template fields: Group IT for IT categories, the SBU Admin Manager for non-IT categories.</summary>
    public static bool CanManageFields(AuthUser user, bool isIt) => Sys(user) || (isIt ? user.Role == Roles.GroupIt : user.Role == Roles.SbuAdmin);

    /// <summary>Group settings: the role named on the setting (Group Finance or Group IT), or the System Administrator.</summary>
    public static bool CanEditSetting(AuthUser user, string editableBy) => user.Role == editableBy || user.Role == Roles.SystemAdmin;

    /// <summary>AU-12: Group Finance creates auditor accounts and sets their expiry.</summary>
    public static bool CanManageAuditors(AuthUser user) => Sys(user) || user.Role == Roles.GroupFinance;

    /// <summary>
    /// Attach an invoice, photo or document to an asset: whoever may edit its technical record, its SBU
    /// Finance (invoices, SF-01), and Stores of the SBU for the items they hold.
    /// </summary>
    public static bool CanAttach(AuthUser user, Asset asset, bool isIt) =>
        CanEditTechnical(user, isIt, asset.SbuId) || IsSbuFinanceOf(user, asset.SbuId)
        || (user.Role == Roles.Stores && CanView(user, asset, isIt));

    /// <summary>Matrix "Raise gate pass": Group IT and SBU IT for IT assets, the SBU Admin Manager for non-IT assets.</summary>
    public static bool CanRaiseGatePass(AuthUser user, Asset asset, bool isIt) => CanEditTechnical(user, isIt, asset.SbuId);

    /// <summary>
    /// Matrix "Approve gate pass": Group IT and SBU IT (IT assets of their scope); the HOD for assets their
    /// department holds; the SBU Admin Manager when the department has no HOD to decide; SBU Finance only for
    /// high-value passes (SF-05).
    /// </summary>
    // ASSUMPTION: "HOD unavailable" means the asset's department has no active HOD account (leave and
    // delegation arrive with the Phase 4 approval workflow). The pass's value is the asset's USD cost.
    public static bool CanDecideGatePass(AuthUser user, Asset asset, bool isIt, bool highValue, bool hodAvailable) => user.Role switch
    {
        Roles.SystemAdmin => true,
        Roles.GroupIt => isIt,
        Roles.SbuIt => isIt && OwnSbu(user, asset.SbuId),
        Roles.Hod => OwnSbu(user, asset.SbuId) && asset.DepartmentId is not null && asset.DepartmentId == user.DepartmentId,
        Roles.SbuAdmin => OwnSbu(user, asset.SbuId) && !hodAvailable,
        Roles.SbuFinance => OwnSbu(user, asset.SbuId) && highValue,
        _ => false,
    };

    /// <summary>A location move (a technical field), or a gate exit / return recorded by that SBU's Gate Officer.</summary>
    public static bool CanMove(AuthUser user, Asset asset, bool isIt) =>
        Sys(user) || CanEditTechnical(user, isIt, asset.SbuId) || (user.Role == Roles.GateOfficer && OwnSbu(user, asset.SbuId));

    /// <summary>
    /// Lifecycle events. Issue / assign: Group IT, SBU IT (IT), the SBU Admin Manager (non-IT) and Stores.
    /// Returns: IT staff and Stores. Repairs and notes: whoever edits the technical record. Inter-SBU transfer:
    /// Group IT approves (IT assets); SBU IT may only request (Phase 4).
    /// </summary>
    // ASSUMPTION: a non-IT transfer between SBUs needs both Admin Managers to confirm (AM-08); until that
    // workflow exists (Phase 4) nobody records one directly.
    public static bool CanRecordLifecycle(AuthUser user, string eventType, Asset asset, bool isIt)
    {
        if (Sys(user)) return true;
        var technical = CanEditTechnical(user, isIt, asset.SbuId);
        var stores = user.Role == Roles.Stores && OwnSbu(user, asset.SbuId);
        return eventType switch
        {
            LifecycleEventTypes.Deployment or LifecycleEventTypes.Note => technical || stores,
            LifecycleEventTypes.ReturnToIt => (isIt && technical) || stores,
            LifecycleEventTypes.RepairSent or LifecycleEventTypes.RepairReturned => technical,
            LifecycleEventTypes.SbuTransfer => user.Role == Roles.GroupIt && isIt,
            _ => false,
        };
    }

    /// <summary>
    /// Who may make a status change (Phase 3), on top of the state machine. Issue / return: as the lifecycle events
    /// (Stores may not take back an IT item that SBU IT has not recommended for disposal, ST-05). Repairs, transfer
    /// requests, lost / stolen and recovery: whoever edits the technical record. Deciding a transfer: Group IT (IT)
    /// or the SBU Admin Manager (non-IT). Recommending disposal: the technical owner or Stores ("return as scrap").
    /// Deciding a disposal and writing off a lost item: SBU Finance of the SBU, or Group Finance.
    /// </summary>
    // ASSUMPTION: Group Finance's above-threshold sign-off (GF-04) and Group IT's data-wipe sign-off (GI-10) become
    // extra approval steps in Phase 4; until then SBU Finance or Group Finance decides every disposal.
    public static bool CanChangeStatus(AuthUser user, Asset asset, bool isIt, string from, string to)
    {
        if (Sys(user)) return true;
        var technical = CanEditTechnical(user, isIt, asset.SbuId);
        var stores = user.Role == Roles.Stores && OwnSbu(user, asset.SbuId);
        var finance = user.Role == Roles.GroupFinance || IsSbuFinanceOf(user, asset.SbuId);
        return (from, to) switch
        {
            (_, AssetStatuses.InUse) when from is AssetStatuses.InStore => technical || stores,
            (AssetStatuses.InUse, AssetStatuses.InStore) => technical || (stores && !isIt),
            (AssetStatuses.TransferPending, _) => isIt ? user.Role == Roles.GroupIt : user.Role == Roles.SbuAdmin && OwnSbu(user, asset.SbuId),
            (AssetStatuses.PendingDisposal, _) => finance,
            (AssetStatuses.LostStolen, AssetStatuses.PendingDisposal) => finance,
            (_, AssetStatuses.PendingDisposal) => technical || stores,
            (_, AssetStatuses.OutOnGatePass) or (AssetStatuses.OutOnGatePass, _) =>
                technical || (user.Role == Roles.GateOfficer && OwnSbu(user, asset.SbuId)),
            _ => technical,
        };
    }

    /// <summary>Dismiss an AI insight: who looks after the asset (Group IT for insights not about one asset).</summary>
    public static bool CanDismissInsight(AuthUser user, Asset? asset, bool isIt) =>
        Sys(user) || (asset is null ? user.Role == Roles.GroupIt : CanEditTechnical(user, isIt, asset.SbuId));

    // ASSUMPTION: locations are kept by Group IT (any SBU) and the SBU Admin Manager (own SBU); the matrix has no row.
    // Gates and their RFID readers are configured by the System Administrator only (product owner, 29 Oct 2026).
    public static bool CanManageLocation(AuthUser user, int? sbuId, string? type = null) =>
        Sys(user) || (type != LocationTypes.Gate && (user.Role == Roles.GroupIt || (user.Role == Roles.SbuAdmin && OwnSbu(user, sbuId))));

    /// <summary>Gates and RFID readers: how many there are per SBU and how they are set up (System Administrator).</summary>
    public static bool CanManageGates(AuthUser user) => Sys(user);

    /// <summary>
    /// Matrix "Manage users" (GI-08): Group IT manages every account except auditors (Group Finance, AU-12); SBU
    /// IT manages its own SBU's accounts and may only give SBU roles in that SBU. Nobody changes their own role.
    /// </summary>
    public static bool CanManageUser(AuthUser user, string role, int? sbuId) => user.Role switch
    {
        Roles.SystemAdmin => true,
        Roles.GroupIt => role != Roles.Auditor,
        Roles.SbuIt => Roles.IsSbuScoped(role) && sbuId is not null && OwnSbu(user, sbuId),
        _ => false,
    };

    /// <summary>Departments and employees: SBU HR maintains its own SBU's (HR-01).</summary>
    public static bool CanManagePeople(AuthUser user, int sbuId) => Sys(user) || (user.Role == Roles.SbuHr && OwnSbu(user, sbuId));

    /// <summary>Who sees which employees: Group roles every SBU (read-only); the HOD their department; others their SBU.</summary>
    public static bool CanViewPerson(AuthUser user, int sbuId, int? departmentId) => user.Role switch
    {
        Roles.GroupFinance or Roles.GroupIt or Roles.Auditor or Roles.SystemAdmin => true,
        Roles.Hod => OwnSbu(user, sbuId) && departmentId is not null && departmentId == user.DepartmentId,
        _ => PeopleReaders.Contains(user.Role) && OwnSbu(user, sbuId),
    };

    private static readonly IReadOnlySet<string> PeopleReaders = Roles.Parse(Roles.PeopleReaders);

    /// <summary>
    /// Cost, value, depreciation and invoice amounts: Group Finance, SBU Finance, auditors and the System
    /// Administrator. Group IT sees the asset inventory only, and the Employee never sees what an item cost
    /// (product owner, 29 Oct 2026).
    /// </summary>
    // ASSUMPTION: the other operational roles (SBU IT, SBU Admin Manager, Stores, HOD, SBU HR, Gate Officer) do not
    // see financial data either; HR-05 (NBV of missing items) will revisit this for SBU HR in Phase 4.
    public static bool CanSeeFinancials(AuthUser user) => user.Role is Roles.GroupFinance or Roles.SbuFinance or Roles.Auditor or Roles.SystemAdmin;

    /// <summary>
    /// The System Administrator may do everything (product owner, 29 Oct 2026); every request they make is
    /// recorded in the access log, and every change in the audit trail as for anyone else.
    /// </summary>
    public static bool Sys(AuthUser user) => user.Role == Roles.SystemAdmin;

    public static bool OwnSbu(AuthUser user, int? sbuId) =>
        Roles.IsSbuScoped(user.Role) && user.SbuId is not null && sbuId is not null && user.SbuId == sbuId;

    public static ApiException Deny(AuthUser user, string action) =>
        new(StatusCodes.Status403Forbidden, $"Your role ({Roles.DisplayName(user.Role)}) may not {action}.");
}
