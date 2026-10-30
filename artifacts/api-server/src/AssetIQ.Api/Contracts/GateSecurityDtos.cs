namespace AssetIQ.Api.Contracts;

// ── Phase 5b: security checklist, shift view, visitor equipment, incidents ─────

public sealed record SecurityActionDto(string Action, bool Done, string? Reference, string? Notes, string? DoneBy, DateTime? DoneAt);

/// <summary>GI-07: the security checklist of a lost or stolen asset.</summary>
public sealed record SecurityChecklistDto(int AssetId, string Status, string? LostDate, string? ReportedBy, string? IncidentNotes,
    IReadOnlyList<SecurityActionDto> Actions, bool CanRecord);

public sealed record ShiftPassDto(int GatePassId, int AssetId, string AssetName, string? Holder, string? Phone, string? Destination, string? ReturnDate);

/// <summary>SG-04: approved passes due out today and every overdue item.</summary>
public sealed record GateShiftDto(string Date, IReadOnlyList<ShiftPassDto> DueOut, IReadOnlyList<ShiftPassDto> Overdue);

public sealed record VisitorItemDto(int Id, string? GateName, string OwnerName, string? OwnerCompany, string Item, string? SerialNumber, bool HasPhoto,
    string CheckedInBy, DateTime CheckedInAt, string? CheckedOutBy, DateTime? CheckedOutAt);

public sealed record GateIncidentDto(int Id, string Kind, string? GateName, int? AssetId, string? AssetName, int? GatePassId, string? Code, string Notes,
    bool HasPhoto, string Officer, DateTime CreatedAt);
