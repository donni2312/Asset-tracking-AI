using AssetIQ.Api.Domain;
using AssetIQ.Api.Auth;
using AssetIQ.Api.Contracts;
using AssetIQ.Api.Infrastructure;
using AssetIQ.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace AssetIQ.Api.Controllers;

public sealed class DashboardController(DashboardService dashboard) : ApiControllerBase
{
    [HttpGet("dashboard/summary")]
    [RequireRole(Roles.RecordReaders)]
    [FailureMessage("Failed to get dashboard summary")]
    public Task<DashboardSummaryDto> Summary() => dashboard.GetSummaryAsync(Aborted);

    [HttpGet("dashboard/activity")]
    [RequireRole(Roles.RecordReaders)]
    [FailureMessage("Failed to get activity feed")]
    public Task<List<ActivityItemDto>> Activity([FromQuery] string? limit, [FromQuery] string? sbuId) =>
        dashboard.GetActivityAsync(Math.Clamp(RequestValues.OptionalInt(limit) ?? 20, 0, 100), RequestValues.OptionalInt(sbuId), Aborted);

    [HttpGet("dashboard/location-breakdown")]
    [RequireRole(Roles.RecordReaders)]
    [FailureMessage("Failed to get location breakdown")]
    public Task<List<LocationBreakdownDto>> LocationBreakdown([FromQuery] string? sbuId) =>
        dashboard.GetLocationBreakdownAsync(RequestValues.OptionalInt(sbuId), Aborted);

    /// <summary>Daily movement counts by trigger for the last <c>days</c> days (1–90, default 14), oldest first.</summary>
    [HttpGet("dashboard/movement-trend")]
    [RequireRole(Roles.RecordReaders)]
    [FailureMessage("Failed to get movement trend")]
    public Task<List<MovementTrendPointDto>> MovementTrend([FromQuery] string? days)
    {
        var count = 14;
        if (days is not null)
        {
            var n = JsNumber.Parse(days);
            if (!JsNumber.IsInteger(n)) throw new ApiException(StatusCodes.Status400BadRequest, "days: Expected an integer");
            if (n < 1) throw new ApiException(StatusCodes.Status400BadRequest, "days: Number must be greater than or equal to 1");
            if (n > 90) throw new ApiException(StatusCodes.Status400BadRequest, "days: Number must be less than or equal to 90");
            count = (int)n;
        }
        return dashboard.GetMovementTrendAsync(count, Aborted);
    }

    /// <summary>Reader-detected movements (RFID scans and gate-pass exits).</summary>
    [HttpGet("dashboard/gate-activity")]
    [RequireRole(Roles.GateLogReaders)]
    [FailureMessage("Failed to get gate activity")]
    public Task<List<GateActivityItemDto>> GateActivity([FromQuery] string? limit)
    {
        var requested = RequestValues.OptionalInt(limit);
        return dashboard.GetGateActivityAsync(requested is > 0 ? Math.Min(requested.Value, 100) : 30, Aborted);
    }
}
