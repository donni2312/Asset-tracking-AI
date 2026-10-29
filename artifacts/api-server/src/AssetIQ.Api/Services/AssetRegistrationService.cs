using System.Globalization;
using AssetIQ.Api.Auth;
using AssetIQ.Api.Data;
using AssetIQ.Api.Domain;
using AssetIQ.Api.Infrastructure;
using AssetIQ.Api.Services.Templates;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AssetIQ.Api.Services;

public sealed record RegisterAssetInput(
    int CategoryId, string? SubCategory, IReadOnlyDictionary<string, string?> Attributes, string? NoSerialReason,
    int? SbuId, int LocationId, string Custodian, string Condition, string? Notes, string? PreviousAssetNumber,
    string? Supplier, string? PoNo, string? PurchaseDate, string? WarrantyEnds);

/// <summary>Technical changes (PATCH /assets/{id}); every member is optional.</summary>
public sealed record TechnicalUpdate(
    string? SubCategory, IReadOnlyDictionary<string, string?>? Attributes, string? NoSerialReason,
    Optional<int?> LocationId, Optional<string?> Custodian, string? Condition, string? Status,
    Optional<int?> SbuId, Optional<string?> Notes, Optional<string?> WarrantyEnds, string? Reason);

public sealed record CategoryChange(int CategoryId, string? SubCategory, IReadOnlyDictionary<string, string?> Attributes, string? NoSerialReason, string Reason);

/// <summary>Financial fields (AU-04, AU-06); only SBU Finance of the asset's SBU may change them.</summary>
public sealed record FinancialUpdate(
    Optional<decimal?> OriginalCost, Optional<string?> Currency, Optional<decimal?> ExchangeRate,
    Optional<string?> Supplier, Optional<string?> InvoiceNo, Optional<string?> PoNo,
    Optional<string?> PurchaseDate, Optional<string?> InUseDate, Optional<decimal?> ResidualValue, string Reason);

