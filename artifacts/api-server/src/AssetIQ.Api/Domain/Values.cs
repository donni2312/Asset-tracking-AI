namespace AssetIQ.Api.Domain;

// The text values stored in the database and exchanged with the dashboard (lib/api-spec/openapi.yaml).
// They stay strings, as in the database, so an unexpected value in a row never breaks a read.

public static class AssetStatuses
{
    /// <summary>AU-03: waiting for its Business Central number; it cannot be tagged, issued or capitalised.</summary>
    public const string PendingAssetNumber = "pending_asset_number";
    public const string InStore = "in_store";
    public const string InUse = "in_use";
    public const string OutOnGatePass = "out_on_gate_pass";
    /// <summary>Phase 4: an IT repair waiting for Finance's approval (product owner, 29 Oct 2026).</summary>
    public const string RepairRequested = "repair_requested";
    public const string InRepair = "in_repair";
    public const string TransferPending = "transfer_pending";
    public const string PendingDisposal = "pending_disposal";
    /// <summary>GI-07: needs a date, a reporter and notes; excluded from active counts.</summary>
    public const string LostStolen = "lost_stolen";
    /// <summary>Final.</summary>
    public const string Disposed = "disposed";
    /// <summary>Phase 3 statuses; the transitions between them are <see cref="AssetStatusMachine"/>.</summary>
    public static readonly IReadOnlyList<string> All =
        [PendingAssetNumber, InStore, InUse, OutOnGatePass, RepairRequested, InRepair, TransferPending, PendingDisposal, LostStolen, Disposed];
    /// <summary>Statuses a user may ask for; leaving <see cref="PendingAssetNumber"/> happens only when BC issues the number.</summary>
    public static readonly IReadOnlyList<string> Settable = All.Where(s => s != PendingAssetNumber).ToList();
}

/// <summary>Phase 4c: the Gate Officer's verdict on a scan (SG-01) and action on an alert (SG-02).</summary>
public static class GateVerdicts
{
    public const string Clear = "clear";
    public const string Return = "return";
    public const string Stop = "stop";
}

public static class GateActions
{
    public const string Stopped = "stopped";
    public const string ReleasedWithOverride = "released_with_override";
    public const string Escalated = "escalated";
    public static readonly IReadOnlyList<string> All = [Stopped, ReleasedWithOverride, Escalated];
}

/// <summary>Phase 4c: verification counts (SI-07) and their reconciliation (SF-06).</summary>
public static class VerificationScopes
{
    public const string It = "it";
    public const string NonIt = "non_it";
    public const string All = "all";
    public static readonly IReadOnlyList<string> Values = [It, NonIt, All];
}

public static class VerificationStatuses
{
    public const string Open = "open";
    public const string Closed = "closed";
    public const string Reconciled = "reconciled";
}

public static class VerificationResults
{
    public const string Found = "found";
    public const string Missing = "missing";
    public const string Unrecorded = "unrecorded";
}

public static class VerificationResolutions
{
    public const string Adjust = "adjust";
    public const string WriteOff = "write_off";
    public const string Investigate = "investigate";
    public const string Register = "register";
    public static readonly IReadOnlyList<string> All = [Adjust, WriteOff, Investigate, Register];
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
    public const string Inactive = "inactive";
    public static readonly IReadOnlyList<string> All = [Active, Inactive];
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

public static class AttachmentKinds
{
    public const string Invoice = "invoice";
    public const string Photo = "photo";
    public const string Other = "other";
    public static readonly IReadOnlyList<string> All = [Invoice, Photo, Other];
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
/// The roles of the docs/user-stories.md Permissions matrix (Phase 2), plus the Auditor (AU-12), the
/// Employee and the System Administrator. Group roles act across SBUs; SBU roles act on their own SBU
/// (<c>users.sbu_id</c>). What each role may do is decided in <see cref="Auth.AccessPolicy"/>; the role groups
/// below are the endpoint-level gate (<c>[RequireRole(Roles.AssetReaders)]</c>), and the record-level rules
/// (own SBU, IT or non-IT, own department) are checked on top of them.
/// </summary>
public static class Roles
{
    public const string GroupFinance = "group_finance";
    public const string GroupIt = "group_it";
    public const string SbuIt = "sbu_it";
    public const string SbuFinance = "sbu_finance";
    public const string GateOfficer = "gate_officer";
    /// <summary>Line Manager / Head of Department: their own department.</summary>
    public const string Hod = "hod";
    public const string SbuHr = "sbu_hr";
    public const string Stores = "stores";
    public const string SbuAdmin = "sbu_admin";
    /// <summary>AU-12: read-only, all SBUs, until the expiry date Group Finance sets.</summary>
    public const string Auditor = "auditor";
    /// <summary>Sees the items they hold.</summary>
    public const string Employee = "employee";
    /// <summary>
    /// May do everything, including setting the final depreciation rates and configuring gates; every request is
    /// recorded in the access log (product owner, 29 Oct 2026). Not a column of the permissions matrix.
    /// </summary>
    public const string SystemAdmin = "system_admin";

