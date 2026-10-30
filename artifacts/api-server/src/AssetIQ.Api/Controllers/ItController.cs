using AssetIQ.Api.Auth;
using AssetIQ.Api.Contracts;
using AssetIQ.Api.Domain;
using AssetIQ.Api.Infrastructure;
using AssetIQ.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace AssetIQ.Api.Controllers;

/// <summary>Phase 4d: IT inventory (GI-01), hardware refresh (GI-03) and the SBU IT status board (SI-06).</summary>
public sealed class ItController(ItReportService it, AccessLog accessLog) : ApiControllerBase
{
    [HttpGet("it/inventory")]
    [RequireRole(Roles.ItReaders)]
    [FailureMessage("Failed to load the IT inventory")]
    public Task<List<ItInventoryRowDto>> Inventory([FromQuery] string? sbuId, [FromQuery] string? type, [FromQuery] string? make, [FromQuery] string? model,
        [FromQuery] string? minAgeYears, [FromQuery] string? status) =>
        it.InventoryAsync(Caller, Filter(sbuId, type, make, model, minAgeYears, status), Aborted);

    [HttpGet("it/inventory.xlsx")]
    [RequireRole(Roles.ItReaders)]
    [FailureMessage("Failed to export the IT inventory")]
    public async Task<IActionResult> InventoryExport([FromQuery] string? sbuId, [FromQuery] string? type, [FromQuery] string? make, [FromQuery] string? model,
        [FromQuery] string? minAgeYears, [FromQuery] string? status)
    {
        var rows = await it.InventoryAsync(Caller, Filter(sbuId, type, make, model, minAgeYears, status), Aborted);
        await accessLog.ExportAsync(Caller, "IT inventory xlsx", Request, Aborted);
        var table = ItReportService.InventoryTable("IT inventory", [$"Prepared for {Caller.Name} ({Roles.DisplayName(Caller.Role)})"], rows, refresh: false);
        return Download(TableExport.Excel(table), TableExport.ExcelContentType, "it-inventory.xlsx");
    }

    [HttpGet("it/refresh")]
    [RequireRole(Roles.ItReaders)]
    [FailureMessage("Failed to load the refresh report")]
    public Task<List<ItInventoryRowDto>> Refresh([FromQuery] string? sbuId) => it.RefreshAsync(Caller, RequestValues.OptionalInt(sbuId), Aborted);

    [HttpGet("it/refresh.xlsx")]
    [RequireRole(Roles.ItReaders)]
    [FailureMessage("Failed to export the refresh report")]
    public async Task<IActionResult> RefreshExport([FromQuery] string? sbuId)
    {
        var rows = await it.RefreshAsync(Caller, RequestValues.OptionalInt(sbuId), Aborted);
        await accessLog.ExportAsync(Caller, "IT refresh xlsx", Request, Aborted);
        var table = ItReportService.InventoryTable("Hardware refresh", ["Devices past their refresh age or out of warranty"], rows, refresh: true);
        return Download(TableExport.Excel(table), TableExport.ExcelContentType, "it-refresh.xlsx");
    }

    [HttpGet("it/status-board")]
    [RequireRole(Roles.ItReaders)]
    [FailureMessage("Failed to load the status board")]
    public Task<ItStatusBoardDto> StatusBoard([FromQuery] string? departmentId, [FromQuery] string? locationId) =>
        it.StatusBoardAsync(Caller, RequestValues.OptionalInt(departmentId), RequestValues.OptionalInt(locationId), Aborted);

    private static InventoryFilter Filter(string? sbuId, string? type, string? make, string? model, string? minAgeYears, string? status) => new(
        RequestValues.OptionalInt(sbuId), RequestValues.OptionalText(type), RequestValues.OptionalText(make), RequestValues.OptionalText(model),
        RequestValues.OptionalInt(minAgeYears), RequestValues.OptionalChoice(status, AssetStatuses.All));
}
