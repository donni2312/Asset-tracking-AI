using AssetIQ.Api.Auth;
using AssetIQ.Api.Configuration;
using AssetIQ.Api.Data;
using AssetIQ.Api.Domain;
using AssetIQ.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AssetIQ.Api.Services.Depreciation;

public sealed record DepreciationRunResult(string Period, int? SbuId, int Assets, int MonthsWritten, decimal ChargeForPeriod);

/// <summary>
/// Depreciation from the database (AU-05, GF-03, SF-03): builds each capitalised asset's schedule with
/// <see cref="DepreciationEngine"/>, merges it with locked months (<see cref="DepreciationLedger"/>), stores
/// it (the monthly run) and locks months. Reads use the same computation as the run, so the asset card,
/// the category report and the dashboard always show what a run would store.
/// </summary>
public sealed class DepreciationService(AssetIqDbContext db, AppClock clock, AppConfig config, AuditTrail audit)
{
    /// <summary>The current month in the organisation's time zone.</summary>
    public Period CurrentPeriod
    {
        get
        {
            var parts = ZonedTime.Parts(clock.UtcNow, config.TimeZone);
            return new Period(parts.Year, parts.Month);
        }
    }

    /// <summary>Merged schedules through <paramref name="through"/> for the given assets (uncapitalised assets have none).</summary>
    public async Task<Dictionary<int, IReadOnlyList<MonthLine>>> SchedulesAsync(IReadOnlyCollection<int> assetIds, Period through, CancellationToken ct)
    {
        var result = new Dictionary<int, IReadOnlyList<MonthLine>>();
        if (assetIds.Count == 0) return result;
        var ids = assetIds.ToList();

        var assets = await db.Assets.AsNoTracking()
            .Where(a => ids.Contains(a.Id) && a.CapitalisedAt != null && a.CostUsd != null && a.InUseDate != null)
            .Select(a => new
            {
                a.Id, a.SbuId, Cost = a.CostUsd!.Value, a.InUseDate, a.DisposalDate, a.ResidualValue,
                a.AssetCategory.DepreciationMethod, a.AssetCategory.UsefulLifeMonths, a.AssetCategory.AnnualRate,
            })
            .ToListAsync(ct);
        if (assets.Count == 0) return result;
        var capitalisedIds = assets.Select(a => a.Id).ToList();

        var additions = (await db.AssetUpgrades.AsNoTracking()
                .Where(u => capitalisedIds.Contains(u.AssetId) && u.Status == UpgradeStatuses.Capitalised && u.CapitalisedPeriod != null)
                .Select(u => new { u.AssetId, u.CapitalisedPeriod, u.Cost, u.RevisedLifeMonths })
                .ToListAsync(ct))
            .GroupBy(u => u.AssetId)
            .ToDictionary(g => g.Key, g => g.Select(u => new CapitalAddition(Period.Parse(u.CapitalisedPeriod!), u.Cost, u.RevisedLifeMonths)).ToList());
        // SF-07 (Phase 5a): a revaluation or impairment changes the carrying amount from its month, like a signed upgrade.
        foreach (var r in await db.AssetRevaluations.AsNoTracking().Where(r => capitalisedIds.Contains(r.AssetId)).ToListAsync(ct))
        {
            if (!additions.TryGetValue(r.AssetId, out var list)) additions[r.AssetId] = list = [];
            list.Add(new CapitalAddition(Period.Parse(r.Period), r.Amount, r.RevisedLifeMonths));
        }

        var posted = (await db.DepreciationEntries.AsNoTracking().Where(e => capitalisedIds.Contains(e.AssetId)).ToListAsync(ct))
            .GroupBy(e => e.AssetId)
            .ToDictionary(g => g.Key, g => (IReadOnlyDictionary<Period, PostedMonth>)g.ToDictionary(
                e => Period.Parse(e.Period), e => new PostedMonth(Period.Parse(e.Period), e.OpeningCost, e.OpeningAccumulated, e.Charge, e.Adjustment)));

        var locks = await LockedThroughBySbuAsync(ct);
        var none = new Dictionary<Period, PostedMonth>();
        foreach (var a in assets)
        {
            var input = new DepreciationInput(
                a.Cost, Period.Parse(a.InUseDate!), new DepreciationPolicy(a.DepreciationMethod, a.UsefulLifeMonths, a.AnnualRate),
                a.ResidualValue, additions.GetValueOrDefault(a.Id) ?? [], Period.OfDate(a.DisposalDate));
            var ideal = DepreciationEngine.Schedule(input, through);
            var lockedThrough = a.SbuId is { } sbu && locks.TryGetValue(sbu, out var l) ? l : (Period?)null;
            result[a.Id] = DepreciationLedger.Merge(ideal, posted.GetValueOrDefault(a.Id) ?? none, lockedThrough);
        }
        return result;
    }