    public static readonly IReadOnlyList<string> All =
        [GroupFinance, GroupIt, SbuIt, SbuFinance, GateOfficer, Hod, SbuHr, Stores, SbuAdmin, Auditor, Employee, SystemAdmin];

    /// <summary>Roles that act on one SBU only (the user's <c>sbu_id</c>, required for these roles).</summary>
    public static readonly IReadOnlyList<string> SbuScoped = [SbuIt, SbuFinance, GateOfficer, Hod, SbuHr, Stores, SbuAdmin, Employee];

    /// <summary>Roles that must be linked to an employee record (their scope is a department or a person).</summary>
    public static readonly IReadOnlyList<string> NeedEmployee = [Hod, Employee];

    public static bool IsSbuScoped(string? role) => role is not null && SbuScoped.Contains(role);

    // Endpoint-level role groups (comma-separated so they can be used in attributes). "*" = any signed-in role.
    public const string Everyone = "*";
    /// <summary>Matrix "View assets": every role with an asset scope (the Gate Officer sees assets only through a scan).</summary>
    public const string AssetReaders = GroupFinance + "," + GroupIt + "," + SbuIt + "," + SbuFinance + "," + Hod + "," + SbuHr + "," + Stores + "," + SbuAdmin + "," + Auditor + "," + Employee;
    /// <summary>Asset card, history, dashboards, insights and exports: the asset readers except the Employee.</summary>
    public const string RecordReaders = GroupFinance + "," + GroupIt + "," + SbuIt + "," + SbuFinance + "," + Hod + "," + SbuHr + "," + Stores + "," + SbuAdmin + "," + Auditor;
    /// <summary>Fixed asset register, category report, Asset Position, completeness, approvals (AU-09 … AU-11, SF-02).</summary>
    public const string FinanceReaders = GroupFinance + "," + SbuFinance + "," + Auditor;
    /// <summary>Matrix "Check exits and returns at gate": the Gate Officer records them; Group IT and SBU IT view the log.</summary>
    public const string GateLogReaders = GroupIt + "," + SbuIt + "," + GateOfficer + "," + Auditor;
    public const string GatePassReaders = GroupFinance + "," + GroupIt + "," + SbuIt + "," + SbuFinance + "," + GateOfficer + "," + Hod + "," + SbuAdmin + "," + Auditor;
    /// <summary>Matrix "Raise gate pass".</summary>
    public const string GatePassRaisers = GroupIt + "," + SbuIt + "," + SbuAdmin;
    /// <summary>Matrix "Approve gate pass" (record-level rules in AccessPolicy.CanDecideGatePass).</summary>
    public const string GatePassApprovers = GroupIt + "," + SbuIt + "," + SbuFinance + "," + Hod + "," + SbuAdmin;
    /// <summary>Location moves (technical field) and gate exits / returns.</summary>
    public const string Movers = GroupIt + "," + SbuIt + "," + SbuAdmin + "," + GateOfficer;
    /// <summary>Issue / assign, returns, repairs, transfers, notes (per-event rules in AccessPolicy.CanRecordLifecycle).</summary>
    public const string LifecycleWriters = GroupIt + "," + SbuIt + "," + Stores + "," + SbuAdmin;
    public const string TechnicalEditors = GroupIt + "," + SbuIt + "," + SbuAdmin;
    /// <summary>Phase 4: repairs (IT records the work, Finance the cost).</summary>
    public const string RepairEditors = TechnicalEditors + "," + GroupFinance + "," + SbuFinance;
    /// <summary>Phase 4: recommend a disposal (technical owner, Stores "return as scrap", SBU Finance writing off a lost item).</summary>
    public const string DisposalRecommenders = TechnicalEditors + "," + Stores + "," + SbuFinance;
    /// <summary>Phase 4: roles that decide approval steps and may delegate while on leave (LM-03).</summary>
    /// <summary>Phase 4b: purchase orders, receipts and stock.</summary>
    public const string StoresReaders = GroupFinance + "," + GroupIt + "," + SbuFinance + "," + SbuIt + "," + Stores + "," + SbuAdmin + "," + Auditor;
    /// <summary>Matrix "Request equipment": Group IT, SBU IT, HOD; SBU HR for onboarding kits (HR-02).</summary>
    public const string EquipmentRequesters = GroupIt + "," + SbuIt + "," + Hod + "," + SbuHr;
    /// <summary>HR-04 / SI-09: who records an item of an exit clearance (record-level rules in ToolboxService).</summary>
    public const string ClearanceRecorders = SbuHr + "," + SbuIt + "," + SbuAdmin + "," + Stores;
    public const string Approvers = Hod + "," + SbuAdmin + "," + SbuIt + "," + GroupIt + "," + SbuFinance + "," + GroupFinance;
    /// <summary>Phase 3 status changes (record-level rules: AccessPolicy.CanChangeStatus).</summary>
    public const string StatusChangers = GroupFinance + "," + GroupIt + "," + SbuIt + "," + SbuFinance + "," + GateOfficer + "," + Stores + "," + SbuAdmin;
    /// <summary>Matrix "Register / tag asset" plus SBU Finance ("Capitalise only", checked per action).</summary>
    public const string Registrars = GroupIt + "," + SbuIt + "," + Stores + "," + SbuAdmin;
    public const string FinanceWriters = GroupFinance + "," + SbuFinance;
    /// <summary>Who may attach files (record-level rule: AccessPolicy.CanAttach).</summary>
    public const string Attachers = GroupIt + "," + SbuIt + "," + Stores + "," + SbuAdmin + "," + SbuFinance;
    public const string InsightManagers = GroupIt + "," + SbuIt + "," + SbuAdmin;
    public const string LocationManagers = GroupIt + "," + SbuAdmin;
    public const string SbuManagers = GroupIt + "," + SystemAdmin;
    public const string ReaderViewers = GroupIt + "," + SbuIt + "," + GateOfficer + "," + Auditor;
    /// <summary>Gates and RFID readers are configured by the System Administrator (who passes every role gate).</summary>
    public const string GateManagers = SystemAdmin;
    public const string BcViewers = GroupFinance + "," + GroupIt + "," + SystemAdmin + "," + Auditor;
    public const string BcOperators = GroupFinance + "," + SystemAdmin;
    /// <summary>Matrix "Manage users".</summary>
    public const string UserManagers = GroupIt + "," + SbuIt;
    /// <summary>Departments and employees: SBU HR maintains them; the roles that assign or account for items read them.</summary>
    public const string PeopleReaders = GroupFinance + "," + GroupIt + "," + SbuIt + "," + SbuFinance + "," + Hod + "," + SbuHr + "," + Stores + "," + SbuAdmin + "," + Auditor;
    public const string CategoryOwners = GroupFinance + "," + SystemAdmin;
    public const string Administrators = GroupFinance + "," + GroupIt + "," + SbuAdmin + "," + SystemAdmin;
    public const string AuditorManagers = GroupFinance;
    /// <summary>AU-12: who signed in and exported what.</summary>
    public const string AccessLogReaders = GroupFinance;
    /// <summary>AU-07: SBU IT (IT), the SBU Admin Manager (non-IT) and Stores log upgrades.</summary>
    public const string Upgraders = SbuIt + "," + SbuAdmin + "," + Stores;
    /// <summary>Template fields: Group IT (IT categories), the SBU Admin Manager (non-IT categories).</summary>
    public const string FieldOwners = GroupIt + "," + SbuAdmin;
    /// <summary>Group settings name the role that edits them (Group Finance or Group IT); the System Administrator edits all.</summary>
    public const string SettingOwners = GroupFinance + "," + GroupIt + "," + SystemAdmin;
    /// <summary>Phase 4c: the gate screen (scan, exits / returns, alerts); Group IT and SBU IT see it as the log readers do.</summary>
    public const string GateOfficers = GateOfficer + "," + GroupIt + "," + SbuIt + "," + SbuAdmin;
    /// <summary>Phase 4c: who counts a location (SI-07): SBU IT / Group IT (IT), Stores / Admin Manager (non-IT), Finance (any).</summary>
    public const string VerificationRunners = GroupIt + "," + SbuIt + "," + Stores + "," + SbuAdmin + "," + SbuFinance + "," + GroupFinance;
    public const string VerificationReaders = VerificationRunners + "," + Auditor;
    /// <summary>Everyone who may scan a tag (SI-03, SG-01): every role except the System Administrator.</summary>
    public const string Scanners = AssetReaders + "," + GateOfficer;

    /// <summary>The roles of a comma-separated group.</summary>
    public static IReadOnlySet<string> Parse(string group) => group.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToHashSet();

    /// <summary>Display names, as on the sidebar ("Viewing as …").</summary>
    public static string DisplayName(string role) => role switch
    {
        GroupFinance => "Group Finance Executive",
        GroupIt => "Group IT Network & Hardware Admin",
        SbuIt => "SBU IT Admin / Technician",
        SbuFinance => "SBU Finance",
        GateOfficer => "Gate Officer",
        Hod => "Line Manager / HOD",
        SbuHr => "SBU HR",
        Stores => "Stores Clerk",
        SbuAdmin => "SBU Admin Manager",
        Auditor => "Auditor",
        Employee => "Employee",
        SystemAdmin => "System Administrator",
        _ => role,
    };
}

