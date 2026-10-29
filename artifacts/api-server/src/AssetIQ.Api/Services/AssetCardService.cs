using System.Globalization;
using System.Text.Json;
using AssetIQ.Api.Auth;
using AssetIQ.Api.Contracts;
using AssetIQ.Api.Data;
using AssetIQ.Api.Domain;
using AssetIQ.Api.Infrastructure;
using AssetIQ.Api.Services.Depreciation;
using AssetIQ.Api.Services.Templates;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace AssetIQ.Api.Services;

/// <summary>
/// The one-page asset card (AU-08): description, serial, asset number, cost broken into original cost and
/// capitalised upgrades, monthly and accumulated depreciation, NBV, purchase details, upgrades, the monthly
/// schedule and the dated history (AU-06). Opened by id, asset-number search or tag scan; exports to PDF.
/// </summary>
public sealed class AssetCardService(
    AssetIqDbContext db, AssetQueries queries, ValuationService valuations, DepreciationService depreciation, CompletenessService completeness)
{
    /// <summary>Finds an asset by asset number, previous number, RFID tag or serial (exact, case-insensitive).</summary>
    public async Task<int> LookupAsync(AuthUser user, string code, CancellationToken ct)
    {
        var text = code.Trim().ToUpperInvariant();
        if (text.Length == 0) throw new ApiException(StatusCodes.Status400BadRequest, "code: Required");
        var found = await db.Assets.IgnoreQueryFilters().AsNoTracking().Include(a => a.AssetCategory)
            .Where(a => a.AssetNumber!.ToUpper() == text || a.RfidTag!.ToUpper() == text || a.PreviousAssetNumber!.ToUpper() == text || a.SerialNumber == text)
            .FirstOrDefaultAsync(ct) ?? throw new ApiException(StatusCodes.Status404NotFound, $"No asset has the number, tag or serial {code.Trim()}");
        if (!AccessPolicy.CanView(user, found, found.AssetCategory.IsIt)) throw AccessPolicy.Deny(user, "view this asset");
        return found.Id;
    }

    public async Task<AssetCardDto> GetAsync(AuthUser user, int assetId, CancellationToken ct)
    {
        var asset = await db.Assets.IgnoreQueryFilters().AsNoTracking()
            .Include(a => a.AssetCategory).ThenInclude(c => c.Fields)
            .Include(a => a.Attributes)
            .FirstOrDefaultAsync(a => a.Id == assetId, ct) ?? throw new ApiException(StatusCodes.Status404NotFound, "Asset not found");
        var category = asset.AssetCategory;
        if (!AccessPolicy.CanView(user, asset, category.IsIt)) throw AccessPolicy.Deny(user, "view this asset");
        var template = Template.From(category);
        var period = depreciation.CurrentPeriod;

        var dto = (await queries.GetDetailedAsync(assetId, ct))!;
        var value = (await valuations.ValueAsync(user, new ValuationFilter(AssetId: assetId), period, ct)).FirstOrDefault();
        var schedule = (await depreciation.SchedulesAsync([assetId], period, ct)).GetValueOrDefault(assetId) ?? [];
        var locked = asset.SbuId is { } sbu && (await depreciation.LockedThroughBySbuAsync(ct)).TryGetValue(sbu, out var l) ? l : (Period?)null;
        var upgrades = await db.AssetUpgrades.AsNoTracking().Where(u => u.AssetId == assetId).OrderByDescending(u => u.UpgradeDate).ThenByDescending(u => u.Id).ToListAsync(ct);
        var gaps = (await completeness.ReportAsync(user, asset.SbuId, asset.CategoryId, ct)).Incomplete
            .FirstOrDefault(r => r.AssetId == assetId)?.Gaps.Select(g => g.Message).ToList() ?? [];

        var attributes = new List<AssetAttributeDto>();
        foreach (var field in template.Fields)
        {
            string? text = field.Key == template.IdentifierFieldKey ? asset.SerialNumber
                : field.Key == Template.MacAddressKey ? asset.MacAddress
                : asset.Attributes.FirstOrDefault(a => a.FieldId == field.Id)?.ValueText;
            if (text is not null) attributes.Add(new AssetAttributeDto(field.Key, field.Label, text, field.Unit));
        }

        return new AssetCardDto
        {
            Asset = dto,
            CategoryName = category.Name,
            IsIt = category.IsIt,
            Attributes = attributes,
            Value = new AssetValueDto
            {
                Period = period.ToString(),
                Capitalised = asset.CapitalisedAt is not null,
                OriginalCostInCurrency = asset.OriginalCost,
                Currency = asset.Currency,
                ExchangeRate = asset.ExchangeRate,
                OriginalCost = value?.OriginalCost ?? 0m,
                CapitalisedUpgrades = value?.CapitalisedUpgrades ?? 0m,
                TotalCost = value?.TotalCost ?? 0m,
                DepreciationMonth = value?.DepreciationMonth ?? 0m,
                AccumulatedDepreciation = value?.AccumulatedDepreciation ?? 0m,
                NetBookValue = value?.NetBookValue ?? 0m,
                ResidualValue = asset.ResidualValue,
                DepreciationMethod = category.DepreciationMethod,
                UsefulLifeMonths = category.UsefulLifeMonths,
                AnnualRate = category.AnnualRate,
            },
            Upgrades = upgrades.Select(u => ToDto(u, asset.AssetNumber, asset.Name)).ToList(),
            Schedule = schedule.Reverse().Select(x => new DepreciationLineDto(x.Period.ToString(), x.OpeningCost, x.OpeningAccumulated, x.Charge, x.Adjustment,
                x.ClosingAccumulated, x.ClosingNbv, locked is { } lk && x.Period <= lk)).ToList(),
            Timeline = await TimelineAsync(asset, upgrades, ct),
            CompletenessGaps = gaps,
            Can = new AssetCardPermissionsDto(
                AccessPolicy.CanEditTechnical(user, category.IsIt, asset.SbuId),
                AccessPolicy.IsSbuFinanceOf(user, asset.SbuId),
                AccessPolicy.CanLogUpgrade(user, category.IsIt, asset.SbuId),
                AccessPolicy.IsSbuFinanceOf(user, asset.SbuId),
                AccessPolicy.IsSbuFinanceOf(user, asset.SbuId),
                asset.AssetNumber is not null && (AccessPolicy.CanEditTechnical(user, category.IsIt, asset.SbuId) || user.Role == Roles.Stores),
                AccessPolicy.CanAttach(user, asset, category.IsIt)),
        };
    }

    public static AssetUpgradeDto ToDto(AssetUpgrade u, string? assetNumber = null, string? assetDescription = null) => new()
    {
        Id = u.Id, AssetId = u.AssetId, AssetNumber = assetNumber, AssetDescription = assetDescription,
        UpgradeDate = u.UpgradeDate, Description = u.Description, BeforeText = u.BeforeText, AfterText = u.AfterText,
        FieldChanges = JsonSerializer.Deserialize<Dictionary<string, string?>>(u.FieldChanges) ?? [],
        Cost = u.Cost, Currency = u.Currency, InvoiceAmount = u.InvoiceAmount ?? u.Cost, ExchangeRate = u.ExchangeRate, Supplier = u.Supplier, InvoiceNo = u.InvoiceNo, LoggedBy = u.LoggedBy, LoggedByRole = u.LoggedByRole,
        Status = u.Status, DecidedBy = u.DecidedBy, DecidedAt = u.DecidedAt, DecisionComment = u.DecisionComment,
        CapitalisedPeriod = u.CapitalisedPeriod, RevisedLifeMonths = u.RevisedLifeMonths, CreatedAt = u.CreatedAt,
    };

    /// <summary>
    /// AU-06 / GI-04: purchase, capitalisation, upgrades, assignments, gate passes, repairs, transfers and the
    /// financial changes from the audit trail, newest first.
    /// </summary>
    private async Task<List<TimelineEventDto>> TimelineAsync(Asset asset, List<AssetUpgrade> upgrades, CancellationToken ct)
    {
        var events = new List<TimelineEventDto>();
        if (asset.PurchaseDate is not null)
            events.Add(new("purchase", Day(asset.PurchaseDate), "purchase", "Purchased",
                Join($"Supplier {asset.Supplier}", asset.InvoiceNo is null ? null : $"invoice {asset.InvoiceNo}", asset.PoNo is null ? null : $"PO {asset.PoNo}"), null));
        foreach (var u in upgrades)
        {
            events.Add(new($"upg-{u.Id}", u.CreatedAt, "upgrade", $"Upgrade logged: {u.Description}", $"{u.BeforeText} → {u.AfterText} · {TableExport.Format(u.Cost, true)}", u.LoggedBy));
            if (u.DecidedAt is { } decided)
                events.Add(new($"upgd-{u.Id}", decided, "upgrade", $"Upgrade {u.Status}: {u.Description}",
                    u.CapitalisedPeriod is null ? u.DecisionComment : Join($"from {u.CapitalisedPeriod}", u.DecisionComment), u.DecidedBy));
        }
        var lifecycle = await db.AssetLifecycleEvents.AsNoTracking().Where(e => e.AssetId == asset.Id).ToListAsync(ct);
        events.AddRange(lifecycle.Select(e => new TimelineEventDto($"lc-{e.Id}", e.Timestamp, e.EventType, LifecycleTitle(e.EventType),
            Join(e.AssignedTo is null ? null : $"to {e.AssignedTo}", e.Vendor, e.Notes), e.PerformedBy)));
        var passes = await db.GatePasses.AsNoTracking().Where(p => p.AssetId == asset.Id).ToListAsync(ct);
        events.AddRange(passes.Select(p => new TimelineEventDto($"gp-{p.Id}", p.DecidedAt ?? p.CreatedAt, "gate_pass", $"Gate pass #{p.Id} {p.Status}",
            Join(p.Reason, p.Destination), p.ApprovedBy ?? p.RequestedBy)));
        var audit = await db.AuditLog.AsNoTracking()
            .Where(e => e.AssetId == asset.Id && e.Action != "updated")
            .ToListAsync(ct);
        events.AddRange(audit.Where(e => !e.Action.StartsWith("upgrade_", StringComparison.Ordinal)).Select(e => new TimelineEventDto($"au-{e.Id}", e.At, "record", AuditTitle(e),
            Join(e.NewValue, e.Reason), e.UserName)));
        return events.OrderByDescending(e => e.At).ThenByDescending(e => e.Id, StringComparer.Ordinal).ToList();
    }

    private static string AuditTitle(AuditEntry e) => e.Action switch
    {
        "created" => "Registered",
        "asset_number_issued" => "Asset number issued by Business Central",
        "capitalised" => "Capitalised",
        "category_changed" => $"Category changed from {e.OldValue}",
        "duplicate_serial_attempt" => "Blocked: another registration used this serial",
        "duplicate_mac_attempt" => "Blocked: another registration used this MAC address",
        "no_serial_requested" => "Recorded as \"No serial\"",
        "no_serial_approved" => "\"No serial\" reason approved",
        "no_serial_rejected" => "\"No serial\" reason rejected",
        _ => e.Action.Replace('_', ' '),
    };

    private static string LifecycleTitle(string type) => type switch
    {
        LifecycleEventTypes.Deployment => "Assigned",
        LifecycleEventTypes.ReturnToIt => "Returned to IT",
        LifecycleEventTypes.RepairSent => "Sent for repair",
        LifecycleEventTypes.RepairReturned => "Back from repair",
        LifecycleEventTypes.SbuTransfer => "Transferred between SBUs",
        _ => "Note",
    };

    private static DateTime Day(string day) =>
        DateTime.SpecifyKind(DateTime.ParseExact(day[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture), DateTimeKind.Utc);

    private static string? Join(params string?[] parts)
    {
        var kept = parts.Where(p => !string.IsNullOrWhiteSpace(p) && p != "Supplier ").ToList();
        return kept.Count == 0 ? null : string.Join(" · ", kept);
    }

    // ── PDF ───────────────────────────────────────────────────────────────────

    public static byte[] Pdf(AssetCardDto card, string generatedFor, DateTime generatedAt)
    {
        var a = card.Asset;
        var v = card.Value;
        string Money(decimal value) => "$" + TableExport.Format(value, true);
        return Document.Create(document => document.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(32);
            page.DefaultTextStyle(t => t.FontSize(9));
            page.Header().Column(col =>
            {
                col.Item().Text($"Asset card — {a.AssetNumber ?? "asset number pending"}").FontSize(15).Bold();
                col.Item().Text(a.Name).FontSize(11);
                col.Item().Text($"{a.SbuCode} · {card.CategoryName} · {a.Status} · generated {generatedAt:dd MMM yyyy HH:mm} UTC for {generatedFor}").FontColor(Colors.Grey.Darken2);
            });
            page.Content().PaddingTop(10).Column(col =>
            {
                col.Spacing(10);
                col.Item().Element(c => Pairs(c, "Identity", [
                    ("Asset number", a.AssetNumber ?? "Pending (Business Central)"), ("Previous number", a.PreviousAssetNumber ?? "—"),
                    ("Serial number", a.SerialNumber ?? $"No serial ({a.NoSerialStatus}): {a.NoSerialReason}"), ("RFID / QR tag", a.RfidTag ?? "Not tagged"),
                    ("Custodian", a.AssignedTo ?? "—"), ("Location", a.CurrentLocationName ?? "—"), ("Condition", a.Condition ?? "—"),
                ]));
                col.Item().Element(c => Pairs(c, "Specification", card.Attributes.Select(x => (x.Label, x.Unit is null ? x.Value : $"{x.Value} {x.Unit}")).ToList()));
                // Without financial access the card carries no value (FinancialRedaction): the section is left out.
                if (v is not null) col.Item().Element(c => Pairs(c, $"Cost & value ({v.Period})", [
                    ("Original cost", v.Capitalised ? Money(v.OriginalCost) : "Not capitalised"),
                    ("Currency / rate used", $"{v.Currency ?? "—"} · {TableExport.Format(v.ExchangeRate)}"),
                    ("Capitalised upgrades", Money(v.CapitalisedUpgrades)), ("Total cost", Money(v.TotalCost)),
                    ("Depreciation this month", Money(v.DepreciationMonth)), ("Accumulated depreciation", Money(v.AccumulatedDepreciation)),
                    ("Net book value", Money(v.NetBookValue)),
                    ("Method / life", v.DepreciationMethod == DepreciationMethods.StraightLine ? $"Straight-line · {v.UsefulLifeMonths} months" : $"Reducing balance · {TableExport.Format(v.AnnualRate * 100)}% a year"),
                ]));
                col.Item().Element(c => Pairs(c, "Purchase details", [
                    ("Supplier", a.Supplier ?? "—"), ("Invoice no.", a.InvoiceNo ?? "—"), ("PO no.", a.PoNo ?? "—"),
                    ("Purchase date", a.PurchaseDate ?? "—"), ("In-use date", a.InUseDate ?? "—"), ("Warranty ends", a.WarrantyEnds ?? "—"),
                ]));
                col.Item().Text("Upgrades").Bold().FontSize(11);
                col.Item().Table(t =>
                {
                    t.ColumnsDefinition(cols => { cols.RelativeColumn(); cols.RelativeColumn(2); cols.RelativeColumn(2); cols.RelativeColumn(); cols.RelativeColumn(2); });
                    foreach (var h in new[] { "Date", "Upgrade", "Before → after", "Cost", "Finance decision" }) t.Cell().Background(Colors.Grey.Lighten2).Padding(3).Text(h).Bold();
                    foreach (var u in card.Upgrades)
                    {
                        t.Cell().Padding(3).Text(u.UpgradeDate);
                        t.Cell().Padding(3).Text(u.Description);
                        t.Cell().Padding(3).Text($"{u.BeforeText} → {u.AfterText}");
                        t.Cell().Padding(3).AlignRight().Text(u.Cost is { } cost ? Money(cost) : "—");
                        t.Cell().Padding(3).Text(u.CapitalisedPeriod is null ? u.Status : $"{u.Status} from {u.CapitalisedPeriod}");
                    }
                });
                if (v is not null) col.Item().Text("Depreciation (latest 12 months)").Bold().FontSize(11);
                if (v is not null) col.Item().Table(t =>
                {
                    t.ColumnsDefinition(cols => { for (var i = 0; i < 5; i++) cols.RelativeColumn(); });
                    foreach (var h in new[] { "Month", "Opening cost", "Charge", "Accumulated", "NBV" }) t.Cell().Background(Colors.Grey.Lighten2).Padding(3).Text(h).Bold();
                    foreach (var line in card.Schedule.Take(12))
                    {
                        t.Cell().Padding(3).Text(line.Period);
                        t.Cell().Padding(3).AlignRight().Text(Money(line.OpeningCost));
                        t.Cell().Padding(3).AlignRight().Text(Money(line.Charge + line.Adjustment));
                        t.Cell().Padding(3).AlignRight().Text(Money(line.ClosingAccumulated));
                        t.Cell().Padding(3).AlignRight().Text(Money(line.ClosingNbv));
                    }
                });
                col.Item().Text("History").Bold().FontSize(11);
                foreach (var e in card.Timeline.Take(25))
                    col.Item().Text($"{e.At:dd MMM yyyy} · {e.Title}{(e.Detail is null ? "" : $" — {e.Detail}")}{(e.By is null ? "" : $" ({e.By})")}");
            });
            page.Footer().AlignRight().Text(x => { x.Span("Page "); x.CurrentPageNumber(); x.Span(" of "); x.TotalPages(); });
        })).GeneratePdf();
    }

    private static void Pairs(IContainer container, string title, IReadOnlyList<(string Key, string Value)> pairs) => container.Column(col =>
    {
        col.Item().Text(title).Bold().FontSize(11);
        col.Item().Table(t =>
        {
            t.ColumnsDefinition(cols => { cols.RelativeColumn(); cols.RelativeColumn(2); cols.RelativeColumn(); cols.RelativeColumn(2); });
            foreach (var (key, value) in pairs)
            {
                t.Cell().Padding(2).Text(key).FontColor(Colors.Grey.Darken2);
                t.Cell().Padding(2).Text(value);
            }
        });
    });
}
