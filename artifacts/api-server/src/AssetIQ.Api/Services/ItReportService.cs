using System.Globalization;
using System.Text.Json;
using AssetIQ.Api.Auth;
using AssetIQ.Api.Configuration;
using AssetIQ.Api.Contracts;
using AssetIQ.Api.Data;
using AssetIQ.Api.Domain;
using AssetIQ.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AssetIQ.Api.Services;

public sealed record InventoryFilter(int? SbuId, string? SubCategory, string? Make, string? Model, int? MinAgeYears, string? Status);

/// <summary>
/// Phase 4d, the IT views: the Group IT inventory (GI-01), the hardware refresh report and warranty alerts (GI-03),
/// and the SBU IT status board with the spare stock ready to issue (SI-06). No money: these are inventory views.
/// </summary>
public sealed class ItReportService(AssetIqDbContext db, AppClock clock, AppConfig config, InsightService insights, NotificationService notifications)
{
    private DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(clock.UtcNow, config.TimeZone));

    /// <summary>GI-01: every IT asset in scope with SBU, site, user, status and when it was last seen.</summary>
    public async Task<List<ItInventoryRowDto>> InventoryAsync(AuthUser user, InventoryFilter filter, CancellationToken ct)
    {
        var query = db.Assets.AsNoTracking().Where(a => a.AssetCategory.IsIt && a.Status != AssetStatuses.Disposed);
        if (filter.SbuId is { } sbu) query = query.Where(a => a.SbuId == sbu);
        if (filter.SubCategory is { } sub) query = query.Where(a => a.SubCategory == sub);
        if (filter.Status is { } status) query = query.Where(a => a.Status == status);
        var rows = await Rows(query).ToListAsync(ct);
        var result = await DecorateAsync(rows, ct);
        if (filter.Make is { } make) result = result.Where(r => r.Make?.Contains(make, StringComparison.OrdinalIgnoreCase) == true).ToList();
        if (filter.Model is { } model) result = result.Where(r => r.Model?.Contains(model, StringComparison.OrdinalIgnoreCase) == true).ToList();
        if (filter.MinAgeYears is { } age) result = result.Where(r => r.AgeYears >= age).ToList();
        return result;
    }

    /// <summary>GI-03: devices past their refresh age (group setting by type) or out of warranty, by SBU.</summary>
    public async Task<List<ItInventoryRowDto>> RefreshAsync(AuthUser user, int? sbuId, CancellationToken ct)
    {
        var ages = await RefreshAgesAsync(ct);
        var all = await InventoryAsync(user, new InventoryFilter(sbuId, null, null, null, null, null), ct);
        var today = Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return all.Select(r =>
            {
                var limit = ages.GetValueOrDefault((r.SubCategory ?? "").ToLowerInvariant(), ages.GetValueOrDefault("default", 5));
                var reasons = new List<string>();
                if (r.AgeYears is { } age && age >= limit) reasons.Add($"{age:0.#} years old (refresh at {limit})");
                if (r.WarrantyEnds is { } w && string.CompareOrdinal(w, today) < 0) reasons.Add($"out of warranty since {w}");
                return r with { RefreshReasons = reasons };
            })
            .Where(r => r.RefreshReasons.Count > 0)
            .OrderBy(r => r.SbuCode).ThenByDescending(r => r.AgeYears)
            .ToList();
    }

    /// <summary>GI-03: an alert (once) for each IT asset whose warranty ends within the alert window; Group IT and SBU IT are told.</summary>
    public async Task<int> WarrantyAlertsAsync(CancellationToken ct)
    {
        var setting = await db.GroupSettings.AsNoTracking().Where(s => s.Key == "warranty_alert_days").Select(s => s.Value).FirstOrDefaultAsync(ct);
        var days = int.TryParse(setting, NumberStyles.Integer, CultureInfo.InvariantCulture, out var d) ? d : 60;
        var today = Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var until = Today.AddDays(days).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var due = await db.Assets.IgnoreQueryFilters().AsNoTracking()
            .Where(a => a.AssetCategory.IsIt && a.Status != AssetStatuses.Disposed && a.WarrantyEnds != null
                && string.Compare(a.WarrantyEnds, today) >= 0 && string.Compare(a.WarrantyEnds, until) <= 0)
            .Select(a => new { a.Id, a.Name, a.AssetNumber, a.WarrantyEnds, a.SbuId }).ToListAsync(ct);
        var raised = 0;
        foreach (var a in due)
        {
            var row = await insights.RaiseAsync(new InsightDraft("warranty_expiry", InsightTypes.MaintenanceDue, InsightSeverities.Info,
                $"Warranty of \"{a.Name}\" ({a.AssetNumber ?? "number pending"}) ends on {a.WarrantyEnds}", a.Id), InsightDedupe.Message, ct);
            if (row is null) continue;
            raised++;
            var people = await db.Users.Where(u => u.Active && (u.Role == Roles.GroupIt || u.Role == Roles.SbuIt && u.SbuId == a.SbuId)).ToListAsync(ct);
            notifications.Notify(people, "warranty", $"Warranty ends {a.WarrantyEnds}: {a.Name}", row.Message, $"/assets/{a.Id}/card");
            await db.SaveChangesAsync(ct);
        }
        return raised;
    }

    /// <summary>SI-06: counts per status (and faulty items) for the caller's IT assets, and the spare stock ready to issue.</summary>
    public async Task<ItStatusBoardDto> StatusBoardAsync(AuthUser user, int? departmentId, int? locationId, CancellationToken ct)
    {
        var query = db.Assets.AsNoTracking().Where(a => a.AssetCategory.IsIt && a.Status != AssetStatuses.Disposed);
        if (departmentId is { } dep) query = query.Where(a => a.DepartmentId == dep);
        if (locationId is { } loc) query = query.Where(a => a.CurrentLocationId == loc);
        var counts = await query.GroupBy(a => a.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        // ASSUMPTION: "faulty" (SI-06) is an item whose condition is poor or damaged and that is not already in repair.
        var faulty = await query.CountAsync(a => (a.Condition == "poor" || a.Condition == "damaged") && a.Status != AssetStatuses.InRepair && a.Status != AssetStatuses.RepairRequested, ct);
        var spare = await DecorateAsync(await Rows(query.Where(a => a.Status == AssetStatuses.InStore && a.AssetNumber != null && a.RfidTag != null)).ToListAsync(ct), ct);
        return new ItStatusBoardDto
        {
            Counts = AssetStatuses.All.Select(s => new StatusCountDto(s, counts.FirstOrDefault(c => c.Key == s)?.Count ?? 0)).ToList(),
            Faulty = faulty,
            Spare = spare,
        };
    }

    public static ExportTable InventoryTable(string title, IReadOnlyList<string> subtitle, List<ItInventoryRowDto> rows, bool refresh) => new(
        title, subtitle,
        [new("Asset no."), new("Description"), new("Type"), new("Make"), new("Model"), new("Serial"), new("SBU"), new("Site"), new("Assigned to"), new("Status"),
         new("Last seen"), new("Age (years)", Number: true), new("Warranty ends"), .. refresh ? new[] { new ExportColumn("Why") } : []],
        rows.Select(r => new ExportRow([r.AssetNumber, r.Description, r.SubCategory, r.Make, r.Model, r.SerialNumber, r.SbuCode, r.Site, r.AssignedTo, r.Status,
            r.LastSeen?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), r.AgeYears, r.WarrantyEnds, .. refresh ? new object?[] { string.Join("; ", r.RefreshReasons) } : []]))
            .Append(new ExportRow(["Total", $"{rows.Count} devices", null, null, null, null, null, null, null, null, null, null, null, .. refresh ? new object?[] { null } : []], Bold: true))
            .ToList());

    private sealed record Row(int Id, string? AssetNumber, string Name, string? SubCategory, string? SerialNumber, string? SbuCode, string? Site,
        string? AssignedTo, string Status, string? Condition, string? PurchaseDate, string? InUseDate, string? WarrantyEnds, DateTime CreatedAt, string? RfidTag);

    private static IQueryable<Row> Rows(IQueryable<Asset> query) =>
        query.OrderBy(a => a.Sbu!.Code).ThenBy(a => a.Name).Select(a => new Row(a.Id, a.AssetNumber, a.Name, a.SubCategory, a.SerialNumber, a.Sbu!.Code,
            a.CurrentLocation!.Name, a.AssignedTo, a.Status, a.Condition, a.PurchaseDate, a.InUseDate, a.WarrantyEnds, a.CreatedAt, a.RfidTag));

    private async Task<List<ItInventoryRowDto>> DecorateAsync(List<Row> rows, CancellationToken ct)
    {
        var ids = rows.Select(r => r.Id).ToList();
        var attributes = (await db.AssetAttributeValues.AsNoTracking().Where(v => ids.Contains(v.AssetId) && (v.Field.Key == "make" || v.Field.Key == "model"))
            .Select(v => new { v.AssetId, v.Field.Key, v.ValueText }).ToListAsync(ct)).ToLookup(v => v.AssetId);
        var seen = await db.Movements.IgnoreQueryFilters().AsNoTracking().Where(m => ids.Contains(m.AssetId))
            .GroupBy(m => m.AssetId).Select(g => new { g.Key, Last = g.Max(m => m.Timestamp) }).ToDictionaryAsync(x => x.Key, x => x.Last, ct);
        var today = Today;
        return rows.Select(r =>
        {
            var since = r.PurchaseDate ?? r.InUseDate;
            var start = since is not null && DateOnly.TryParseExact(since, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : DateOnly.FromDateTime(r.CreatedAt);
            return new ItInventoryRowDto
            {
                AssetId = r.Id, AssetNumber = r.AssetNumber, Description = r.Name, SubCategory = r.SubCategory, SerialNumber = r.SerialNumber, RfidTag = r.RfidTag,
                Make = attributes[r.Id].FirstOrDefault(v => v.Key == "make")?.ValueText, Model = attributes[r.Id].FirstOrDefault(v => v.Key == "model")?.ValueText,
                SbuCode = r.SbuCode, Site = r.Site, AssignedTo = r.AssignedTo, Status = r.Status, Condition = r.Condition,
                LastSeen = seen.TryGetValue(r.Id, out var last) ? last : null,
                AgeYears = Math.Round((today.DayNumber - start.DayNumber) / 365.25m, 1),
                WarrantyEnds = r.WarrantyEnds, RefreshReasons = [],
            };
        }).ToList();
    }

    private async Task<Dictionary<string, int>> RefreshAgesAsync(CancellationToken ct)
    {
        var json = await db.GroupSettings.AsNoTracking().Where(s => s.Key == "device_refresh_age_years").Select(s => s.Value).FirstOrDefaultAsync(ct);
        try
        {
            var map = json is null ? null : JsonSerializer.Deserialize<Dictionary<string, int>>(json);
            return map is null ? new() : map.ToDictionary(kv => kv.Key.ToLowerInvariant(), kv => kv.Value);
        }
        catch (JsonException)
        {
            return new();
        }
    }
}