/// <summary>
/// Registration and maintenance of the asset record (AU-01 … AU-04). The category template drives the
/// record: values are validated on the server, the description is generated and rebuilt on every change,
/// the identifier (serial / VIN / IMEI) and MAC address are unique across the Group, and every change is
/// written to the audit trail in the same transaction.
/// </summary>
public sealed class AssetRegistrationService(
    AssetIqDbContext db, AppClock clock, AuditTrail audit, BcNumberService bcNumbers, ILogger<AssetRegistrationService> logger)
{
    public async Task<Template> LoadTemplateAsync(int categoryId, CancellationToken ct, bool activeOnly = true)
    {
        var category = await db.AssetCategories.AsNoTracking().Include(c => c.Fields).FirstOrDefaultAsync(c => c.Id == categoryId, ct)
            ?? throw new ApiException(StatusCodes.Status400BadRequest, $"categoryId: category {categoryId} does not exist");
        if (activeOnly && !category.Active) throw new ApiException(StatusCodes.Status400BadRequest, $"categoryId: {category.Name} is no longer in use");
        return Template.From(category);
    }

    // ── AU-01 / AU-02 / AU-03: register ─────────────────────────────────────────

    public async Task<int> RegisterAsync(AuthUser user, RegisterAssetInput input, CancellationToken ct)
    {
        var template = await LoadTemplateAsync(input.CategoryId, ct);
        var sbuId = input.SbuId ?? (Roles.IsSbuScoped(user.Role) ? user.SbuId : null)
            ?? throw new ApiException(StatusCodes.Status400BadRequest, "sbuId: Required");
        if (!await db.Sbus.AnyAsync(s => s.Id == sbuId, ct)) throw new ApiException(StatusCodes.Status404NotFound, "SBU not found");
        if (!AccessPolicy.CanRegister(user, template.IsIt, sbuId))
        {
            throw AccessPolicy.Deny(user, template.IsIt ? "register IT assets for this SBU" : "register non-IT assets (only the SBU's Admin Manager may, AM-01)");
        }
        if (!await db.Locations.AnyAsync(l => l.Id == input.LocationId, ct)) throw new ApiException(StatusCodes.Status404NotFound, "Location not found");

        var noSerial = !string.IsNullOrWhiteSpace(input.NoSerialReason);
        var validation = TemplateRules.Validate(template, input.SubCategory, input.Attributes, noSerial);
        var errors = validation.Errors.ToList();
        var identity = ReadIdentity(template, validation, noSerial, input.NoSerialReason, errors);
        if (errors.Count > 0) throw new ApiException(StatusCodes.Status400BadRequest, string.Join("; ", errors));

        var description = TemplateRules.BuildDescription(template, input.SubCategory, validation.Values);
        await EnsureUniqueAsync(user, identity.Serial, identity.Mac, null, description, ct);

        var now = clock.UtcNow;
        var asset = new Asset
        {
            Name = description,
            Category = input.SubCategory?.Trim() ?? template.Name,
            CategoryId = template.CategoryId,
            SubCategory = input.SubCategory?.Trim(),
            SerialNumber = identity.Serial,
            NoSerialReason = identity.Serial is null ? input.NoSerialReason!.Trim() : null,
            NoSerialStatus = identity.Serial is null ? NoSerialStatuses.Pending : null,
            MacAddress = identity.Mac,
            Status = AssetStatuses.PendingAssetNumber,
            Condition = input.Condition,
            AssignedTo = input.Custodian.Trim(),
            CurrentLocationId = input.LocationId,
            SbuId = sbuId,
            PreviousAssetNumber = Blank(input.PreviousAssetNumber),
            Supplier = Blank(input.Supplier),
            PoNo = Blank(input.PoNo),
            PurchaseDate = input.PurchaseDate,
            WarrantyEnds = input.WarrantyEnds,
            Notes = Blank(input.Notes),
            CreatedAt = now,
            UpdatedAt = now,
        };
        asset.Attributes = AttributeRows(template, validation.Values);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        db.Assets.Add(asset);
        await SaveOrExplainDuplicateAsync(user, asset, ct);
        audit.Record(user, "asset", asset.Id, asset.Id, "created", "description", null, description,
            $"Registered as {template.Name} by {Roles.DisplayName(user.Role)}");
        if (identity.Serial is null) audit.Record(user, "asset", asset.Id, asset.Id, "no_serial_requested", "noSerialReason", null, asset.NoSerialReason);
        bcNumbers.Enqueue(asset);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        // Ask Business Central straight away; if it is unreachable the request stays queued and is retried.
        try
        {
            await bcNumbers.ProcessDueAsync(ct, asset.Id);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Requesting the Business Central number of asset {AssetId} failed; it stays queued", asset.Id);
        }
        return asset.Id;
    }

    private sealed record Identity(string? Serial, string? Mac);

    /// <summary>The identifier field goes to serial_number and the MAC address to mac_address (both Group-unique).</summary>
    private static Identity ReadIdentity(Template template, TemplateValidation validation, bool noSerial, string? noSerialReason, List<string> errors)
    {
        string? serial = null;
        if (template.IdentifierField is { } idField && validation.Values.TryGetValue(idField.Key, out var id))
        {
            serial = TemplateRules.NormaliseIdentifier(id.Text);
        }
        if (serial is null && !noSerial)
        {
            errors.Add(template.IdentifierField is { } f
                ? $"attributes.{f.Key}: {f.Label} is required, or record the item as \"No serial\" with a reason (noSerialReason)"
                : $"noSerialReason: {template.Name} has no manufacturer serial; give the reason it is recorded as \"No serial\"");
        }
        if (serial is not null && noSerialReason is not null && template.IdentifierField is not null)
        {
            errors.Add("noSerialReason: not allowed when a serial number is given");
        }
        string? mac = null;
        if (validation.Values.TryGetValue(Template.MacAddressKey, out var macValue))
        {
            mac = TemplateRules.NormaliseMac(macValue.Text);
            if (mac is null) errors.Add($"attributes.{Template.MacAddressKey}: '{macValue.Text}' is not a MAC address");
        }
        return new Identity(serial, mac);
    }

    /// <summary>Template values except the ones stored in their own columns.</summary>
    private static List<AssetAttributeValue> AttributeRows(Template template, IReadOnlyDictionary<string, FieldValue> values) =>
        values
            .Where(v => v.Key != template.IdentifierFieldKey && v.Key != Template.MacAddressKey)
            .Select(v => new AssetAttributeValue { FieldId = template.Field(v.Key)!.Id, ValueText = v.Value.Text, ValueNumber = v.Value.Number })
            .ToList();

    /// <summary>
    /// AU-02 / GI-02: a serial or MAC already used anywhere in the Group is refused with the asset that holds
    /// it, and the attempt is logged (saved on its own, so the refusal cannot roll it back).
    /// </summary>
    private async Task EnsureUniqueAsync(AuthUser user, string? serial, string? mac, int? exceptAssetId, string attempted, CancellationToken ct)
    {
        if (serial is not null)
        {
            var holder = await db.Assets.AsNoTracking().Where(a => a.SerialNumber == serial && a.Id != exceptAssetId)
                .Select(a => new { a.Id, a.AssetNumber, a.Name }).FirstOrDefaultAsync(ct);
            if (holder is not null) await RefuseDuplicateAsync(user, "serial number", serial, holder.Id, holder.AssetNumber, holder.Name, attempted, ct);
        }
        if (mac is not null)
        {
            var holder = await db.Assets.AsNoTracking().Where(a => a.MacAddress == mac && a.Id != exceptAssetId)
                .Select(a => new { a.Id, a.AssetNumber, a.Name }).FirstOrDefaultAsync(ct);
            if (holder is not null) await RefuseDuplicateAsync(user, "MAC address", mac, holder.Id, holder.AssetNumber, holder.Name, attempted, ct);
        }
    }

    private async Task RefuseDuplicateAsync(AuthUser user, string what, string value, int holderId, string? holderNumber, string holderName, string attempted, CancellationToken ct)
    {
        // A failed insert aborts the surrounding transaction; the attempt is logged outside it.
        if (db.Database.CurrentTransaction is { } open) await open.RollbackAsync(ct);
        db.ChangeTracker.Clear();
        var action = what == "serial number" ? "duplicate_serial_attempt" : "duplicate_mac_attempt";
        audit.Record(user, "asset", holderId, holderId, action, what == "serial number" ? "serialNumber" : "macAddress", null, value,
            $"Refused: \"{attempted}\" tried to use the {what} of this asset");
        await db.SaveChangesAsync(ct);
        var holder = holderNumber ?? $"asset #{holderId} (asset number pending)";
        throw new ApiException(StatusCodes.Status409Conflict,
            $"The {what} {value} is already used by {holder} ({holderName}). Duplicates are blocked and this attempt has been logged.");
    }

    /// <summary>Saves; a unique-constraint race on serial / MAC / tag becomes the same 409 as the pre-check.</summary>
    private async Task SaveOrExplainDuplicateAsync(AuthUser user, Asset asset, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg)
        {
            var (column, value) = pg.ConstraintName switch
            {
                "assets_serial_number_unique" => ("serial number", asset.SerialNumber),
                "assets_mac_address_unique" => ("MAC address", asset.MacAddress),
                "assets_rfid_tag_unique" => ("RFID tag", asset.RfidTag),
                _ => (null, null),
            };
            if (column is null || value is null) throw;
            if (db.Database.CurrentTransaction is { } open) await open.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            var holder = await db.Assets.AsNoTracking()
                .Where(a => column == "serial number" ? a.SerialNumber == value : column == "MAC address" ? a.MacAddress == value : a.RfidTag == value)
                .Select(a => new { a.Id, a.AssetNumber, a.Name }).FirstAsync(ct);
            await RefuseDuplicateAsync(user, column, value, holder.Id, holder.AssetNumber, holder.Name, asset.Name, ct);
        }
    }

    // ── technical edits: rebuild the description, audit every field ──────────────

    public async Task UpdateTechnicalAsync(AuthUser user, int assetId, TechnicalUpdate input, CancellationToken ct)
    {
        var asset = await LoadAsync(assetId, ct);
        var template = await LoadTemplateAsync(asset.CategoryId, ct, activeOnly: false);
        if (!AccessPolicy.CanEditTechnical(user, template.IsIt, asset.SbuId)) throw AccessPolicy.Deny(user, "edit this asset's technical fields");
        if (input.SbuId.IsSet && input.SbuId.Value != asset.SbuId)
        {
            if (input.SbuId.Value is not { } newSbu || !AccessPolicy.CanEditTechnical(user, template.IsIt, newSbu))
                throw AccessPolicy.Deny(user, "move this asset to another SBU");
            if (!await db.Sbus.AnyAsync(s => s.Id == newSbu, ct)) throw new ApiException(StatusCodes.Status404NotFound, "SBU not found");
        }
        if (input.LocationId is { IsSet: true, Value: { } loc } && !await db.Locations.AnyAsync(l => l.Id == loc, ct))
            throw new ApiException(StatusCodes.Status404NotFound, "Location not found");
        if (input.Status is not null && asset.Status == AssetStatuses.PendingAssetNumber && input.Status != asset.Status)
            throw new ApiException(StatusCodes.Status409Conflict, "The asset is waiting for its Business Central asset number; its status changes when the number is issued");

        var reason = Blank(input.Reason);
        // Template values are re-validated (and the description rebuilt) only when they change, so a status
        // or custodian change on an asset registered before the templates existed still goes through; AU-11
        // lists that asset's missing template fields until they are filled in.
        var templateChange = input.Attributes is not null || input.SubCategory is not null || input.NoSerialReason is not null;
        if (templateChange)
        {
            var current = CurrentValues(template, asset);
            var merged = new Dictionary<string, string?>(current, StringComparer.Ordinal);
            foreach (var (key, value) in input.Attributes ?? new Dictionary<string, string?>()) merged[key] = value;
            var subCategory = input.SubCategory ?? asset.SubCategory;
            var noSerialReason = input.NoSerialReason ?? asset.NoSerialReason;
            var noSerial = !string.IsNullOrWhiteSpace(noSerialReason);
            var validation = TemplateRules.Validate(template, subCategory, merged, noSerial);
            var errors = validation.Errors.ToList();
            var serialGiven = template.IdentifierFieldKey is { } idKey && !string.IsNullOrWhiteSpace(merged.GetValueOrDefault(idKey));
            var identity = ReadIdentity(template, validation, noSerial, serialGiven ? null : noSerialReason, errors);
            if (errors.Count > 0) throw new ApiException(StatusCodes.Status400BadRequest, string.Join("; ", errors));

            var description = TemplateRules.BuildDescription(template, subCategory, validation.Values);
            await EnsureUniqueAsync(user, identity.Serial, identity.Mac, asset.Id, description, ct);

            foreach (var field in template.Fields)
            {
                var before = current.GetValueOrDefault(field.Key);
                var after = validation.Values.TryGetValue(field.Key, out var v) ? v.Text : null;
                audit.RecordChange(user, "asset", asset.Id, asset.Id, $"attributes.{field.Key}", before, after, reason);
            }
            audit.RecordChange(user, "asset", asset.Id, asset.Id, "subCategory", asset.SubCategory, subCategory?.Trim(), reason);
            audit.RecordChange(user, "asset", asset.Id, asset.Id, "description", asset.Name, description, reason);

            asset.SubCategory = subCategory?.Trim();
            asset.Category = asset.SubCategory ?? template.Name;
            asset.Name = description;
            if (identity.Serial != asset.SerialNumber && identity.Serial is not null)
            {
                asset.NoSerialReason = null;
                asset.NoSerialStatus = null;
            }
            else if (identity.Serial is null && asset.SerialNumber is not null)
            {
                asset.NoSerialReason = noSerialReason!.Trim();
                asset.NoSerialStatus = NoSerialStatuses.Pending;
                audit.Record(user, "asset", asset.Id, asset.Id, "no_serial_requested", "noSerialReason", null, asset.NoSerialReason, reason);
            }
            asset.SerialNumber = identity.Serial;
            asset.MacAddress = identity.Mac;
            ReplaceAttributes(asset, AttributeRows(template, validation.Values));
        }

        if (input.LocationId.IsSet && audit.RecordChange(user, "asset", asset.Id, asset.Id, "currentLocationId", asset.CurrentLocationId, input.LocationId.Value, reason))
            asset.CurrentLocationId = input.LocationId.Value;
        if (input.Custodian.IsSet && audit.RecordChange(user, "asset", asset.Id, asset.Id, "custodian", asset.AssignedTo, Blank(input.Custodian.Value), reason))
            asset.AssignedTo = Blank(input.Custodian.Value);
        if (input.Condition is not null && audit.RecordChange(user, "asset", asset.Id, asset.Id, "condition", asset.Condition, input.Condition, reason))
            asset.Condition = input.Condition;
        if (input.Status is not null && audit.RecordChange(user, "asset", asset.Id, asset.Id, "status", asset.Status, input.Status, reason))
            asset.Status = input.Status;
        if (input.SbuId.IsSet && audit.RecordChange(user, "asset", asset.Id, asset.Id, "sbuId", asset.SbuId, input.SbuId.Value, reason))
            asset.SbuId = input.SbuId.Value;
        if (input.Notes.IsSet && audit.RecordChange(user, "asset", asset.Id, asset.Id, "notes", asset.Notes, input.Notes.Value, reason))
            asset.Notes = input.Notes.Value;
        if (input.WarrantyEnds.IsSet && audit.RecordChange(user, "asset", asset.Id, asset.Id, "warrantyEnds", asset.WarrantyEnds, input.WarrantyEnds.Value, reason))
            asset.WarrantyEnds = input.WarrantyEnds.Value;

        asset.UpdatedAt = clock.UtcNow;
        await SaveOrExplainDuplicateAsync(user, asset, ct);
    }

    /// <summary>Updates the tracked attribute rows in place: changed values are updated, new ones added, dropped ones removed.</summary>
    private void ReplaceAttributes(Asset asset, List<AssetAttributeValue> rows)
    {
        foreach (var existing in asset.Attributes.Where(e => rows.All(r => r.FieldId != e.FieldId)).ToList())
        {
            asset.Attributes.Remove(existing);
            db.AssetAttributeValues.Remove(existing);
        }
        foreach (var row in rows)
        {
            var existing = asset.Attributes.FirstOrDefault(e => e.FieldId == row.FieldId);
            if (existing is null)
            {
                row.AssetId = asset.Id;
                asset.Attributes.Add(row);
            }
            else
            {
                existing.ValueText = row.ValueText;
                existing.ValueNumber = row.ValueNumber;
            }
        }
    }

    /// <summary>The asset's template values as text, including the identifier and MAC columns.</summary>
    private static Dictionary<string, string?> CurrentValues(Template template, Asset asset)
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var row in asset.Attributes)
        {
            var field = template.Fields.FirstOrDefault(f => f.Id == row.FieldId);
            if (field is not null) values[field.Key] = row.ValueText;
        }
        if (template.IdentifierFieldKey is { } idKey && asset.SerialNumber is not null) values[idKey] = asset.SerialNumber;
        if (template.Field(Template.MacAddressKey) is not null && asset.MacAddress is not null) values[Template.MacAddressKey] = asset.MacAddress;
        return values;
    }

    // ── category change: SBU Finance only, audit-logged, new template filled in ──

    public async Task ChangeCategoryAsync(AuthUser user, int assetId, CategoryChange input, CancellationToken ct)
    {
        var asset = await LoadAsync(assetId, ct);
        if (!AccessPolicy.IsSbuFinanceOf(user, asset.SbuId)) throw AccessPolicy.Deny(user, "change an asset's category (only SBU Finance of its SBU may)");
        if (input.CategoryId == asset.CategoryId) throw new ApiException(StatusCodes.Status400BadRequest, "categoryId: the asset is already in this category");
        var oldTemplate = await LoadTemplateAsync(asset.CategoryId, ct, activeOnly: false);
        var template = await LoadTemplateAsync(input.CategoryId, ct);

        var noSerial = !string.IsNullOrWhiteSpace(input.NoSerialReason);
        var validation = TemplateRules.Validate(template, input.SubCategory, input.Attributes, noSerial);
        var errors = validation.Errors.ToList();
        var identity = ReadIdentity(template, validation, noSerial, input.NoSerialReason, errors);
        if (errors.Count > 0) throw new ApiException(StatusCodes.Status400BadRequest, string.Join("; ", errors));
        var description = TemplateRules.BuildDescription(template, input.SubCategory, validation.Values);
        await EnsureUniqueAsync(user, identity.Serial, identity.Mac, asset.Id, description, ct);

        audit.Record(user, "asset", asset.Id, asset.Id, "category_changed", "category", oldTemplate.Name, template.Name, input.Reason);
        audit.RecordChange(user, "asset", asset.Id, asset.Id, "description", asset.Name, description, input.Reason);
        audit.RecordChange(user, "asset", asset.Id, asset.Id, "serialNumber", asset.SerialNumber, identity.Serial, input.Reason);

        asset.CategoryId = template.CategoryId;
        asset.SubCategory = input.SubCategory?.Trim();
        asset.Category = asset.SubCategory ?? template.Name;
        asset.Name = description;
        asset.SerialNumber = identity.Serial;
        asset.MacAddress = identity.Mac;
        asset.NoSerialReason = identity.Serial is null ? input.NoSerialReason!.Trim() : null;
        asset.NoSerialStatus = identity.Serial is null ? NoSerialStatuses.Pending : null;
        ReplaceAttributes(asset, AttributeRows(template, validation.Values));
        asset.UpdatedAt = clock.UtcNow;
        await SaveOrExplainDuplicateAsync(user, asset, ct);
    }

    // ── AU-04 / AU-06: cost and purchase details (SBU Finance) ───────────────────

    public async Task UpdateFinancialAsync(AuthUser user, int assetId, FinancialUpdate input, CancellationToken ct)
    {
        var asset = await LoadAsync(assetId, ct);
        if (!AccessPolicy.IsSbuFinanceOf(user, asset.SbuId)) throw AccessPolicy.Deny(user, "edit financial fields (only SBU Finance of the asset's SBU may)");

        var cost = input.OriginalCost.IsSet ? input.OriginalCost.Value : asset.OriginalCost;
        var currency = input.Currency.IsSet ? input.Currency.Value?.Trim().ToUpperInvariant() : asset.Currency;
        var rate = input.ExchangeRate.IsSet ? input.ExchangeRate.Value : asset.ExchangeRate;
        var residual = input.ResidualValue.IsSet ? input.ResidualValue.Value ?? 0m : asset.ResidualValue;
        CheckMoney(cost, currency, rate, residual);
        if (asset.CapitalisedAt is not null && (input.OriginalCost.IsSet || input.ExchangeRate.IsSet || input.Currency.IsSet || input.InUseDate.IsSet)
            && (cost is null || currency is null || rate is null || input.InUseDate is { IsSet: true, Value: null }))
        {
            throw new ApiException(StatusCodes.Status400BadRequest, "A capitalised asset must keep its cost, currency, exchange rate and in-use date");
        }

        var reason = input.Reason;
        void Set<T>(string field, T before, T after, Action<T> apply)
        {
            if (audit.RecordChange(user, "asset", asset.Id, asset.Id, field, before, after, reason)) apply(after);
        }
        Set("originalCost", asset.OriginalCost, cost, v => asset.OriginalCost = v);
        Set("currency", asset.Currency, currency, v => asset.Currency = v);
        Set("exchangeRate", asset.ExchangeRate, rate, v => asset.ExchangeRate = v);
        Set("residualValue", asset.ResidualValue, residual, v => asset.ResidualValue = v);
        if (input.Supplier.IsSet) Set("supplier", asset.Supplier, Blank(input.Supplier.Value), v => asset.Supplier = v);
        if (input.InvoiceNo.IsSet) Set("invoiceNo", asset.InvoiceNo, Blank(input.InvoiceNo.Value), v => asset.InvoiceNo = v);
        if (input.PoNo.IsSet) Set("poNo", asset.PoNo, Blank(input.PoNo.Value), v => asset.PoNo = v);
        if (input.PurchaseDate.IsSet) Set("purchaseDate", asset.PurchaseDate, input.PurchaseDate.Value, v => asset.PurchaseDate = v);
        if (input.InUseDate.IsSet) Set("inUseDate", asset.InUseDate, input.InUseDate.Value, v => asset.InUseDate = v);

        // A capitalised asset's USD cost follows a corrected cost or rate (a change to past, locked months is
        // posted as an adjustment in the next open month).
        if (asset.CapitalisedAt is not null)
        {
            var usd = Usd(asset.OriginalCost!.Value, asset.ExchangeRate!.Value);
            Set("costUsd", asset.CostUsd, usd, v => asset.CostUsd = v);
            asset.PurchaseValue = asset.CostUsd;
        }
        else
        {
            asset.PurchaseValue = asset.Currency == "USD" ? asset.OriginalCost : asset.PurchaseValue;
        }
        asset.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    private static void CheckMoney(decimal? cost, string? currency, decimal? rate, decimal residual)
    {
        var problems = new List<string>();
        if (cost is < 0) problems.Add("originalCost: must not be negative");
        if (cost is not null && decimal.Round(cost.Value, 2) != cost.Value) problems.Add("originalCost: at most 2 decimal places");
        if (currency is not null && (currency.Length != 3 || !currency.All(char.IsAsciiLetterUpper))) problems.Add("currency: a 3-letter ISO code (USD, ZWG, ZAR)");
        if (rate is <= 0) problems.Add("exchangeRate: must be greater than 0");
        if (currency == "USD" && rate is not null && rate != 1m) problems.Add("exchangeRate: must be 1 for USD");
        if (residual < 0) problems.Add("residualValue: must not be negative");
        if (problems.Count > 0) throw new ApiException(StatusCodes.Status400BadRequest, string.Join("; ", problems));
    }

    /// <summary>AU-04: USD equivalent, original cost × exchange rate, rounded to cents.</summary>
    public static decimal Usd(decimal originalCost, decimal exchangeRate) => Math.Round(originalCost * exchangeRate, 2, MidpointRounding.AwayFromZero);

    /// <summary>
    /// SF-01 / AU-04: SBU Finance capitalises the asset; the USD cost is fixed from the original cost and the
    /// rate used, and depreciation starts in the in-use month. Not while the asset number is pending (AU-03).
    /// </summary>
    public async Task CapitaliseAsync(AuthUser user, int assetId, string? reason, CancellationToken ct)
    {
        var asset = await LoadAsync(assetId, ct);
        if (!AccessPolicy.IsSbuFinanceOf(user, asset.SbuId)) throw AccessPolicy.Deny(user, "capitalise assets of this SBU");
        if (asset.AssetNumber is null) throw new ApiException(StatusCodes.Status409Conflict, "The asset cannot be capitalised while its Business Central asset number is pending");
        if (asset.CapitalisedAt is not null) throw new ApiException(StatusCodes.Status409Conflict, "The asset is already capitalised");
        var missing = new List<string>();
        if (asset.OriginalCost is null) missing.Add("original cost");
        if (asset.Currency is null) missing.Add("currency");
        if (asset.ExchangeRate is null) missing.Add("exchange rate");
        if (asset.InUseDate is null) missing.Add("in-use date");
        if (missing.Count > 0) throw new ApiException(StatusCodes.Status400BadRequest, $"Record the {string.Join(", ", missing)} before capitalising");

        var now = clock.UtcNow;
        asset.CostUsd = Usd(asset.OriginalCost!.Value, asset.ExchangeRate!.Value);
        asset.PurchaseValue = asset.CostUsd;
        asset.CapitalisedAt = now;
        asset.CapitalisedBy = user.Name;
        asset.UpdatedAt = now;
        audit.Record(user, "asset", asset.Id, asset.Id, "capitalised", "costUsd", null, asset.CostUsd,
            reason ?? $"{AuditTrail.Text(asset.OriginalCost)} {asset.Currency} × {AuditTrail.Text(asset.ExchangeRate)}, in use from {asset.InUseDate}");
        await db.SaveChangesAsync(ct);
    }

    /// <summary>AU-02: SBU Finance approves or rejects a "No serial" reason; until approved the asset is incomplete (AU-11).</summary>
    public async Task DecideNoSerialAsync(AuthUser user, int assetId, bool approve, string? comment, CancellationToken ct)
    {
        var asset = await LoadAsync(assetId, ct);
        if (!AccessPolicy.IsSbuFinanceOf(user, asset.SbuId)) throw AccessPolicy.Deny(user, "decide \"No serial\" reasons for this SBU");
        if (asset.NoSerialStatus != NoSerialStatuses.Pending) throw new ApiException(StatusCodes.Status409Conflict, "There is no \"No serial\" reason waiting for a decision");
        var status = approve ? NoSerialStatuses.Approved : NoSerialStatuses.Rejected;
        audit.Record(user, "asset", asset.Id, asset.Id, approve ? "no_serial_approved" : "no_serial_rejected", "noSerialStatus", asset.NoSerialStatus, status, comment);
        asset.NoSerialStatus = status;
        asset.NoSerialDecidedBy = user.Name;
        asset.NoSerialDecidedAt = clock.UtcNow;
        asset.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Links the RFID / QR tag. Not before the asset number is issued (AU-03): the number is printed on the tag.</summary>
    public async Task TagAsync(AuthUser user, int assetId, string rfidTag, CancellationToken ct)
    {
        var asset = await LoadAsync(assetId, ct);
        if (!AccessPolicy.CanEditTechnical(user, asset.AssetCategory.IsIt, asset.SbuId) && !(user.Role == Roles.Stores && AccessPolicy.CanView(user, asset, asset.AssetCategory.IsIt)))
            throw AccessPolicy.Deny(user, "tag this asset");
        if (asset.AssetNumber is null) throw new ApiException(StatusCodes.Status409Conflict, "The asset cannot be tagged while its Business Central asset number is pending");
        var tag = rfidTag.Trim();
        if (tag.Length == 0) throw new ApiException(StatusCodes.Status400BadRequest, "rfidTag: Required");
        var holder = await db.Assets.AsNoTracking().Where(a => a.RfidTag == tag && a.Id != asset.Id).Select(a => a.AssetNumber ?? a.Name).FirstOrDefaultAsync(ct);
        if (holder is not null) throw new ApiException(StatusCodes.Status409Conflict, $"RFID tag {tag} is already linked to {holder}");
        audit.RecordChange(user, "asset", asset.Id, asset.Id, "rfidTag", asset.RfidTag, tag);
        asset.RfidTag = tag;
        asset.UpdatedAt = clock.UtcNow;
        await SaveOrExplainDuplicateAsync(user, asset, ct);
    }

    /// <summary>
    /// Deletes a mistaken registration. An asset that has an asset number can never be deleted, so its number
    /// can never be reused (AU-03; the database refuses too); one with history cannot be deleted either.
    /// </summary>
    public async Task DeleteAsync(AuthUser user, int assetId, CancellationToken ct)
    {
        var asset = await db.Assets.Include(a => a.AssetCategory).FirstOrDefaultAsync(a => a.Id == assetId, ct);
        if (asset is null) return;
        if (!AccessPolicy.CanEditTechnical(user, asset.AssetCategory.IsIt, asset.SbuId)) throw AccessPolicy.Deny(user, "delete this asset");
        if (asset.AssetNumber is not null)
            throw new ApiException(StatusCodes.Status409Conflict, $"Asset {asset.AssetNumber} has a Business Central asset number and cannot be deleted; asset numbers are never reused");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            await db.AssetAttributeValues.Where(a => a.AssetId == assetId).ExecuteDeleteAsync(ct);
            await db.BcNumberRequests.Where(r => r.AssetId == assetId).ExecuteDeleteAsync(ct);
            await db.Assets.Where(a => a.Id == assetId).ExecuteDeleteAsync(ct);
            audit.Record(user, "asset", assetId, assetId, "deleted", "description", asset.Name, null);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.ForeignKeyViolation)
        {
            throw new ApiException(StatusCodes.Status409Conflict, "The asset has history (movements, gate passes, upgrades …) and cannot be deleted");
        }
    }

    private async Task<Asset> LoadAsync(int assetId, CancellationToken ct) =>
        await db.Assets.Include(a => a.Attributes).Include(a => a.AssetCategory).FirstOrDefaultAsync(a => a.Id == assetId, ct)
        ?? throw new ApiException(StatusCodes.Status404NotFound, "Asset not found");

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public static string Day(DateTime value) => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
