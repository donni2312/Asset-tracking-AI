using System.Linq.Expressions;
using AssetIQ.Api.Data;
using AssetIQ.Api.Domain;
using AssetIQ.Api.Infrastructure;

namespace AssetIQ.Api.Auth;

/// <summary>
/// Who may see and change which assets (docs/user-stories.md, Permissions matrix). Phase 1 covers the
/// actions of the audit stories (AU-01 … AU-12); every rule here is applied by the API, never only by the
/// dashboard. Rules are pure so they can be unit tested; <see cref="Deny"/> turns a "no" into a 403.
/// </summary>
public static class AccessPolicy
{
    /// <summary>Roles that see every asset in every SBU (Group Finance, auditors, and the original roles until Phase 2).</summary>
    private static readonly HashSet<string> SeeEverything = [Roles.GroupFinance, Roles.Auditor, Roles.Admin, Roles.Operator, Roles.Viewer];

    /// <summary>The assets <paramref name="user"/> may see: applied to every list, report, dashboard and export.</summary>
    public static Expression<Func<Asset, bool>> Visible(AuthUser user)
    {
        var sbu = user.SbuId ?? -1;
        return user.Role switch
        {
            _ when SeeEverything.Contains(user.Role) => a => true,
            Roles.GroupIt => a => a.AssetCategory.IsIt,
            Roles.SbuFinance => a => a.SbuId == sbu,
            Roles.SbuIt => a => a.SbuId == sbu && a.AssetCategory.IsIt,
            Roles.SbuAdmin => a => a.SbuId == sbu && !a.AssetCategory.IsIt,
            // ASSUMPTION: until the Phase 3 "In store" status exists, Stores sees its SBU's items that have no
            // custodian yet or are still waiting for their asset number.
            Roles.Stores => a => a.SbuId == sbu && (a.Status == AssetStatuses.PendingAssetNumber || a.AssignedTo == null),
            _ => a => false,
        };
    }

    public static bool CanView(AuthUser user, Asset asset, bool isIt) => user.Role switch
    {
        _ when SeeEverything.Contains(user.Role) => true,
        Roles.GroupIt => isIt,
        Roles.SbuFinance => OwnSbu(user, asset.SbuId),
        Roles.SbuIt => OwnSbu(user, asset.SbuId) && isIt,
        Roles.SbuAdmin => OwnSbu(user, asset.SbuId) && !isIt,
        Roles.Stores => OwnSbu(user, asset.SbuId) && (asset.Status == AssetStatuses.PendingAssetNumber || asset.AssignedTo is null),
        _ => false,
    };

    /// <summary>
    /// Register an asset (matrix "Register / tag asset"; AM-01). IT assets: Group IT, SBU IT and Stores of the
    /// SBU, plus the original operator/admin roles. Non-IT assets: only that SBU's Admin Manager.
    /// </summary>
    // ASSUMPTION: the matrix lets Stores register at receipt, but AM-01 says no other role than the SBU Admin
    // Manager may create non-IT records; the stricter AM-01 rule wins, so Stores registers IT items only.
    public static bool CanRegister(AuthUser user, bool isIt, int sbuId) => isIt
        ? user.Role is Roles.GroupIt or Roles.Admin or Roles.Operator || (user.Role is Roles.SbuIt or Roles.Stores && OwnSbu(user, sbuId))
        : user.Role == Roles.SbuAdmin && OwnSbu(user, sbuId);

    /// <summary>Edit technical fields (matrix "Edit technical fields"; AM-01 for non-IT).</summary>
    public static bool CanEditTechnical(AuthUser user, bool isIt, int? sbuId) => isIt
        ? user.Role is Roles.GroupIt or Roles.Admin or Roles.Operator || (user.Role == Roles.SbuIt && OwnSbu(user, sbuId))
        : user.Role == Roles.SbuAdmin && OwnSbu(user, sbuId);

    /// <summary>AU-07: SBU IT (IT assets), the SBU Admin Manager (non-IT) or Stores of the asset's SBU log upgrades.</summary>
    public static bool CanLogUpgrade(AuthUser user, bool isIt, int? sbuId) =>
        OwnSbu(user, sbuId) && (user.Role == Roles.Stores || (isIt ? user.Role == Roles.SbuIt : user.Role == Roles.SbuAdmin));

    /// <summary>
    /// Financial decisions on an asset of <paramref name="sbuId"/>: cost, capitalisation, upgrade treatment,
    /// "No serial" reasons, category changes, month locks. Only SBU Finance of that SBU.
    /// </summary>
    public static bool IsSbuFinanceOf(AuthUser user, int? sbuId) => user.Role == Roles.SbuFinance && OwnSbu(user, sbuId);

    /// <summary>Run depreciation: SBU Finance for its SBU, Group Finance for any SBU or the whole Group.</summary>
    public static bool CanRunDepreciation(AuthUser user, int? sbuId) =>
        user.Role == Roles.GroupFinance || (sbuId is not null && IsSbuFinanceOf(user, sbuId));

    /// <summary>Matrix "Set categories &amp; depreciation rates": Group Finance owns categories and their policy.</summary>
    public static bool CanManageCategories(AuthUser user) => user.Role == Roles.GroupFinance;

    /// <summary>Template fields: Group IT for IT categories, the SBU Admin Manager for non-IT categories.</summary>
    public static bool CanManageFields(AuthUser user, bool isIt) => isIt ? user.Role == Roles.GroupIt : user.Role == Roles.SbuAdmin;

    /// <summary>Group settings: the role named on the setting (Group Finance or Group IT).</summary>
    public static bool CanEditSetting(AuthUser user, string editableBy) => user.Role == editableBy;

    /// <summary>AU-12: Group Finance creates auditor accounts and sets their expiry.</summary>
    public static bool CanManageAuditors(AuthUser user) => user.Role == Roles.GroupFinance;

    private static bool OwnSbu(AuthUser user, int? sbuId) =>
        Roles.IsSbuScoped(user.Role) && user.SbuId is not null && sbuId is not null && user.SbuId == sbuId;

    public static ApiException Deny(AuthUser user, string action) =>
        new(StatusCodes.Status403Forbidden, $"Your role ({Roles.DisplayName(user.Role)}) may not {action}.");
}
