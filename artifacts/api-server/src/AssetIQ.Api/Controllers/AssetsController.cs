using System.Globalization;
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
/// Asset records. Lists show only what the caller may see (the request's <see cref="AccessScope"/> filters every
/// query); asking for one asset outside that scope is a 403.
/// </summary>
public sealed class AssetsController(AssetIqDbContext db, AssetQueries assets, AssetRegistrationService registration, AssetAccess access) : ApiControllerBase
{
    [HttpGet("assets")]
    [RequireRole(Roles.AssetReaders)]
    [FailureMessage("Failed to list assets")]
    public Task<List<AssetDto>> List(
        [FromQuery] string? status, [FromQuery] string? locationId, [FromQuery] string? sbuId,
        [FromQuery] string? category, [FromQuery] string? q)
    {
        var statusFilter = RequestValues.OptionalChoice(status, AssetStatuses.All);
        var locationFilter = RequestValues.OptionalInt(locationId);
        var sbuFilter = RequestValues.OptionalInt(sbuId);
        var categoryFilter = RequestValues.OptionalText(category);
        var search = RequestValues.OptionalText(q);

        var query = db.Assets.AsNoTracking();
        if (statusFilter is not null) query = query.Where(a => a.Status == statusFilter);
        if (locationFilter is not null) query = query.Where(a => a.CurrentLocationId == locationFilter);
        if (sbuFilter is not null) query = query.Where(a => a.SbuId == sbuFilter);
        if (categoryFilter is not null) query = query.Where(a => a.Category == categoryFilter);
        if (search is not null)
        {
            var pattern = $"%{search}%";
            query = query.Where(a => EF.Functions.ILike(a.Name, pattern) || EF.Functions.ILike(a.SerialNumber!, pattern) || EF.Functions.ILike(a.RfidTag!, pattern) || EF.Functions.ILike(a.AssetNumber!, pattern));
        }
        return query.OrderBy(a => a.Name).Select(AssetQueries.ToDto).ToListAsync(Aborted);
    }

    /// <summary>
    /// Registers an asset from its category template (AU-01 … AU-03): there is no generic form. The API
    /// rejects a missing category, a missing required field or a field from another category, generates the
    /// description, enforces Group-unique serial / MAC, and requests the Business Central asset number.
    /// </summary>
    [HttpPost("assets")]
    [RequireRole(Roles.Registrars)]
    [FailureMessage("Failed to create asset")]
    public async Task<IActionResult> Create([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] JsonElement? body)
    {
        var input = JsonBody.From(body);
        if (input.Has("name")) throw new ApiException(StatusCodes.Status400BadRequest, "name: the description is generated from the category fields and cannot be typed (AU-01)");
        if (input.Has("rfidTag")) throw new ApiException(StatusCodes.Status400BadRequest, "rfidTag: an asset is tagged after Business Central issues its asset number (AU-03)");
        var categoryId = input.RequiredInt("categoryId");
        var subCategory = input.OptionalString("subCategory");
        var attributes = input.OptionalValueMap("attributes");
        var noSerialReason = input.OptionalString("noSerialReason");
        var sbuId = input.OptionalInt("sbuId");
        var locationId = input.RequiredInt("currentLocationId");
        var custodian = input.RequiredString("assignedTo", minLength: 1);
        var condition = input.RequiredEnum("condition", AssetConditions.All);
        var notes = input.OptionalString("notes");
        var previousAssetNumber = input.OptionalString("previousAssetNumber");
        var supplier = input.OptionalString("supplier");
        var poNo = input.OptionalString("poNo");
        var purchaseDate = input.NullableDay("purchaseDate");
        var warrantyEnds = input.NullableDay("warrantyEnds");
        input.ThrowIfInvalid();

        var id = await registration.RegisterAsync(Caller, new RegisterAssetInput(
            categoryId, subCategory, attributes.Value ?? [], noSerialReason, sbuId, locationId, custodian, condition, notes,
            previousAssetNumber, supplier, poNo, purchaseDate.Value, warrantyEnds.Value), Aborted);
        return Status201((await assets.GetDetailedAsync(id, Aborted))!);
    }

    [HttpGet("assets/{id}")]
    [RequireRole(Roles.AssetReaders)]
    [FailureMessage("Failed to get asset")]
    public async Task<AssetDto> Get(string id)
    {
        var assetId = RequestValues.Id(id);
        await access.EnsureVisibleAsync(Caller, assetId, Aborted);
        return (await assets.GetDetailedAsync(assetId, Aborted))!;
    }

