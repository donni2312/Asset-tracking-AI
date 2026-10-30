using AssetIQ.Api.Auth;
using AssetIQ.Api.Contracts;
using AssetIQ.Api.Data;
using AssetIQ.Api.Domain;
using AssetIQ.Api.Infrastructure;
using AssetIQ.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AssetIQ.Api.Controllers;

/// <summary>Phase 4d, SF-10: the import template, the validation report, and the import itself.</summary>
public sealed class ImportController(AssetIqDbContext db, AssetRegistrationService registration, AccessLog accessLog) : ApiControllerBase
{
    private const long MaxBytes = 10 * 1024 * 1024;

    /// <summary>The template: the standard columns, then the fields of the chosen category, with an example row.</summary>
    [HttpGet("imports/assets/template.{format:regex(^(csv|xlsx)$)}")]
    [RequireRole(Roles.Importers)]
    [FailureMessage("Failed to build the import template")]
    public async Task<IActionResult> Template(string format, [FromQuery] string? categoryId)
    {
        var id = RequestValues.OptionalInt(categoryId) ?? throw new ApiException(StatusCodes.Status400BadRequest, "categoryId: Required");
        var template = await registration.LoadTemplateAsync(id, Aborted);
        var fields = template.Fields.Select(f => (f.Key, $"{f.Label}{(f.Unit is null ? "" : $" ({f.Unit})")}{(f.Required ? " — required" : "")}{(f.AllowedValues is null ? "" : $": {string.Join(", ", f.AllowedValues)}")}")).ToList();
        var columns = AssetRegistrationService.ImportColumns.Select(c => c.Column).Concat(fields.Select(f => f.Key)).ToList();
        var sbu = Caller.SbuId is { } sbuId ? await db.Sbus.AsNoTracking().Where(s => s.Id == sbuId).Select(s => s.Code).FirstOrDefaultAsync(Aborted) : null;
        var example = columns.Select(c => c switch
        {
            "category" => template.Code,
            "sub_category" => template.SubCategories.FirstOrDefault() ?? "",
            "sbu" => sbu ?? "",
            "condition" => "good",
            "currency" => "USD",
            "exchange_rate" => "1",
            _ => "",
        }).ToList();
        var name = $"asset-import-{template.Code.ToLowerInvariant()}";
        return format == "csv"
            ? Download(SpreadsheetRows.TemplateCsv(columns, example), "text/csv", $"{name}.csv")
            : Download(SpreadsheetRows.TemplateExcel(columns, example, [.. AssetRegistrationService.ImportColumns, .. fields]), TableExport.ExcelContentType, $"{name}.xlsx");
    }

    /// <summary>SF-10: the validation report for an uploaded CSV / Excel file; nothing is saved.</summary>
    [HttpPost("imports/assets/validate")]
    [RequireRole(Roles.Importers)]
    [FailureMessage("Failed to check the file")]
    public async Task<ImportReportDto> Validate(IFormFile? file) =>
        await registration.ImportAsync(Caller, Read(file), commit: false, Aborted);

    /// <summary>SF-10: imports the file when the report shows no error (all rows or none); otherwise 409 with the report.</summary>
    [HttpPost("imports/assets")]
    [RequireRole(Roles.Importers)]
    [FailureMessage("Failed to import the file")]
    public async Task<IActionResult> Import(IFormFile? file)
    {
        var report = await registration.ImportAsync(Caller, Read(file), commit: true, Aborted);
        if (!report.CanImport) return new ObjectResult(report) { StatusCode = StatusCodes.Status409Conflict };
        await accessLog.ExportAsync(Caller, $"asset import ({report.Imported} assets)", Request, Aborted);
        return Status201(report);
    }

    private static List<SheetRow> Read(IFormFile? file)
    {
        if (file is null || file.Length == 0) throw new ApiException(StatusCodes.Status400BadRequest, "file: Required");
        if (file.Length > MaxBytes) throw new ApiException(StatusCodes.Status400BadRequest, "file: at most 10 MB");
        if (!file.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) && !file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            throw new ApiException(StatusCodes.Status400BadRequest, "file: a .csv or .xlsx file");
        using var stream = file.OpenReadStream();
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        copy.Position = 0;
        return SpreadsheetRows.Read(copy, file.FileName);
    }
}
