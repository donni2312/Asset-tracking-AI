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
    /// <summary>One of <see cref="Domain.AssetStatuses.All"/>; changed only through <see cref="Domain.AssetStatusMachine"/>.</summary>
    public string Status { get; set; } = Domain.AssetStatuses.PendingAssetNumber;
    /// <summary>GI-07: when lost / stolen (YYYY-MM-DD), who reported it, and what happened.</summary>
    public string? LostDate { get; set; }
    public string? LostReportedBy { get; set; }
    public string? LostNotes { get; set; }
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
    /// <summary>SF-01 (Phase 4d), set by SBU Finance.</summary>
    public string? CostCentre { get; set; }
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
    /// <summary>LM-04: the local day (YYYY-MM-DD) the last daily digest went out.</summary>
    public string? LastDigestOn { get; set; }
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

// ── Phase 4: approvals engine, notifications, repairs, disposals ─────────────

/// <summary>`approval_requests`: one thing to decide (repair, disposal, transfer, gate pass).</summary>
public sealed class ApprovalRequest
{
    public int Id { get; set; }
    /// <summary>repair | disposal | transfer | gate_pass | equipment</summary>
    public string Kind { get; set; } = "";
    /// <summary>Null for an equipment request.</summary>
    public int? AssetId { get; set; }
    public int SbuId { get; set; }
    public int? SubjectId { get; set; }
    /// <summary>pending | approved | rejected</summary>
    public string Status { get; set; } = "pending";
    public string Summary { get; set; } = "";
    /// <summary>JSON object with the kind's details (transfer target, reason …).</summary>
    public string Details { get; set; } = "{}";
    public string RequestedBy { get; set; } = "";
    public int? RequestedByUserId { get; set; }
    public DateTime? DecidedAt { get; set; }
    public DateTime CreatedAt { get; set; }

    public Asset? Asset { get; set; }
    public List<ApprovalStep> Steps { get; set; } = [];
}

