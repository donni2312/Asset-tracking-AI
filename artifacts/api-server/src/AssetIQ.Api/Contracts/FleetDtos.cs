namespace AssetIQ.Api.Contracts;

// ── Phase 4d: fleet (AM-02 … AM-04) ───────────────────────────────────────────

public sealed record VehicleDocumentDto
{
    public required int Id { get; init; }
    /// <summary>licence | insurance | fitness | zinara</summary>
    public required string Kind { get; init; }
    public string? Reference { get; init; }
    /// <summary>YYYY-MM-DD</summary>
    public required string ExpiresOn { get; init; }
    /// <summary>ok | due_30 | due_7 | expired</summary>
    public required string State { get; init; }
    public required int DaysLeft { get; init; }
}

public sealed record ServiceDueDto
{
    public int? IntervalReading { get; init; }
    public int? IntervalDays { get; init; }
    /// <summary>YYYY-MM-DD, when the schedule is by date.</summary>
    public string? DueDate { get; init; }
    /// <summary>Mileage / hours, when the schedule is by reading.</summary>
    public int? DueReading { get; init; }
    /// <summary>ok | due_soon | overdue</summary>
    public required string State { get; init; }
}

public sealed record ServiceLogDto
{
    public required int Id { get; init; }
    public required int AssetId { get; init; }
    public required string ServiceDate { get; init; }
    public int? Reading { get; init; }
    public required string Garage { get; init; }
    public required string WorkDone { get; init; }
    /// <summary>Finance roles only.</summary>
    public decimal? Cost { get; init; }
    public string? Currency { get; init; }
    public string? CostBy { get; init; }
    public required string LoggedBy { get; init; }
}

public sealed record FleetItemDto
{
    public required int AssetId { get; init; }
    public string? AssetNumber { get; init; }
    public required string Name { get; init; }
    public int? SbuId { get; init; }
    public string? SbuCode { get; init; }
    /// <summary>MV (motor vehicles) | PG (plant and generators)</summary>
    public required string CategoryCode { get; init; }
    public string? SubCategory { get; init; }
    public string? RegistrationNumber { get; init; }
    public string? MakeModel { get; init; }
    public int? DriverEmployeeId { get; init; }
    public string? Driver { get; init; }
    public int? LocationId { get; init; }
    /// <summary>The depot (current location).</summary>
    public string? Depot { get; init; }
    /// <summary>active | in_service | off_road</summary>
    public required string FleetStatus { get; init; }
    public required string AssetStatus { get; init; }
    /// <summary>Mileage (km) or hours run.</summary>
    public int? Reading { get; init; }
    /// <summary>km | h</summary>
    public required string ReadingUnit { get; init; }
    public required List<VehicleDocumentDto> Documents { get; init; }
    /// <summary>A document in force has expired: the vehicle shows red (AM-03).</summary>
    public required bool DocumentsExpired { get; init; }
    public ServiceDueDto? NextService { get; init; }
    public required bool CanEdit { get; init; }
    /// <summary>Filled on the single-vehicle view.</summary>
    public List<ServiceLogDto> Services { get; init; } = [];
}

// ── Phase 4d: bulk import (SF-10) ─────────────────────────────────────────────

public sealed record ImportRowDto
{
    /// <summary>Row number in the file (the header is row 1).</summary>
    public required int Row { get; init; }
    /// <summary>The generated description, when the template values are valid.</summary>
    public string? Description { get; init; }
    public required List<string> Errors { get; init; }
}

public sealed record ImportReportDto
{
    public required int RowCount { get; init; }
    public required int ErrorRowCount { get; init; }
    /// <summary>No row has an error: the file may be imported.</summary>
    public required bool CanImport { get; init; }
    public required int Imported { get; init; }
    public required List<int> AssetIds { get; init; }
    public required List<ImportRowDto> Rows { get; init; }
}

// ── Phase 4d: IT inventory, refresh, status board (GI-01, GI-03, SI-06) ─────────

public sealed record ItInventoryRowDto
{
    public required int AssetId { get; init; }
    public string? AssetNumber { get; init; }
    public required string Description { get; init; }
    public string? SubCategory { get; init; }
    public string? Make { get; init; }
    public string? Model { get; init; }
    public string? SerialNumber { get; init; }
    public string? RfidTag { get; init; }
    public string? SbuCode { get; init; }
    public string? Site { get; init; }
    public string? AssignedTo { get; init; }
    public required string Status { get; init; }
    public string? Condition { get; init; }
    /// <summary>The latest movement or scan.</summary>
    public DateTime? LastSeen { get; init; }
    /// <summary>From the purchase (or in-use) date.</summary>
    public decimal? AgeYears { get; init; }
    public string? WarrantyEnds { get; init; }
    /// <summary>GI-03: why the device is due for refresh.</summary>
    public required List<string> RefreshReasons { get; init; }
}

public sealed record StatusCountDto(string Status, int Count);

public sealed record ItStatusBoardDto
{
    public required List<StatusCountDto> Counts { get; init; }
    /// <summary>In poor or damaged condition and not in repair.</summary>
    public required int Faulty { get; init; }
    /// <summary>In store, numbered and tagged: ready to issue.</summary>
    public required List<ItInventoryRowDto> Spare { get; init; }
}

