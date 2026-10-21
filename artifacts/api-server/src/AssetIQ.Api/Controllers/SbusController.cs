using System.Text.Json;
using AssetIQ.Api.Contracts;
using AssetIQ.Api.Data;
using AssetIQ.Api.Infrastructure;
using AssetIQ.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;

namespace AssetIQ.Api.Controllers;

public sealed class SbusController(AssetIqDbContext db, AppClock clock, AssetQueries assets) : ApiControllerBase
{
    [HttpGet("sbus")]
    [FailureMessage("Failed to list SBUs")]
    public Task<List<SbuDto>> List() => Project(db.Sbus.AsNoTracking()).ToListAsync(Aborted);

    [HttpPost("sbus")]
    [FailureMessage("Failed to create SBU")]
    public async Task<IActionResult> Create([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] JsonElement? body)
    {
        var input = JsonBody.From(body);
        var code = input.NullableString("code").Value;
        var name = input.NullableString("name").Value;
        var description = input.NullableString("description").Value;
        var headOffice = input.NullableString("headOffice").Value;
        var color = input.NullableString("color").Value;
        if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(name))
        {
            throw new ApiException(StatusCodes.Status400BadRequest, "code and name are required");
        }
        input.ThrowIfInvalid();

        var now = clock.UtcNow;
        var sbu = new Sbu
        {
            Code = code,
            Name = name,
            Description = description,
            HeadOffice = headOffice,
            Color = color ?? "#6B7280",
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Sbus.Add(sbu);
        await db.SaveChangesAsync(Aborted);
        return Status201(await GetDtoAsync(sbu.Id));
    }

    [HttpGet("sbus/{id}")]
    [FailureMessage("Failed to get SBU")]
    public Task<SbuDto> Get(string id) => GetDtoAsync(RequestValues.Id(id));

    /// <summary>code, name and color change only when given a value; description and headOffice whenever present (null clears them).</summary>
    [HttpPatch("sbus/{id}")]
    [FailureMessage("Failed to update SBU")]
    public async Task<SbuDto> Update(string id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] JsonElement? body)
    {
        var sbuId = RequestValues.Id(id);
        var input = JsonBody.From(body);
        var code = input.NullableString("code");
        var name = input.NullableString("name");
        var description = input.NullableString("description");
        var headOffice = input.NullableString("headOffice");
        var color = input.NullableString("color");
        input.ThrowIfInvalid();

        var sbu = await db.Sbus.FirstOrDefaultAsync(s => s.Id == sbuId, Aborted)
            ?? throw new ApiException(StatusCodes.Status404NotFound, "SBU not found");
        if (!string.IsNullOrEmpty(code.Value)) sbu.Code = code.Value;
        if (!string.IsNullOrEmpty(name.Value)) sbu.Name = name.Value;
        if (description.IsSet) sbu.Description = description.Value;
        if (headOffice.IsSet) sbu.HeadOffice = headOffice.Value;
        if (!string.IsNullOrEmpty(color.Value)) sbu.Color = color.Value;
        sbu.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync(Aborted);
        return await GetDtoAsync(sbuId);
    }

    [HttpGet("sbus/{id}/assets")]
    [FailureMessage("Failed to get SBU assets")]
    public Task<List<AssetDto>> Assets(string id)
    {
        var sbuId = RequestValues.Id(id);
        return assets.ListDetailedAsync(a => a.SbuId == sbuId, Aborted);
    }

    private async Task<SbuDto> GetDtoAsync(int id) =>
        await Project(db.Sbus.AsNoTracking().Where(s => s.Id == id)).FirstOrDefaultAsync(Aborted)
        ?? throw new ApiException(StatusCodes.Status404NotFound, "SBU not found");

    /// <summary>SBUs ordered by code, with how many assets and locations each owns.</summary>
    private IQueryable<SbuDto> Project(IQueryable<Sbu> sbus) => sbus
        .OrderBy(s => s.Code)
        .Select(s => new SbuDto
        {
            Id = s.Id,
            Code = s.Code,
            Name = s.Name,
            Description = s.Description,
            HeadOffice = s.HeadOffice,
            Color = s.Color,
            CreatedAt = s.CreatedAt,
            UpdatedAt = s.UpdatedAt,
            AssetCount = db.Assets.Count(a => a.SbuId == s.Id),
            LocationCount = db.Locations.Count(l => l.SbuId == s.Id),
        });
}