/// <summary>`approval_steps`: one approver's part of a request.</summary>
public sealed class ApprovalStep
{
    public int Id { get; set; }
    public int RequestId { get; set; }
    public int StepNo { get; set; }
    public string Role { get; set; } = "";
    public int? SbuId { get; set; }
    public int? DepartmentId { get; set; }
    public string Label { get; set; } = "";
    /// <summary>waiting (an earlier step is open) | pending | approved | rejected</summary>
    public string Status { get; set; } = "pending";
    public string? DecidedBy { get; set; }
    public int? DecidedByUserId { get; set; }
    public string? OnBehalfOf { get; set; }
    public DateTime? DecidedAt { get; set; }
    public string? Comment { get; set; }
    public DateTime? RemindedAt { get; set; }
    public string? EscalatedToRole { get; set; }
    public DateTime? EscalatedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>`delegations`: LM-03, an approver on leave and who decides for them.</summary>
public sealed class Delegation
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int? DelegateUserId { get; set; }
    public DateTime StartsAt { get; set; }
    public DateTime EndsAt { get; set; }
    public string? Reason { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class Notification
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Kind { get; set; } = "";
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public string? Link { get; set; }
    public DateTime? ReadAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class OutboundEmail
{
    public int Id { get; set; }
    public int? UserId { get; set; }
    public string? ToAddress { get; set; }
    public string Subject { get; set; } = "";
    public string Body { get; set; } = "";
    public string Status { get; set; } = "logged";
    public DateTime CreatedAt { get; set; }
}

/// <summary>`asset_repairs` (SI-05). The cost is Finance's.</summary>
public sealed class AssetRepair
{
    public int Id { get; set; }
    public int AssetId { get; set; }
    public string Fault { get; set; } = "";
    public string? Action { get; set; }
    public string? Parts { get; set; }
    public string? Vendor { get; set; }
    public int? DowntimeDays { get; set; }
    public decimal? Cost { get; set; }
    /// <summary>requested | approved | rejected | completed</summary>
    public string Status { get; set; } = "requested";
    public string PreviousStatus { get; set; } = "";
    public string RequestedBy { get; set; } = "";
    public DateTime? CompletedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>`asset_disposals` (SI-08, SF-04, GF-04, GI-10).</summary>
public sealed class AssetDisposal
{
    public int Id { get; set; }
    public int AssetId { get; set; }
    public string Reason { get; set; } = "";
    public string? Condition { get; set; }
    public string RecommendedBy { get; set; } = "";
    public string PreviousStatus { get; set; } = "";
    /// <summary>sale | scrap | donation | write_off</summary>
    public string? Method { get; set; }
    public string? DisposalDate { get; set; }
    public decimal? Proceeds { get; set; }
    public string? Buyer { get; set; }
    public decimal? NbvAtDisposal { get; set; }
    public decimal? GainLoss { get; set; }
    public string? DataWipeNote { get; set; }
    /// <summary>pending | completed | rejected</summary>
    public string Status { get; set; } = "pending";
    public DateTime CreatedAt { get; set; }
}

// ── Phase 4b: purchase orders, receiving, equipment requests, acknowledgements, exit clearance ──

public sealed class PurchaseOrder
{
    public int Id { get; set; }
    public int SbuId { get; set; }
    public string PoNo { get; set; } = "";
    public string Supplier { get; set; } = "";
    /// <summary>open | partly_received | received | closed</summary>
    public string Status { get; set; } = "open";
    public string CreatedBy { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public List<PurchaseOrderLine> Lines { get; set; } = [];
}

public sealed class PurchaseOrderLine
{
    public int Id { get; set; }
    public int PurchaseOrderId { get; set; }
    public int CategoryId { get; set; }
    public string Description { get; set; } = "";
    public int Quantity { get; set; }
    public decimal? UnitCost { get; set; }
    public int ReceivedQuantity { get; set; }
}

public sealed class GoodsReceipt
{
    public int Id { get; set; }
    public int PurchaseOrderId { get; set; }
    public string ReceivedBy { get; set; } = "";
    public string? Notes { get; set; }
    public bool Flagged { get; set; }
    public string? FlagReason { get; set; }
    public DateTime ReceivedAt { get; set; }
    public List<GoodsReceiptLine> Lines { get; set; } = [];
}

public sealed class GoodsReceiptLine
{
    public int Id { get; set; }
    public int ReceiptId { get; set; }
    public int PurchaseOrderLineId { get; set; }
    public int QuantityReceived { get; set; }
    public int QuantityDamaged { get; set; }
    /// <summary>JSON array of serial numbers counted at the door.</summary>
    public string Serials { get; set; } = "[]";
    public string? Condition { get; set; }
    public int Registered { get; set; }
}

public sealed class EquipmentRequest
{
    public int Id { get; set; }
    public int SbuId { get; set; }
    public int? EmployeeId { get; set; }
    public int? DepartmentId { get; set; }
    public int CategoryId { get; set; }
    public string ItemType { get; set; } = "";
    public int Quantity { get; set; } = 1;
    public int IssuedQuantity { get; set; }
    public string Reason { get; set; } = "";
    public string Urgency { get; set; } = "normal";
    /// <summary>pending | approved | rejected | fulfilled</summary>
    public string Status { get; set; } = "pending";
    /// <summary>request | onboarding</summary>
    public string Source { get; set; } = "request";
    public string RequestedBy { get; set; } = "";
    public int? RequestedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class AssetAcknowledgement
{
    public int Id { get; set; }
    public int AssetId { get; set; }
    public int EmployeeId { get; set; }
    public int? EquipmentRequestId { get; set; }
    public string Token { get; set; } = "";
    public string IssuedBy { get; set; } = "";
    public DateTime IssuedAt { get; set; }
    public DateTime? AcknowledgedAt { get; set; }
    /// <summary>app | email</summary>
    public string? Method { get; set; }
}

public sealed class ExitClearance
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public string Reason { get; set; } = "";
    /// <summary>open | completed</summary>
    public string Status { get; set; } = "open";
    public string? ExceptionApprovedBy { get; set; }
    public string? ExceptionReason { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public List<ExitClearanceItem> Items { get; set; } = [];
}

public sealed class ExitClearanceItem
{
    public int Id { get; set; }
    public int ClearanceId { get; set; }
    public int AssetId { get; set; }
    /// <summary>outstanding | returned | damaged | missing</summary>
    public string Outcome { get; set; } = "outstanding";
    public string? RecordedBy { get; set; }
    public DateTime? RecordedAt { get; set; }
    public string? Notes { get; set; }
    /// <summary>HR-05: recovered | written_off | cost_recovery</summary>
    public string? RecoveryOutcome { get; set; }
    public string? RecoveryApprovedBy { get; set; }
    public string? RecoveryNote { get; set; }
    public string? RecoveryRecordedBy { get; set; }
    public DateTime? RecoveryRecordedAt { get; set; }
}

// ── Phase 4c: gate pass items and gate actions, verification ──────────────────

/// <summary>`gate_pass_items`: one item of a gate pass and its exit / return (SG-03).</summary>
public sealed class GatePassItem
{
    public int Id { get; set; }
    public int GatePassId { get; set; }
    public int AssetId { get; set; }
    public Asset Asset { get; set; } = null!;
    public DateTime? ExitedAt { get; set; }
    public int? ExitGateId { get; set; }
    public string? ExitOfficer { get; set; }
    public string? ExitNotes { get; set; }
    public DateTime? ReturnedAt { get; set; }
    public int? ReturnGateId { get; set; }
    public string? ReturnOfficer { get; set; }
    public string? ReturnCondition { get; set; }
    public string? ReturnNotes { get; set; }
}

/// <summary>`gate_actions`: SG-02, what the Gate Officer did about an alert.</summary>
public sealed class GateAction
{
    public int Id { get; set; }
    public int? InsightId { get; set; }
    public int AssetId { get; set; }
    public int? GateLocationId { get; set; }
    /// <summary>stopped | released_with_override | escalated</summary>
    public string Action { get; set; } = "";
    public string? Notes { get; set; }
    public string Officer { get; set; } = "";
    public DateTime At { get; set; }
}

public sealed class VerificationSession
{
    public int Id { get; set; }
    public int SbuId { get; set; }
    public int LocationId { get; set; }
    /// <summary>it | non_it | in_store | all</summary>
    public string Scope { get; set; } = "all";
    /// <summary>open | closed | reconciled</summary>
    public string Status { get; set; } = "open";
    public string StartedBy { get; set; } = "";
    public DateTime StartedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
}

public sealed class VerificationScan
{
    public int Id { get; set; }
    public int SessionId { get; set; }
    public string Code { get; set; } = "";
    public int? AssetId { get; set; }
    public string ScannedBy { get; set; } = "";
    public DateTime ScannedAt { get; set; }
}

public sealed class VerificationResult
{
    public int Id { get; set; }
    public int SessionId { get; set; }
    public int? AssetId { get; set; }
    public string? Code { get; set; }
    /// <summary>found | missing | unrecorded</summary>
    public string Result { get; set; } = "";
    /// <summary>adjust | write_off | investigate | register</summary>
    public string? Resolution { get; set; }
    public string? Comment { get; set; }
    public string? ResolvedBy { get; set; }
    public DateTime? ResolvedAt { get; set; }
}

// ── Phase 4d: fleet (AM-02 … AM-04) ───────────────────────────────────────────

public sealed class FleetDetail
{
    public int Id { get; set; }
    public int AssetId { get; set; }
    /// <summary>active | in_service | off_road</summary>
    public string FleetStatus { get; set; } = "active";
    public int? DriverEmployeeId { get; set; }
    public string? DriverName { get; set; }
    public string UpdatedBy { get; set; } = "";
    public DateTime UpdatedAt { get; set; }
}

public sealed class VehicleDocument
{
    public int Id { get; set; }
    public int AssetId { get; set; }
    /// <summary>licence | insurance | fitness | zinara</summary>
    public string Kind { get; set; } = "";
    public string? Reference { get; set; }
    /// <summary>YYYY-MM-DD</summary>
    public string ExpiresOn { get; set; } = "";
    public DateTime? Reminded30At { get; set; }
    public DateTime? Reminded7At { get; set; }
    public DateTime? ExpiredNotifiedAt { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

public sealed class ServiceSchedule
{
    public int Id { get; set; }
    public int AssetId { get; set; }
    /// <summary>km (vehicles) or hours run (generators).</summary>
    public int? IntervalReading { get; set; }
    public int? IntervalDays { get; set; }
    public string StartDate { get; set; } = "";
    public int? StartReading { get; set; }
    public string? OverdueNotifiedFor { get; set; }
    public string UpdatedBy { get; set; } = "";
    public DateTime UpdatedAt { get; set; }
}

public sealed class ServiceLog
{
    public int Id { get; set; }
    public int AssetId { get; set; }
    public string ServiceDate { get; set; } = "";
    public int? Reading { get; set; }
    public string Garage { get; set; } = "";
    public string WorkDone { get; set; } = "";
    public decimal? Cost { get; set; }
    public string? Currency { get; set; }
    public string? CostBy { get; set; }
    public string LoggedBy { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

// ── Phase 5a: revaluations, CAPEX budgets ─────────────────────────────────────

public sealed class AssetRevaluation
{
    public int Id { get; set; }
    public int AssetId { get; set; }
    /// <summary>revaluation | impairment</summary>
    public string Kind { get; set; } = "";
    /// <summary>YYYY-MM: takes effect from this month.</summary>
    public string Period { get; set; } = "";
    /// <summary>USD change to the carrying amount (negative = down).</summary>
    public decimal Amount { get; set; }
    public decimal NbvBefore { get; set; }
    public decimal NbvAfter { get; set; }
    public int? RevisedLifeMonths { get; set; }
    public string Reason { get; set; } = "";
    public int AttachmentId { get; set; }
    public string RecordedBy { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

public sealed class CapexBudget
{
    public int Id { get; set; }
    public int SbuId { get; set; }
    /// <summary>The year the financial year ends in (FY2027 = Nov 2026 – Oct 2027).</summary>
    public int FinancialYear { get; set; }
    public int CategoryId { get; set; }
    public decimal Amount { get; set; }
    public int? AlertedPercent { get; set; }
    public string UpdatedBy { get; set; } = "";
    public DateTime UpdatedAt { get; set; }
}

// ── Phase 5b: security actions, visitor equipment, gate incidents ──────────────

public sealed class SecurityAction
{
    public int Id { get; set; }
    public int AssetId { get; set; }
    /// <summary>remote_wipe | account_disable | police_report</summary>
    public string Action { get; set; } = "";
    public string? Reference { get; set; }
    public string? Notes { get; set; }
    public string DoneBy { get; set; } = "";
    public DateTime DoneAt { get; set; }
}

public sealed class VisitorItem
{
    public int Id { get; set; }
    public int SbuId { get; set; }
    public int GateLocationId { get; set; }
    public string OwnerName { get; set; } = "";
    public string? OwnerCompany { get; set; }
    public string Item { get; set; } = "";
    public string? SerialNumber { get; set; }
    public byte[]? Photo { get; set; }
    public string? PhotoContentType { get; set; }
    public string CheckedInBy { get; set; } = "";
    public DateTime CheckedInAt { get; set; }
    public string? CheckedOutBy { get; set; }
    public DateTime? CheckedOutAt { get; set; }
    public int? CheckedOutGateId { get; set; }
}

public sealed class GateIncident
{
    public int Id { get; set; }
    public int SbuId { get; set; }
    public int? GateLocationId { get; set; }
    /// <summary>forged_pass | unknown_tag | refused_search | other</summary>
    public string Kind { get; set; } = "";
    public int? AssetId { get; set; }
    public int? GatePassId { get; set; }
    public string? Code { get; set; }
    public string Notes { get; set; } = "";
    public byte[]? Photo { get; set; }
    public string? PhotoContentType { get; set; }
    public string Officer { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

