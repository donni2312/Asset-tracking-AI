using AssetIQ.Api.Auth;
using AssetIQ.Api.Data;
using AssetIQ.Api.Domain;
using AssetIQ.Api.Services.Templates;
using Microsoft.EntityFrameworkCore;

namespace AssetIQ.Api.Services;

/// <summary>A missing Priority 1 item and who fixes it.</summary>
public sealed record CompletenessGap(string Field, string Message, string Owner);

/// <summary>What the AU-11 rules need to know about one asset.</summary>
public sealed record CompletenessFacts(
    Template Template, IReadOnlySet<string> FilledFieldKeys, string? SerialNumber, string? NoSerialStatus,
    string? AssetNumber, int? LocationId, string? Custodian, string? Condition,
    decimal? OriginalCost, string? InvoiceNo, string? PoNo, int PendingUpgrades);

/// <summary>
/// AU-11 data completeness: every Priority 1 field (description fields, serial or an approved "No serial"
/// reason, asset number, cost, invoice and PO) plus upgrades waiting for a Finance decision. Pure rules.
/// </summary>
public static class CompletenessRules
{
    public const string SbuFinance = "SBU Finance";
    public const string BusinessCentral = "Business Central (automatic)";

    public static IReadOnlyList<CompletenessGap> Gaps(CompletenessFacts f)
    {
        var gaps = new List<CompletenessGap>();
        var technical = f.Template.IsIt ? "SBU IT" : "SBU Admin Manager";

        foreach (var field in f.Template.Fields.Where(x => x.Required && x.Key != f.Template.IdentifierFieldKey && !f.FilledFieldKeys.Contains(x.Key)))
            gaps.Add(new($"attributes.{field.Key}", $"Description field missing: {field.Label}", technical));
        if (f.LocationId is null) gaps.Add(new("location", "Location missing", technical));
        if (string.IsNullOrWhiteSpace(f.Custodian)) gaps.Add(new("custodian", "Custodian missing", technical));
        if (string.IsNullOrWhiteSpace(f.Condition)) gaps.Add(new("condition", "Condition missing", technical));

        if (f.SerialNumber is null)
        {
            switch (f.NoSerialStatus)
            {
                case NoSerialStatuses.Approved: break;
                case NoSerialStatuses.Pending: gaps.Add(new("serialNumber", "\"No serial\" reason awaiting SBU Finance approval", SbuFinance)); break;
                case NoSerialStatuses.Rejected: gaps.Add(new("serialNumber", "\"No serial\" reason rejected: capture the serial", technical)); break;
                default: gaps.Add(new("serialNumber", "Serial missing (or a \"No serial\" reason)", technical)); break;
            }
        }
        if (f.AssetNumber is null) gaps.Add(new("assetNumber", "Asset number pending from Business Central", BusinessCentral));
        if (f.OriginalCost is null) gaps.Add(new("originalCost", "Cost missing", SbuFinance));
        if (string.IsNullOrWhiteSpace(f.InvoiceNo)) gaps.Add(new("invoiceNo", "Invoice missing", SbuFinance));
        if (string.IsNullOrWhiteSpace(f.PoNo)) gaps.Add(new("poNo", "PO number missing", SbuFinance));
        if (f.PendingUpgrades > 0)
            gaps.Add(new("upgrades", f.PendingUpgrades == 1 ? "Upgrade awaiting Finance decision" : $"{f.PendingUpgrades} upgrades awaiting Finance decision", SbuFinance));
        return gaps;
    }
}

public sealed record CompletenessRow(int AssetId, string? AssetNumber, string Description, int? SbuId, string? SbuCode, int CategoryId, string CategoryName, IReadOnlyList<CompletenessGap> Gaps);

public sealed record SbuCompleteness(int? SbuId, string? SbuCode, int Assets, int Complete, decimal PercentComplete);

public sealed record CompletenessReport(IReadOnlyList<SbuCompleteness> Sbus, IReadOnlyList<CompletenessRow> Incomplete, int Assets, int Complete, decimal PercentComplete);

public sealed class CompletenessService(AssetIqDbContext db)
{
    public async Task<CompletenessReport> ReportAsync(AuthUser user, int? sbuId, int? categoryId, CancellationToken ct)
    {
        var query = db.Assets.AsNoTracking().Where(AccessPolicy.Visible(user)).Where(a => a.DisposalDate == null);
        if (sbuId is { } s) query = query.Where(a => a.SbuId == s);
        if (categoryId is { } c) query = query.Where(a => a.CategoryId == c);
        var assets = await query
            .Select(a => new
            {
                a.Id, a.AssetNumber, a.Name, a.SbuId, SbuCode = a.Sbu!.Code, a.CategoryId, a.SerialNumber, a.NoSerialStatus,
                a.CurrentLocationId, a.AssignedTo, a.Condition, a.OriginalCost, a.InvoiceNo, a.PoNo,
                Filled = a.Attributes.Select(x => x.Field.Key).ToList(),
                PendingUpgrades = db.AssetUpgrades.Count(u => u.AssetId == a.Id && u.Status == UpgradeStatuses.Pending),
            })
            .ToListAsync(ct);
        var categoryIds = assets.Select(a => a.CategoryId).Distinct().ToList();
        var templates = (await db.AssetCategories.AsNoTracking().Include(x => x.Fields).Where(x => categoryIds.Contains(x.Id)).ToListAsync(ct))
            .ToDictionary(x => x.Id, Template.From);

        var rows = assets.Select(a =>
        {
            var template = templates[a.CategoryId];
            var filled = a.Filled.ToHashSet(StringComparer.Ordinal);
            var gaps = CompletenessRules.Gaps(new CompletenessFacts(template, filled, a.SerialNumber, a.NoSerialStatus, a.AssetNumber,
                a.CurrentLocationId, a.AssignedTo, a.Condition, a.OriginalCost, a.InvoiceNo, a.PoNo, a.PendingUpgrades));
            return new CompletenessRow(a.Id, a.AssetNumber, a.Name, a.SbuId, a.SbuCode, a.CategoryId, template.Name, gaps);
        }).ToList();

        var bySbu = rows.GroupBy(r => (r.SbuId, r.SbuCode))
            .Select(g => new SbuCompleteness(g.Key.SbuId, g.Key.SbuCode, g.Count(), g.Count(r => r.Gaps.Count == 0), Percent(g.Count(r => r.Gaps.Count == 0), g.Count())))
            .OrderBy(x => x.SbuCode)
            .ToList();
        var complete = rows.Count(r => r.Gaps.Count == 0);
        var incomplete = rows.Where(r => r.Gaps.Count > 0).OrderBy(r => r.SbuCode).ThenBy(r => r.CategoryName).ThenBy(r => r.AssetNumber ?? "~").ToList();
        return new CompletenessReport(bySbu, incomplete, rows.Count, complete, Percent(complete, rows.Count));
    }

    /// <summary>% of assets with no gap, one decimal; 100 when there is nothing to check.</summary>
    public static decimal Percent(int complete, int total) =>
        total == 0 ? 100m : Math.Round(complete * 100m / total, 1, MidpointRounding.AwayFromZero);
}
