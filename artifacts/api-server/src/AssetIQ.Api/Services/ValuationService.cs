using AssetIQ.Api.Auth;
using AssetIQ.Api.Data;
using AssetIQ.Api.Domain;
using AssetIQ.Api.Services.Depreciation;
using Microsoft.EntityFrameworkCore;

namespace AssetIQ.Api.Services;

public sealed record ValuationFilter(int? SbuId = null, int? LocationId = null, int? CategoryId = null, int? AssetId = null);

/// <summary>One asset's figures for a month: the row of the register (SF-02) that every report adds up.</summary>
public sealed record AssetValuation
{
    public required int AssetId { get; init; }
    public string? AssetNumber { get; init; }
    public required string Description { get; init; }
    public string? SerialNumber { get; init; }
    public required int CategoryId { get; init; }
    public required string CategoryName { get; init; }
    public required bool IsIt { get; init; }
    public int? SbuId { get; init; }
    public string? SbuCode { get; init; }
    public int? LocationId { get; init; }
    public string? LocationName { get; init; }
    public string? Custodian { get; init; }
    public required string Status { get; init; }
    public required bool Capitalised { get; init; }
    public string? InUseDate { get; init; }
    /// <summary>USD cost at capitalisation (0 until capitalised).</summary>
    public required decimal OriginalCost { get; init; }
    /// <summary>All upgrades capitalised up to and including the period.</summary>
    public required decimal CapitalisedUpgrades { get; init; }
    /// <summary>Upgrades capitalised from the start of the financial year (1 November) to the period.</summary>
    public required decimal UpgradesYtd { get; init; }
    /// <summary>SF-07: revaluations and impairments up to and including the period (negative = down).</summary>
    public required decimal Revaluations { get; init; }
    public decimal TotalCost => OriginalCost + CapitalisedUpgrades + Revaluations;
    public required decimal DepreciationMonth { get; init; }
    public required decimal DepreciationYtd { get; init; }
    public required decimal AccumulatedDepreciation { get; init; }
    public decimal NetBookValue => TotalCost - AccumulatedDepreciation;
    /// <summary>Capitalised with an in-use date in the period's financial year (AU-10 "additions this year").</summary>
    public required bool AdditionThisYear { get; init; }
}

