using AssetIQ.Api.Auth;
using AssetIQ.Api.Contracts;
using AssetIQ.Api.Data;
using AssetIQ.Api.Domain;
using AssetIQ.Api.Infrastructure;
using AssetIQ.Api.Services.Depreciation;
using Microsoft.EntityFrameworkCore;

namespace AssetIQ.Api.Services;

/// <summary>Filters shared by the register, the category report and the dashboard.</summary>
public sealed record ReportQuery(Period Period, int? SbuId, int? LocationId, int? CategoryId, int? DepartmentId = null, string? Status = null)
{
    public ValuationFilter Filter => new(SbuId, LocationId, CategoryId);
    public ReportFiltersDto ToDto() => new(Period.ToString(), SbuId, LocationId, CategoryId);
}

/// <summary>
/// The asset register (SF-02), the category report (AU-09) and the management dashboard (AU-10), all built
/// from <see cref="ValuationService"/> rows, plus their Excel and PDF exports. Category subtotals are sums of
/// register rows and the grand total is the sum of the subtotals, so they agree by construction (and tests
/// check it).
/// </summary>
public sealed class FinanceReportService(AssetIqDbContext db, ValuationService valuations, CompletenessService completeness)
{
    public async Task<RegisterReportDto> RegisterAsync(AuthUser user, ReportQuery query, CancellationToken ct)
    {
        var rows = await RegisterRowsAsync(user, query, ct);
        return new RegisterReportDto(query.ToDto(), rows.Select(ToRow).ToList(), Totals(ValuationTotals.Of(rows)));
    }

    public async Task<CategoryReportDto> CategoryReportAsync(AuthUser user, ReportQuery query, CancellationToken ct)
    {
        var rows = await valuations.ValueAsync(user, query.Filter, query.Period, ct);
        var categories = ByCategory(rows);
        return new CategoryReportDto(query.ToDto(), categories, SumOf(categories));
    }

