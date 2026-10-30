using System.Globalization;
using System.Text.Json;
using AssetIQ.Api.Auth;
using AssetIQ.Api.Configuration;
using AssetIQ.Api.Contracts;
using AssetIQ.Api.Data;
using AssetIQ.Api.Domain;
using AssetIQ.Api.Infrastructure;
using AssetIQ.Api.Services.Depreciation;
using Microsoft.EntityFrameworkCore;

namespace AssetIQ.Api.Services;

public sealed record RevaluationInput(string Kind, decimal? NewValue, decimal? ImpairmentAmount, int? RevisedLifeMonths, string Reason, int AttachmentId);

public sealed record CapexLineInput(int CategoryId, decimal Amount);

/// <summary>
/// Phase 5a, Finance: revaluations and impairments (SF-07), CAPEX budgets against what is capitalised (SF-09, GF-05,
/// with alerts at the capex_alert_percents), and the results of verification counts per SBU (GF-08). Everything here
/// is money, so only the Finance roles (and the auditor, read-only) reach it.
/// </summary>
public sealed class FinancePlanningService(
    AssetIqDbContext db, AppClock clock, AppConfig config, AuditTrail audit, DepreciationService depreciation, ValuationService valuations,
    NotificationService notifications)
{
    // ── SF-07 ───────────────────────────────────────────────────────────────

    /// <summary>
    /// A revaluation to a new value, or an impairment by an amount, from the current open month. The NBV changes at
    /// once and the depreciation after it is spread over the remaining (or revised) life. Reason and a supporting
    /// document (an attachment of the asset) are required.
    /// </summary>
    public async Task<AssetRevaluation> RevalueAsync(AuthUser user, int assetId, RevaluationInput input, CancellationToken ct)
    {
        var asset = await db.Assets.IgnoreQueryFilters().FirstOrDefaultAsync(a => a.Id == assetId, ct) ?? throw new ApiException(StatusCodes.Status404NotFound, "Asset not found");
        if (!AccessPolicy.IsSbuFinanceOf(user, asset.SbuId)) throw AccessPolicy.Deny(user, "revalue or impair this asset (its SBU Finance does, SF-07)");
        if (asset.CapitalisedAt is null) throw new ApiException(StatusCodes.Status409Conflict, "Only a capitalised asset can be revalued or impaired");
        if (asset.Status == AssetStatuses.Disposed) throw new ApiException(StatusCodes.Status409Conflict, "The asset has been disposed");
        if (!await db.AssetAttachments.AnyAsync(a => a.Id == input.AttachmentId && a.AssetId == assetId, ct))
            throw new ApiException(StatusCodes.Status400BadRequest, "attachmentId: attach the supporting document (valuation report, assessment) to the asset first");

        var period = depreciation.CurrentPeriod;
        var locks = await depreciation.LockedThroughBySbuAsync(ct);
        if (asset.SbuId is { } sbu && locks.TryGetValue(sbu, out var locked) && period <= locked)
            throw new ApiException(StatusCodes.Status409Conflict, $"{period} is locked for this SBU; the change would take effect in a closed month");

        var before = await NbvBeforeAsync(assetId, period, ct);
        decimal amount;
        if (input.Kind == "impairment")
        {
            if (input.ImpairmentAmount is not > 0) throw new ApiException(StatusCodes.Status400BadRequest, "impairmentAmount: a positive amount (USD)");
            if (input.ImpairmentAmount > before - asset.ResidualValue)
                throw new ApiException(StatusCodes.Status400BadRequest, $"impairmentAmount: at most {before - asset.ResidualValue:0.00}, the NBV above the residual value");
            amount = -input.ImpairmentAmount.Value;
        }
        else
        {
            if (input.NewValue is not >= 0) throw new ApiException(StatusCodes.Status400BadRequest, "newValue: the revalued amount (USD)");
            if (input.NewValue < asset.ResidualValue) throw new ApiException(StatusCodes.Status400BadRequest, "newValue: cannot be below the residual value");
            amount = input.NewValue.Value - before;
            if (amount == 0) throw new ApiException(StatusCodes.Status400BadRequest, "newValue: equals the current NBV");
        }
        if (input.RevisedLifeMonths is <= 0) throw new ApiException(StatusCodes.Status400BadRequest, "revisedLifeMonths: a positive number of months");

        var row = new AssetRevaluation
        {
            AssetId = assetId, Kind = input.Kind, Period = period.ToString(), Amount = amount, NbvBefore = before, NbvAfter = before + amount,
            RevisedLifeMonths = input.RevisedLifeMonths, Reason = input.Reason, AttachmentId = input.AttachmentId, RecordedBy = Name(user), CreatedAt = clock.UtcNow,
        };
        db.AssetRevaluations.Add(row);
        await db.SaveChangesAsync(ct);
        audit.Record(user, "asset", assetId, assetId, input.Kind == "impairment" ? "impaired" : "revalued", "netBookValue",
            before, row.NbvAfter, $"{input.Reason} (from {period}, document #{input.AttachmentId})");
        await db.SaveChangesAsync(ct);
        return row;
    }

    public async Task<List<RevaluationDto>> RevaluationsAsync(int assetId, CancellationToken ct) =>
        await db.AssetRevaluations.AsNoTracking().Where(r => r.AssetId == assetId).OrderByDescending(r => r.CreatedAt)
            .Select(r => new RevaluationDto(r.Id, r.Kind, r.Period, r.Amount, r.NbvBefore, r.NbvAfter, r.RevisedLifeMonths, r.Reason, r.AttachmentId, r.RecordedBy, r.CreatedAt))
            .ToListAsync(ct);

    /// <summary>The carrying amount at the start of <paramref name="period"/> (the close of the month before, or cost when new).</summary>
    private async Task<decimal> NbvBeforeAsync(int assetId, Period period, CancellationToken ct)
    {
        var lines = (await depreciation.SchedulesAsync([assetId], period.Previous(), ct)).GetValueOrDefault(assetId);
        if (lines is { Count: > 0 }) return lines[^1].ClosingNbv;
        var cost = await db.Assets.IgnoreQueryFilters().Where(a => a.Id == assetId).Select(a => a.CostUsd ?? 0m).FirstAsync(ct);
        var earlier = await db.AssetRevaluations.Where(r => r.AssetId == assetId).SumAsync(r => (decimal?)r.Amount, ct) ?? 0m;
        return cost + earlier;
    }

    // ── SF-09 / GF-05 ───────────────────────────────────────────────────────

    /// <summary>The financial year (by the year it ends) of today, in the organisation's time zone.</summary>
    public int CurrentFinancialYear => FinancialYear.EndOf(depreciation.CurrentPeriod).Year;

    /// <summary>SF-09: SBU Finance enters its SBU's budget per category for a financial year (Group Finance may load any SBU's, GF-05).</summary>
    public async Task SetBudgetAsync(AuthUser user, int sbuId, int financialYear, IReadOnlyList<CapexLineInput> lines, CancellationToken ct)
    {
        if (!(AccessPolicy.IsSbuFinanceOf(user, sbuId) || user.Role == Roles.GroupFinance)) throw AccessPolicy.Deny(user, "set this SBU's CAPEX budget");
        if (financialYear < 2000 || financialYear > 2100) throw new ApiException(StatusCodes.Status400BadRequest, "financialYear: e.g. 2027 for November 2026 – October 2027");
        if (lines.Count == 0) throw new ApiException(StatusCodes.Status400BadRequest, "lines: at least one category");
        var categories = await db.AssetCategories.AsNoTracking().Select(c => c.Id).ToListAsync(ct);
        foreach (var line in lines)
        {
            if (!categories.Contains(line.CategoryId)) throw new ApiException(StatusCodes.Status400BadRequest, $"categoryId {line.CategoryId}: no such category");
            if (line.Amount < 0) throw new ApiException(StatusCodes.Status400BadRequest, "amount: cannot be negative");
            var row = await db.CapexBudgets.FirstOrDefaultAsync(b => b.SbuId == sbuId && b.FinancialYear == financialYear && b.CategoryId == line.CategoryId, ct);
            if (row is null)
            {
                row = new CapexBudget { SbuId = sbuId, FinancialYear = financialYear, CategoryId = line.CategoryId };
                db.CapexBudgets.Add(row);
            }
            audit.Record(user, "capex_budget", row.Id == 0 ? null : row.Id, null, "capex_budget_set", $"FY{financialYear} category {line.CategoryId}", row.Amount, line.Amount);
            if (row.Amount != line.Amount) row.AlertedPercent = null; // a new figure is re-checked against the thresholds
            row.Amount = line.Amount;
            row.UpdatedBy = Name(user);
            row.UpdatedAt = clock.UtcNow;
        }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>GF-05 / SF-09: budget against capitalised spend per SBU and category, with variance and over-budget flags.</summary>
    public async Task<CapexReportDto> CapexAsync(AuthUser user, int financialYear, int? sbuId, CancellationToken ct)
    {
        var budgets = db.CapexBudgets.AsNoTracking().Where(b => b.FinancialYear == financialYear);
        if (Roles.IsSbuScoped(user.Role)) budgets = budgets.Where(b => b.SbuId == user.SbuId);
        else if (sbuId is not null) budgets = budgets.Where(b => b.SbuId == sbuId);
        var budgetRows = await budgets.ToListAsync(ct);
        var spend = await SpendAsync(financialYear, Roles.IsSbuScoped(user.Role) ? user.SbuId : sbuId, ct);
        var sbus = await db.Sbus.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Code, ct);
        var categories = await db.AssetCategories.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Name, ct);

        var keys = budgetRows.Select(b => (b.SbuId, b.CategoryId)).Union(spend.Keys).Distinct().ToList();
        var lines = keys.Select(k =>
        {
            var budget = budgetRows.FirstOrDefault(b => b.SbuId == k.SbuId && b.CategoryId == k.CategoryId)?.Amount ?? 0m;
            var actual = spend.GetValueOrDefault(k);
            return new CapexLineDto(k.SbuId, sbus.GetValueOrDefault(k.SbuId), k.CategoryId, categories.GetValueOrDefault(k.CategoryId) ?? "", budget, actual,
                actual - budget, budget == 0 ? null : Math.Round(actual / budget * 100m, 1), actual > budget);
        }).OrderBy(l => l.SbuCode).ThenBy(l => l.CategoryName).ToList();
        var bySbu = lines.GroupBy(l => (l.SbuId, l.SbuCode)).Select(g =>
        {
            var budget = g.Sum(l => l.Budget);
            var actual = g.Sum(l => l.Actual);
            return new CapexSbuDto(g.Key.SbuId, g.Key.SbuCode, budget, actual, actual - budget, budget == 0 ? null : Math.Round(actual / budget * 100m, 1), actual > budget);
        }).OrderBy(s => s.SbuCode).ToList();
        return new CapexReportDto(financialYear, $"FY{financialYear}", $"{financialYear - 1}-11-01", $"{financialYear}-10-31", lines, bySbu);
    }

    /// <summary>SF-09: an alert when a budget line reaches each threshold (80 %, 100 % by default), once per threshold.</summary>
    public async Task<int> CapexAlertsAsync(CancellationToken ct)
    {
        var setting = await db.GroupSettings.AsNoTracking().Where(s => s.Key == "capex_alert_percents").Select(s => s.Value).FirstOrDefaultAsync(ct);
        int[] thresholds;
        try { thresholds = (setting is null ? null : JsonSerializer.Deserialize<int[]>(setting)) ?? [80, 100]; }
        catch (JsonException) { thresholds = [80, 100]; }
        var year = CurrentFinancialYear;
        var rows = await db.CapexBudgets.Where(b => b.FinancialYear == year && b.Amount > 0).ToListAsync(ct);
        if (rows.Count == 0) return 0;
        var spend = await SpendAsync(year, null, ct);
        var categories = await db.AssetCategories.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var sent = 0;
        foreach (var row in rows)
        {
            var used = spend.GetValueOrDefault((row.SbuId, row.CategoryId)) / row.Amount * 100m;
            var reached = thresholds.Where(t => used >= t).DefaultIfEmpty(0).Max();
            if (reached == 0 || reached <= (row.AlertedPercent ?? 0)) continue;
            row.AlertedPercent = reached;
            var people = await db.Users.Where(u => u.Active && (u.Role == Roles.SbuFinance && u.SbuId == row.SbuId || u.Role == Roles.GroupFinance)).ToListAsync(ct);
            notifications.Notify(people, "capex", $"CAPEX {reached}% used: {categories.GetValueOrDefault(row.CategoryId)} FY{year}",
                $"{used:0.0}% of the {row.Amount:N2} USD budget has been capitalised", "/capex");
            sent++;
        }
        await db.SaveChangesAsync(ct);
        return sent;
    }

    /// <summary>USD capitalised in the financial year per SBU and category: assets capitalised in it plus upgrades capitalised in it.</summary>
    // ASSUMPTION: spend is counted when an asset (or upgrade) is capitalised, at its USD cost, in the financial year of the capitalisation.
    private async Task<Dictionary<(int SbuId, int CategoryId), decimal>> SpendAsync(int financialYear, int? sbuId, CancellationToken ct)
    {
        var from = ZonedStart(new DateTime(financialYear - 1, 11, 1));
        var to = ZonedStart(new DateTime(financialYear, 11, 1));
        var assets = db.Assets.IgnoreQueryFilters().AsNoTracking().Where(a => a.CapitalisedAt >= from && a.CapitalisedAt < to && a.CostUsd != null && a.SbuId != null);
        if (sbuId is not null) assets = assets.Where(a => a.SbuId == sbuId);
        var assetSpend = await assets.GroupBy(a => new { a.SbuId, a.CategoryId }).Select(g => new { g.Key.SbuId, g.Key.CategoryId, Sum = g.Sum(a => a.CostUsd!.Value) }).ToListAsync(ct);
        var firstPeriod = $"{financialYear - 1}-11";
        var lastPeriod = $"{financialYear}-10";
        var upgrades = from u in db.AssetUpgrades.AsNoTracking()
                       join a in db.Assets.IgnoreQueryFilters() on u.AssetId equals a.Id
                       where u.Status == UpgradeStatuses.Capitalised && u.CapitalisedPeriod != null
                             && string.Compare(u.CapitalisedPeriod, firstPeriod) >= 0 && string.Compare(u.CapitalisedPeriod, lastPeriod) <= 0 && a.SbuId != null
                       select new { a.SbuId, a.CategoryId, u.Cost };
        if (sbuId is not null) upgrades = upgrades.Where(x => x.SbuId == sbuId);
        var upgradeSpend = await upgrades.GroupBy(x => new { x.SbuId, x.CategoryId }).Select(g => new { g.Key.SbuId, g.Key.CategoryId, Sum = g.Sum(x => x.Cost) }).ToListAsync(ct);
        var result = new Dictionary<(int, int), decimal>();
        foreach (var x in assetSpend) result[(x.SbuId!.Value, x.CategoryId)] = result.GetValueOrDefault((x.SbuId!.Value, x.CategoryId)) + x.Sum;
        foreach (var x in upgradeSpend) result[(x.SbuId!.Value, x.CategoryId)] = result.GetValueOrDefault((x.SbuId!.Value, x.CategoryId)) + x.Sum;
        return result;
    }

    private DateTime ZonedStart(DateTime localMidnight) => TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(localMidnight, DateTimeKind.Unspecified), config.TimeZone);

    // ── GF-08 ───────────────────────────────────────────────────────────────

    /// <summary>GF-08: per count, found / missing / unrecorded with their value; the missing items with last location and custodian.</summary>
    public async Task<List<VerificationOutcomeDto>> VerificationOutcomesAsync(AuthUser user, int? sbuId, CancellationToken ct)
    {
        var sessions = db.VerificationSessions.AsNoTracking().Where(s => s.Status != VerificationStatuses.Open);
        if (Roles.IsSbuScoped(user.Role)) sessions = sessions.Where(s => s.SbuId == user.SbuId);
        else if (sbuId is not null) sessions = sessions.Where(s => s.SbuId == sbuId);
        var list = await sessions.OrderByDescending(s => s.ClosedAt).Take(200).ToListAsync(ct);
        if (list.Count == 0) return [];
        var ids = list.Select(s => s.Id).ToList();
        var results = await db.VerificationResults.AsNoTracking().Where(r => ids.Contains(r.SessionId)).ToListAsync(ct);
        var assetIds = results.Where(r => r.AssetId is not null).Select(r => r.AssetId!.Value).Distinct().ToList();
        var period = depreciation.CurrentPeriod;
        var nbv = (await valuations.ValueAsync(user, new ValuationFilter(null, null, null), period, ct)).ToDictionary(v => v.AssetId, v => v.NetBookValue);
        var assets = await db.Assets.IgnoreQueryFilters().AsNoTracking().Where(a => assetIds.Contains(a.Id))
            .Select(a => new { a.Id, a.Name, a.AssetNumber, a.AssignedTo, Location = a.CurrentLocation!.Name }).ToDictionaryAsync(a => a.Id, ct);
        var lastSeen = await db.Movements.IgnoreQueryFilters().AsNoTracking().Where(m => assetIds.Contains(m.AssetId) && m.ToLocationId != null)
            .GroupBy(m => m.AssetId).Select(g => new { g.Key, Last = g.Max(m => m.Timestamp) }).ToDictionaryAsync(x => x.Key, x => x.Last, ct);
        var sbus = await db.Sbus.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Code, ct);
        var locations = await db.Locations.AsNoTracking().ToDictionaryAsync(l => l.Id, l => l.Name, ct);
        decimal ValueOf(IEnumerable<VerificationResult> rows) => rows.Where(r => r.AssetId is not null).Sum(r => nbv.GetValueOrDefault(r.AssetId!.Value));
        return list.Select(s =>
        {
            var own = results.Where(r => r.SessionId == s.Id).ToList();
            var found = own.Where(r => r.Result == VerificationResults.Found).ToList();
            var missing = own.Where(r => r.Result == VerificationResults.Missing).ToList();
            var unrecorded = own.Where(r => r.Result == VerificationResults.Unrecorded).ToList();
            return new VerificationOutcomeDto(s.Id, s.SbuId, sbus.GetValueOrDefault(s.SbuId), locations.GetValueOrDefault(s.LocationId), s.Scope, s.Status, s.ClosedAt,
                found.Count, ValueOf(found), missing.Count, ValueOf(missing), unrecorded.Count, ValueOf(unrecorded),
                missing.Select(r => assets.TryGetValue(r.AssetId!.Value, out var a)
                    ? new MissingAssetDto(a.Id, a.AssetNumber, a.Name, a.Location, a.AssignedTo, lastSeen.TryGetValue(a.Id, out var t) ? t : null, nbv.GetValueOrDefault(a.Id), r.Resolution)
                    : null).OfType<MissingAssetDto>().ToList());
        }).ToList();
    }

    private static string Name(AuthUser user) => user.Name.Length > 0 ? user.Name : user.Username;
}
