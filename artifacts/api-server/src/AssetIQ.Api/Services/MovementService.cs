using System.Linq.Expressions;
using AssetIQ.Api.Contracts;
using AssetIQ.Api.Data;
using AssetIQ.Api.Domain;
using AssetIQ.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AssetIQ.Api.Services;

public sealed class MovementService(AssetIqDbContext db, AppClock clock)
{
    public static readonly Expression<Func<Movement, MovementDto>> ToDto = m => new MovementDto
    {
        Id = m.Id,
        AssetId = m.AssetId,
        AssetName = m.Asset.Name,
        RfidTag = m.Asset.RfidTag,
        FromLocationId = m.FromLocationId,
        FromLocationName = m.FromLocation!.Name,
        ToLocationId = m.ToLocationId,
        ToLocationName = m.ToLocation!.Name,
        TriggeredBy = m.TriggeredBy,
        RfidReaderId = m.RfidReaderId,
        GatePassId = m.GatePassId,
        MovedBy = m.MovedBy,
        Notes = m.Notes,
        Timestamp = m.Timestamp,
    };

    /// <summary>
    /// Point the (tracked) asset at <paramref name="locationId"/> (null = off-premises) unless a later
    /// movement already exists. This keeps the current location correct when a scan is replayed with a
    /// back-dated timestamp (the simulator's "scan time").
    /// </summary>
    public async Task<bool> ApplyLocationIfLatestAsync(Asset asset, Movement movement, int? locationId, CancellationToken ct)
    {
        var later = await db.Movements.AnyAsync(
            m => m.AssetId == asset.Id && m.Id != movement.Id && m.Timestamp > movement.Timestamp, ct);
        if (later) return false;
        asset.CurrentLocationId = locationId;
        asset.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>Record the asset leaving the premises under <paramref name="pass"/> (to-location null = off-premises).</summary>
    public async Task<Movement> RecordGatePassExitAsync(
        GatePass pass, Asset asset, string gateName, int? rfidReaderId, DateTime timestamp, CancellationToken ct)
    {
        var details = new List<string> { $"Left the premises through {gateName} under gate pass #{pass.Id}" };
        if (!string.IsNullOrEmpty(pass.ApprovedBy)) details.Add($"approved by {pass.ApprovedBy}");
        if (!string.IsNullOrEmpty(pass.Destination)) details.Add($"destination: {pass.Destination}");
        if (!string.IsNullOrEmpty(pass.ReturnDate)) details.Add($"due back {pass.ReturnDate}");

        var movement = new Movement
        {
            AssetId = asset.Id,
            FromLocationId = asset.CurrentLocationId,
            ToLocationId = null,
            TriggeredBy = MovementTriggers.GatePass,
            RfidReaderId = rfidReaderId,
            GatePassId = pass.Id,
            MovedBy = pass.Source == GatePassSources.Manual ? pass.RequestedBy : asset.AssignedTo,
            Notes = string.Join(" — ", details),
            Timestamp = timestamp,
        };
        db.Movements.Add(movement);
        await db.SaveChangesAsync(ct);
        await ApplyLocationIfLatestAsync(asset, movement, null, ct);
        return movement;
    }
}