    /// <summary>
    /// Technical changes. Template values are merged with the current ones and re-validated, the description
    /// is rebuilt, and every change is audit-logged. The description and category cannot be set here.
    /// </summary>
    [HttpPatch("assets/{id}")]
    [RequireRole(Roles.TechnicalEditors)]
    [FailureMessage("Failed to update asset")]
    public async Task<AssetDto> Update(string id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] JsonElement? body)
    {
        var assetId = RequestValues.Id(id);
        var input = JsonBody.From(body);
        if (input.Has("name")) throw new ApiException(StatusCodes.Status400BadRequest, "name: the description is generated from the category fields and cannot be typed (AU-01)");
        if (input.Has("category") || input.Has("categoryId"))
            throw new ApiException(StatusCodes.Status400BadRequest, "category: change the category with POST /assets/{id}/category (SBU Finance only)");
        var subCategory = input.OptionalString("subCategory");
        var attributes = input.OptionalValueMap("attributes");
        var noSerialReason = input.OptionalString("noSerialReason");
        var status = input.OptionalEnum("status", AssetStatuses.Settable);
        var condition = input.OptionalEnum("condition", AssetConditions.All);
        var assignedTo = input.NullableString("assignedTo");
        var currentLocationId = input.NullableInt("currentLocationId");
        var sbuId = input.NullableInt("sbuId");
        var notes = input.NullableString("notes");
        var warrantyEnds = input.NullableDay("warrantyEnds");
        var reason = input.OptionalString("reason");
        var custodianEmployeeId = input.NullableInt("custodianEmployeeId");
        var departmentId = input.NullableInt("departmentId");
        input.ThrowIfInvalid();

        await registration.UpdateTechnicalAsync(Caller, assetId, new TechnicalUpdate(
            subCategory, attributes.IsSet ? attributes.Value : null, noSerialReason, currentLocationId, assignedTo, condition, status,
            sbuId, notes, warrantyEnds, reason, custodianEmployeeId, departmentId), Aborted);
        return (await assets.GetDetailedAsync(assetId, Aborted))!;
    }

    /// <summary>Removes a mistaken registration. Assets with an asset number or with history cannot be deleted.</summary>
    [HttpDelete("assets/{id}")]
    [RequireRole(Roles.Registrars)]
    [FailureMessage("Failed to delete asset")]
    public async Task<IActionResult> Delete(string id)
    {
        await registration.DeleteAsync(Caller, RequestValues.Id(id), Aborted);
        return NoContent();
    }

    /// <summary>Movements and lifecycle events of the asset, newest first.</summary>
    [HttpGet("assets/{id}/history")]
    [RequireRole(Roles.RecordReaders)]
    [FailureMessage("Failed to get asset history")]
    public async Task<List<AssetHistoryEventDto>> History(string id)
    {
        var assetId = RequestValues.Id(id);
        await access.EnsureVisibleAsync(Caller, assetId, Aborted);
        var movementItems = await db.Movements.AsNoTracking()
            .Where(m => m.AssetId == assetId)
            .OrderByDescending(m => m.Timestamp)
            .Select(m => new AssetHistoryEventDto
            {
                Id = "mov-" + m.Id,
                Kind = "movement",
                Timestamp = m.Timestamp,
                FromLocationName = m.FromLocation!.Name,
                ToLocationName = m.ToLocation!.Name,
                TriggeredBy = m.TriggeredBy,
                GatePassId = m.GatePassId,
                MovedBy = m.MovedBy,
                Notes = m.Notes,
            })
            .ToListAsync(Aborted);

        var lifecycleItems = await db.AssetLifecycleEvents.AsNoTracking()
            .Where(e => e.AssetId == assetId)
            .OrderByDescending(e => e.Timestamp)
            .Select(e => new AssetHistoryEventDto
            {
                Id = "lc-" + e.Id,
                Kind = "lifecycle",
                Timestamp = e.Timestamp,
                EventType = e.EventType,
                PerformedBy = e.PerformedBy,
                FromSbuName = e.FromSbu!.Name,
                ToSbuName = e.ToSbu!.Name,
                AssignedTo = e.AssignedTo,
                Vendor = e.Vendor,
                TicketRef = e.TicketRef,
                ConditionBefore = e.ConditionBefore,
                ConditionAfter = e.ConditionAfter,
                Notes = e.Notes,
            })
            .ToListAsync(Aborted);

        return movementItems.Concat(lifecycleItems).OrderByDescending(e => e.Timestamp).ToList();
    }
}
