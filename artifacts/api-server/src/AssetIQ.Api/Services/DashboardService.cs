using AssetIQ.Api.Configuration;
using AssetIQ.Api.Contracts;
using AssetIQ.Api.Data;
using AssetIQ.Api.Domain;
using AssetIQ.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AssetIQ.Api.Services;

public sealed class DashboardService(AssetIqDbContext db, AppClock clock, AppConfig config)
{
    private static readonly Dictionary<string, string> LifecycleLabels = new()
    {
        [LifecycleEventTypes.Deployment] = "deployed to",
        [LifecycleEventTypes.ReturnToIt] = "returned to IT",
        [LifecycleEventTypes.RepairSent] = "sent for repair",
        [LifecycleEventTypes.RepairReturned] = "returned from repair",
        [LifecycleEventTypes.SbuTransfer] = "transferred between SBUs",
        [LifecycleEventTypes.Note] = "note added",
    };

    public async Task<DashboardSummaryDto> GetSummaryAsync(CancellationToken ct)
    {
        // "Today" is the organisation's calendar day, not the server's (the API may run in UTC).
        var today = ZonedTime.StartOfZonedDay(clock.UtcNow, config.TimeZone);
        var sbuBreakdown = await db.Sbus.AsNoTracking()
            .OrderBy(s => s.Code)
            .Select(s => new SbuBreakdownDto
            {
                SbuId = s.Id,
                SbuCode = s.Code,
                SbuName = s.Name,
                SbuColor = s.Color,
                TotalAssets = db.Assets.Count(a => a.SbuId == s.Id),
                ActiveAssets = db.Assets.Count(a => a.SbuId == s.Id && a.Status == AssetStatuses.Active),
                MissingAssets = db.Assets.Count(a => a.SbuId == s.Id && a.Status == AssetStatuses.Missing),
                MaintenanceAssets = db.Assets.Count(a => a.SbuId == s.Id && a.Status == AssetStatuses.Maintenance),
            })
            .ToListAsync(ct);

        return new DashboardSummaryDto
        {
            TotalAssets = await db.Assets.CountAsync(ct),
            ActiveAssets = await db.Assets.CountAsync(a => a.Status == AssetStatuses.Active, ct),
            MissingAssets = await db.Assets.CountAsync(a => a.Status == AssetStatuses.Missing, ct),
            MaintenanceAssets = await db.Assets.CountAsync(a => a.Status == AssetStatuses.Maintenance, ct),
            PendingGatePasses = await db.GatePasses.CountAsync(p => p.Status == GatePassStatuses.Pending, ct),
            TodayMovements = await db.Movements.CountAsync(m => m.Timestamp >= today, ct),
            TotalLocations = await db.Locations.CountAsync(ct),
            UnacknowledgedInsights = await db.AiInsights.CountAsync(i => !i.Dismissed, ct),
            SbuBreakdown = sbuBreakdown,
        };
    }

