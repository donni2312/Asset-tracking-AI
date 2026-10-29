using System.Text.Json.Serialization;

namespace AssetIQ.Api.Contracts;

// Response bodies, in the shapes of lib/api-spec/openapi.yaml (the dashboard's generated client is
// built from it). Property names serialize as camelCase; nulls are always written, never omitted,
// unless a property says otherwise.

public sealed record HealthStatusDto(string Status);

// ── Auth ──────────────────────────────────────────────────────────────────────

public sealed record UserDto
{
    public required int Id { get; init; }
    public required string Username { get; init; }
    public required string Name { get; init; }
    public required string Role { get; init; }
    /// <summary>"Group Finance Executive", "SBU IT Admin / Technician", …</summary>
    public required string RoleName { get; init; }
    /// <summary>The SBU an SBU role acts on; null for Group roles.</summary>
    public int? SbuId { get; init; }
    public string? SbuCode { get; init; }
    /// <summary>"All SBUs", "BAK Logistics (IT assets)", …: the scope shown on the sidebar.</summary>
    public required string Scope { get; init; }
    /// <summary>The employee the account belongs to (HOD, Employee) and their department.</summary>
    public int? EmployeeId { get; init; }
    public string? EmployeeName { get; init; }
    public int? DepartmentId { get; init; }
    public string? DepartmentName { get; init; }
    public DateTime? AccessExpiresAt { get; init; }
    public DateTime? LastLoginAt { get; init; }
}

public sealed record LoginResultDto(string Token, DateTime ExpiresAt, UserDto User);

// ── SBUs ──────────────────────────────────────────────────────────────────────

public sealed record SbuDto
{
    public required int Id { get; init; }
    public required string Code { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public string? HeadOffice { get; init; }
    public required string Color { get; init; }
    public required DateTime CreatedAt { get; init; }
    public required DateTime UpdatedAt { get; init; }
    public int AssetCount { get; init; }
    public int LocationCount { get; init; }
}

// ── Assets ────────────────────────────────────────────────────────────────────

public sealed record AssetDto
{
    public required int Id { get; init; }
    /// <summary>The generated description (AU-01).</summary>
    public required string Name { get; init; }
    /// <summary>The sub-category (Laptop, Desktop, …).</summary>
    public required string Category { get; init; }
    public required int CategoryId { get; init; }
    public required string CategoryName { get; init; }
    public string? SubCategory { get; init; }
    /// <summary>Null when the asset is recorded as "No serial".</summary>
    public string? SerialNumber { get; init; }
    public string? NoSerialReason { get; init; }
    public string? NoSerialStatus { get; init; }
    public string? MacAddress { get; init; }
    /// <summary>Null until tagged.</summary>
    public string? RfidTag { get; init; }
    /// <summary>Business Central fixed-asset number; null while pending (AU-03).</summary>
    public string? AssetNumber { get; init; }
    public string? PreviousAssetNumber { get; init; }
    public required string Status { get; init; }
    public string? Condition { get; init; }
    public string? AssignedTo { get; init; }
    public int? CurrentLocationId { get; init; }
    public string? CurrentLocationName { get; init; }
    public int? SbuId { get; init; }
    public string? SbuName { get; init; }
    public string? SbuCode { get; init; }
    /// <summary>YYYY-MM-DD</summary>
    public string? PurchaseDate { get; init; }
    public decimal? PurchaseValue { get; init; }
    public decimal? OriginalCost { get; init; }
    public string? Currency { get; init; }
    public decimal? ExchangeRate { get; init; }
    public decimal? CostUsd { get; init; }
    public DateTime? CapitalisedAt { get; init; }
    public string? CapitalisedBy { get; init; }
    /// <summary>Null (like every cost and value field) for roles that may not see financial data.</summary>
    public decimal? ResidualValue { get; init; }
    public string? Supplier { get; init; }
    public string? InvoiceNo { get; init; }
    public string? PoNo { get; init; }
    public string? InUseDate { get; init; }
    public string? WarrantyEnds { get; init; }
    public string? DisposalDate { get; init; }
    /// <summary>GI-07 incident details while lost / stolen.</summary>
    public string? LostDate { get; init; }
    public string? LostReportedBy { get; init; }
    public string? LostNotes { get; init; }
    public string? Notes { get; init; }
    public required DateTime CreatedAt { get; init; }
    public required DateTime UpdatedAt { get; init; }
}

/// <summary>One entry of an asset's combined history: a movement ("mov-12") or a lifecycle event ("lc-7").</summary>
public sealed record AssetHistoryEventDto
{
    public required string Id { get; init; }
    /// <summary>movement | lifecycle</summary>
    public required string Kind { get; init; }
    public required DateTime Timestamp { get; init; }
    // movement fields
    public string? FromLocationName { get; init; }
    public string? ToLocationName { get; init; }
    public string? TriggeredBy { get; init; }
    public int? GatePassId { get; init; }
    public string? MovedBy { get; init; }
    // lifecycle fields
    public string? EventType { get; init; }
    public string? PerformedBy { get; init; }
    public string? FromSbuName { get; init; }
    public string? ToSbuName { get; init; }
    public string? AssignedTo { get; init; }
    public string? Vendor { get; init; }
    public string? TicketRef { get; init; }
    public string? ConditionBefore { get; init; }
    public string? ConditionAfter { get; init; }
    public string? Notes { get; init; }
}

public sealed record LifecycleEventDto
{
    public required int Id { get; init; }
    public required int AssetId { get; init; }
    public required string AssetName { get; init; }
    public required string RfidTag { get; init; }
    public required string EventType { get; init; }
    public required string PerformedBy { get; init; }
    public int? FromSbuId { get; init; }
    public string? FromSbuName { get; init; }
    public int? ToSbuId { get; init; }
    public string? ToSbuName { get; init; }
    public string? AssignedTo { get; init; }
    public string? Vendor { get; init; }
    public string? TicketRef { get; init; }
    public string? ConditionBefore { get; init; }
    public string? ConditionAfter { get; init; }
    public string? Notes { get; init; }
    public required DateTime Timestamp { get; init; }
}

// ── Locations ─────────────────────────────────────────────────────────────────

public sealed record LocationDto
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public required string Type { get; init; }
    public required string Building { get; init; }
    public string? Floor { get; init; }
    public string? Description { get; init; }
    public int? SbuId { get; init; }
    public string? SbuName { get; init; }
    public string? SbuCode { get; init; }
    public required DateTime CreatedAt { get; init; }
    public int AssetCount { get; init; }
}

