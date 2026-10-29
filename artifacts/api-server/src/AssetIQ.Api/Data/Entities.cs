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
    /// <summary>The description (AU-01), generated from the category template; never typed by a user.</summary>
    public string Name { get; set; } = "";
    /// <summary>The sub-category (Laptop, Desktop, …): the fine-grained type the RFID, AI and CSV code reads.</summary>
    public string Category { get; set; } = "";
    public int CategoryId { get; set; }
    public string? SubCategory { get; set; }
    /// <summary>Group-unique identifier: the template's identifier field. Null only with a "No serial" reason.</summary>
    public string? SerialNumber { get; set; }
    public string? NoSerialReason { get; set; }
    /// <summary>pending | approved | rejected</summary>
    public string? NoSerialStatus { get; set; }
    public string? NoSerialDecidedBy { get; set; }
    public DateTime? NoSerialDecidedAt { get; set; }
    public string? MacAddress { get; set; }
    /// <summary>Null until tagged; an asset cannot be tagged before it has an asset number.</summary>
    public string? RfidTag { get; set; }
    /// <summary>Business Central fixed-asset number; null while pending. A trigger forbids changing it once set.</summary>
    public string? AssetNumber { get; set; }
    public string? PreviousAssetNumber { get; set; }
    /// <summary>pending_asset_number | active | maintenance | retired | missing</summary>
    public string Status { get; set; } = "active";
    /// <summary>new | good | fair | poor | damaged</summary>
    public string? Condition { get; set; }
    /// <summary>The custodian (the employee's name when <see cref="CustodianEmployeeId"/> is set).</summary>
    public string? AssignedTo { get; set; }
    /// <summary>The employee holding the asset (HR toolbox, Employee scope).</summary>
    public int? CustodianEmployeeId { get; set; }
    /// <summary>The department holding the asset (HOD scope, LM-01).</summary>
    public int? DepartmentId { get; set; }
    public int? CurrentLocationId { get; set; }
    public int? SbuId { get; set; }
    public decimal? OriginalCost { get; set; }
    /// <summary>ISO 4217 code.</summary>
    public string? Currency { get; set; }
    /// <summary>Currency → USD.</summary>
    public decimal? ExchangeRate { get; set; }
    /// <summary>OriginalCost × ExchangeRate, fixed when the asset is capitalised.</summary>
    public decimal? CostUsd { get; set; }
    public DateTime? CapitalisedAt { get; set; }
    public string? CapitalisedBy { get; set; }
    public decimal ResidualValue { get; set; }
    public string? Supplier { get; set; }
    public string? InvoiceNo { get; set; }
    public string? PoNo { get; set; }
    /// <summary>ISO date string (YYYY-MM-DD) — the column is `text`.</summary>
    public string? PurchaseDate { get; set; }
    /// <summary>YYYY-MM-DD; depreciation starts in this month.</summary>
    public string? InUseDate { get; set; }
    public string? WarrantyEnds { get; set; }
    /// <summary>YYYY-MM-DD; no depreciation from this month on.</summary>
    public string? DisposalDate { get; set; }
    /// <summary>Legacy USD value, kept equal to the original cost.</summary>
    public decimal? PurchaseValue { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Location? CurrentLocation { get; set; }
    public Sbu? Sbu { get; set; }
    public AssetCategory AssetCategory { get; set; } = null!;
    public List<AssetAttributeValue> Attributes { get; set; } = [];
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
    /// <summary>One of <see cref="Domain.Roles.All"/>.</summary>
    public string Role { get; set; } = Domain.Roles.Employee;
    /// <summary>The SBU an SBU role acts on.</summary>
    public int? SbuId { get; set; }
    /// <summary>The employee the account belongs to (required for the HOD and Employee roles).</summary>
    public int? EmployeeId { get; set; }
    /// <summary>Auditors: refused after this instant (set by Group Finance).</summary>
    public DateTime? AccessExpiresAt { get; set; }
    public string PasswordHash { get; set; } = "";
    /// <summary>Disabled accounts cannot sign in.</summary>
    public bool Active { get; set; } = true;
    public DateTime? LastLoginAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>`asset_categories`: a category, its template (AU-01) and its depreciation policy (GF-03).</summary>
public sealed class AssetCategory
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsIt { get; set; }
    /// <summary>JSON array of sub-category names.</summary>
    public string SubCategories { get; set; } = "[]";
    public string DescriptionPattern { get; set; } = "";
    public string? IdentifierFieldKey { get; set; }
    /// <summary>straight_line | reducing_balance</summary>
    public string DepreciationMethod { get; set; } = "";
    public int? UsefulLifeMonths { get; set; }
    public decimal? AnnualRate { get; set; }
    /// <summary>Residual value as a % of cost, proposed at capitalisation (0 by default).</summary>
    public decimal DefaultResidualPercent { get; set; }
    public bool Active { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public List<CategoryTemplateField> Fields { get; set; } = [];
}

/// <summary>`category_template_fields`</summary>
public sealed class CategoryTemplateField
{
    public int Id { get; set; }
    public int CategoryId { get; set; }
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    /// <summary>text | number | integer | date | list</summary>
    public string Type { get; set; } = "text";
    public string? Unit { get; set; }
    public bool Required { get; set; }
    /// <summary>JSON array of allowed values (list fields).</summary>
    public string? AllowedValues { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>`asset_attribute_values`: one typed template value per asset.</summary>
public sealed class AssetAttributeValue
{
    public int Id { get; set; }
    public int AssetId { get; set; }
    public int FieldId { get; set; }
    public string ValueText { get; set; } = "";
    public decimal? ValueNumber { get; set; }

    public CategoryTemplateField Field { get; set; } = null!;
}

/// <summary>`group_settings`</summary>
public sealed class GroupSetting
{
    public string Key { get; set; } = "";
    /// <summary>JSON value.</summary>
    public string Value { get; set; } = "null";
    public string Description { get; set; } = "";
    /// <summary>group_finance | group_it</summary>
    public string EditableBy { get; set; } = "";
    public string? UpdatedBy { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>`asset_upgrades` (AU-07)</summary>
public sealed class AssetUpgrade
{
    public int Id { get; set; }
    public int AssetId { get; set; }
    /// <summary>YYYY-MM-DD</summary>
    public string UpgradeDate { get; set; } = "";
    public string Description { get; set; } = "";
    public string BeforeText { get; set; } = "";
    public string AfterText { get; set; } = "";
    /// <summary>JSON object: template field key → value after the upgrade.</summary>
    public string FieldChanges { get; set; } = "{}";
    /// <summary>USD: <see cref="InvoiceAmount"/> × <see cref="ExchangeRate"/>.</summary>
    public decimal Cost { get; set; }
    /// <summary>The invoice's currency (ISO 4217), as invoiced.</summary>
    public string Currency { get; set; } = "USD";
    /// <summary>The invoice amount in <see cref="Currency"/>.</summary>
    public decimal? InvoiceAmount { get; set; }
    /// <summary><see cref="Currency"/> → USD.</summary>
    public decimal ExchangeRate { get; set; } = 1m;
    public string? Supplier { get; set; }
    public string? InvoiceNo { get; set; }
    public string LoggedBy { get; set; } = "";
    public string LoggedByRole { get; set; } = "";
    /// <summary>pending | capitalised | expensed | rejected</summary>
    public string Status { get; set; } = "pending";
    public string? DecidedBy { get; set; }
    public DateTime? DecidedAt { get; set; }
    public string? DecisionComment { get; set; }
    /// <summary>YYYY-MM</summary>
    public string? CapitalisedPeriod { get; set; }
    public int? RevisedLifeMonths { get; set; }
    public DateTime CreatedAt { get; set; }

    public Asset Asset { get; set; } = null!;
}

/// <summary>`depreciation_entries`: one row per asset per month (AU-05).</summary>
public sealed class DepreciationEntry
{
    public int Id { get; set; }
    public int AssetId { get; set; }
    /// <summary>YYYY-MM</summary>
    public string Period { get; set; } = "";
    public decimal OpeningCost { get; set; }
    public decimal OpeningAccumulated { get; set; }
    public decimal Charge { get; set; }
    public decimal Adjustment { get; set; }
    public decimal ClosingAccumulated { get; set; }
    public decimal ClosingNbv { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>`depreciation_locks`: a month SBU Finance has locked; it and every earlier month are final.</summary>
public sealed class DepreciationLock
{
    public int Id { get; set; }
    public int SbuId { get; set; }
    public string Period { get; set; } = "";
    public string LockedBy { get; set; } = "";
    public DateTime LockedAt { get; set; }
}

/// <summary>`audit_log`: append-only (a trigger rejects UPDATE and DELETE).</summary>
public sealed class AuditEntry
{
    public int Id { get; set; }
    public string Entity { get; set; } = "";
    public int? EntityId { get; set; }
    public int? AssetId { get; set; }
    public string Action { get; set; } = "";
    public string? Field { get; set; }
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public string? Reason { get; set; }
    public int? UserId { get; set; }
    public string UserName { get; set; } = "";
    public DateTime At { get; set; }
}

/// <summary>`access_log`: sign-ins and exports.</summary>
public sealed class AccessLogEntry
{
    public int Id { get; set; }
    public int? UserId { get; set; }
    public string Username { get; set; } = "";
    public string Role { get; set; } = "";
    /// <summary>login | login_refused | export</summary>
    public string Action { get; set; } = "";
    public string? Detail { get; set; }
    public DateTime At { get; set; }
}

/// <summary>`bc_number_requests`: a queued request for a Business Central fixed-asset number.</summary>
public sealed class BcNumberRequest
{
    public int Id { get; set; }
    public int AssetId { get; set; }
    /// <summary>queued | done</summary>
    public string Status { get; set; } = "queued";
    public int Attempts { get; set; }
    public DateTime NextAttemptAt { get; set; }
    public string? LastError { get; set; }
    public string? AssetNumber { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>`bc_mock_number_series`: counters of the mock Business Central.</summary>
public sealed class BcMockNumberSeries
{
    public int Id { get; set; }
    public string Prefix { get; set; } = "";
    public int LastNumber { get; set; }
}

/// <summary>`departments`: a department of one SBU (Phase 2).</summary>
public sealed class Department
{
    public int Id { get; set; }
    public int SbuId { get; set; }
    public string Name { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>`employees`: an employee of one SBU (Phase 2; the toolbox stories build on it in Phase 4).</summary>
public sealed class Employee
{
    public int Id { get; set; }
    public int SbuId { get; set; }
    public int? DepartmentId { get; set; }
    public string EmployeeNo { get; set; } = "";
    public string Name { get; set; } = "";
    public string? JobRole { get; set; }
    public int? LineManagerId { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public bool Active { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Department? Department { get; set; }
}

/// <summary>`asset_attachments`: an invoice, photo or document on an asset. Append-only (database trigger).</summary>
public sealed class AssetAttachment
{
    public int Id { get; set; }
    public int AssetId { get; set; }
    public int? UpgradeId { get; set; }
    /// <summary>invoice | photo | other</summary>
    public string Kind { get; set; } = "";
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public int SizeBytes { get; set; }
    public string Sha256 { get; set; } = "";
    public byte[] Content { get; set; } = [];
    public string UploadedBy { get; set; } = "";
    public string UploadedByRole { get; set; } = "";
    public DateTime UploadedAt { get; set; }

    public Asset Asset { get; set; } = null!;
}
