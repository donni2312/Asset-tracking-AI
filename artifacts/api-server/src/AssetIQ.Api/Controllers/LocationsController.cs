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

public sealed class LocationsController(AssetIqDbContext db, AppClock clock, AssetQueries assets) : ApiControllerBase
{
    [HttpGet("locations")]
    [FailureMessage("Failed to list locations")]
    public Task<List<LocationDto>> List([FromQuery] string? sbuId, [FromQuery] string? type)
    {
        var sbuFilter = RequestValues.OptionalInt(sbuId);
        var typeFilter = RequestValues.OptionalText(type);
        var query = db.Locations.AsNoTracking();
        if (sbuFilter is not null) query = query.Where(l => l.SbuId == sbuFilter);
        if (typeFilter is not null) query = query.Where(l => l.Type == typeFilter);
        return Project(query.OrderBy(l => l.Sbu!.Code).ThenBy(l => l.Name)).ToListAsync(Aborted);
    }

    [HttpPost("locations")]
    [RequireRole(Roles.Operator)]
    [FailureMessage("Failed to create location")]
    public async Task<IActionResult> Create([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] JsonElement? body)
    {
        var input = JsonBody.From(body);
        var name = input.RequiredString("name");
        var type = input.RequiredEnum("type", LocationTypes.All);
        var building = input.RequiredString("building");
        var floor = input.OptionalString("floor");
        var description = input.OptionalString("description");
        var sbuId = input.OptionalInt("sbuId");
        input.ThrowIfInvalid();

        var now = clock.UtcNow;
        var location = new Location
        {
            Name = name,
            Type = type,
            Building = building,
            Floor = floor,
            Description = description,
            SbuId = sbuId,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Locations.Add(location);
        await db.SaveChangesAsync(Aborted);
        // The same enriched shape as GET (SBU name/code, asset count).
        return Status201(await GetDtoAsync(location.Id));
    }

    [HttpGet("locations/{id}")]
    [FailureMessage("Failed to get location")]
    public Task<LocationDto> Get(string id) => GetDtoAsync(RequestValues.Id(id));

    [HttpPatch("locations/{id}")]
    [RequireRole(Roles.Operator)]
    [FailureMessage("Failed to update location")]
    public async Task<LocationDto> Update(string id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] JsonElement? body)
    {
        var locationId = RequestValues.Id(id);
        var input = JsonBody.From(body);
        var name = input.OptionalString("name");
        var type = input.OptionalEnum("type", LocationTypes.All);
        var building = input.OptionalString("building");
        var floor = input.NullableString("floor");
        var description = input.NullableString("description");
        var sbuId = input.NullableInt("sbuId");
        input.ThrowIfInvalid();

        var location = await db.Locations.FirstOrDefaultAsync(l => l.Id == locationId, Aborted)
            ?? throw new ApiException(StatusCodes.Status404NotFound, "Location not found");
        if (name is not null) location.Name = name;
        if (type is not null) location.Type = type;
        if (building is not null) location.Building = building;
        if (floor.IsSet) location.Floor = floor.Value;
        if (description.IsSet) location.Description = description.Value;
        if (sbuId.IsSet) location.SbuId = sbuId.Value;
        location.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync(Aborted);
        return await GetDtoAsync(locationId);
    }

    [HttpGet("locations/{id}/assets")]
    [FailureMessage("Failed to get location assets")]
    public Task<List<AssetDto>> Assets(string id)
    {
        var locationId = RequestValues.Id(id);
        return assets.ListDetailedAsync(a => a.CurrentLocationId == locationId, Aborted);
    }

    private async Task<LocationDto> GetDtoAsync(int id) =>
        await Project(db.Locations.AsNoTracking().Where(l => l.Id == id)).FirstOrDefaultAsync(Aborted)
        ?? throw new ApiException(StatusCodes.Status404NotFound, "Location not found");

    private IQueryable<LocationDto> Project(IQueryable<Location> locations) => locations.Select(l => new LocationDto
    {
        Id = l.Id,
        Name = l.Name,
        Type = l.Type,
        Building = l.Building,
        Floor = l.Floor,
        Description = l.Description,
        SbuId = l.SbuId,
        SbuName = l.Sbu!.Name,
        SbuCode = l.Sbu!.Code,
        CreatedAt = l.CreatedAt,
        AssetCount = db.Assets.Count(a => a.CurrentLocationId == l.Id),
    });
}
