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

public sealed class RfidController(AssetIqDbContext db, RfidScanService scans, AppClock clock, AuditTrail audit) : ApiControllerBase
{
    [HttpGet("rfid/readers")]
    [RequireRole(Roles.ReaderViewers)]
    [FailureMessage("Failed to list RFID readers")]
    public Task<List<RfidReaderDto>> Readers([FromQuery] string? sbuId)
    {
        var sbuFilter = RequestValues.OptionalInt(sbuId);
        var query = db.RfidReaders.AsNoTracking();
        if (Roles.IsSbuScoped(Caller.Role)) query = query.Where(r => r.Location.SbuId == Caller.SbuId);
        if (sbuFilter is not null) query = query.Where(r => r.Location.SbuId == sbuFilter);
        return query
            .OrderBy(r => r.Location.Sbu!.Code)
            .ThenBy(r => r.Name)
            .Select(r => new RfidReaderDto
            {
                Id = r.Id,
                Name = r.Name,
                LocationId = r.LocationId,
                LocationName = r.Location.Name,
                SbuId = r.Location.SbuId,
                SbuCode = r.Location.Sbu!.Code,
                SbuName = r.Location.Sbu!.Name,
                Status = r.Status,
                LastReadAt = r.LastReadAt,
            })
            .ToListAsync(Aborted);
    }

    /// <summary>Adds a reader at a location (a gate or any other read point); System Administrator.</summary>
    [HttpPost("rfid/readers")]
    [RequireRole(Roles.GateManagers)]
    [FailureMessage("Failed to add the reader")]
    public async Task<IActionResult> CreateReader([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] JsonElement? body)
    {
        var input = JsonBody.From(body);
        var name = input.RequiredString("name", minLength: 2).Trim();
        var locationId = input.RequiredInt("locationId");
        var status = input.OptionalEnum("status", ReaderStatuses.All) ?? ReaderStatuses.Active;
        input.ThrowIfInvalid();
        if (!AccessPolicy.CanManageGates(Caller)) throw AccessPolicy.Deny(Caller, "configure gates and readers");
        if (!await db.Locations.AnyAsync(l => l.Id == locationId, Aborted)) throw new ApiException(StatusCodes.Status404NotFound, "Location not found");
        var reader = new RfidReader { Name = name, LocationId = locationId, Status = status, CreatedAt = clock.UtcNow };
        db.RfidReaders.Add(reader);
        await db.SaveChangesAsync(Aborted);
        audit.Record(Caller, "rfid_reader", reader.Id, null, "created", "name", null, name, $"location {locationId}, {status}");
        await db.SaveChangesAsync(Aborted);
        return Status201((await Readers(null)).First(r => r.Id == reader.Id));
    }

    /// <summary>Rename, move to another location, or switch a reader on or off; System Administrator.</summary>
    [HttpPatch("rfid/readers/{id}")]
    [RequireRole(Roles.GateManagers)]
    [FailureMessage("Failed to update the reader")]
    public async Task<RfidReaderDto> UpdateReader(string id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] JsonElement? body)
    {
        var readerId = RequestValues.Id(id);
        var reader = await db.RfidReaders.FirstOrDefaultAsync(r => r.Id == readerId, Aborted)
            ?? throw new ApiException(StatusCodes.Status404NotFound, "Reader not found");
        var input = JsonBody.From(body);
        var name = input.OptionalString("name");
        var locationId = input.OptionalInt("locationId");
        var status = input.OptionalEnum("status", ReaderStatuses.All);
        input.ThrowIfInvalid();
        if (!AccessPolicy.CanManageGates(Caller)) throw AccessPolicy.Deny(Caller, "configure gates and readers");
        if (locationId is not null && !await db.Locations.AnyAsync(l => l.Id == locationId, Aborted)) throw new ApiException(StatusCodes.Status404NotFound, "Location not found");
        if (name is not null && audit.RecordChange(Caller, "rfid_reader", reader.Id, null, "name", reader.Name, name.Trim())) reader.Name = name.Trim();
        if (locationId is not null && audit.RecordChange(Caller, "rfid_reader", reader.Id, null, "locationId", reader.LocationId, locationId.Value)) reader.LocationId = locationId.Value;
        if (status is not null && audit.RecordChange(Caller, "rfid_reader", reader.Id, null, "status", reader.Status, status)) reader.Status = status;
        await db.SaveChangesAsync(Aborted);
        return (await Readers(null)).First(r => r.Id == reader.Id);
    }

    /// <summary>Processed exactly like a hardware read (see <see cref="RfidScanService"/>).</summary>
    [HttpPost("rfid/simulate")]
    [RequireRole(Roles.GroupIt)]
    [FailureMessage("RFID simulation failed")]
    public Task<RfidScanResultDto> Simulate([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] JsonElement? body)
    {
        var input = JsonBody.From(body);
        var scan = new RfidScanInput(input.RequiredString("rfidTag"), input.RequiredInt("readerId"), input.OptionalDate("scannedAt"));
        input.ThrowIfInvalid();
        return scans.ProcessAsync(scan, Aborted);
    }
}
