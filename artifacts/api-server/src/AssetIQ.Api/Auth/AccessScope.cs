using AssetIQ.Api.Data;
using AssetIQ.Api.Domain;

namespace AssetIQ.Api.Auth;

/// <summary>
/// Which assets the current request may see (the "View assets" row of the permissions matrix). One instance
/// per request: the token handler narrows it to the signed-in caller, and signed-out requests see nothing.
/// <see cref="AssetIqDbContext"/> applies it as a global query filter to assets and to every table that hangs
/// off an asset (movements, gate passes, lifecycle events, upgrades, insights, attachments), so a list, report,
/// dashboard, export or AI answer cannot include another SBU's records even if a query forgets to filter.
/// Work outside a request (the Business Central worker, tests arranging data) is unrestricted; the few rules
/// that must look across SBUs inside a request (Group-unique serials, RFID reads, scans) say so with
/// <c>IgnoreQueryFilters()</c>.
/// </summary>
public sealed class AccessScope
{
    public bool Unrestricted { get; private set; } = true;
    /// <summary>Nothing at all (signed out, System Administrator, or a role missing its SBU / employee link).</summary>
    public bool Nothing { get; private set; }
    public int? SbuId { get; private set; }
    /// <summary>true: IT assets only; false: non-IT only; null: both.</summary>
    public bool? It { get; private set; }
    /// <summary>Stores: items in store (until Phase 3, items not issued to an employee or a department).</summary>
    public bool InStoreOnly { get; private set; }
    /// <summary>HOD: assets held by their department.</summary>
    public int? DepartmentId { get; private set; }
    /// <summary>Employee: the items they hold.</summary>
    public int? EmployeeId { get; private set; }
    /// <summary>SBU HR: items held by employees (the toolboxes).</summary>
    public bool HeldByEmployee { get; private set; }

    /// <summary>A signed-out request: sees nothing.</summary>
    public void DenyAll() => Set(new AccessScope { Unrestricted = false, Nothing = true });

    /// <summary>Narrows this request's scope to what <paramref name="user"/> may see.</summary>
    public void Narrow(AuthUser user) => Set(For(user));

    public static AccessScope For(AuthUser user)
    {
        var sbu = user.SbuId;
        if (Roles.IsSbuScoped(user.Role) && sbu is null) return None();
        return user.Role switch
        {
            Roles.GroupFinance or Roles.Auditor => new AccessScope(),
            Roles.GroupIt => Restricted(it: true),
            Roles.SbuFinance => Restricted(sbu),
            Roles.SbuIt => Restricted(sbu, it: true),
            Roles.SbuAdmin => Restricted(sbu, it: false),
            // ASSUMPTION: until the Phase 3 "In store" status exists, Stores sees its SBU's items that are not issued
            // to an employee or a department (including what Stores itself has just received and registered).
            Roles.Stores => Restricted(sbu, inStore: true),
            Roles.SbuHr => Restricted(sbu, heldByEmployee: true),
            Roles.Hod => user.DepartmentId is { } department ? Restricted(sbu, department: department) : None(),
            Roles.Employee => user.EmployeeId is { } employee ? Restricted(sbu, employee: employee) : None(),
            // The Gate Officer reaches assets only through a scan or a gate pass; the endpoints enforce that.
            Roles.GateOfficer => Restricted(sbu),
            _ => None(),
        };
    }

    /// <summary>The same rule as the query filter, for one asset already loaded.</summary>
    public bool Allows(Asset asset, bool isIt) =>
        Unrestricted || (!Nothing
            && (SbuId is null || asset.SbuId == SbuId)
            && (It is null || isIt == It)
            && (!InStoreOnly || (asset.CustodianEmployeeId == null && asset.DepartmentId == null))
            && (DepartmentId is null || asset.DepartmentId == DepartmentId)
            && (EmployeeId is null || asset.CustodianEmployeeId == EmployeeId)
            && (!HeldByEmployee || asset.CustodianEmployeeId != null));

    /// <summary>A Group-wide scope for the caller's role (no SBU restriction): every SBU, or every SBU's IT assets.</summary>
    public bool IsGroupWide => Unrestricted || (!Nothing && SbuId is null);

    private static AccessScope None() => new() { Unrestricted = false, Nothing = true };

    private static AccessScope Restricted(int? sbu = null, bool? it = null, bool inStore = false, int? department = null, int? employee = null, bool heldByEmployee = false) =>
        new()
        {
            Unrestricted = false, SbuId = sbu, It = it, InStoreOnly = inStore, DepartmentId = department, EmployeeId = employee,
            HeldByEmployee = heldByEmployee,
        };

    private void Set(AccessScope other)
    {
        Unrestricted = other.Unrestricted;
        Nothing = other.Nothing;
        SbuId = other.SbuId;
        It = other.It;
        InStoreOnly = other.InStoreOnly;
        DepartmentId = other.DepartmentId;
        EmployeeId = other.EmployeeId;
        HeldByEmployee = other.HeldByEmployee;
    }
}
