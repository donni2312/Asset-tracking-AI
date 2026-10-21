using AssetIQ.Api.Auth;
using AssetIQ.Api.Domain;
using AssetIQ.Api.Infrastructure;
using AssetIQ.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace AssetIQ.Api.Controllers;

/// <summary>
/// CSV downloads. Any signed-in user (viewer and up) may download: looking at a screen needs no sign-in,
/// but a bulk export is where the data leaves the system, so it is attributable to an account.
/// </summary>
[RequireRole(Roles.Viewer)]
public sealed class ReportsController(ReportService reports) : ApiControllerBase
{
    private const string CsvType = "text/csv; charset=utf-8";

    [HttpGet("reports/assets.csv")]
    [FailureMessage("Failed to build the asset report")]
    public async Task<IActionResult> Assets(
        [FromQuery] string? status, [FromQuery] string? locationId, [FromQuery] string? sbuId,
        [FromQuery] string? category, [FromQuery] string? q) =>
        Csv(await reports.AssetsAsync(
            RequestValues.OptionalChoice(status, AssetStatuses.All), RequestValues.OptionalInt(locationId), RequestValues.OptionalInt(sbuId),
            RequestValues.OptionalText(category), RequestValues.OptionalText(q), Aborted));

    [HttpGet("reports/movements.csv")]
    [FailureMessage("Failed to build the movement report")]
    public async Task<IActionResult> Movements([FromQuery] string? from, [FromQuery] string? to, [FromQuery] string? assetId, [FromQuery] string? sbuId) =>
        Csv(await reports.MovementsAsync(DayRange.Parse(from, to), RequestValues.OptionalInt(assetId), RequestValues.OptionalInt(sbuId), Aborted));

    [HttpGet("reports/gate-passes.csv")]
    [FailureMessage("Failed to build the gate pass report")]
    public async Task<IActionResult> GatePasses([FromQuery] string? status, [FromQuery] string? sbuId, [FromQuery] string? from, [FromQuery] string? to) =>
        Csv(await reports.GatePassesAsync(
            RequestValues.OptionalChoice(status, GatePassStatuses.All), RequestValues.OptionalInt(sbuId), DayRange.Parse(from, to), Aborted));

    [HttpGet("reports/insights.csv")]
    [FailureMessage("Failed to build the insights report")]
    public async Task<IActionResult> Insights([FromQuery] string? includeDismissed) =>
        Csv(await reports.InsightsAsync(!string.Equals(includeDismissed, "false", StringComparison.OrdinalIgnoreCase), Aborted));

    [HttpGet("reports/lifecycle.csv")]
    [FailureMessage("Failed to build the lifecycle report")]
    public async Task<IActionResult> Lifecycle([FromQuery] string? eventType, [FromQuery] string? sbuId, [FromQuery] string? from, [FromQuery] string? to) =>
        Csv(await reports.LifecycleAsync(RequestValues.OptionalText(eventType), RequestValues.OptionalInt(sbuId), DayRange.Parse(from, to), Aborted));

    private FileContentResult Csv(CsvReport report) => File(report.Content, CsvType, report.FileName);
}