    public async Task<FinanceDashboardDto> DashboardAsync(AuthUser user, ReportQuery query, CancellationToken ct)
    {
        var rows = await valuations.ValueAsync(user, query.Filter, query.Period, ct);
        var categories = ByCategory(rows);
        var colours = await db.Sbus.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Color, ct);
        var bySbu = rows.GroupBy(r => (r.SbuId, r.SbuCode))
            .Select(g => new SbuValueDto(g.Key.SbuId, g.Key.SbuCode, g.Key.SbuId is { } id ? colours.GetValueOrDefault(id) : null, g.Sum(r => r.NetBookValue), g.Sum(r => r.TotalCost)))
            .OrderBy(s => s.SbuCode)
            .ToList();
        var complete = await completeness.ReportAsync(user, query.SbuId, query.CategoryId, ct);
        var pending = (await ApprovalsAsync(user, ct)).Count;
        return new FinanceDashboardDto(query.ToDto(), categories, SumOf(categories), bySbu, complete.PercentComplete, complete.Incomplete.Count, pending);
    }

    /// <summary>Category subtotals, each the sum of its register rows.</summary>
    public static List<CategoryLineDto> ByCategory(IReadOnlyCollection<AssetValuation> rows) =>
        rows.GroupBy(r => (r.CategoryId, r.CategoryName))
            .OrderBy(g => g.Key.CategoryName, StringComparer.Ordinal)
            .Select(g => new CategoryLineDto(g.Key.CategoryId, g.Key.CategoryName, Totals(ValuationTotals.Of(g.ToList()))))
            .ToList();

    /// <summary>The grand total as the sum of the category subtotals.</summary>
    public static TotalsDto SumOf(IReadOnlyCollection<CategoryLineDto> lines) => new()
    {
        Count = lines.Sum(l => l.Totals.Count),
        OriginalCost = lines.Sum(l => l.Totals.OriginalCost),
        UpgradesInPeriod = lines.Sum(l => l.Totals.UpgradesInPeriod),
        CapitalisedUpgrades = lines.Sum(l => l.Totals.CapitalisedUpgrades),
        TotalCost = lines.Sum(l => l.Totals.TotalCost),
        DepreciationMonth = lines.Sum(l => l.Totals.DepreciationMonth),
        DepreciationYtd = lines.Sum(l => l.Totals.DepreciationYtd),
        AccumulatedDepreciation = lines.Sum(l => l.Totals.AccumulatedDepreciation),
        NetBookValue = lines.Sum(l => l.Totals.NetBookValue),
        Additions = lines.Sum(l => l.Totals.Additions),
    };

    public static TotalsDto Totals(ValuationTotals t) => new()
    {
        Count = t.Count, OriginalCost = t.OriginalCost, UpgradesInPeriod = t.UpgradesInPeriod, CapitalisedUpgrades = t.CapitalisedUpgrades,
        TotalCost = t.TotalCost, DepreciationMonth = t.DepreciationMonth, DepreciationYtd = t.DepreciationYtd,
        AccumulatedDepreciation = t.AccumulatedDepreciation, NetBookValue = t.NetBookValue, Additions = t.Additions,
    };

    private static RegisterRowDto ToRow(AssetValuation v) => new()
    {
        AssetId = v.AssetId, AssetNumber = v.AssetNumber, Description = v.Description, SerialNumber = v.SerialNumber,
        CategoryId = v.CategoryId, CategoryName = v.CategoryName, SbuCode = v.SbuCode, LocationName = v.LocationName,
        Custodian = v.Custodian, Status = v.Status, Capitalised = v.Capitalised, InUseDate = v.InUseDate,
        OriginalCost = v.OriginalCost, UpgradesInPeriod = v.UpgradesYtd, CapitalisedUpgrades = v.CapitalisedUpgrades,
        TotalCost = v.TotalCost, DepreciationMonth = v.DepreciationMonth, DepreciationYtd = v.DepreciationYtd,
        AccumulatedDepreciation = v.AccumulatedDepreciation, NetBookValue = v.NetBookValue,
    };

    // ── exports ────────────────────────────────────────────────────────────────

    private static readonly ExportColumn[] MoneyColumns =
    [
        new("Original cost", Money: true), new("Upgrades in period", Money: true), new("Total cost", Money: true),
        new("Depr. this month", Money: true), new("Depr. YTD", Money: true), new("Accumulated depr.", Money: true), new("NBV", Money: true),
    ];

    private static object?[] MoneyCells(TotalsDto t) =>
        [t.OriginalCost, t.UpgradesInPeriod, t.TotalCost, t.DepreciationMonth, t.DepreciationYtd, t.AccumulatedDepreciation, t.NetBookValue];

    /// <summary>AU-09 export: per category, its assets, then the category subtotal; the grand total last.</summary>
    public async Task<ExportTable> CategoryReportTableAsync(AuthUser user, ReportQuery query, CancellationToken ct)
    {
        var rows = await valuations.ValueAsync(user, query.Filter, query.Period, ct);
        var categories = ByCategory(rows);
        var table = new List<ExportRow>();
        foreach (var category in categories)
        {
            foreach (var v in rows.Where(r => r.CategoryId == category.CategoryId))
                table.Add(new ExportRow([category.CategoryName, v.AssetNumber ?? "pending", v.Description, v.SbuCode, 1, .. MoneyCells(Totals(ValuationTotals.Of([v])))]));
            table.Add(new ExportRow([$"{category.CategoryName} subtotal", null, null, null, category.Totals.Count, .. MoneyCells(category.Totals)], Bold: true));
        }
        var total = SumOf(categories);
        table.Add(new ExportRow(["Grand total", null, null, null, total.Count, .. MoneyCells(total)], Bold: true));
        return new ExportTable($"Assets by category — {query.Period}", await SubtitleAsync(user, query, ct),
            [new("Category"), new("Asset no."), new("Description"), new("SBU"), new("Count", Number: true), .. MoneyColumns], table);
    }

    /// <summary>SF-02 export: the register for the filters, with its total.</summary>
    public async Task<ExportTable> RegisterTableAsync(AuthUser user, ReportQuery query, CancellationToken ct)
    {
        var rows = await RegisterRowsAsync(user, query, ct);
        var table = rows.Select(v => new ExportRow([v.AssetNumber ?? "pending", v.Description, v.SerialNumber, v.CategoryName, v.SbuCode, v.LocationName, v.Custodian, v.Status,
            .. MoneyCells(Totals(ValuationTotals.Of([v])))])).ToList();
        var total = Totals(ValuationTotals.Of(rows));
        table.Add(new ExportRow(["Total", $"{total.Count} assets", null, null, null, null, null, null, .. MoneyCells(total)], Bold: true));
        return new ExportTable($"Fixed asset register — {query.Period}", await SubtitleAsync(user, query, ct),
            [new("Asset no."), new("Description"), new("Serial"), new("Category"), new("SBU"), new("Location"), new("Custodian"), new("Status"), .. MoneyColumns], table);
    }

    /// <summary>AU-05: the monthly schedule of one asset.</summary>
    public static ExportTable ScheduleTable(AssetCardDto card) => new(
        $"Depreciation schedule — {card.Asset.AssetNumber ?? $"asset {card.Asset.Id}"}",
        [card.Asset.Name, $"{card.Value!.DepreciationMethod} · residual {TableExport.Format(card.Value.ResidualValue, true)}"],
        [new("Month"), new("Opening cost", Money: true), new("Opening accumulated", Money: true), new("Charge", Money: true), new("Adjustment", Money: true),
         new("Closing accumulated", Money: true), new("NBV", Money: true), new("Locked")],
        card.Schedule.OrderBy(l => l.Period, StringComparer.Ordinal)
            .Select(l => new ExportRow([l.Period, l.OpeningCost, l.OpeningAccumulated, l.Charge, l.Adjustment, l.ClosingAccumulated, l.ClosingNbv, l.Locked]))
            .ToList());

    /// <summary>SF-02 (Phase 4d): the register rows, also filtered by department and status.</summary>
    private async Task<List<AssetValuation>> RegisterRowsAsync(AuthUser user, ReportQuery query, CancellationToken ct)
    {
        var rows = await valuations.ValueAsync(user, query.Filter, query.Period, ct);
        if (query.DepartmentId is null && query.Status is null) return rows.ToList();
        var ids = rows.Select(r => r.AssetId).ToList();
        var matching = db.Assets.IgnoreQueryFilters().Where(a => ids.Contains(a.Id));
        if (query.DepartmentId is { } d) matching = matching.Where(a => a.DepartmentId == d);
        if (query.Status is { } st) matching = matching.Where(a => a.Status == st);
        var keep = (await matching.Select(a => a.Id).ToListAsync(ct)).ToHashSet();
        return rows.Where(r => keep.Contains(r.AssetId)).ToList();
    }

    // ── GF-06 standard reports (Phase 4d) ────────────────────────────────────────

    public static readonly IReadOnlyList<string> StandardReports = ["register", "additions", "disposals", "depreciation", "movements"];

    /// <summary>
    /// GF-06: the fixed asset register, additions and disposals (January to the period), the depreciation of the period
    /// with year-to-date and accumulated figures, and the movements of the period's month; by SBU and period, with totals.
    /// </summary>
    public async Task<ExportTable> StandardReportTableAsync(AuthUser user, string kind, ReportQuery query, CancellationToken ct)
    {
        if (kind == "register") return await RegisterTableAsync(user, query, ct);
        var subtitle = await SubtitleAsync(user, query, ct);
        var yearStart = $"{query.Period.Year:D4}-01-01";
        var periodEnd = $"{query.Period.Year:D4}-{query.Period.Month:D2}-{DateTime.DaysInMonth(query.Period.Year, query.Period.Month):D2}";
        switch (kind)
        {
            case "additions":
            {
                var rows = (await valuations.ValueAsync(user, query.Filter, query.Period, ct))
                    .Where(r => r.AdditionThisYear && string.CompareOrdinal(r.InUseDate, periodEnd) <= 0).OrderBy(r => r.InUseDate, StringComparer.Ordinal).ToList();
                var table = rows.Select(v => new ExportRow([v.InUseDate, v.AssetNumber ?? "pending", v.Description, v.CategoryName, v.SbuCode, v.OriginalCost, v.NetBookValue])).ToList();
                table.Add(new ExportRow(["Total", $"{rows.Count} assets", null, null, null, rows.Sum(r => r.OriginalCost), rows.Sum(r => r.NetBookValue)], Bold: true));
                return new ExportTable($"Additions — January to {query.Period}", subtitle,
                    [new("In use from"), new("Asset no."), new("Description"), new("Category"), new("SBU"), new("Cost", Money: true), new("NBV", Money: true)], table);
            }
            case "disposals":
            {
                var disposals = from d in db.AssetDisposals.AsNoTracking()
                                join a in db.Assets.IgnoreQueryFilters() on d.AssetId equals a.Id
                                where d.Status == "completed" && d.DisposalDate != null
                                      && string.Compare(d.DisposalDate, yearStart) >= 0 && string.Compare(d.DisposalDate, periodEnd) <= 0
                                select new { d, a };
                if (Roles.IsSbuScoped(user.Role)) disposals = disposals.Where(x => x.a.SbuId == user.SbuId);
                if (query.SbuId is { } sbuId) disposals = disposals.Where(x => x.a.SbuId == sbuId);
                if (query.CategoryId is { } categoryId) disposals = disposals.Where(x => x.a.CategoryId == categoryId);
                var rows = await disposals.OrderBy(x => x.d.DisposalDate)
                    .Select(x => new { x.d.DisposalDate, x.a.AssetNumber, x.a.Name, Sbu = x.a.Sbu!.Code, x.d.Method, x.d.Proceeds, x.d.NbvAtDisposal, x.d.GainLoss })
                    .ToListAsync(ct);
                var table = rows.Select(r => new ExportRow([r.DisposalDate, r.AssetNumber, r.Name, r.Sbu, r.Method, r.Proceeds, r.NbvAtDisposal, r.GainLoss])).ToList();
                table.Add(new ExportRow(["Total", $"{rows.Count} disposals", null, null, null, rows.Sum(r => r.Proceeds ?? 0), rows.Sum(r => r.NbvAtDisposal ?? 0), rows.Sum(r => r.GainLoss ?? 0)], Bold: true));
                return new ExportTable($"Disposals — January to {query.Period}", subtitle,
                    [new("Date"), new("Asset no."), new("Description"), new("SBU"), new("Method"), new("Proceeds", Money: true), new("NBV at disposal", Money: true), new("Gain / (loss)", Money: true)], table);
            }
            case "depreciation":
            {
                var rows = await valuations.ValueAsync(user, query.Filter, query.Period, ct);
                var categories = ByCategory(rows);
                var table = new List<ExportRow>();
                foreach (var category in categories)
                {
                    foreach (var v in rows.Where(r => r.CategoryId == category.CategoryId))
                        table.Add(new ExportRow([category.CategoryName, v.AssetNumber ?? "pending", v.Description, v.SbuCode, v.TotalCost, v.DepreciationMonth, v.DepreciationYtd, v.AccumulatedDepreciation, v.NetBookValue]));
                    var t = category.Totals;
                    table.Add(new ExportRow([$"{category.CategoryName} subtotal", null, null, null, t.TotalCost, t.DepreciationMonth, t.DepreciationYtd, t.AccumulatedDepreciation, t.NetBookValue], Bold: true));
                }
                var total = SumOf(categories);
                table.Add(new ExportRow(["Grand total", null, null, null, total.TotalCost, total.DepreciationMonth, total.DepreciationYtd, total.AccumulatedDepreciation, total.NetBookValue], Bold: true));
                return new ExportTable($"Depreciation — {query.Period}", subtitle,
                    [new("Category"), new("Asset no."), new("Description"), new("SBU"), new("Total cost", Money: true), new("Charge this month", Money: true),
                     new("Charge YTD", Money: true), new("Accumulated", Money: true), new("NBV", Money: true)], table);
            }
            default:
            {
                var monthStart = new DateTime(query.Period.Year, query.Period.Month, 1, 0, 0, 0, DateTimeKind.Utc);
                var monthEnd = monthStart.AddMonths(1);
                var moves = from m in db.Movements.IgnoreQueryFilters().AsNoTracking()
                            join a in db.Assets.IgnoreQueryFilters() on m.AssetId equals a.Id
                            where m.Timestamp >= monthStart && m.Timestamp < monthEnd
                            select new { m, a };
                if (Roles.IsSbuScoped(user.Role)) moves = moves.Where(x => x.a.SbuId == user.SbuId);
                if (query.SbuId is { } sbuId) moves = moves.Where(x => x.a.SbuId == sbuId);
                if (query.CategoryId is { } categoryId) moves = moves.Where(x => x.a.CategoryId == categoryId);
                if (query.LocationId is { } locationId) moves = moves.Where(x => x.m.FromLocationId == locationId || x.m.ToLocationId == locationId);
                var names = await db.Locations.AsNoTracking().ToDictionaryAsync(l => l.Id, l => l.Name, ct);
                var sbus = await db.Sbus.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Code, ct);
                var rows = await moves.OrderBy(x => x.m.Timestamp)
                    .Select(x => new { x.m.Timestamp, x.a.AssetNumber, x.a.Name, x.a.SbuId, x.m.FromLocationId, x.m.ToLocationId, x.m.TriggeredBy, x.m.MovedBy })
                    .ToListAsync(ct);
                string Where(int? id) => id is { } i && names.TryGetValue(i, out var n) ? n : "off premises";
                var table = rows.Select(r => new ExportRow([r.Timestamp.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture), r.AssetNumber, r.Name,
                    r.SbuId is { } si ? sbus.GetValueOrDefault(si) : null, Where(r.FromLocationId), Where(r.ToLocationId), r.TriggeredBy, r.MovedBy])).ToList();
                table.Add(new ExportRow(["Total", $"{rows.Count} movements", null, null, null, null, null, null], Bold: true));
                return new ExportTable($"Movements — {query.Period}", subtitle,
                    [new("When (UTC)"), new("Asset no."), new("Description"), new("SBU"), new("From"), new("To"), new("Trigger"), new("By")], table);
            }
        }
    }

    private async Task<List<string>> SubtitleAsync(AuthUser user, ReportQuery query, CancellationToken ct)
    {
        var sbu = query.SbuId is { } s ? await db.Sbus.AsNoTracking().Where(x => x.Id == s).Select(x => x.Code).FirstOrDefaultAsync(ct) : null;
        var location = query.LocationId is { } l ? await db.Locations.AsNoTracking().Where(x => x.Id == l).Select(x => x.Name).FirstOrDefaultAsync(ct) : null;
        var category = query.CategoryId is { } c ? await db.AssetCategories.AsNoTracking().Where(x => x.Id == c).Select(x => x.Name).FirstOrDefaultAsync(ct) : null;
        return
        [
            $"Period: {query.Period} · SBU: {sbu ?? "All in scope"} · Location: {location ?? "All"} · Category: {category ?? "All"}",
            $"Amounts in USD · prepared for {user.Name} ({Roles.DisplayName(user.Role)}) · upgrades in period = capitalised January–{query.Period}",
        ];
    }

    // ── approvals inbox (Phase 1: upgrades and "No serial" reasons) ─────────────

    /// <summary>Everything waiting on <paramref name="user"/>: only SBU Finance decides these (AU-02, AU-07).</summary>
    public async Task<List<ApprovalItemDto>> ApprovalsAsync(AuthUser user, CancellationToken ct)
    {
        if (user.Role != Roles.SbuFinance || user.SbuId is null) return [];
        var sbu = user.SbuId;
        var upgrades = await db.AssetUpgrades.AsNoTracking()
            .Where(u => u.Status == UpgradeStatuses.Pending && u.Asset.SbuId == sbu)
            .Select(u => new ApprovalItemDto($"upgrade-{u.Id}", "upgrade", u.AssetId, u.Asset.AssetNumber, u.Asset.Name, u.Asset.Sbu!.Code,
                u.Description + ": " + u.BeforeText + " → " + u.AfterText, u.CreatedAt, u.LoggedBy, u.Id, null, null, null, null, false))
            .ToListAsync(ct);
        var noSerial = await db.Assets.AsNoTracking()
            .Where(a => a.SbuId == sbu && a.NoSerialStatus == NoSerialStatuses.Pending)
            .Select(a => new ApprovalItemDto($"no-serial-{a.Id}", "no_serial", a.Id, a.AssetNumber, a.Name, a.Sbu!.Code,
                "\"No serial\": " + a.NoSerialReason, a.CreatedAt, "", null, null, null, null, null, false))
            .ToListAsync(ct);
        return upgrades.Concat(noSerial).OrderBy(x => x.RaisedAt).ToList();
    }
}
