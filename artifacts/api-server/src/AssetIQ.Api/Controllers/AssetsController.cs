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

public sealed class AssetsController(AssetIqDbContext db, AppClock clock, AssetQueries assets) : ApiControllerBase
{
    /// <summary>
    /// Fields an operator may change in the course of their work. Everything else is the asset's record
    /// (what it is and which unit owns it) and needs an admin.
    /// </summary>
    private static readonly HashSet<string> OperatorFields = ["status", "assignedTo", "currentLocationId", "notes"];

    /// <summary>The updatable fields, in the contract's (AssetUpdate) order.</summary>
    private static readonly string[] UpdateFields = ["name", "category", "status", "assignedTo", "currentLocationId", "sbuId", "notes"];

    [HttpGet("assets")]
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
            query = query.Where(a => EF.Functions.ILike(a.Name, pattern) || EF.Functions.ILike(a.SerialNumber, pattern) || EF.Functions.ILike(a.RfidTag, pattern));
        }
        return query.OrderBy(a => a.Name).Select(AssetQueries.ToDto).ToListAsync(Aborted);
    }

    [HttpPost("assets")]
    [RequireRole(Roles.Admin)]
    [FailureMessage("Failed to create asset")]
    public async Task<IActionResult> Create([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] JsonElement? body)
    {
        var input = JsonBody.From(body);
        var name = input.RequiredString("name");
        var category = input.RequiredString("category");
        var serialNumber = input.RequiredString("serialNumber");
        var rfidTag = input.RequiredString("rfidTag");
        var status = input.OptionalEnum("status", AssetStatuses.All) ?? AssetStatuses.Active;
        var assignedTo = input.OptionalString("assignedTo");
        var currentLocationId = input.OptionalInt("currentLocationId");
        var sbuId = input.OptionalInt("sbuId");
        var purchaseDate = input.OptionalDate("purchaseDate");
        var purchaseValue = input.OptionalNumber("purchaseValue");
        var notes = input.OptionalString("notes");
        input.ThrowIfInvalid();

        var now = clock.UtcNow;
        var asset = new Asset
        {
            Name = name,
            Category = category,
            SerialNumber = serialNumber,
            RfidTag = rfidTag,
            Status = status,
            AssignedTo = assignedTo,
            CurrentLocationId = currentLocationId,
            SbuId = sbuId,
            PurchaseDate = purchaseDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            PurchaseValue = purchaseValue,
            Notes = notes,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Assets.Add(asset);
        await db.SaveChangesAsync(Aborted);
        return Status201((await assets.GetDetailedAsync(asset.Id, Aborted))!);
    }

    [HttpGet("assets/{id}")]
    [FailureMessage("Failed to get asset")]
    public async Task<AssetDto> Get(string id) =>
        await assets.GetDetailedAsync(RequestValues.Id(id), Aborted)
        ?? throw new ApiException(StatusCodes.Status404NotFound, "Asset not found");

    /// <summary>
    /// Operators may change the operational fields; changing the record itself (name, category, sbuId)
    /// needs an admin. The split is decided by which fields the body actually contains, so a record
    /// field cannot be smuggled in beside an allowed one.
    /// </summary>
    [HttpPatch("assets/{id}")]
    [RequireRole(Roles.Operator)]
    [FailureMessage("Failed to update asset")]
    public async Task<AssetDto> Update(string id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] JsonElement? body)
    {
        var assetId = RequestValues.Id(id);
        var input = JsonBody.From(body);
        var name = input.OptionalString("name");
        var category = input.OptionalString("category");
        var status = input.OptionalEnum("status", AssetStatuses.All);
        var assignedTo = input.NullableString("assignedTo");
        var currentLocationId = input.NullableInt("currentLocationId");
        var sbuId = input.NullableInt("sbuId");
        var notes = input.NullableString("notes");
        input.ThrowIfInvalid();

        var recordFields = UpdateFields.Where(f => input.Has(f) && !OperatorFields.Contains(f)).ToList();
        var user = User.GetAuthUser()!;
        if (recordFields.Count > 0 && !Roles.Satisfies(user.Role, Roles.Admin))
        {
            throw new ApiException(StatusCodes.Status403Forbidden,
                $"Changing {string.Join(", ", recordFields)} needs the admin role; you are signed in as {user.Role}.");
        }

        var asset = await db.Assets.FirstOrDefaultAsync(a => a.Id == assetId, Aborted)
            ?? throw new ApiException(StatusCodes.Status404NotFound, "Asset not found");
        if (name is not null) asset.Name = name;
        if (category is not null) asset.Category = category;
        if (status is not null) asset.Status = status;
        if (assignedTo.IsSet) asset.AssignedTo = assignedTo.Value;
        if (currentLocationId.IsSet) asset.CurrentLocationId = currentLocationId.Value;
        if (sbuId.IsSet) asset.SbuId = sbuId.Value;
        if (notes.IsSet) asset.Notes = notes.Value;
        asset.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync(Aborted);
        return (await assets.GetDetailedAsync(assetId, Aborted))!;
    }

    /// <summary>Removes the asset row. An asset that already has history cannot be deleted (the database refuses).</summary>
    [HttpDelete("assets/{id}")]
    [RequireRole(Roles.Admin)]
    [FailureMessage("Failed to delete asset")]
    public async Task<IActionResult> Delete(string id)
    {
        var assetId = RequestValues.Id(id);
        await db.Assets.Where(a => a.Id == assetId).ExecuteDeleteAsync(Aborted);
        return NoContent();
    }

    /// <summary>Movements and lifecycle events of the asset, newest first.</summary>
    [HttpGet("assets/{id}/history")]
    [FailureMessage("Failed to get asset history")]
    public async Task<List<AssetHistoryEventDto>> History(string id)
    {
        var assetId = RequestValues.Id(id);
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
