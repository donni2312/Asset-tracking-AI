using System.Text.Json;
using AssetIQ.Api.Auth;
using AssetIQ.Api.Contracts;
using AssetIQ.Api.Data;
using AssetIQ.Api.Domain;
using AssetIQ.Api.Infrastructure;
using AssetIQ.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;

namespace AssetIQ.Api.Controllers;

/// <summary>
/// The audited asset record (AU-01 … AU-08): asset card, category change, financial fields, capitalisation,
/// "No serial" decisions, tagging, upgrades and the audit trail. Every action checks <see cref="AccessPolicy"/>.
/// </summary>
public sealed class AssetRecordController(
    AssetIqDbContext db, AppClock clock, AssetQueries assets, AssetRegistrationService registration, AssetCardService cards,
    UpgradeService upgrades, AccessLog accessLog) : ApiControllerBase
{
    /// <summary>AU-08: open the card by asset number, previous number, tag or serial (search box or scanner).</summary>
    [HttpGet("assets/lookup")]
    [RequireRole(Roles.Viewer)]
    [FailureMessage("Failed to find the asset")]
    public async Task<AssetLookupDto> Lookup([FromQuery] string? code) =>
        new(await cards.LookupAsync(Caller, code ?? "", Aborted));

    [HttpGet("assets/{id}/card")]
    [RequireRole(Roles.Viewer)]
    [FailureMessage("Failed to load the asset card")]
    public Task<AssetCardDto> Card(string id) => cards.GetAsync(Caller, RequestValues.Id(id), Aborted);

    [HttpGet("assets/{id}/card.pdf")]
    [RequireRole(Roles.Viewer)]
    [FailureMessage("Failed to export the asset card")]
    public async Task<IActionResult> CardPdf(string id)
    {
        var card = await cards.GetAsync(Caller, RequestValues.Id(id), Aborted);
        await accessLog.ExportAsync(Caller, $"asset card PDF {card.Asset.AssetNumber ?? card.Asset.Id.ToString()}", Request, Aborted);
        return Download(AssetCardService.Pdf(card, Caller.Name, clock.UtcNow), TableExport.PdfContentType, $"asset-card-{card.Asset.AssetNumber ?? card.Asset.Id.ToString()}.pdf");
    }

    /// <summary>AU-05: the asset's monthly schedule as Excel.</summary>
    [HttpGet("assets/{id}/depreciation.xlsx")]
    [RequireRole(Roles.Viewer)]
    [FailureMessage("Failed to export the depreciation schedule")]
    public async Task<IActionResult> ScheduleExcel(string id)
    {
        var card = await cards.GetAsync(Caller, RequestValues.Id(id), Aborted);
        await accessLog.ExportAsync(Caller, $"depreciation schedule {card.Asset.AssetNumber ?? card.Asset.Id.ToString()}", Request, Aborted);
        return Download(TableExport.Excel(FinanceReportService.ScheduleTable(card)), TableExport.ExcelContentType,
            $"depreciation-{card.Asset.AssetNumber ?? card.Asset.Id.ToString()}.xlsx");
    }

    /// <summary>AU-06 / GF-07: the asset's audit trail, newest first. Read-only: nothing can change or delete an entry.</summary>
    [HttpGet("assets/{id}/audit")]
    [RequireRole(Roles.Viewer)]
    [FailureMessage("Failed to load the audit trail")]
    public async Task<List<AuditEntryDto>> Audit(string id)
    {
        var assetId = RequestValues.Id(id);
        await cards.GetAsync(Caller, assetId, Aborted); // 404 / 403 as for the card
        return await db.AuditLog.AsNoTracking().Where(e => e.AssetId == assetId).OrderByDescending(e => e.At).ThenByDescending(e => e.Id)
            .Select(e => new AuditEntryDto(e.Id, e.At, e.Action, e.Field, e.OldValue, e.NewValue, e.Reason, e.UserName))
            .ToListAsync(Aborted);
    }

    /// <summary>Change the category: SBU Finance only; the new category's required fields must be filled in.</summary>
    [HttpPost("assets/{id}/category")]
    [RequireRole(Roles.Viewer)]
    [FailureMessage("Failed to change the category")]
    public async Task<AssetDto> ChangeCategory(string id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] JsonElement? body)
    {
        var assetId = RequestValues.Id(id);
        var input = JsonBody.From(body);
        var categoryId = input.RequiredInt("categoryId");
        var subCategory = input.OptionalString("subCategory");
        var attributes = input.OptionalValueMap("attributes");
        var noSerialReason = input.OptionalString("noSerialReason");
        var reason = input.RequiredString("reason", minLength: 3);
        input.ThrowIfInvalid();
        await registration.ChangeCategoryAsync(Caller, assetId, new CategoryChange(categoryId, subCategory, attributes.Value ?? [], noSerialReason, reason), Aborted);
        return (await assets.GetDetailedAsync(assetId, Aborted))!;
    }

    /// <summary>AU-04 / AU-06: cost, currency, rate, residual value and purchase details (SBU Finance; a reason is required).</summary>
    [HttpPatch("assets/{id}/financial")]
    [RequireRole(Roles.Viewer)]
    [FailureMessage("Failed to update the financial fields")]
    public async Task<AssetDto> UpdateFinancial(string id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] JsonElement? body)
    {
        var assetId = RequestValues.Id(id);
        var input = JsonBody.From(body);
        var update = new FinancialUpdate(
            input.NullableNumber("originalCost"), input.NullableString("currency"), input.NullableNumber("exchangeRate"),
            input.NullableString("supplier"), input.NullableString("invoiceNo"), input.NullableString("poNo"),
            input.NullableDay("purchaseDate"), input.NullableDay("inUseDate"), input.NullableNumber("residualValue"),
            input.RequiredString("reason", minLength: 3));
        input.ThrowIfInvalid();
        await registration.UpdateFinancialAsync(Caller, assetId, update, Aborted);
        return (await assets.GetDetailedAsync(assetId, Aborted))!;
    }

    /// <summary>SF-01 / AU-04: capitalise (fixes the USD cost; depreciation starts in the in-use month).</summary>
    [HttpPost("assets/{id}/capitalise")]
    [RequireRole(Roles.Viewer)]
    [FailureMessage("Failed to capitalise the asset")]
    public async Task<AssetDto> Capitalise(string id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] JsonElement? body)
    {
        var assetId = RequestValues.Id(id);
        var input = JsonBody.From(body ?? JsonDocument.Parse("{}").RootElement);
        var reason = input.OptionalString("reason");
        input.ThrowIfInvalid();
        await registration.CapitaliseAsync(Caller, assetId, reason, Aborted);
        return (await assets.GetDetailedAsync(assetId, Aborted))!;
    }

    /// <summary>AU-02: SBU Finance approves or rejects the "No serial" reason.</summary>
    [HttpPost("assets/{id}/no-serial-decision")]
    [RequireRole(Roles.Viewer)]
    [FailureMessage("Failed to record the decision")]
    public async Task<AssetDto> NoSerialDecision(string id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] JsonElement? body)
    {
        var assetId = RequestValues.Id(id);
        var input = JsonBody.From(body);
        var approve = input.RequiredBool("approve");
        var comment = input.OptionalString("comment");
        input.ThrowIfInvalid();
        await registration.DecideNoSerialAsync(Caller, assetId, approve, comment, Aborted);
        return (await assets.GetDetailedAsync(assetId, Aborted))!;
    }

    /// <summary>Link the RFID / QR tag (only once the asset number is issued).</summary>
    [HttpPost("assets/{id}/tag")]
    [RequireRole(Roles.Viewer)]
    [FailureMessage("Failed to tag the asset")]
    public async Task<AssetDto> Tag(string id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] JsonElement? body)
    {
        var assetId = RequestValues.Id(id);
        var input = JsonBody.From(body);
        var tag = input.RequiredString("rfidTag", minLength: 1);
        input.ThrowIfInvalid();
        await registration.TagAsync(Caller, assetId, tag, Aborted);
        return (await assets.GetDetailedAsync(assetId, Aborted))!;
    }

    [HttpGet("assets/{id}/upgrades")]
    [RequireRole(Roles.Viewer)]
    [FailureMessage("Failed to list upgrades")]
    public async Task<IReadOnlyList<AssetUpgradeDto>> Upgrades(string id) =>
        (await cards.GetAsync(Caller, RequestValues.Id(id), Aborted)).Upgrades;

    /// <summary>AU-07: log an upgrade (SBU IT for IT assets, the SBU Admin Manager for non-IT, or Stores).</summary>
    [HttpPost("assets/{id}/upgrades")]
    [RequireRole(Roles.Viewer)]
    [FailureMessage("Failed to log the upgrade")]
    public async Task<IActionResult> LogUpgrade(string id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] JsonElement? body)
    {
        var assetId = RequestValues.Id(id);
        var input = JsonBody.From(body);
        var date = input.NullableDay("upgradeDate");
        if (!input.Has("upgradeDate")) input.RequiredString("upgradeDate");
        var log = new LogUpgradeInput(
            date.Value ?? "", input.RequiredString("description", minLength: 2), input.RequiredString("beforeText", minLength: 1),
            input.RequiredString("afterText", minLength: 1), input.OptionalNumber("cost") ?? MissingCost(input),
            input.OptionalString("supplier"), input.OptionalString("invoiceNo"), input.OptionalValueMap("fieldChanges").Value ?? []);
        input.ThrowIfInvalid();
        var upgrade = await upgrades.LogAsync(Caller, assetId, log, Aborted);
        return Status201(AssetCardService.ToDto(upgrade));
    }

    private static decimal MissingCost(JsonBody input)
    {
        if (!input.Has("cost")) input.RequiredString("cost");
        return 0m;
    }

    /// <summary>AU-07: SBU Finance capitalises, expenses or rejects an upgrade.</summary>
    [HttpPost("upgrades/{id}/decision")]
    [RequireRole(Roles.Viewer)]
    [FailureMessage("Failed to record the upgrade decision")]
    public async Task<AssetUpgradeDto> DecideUpgrade(string id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] JsonElement? body)
    {
        var upgradeId = RequestValues.Id(id);
        var input = JsonBody.From(body);
        var decision = new UpgradeDecision(
            input.RequiredEnum("treatment", UpgradeTreatments.All), input.OptionalString("comment"),
            input.OptionalInt("revisedLifeMonths"), input.OptionalString("effectivePeriod"));
        input.ThrowIfInvalid();
        var upgrade = await upgrades.DecideAsync(Caller, upgradeId, decision, Aborted);
        return AssetCardService.ToDto(upgrade, upgrade.Asset.AssetNumber, upgrade.Asset.Name);
    }
}

public sealed record AssetLookupDto(int AssetId);
