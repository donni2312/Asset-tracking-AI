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

public sealed class MovementsController(AssetIqDbContext db, AppClock clock, AssetAccess access) : ApiControllerBase
{
    [HttpGet("movements")]
    [RequireRole(Roles.GateLogReaders)]
    [FailureMessage("Failed to list movements")]
    public Task<List<MovementDto>> List([FromQuery] string? assetId, [FromQuery] string? sbuId, [FromQuery] string? limit)
    {
        var take = Math.Clamp(RequestValues.OptionalInt(limit) ?? 50, 1, 200);
        var assetFilter = RequestValues.OptionalInt(assetId);
        var sbuFilter = RequestValues.OptionalInt(sbuId);

        var query = db.Movements.AsNoTracking();
        if (assetFilter is not null) query = query.Where(m => m.AssetId == assetFilter);
        if (sbuFilter is not null) query = query.Where(m => m.Asset.SbuId == sbuFilter);
        return query
            .OrderByDescending(m => m.Timestamp)
            .ThenByDescending(m => m.Id)
            .Take(take)
            .Select(MovementService.ToDto)
            .ToListAsync(Aborted);
    }

    /// <summary>A manual movement recorded by a person; the asset's current location follows.</summary>
    [HttpPost("movements")]
    [RequireRole(Roles.Movers)]
    [FailureMessage("Failed to create movement")]
    public async Task<IActionResult> Create([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] JsonElement? body)
    {
        var input = JsonBody.From(body);
        var assetId = input.RequiredInt("assetId");
        var toLocationId = input.RequiredInt("toLocationId");
        var movedBy = input.OptionalString("movedBy")?.Trim();
        var notes = input.OptionalString("notes")?.Trim();
        input.ThrowIfInvalid();

        var found = await db.Assets.IgnoreQueryFilters().AsNoTracking().Include(a => a.AssetCategory).FirstOrDefaultAsync(a => a.Id == assetId, Aborted)
            ?? throw new ApiException(StatusCodes.Status404NotFound, "Asset not found");
        if (!AccessPolicy.CanMove(Caller, found, found.AssetCategory.IsIt)) throw AccessPolicy.Deny(Caller, "move this asset");
        var asset = await db.Assets.IgnoreQueryFilters().FirstAsync(a => a.Id == assetId, Aborted);
        var destination = await db.Locations.AsNoTracking()
            .Where(l => l.Id == toLocationId)
            .Select(l => new { l.Id, l.Name })
            .FirstOrDefaultAsync(Aborted)
            ?? throw new ApiException(StatusCodes.Status404NotFound, "Destination location not found");
        if (asset.CurrentLocationId == toLocationId)
        {
            throw new ApiException(StatusCodes.Status400BadRequest, $"\"{asset.Name}\" is already at {destination.Name}");
        }

        var now = clock.UtcNow;
        var movement = new Movement
        {
            AssetId = assetId,
            FromLocationId = asset.CurrentLocationId,
            ToLocationId = toLocationId,
            TriggeredBy = MovementTriggers.Manual,
            MovedBy = string.IsNullOrEmpty(movedBy) ? null : movedBy,
            Notes = string.IsNullOrEmpty(notes) ? null : notes,
            Timestamp = now,
        };
        db.Movements.Add(movement);
        asset.CurrentLocationId = toLocationId;
        asset.UpdatedAt = now;
        await db.SaveChangesAsync(Aborted); // one transaction: the movement and the asset's new location

        return Status201(await db.Movements.AsNoTracking().Where(m => m.Id == movement.Id).Select(MovementService.ToDto).FirstAsync(Aborted));
    }

    [HttpGet("movements/{id}")]
    [RequireRole(Roles.GateLogReaders)]
    [FailureMessage("Failed to get movement")]
    public async Task<MovementDto> Get(string id)
    {
        var movementId = RequestValues.Id(id);
        var assetId = await db.Movements.IgnoreQueryFilters().Where(m => m.Id == movementId).Select(m => (int?)m.AssetId).FirstOrDefaultAsync(Aborted)
            ?? throw new ApiException(StatusCodes.Status404NotFound, "Movement not found");
        if (Caller.Role != Roles.GateOfficer) await access.EnsureVisibleAsync(Caller, assetId, Aborted);
        return await db.Movements.AsNoTracking().Where(m => m.Id == movementId).Select(MovementService.ToDto).FirstOrDefaultAsync(Aborted)
            ?? throw new ApiException(StatusCodes.Status404NotFound, "Movement not found");
    }
}