/// <summary>
/// The one calculation behind the asset card (AU-08), the category report (AU-09), the register export and
/// the management dashboard (AU-10). Because they all add up the same rows, the dashboard can never disagree
/// with the report, and the report's grand total is the register's total for the same filters.
/// </summary>
public sealed class ValuationService(AssetIqDbContext db, DepreciationService depreciation)
{
    /// <summary>
    /// Assets on the register at the end of <paramref name="period"/> that <paramref name="user"/> may see:
    /// registered by then and not disposed in or before it.
    /// </summary>
    // ASSUMPTION: assets not yet capitalised are listed (and counted) with zero cost until SBU Finance
    // capitalises them, so the count matches the asset register.
    public async Task<List<AssetValuation>> ValueAsync(AuthUser user, ValuationFilter filter, Period period, CancellationToken ct)
    {
        var periodText = period.ToString();
        var endOfPeriod = new DateTime(period.Year, period.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(1);
        var query = db.Assets.AsNoTracking().Where(AccessPolicy.Visible(user));
        if (filter.SbuId is { } sbu) query = query.Where(a => a.SbuId == sbu);
        if (filter.LocationId is { } loc) query = query.Where(a => a.CurrentLocationId == loc);
        if (filter.CategoryId is { } cat) query = query.Where(a => a.CategoryId == cat);
        if (filter.AssetId is { } id) query = query.Where(a => a.Id == id);
        query = query.Where(a => a.DisposalDate == null || string.Compare(a.DisposalDate.Substring(0, 7), periodText) > 0);

        var rows = await query
            .Select(a => new
            {
                a.Id, a.AssetNumber, a.Name, a.SerialNumber, a.CategoryId, CategoryName = a.AssetCategory.Name, a.AssetCategory.IsIt,
                a.SbuId, SbuCode = a.Sbu!.Code, a.CurrentLocationId, LocationName = a.CurrentLocation!.Name, a.AssignedTo,
                a.Status, a.CapitalisedAt, a.CostUsd, a.InUseDate, a.CreatedAt,
            })
            .ToListAsync(ct);
        rows = rows.Where(r => r.CapitalisedAt is not null && r.InUseDate is not null
                ? Period.Parse(r.InUseDate) <= period
                : r.CreatedAt < endOfPeriod)
            .ToList();

        var ids = rows.Select(r => r.Id).ToList();
        var upgrades = await db.AssetUpgrades.AsNoTracking()
            .Where(u => ids.Contains(u.AssetId) && u.Status == UpgradeStatuses.Capitalised && u.CapitalisedPeriod != null && string.Compare(u.CapitalisedPeriod, periodText) <= 0)
            .Select(u => new { u.AssetId, u.CapitalisedPeriod, u.Cost })
            .ToListAsync(ct);
        var schedules = await depreciation.SchedulesAsync(ids, period, ct);
        var revaluations = (await db.AssetRevaluations.AsNoTracking()
                .Where(v => ids.Contains(v.AssetId) && string.Compare(v.Period, periodText) <= 0).Select(v => new { v.AssetId, v.Amount }).ToListAsync(ct))
            .GroupBy(v => v.AssetId).ToDictionary(g => g.Key, g => g.Sum(v => v.Amount));
        var yearStart = period.StartOfFinancialYear;

        return rows.Select(r =>
        {
            var lines = schedules.GetValueOrDefault(r.Id) ?? [];
            var capitalised = r.CapitalisedAt is not null && r.CostUsd is not null;
            var own = upgrades.Where(u => u.AssetId == r.Id).ToList();
            return new AssetValuation
            {
                AssetId = r.Id, AssetNumber = r.AssetNumber, Description = r.Name, SerialNumber = r.SerialNumber,
                CategoryId = r.CategoryId, CategoryName = r.CategoryName, IsIt = r.IsIt,
                SbuId = r.SbuId, SbuCode = r.SbuCode, LocationId = r.CurrentLocationId, LocationName = r.LocationName,
                Custodian = r.AssignedTo, Status = r.Status, Capitalised = capitalised, InUseDate = r.InUseDate,
                OriginalCost = capitalised ? r.CostUsd!.Value : 0m,
                CapitalisedUpgrades = capitalised ? own.Sum(u => u.Cost) : 0m,
                Revaluations = capitalised ? revaluations.GetValueOrDefault(r.Id) : 0m,
                UpgradesYtd = capitalised ? own.Where(u => Period.Parse(u.CapitalisedPeriod!) >= yearStart).Sum(u => u.Cost) : 0m,
                DepreciationMonth = lines.Where(l => l.Period == period).Sum(l => l.Posted),
                DepreciationYtd = lines.Where(l => l.Period >= yearStart && l.Period <= period).Sum(l => l.Posted),
                AccumulatedDepreciation = lines.LastOrDefault()?.ClosingAccumulated ?? 0m,
                AdditionThisYear = capitalised && r.InUseDate is not null && FinancialYear.Contains(period, Period.Parse(r.InUseDate)),
            };
        })
        .OrderBy(v => v.CategoryName).ThenBy(v => v.AssetNumber ?? "~").ThenBy(v => v.AssetId)
        .ToList();
    }
}

/// <summary>Totals of a set of valuations (a category subtotal or a grand total).</summary>
public sealed record ValuationTotals(
    int Count, decimal OriginalCost, decimal UpgradesInPeriod, decimal CapitalisedUpgrades, decimal Revaluations, decimal TotalCost,
    decimal DepreciationMonth, decimal DepreciationYtd, decimal AccumulatedDepreciation, decimal NetBookValue, int Additions)
{
    public static ValuationTotals Of(IReadOnlyCollection<AssetValuation> rows) => new(
        rows.Count, rows.Sum(r => r.OriginalCost), rows.Sum(r => r.UpgradesYtd), rows.Sum(r => r.CapitalisedUpgrades), rows.Sum(r => r.Revaluations), rows.Sum(r => r.TotalCost),
        rows.Sum(r => r.DepreciationMonth), rows.Sum(r => r.DepreciationYtd), rows.Sum(r => r.AccumulatedDepreciation), rows.Sum(r => r.NetBookValue),
        rows.Count(r => r.AdditionThisYear));
}
