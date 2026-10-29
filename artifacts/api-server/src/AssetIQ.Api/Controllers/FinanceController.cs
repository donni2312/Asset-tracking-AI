using System.Text.Json;
using AssetIQ.Api.Auth;
using AssetIQ.Api.Contracts;
using AssetIQ.Api.Data;
using AssetIQ.Api.Domain;
using AssetIQ.Api.Infrastructure;
using AssetIQ.Api.Services;
using AssetIQ.Api.Services.Depreciation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;

namespace AssetIQ.Api.Controllers;

/// <summary>
/// Register, category report (AU-09), management dashboard (AU-10), completeness (AU-11), the approvals inbox
/// and the depreciation run / month locks (AU-05, SF-03, GF-03). Everything is filtered to what the caller may
/// see, in the JSON and in every export; every export is logged (AU-12).
/// </summary>
public sealed class FinanceController(
    AssetIqDbContext db, FinanceReportService reports, CompletenessService completeness, DepreciationService depreciation, AccessLog accessLog)
    : ApiControllerBase
{
    private ReportQuery Query(string? period, string? sbuId, string? locationId, string? categoryId)
    {
        var p = depreciation.CurrentPeriod;
        if (!string.IsNullOrWhiteSpace(period))
        {
            if (!Period.TryParse(period, out p) || period.Trim().Length != 7) throw new ApiException(StatusCodes.Status400BadRequest, "period: a month (YYYY-MM)");
            if (p > depreciation.CurrentPeriod) throw new ApiException(StatusCodes.Status400BadRequest, "period: cannot be a future month");
        }
        return new ReportQuery(p, RequestValues.OptionalInt(sbuId), RequestValues.OptionalInt(locationId), RequestValues.OptionalInt(categoryId));
    }

    [HttpGet("finance/register")]
    [RequireRole(Roles.FinanceReaders)]
    [FailureMessage("Failed to load the asset register")]
    public Task<RegisterReportDto> Register([FromQuery] string? period, [FromQuery] string? sbuId, [FromQuery] string? locationId, [FromQuery] string? categoryId) =>
        reports.RegisterAsync(Caller, Query(period, sbuId, locationId, categoryId), Aborted);

    [HttpGet("finance/register.{format:regex(^(xlsx|pdf)$)}")]
    [RequireRole(Roles.FinanceReaders)]
    [FailureMessage("Failed to export the asset register")]
    public async Task<IActionResult> RegisterExport(string format, [FromQuery] string? period, [FromQuery] string? sbuId, [FromQuery] string? locationId, [FromQuery] string? categoryId)
    {
        var query = Query(period, sbuId, locationId, categoryId);
        var table = await reports.RegisterTableAsync(Caller, query, Aborted);
        await accessLog.ExportAsync(Caller, $"asset register {format}", Request, Aborted);
        return Export(table, format, $"asset-register-{query.Period}");
    }

    [HttpGet("finance/category-report")]
    [RequireRole(Roles.FinanceReaders)]
    [FailureMessage("Failed to load the category report")]
    public Task<CategoryReportDto> CategoryReport([FromQuery] string? period, [FromQuery] string? sbuId, [FromQuery] string? locationId, [FromQuery] string? categoryId) =>
        reports.CategoryReportAsync(Caller, Query(period, sbuId, locationId, categoryId), Aborted);

    [HttpGet("finance/category-report.{format:regex(^(xlsx|pdf)$)}")]
    [RequireRole(Roles.FinanceReaders)]
    [FailureMessage("Failed to export the category report")]
    public async Task<IActionResult> CategoryReportExport(string format, [FromQuery] string? period, [FromQuery] string? sbuId, [FromQuery] string? locationId, [FromQuery] string? categoryId)
    {
        var query = Query(period, sbuId, locationId, categoryId);
        var table = await reports.CategoryReportTableAsync(Caller, query, Aborted);
        await accessLog.ExportAsync(Caller, $"category report {format}", Request, Aborted);
        return Export(table, format, $"category-report-{query.Period}");
    }

    [HttpGet("finance/dashboard")]
    [RequireRole(Roles.FinanceReaders)]
    [FailureMessage("Failed to load the finance dashboard")]
    public Task<FinanceDashboardDto> Dashboard([FromQuery] string? period, [FromQuery] string? sbuId) =>
        reports.DashboardAsync(Caller, Query(period, sbuId, null, null), Aborted);

    [HttpGet("finance/completeness")]
    [RequireRole(Roles.FinanceReaders)]
    [FailureMessage("Failed to load the completeness report")]
    public async Task<CompletenessReportDto> Completeness([FromQuery] string? sbuId, [FromQuery] string? categoryId)
    {
        var report = await completeness.ReportAsync(Caller, RequestValues.OptionalInt(sbuId), RequestValues.OptionalInt(categoryId), Aborted);
        return new CompletenessReportDto(report.Assets, report.Complete, report.PercentComplete,
            report.Sbus.Select(s => new SbuCompletenessDto(s.SbuId, s.SbuCode, s.Assets, s.Complete, s.PercentComplete)).ToList(),
            report.Incomplete.Select(r => new CompletenessRowDto(r.AssetId, r.AssetNumber, r.Description, r.SbuCode, r.CategoryName,
                r.Gaps.Select(g => new CompletenessGapDto(g.Field, g.Message, g.Owner)).ToList())).ToList());
    }

    [HttpGet("approvals")]
    [RequireRole(Roles.FinanceReaders)]
    [FailureMessage("Failed to load approvals")]
    public Task<List<ApprovalItemDto>> Approvals() => reports.ApprovalsAsync(Caller, Aborted);

    /// <summary>SF-03 / GF-03: the monthly run (SBU Finance for its SBU, Group Finance for any SBU or the Group).</summary>
    [HttpPost("depreciation/run")]
    [RequireRole(Roles.FinanceWriters)]
    [FailureMessage("Depreciation run failed")]
    public async Task<DepreciationRunDto> Run([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] JsonElement? body)
    {
        var input = JsonBody.From(body);
        var period = PeriodOf(input.RequiredString("period"));
        var sbuId = input.OptionalInt("sbuId") ?? (Caller.Role == Roles.SbuFinance ? Caller.SbuId : null);
        input.ThrowIfInvalid();
        var result = await depreciation.RunAsync(Caller, sbuId, period, Aborted);
        return new DepreciationRunDto(result.Period, result.SbuId, result.Assets, result.MonthsWritten, result.ChargeForPeriod);
    }

    [HttpGet("depreciation/locks")]
    [RequireRole(Roles.FinanceReaders)]
    [FailureMessage("Failed to list locked months")]
    public Task<List<DepreciationLockDto>> Locks() =>
        db.DepreciationLocks.AsNoTracking().OrderBy(l => l.SbuId).ThenByDescending(l => l.Period)
            .Select(l => new DepreciationLockDto(l.Id, l.SbuId, l.Period, l.LockedBy, l.LockedAt)).ToListAsync(Aborted);

    /// <summary>SF-03: SBU Finance locks a reviewed month; it is never recalculated afterwards.</summary>
    [HttpPost("depreciation/locks")]
    [RequireRole(Roles.SbuFinance)]
    [FailureMessage("Failed to lock the month")]
    public async Task<IActionResult> Lock([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] JsonElement? body)
    {
        var input = JsonBody.From(body);
        var period = PeriodOf(input.RequiredString("period"));
        var sbuId = input.OptionalInt("sbuId") ?? Caller.SbuId ?? 0;
        input.ThrowIfInvalid();
        await depreciation.LockAsync(Caller, sbuId, period, Aborted);
        var created = await db.DepreciationLocks.AsNoTracking().FirstAsync(l => l.SbuId == sbuId && l.Period == period.ToString(), Aborted);
        return Status201(new DepreciationLockDto(created.Id, created.SbuId, created.Period, created.LockedBy, created.LockedAt));
    }

    private static Period PeriodOf(string text) =>
        Period.TryParse(text, out var p) && text.Trim().Length == 7 ? p : throw new ApiException(StatusCodes.Status400BadRequest, "period: a month (YYYY-MM)");

    private FileContentResult Export(ExportTable table, string format, string name) => format == "xlsx"
        ? Download(TableExport.Excel(table), TableExport.ExcelContentType, $"{name}.xlsx")
        : Download(TableExport.Pdf(table), TableExport.PdfContentType, $"{name}.pdf");
}
