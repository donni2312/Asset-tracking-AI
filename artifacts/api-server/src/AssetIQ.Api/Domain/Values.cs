namespace AssetIQ.Api.Domain;

// The text values stored in the database and exchanged with the dashboard (lib/api-spec/openapi.yaml).
// They stay strings, as in the database, so an unexpected value in a row never breaks a read.

public static class AssetStatuses
{
    public const string Active = "active";
    public const string Maintenance = "maintenance";
    public const string Retired = "retired";
    public const string Missing = "missing";
    public static readonly IReadOnlyList<string> All = [Active, Maintenance, Retired, Missing];
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

/// <summary>Least to most privileged: a role satisfies every requirement at or below it.</summary>
public static class Roles
{
    public const string Viewer = "viewer";
    public const string Operator = "operator";
    public const string Admin = "admin";
    public static readonly IReadOnlyList<string> All = [Viewer, Operator, Admin];

    /// <summary>0 viewer, 1 operator, 2 admin; -1 for anything else (satisfies nothing).</summary>
    public static int Rank(string? role) => role switch
    {
        Viewer => 0,
        Operator => 1,
        Admin => 2,
        _ => -1,
    };

    public static bool Satisfies(string? role, string required) => Rank(role) >= 0 && Rank(role) >= Rank(required);
}
