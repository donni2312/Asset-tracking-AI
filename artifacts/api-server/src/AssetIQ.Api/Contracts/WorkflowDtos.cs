namespace AssetIQ.Api.Contracts;

// ── Phase 4: approvals engine and workflows ─────────────────────────────────

public sealed record ApprovalStepDto(int Id, int StepNo, string Role, string Label, string Status, string? DecidedBy, string? OnBehalfOf,
    DateTime? DecidedAt, string? Comment, string? EscalatedToRole, DateTime? RemindedAt);

public sealed record ApprovalRequestDto(int Id, string Kind, int? AssetId, string? AssetNumber, string AssetDescription, string Status, string Summary,
    string RequestedBy, DateTime CreatedAt, DateTime? DecidedAt, IReadOnlyList<ApprovalStepDto> Steps);

/// <summary>SI-05. <c>Cost</c> is null for roles without financial access.</summary>
public sealed record RepairDto(int Id, int AssetId, string Fault, string? Action, string? Parts, string? Vendor, int? DowntimeDays, decimal? Cost,
    string Status, string RequestedBy, DateTime CreatedAt, DateTime? CompletedAt);

/// <summary>SF-04. The money is null for roles without financial access.</summary>
public sealed record DisposalDto(int Id, int AssetId, string Reason, string? Condition, string RecommendedBy, string? Method, string? DisposalDate,
    decimal? Proceeds, string? Buyer, decimal? NbvAtDisposal, decimal? GainLoss, string? DataWipeNote, string Status, DateTime CreatedAt);

public sealed record DelegationDto(int Id, int UserId, string UserName, int? DelegateUserId, string? DelegateName, DateTime StartsAt, DateTime EndsAt, string? Reason);

public sealed record ColleagueDto(int Id, string Name, string Role);

public sealed record NotificationDto(int Id, string Kind, string Title, string Body, string? Link, DateTime CreatedAt, DateTime? ReadAt);
