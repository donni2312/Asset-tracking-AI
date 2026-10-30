namespace AssetIQ.Api.Contracts;

// ── Phase 4c: gate officer, verification ──────────────────────────────────────

public sealed record GatePassItemDto
{
    public required int AssetId { get; init; }
    public required string AssetName { get; init; }
    public string? RfidTag { get; init; }
    public string? AssetNumber { get; init; }
    public DateTime? ExitedAt { get; init; }
    public string? ExitOfficer { get; init; }
    public DateTime? ReturnedAt { get; init; }
    public string? ReturnOfficer { get; init; }
    public string? ReturnCondition { get; init; }
    public string? ReturnNotes { get; init; }
}

/// <summary>SG-01: the result of a scan at the gate.</summary>
public sealed record GateScanDto
{
    /// <summary>clear (may leave) | return (expected back) | stop (must not leave)</summary>
    public required string Verdict { get; init; }
    public required string Message { get; init; }
    public int? AssetId { get; init; }
    public GatePassDto? GatePass { get; init; }
    /// <summary>SG-05: the scan matched a visitor's checked-in item.</summary>
    public int? VisitorItemId { get; init; }
}

public sealed record GateActionDto
{
    public required int Id { get; init; }
    public int? InsightId { get; init; }
    public required int AssetId { get; init; }
    public int? GateLocationId { get; init; }
    /// <summary>stopped | released_with_override | escalated</summary>
    public required string Action { get; init; }
    public string? Notes { get; init; }
    public required string Officer { get; init; }
    public required DateTime At { get; init; }
}

public sealed record GateAlertDto
{
    public required int InsightId { get; init; }
    public required int AssetId { get; init; }
    public required string AssetName { get; init; }
    public string? RfidTag { get; init; }
    public string? AssetNumber { get; init; }
    public required string Type { get; init; }
    public required string Severity { get; init; }
    public required string Message { get; init; }
    public int? GatePassId { get; init; }
    public required DateTime CreatedAt { get; init; }
    public required List<GateActionDto> Actions { get; init; }
}

/// <summary>GI-06: an item out after its pass's return date.</summary>
public sealed record GateOverdueDto
{
    public required int GatePassId { get; init; }
    public required int AssetId { get; init; }
    public required string AssetName { get; init; }
    public string? RfidTag { get; init; }
    public string? AssetNumber { get; init; }
    public int? SbuId { get; init; }
    public required string RequestedBy { get; init; }
    public string? Destination { get; init; }
    /// <summary>YYYY-MM-DD</summary>
    public required string ReturnDate { get; init; }
    public required DateTime ExitedAt { get; init; }
}

public sealed record VerificationScanDto
{
    public required string Code { get; init; }
    public int? AssetId { get; init; }
    public string? AssetName { get; init; }
    public required string ScannedBy { get; init; }
    public required DateTime ScannedAt { get; init; }
}

public sealed record VerificationResultDto
{
    public required int Id { get; init; }
    public int? AssetId { get; init; }
    public string? AssetName { get; init; }
    public string? AssetNumber { get; init; }
    public string? RfidTag { get; init; }
    /// <summary>The scanned code of an unrecorded item.</summary>
    public string? Code { get; init; }
    /// <summary>found | missing | unrecorded</summary>
    public required string Result { get; init; }
    /// <summary>Where the register says the item is.</summary>
    public string? RecordedLocationName { get; init; }
    /// <summary>adjust | write_off | investigate | register</summary>
    public string? Resolution { get; init; }
    public string? Comment { get; init; }
    public string? ResolvedBy { get; init; }
    public DateTime? ResolvedAt { get; init; }
}

public sealed record VerificationSessionDto
{
    public required int Id { get; init; }
    public required int SbuId { get; init; }
    public required string SbuCode { get; init; }
    public required int LocationId { get; init; }
    public required string LocationName { get; init; }
    /// <summary>it | non_it | all</summary>
    public required string Scope { get; init; }
    /// <summary>open | closed | reconciled</summary>
    public required string Status { get; init; }
    public required string StartedBy { get; init; }
    public required DateTime StartedAt { get; init; }
    public DateTime? ClosedAt { get; init; }
    public required int ExpectedCount { get; init; }
    public required int ScanCount { get; init; }
    public required int FoundCount { get; init; }
    public required int MissingCount { get; init; }
    public required int UnrecordedCount { get; init; }
    public required int UnresolvedCount { get; init; }
    public required bool CanScan { get; init; }
    public required bool CanResolve { get; init; }
    public required List<VerificationResultDto> Results { get; init; }
    public required List<VerificationScanDto> Scans { get; init; }
}
