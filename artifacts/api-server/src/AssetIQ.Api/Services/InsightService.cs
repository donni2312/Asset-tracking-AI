using System.Linq.Expressions;
using AssetIQ.Api.Configuration;
using AssetIQ.Api.Contracts;
using AssetIQ.Api.Data;
using AssetIQ.Api.Domain;
using AssetIQ.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AssetIQ.Api.Services;

public enum InsightDedupe
{
    /// <summary>Skip when an open insight with the same type, asset and message exists (repeated scans do not spam the feed).</summary>
    Message,
    /// <summary>Skip when any open insight of that type exists for the asset (periodic rules such as idle / missing).</summary>
    AssetType,
}

public sealed class InsightService(
    AssetIqDbContext db,
    AppClock clock,
    AppConfig config,
    AssetQueries assets,
    ILogger<InsightService> logger)
{
    public static readonly Expression<Func<AiInsight, InsightDto>> ToDto = i => new InsightDto
    {
        Id = i.Id,
        Type = i.Type,
        Message = i.Message,
        AssetId = i.AssetId,
        AssetName = i.Asset!.Name,
        SbuCode = i.Asset!.Sbu!.Code,
        GatePassId = i.GatePassId,
        Severity = i.Severity,
        Dismissed = i.Dismissed,
        CreatedAt = i.CreatedAt,
    };

    /// <summary>Persist an insight unless an identical one is still open. Returns the new row, or null.</summary>
    public async Task<AiInsight?> RaiseAsync(InsightDraft draft, InsightDedupe dedupe, CancellationToken ct)
    {
        var open = db.AiInsights.IgnoreQueryFilters().Where(i => i.Type == draft.Type && !i.Dismissed && i.AssetId == draft.AssetId);
        if (dedupe == InsightDedupe.Message) open = open.Where(i => i.Message == draft.Message);
        if (await open.AnyAsync(ct)) return null;

        var row = new AiInsight
        {
            Type = draft.Type,
            Severity = draft.Severity,
            Message = draft.Message,
            AssetId = draft.AssetId,
            GatePassId = draft.GatePassId,
            Dismissed = false,
            CreatedAt = clock.UtcNow,
        };
        db.AiInsights.Add(row);
        await db.SaveChangesAsync(ct);
        return row;
    }

    /// <summary>The rows as API objects (asset name and the asset's SBU code attached), in the given order.</summary>
    public async Task<List<InsightDto>> ToDtosAsync(IReadOnlyCollection<AiInsight> rows, CancellationToken ct)
    {
        if (rows.Count == 0) return [];
        var ids = rows.Select(r => r.Id).ToList();
        var byId = await db.AiInsights.IgnoreQueryFilters().AsNoTracking().Where(i => ids.Contains(i.Id)).Select(ToDto).ToDictionaryAsync(d => d.Id, ct);
        return ids.Select(id => byId[id]).ToList();
    }

    /// <summary>Every insight the caller may see (the query filter scopes them), newest first.</summary>
    public Task<List<InsightDto>> ListAsync(CancellationToken ct) =>
        db.AiInsights.AsNoTracking()
            .OrderByDescending(i => i.CreatedAt)
            .ThenByDescending(i => i.Id)
            .Select(ToDto)
            .ToListAsync(ct);

    /// <summary>
    /// Periodic rules (idle and missing assets), evaluated whenever insights are listed. A failure is
    /// logged and never stops the list from loading. Returns how many insights were raised.
    /// </summary>
    public async Task<int> RunPeriodicRulesAsync(CancellationToken ct)
    {
        try
        {
            var now = clock.UtcNow;
            var all = await db.Assets.IgnoreQueryFilters().AsNoTracking()
                .OrderBy(a => a.Id)
                .Select(a => new PeriodicAsset(a.Id, a.Name, a.Status, a.SerialNumber ?? "no serial", a.CreatedAt))
                .ToListAsync(ct);
            var lastMoved = await assets.LastMovedAtAsync(ct);
            var open = (await db.AiInsights.IgnoreQueryFilters().AsNoTracking()
                    .Where(i => !i.Dismissed && i.AssetId != null && (i.Type == InsightTypes.IdleAsset || i.Type == InsightTypes.MissingAsset))
                    .Select(i => new { i.Type, AssetId = i.AssetId!.Value })
                    .ToListAsync(ct))
                .Select(i => (i.Type, i.AssetId))
                .ToHashSet();

            var created = 0;
            foreach (var asset in all)
            {
                var drafts = new[]
                {
                    AnomalyRules.IdleAsset(asset, lastMoved.TryGetValue(asset.Id, out var last) ? last : null, now, config.IdleDays),
                    AnomalyRules.MissingAsset(asset),
                };
                foreach (var draft in drafts.OfType<InsightDraft>())
                {
                    if (!open.Add((draft.Type, asset.Id))) continue;
                    db.AiInsights.Add(new AiInsight
                    {
                        Type = draft.Type,
                        Severity = draft.Severity,
                        Message = draft.Message,
                        AssetId = draft.AssetId,
                        GatePassId = draft.GatePassId,
                        Dismissed = false,
                        CreatedAt = now,
                    });
                    created++;
                }
            }
            if (created > 0) await db.SaveChangesAsync(ct);
            return created;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Periodic insight analysis failed");
            db.ChangeTracker.Clear();
            return 0;
        }
    }
}
