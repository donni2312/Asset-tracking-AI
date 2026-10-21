namespace AssetIQ.Api.Data;

// Entities mirror the existing PostgreSQL tables (defined by lib/db/src/schema). Column names are the
// snake_case form of the property names (see AssetIqDbContext); timestamps are UTC values stored in
// `timestamp without time zone` columns. Default values match the column defaults.

/// <summary>Strategic Business Unit (`sbus`).</summary>
public sealed class Sbu
{
    public int Id { get; set; }
    /// <summary>TSAS | BAK | AGRI | PROP | TSF</summary>
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string? HeadOffice { get; set; }
    /// <summary>UI badge colour (hex).</summary>
    public string Color { get; set; } = "#6B7280";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>`locations`</summary>
public sealed class Location
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    /// <summary>office | gate | warehouse | meeting_room | lab | reception | server_room</summary>
    public string Type { get; set; } = "";
    public string Building { get; set; } = "";
    public string? Floor { get; set; }
    public string? Description { get; set; }
    public int? SbuId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Sbu? Sbu { get; set; }
}

/// <summary>`rfid_readers`</summary>
public sealed class RfidReader
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int LocationId { get; set; }
    /// <summary>active | inactive</summary>
    public string Status { get; set; } = "active";
    public DateTime? LastReadAt { get; set; }
    public DateTime CreatedAt { get; set; }

    public Location Location { get; set; } = null!;
}

/// <summary>`assets`</summary>
public sealed class Asset
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public string SerialNumber { get; set; } = "";
    public string RfidTag { get; set; } = "";
    /// <summary>active | maintenance | retired | missing</summary>
    public string Status { get; set; } = "active";
    public string? AssignedTo { get; set; }
    public int? CurrentLocationId { get; set; }
    public int? SbuId { get; set; }
    /// <summary>ISO date string (YYYY-MM-DD) — the column is `text`.</summary>
    public string? PurchaseDate { get; set; }
    /// <summary>USD value.</summary>
    public decimal? PurchaseValue { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Location? CurrentLocation { get; set; }
    public Sbu? Sbu { get; set; }
}

/// <summary>`movements` — one row per RFID read, manual move or gate-pass exit.</summary>
public sealed class Movement
{
    public int Id { get; set; }
    public int AssetId { get; set; }
    public int? FromLocationId { get; set; }
    /// <summary>null = off-premises (the asset left through a gate under an approved gate pass).</summary>
    public int? ToLocationId { get; set; }
    /// <summary>rfid | manual | gate_pass</summary>
    public string TriggeredBy { get; set; } = "manual";
    public int? RfidReaderId { get; set; }
    /// <summary>Gate pass that authorised this movement, or that was raised because of it.</summary>
    public int? GatePassId { get; set; }
    public string? MovedBy { get; set; }
    public string? Notes { get; set; }
    public DateTime Timestamp { get; set; }

    public Asset Asset { get; set; } = null!;
    public Location? FromLocation { get; set; }
    public Location? ToLocation { get; set; }
    public RfidReader? RfidReader { get; set; }
    public GatePass? GatePass { get; set; }
}

/// <summary>`gate_passes`</summary>
public sealed class GatePass
{
    public int Id { get; set; }
    public int AssetId { get; set; }
    public string RequestedBy { get; set; } = "";
    /// <summary>The person who decided (approved OR rejected) the pass.</summary>
    public string? ApprovedBy { get; set; }
    /// <summary>pending | approved | rejected</summary>
    public string Status { get; set; } = "pending";
    public string Reason { get; set; } = "";
    public string? Destination { get; set; }
    /// <summary>ISO date string (YYYY-MM-DD); an approved pass is valid until this date.</summary>
    public string? ReturnDate { get; set; }
    public string? DecisionNote { get; set; }
    /// <summary>manual | rfid_gate</summary>
    public string Source { get; set; } = "manual";
    /// <summary>Gate where the asset was detected (rfid_gate passes).</summary>
    public int? GateLocationId { get; set; }
    public DateTime? DecidedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Asset Asset { get; set; } = null!;
    public Location? GateLocation { get; set; }
}

/// <summary>`ai_insights`</summary>
public sealed class AiInsight
{
    public int Id { get; set; }
    /// <summary>anomaly | idle_asset | unauthorized_movement | missing_asset | maintenance_due | unusual_pattern</summary>
    public string Type { get; set; } = "";
    public string Message { get; set; } = "";
    public int? AssetId { get; set; }
    /// <summary>Set for gate-pass related alerts so approving the pass can resolve them.</summary>
    public int? GatePassId { get; set; }
    /// <summary>info | warning | critical</summary>
    public string Severity { get; set; } = "info";
    public bool Dismissed { get; set; }
    public DateTime CreatedAt { get; set; }

    public Asset? Asset { get; set; }
    public GatePass? GatePass { get; set; }
}

/// <summary>
/// `asset_lifecycle_events` — business-meaningful state transitions beyond raw RFID reads:
/// deployment, return_to_it, repair_sent, repair_returned, sbu_transfer, note.
/// </summary>
public sealed class AssetLifecycleEvent
{
    public int Id { get; set; }
    public int AssetId { get; set; }
    public string EventType { get; set; } = "";
    public string PerformedBy { get; set; } = "";
    public int? FromSbuId { get; set; }
    public int? ToSbuId { get; set; }
    public string? AssignedTo { get; set; }
    public string? Vendor { get; set; }
    public string? TicketRef { get; set; }
    public string? ConditionBefore { get; set; }
    public string? ConditionAfter { get; set; }
    public string? Notes { get; set; }
    public DateTime Timestamp { get; set; }

    public Asset Asset { get; set; } = null!;
    public Sbu? FromSbu { get; set; }
    public Sbu? ToSbu { get; set; }
}

/// <summary>`users` — dashboard accounts. Only the scrypt password hash is stored.</summary>
public sealed class UserAccount
{
    public int Id { get; set; }
    public string Username { get; set; } = "";
    /// <summary>Display name, recorded on decisions.</summary>
    public string Name { get; set; } = "";
    /// <summary>viewer | operator | admin</summary>
    public string Role { get; set; } = "viewer";
    public string PasswordHash { get; set; } = "";
    /// <summary>Disabled accounts cannot sign in.</summary>
    public bool Active { get; set; } = true;
    public DateTime? LastLoginAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
