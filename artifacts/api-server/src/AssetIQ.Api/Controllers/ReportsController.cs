using AssetIQ.Api.Auth;
using AssetIQ.Api.Domain;
using AssetIQ.Api.Infrastructure;
using AssetIQ.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace AssetIQ.Api.Controllers;

/// <summary>
/// CSV downloads, limited to what the caller may see (the query filter scopes every row) and recorded in
/// the access log (AU-12), since an export is where data leaves the system.
/// </summary>
public sealed class ReportsController(ReportService reports, AccessLog accessLog) : ApiControllerBase
{
    private const string CsvType = "text/csv; charset=utf-8";

    [HttpGet("reports/assets.csv")]
    [RequireRole(Roles.RecordReaders)]
    [FailureMessage("Failed to build the asset report")]
    public async Task<IActionResult> Assets(
        [FromQuery] string? status, [FromQuery] string? locationId, [FromQuery] string? sbuId,
        [FromQuery] string? category, [FromQuery] string? q) =>
        await CsvAsync(await reports.AssetsAsync(
            RequestValues.OptionalChoice(status, AssetStatuses.All), RequestValues.OptionalInt(locationId), RequestValues.OptionalInt(sbuId),
            RequestValues.OptionalText(category), RequestValues.OptionalText(q), Aborted));

    [HttpGet("reports/movements.csv")]
    [RequireRole(Roles.GateLogReaders)]
    [FailureMessage("Failed to build the movement report")]
    public async Task<IActionResult> Movements([FromQuery] string? from, [FromQuery] string? to, [FromQuery] string? assetId, [FromQuery] string? sbuId) =>
        await CsvAsync(await reports.MovementsAsync(DayRange.Parse(from, to), RequestValues.OptionalInt(assetId), RequestValues.OptionalInt(sbuId), Aborted));

    [HttpGet("reports/gate-passes.csv")]
    [RequireRole(Roles.GatePassReaders)]
    [FailureMessage("Failed to build the gate pass report")]
    public async Task<IActionResult> GatePasses([FromQuery] string? status, [FromQuery] string? sbuId, [FromQuery] string? from, [FromQuery] string? to) =>
        await CsvAsync(await reports.GatePassesAsync(
            RequestValues.OptionalChoice(status, GatePassStatuses.All), RequestValues.OptionalInt(sbuId), DayRange.Parse(from, to), Aborted));

    [HttpGet("reports/insights.csv")]
    [RequireRole(Roles.RecordReaders)]
    [FailureMessage("Failed to build the insights report")]
    public async Task<IActionResult> Insights([FromQuery] string? includeDismissed) =>
        await CsvAsync(await reports.InsightsAsync(!string.Equals(includeDismissed, "false", StringComparison.OrdinalIgnoreCase), Aborted));

    [HttpGet("reports/lifecycle.csv")]
    [RequireRole(Roles.RecordReaders)]
    [FailureMessage("Failed to build the lifecycle report")]
    public async Task<IActionResult> Lifecycle([FromQuery] string? eventType, [FromQuery] string? sbuId, [FromQuery] string? from, [FromQuery] string? to) =>
        await CsvAsync(await reports.LifecycleAsync(RequestValues.OptionalText(eventType), RequestValues.OptionalInt(sbuId), DayRange.Parse(from, to), Aborted));

    private async Task<FileContentResult> CsvAsync(CsvReport report)
    {
        await accessLog.ExportAsync(Caller, report.FileName, Request, Aborted);
        return File(report.Content, CsvType, report.FileName);
    }
}