    /// <summary>Recent movements, gate-pass events, lifecycle events and open insights, newest first.</summary>
    public async Task<List<ActivityItemDto>> GetActivityAsync(int limit, int? sbuId, CancellationToken ct)
    {
        var items = new List<ActivityItemDto>();

        var movementQuery = db.Movements.AsNoTracking();
        if (sbuId is not null) movementQuery = movementQuery.Where(m => m.Asset.SbuId == sbuId);
        var recentMovements = await movementQuery
            .OrderByDescending(m => m.Timestamp)
            .Take(limit)
            .Select(m => new
            {
                m.Id,
                m.AssetId,
                AssetName = m.Asset.Name,
                SbuCode = m.Asset.Sbu!.Code,
                m.TriggeredBy,
                m.GatePassId,
                m.Timestamp,
                ToLocationName = m.ToLocation!.Name,
            })
            .ToListAsync(ct);
        foreach (var m in recentMovements)
        {
            var isRfid = m.TriggeredBy == MovementTriggers.Rfid;
            var description = m.TriggeredBy == MovementTriggers.GatePass
                ? $"{m.AssetName} left the premises under gate pass #{m.GatePassId}"
                : isRfid
                    ? $"RFID scan: {m.AssetName} detected at {m.ToLocationName ?? "unknown location"}"
                    : $"{m.AssetName} moved manually to {m.ToLocationName ?? "an unknown location"}";
            items.Add(new ActivityItemDto
            {
                Id = $"mov-{m.Id}",
                Type = isRfid ? "rfid_scan" : "movement",
                Description = description,
                AssetId = m.AssetId,
                AssetName = m.AssetName,
                SbuCode = m.SbuCode,
                Severity = null,
                Timestamp = m.Timestamp,
            });
        }

        // The latest gate-pass events across all SBUs; the SBU filter applies after the limit.
        var recentPasses = await db.GatePasses.AsNoTracking()
            .OrderByDescending(p => p.UpdatedAt)
            .Take(limit)
            .Select(p => new
            {
                p.Id,
                p.AssetId,
                AssetName = p.Asset.Name,
                AssetSbuId = p.Asset.SbuId,
                SbuCode = p.Asset.Sbu!.Code,
                p.Status,
                p.RequestedBy,
                p.ApprovedBy,
                p.Source,
                p.CreatedAt,
                p.UpdatedAt,
                p.DecidedAt,
            })
            .ToListAsync(ct);
        foreach (var p in recentPasses)
        {
            if (sbuId is not null && p.AssetSbuId != sbuId) continue;
            if (p.Status == GatePassStatuses.Pending)
            {
                items.Add(new ActivityItemDto
                {
                    Id = $"gp-created-{p.Id}",
                    Type = "gate_pass_created",
                    Description = p.Source == GatePassSources.RfidGate
                        ? $"Gate pass #{p.Id} auto-raised for {p.AssetName} (detected at a gate without a pass)"
                        : $"Gate pass #{p.Id} requested for {p.AssetName} by {p.RequestedBy}",
                    AssetId = p.AssetId,
                    AssetName = p.AssetName,
                    SbuCode = p.SbuCode,
                    Severity = InsightSeverities.Warning,
                    Timestamp = p.CreatedAt,
                });
            }
            else
            {
                var by = string.IsNullOrEmpty(p.ApprovedBy) ? "" : $" by {p.ApprovedBy}";
                var approved = p.Status == GatePassStatuses.Approved;
                items.Add(new ActivityItemDto
                {
                    Id = $"gp-{p.Status}-{p.Id}",
                    Type = approved ? "gate_pass_approved" : "gate_pass_rejected",
                    Description = $"Gate pass #{p.Id} for {p.AssetName} {p.Status}{by}",
                    AssetId = p.AssetId,
                    AssetName = p.AssetName,
                    SbuCode = p.SbuCode,
                    Severity = approved ? InsightSeverities.Info : InsightSeverities.Warning,
                    Timestamp = p.DecidedAt ?? p.UpdatedAt,
                });
            }
        }

        var lifecycleQuery = db.AssetLifecycleEvents.AsNoTracking();
        if (sbuId is not null) lifecycleQuery = lifecycleQuery.Where(e => e.Asset.SbuId == sbuId);
        var recentLifecycle = await lifecycleQuery
            .OrderByDescending(e => e.Timestamp)
            .Take(10)
            .Select(e => new { e.Id, e.AssetId, AssetName = e.Asset.Name, SbuCode = e.Asset.Sbu!.Code, e.EventType, e.PerformedBy, e.Timestamp })
            .ToListAsync(ct);
        foreach (var e in recentLifecycle)
        {
            var label = LifecycleLabels.GetValueOrDefault(e.EventType, e.EventType);
            items.Add(new ActivityItemDto
            {
                Id = $"lc-{e.Id}",
                Type = "lifecycle_event",
                Description = $"{e.AssetName} {label} by {e.PerformedBy}",
                AssetId = e.AssetId,
                AssetName = e.AssetName,
                SbuCode = e.SbuCode,
                Severity = null,
                Timestamp = e.Timestamp,
            });
        }

        var openInsights = await db.AiInsights.AsNoTracking()
            .Where(i => !i.Dismissed)
            .OrderByDescending(i => i.CreatedAt)
            .ThenByDescending(i => i.Id)
            .Take(5)
            .Select(i => new { i.Id, i.Message, i.AssetId, AssetName = i.Asset!.Name, SbuCode = i.Asset!.Sbu!.Code, i.Severity, i.CreatedAt })
            .ToListAsync(ct);
        foreach (var i in openInsights)
        {
            items.Add(new ActivityItemDto
            {
                Id = $"ins-{i.Id}",
                Type = "insight",
                Description = i.Message,
                AssetId = i.AssetId,
                AssetName = i.AssetName,
                SbuCode = i.SbuCode,
                Severity = i.Severity,
                Timestamp = i.CreatedAt,
            });
        }

        return items.OrderByDescending(i => i.Timestamp).Take(limit).ToList();
    }