// ── Movements ─────────────────────────────────────────────────────────────────

public record MovementDto
{
    public required int Id { get; init; }
    public required int AssetId { get; init; }
    public required string AssetName { get; init; }
    public required string RfidTag { get; init; }
    public int? FromLocationId { get; init; }
    public string? FromLocationName { get; init; }
    public int? ToLocationId { get; init; }
    public string? ToLocationName { get; init; }
    /// <summary>rfid | manual | gate_pass</summary>
    public required string TriggeredBy { get; init; }
    public int? RfidReaderId { get; init; }
    public int? GatePassId { get; init; }
    public string? MovedBy { get; init; }
    public string? Notes { get; init; }
    public required DateTime Timestamp { get; init; }
}

/// <summary>The movement recorded by an RFID scan, with the SBU of the location that read the tag.</summary>
public sealed record ScanMovementDto : MovementDto
{
    public int? SbuId { get; init; }
    public string? SbuName { get; init; }
}

// ── Gate passes ───────────────────────────────────────────────────────────────

public sealed record GatePassDto
{
    public required int Id { get; init; }
    public required int AssetId { get; init; }
    public required string AssetName { get; init; }
    public required string RfidTag { get; init; }
    public int? SbuId { get; init; }
    public string? SbuName { get; init; }
    public required string RequestedBy { get; init; }
    public string? ApprovedBy { get; init; }
    public required string Status { get; init; }
    public required string Reason { get; init; }
    public string? Destination { get; init; }
    /// <summary>YYYY-MM-DD</summary>
    public string? ReturnDate { get; init; }
    public string? DecisionNote { get; init; }
    public required string Source { get; init; }
    public int? GateLocationId { get; init; }
    public string? GateLocationName { get; init; }
    public DateTime? DecidedAt { get; init; }
    /// <summary>When the asset left the premises under this pass (latest gate_pass movement); null = not used yet.</summary>
    public DateTime? ExitedAt { get; init; }
    public required DateTime CreatedAt { get; init; }
    public required DateTime UpdatedAt { get; init; }
}

// ── RFID ──────────────────────────────────────────────────────────────────────

public sealed record RfidReaderDto
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public required int LocationId { get; init; }
    public required string LocationName { get; init; }
    public int? SbuId { get; init; }
    public string? SbuCode { get; init; }
    public string? SbuName { get; init; }
    public required string Status { get; init; }
    public DateTime? LastReadAt { get; init; }
}

