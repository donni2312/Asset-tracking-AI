namespace AssetIQ.Api.Domain;

// The text values stored in the database and exchanged with the dashboard (lib/api-spec/openapi.yaml).
// They stay strings, as in the database, so an unexpected value in a row never breaks a read.

public static class AssetStatuses
{
    public const string Active = "active";
    public const string Maintenance = "maintenance";
    public const string Retired = "retired";
    public const string Missing = "missing";
    /// <summary>AU-03: waiting for its Business Central number; it cannot be tagged, issued or capitalised.</summary>
    public const string PendingAssetNumber = "pending_asset_number";
    public static readonly IReadOnlyList<string> All = [PendingAssetNumber, Active, Maintenance, Retired, Missing];
    /// <summary>Statuses a user may set; leaving <see cref="PendingAssetNumber"/> happens only when BC issues the number.</summary>
    public static readonly IReadOnlyList<string> Settable = [Active, Maintenance, Retired, Missing];
}

public static class AssetConditions
{
    public static readonly IReadOnlyList<string> All = ["new", "good", "fair", "poor", "damaged"];
}

public static class DepreciationMethods
{
    public const string StraightLine = "straight_line";
    public const string ReducingBalance = "reducing_balance";
    public static readonly IReadOnlyList<string> All = [StraightLine, ReducingBalance];
}

public static class TemplateFieldTypes
{
    public const string Text = "text";
    public const string Number = "number";
    public const string Integer = "integer";
    public const string Date = "date";
    public const string List = "list";
    public static readonly IReadOnlyList<string> All = [Text, Number, Integer, Date, List];
}

public static class UpgradeStatuses
{
    public const string Pending = "pending";
    public const string Capitalised = "capitalised";
    public const string Expensed = "expensed";
    public const string Rejected = "rejected";
}

/// <summary>Finance's decision on an upgrade (AU-07).</summary>
public static class UpgradeTreatments
{
    public const string Capitalise = "capitalise";
    public const string Expense = "expense";
    public const string Reject = "reject";
    public static readonly IReadOnlyList<string> All = [Capitalise, Expense, Reject];
}

public static class NoSerialStatuses
{
    public const string Pending = "pending";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
}

public static class LocationTypes
{
    public const string Gate = "gate";
    public static readonly IReadOnlyList<string> All = ["office", Gate, "warehouse", "meeting_room", "lab", "reception", "server_room"];
}

public static class MovementTriggers
{
    public const string Rfid = "rfid";
    public const string Manual = "manual";
    public const string GatePass = "gate_pass";
}

public static class GatePassStatuses
{
    public const string Pending = "pending";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
    public static readonly IReadOnlyList<string> All = [Pending, Approved, Rejected];
}

public static class GatePassSources
{
    /// <summary>Requested in advance through the dashboard / API.</summary>
    public const string Manual = "manual";
    /// <summary>Raised automatically when a gate reader saw the asset without an approved pass.</summary>
    public const string RfidGate = "rfid_gate";
}

public static class ReaderStatuses
{
    public const string Active = "active";
}

public static class InsightTypes
{
    public const string Anomaly = "anomaly";
    public const string IdleAsset = "idle_asset";
    public const string UnauthorizedMovement = "unauthorized_movement";
    public const string MissingAsset = "missing_asset";
    public const string MaintenanceDue = "maintenance_due";
    public const string UnusualPattern = "unusual_pattern";
}

public static class InsightSeverities
{
    public const string Info = "info";
    public const string Warning = "warning";
    public const string Critical = "critical";
}

public static class LifecycleEventTypes
{
    public const string Deployment = "deployment";
    public const string ReturnToIt = "return_to_it";
    public const string RepairSent = "repair_sent";
    public const string RepairReturned = "repair_returned";
    public const string SbuTransfer = "sbu_transfer";
    public const string Note = "note";
}

/// <summary>
/// The original three roles (least to most privileged: a role satisfies every requirement at or below it)
/// plus the scoped roles of docs/user-stories.md. Phase 1 adds the scoped roles the audit stories need; the
/// endpoints built before them still use the three-level <see cref="Rank"/>, onto which each scoped role is
/// mapped, until Phase 2 replaces that with the full permissions matrix.
/// </summary>
public static class Roles
{
    public const string Viewer = "viewer";
    public const string Operator = "operator";
    public const string Admin = "admin";

    public const string GroupFinance = "group_finance";
    public const string GroupIt = "group_it";
    public const string SbuIt = "sbu_it";
    public const string SbuFinance = "sbu_finance";
    public const string SbuAdmin = "sbu_admin";
    public const string Stores = "stores";
    /// <summary>AU-12: read-only, all SBUs, until the expiry date Group Finance sets.</summary>
    public const string Auditor = "auditor";

    public static readonly IReadOnlyList<string> All = [Viewer, Operator, Admin, GroupFinance, GroupIt, SbuIt, SbuFinance, SbuAdmin, Stores, Auditor];

    /// <summary>Roles that act on one SBU only (the user's <c>sbu_id</c>).</summary>
    public static readonly IReadOnlyList<string> SbuScoped = [SbuIt, SbuFinance, SbuAdmin, Stores];

    public static bool IsSbuScoped(string? role) => role is not null && SbuScoped.Contains(role);

    /// <summary>Display names, as on the sidebar ("Viewing as …").</summary>
    public static string DisplayName(string role) => role switch
    {
        GroupFinance => "Group Finance Executive",
        GroupIt => "Group IT Network & Hardware Admin",
        SbuIt => "SBU IT Admin / Technician",
        SbuFinance => "SBU Finance",
        SbuAdmin => "SBU Admin Manager",
        Stores => "Stores Clerk",
        Auditor => "Auditor",
        Admin => "Administrator",
        Operator => "Operator",
        Viewer => "Viewer",
        _ => role,
    };

    /// <summary>
    /// 0 viewer, 1 operator, 2 admin; -1 for anything else (satisfies nothing). Scoped roles map onto this
    /// ladder for the pre-Phase-1 endpoints. The auditor is handled separately: it satisfies viewer-level
    /// requirements on reads only (see <see cref="Auth.MinimumRoleHandler"/>).
    /// </summary>
    // ASSUMPTION: until Phase 2, Group IT may use the admin-only tools (RFID simulator, BC sync), the SBU
    // custody roles get operator rights on the older endpoints, and the finance roles viewer rights.
    public static int Rank(string? role) => role switch
    {
        Viewer or SbuFinance or GroupFinance => 0,
        Operator or SbuIt or SbuAdmin or Stores => 1,
        Admin or GroupIt => 2,
        _ => -1,
    };

    public static bool Satisfies(string? role, string required) => Rank(role) >= 0 && Rank(role) >= Rank(required);
}
