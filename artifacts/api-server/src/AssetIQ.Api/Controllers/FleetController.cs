using System.Text.Json;
using AssetIQ.Api.Auth;
using AssetIQ.Api.Contracts;
using AssetIQ.Api.Domain;
using AssetIQ.Api.Infrastructure;
using AssetIQ.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace AssetIQ.Api.Controllers;

/// <summary>Phase 4d: the fleet register, vehicle documents and service schedules (AM-02 … AM-04).</summary>
public sealed class FleetController(FleetService fleet) : ApiControllerBase
{
    private static readonly IReadOnlyList<string> Kinds = ["vehicles", "generators", "all"];

    [HttpGet("fleet")]
    [RequireRole(Roles.FleetReaders)]
    [FailureMessage("Failed to load the fleet register")]
    public Task<List<FleetItemDto>> List([FromQuery] string? sbuId, [FromQuery] string? kind) =>
        fleet.ListAsync(Caller, RequestValues.OptionalInt(sbuId), RequestValues.OptionalChoice(kind, Kinds), Aborted);

    [HttpGet("fleet/{assetId}")]
    [RequireRole(Roles.FleetReaders)]
    [FailureMessage("Failed to load the vehicle")]
    public Task<FleetItemDto> Get(string assetId) => fleet.GetAsync(Caller, RequestValues.Id(assetId), Aborted);

    /// <summary>Body { fleetStatus?: active | in_service | off_road, driverEmployeeId?, driverName?, reading? }.</summary>
    [HttpPatch("fleet/{assetId}")]
    [RequireRole(Roles.SbuAdmin)]
    [FailureMessage("Failed to update the vehicle")]
    public async Task<FleetItemDto> Update(string assetId, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] JsonElement? body)
    {
        var input = JsonBody.From(body);
        var status = input.Has("fleetStatus") ? new Optional<string?>(true, input.RequiredEnum("fleetStatus", FleetStatuses.All)) : default;
        var update = new FleetUpdate(status, input.NullableInt("driverEmployeeId"), input.NullableString("driverName"), input.OptionalInt("reading"));
        input.ThrowIfInvalid();
        var id = RequestValues.Id(assetId);
        await fleet.UpdateAsync(Caller, id, update, Aborted);
        return await fleet.GetAsync(Caller, id, Aborted);
    }

    /// <summary>AM-03: body { kind: licence | insurance | fitness | zinara, reference?, expiresOn: YYYY-MM-DD }.</summary>
    [HttpPost("fleet/{assetId}/documents")]
    [RequireRole(Roles.SbuAdmin)]
    [FailureMessage("Failed to record the document")]
    public async Task<IActionResult> AddDocument(string assetId, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] JsonElement? body)
    {
        var input = JsonBody.From(body);
        var kind = input.RequiredEnum("kind", VehicleDocumentKinds.All);
        var reference = input.OptionalString("reference");
        var expires = input.NullableDay("expiresOn");
        input.ThrowIfInvalid();
        if (expires.Value is null) throw new ApiException(StatusCodes.Status400BadRequest, "expiresOn: Required");
        var id = RequestValues.Id(assetId);
        await fleet.AddDocumentAsync(Caller, id, kind, reference, expires.Value, Aborted);
        return Status201(await fleet.GetAsync(Caller, id, Aborted));
    }

    /// <summary>AM-04: body { intervalReading? (km or hours), intervalDays?, startDate?, startReading? }.</summary>
    [HttpPut("fleet/{assetId}/service-schedule")]
    [RequireRole(Roles.SbuAdmin)]
    [FailureMessage("Failed to set the service schedule")]
    public async Task<FleetItemDto> Schedule(string assetId, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] JsonElement? body)
    {
        var input = JsonBody.From(body);
        var intervalReading = input.OptionalInt("intervalReading");
        var intervalDays = input.OptionalInt("intervalDays");
        var startDate = input.NullableDay("startDate");
        var startReading = input.OptionalInt("startReading");
        input.ThrowIfInvalid();
        var id = RequestValues.Id(assetId);
        await fleet.SetScheduleAsync(Caller, id, intervalReading, intervalDays, startDate.Value, startReading, Aborted);
        return await fleet.GetAsync(Caller, id, Aborted);
    }

    /// <summary>AM-04: body { serviceDate, reading?, garage, workDone }.</summary>
    [HttpPost("fleet/{assetId}/services")]
    [RequireRole(Roles.SbuAdmin)]
    [FailureMessage("Failed to log the service")]
    public async Task<IActionResult> LogService(string assetId, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] JsonElement? body)
    {
        var input = JsonBody.From(body);
        var date = input.NullableDay("serviceDate");
        var reading = input.OptionalInt("reading");
        var garage = input.RequiredString("garage", minLength: 2);
        var work = input.RequiredString("workDone", minLength: 3);
        input.ThrowIfInvalid();
        if (date.Value is null) throw new ApiException(StatusCodes.Status400BadRequest, "serviceDate: Required");
        var id = RequestValues.Id(assetId);
        await fleet.LogServiceAsync(Caller, id, date.Value, reading, garage, work, Aborted);
        return Status201(await fleet.GetAsync(Caller, id, Aborted));
    }

    /// <summary>Body { cost, currency } (SBU Finance).</summary>
    [HttpPatch("service-logs/{id}/cost")]
    [RequireRole(Roles.SbuFinance)]
    [FailureMessage("Failed to record the service cost")]
    public async Task<FleetItemDto> Cost(string id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] JsonElement? body)
    {
        var input = JsonBody.From(body);
        var cost = input.OptionalNumber("cost");
        var currency = input.OptionalString("currency") ?? "USD";
        input.ThrowIfInvalid();
        if (cost is null) throw new ApiException(StatusCodes.Status400BadRequest, "cost: Required");
        var assetId = await fleet.SetServiceCostAsync(Caller, RequestValues.Id(id), cost.Value, currency.ToUpperInvariant(), Aborted);
        return await fleet.GetAsync(Caller, assetId, Aborted);
    }
}