public sealed record GateCheckDto
{
    public required bool IsGate { get; init; }
    /// <summary>entry | exit | internal</summary>
    public required string Direction { get; init; }
    /// <summary>not_gate | entry | authorized | pending_created | pending_existing</summary>
    public required string Outcome { get; init; }
    public required bool Authorized { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public GatePassDto? GatePass { get; init; }
}

public sealed record RfidScanResultDto(ScanMovementDto Movement, GateCheckDto GateCheck, IReadOnlyList<InsightDto> Insights);

// ── Insights ──────────────────────────────────────────────────────────────────

public sealed record InsightDto
{
    public required int Id { get; init; }
    public required string Type { get; init; }
    public required string Message { get; init; }
    public int? AssetId { get; init; }
    public string? AssetName { get; init; }
    public string? SbuCode { get; init; }
    public int? GatePassId { get; init; }
    public required string Severity { get; init; }
    public required bool Dismissed { get; init; }
    public required DateTime CreatedAt { get; init; }
}

public sealed record AiQueryResultDto
{
    public required string Question { get; init; }
    public required string Answer { get; init; }
    public required IReadOnlyList<AssetDto> RelatedAssets { get; init; }
    public required IReadOnlyList<GatePassDto> RelatedGatePasses { get; init; }
    /// <summary>The intent the question was mapped to, e.g. locate_asset.</summary>
    public required string Intent { get; init; }
    /// <summary>rules | openai</summary>
    public required string Source { get; init; }
    public required DateTime Timestamp { get; init; }
}

// ── Dashboard ─────────────────────────────────────────────────────────────────

public sealed record SbuBreakdownDto
{
    public required int SbuId { get; init; }
    public required string SbuCode { get; init; }
    public required string SbuName { get; init; }
    public required string SbuColor { get; init; }
    public required int TotalAssets { get; init; }
    public required int ActiveAssets { get; init; }
    public required int MissingAssets { get; init; }
    public required int MaintenanceAssets { get; init; }
}

public sealed record DashboardSummaryDto
{
    public required int TotalAssets { get; init; }
    public required int ActiveAssets { get; init; }
    public required int MissingAssets { get; init; }
    public required int MaintenanceAssets { get; init; }
    public required int PendingGatePasses { get; init; }
    public required int TodayMovements { get; init; }
    public required int TotalLocations { get; init; }
    public required int UnacknowledgedInsights { get; init; }
    public required IReadOnlyList<SbuBreakdownDto> SbuBreakdown { get; init; }
}

public sealed record ActivityItemDto
{
    public required string Id { get; init; }
    /// <summary>movement | gate_pass_created | gate_pass_approved | gate_pass_rejected | rfid_scan | insight | lifecycle_event</summary>
    public required string Type { get; init; }
    public required string Description { get; init; }
    public int? AssetId { get; init; }
    public string? AssetName { get; init; }
    public string? SbuCode { get; init; }
    /// <summary>info | warning | critical | null</summary>
    public string? Severity { get; init; }
    public required DateTime Timestamp { get; init; }
}

public sealed record LocationBreakdownDto
{
    public required int LocationId { get; init; }
    public required string LocationName { get; init; }
    public required string LocationType { get; init; }
    public string? SbuCode { get; init; }
    public required int AssetCount { get; init; }
}

public sealed record MovementTrendPointDto
{
    /// <summary>Calendar day (YYYY-MM-DD) in the organisation time zone.</summary>
    public required string Date { get; init; }
    public int Rfid { get; set; }
    public int Manual { get; set; }
    public int GatePass { get; set; }
    public int Total { get; set; }
}

public sealed record GateActivityItemDto
{
    public required int ReaderId { get; init; }
    public required string ReaderName { get; init; }
    public required string LocationName { get; init; }
    public required string SbuCode { get; init; }
    public required string SbuName { get; init; }
    public required int AssetId { get; init; }
    public required string AssetName { get; init; }
    public required string RfidTag { get; init; }
    /// <summary>entry | exit | internal</summary>
    public required string Direction { get; init; }
    public required DateTime Timestamp { get; init; }
}

// ── Business Central ──────────────────────────────────────────────────────────

public sealed record BcSyncStatusDto
{
    public required bool Connected { get; init; }
    /// <summary>manual | automatic — the MVP syncs manually only.</summary>
    public required string Mode { get; init; }
    /// <summary>idle | syncing | success | error | not_configured</summary>
    public required string Status { get; init; }
    public DateTime? LastSyncAt { get; init; }
    public required int SyncedAssets { get; init; }
    public string? ErrorMessage { get; init; }
    /// <summary>Always null in the MVP (no scheduled syncs).</summary>
    public DateTime? NextSyncAt { get; init; }
    /// <summary>Movements and gate-pass decisions recorded since the last successful sync.</summary>
    public required int PendingChanges { get; init; }
}
