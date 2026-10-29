using AssetIQ.Api.Services;
using System.Linq.Expressions;
using System.Text.Json;
using AssetIQ.Api.Auth;
using AssetIQ.Api.Contracts;
using AssetIQ.Api.Data;
using AssetIQ.Api.Domain;
using AssetIQ.Api.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;

namespace AssetIQ.Api.Controllers;

/// <summary>Business-meaningful asset events: deployments, returns, repairs, SBU transfers and notes.</summary>
public sealed class LifecycleController(AssetIqDbContext db, AppClock clock, AssetAccess access, AssetRegistrationService registration, AssetStatusService statuses) : ApiControllerBase
{

    private static readonly Expression<Func<AssetLifecycleEvent, LifecycleEventDto>> ToDto = e => new LifecycleEventDto
    {
        Id = e.Id,
        AssetId = e.AssetId,
        AssetName = e.Asset.Name,
        RfidTag = e.Asset.RfidTag ?? "",
        EventType = e.EventType,
        PerformedBy = e.PerformedBy,
        FromSbuId = e.FromSbuId,
        FromSbuName = e.FromSbu!.Name,
        ToSbuId = e.ToSbuId,
        ToSbuName = e.ToSbu!.Name,
        AssignedTo = e.AssignedTo,
        Vendor = e.Vendor,
        TicketRef = e.TicketRef,
        ConditionBefore = e.ConditionBefore,
        ConditionAfter = e.ConditionAfter,
        Notes = e.Notes,
        Timestamp = e.Timestamp,
    };

    [HttpGet("assets/{id}/lifecycle")]
    [RequireRole(Roles.RecordReaders)]
    [FailureMessage("Failed to list lifecycle events")]
    public async Task<List<LifecycleEventDto>> ListForAsset(string id)
    {
        var assetId = RequestValues.Id(id);
        await access.EnsureVisibleAsync(Caller, assetId, Aborted);
        return await db.AssetLifecycleEvents.AsNoTracking()
            .Where(e => e.AssetId == assetId)
            .OrderByDescending(e => e.Timestamp)
            .Select(ToDto)
            .ToListAsync(Aborted);
    }

