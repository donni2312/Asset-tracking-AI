using System.Globalization;
using System.Text.RegularExpressions;
using AssetIQ.Api.Auth;
using AssetIQ.Api.Contracts;
using AssetIQ.Api.Data;
using AssetIQ.Api.Domain;
using AssetIQ.Api.Infrastructure;
using AssetIQ.Api.Services.Templates;
using Microsoft.EntityFrameworkCore;

namespace AssetIQ.Api.Services;

/// <summary>
/// Phase 4d, SF-10: bulk import of existing asset spreadsheets. Every row is checked as a registration would be
/// (category template, required fields, Group-unique serial / MAC / tag, also against the rows above it in the file);
/// the validation report lists every problem before anything is saved, and the import runs only when there is none
/// (all rows or nothing). Imported assets keep their previous asset number and receive a new Business Central number.
/// </summary>
public sealed partial class AssetRegistrationService
{
    /// <summary>The standard columns of the template; any other column is a template field key (make, model, serial_number …).</summary>
    public static readonly IReadOnlyList<(string Column, string Meaning)> ImportColumns =
    [
        ("category", "Category code (e.g. COMP, MV, FF) — required"),
        ("sub_category", "One of the category's types (e.g. Laptop)"),
        ("sbu", "SBU code; defaults to your SBU"),
        ("location", "Location name in that SBU — required"),
        ("custodian", "Who holds it (free text); blank = the SBU's IT store / Stores"),
        ("custodian_employee_no", "Employee number of the holder (optional)"),
        ("condition", "new, good, fair, poor or damaged (default good)"),
        ("previous_asset_number", "The number in the old register (kept, AU-03)"),
        ("rfid_tag", "The existing RFID tag, if any"),
        ("no_serial_reason", "Why there is no serial (instead of a serial)"),
        ("supplier", "Supplier"),
        ("po_no", "Purchase order number"),
        ("invoice_no", "Invoice number"),
        ("purchase_date", "YYYY-MM-DD"),
        ("in_use_date", "YYYY-MM-DD (depreciation starts in this month)"),
        ("warranty_ends", "YYYY-MM-DD"),
        ("cost", "Original cost in the invoice currency"),
        ("currency", "3-letter code (default USD)"),
        ("exchange_rate", "Currency → USD; required when not USD"),
        ("cost_centre", "Cost centre (SF-01)"),
        ("notes", "Free text"),
    ];

    private static readonly HashSet<string> StandardColumns = ImportColumns.Select(c => c.Column).ToHashSet();

    /// <summary>SF-10: who imports. SBU Finance for its SBU, Group Finance for any.</summary>
    // ASSUMPTION: the import is Finance's (SF-10) and also loads non-IT assets, although AM-01 reserves creating
    // non-IT records to the Admin Manager; listed under Questions. Editing afterwards follows the usual rules.
    public static bool CanImport(AuthUser user, int sbuId) =>
        AccessPolicy.Sys(user) || user.Role == Roles.GroupFinance || user.Role == Roles.SbuFinance && AccessPolicy.OwnSbu(user, sbuId);

