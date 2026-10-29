namespace AssetIQ.Api.Contracts;

// ── Phase 2: user accounts, departments, employees, attachments, scans ─────────

public sealed record DepartmentDto(int Id, int SbuId, string SbuCode, string Name, int EmployeeCount);

public sealed record EmployeeDto
{
    public required int Id { get; init; }
    public required int SbuId { get; init; }
    public required string SbuCode { get; init; }
    public int? DepartmentId { get; init; }
    public string? DepartmentName { get; init; }
    public required string EmployeeNo { get; init; }
    public required string Name { get; init; }
    public string? JobRole { get; init; }
    public int? LineManagerId { get; init; }
    public string? LineManagerName { get; init; }
    public string? Email { get; init; }
    public string? Phone { get; init; }
    public required bool Active { get; init; }
    /// <summary>Items the employee holds now (the toolbox, HR-01).</summary>
    public required int ItemsHeld { get; init; }
}

/// <summary>A user account as Group IT / SBU IT manage it (GI-08).</summary>
public sealed record UserAccountDto
{
    public required int Id { get; init; }
    public required string Username { get; init; }
    public required string Name { get; init; }
    public required string Role { get; init; }
    public required string RoleName { get; init; }
    public int? SbuId { get; init; }
    public string? SbuCode { get; init; }
    public int? EmployeeId { get; init; }
    public string? EmployeeName { get; init; }
    public DateTime? AccessExpiresAt { get; init; }
    public required bool Active { get; init; }
    public DateTime? LastLoginAt { get; init; }
}

public sealed record RoleDto(string Role, string Name, bool SbuScoped, bool NeedsEmployee);

public sealed record AttachmentDto
{
    public required int Id { get; init; }
    public required int AssetId { get; init; }
    public int? UpgradeId { get; init; }
    public required string Kind { get; init; }
    public required string FileName { get; init; }
    public required string ContentType { get; init; }
    public required int SizeBytes { get; init; }
    public required string UploadedBy { get; init; }
    public required DateTime UploadedAt { get; init; }
}

/// <summary>
/// A scan at the gate or on the floor (SG-01, SI-03). Everyone who may scan gets the basic identification;
/// the holder and the gate pass only when the asset is theirs to see (or at their own SBU's gate).
/// </summary>
public sealed record ScanResultDto
{
    public required int AssetId { get; init; }
    /// <summary>The caller may open the full record (asset card).</summary>
    public required bool CanOpen { get; init; }
    public required string Description { get; init; }
    public string? AssetNumber { get; init; }
    public string? RfidTag { get; init; }
    public string? SbuCode { get; init; }
    public string? SbuName { get; init; }
    public required string Status { get; init; }
    public string? Holder { get; init; }
    public int? PhotoAttachmentId { get; init; }
    /// <summary>approved (a valid approved pass covers it now) | not_approved | not_shown (another SBU's asset).</summary>
    public required string GateClearance { get; init; }
    public int? GatePassId { get; init; }
    public string? GatePassStatus { get; init; }
    public string? GatePassReturnDate { get; init; }
}
