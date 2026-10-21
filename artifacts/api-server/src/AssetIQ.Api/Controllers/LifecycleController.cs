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
public sealed class LifecycleController(AssetIqDbContext db, AppClock clock) : ApiControllerBase
{
    /// <summary>The asset status each event type implies.</summary>
    private static readonly Dictionary<string, string> StatusAfterEvent = new()
    {
        [LifecycleEventTypes.Deployment] = AssetStatuses.Active,
        [LifecycleEventTypes.ReturnToIt] = AssetStatuses.Active,
        [LifecycleEventTypes.RepairSent] = AssetStatuses.Maintenance,
        [LifecycleEventTypes.RepairReturned] = AssetStatuses.Active,
    };

    private static readonly Expression<Func<AssetLifecycleEvent, LifecycleEventDto>> ToDto = e => new LifecycleEventDto
    {
        Id = e.Id,
        AssetId = e.AssetId,
        AssetName = e.Asset.Name,
        RfidTag = e.Asset.RfidTag,
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
    [FailureMessage("Failed to list lifecycle events")]
    public Task<List<LifecycleEventDto>> ListForAsset(string id)
    {
        var assetId = RequestValues.Id(id);
        return db.AssetLifecycleEvents.AsNoTracking()
            .Where(e => e.AssetId == assetId)
            .OrderByDescending(e => e.Timestamp)
            .Select(ToDto)
            .ToListAsync(Aborted);
    }

    /// <summary>Records the event and applies what it implies to the asset (status, holder, owning SBU).</summary>
    [HttpPost("assets/{id}/lifecycle")]
    [RequireRole(Roles.Operator)]
    [FailureMessage("Failed to create lifecycle event")]
    public async Task<IActionResult> Create(string id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] JsonElement? body)
    {
        var assetId = RequestValues.Id(id);
        var asset = await db.Assets.FirstOrDefaultAsync(a => a.Id == assetId, Aborted)
            ?? throw new ApiException(StatusCodes.Status404NotFound, "Asset not found");

        var input = JsonBody.From(body);
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
        if (StatusAfterEvent.TryGetValue(eventType, out var status))
        {
            asset.Status = status;
            assetChanged = true;
        }
        if (eventType == LifecycleEventTypes.Deployment && !string.IsNullOrEmpty(assignedTo))
        {
            asset.AssignedTo = assignedTo;
            assetChanged = true;
        }
        if (eventType == LifecycleEventTypes.ReturnToIt)
        {
            asset.AssignedTo = null;
            assetChanged = true;
        }
        if (eventType == LifecycleEventTypes.SbuTransfer && toSbuId is > 0 or < 0)
        {
            asset.SbuId = toSbuId;
            assetChanged = true;
        }
        if (assetChanged) asset.UpdatedAt = now;
        await db.SaveChangesAsync(Aborted);

        var dto = await db.AssetLifecycleEvents.AsNoTracking().Where(e => e.Id == lifecycleEvent.Id).Select(ToDto).FirstAsync(Aborted);
        return Status201(dto);
    }

    /// <summary>Events across all assets, newest first; optionally one event type or one SBU's assets.</summary>
    [HttpGet("lifecycle")]
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