    /// <summary>Asset counts per location, largest first.</summary>
    public Task<List<LocationBreakdownDto>> GetLocationBreakdownAsync(int? sbuId, CancellationToken ct)
    {
        var query = db.Locations.AsNoTracking();
        if (sbuId is not null) query = query.Where(l => l.SbuId == sbuId);
        return query
            .Select(l => new LocationBreakdownDto
            {
                LocationId = l.Id,
                LocationName = l.Name,
                LocationType = l.Type,
                SbuCode = l.Sbu!.Code,
                AssetCount = db.Assets.Count(a => a.CurrentLocationId == l.Id),
            })
            .OrderByDescending(b => b.AssetCount)
            .ThenBy(b => b.LocationId)
            .ToListAsync(ct);
    }

    /// <summary>Daily movement counts for the last <paramref name="days"/> days, oldest first (dashboard chart).</summary>
    public async Task<List<MovementTrendPointDto>> GetMovementTrendAsync(int days, CancellationToken ct)
    {
        var zone = config.TimeZone;
        var keys = ZonedTime.LastNDayKeys(clock.UtcNow, days, zone);
        var from = ZonedTime.StartOfDayKey(keys[0], zone);
        var rows = await db.Movements.AsNoTracking()
            .Where(m => m.Timestamp >= from)
            .Select(m => new TrendRow(m.Timestamp, m.TriggeredBy))
            .ToListAsync(ct);
        return BucketMovementsByDay(rows, keys, zone);
    }

    public sealed record TrendRow(DateTime Timestamp, string TriggeredBy);

    /// <summary>Pure: bucket movements into calendar days (organisation time zone) by trigger type.</summary>
    public static List<MovementTrendPointDto> BucketMovementsByDay(IEnumerable<TrendRow> rows, IReadOnlyList<string> dayKeys, TimeZoneInfo zone)
    {
        var points = dayKeys.Select(date => new MovementTrendPointDto { Date = date }).ToList();
        var byDate = points.ToDictionary(p => p.Date);
        foreach (var row in rows)
        {
            if (!byDate.TryGetValue(ZonedTime.DateKey(row.Timestamp, zone), out var point)) continue;
            if (row.TriggeredBy == MovementTriggers.Rfid) point.Rfid++;
            else if (row.TriggeredBy == MovementTriggers.GatePass) point.GatePass++;
            else point.Manual++;
            point.Total++;
        }
        return points;
    }

    /// <summary>Reader-detected movements (RFID scans and gate-pass exits), newest first.</summary>
    public async Task<List<GateActivityItemDto>> GetGateActivityAsync(int limit, CancellationToken ct)
    {
        var rows = await db.Movements.AsNoTracking()
            .Where(m => m.RfidReaderId != null)
            .OrderByDescending(m => m.Timestamp)
            .Take(limit)
            .Select(m => new
            {
                ReaderId = m.RfidReader!.Id,
                ReaderName = m.RfidReader!.Name,
                LocationName = m.RfidReader!.Location.Name,
                LocationType = m.RfidReader!.Location.Type,
                SbuCode = m.RfidReader!.Location.Sbu!.Code,
                SbuName = m.RfidReader!.Location.Sbu!.Name,
                m.AssetId,
                AssetName = m.Asset.Name,
                m.Asset.RfidTag,
                m.TriggeredBy,
                m.FromLocationId,
                m.Timestamp,
            })
            .ToListAsync(ct);

        return rows.Select(m => new GateActivityItemDto
        {
            ReaderId = m.ReaderId,
            ReaderName = m.ReaderName,
            LocationName = m.LocationName,
            SbuCode = m.SbuCode ?? "TSL",
            SbuName = m.SbuName ?? "TSL Group",
            AssetId = m.AssetId,
            AssetName = m.AssetName,
            RfidTag = m.RfidTag ?? "",
            // A gate-pass exit leaves the site; a gate scan of an off-premises asset is an entry.
            Direction = m.LocationType != LocationTypes.Gate
                ? GatePassRules.Internal
                : m.TriggeredBy == MovementTriggers.GatePass ? GatePassRules.Exit : m.FromLocationId is null ? GatePassRules.Entry : GatePassRules.Exit,
            Timestamp = m.Timestamp,
        }).ToList();
    }
}
