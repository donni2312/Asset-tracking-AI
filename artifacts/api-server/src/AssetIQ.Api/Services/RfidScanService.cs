using AssetIQ.Api.Configuration;
using AssetIQ.Api.Contracts;
using AssetIQ.Api.Data;
using AssetIQ.Api.Domain;
using AssetIQ.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AssetIQ.Api.Services;

/// <param name="RfidTag">Tag the reader saw.</param>
/// <param name="ReaderId">The reader.</param>
/// <param name="ScannedAt">When the reader saw the tag; defaults to now (the simulator can replay past scans).</param>
public sealed record RfidScanInput(string RfidTag, int ReaderId, DateTime? ScannedAt);

/// <summary>
/// RFID processing pipeline (plan section 6.2). The simulator calls it today; an Azure IoT Hub
/// consumer would call exactly the same method in production:
/// 1. resolve asset + reader + location;
/// 2. gate check (gate readers only): authorised exit, entry, or auto-raise a pending pass;
/// 3. record the movement and update the asset's current location;
/// 4. evaluate the anomaly rules and persist any insights.
/// Everything runs in one transaction; the asset row is locked so concurrent scans of the same tag
/// cannot raise duplicate pending passes.
/// </summary>
public sealed class RfidScanService(
    AssetIqDbContext db,
    AppClock clock,
    AppConfig config,
    MovementService movements,
    GatePassService gatePasses,
    InsightService insights,
    ILogger<RfidScanService> logger)
{
    /// <summary>Scans further than this in the future are rejected (clock skew allowance).</summary>
    private static readonly TimeSpan MaxFutureSkew = TimeSpan.FromMinutes(5);

    public async Task<RfidScanResultDto> ProcessAsync(RfidScanInput input, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var zone = config.TimeZone;
        var scannedAt = input.ScannedAt ?? now;
        if (scannedAt - now > MaxFutureSkew) throw new ApiException(StatusCodes.Status400BadRequest, "scannedAt cannot be in the future");

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        // 1. Resolve asset, reader and the reader's location.
        var asset = (await db.Assets.FromSql($"SELECT * FROM assets WHERE rfid_tag = {input.RfidTag} FOR UPDATE").ToListAsync(ct)).FirstOrDefault()
            ?? throw new ApiException(StatusCodes.Status404NotFound, $"No asset with RFID tag: {input.RfidTag}");

        var reader = await db.RfidReaders.FirstOrDefaultAsync(r => r.Id == input.ReaderId, ct)
            ?? throw new ApiException(StatusCodes.Status404NotFound, "RFID reader not found");
        if (reader.Status != ReaderStatuses.Active)
        {
            throw new ApiException(StatusCodes.Status409Conflict, $"RFID reader \"{reader.Name}\" is inactive");
        }

        var location = await db.Locations.AsNoTracking()
            .Where(l => l.Id == reader.LocationId)
            .Select(l => new { l.Id, l.Name, l.Type, l.SbuId, SbuCode = l.Sbu!.Code, SbuName = l.Sbu!.Name })
            .FirstOrDefaultAsync(ct)
            ?? throw new ApiException(StatusCodes.Status404NotFound, "The reader's location no longer exists");

        var assetSbuCode = asset.SbuId is { } assetSbuId
            ? await db.Sbus.AsNoTracking().Where(s => s.Id == assetSbuId).Select(s => s.Code).FirstOrDefaultAsync(ct)
            : null;
        var fromLocationName = asset.CurrentLocationId is { } fromId
            ? await db.Locations.AsNoTracking().Where(l => l.Id == fromId).Select(l => l.Name).FirstOrDefaultAsync(ct)
            : null;

        // 2. Gate check.
        var isGate = location.Type == LocationTypes.Gate;
        var direction = GatePassRules.GateDirection(isGate, asset.CurrentLocationId is not null);
        GatePass? validPass = null;
        GatePass? pendingPass = null;
        if (isGate && direction == GatePassRules.Exit)
        {
            var approved = await db.GatePasses.AsNoTracking()
                .Where(p => p.AssetId == asset.Id && p.Status == GatePassStatuses.Approved)
                .ToListAsync(ct);
            validPass = GatePassRules.FindValidApprovedPass(approved, scannedAt, zone);
            if (validPass is null)
            {
                pendingPass = await db.GatePasses.AsNoTracking()
                    .Where(p => p.AssetId == asset.Id && p.Status == GatePassStatuses.Pending)
                    .OrderByDescending(p => p.CreatedAt)
                    .FirstOrDefaultAsync(ct);
            }
        }
        var outcome = GatePassRules.ResolveGateOutcome(isGate, direction, validPass is not null, pendingPass is not null);

        var gatePass = validPass ?? pendingPass;
        if (outcome == GatePassRules.PendingCreated)
        {
            gatePass = new GatePass
            {
                AssetId = asset.Id,
                RequestedBy = $"System (RFID gate reader: {reader.Name})",
                Reason = $"Auto-raised: \"{asset.Name}\" was detected leaving through {location.Name} without an approved gate pass." +
                         (string.IsNullOrEmpty(asset.AssignedTo) ? "" : $" Assigned holder: {asset.AssignedTo}."),
                Status = GatePassStatuses.Pending,
                Source = GatePassSources.RfidGate,
                GateLocationId = location.Id,
                CreatedAt = now,
                UpdatedAt = now,
            };
            db.GatePasses.Add(gatePass);
            await db.SaveChangesAsync(ct);
        }

        // 3. Movement + current location.
        Movement movement;
        if (outcome == GatePassRules.Authorized && validPass is not null)
        {
            // Approved exit: the asset leaves the premises under the pass.
            movement = await movements.RecordGatePassExitAsync(validPass, asset, location.Name, reader.Id, scannedAt, ct);
        }
        else
        {
            movement = new Movement
            {
                AssetId = asset.Id,
                FromLocationId = asset.CurrentLocationId,
                ToLocationId = location.Id,
                TriggeredBy = MovementTriggers.Rfid,
                RfidReaderId = reader.Id,
                GatePassId = gatePass?.Id,
                Notes = ScanNotes(reader.Name, outcome, gatePass?.Id),
                Timestamp = scannedAt,
            };
            db.Movements.Add(movement);
            await db.SaveChangesAsync(ct);
            await movements.ApplyLocationIfLatestAsync(asset, movement, location.Id, ct);
        }

        if (reader.LastReadAt is null || reader.LastReadAt < scannedAt)
        {
            reader.LastReadAt = scannedAt;
            await db.SaveChangesAsync(ct);
        }

        // 4. Anomaly rules.
        var drafts = AnomalyRules.EvaluateScan(new ScanContext(
            new ScanAsset(asset.Id, asset.Name, asset.RfidTag!, asset.SbuId, assetSbuCode),
            new ScanLocation(location.Id, location.Name, location.Type, location.SbuId, location.SbuCode),
            reader.Name,
            scannedAt,
            new ScanGate(outcome, gatePass?.Id),
            zone,
            config.WorkingHours));
        var raised = new List<AiInsight>();
        foreach (var draft in drafts)
        {
            var row = await insights.RaiseAsync(draft, InsightDedupe.Message, ct);
            if (row is not null) raised.Add(row);
        }

        var result = new RfidScanResultDto(
            new ScanMovementDto
            {
                Id = movement.Id,
                AssetId = movement.AssetId,
                AssetName = asset.Name,
                RfidTag = asset.RfidTag!,
                FromLocationId = movement.FromLocationId,
                FromLocationName = fromLocationName,
                ToLocationId = movement.ToLocationId,
                ToLocationName = movement.ToLocationId is null ? null : location.Name,
                TriggeredBy = movement.TriggeredBy,
                RfidReaderId = movement.RfidReaderId,
                GatePassId = movement.GatePassId,
                MovedBy = movement.MovedBy,
                Notes = movement.Notes,
                Timestamp = movement.Timestamp,
                SbuId = location.SbuId,
                SbuName = location.SbuName,
            },
            new GateCheckDto
            {
                IsGate = isGate,
                Direction = direction,
                Outcome = outcome,
                Authorized = GatePassRules.IsAuthorizedOutcome(outcome),
                GatePass = gatePass is null ? null : await gatePasses.ToDtoAsync(gatePass.Id, ct),
            },
            await insights.ToDtosAsync(raised, ct));

        await transaction.CommitAsync(ct);
        logger.LogInformation("RFID scan processed: asset {AssetId}, reader {ReaderId}, outcome {Outcome}, insights {Insights}",
            asset.Id, reader.Id, outcome, string.Join(",", raised.Select(r => r.Type)));
        return result;
    }

    private static string ScanNotes(string readerName, string outcome, int? gatePassId)
    {
        var text = $"Auto-detected by RFID reader: {readerName}";
        return outcome switch
        {
            GatePassRules.EntryOutcome => $"{text} — asset returning on site",
            GatePassRules.PendingCreated => $"{text} — no approved gate pass; pending gate pass #{gatePassId} raised and asset held at the gate",
            GatePassRules.PendingExisting => $"{text} — no approved gate pass; pending gate pass #{gatePassId} still awaiting approval",
            _ => text,
        };
    }
}
