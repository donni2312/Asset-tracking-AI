using System.Text.Json;
using AssetIQ.Api.Contracts;
using AssetIQ.Api.Data;
using AssetIQ.Api.Infrastructure;
using AssetIQ.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;

namespace AssetIQ.Api.Controllers;

public sealed class RfidController(AssetIqDbContext db, RfidScanService scans) : ApiControllerBase
{
    [HttpGet("rfid/readers")]
    [FailureMessage("Failed to list RFID readers")]
    public Task<List<RfidReaderDto>> Readers([FromQuery] string? sbuId)
    {
        var sbuFilter = RequestValues.OptionalInt(sbuId);
        var query = db.RfidReaders.AsNoTracking();
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

    /// <summary>Processed exactly like a hardware read (see <see cref="RfidScanService"/>).</summary>
    [HttpPost("rfid/simulate")]
    [FailureMessage("RFID simulation failed")]
    public Task<RfidScanResultDto> Simulate([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] JsonElement? body)
    {
        var input = JsonBody.From(body);
        var scan = new RfidScanInput(input.RequiredString("rfidTag"), input.RequiredInt("readerId"), input.OptionalDate("scannedAt"));
        input.ThrowIfInvalid();
        return scans.ProcessAsync(scan, Aborted);
    }
}
