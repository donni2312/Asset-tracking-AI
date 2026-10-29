namespace AssetIQ.Api.Contracts;

// Response bodies of the Priority 1 (AU-01 … AU-12) endpoints, in the shapes of lib/api-spec/openapi.yaml.
// Money is a JSON number with at most two decimals; months are "YYYY-MM", days "YYYY-MM-DD".

// ── categories and templates (AU-01, GI-02, AM-01, GF-03) ─────────────────────

public sealed record TemplateFieldDto
{
    public required int Id { get; init; }
    public required string Key { get; init; }
    public required string Label { get; init; }
    public required string Type { get; init; }
    public string? Unit { get; init; }
    public required bool Required { get; init; }
    public IReadOnlyList<string>? AllowedValues { get; init; }
    public required bool IsIdentifier { get; init; }
    public required int SortOrder { get; init; }
}

public sealed record AssetCategoryDto
{
    public required int Id { get; init; }
    public required string Code { get; init; }
    public required string Name { get; init; }
    public required bool IsIt { get; init; }
    public required IReadOnlyList<string> SubCategories { get; init; }
    public required string DescriptionPattern { get; init; }
    public string? IdentifierFieldKey { get; init; }
    public required string DepreciationMethod { get; init; }
    public int? UsefulLifeMonths { get; init; }
    public decimal? AnnualRate { get; init; }
    /// <summary>Residual value proposed at capitalisation, as a % of cost (0 by default).</summary>
    public required decimal DefaultResidualPercent { get; init; }
    public required bool Active { get; init; }
    public required IReadOnlyList<TemplateFieldDto> Fields { get; init; }
}

public sealed record GroupSettingDto
{
    public required string Key { get; init; }
    /// <summary>The setting's JSON value.</summary>
    public required System.Text.Json.JsonElement Value { get; init; }
    public required string Description { get; init; }
    public required string EditableBy { get; init; }
    public string? UpdatedBy { get; init; }
    public required DateTime UpdatedAt { get; init; }
}

// ── asset card (AU-08) ───────────────────────────────────────────────────────

public sealed record AssetAttributeDto(string Key, string Label, string Value, string? Unit);

public sealed record AssetValueDto
{
    public required string Period { get; init; }
    public required bool Capitalised { get; init; }
    public decimal? OriginalCostInCurrency { get; init; }
    public string? Currency { get; init; }
    public decimal? ExchangeRate { get; init; }
    public required decimal OriginalCost { get; init; }
    public required decimal CapitalisedUpgrades { get; init; }
    public required decimal TotalCost { get; init; }
    public required decimal DepreciationMonth { get; init; }
    public required decimal AccumulatedDepreciation { get; init; }
    public required decimal NetBookValue { get; init; }
    public required decimal ResidualValue { get; init; }
    public required string DepreciationMethod { get; init; }
    public int? UsefulLifeMonths { get; init; }
    public decimal? AnnualRate { get; init; }
}

public sealed record AssetUpgradeDto
{
    public required int Id { get; init; }
    public required int AssetId { get; init; }
    public string? AssetNumber { get; init; }
    public string? AssetDescription { get; init; }
    public required string UpgradeDate { get; init; }
    public required string Description { get; init; }
    public required string BeforeText { get; init; }
    public required string AfterText { get; init; }
    public required IReadOnlyDictionary<string, string?> FieldChanges { get; init; }
    /// <summary>USD (invoice amount × exchange rate).</summary>
    public required decimal Cost { get; init; }
    public required string Currency { get; init; }
    public required decimal InvoiceAmount { get; init; }
    public required decimal ExchangeRate { get; init; }
    public string? Supplier { get; init; }
    public string? InvoiceNo { get; init; }
    public required string LoggedBy { get; init; }
    public required string LoggedByRole { get; init; }
    public required string Status { get; init; }
    public string? DecidedBy { get; init; }
    public DateTime? DecidedAt { get; init; }
    public string? DecisionComment { get; init; }
    public string? CapitalisedPeriod { get; init; }
    public int? RevisedLifeMonths { get; init; }
    public required DateTime CreatedAt { get; init; }
}

public sealed record DepreciationLineDto(string Period, decimal OpeningCost, decimal OpeningAccumulated, decimal Charge, decimal Adjustment, decimal ClosingAccumulated, decimal ClosingNbv, bool Locked);

/// <summary>One dated event on the asset's timeline (AU-06, GI-04), newest first.</summary>
public sealed record TimelineEventDto(string Id, DateTime At, string Kind, string Title, string? Detail, string? By);

public sealed record AuditEntryDto(int Id, DateTime At, string Action, string? Field, string? OldValue, string? NewValue, string? Reason, string UserName);