    public async Task<Dictionary<int, Period>> LockedThroughBySbuAsync(CancellationToken ct) =>
        (await db.DepreciationLocks.AsNoTracking().GroupBy(l => l.SbuId).Select(g => new { g.Key, Max = g.Max(l => l.Period) }).ToListAsync(ct))
        .ToDictionary(x => x.Key, x => Period.Parse(x.Max!));

    /// <summary>
    /// The monthly run (SF-03, GF-03): stores every open month through <paramref name="period"/> for the SBU's
    /// capitalised assets (or the whole Group). Locked months are left exactly as they are.
    /// </summary>
    public async Task<DepreciationRunResult> RunAsync(AuthUser user, int? sbuId, Period period, CancellationToken ct)
    {
        if (!AccessPolicy.CanRunDepreciation(user, sbuId)) throw AccessPolicy.Deny(user, sbuId is null ? "run depreciation for the whole Group" : "run depreciation for this SBU");
        if (period > CurrentPeriod) throw new ApiException(StatusCodes.Status400BadRequest, "period: depreciation cannot be run for a future month");

        var ids = await db.Assets.AsNoTracking().Where(a => a.CapitalisedAt != null && (sbuId == null || a.SbuId == sbuId)).Select(a => a.Id).ToListAsync(ct);
        var schedules = await SchedulesAsync(ids, period, ct);
        var locks = await LockedThroughBySbuAsync(ct);
        var sbuOf = await db.Assets.AsNoTracking().Where(a => ids.Contains(a.Id)).ToDictionaryAsync(a => a.Id, a => a.SbuId, ct);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var written = 0;
        var charge = 0m;
        var now = clock.UtcNow;
        foreach (var (assetId, lines) in schedules)
        {
            var lockedThrough = sbuOf[assetId] is { } s && locks.TryGetValue(s, out var l) ? l : (Period?)null;
            var openLines = lines.Where(x => lockedThrough is null || x.Period > lockedThrough).ToList();
            var openKeys = openLines.Select(x => x.Period.ToString()).ToList();
            var firstOpen = lockedThrough?.Next().ToString();
            // Open months no longer in the schedule (e.g. after a disposal) are removed; the rest are replaced.
            await db.DepreciationEntries
                .Where(e => e.AssetId == assetId && (firstOpen == null || string.Compare(e.Period, firstOpen) >= 0) && string.Compare(e.Period, period.ToString()) <= 0)
                .ExecuteDeleteAsync(ct);
            foreach (var line in openLines)
            {
                db.DepreciationEntries.Add(new DepreciationEntry
                {
                    AssetId = assetId, Period = line.Period.ToString(), OpeningCost = line.OpeningCost,
                    OpeningAccumulated = line.OpeningAccumulated, Charge = line.Charge, Adjustment = line.Adjustment,
                    ClosingAccumulated = line.ClosingAccumulated, ClosingNbv = line.ClosingNbv, CreatedAt = now,
                });
                written++;
            }
            charge += lines.FirstOrDefault(x => x.Period == period)?.Posted ?? 0m;
        }
        audit.Record(user, "depreciation", sbuId, null, "depreciation_run", "period", null, period.ToString(),
            $"{schedules.Count} assets, {written} months written, charge for {period} {AuditTrail.Text(charge)}");
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new DepreciationRunResult(period.ToString(), sbuId, schedules.Count, written, charge);
    }

    /// <summary>
    /// SF-03: SBU Finance locks a month after review. The month is run first, so what is locked is exactly what
    /// was reviewed; afterwards neither it nor any earlier month is recalculated.
    /// </summary>
    public async Task LockAsync(AuthUser user, int sbuId, Period period, CancellationToken ct)
    {
        if (!AccessPolicy.IsSbuFinanceOf(user, sbuId)) throw AccessPolicy.Deny(user, "lock depreciation months for this SBU");
        if (period > CurrentPeriod) throw new ApiException(StatusCodes.Status400BadRequest, "period: a future month cannot be locked");
        var locks = await LockedThroughBySbuAsync(ct);
        if (locks.TryGetValue(sbuId, out var lockedThrough) && period <= lockedThrough)
            throw new ApiException(StatusCodes.Status409Conflict, $"{period} is already locked (locked through {lockedThrough})");

        await RunAsync(user, sbuId, period, ct);
        db.DepreciationLocks.Add(new DepreciationLock { SbuId = sbuId, Period = period.ToString(), LockedBy = user.Name, LockedAt = clock.UtcNow });
        audit.Record(user, "depreciation", sbuId, null, "month_locked", "period", lockedThrough == default ? null : lockedThrough.ToString(), period.ToString());
        await db.SaveChangesAsync(ct);
    }
}