    public async Task<ImportReportDto> ImportAsync(AuthUser user, IReadOnlyList<SheetRow> rows, bool commit, CancellationToken ct)
    {
        if (rows.Count == 0) throw new ApiException(StatusCodes.Status400BadRequest, "file: no rows to import");
        var categories = (await db.AssetCategories.AsNoTracking().Include(c => c.Fields).Where(c => c.Active).ToListAsync(ct))
            .Select(Template.From).ToList();
        var sbus = await db.Sbus.AsNoTracking().ToListAsync(ct);
        var locations = await db.Locations.AsNoTracking().ToListAsync(ct);
        var seenSerials = new Dictionary<string, int>();
        var seenMacs = new Dictionary<string, int>();
        var seenTags = new Dictionary<string, int>();
        var report = new List<ImportRowDto>();
        var ready = new List<(Asset Asset, int Row)>();

        foreach (var row in rows)
        {
            var errors = new List<string>();
            var unknown = row.Cells.Keys.Where(k => !StandardColumns.Contains(k)).ToList();

            var template = row["category"] is { } code
                ? categories.FirstOrDefault(c => string.Equals(c.Code, code, StringComparison.OrdinalIgnoreCase) || string.Equals(c.Name, code, StringComparison.OrdinalIgnoreCase))
                : null;
            if (row["category"] is null) errors.Add("category: required");
            else if (template is null) errors.Add($"category: no category \"{row["category"]}\"");

            var sbu = row["sbu"] is { } sbuCode ? sbus.FirstOrDefault(s => string.Equals(s.Code, sbuCode, StringComparison.OrdinalIgnoreCase))
                : sbus.FirstOrDefault(s => s.Id == user.SbuId);
            if (sbu is null) errors.Add(row["sbu"] is null ? "sbu: required" : $"sbu: no SBU \"{row["sbu"]}\"");
            else if (!CanImport(user, sbu.Id)) errors.Add($"sbu: you may not import into {sbu.Code}");

            var location = sbu is null || row["location"] is null ? null
                : locations.FirstOrDefault(l => l.SbuId == sbu.Id && string.Equals(l.Name, row["location"], StringComparison.OrdinalIgnoreCase));
            if (row["location"] is null) errors.Add("location: required");
            else if (sbu is not null && location is null) errors.Add($"location: no location \"{row["location"]}\" in {sbu.Code}");

            var condition = row["condition"]?.ToLowerInvariant() ?? "good";
            if (!AssetConditions.All.Contains(condition)) errors.Add($"condition: one of {string.Join(", ", AssetConditions.All)}");
            string? Day(string column)
            {
                var v = row[column];
                if (v is null) return null;
                if (DateOnly.TryParseExact(v, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) return v;
                errors.Add($"{column}: a date YYYY-MM-DD");
                return null;
            }
            decimal? Number(string column, bool positive)
            {
                var v = row[column];
                if (v is null) return null;
                if (decimal.TryParse(v, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) && (positive ? d > 0 : d >= 0)) return d;
                errors.Add($"{column}: {(positive ? "a positive" : "a")} number");
                return null;
            }
            var purchaseDate = Day("purchase_date");
            var inUseDate = Day("in_use_date");
            var warrantyEnds = Day("warranty_ends");
            var cost = Number("cost", positive: false);
            var currency = (row["currency"] ?? "USD").ToUpperInvariant();
            if (!CurrencyCode().IsMatch(currency)) errors.Add("currency: a 3-letter code");
            var rate = Number("exchange_rate", positive: true) ?? (currency == "USD" ? 1m : null);
            if (cost is not null && rate is null) errors.Add("exchange_rate: required when the currency is not USD");

            int? employeeId = null;
            string? holderName = null;
            if (row["custodian_employee_no"] is { } employeeNo && sbu is not null)
            {
                var employee = await db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.EmployeeNo == employeeNo && e.SbuId == sbu.Id && e.Active, ct);
                if (employee is null) errors.Add($"custodian_employee_no: no active employee {employeeNo} in {sbu.Code}");
                else (employeeId, holderName) = (employee.Id, employee.Name);
            }

            string? description = null;
            Asset? asset = null;
            if (template is not null)
            {
                var attributes = unknown.ToDictionary(k => k, k => row[k]);
                foreach (var key in unknown.Where(k => template.Field(k) is null)) errors.Add($"{key}: not a field of {template.Name}");
                var known = attributes.Where(a => template.Field(a.Key) is not null).ToDictionary(a => a.Key, a => a.Value);
                var noSerialReason = row["no_serial_reason"];
                var validation = TemplateRules.Validate(template, row["sub_category"], known, !string.IsNullOrWhiteSpace(noSerialReason));
                errors.AddRange(validation.Errors);
                var identity = ReadIdentity(template, validation, !string.IsNullOrWhiteSpace(noSerialReason), noSerialReason, errors);
                var tag = row["rfid_tag"]?.ToUpperInvariant();

                // SF-10: duplicates by serial or tag (and MAC), in the Group and among the rows above.
                if (identity.Serial is { } serial)
                {
                    if (seenSerials.TryGetValue(serial, out var other)) errors.Add($"serial {serial}: also on row {other}");
                    else seenSerials[serial] = row.RowNumber;
                    var holder = await db.Assets.IgnoreQueryFilters().AsNoTracking().Where(a => a.SerialNumber == serial).Select(a => a.AssetNumber ?? $"asset #{a.Id}").FirstOrDefaultAsync(ct);
                    if (holder is not null) errors.Add($"serial {serial}: already registered ({holder})");
                }
                if (identity.Mac is { } mac)
                {
                    if (seenMacs.TryGetValue(mac, out var other)) errors.Add($"MAC {mac}: also on row {other}");
                    else seenMacs[mac] = row.RowNumber;
                    if (await db.Assets.IgnoreQueryFilters().AnyAsync(a => a.MacAddress == mac, ct)) errors.Add($"MAC {mac}: already registered");
                }
                if (tag is not null)
                {
                    if (seenTags.TryGetValue(tag, out var other)) errors.Add($"rfid_tag {tag}: also on row {other}");
                    else seenTags[tag] = row.RowNumber;
                    if (await db.Assets.IgnoreQueryFilters().AnyAsync(a => a.RfidTag == tag, ct)) errors.Add($"rfid_tag {tag}: already used");
                }

                if (validation.IsValid) description = TemplateRules.BuildDescription(template, row["sub_category"], validation.Values);
                if (errors.Count == 0 && sbu is not null && location is not null)
                {
                    var now = clock.UtcNow;
                    asset = new Asset
                    {
                        Name = description!, Category = row["sub_category"] ?? template.Name, CategoryId = template.CategoryId, SubCategory = row["sub_category"],
                        SerialNumber = identity.Serial, MacAddress = identity.Mac,
                        NoSerialReason = identity.Serial is null ? noSerialReason : null, NoSerialStatus = identity.Serial is null ? NoSerialStatuses.Pending : null,
                        // ASSUMPTION: an imported item keeps the RFID tag it already carries (the tag-after-number order applies to new registrations).
                        RfidTag = tag,
                        Status = AssetStatuses.PendingAssetNumber, Condition = condition,
                        AssignedTo = holderName ?? row["custodian"] ?? $"{sbu.Code} {(template.IsIt ? "IT store" : "Stores")}",
                        CustodianEmployeeId = employeeId, CurrentLocationId = location.Id, SbuId = sbu.Id,
                        PreviousAssetNumber = row["previous_asset_number"], Supplier = row["supplier"], PoNo = row["po_no"], InvoiceNo = row["invoice_no"],
                        PurchaseDate = purchaseDate, InUseDate = inUseDate, WarrantyEnds = warrantyEnds, CostCentre = row["cost_centre"],
                        OriginalCost = cost, Currency = cost is null ? null : currency, ExchangeRate = cost is null ? null : rate,
                        PurchaseValue = cost is null || rate is null ? null : Math.Round(cost.Value * rate.Value, 2),
                        Notes = row["notes"], CreatedAt = now, UpdatedAt = now,
                    };
                    asset.Attributes = AttributeRows(template, validation.Values);
                    ready.Add((asset, row.RowNumber));
                }
            }
            report.Add(new ImportRowDto { Row = row.RowNumber, Description = description, Errors = errors });
        }

        var errorRows = report.Count(r => r.Errors.Count > 0);
        var created = new List<int>();
        if (commit && errorRows == 0)
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            foreach (var (asset, rowNumber) in ready)
            {
                db.Assets.Add(asset);
                await db.SaveChangesAsync(ct);
                audit.Record(user, "asset", asset.Id, asset.Id, "imported", "description", null, asset.Name, $"Bulk import (SF-10), row {rowNumber}");
                bcNumbers.Enqueue(asset);
                created.Add(asset.Id);
            }
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            try
            {
                await bcNumbers.ProcessDueAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Requesting Business Central numbers for {Count} imported assets failed; they stay queued", created.Count);
            }
        }
        return new ImportReportDto
        {
            Rows = report, RowCount = rows.Count, ErrorRowCount = errorRows, Imported = created.Count, AssetIds = created,
            CanImport = errorRows == 0,
        };
    }

    [GeneratedRegex("^[A-Z]{3}$")]
    private static partial Regex CurrencyCode();
}