public sealed record AssetCardDto
{
    public required AssetDto Asset { get; init; }
    public required string CategoryName { get; init; }
    public required bool IsIt { get; init; }
    public required IReadOnlyList<AssetAttributeDto> Attributes { get; init; }
    public required AssetValueDto Value { get; init; }
    public required IReadOnlyList<AssetUpgradeDto> Upgrades { get; init; }
    public required IReadOnlyList<DepreciationLineDto> Schedule { get; init; }
    public required IReadOnlyList<TimelineEventDto> Timeline { get; init; }
    public required IReadOnlyList<string> CompletenessGaps { get; init; }
    /// <summary>What the signed-in user may do on this card (the API checks each action again).</summary>
    public required AssetCardPermissionsDto Can { get; init; }
}

public sealed record AssetCardPermissionsDto(bool EditTechnical, bool EditFinancial, bool LogUpgrade, bool DecideUpgrades, bool ChangeCategory, bool Tag, bool Attach);

// ── reports (AU-09, AU-10, AU-11) ────────────────────────────────────────────

public sealed record TotalsDto
{
    public required int Count { get; init; }
    public required decimal OriginalCost { get; init; }
    public required decimal UpgradesInPeriod { get; init; }
    public required decimal CapitalisedUpgrades { get; init; }
    public required decimal TotalCost { get; init; }
    public required decimal DepreciationMonth { get; init; }
    public required decimal DepreciationYtd { get; init; }
    public required decimal AccumulatedDepreciation { get; init; }
    public required decimal NetBookValue { get; init; }
    public required int Additions { get; init; }
}

public sealed record RegisterRowDto
{
    public required int AssetId { get; init; }
    public string? AssetNumber { get; init; }
    public required string Description { get; init; }
    public string? SerialNumber { get; init; }
    public required int CategoryId { get; init; }
    public required string CategoryName { get; init; }
    public string? SbuCode { get; init; }
    public string? LocationName { get; init; }
    public string? Custodian { get; init; }
    public required string Status { get; init; }
    public required bool Capitalised { get; init; }
    public string? InUseDate { get; init; }
    public required decimal OriginalCost { get; init; }
    public required decimal UpgradesInPeriod { get; init; }
    public required decimal CapitalisedUpgrades { get; init; }
    public required decimal TotalCost { get; init; }
    public required decimal DepreciationMonth { get; init; }
    public required decimal DepreciationYtd { get; init; }
    public required decimal AccumulatedDepreciation { get; init; }
    public required decimal NetBookValue { get; init; }
}

public sealed record ReportFiltersDto(string Period, int? SbuId, int? LocationId, int? CategoryId);

public sealed record RegisterReportDto(ReportFiltersDto Filters, IReadOnlyList<RegisterRowDto> Rows, TotalsDto Totals);

public sealed record CategoryLineDto(int CategoryId, string CategoryName, TotalsDto Totals);

public sealed record CategoryReportDto(ReportFiltersDto Filters, IReadOnlyList<CategoryLineDto> Categories, TotalsDto Totals);

public sealed record SbuValueDto(int? SbuId, string? SbuCode, string? Color, decimal NetBookValue, decimal TotalCost);

public sealed record FinanceDashboardDto(
    ReportFiltersDto Filters, IReadOnlyList<CategoryLineDto> Categories, TotalsDto Totals, IReadOnlyList<SbuValueDto> BySbu,
    decimal PercentComplete, int IncompleteAssets, int PendingDecisions);

public sealed record CompletenessGapDto(string Field, string Message, string Owner);

public sealed record CompletenessRowDto(int AssetId, string? AssetNumber, string Description, string? SbuCode, string CategoryName, IReadOnlyList<CompletenessGapDto> Gaps);

public sealed record SbuCompletenessDto(int? SbuId, string? SbuCode, int Assets, int Complete, decimal PercentComplete);

public sealed record CompletenessReportDto(int Assets, int Complete, decimal PercentComplete, IReadOnlyList<SbuCompletenessDto> Sbus, IReadOnlyList<CompletenessRowDto> Incomplete);

// ── approvals, depreciation runs, users ──────────────────────────────────────

public sealed record ApprovalItemDto(string Id, string Type, int AssetId, string? AssetNumber, string AssetDescription, string? SbuCode, string Summary, DateTime RaisedAt, string RaisedBy, int? UpgradeId);

public sealed record DepreciationRunDto(string Period, int? SbuId, int Assets, int MonthsWritten, decimal ChargeForPeriod);

public sealed record DepreciationLockDto(int Id, int SbuId, string Period, string LockedBy, DateTime LockedAt);

public sealed record AccountDto
{
    public required int Id { get; init; }
    public int? EmployeeId { get; init; }
    public required string Username { get; init; }
    public required string Name { get; init; }
    public required string Role { get; init; }
    public int? SbuId { get; init; }
    public DateTime? AccessExpiresAt { get; init; }
    public required bool Active { get; init; }
    public DateTime? LastLoginAt { get; init; }
}

public sealed record AccessLogEntryDto(int Id, DateTime At, string Username, string Role, string Action, string? Detail);