    /// <summary>Records the event and applies what it implies to the asset (status, holder, owning SBU).</summary>
    [HttpPost("assets/{id}/lifecycle")]
    [RequireRole(Roles.LifecycleWriters)]
    [FailureMessage("Failed to create lifecycle event")]
    public async Task<IActionResult> Create(string id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] JsonElement? body)
    {
        var assetId = RequestValues.Id(id);
        var asset = await db.Assets.IgnoreQueryFilters().Include(a => a.AssetCategory).FirstOrDefaultAsync(a => a.Id == assetId, Aborted)
            ?? throw new ApiException(StatusCodes.Status404NotFound, "Asset not found");

        var input = JsonBody.From(body);
        var custodianEmployeeId = input.NullableInt("custodianEmployeeId");
        var eventType = input.NullableString("eventType").Value;
        var performedBy = input.NullableString("performedBy").Value;
        var fromSbuId = input.NullableInt("fromSbuId").Value;
        var toSbuId = input.NullableInt("toSbuId").Value;
        var assignedTo = input.NullableString("assignedTo").Value;
        var vendor = input.NullableString("vendor").Value;
        var ticketRef = input.NullableString("ticketRef").Value;
        var conditionBefore = input.NullableString("conditionBefore").Value;
        var conditionAfter = input.NullableString("conditionAfter").Value;
        var notes = input.NullableString("notes").Value;
        if (string.IsNullOrEmpty(eventType) || string.IsNullOrEmpty(performedBy))
        {
            throw new ApiException(StatusCodes.Status400BadRequest, "eventType and performedBy are required");
        }
        input.ThrowIfInvalid();
        if (!AccessPolicy.CanRecordLifecycle(Caller, eventType, asset, asset.AssetCategory.IsIt))
            throw AccessPolicy.Deny(Caller, eventType == LifecycleEventTypes.SbuTransfer
                ? "move an asset to another SBU (Group IT approves IT transfers, GI-05; SBU IT may only request one)"
                : $"record a {eventType.Replace('_', ' ')} for this asset");
        if (eventType == LifecycleEventTypes.SbuTransfer && toSbuId is not null && !await db.Sbus.AnyAsync(x => x.Id == toSbuId, Aborted))
            throw new ApiException(StatusCodes.Status404NotFound, "SBU not found");

        var now = clock.UtcNow;
        var lifecycleEvent = new AssetLifecycleEvent
        {
            AssetId = assetId,
            EventType = eventType,
            PerformedBy = performedBy,
            FromSbuId = fromSbuId,
            ToSbuId = toSbuId,
            AssignedTo = assignedTo,
            Vendor = vendor,
            TicketRef = ticketRef,
            ConditionBefore = conditionBefore,
            ConditionAfter = conditionAfter,
            Notes = notes,
            Timestamp = now,
        };
        db.AssetLifecycleEvents.Add(lifecycleEvent);

        var assetChanged = false;
        // Phase 3: the status each event implies, through the state machine (409 when the table forbids it).
        // A deployment of an asset already in use is a reassignment: its status stays.
        if (eventType == LifecycleEventTypes.Deployment) AssetRegistrationService.EnsureReassignable(asset);
        var status = eventType switch
        {
            LifecycleEventTypes.Deployment when asset.Status != AssetStatuses.InUse => AssetStatuses.InUse,
            LifecycleEventTypes.ReturnToIt => AssetStatuses.InStore,
            LifecycleEventTypes.RepairSent => AssetStatuses.InRepair,
            LifecycleEventTypes.RepairReturned => null, // decided below, once the holder is known
            LifecycleEventTypes.SbuTransfer when asset.Status == AssetStatuses.TransferPending => "decided below",
            _ => null,
        };
        if (status is not null && status != "decided below")
        {
            statuses.Change(Caller, asset, new StatusChange(status, notes));
            assetChanged = true;
        }
        if (eventType == LifecycleEventTypes.Deployment && custodianEmployeeId is { IsSet: true, Value: not null })
        {
            // Issued to an employee record: the holder's name and department follow the employee.
            await registration.AssignCustodianAsync(Caller, asset, custodianEmployeeId.Value, notes, Aborted);
            lifecycleEvent.AssignedTo = asset.AssignedTo;
            assetChanged = true;
        }
        else if (eventType == LifecycleEventTypes.Deployment && !string.IsNullOrEmpty(assignedTo))
        {
            asset.AssignedTo = assignedTo;
            assetChanged = true;
        }
        if (eventType == LifecycleEventTypes.ReturnToIt)
        {
            // SI-02: an unassigned IT item stays with SBU IT; a non-IT item goes to the SBU's Stores.
            asset.AssignedTo = AssetStatusService.StoreHolder(asset, await db.Sbus.Where(x => x.Id == asset.SbuId).Select(x => x.Code).FirstOrDefaultAsync(Aborted));
            asset.CustodianEmployeeId = null;
            asset.DepartmentId = null;
            assetChanged = true;
        }
        if (eventType == LifecycleEventTypes.SbuTransfer && toSbuId is > 0 or < 0)
        {
            asset.SbuId = toSbuId;
            assetChanged = true;
        }
        if (eventType == LifecycleEventTypes.RepairReturned || (eventType == LifecycleEventTypes.SbuTransfer && asset.Status == AssetStatuses.TransferPending))
        {
            statuses.Change(Caller, asset, new StatusChange(AssetStatusService.ReturnStatus(asset), notes));
            assetChanged = true;
        }
        if (assetChanged) asset.UpdatedAt = now;
        await db.SaveChangesAsync(Aborted);

        var dto = await db.AssetLifecycleEvents.AsNoTracking().Where(e => e.Id == lifecycleEvent.Id).Select(ToDto).FirstAsync(Aborted);
        return Status201(dto);
    }

    /// <summary>Events across all assets, newest first; optionally one event type or one SBU's assets.</summary>
    [HttpGet("lifecycle")]
    [RequireRole(Roles.RecordReaders)]
    [FailureMessage("Failed to list lifecycle events")]
    public Task<List<LifecycleEventDto>> List([FromQuery] string? eventType, [FromQuery] string? sbuId, [FromQuery] string? limit)
    {
        var take = Math.Clamp(RequestValues.OptionalInt(limit) ?? 50, 0, 200);
        var typeFilter = RequestValues.OptionalText(eventType);
        var sbuFilter = RequestValues.OptionalInt(sbuId);

        var query = db.AssetLifecycleEvents.AsNoTracking();
        if (typeFilter is not null) query = query.Where(e => e.EventType == typeFilter);
        if (sbuFilter is not null) query = query.Where(e => e.Asset.SbuId == sbuFilter);
        return query.OrderByDescending(e => e.Timestamp).Take(take).Select(ToDto).ToListAsync(Aborted);
    }
}
