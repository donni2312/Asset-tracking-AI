namespace AssetIQ.Api.Contracts;

// ── Phase 5a: revaluations, CAPEX, verification outcomes ──────────────────────

/// <summary>SF-07: one revaluation or impairment (USD; Finance roles only).</summary>
public sealed record RevaluationDto(int Id, string Kind, string Period, decimal Amount, decimal NbvBefore, decimal NbvAfter, int? RevisedLifeMonths,
    string Reason, int AttachmentId, string RecordedBy, DateTime CreatedAt);

/// <param name="UsedPercent">Actual ÷ budget × 100; null without a budget.</param>
public sealed record CapexLineDto(int SbuId, string? SbuCode, int CategoryId, string CategoryName, decimal Budget, decimal Actual, decimal Variance,
    decimal? UsedPercent, bool OverBudget);

public sealed record CapexSbuDto(int SbuId, string? SbuCode, decimal Budget, decimal Actual, decimal Variance, decimal? UsedPercent, bool OverBudget);

/// <param name="FinancialYear">The year it ends in: 2027 = 1 November 2026 – 31 October 2027.</param>
public sealed record CapexReportDto(int FinancialYear, string Label, string StartsOn, string EndsOn, IReadOnlyList<CapexLineDto> Lines, IReadOnlyList<CapexSbuDto> BySbu);

public sealed record MissingAssetDto(int AssetId, string? AssetNumber, string Description, string? LastLocation, string? Custodian, DateTime? LastSeen,
    decimal NetBookValue, string? Resolution);

/// <summary>GF-08: one verification count's outcome, with values (NBV, USD).</summary>
public sealed record VerificationOutcomeDto(int SessionId, int SbuId, string? SbuCode, string? LocationName, string Scope, string Status, DateTime? ClosedAt,
    int Found, decimal FoundValue, int Missing, decimal MissingValue, int Unrecorded, decimal UnrecordedValue, IReadOnlyList<MissingAssetDto> MissingAssets);
