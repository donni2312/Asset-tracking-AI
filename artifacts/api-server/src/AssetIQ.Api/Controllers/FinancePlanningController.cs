using System.Text.Json;
using AssetIQ.Api.Auth;
using AssetIQ.Api.Contracts;
using AssetIQ.Api.Domain;
using AssetIQ.Api.Infrastructure;
using AssetIQ.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace AssetIQ.Api.Controllers;

/// <summary>Phase 5a: revaluations and impairments (SF-07), CAPEX budgets (SF-09, GF-05), verification outcomes (GF-08).</summary>
public sealed class FinancePlanningController(FinancePlanningService finance, AssetAccess access) : ApiControllerBase
{
    [HttpGet("assets/{id}/revaluations")]
    [RequireRole(Roles.FinanceReaders)]
    [FailureMessage("Failed to list the revaluations")]
    public async Task<List<RevaluationDto>> Revaluations(string id)
    {
        var assetId = RequestValues.Id(id);
        await access.EnsureVisibleAsync(Caller, assetId, Aborted);
        return await finance.RevaluationsAsync(assetId, Aborted);
    }

    /// <summary>SF-07: body { kind: revaluation | impairment, newValue (revaluation), impairmentAmount (impairment), revisedLifeMonths?, reason, attachmentId }.</summary>
    [HttpPost("assets/{id}/revaluations")]
    [RequireRole(Roles.SbuFinance)]
    [FailureMessage("Failed to record the revaluation")]
    public async Task<IActionResult> Revalue(string id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] JsonElement? body)
    {
        var input = JsonBody.From(body);
        var request = new RevaluationInput(
            input.RequiredEnum("kind", ["revaluation", "impairment"]), input.OptionalNumber("newValue"), input.OptionalNumber("impairmentAmount"),
            input.OptionalInt("revisedLifeMonths"), input.RequiredString("reason", minLength: 3), input.RequiredInt("attachmentId"));
        input.ThrowIfInvalid();
        var assetId = RequestValues.Id(id);
        await finance.RevalueAsync(Caller, assetId, request, Aborted);
        return Status201(await finance.RevaluationsAsync(assetId, Aborted));
    }

    /// <summary>GF-05 / SF-09: budget against capitalised spend for a financial year (default: the current one).</summary>
    [HttpGet("capex")]
    [RequireRole(Roles.FinanceReaders)]
    [FailureMessage("Failed to load the CAPEX report")]
    public Task<CapexReportDto> Capex([FromQuery] string? financialYear, [FromQuery] string? sbuId) =>
        finance.CapexAsync(Caller, RequestValues.OptionalInt(financialYear) ?? finance.CurrentFinancialYear, RequestValues.OptionalInt(sbuId), Aborted);

    /// <summary>SF-09: body { sbuId?, financialYear, lines: [{ categoryId, amount }] }.</summary>
    [HttpPut("capex/budgets")]
    [RequireRole(Roles.FinanceWriters)]
    [FailureMessage("Failed to save the CAPEX budget")]
    public async Task<CapexReportDto> SetBudget([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] JsonElement? body)
    {
        var input = JsonBody.From(body);
        var sbuId = input.OptionalInt("sbuId") ?? Caller.SbuId ?? 0;
        var year = input.RequiredInt("financialYear");
        input.ThrowIfInvalid();
        var lines = new List<CapexLineInput>();
        if (body is { ValueKind: JsonValueKind.Object } b && b.TryGetProperty("lines", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var x in arr.EnumerateArray())
            {
                var line = JsonBody.From(x);
                var categoryId = line.RequiredInt("categoryId");
                var amount = line.OptionalNumber("amount");
                line.ThrowIfInvalid();
                lines.Add(new CapexLineInput(categoryId, amount ?? throw new ApiException(StatusCodes.Status400BadRequest, "lines.amount: Required")));
            }
        }
        await finance.SetBudgetAsync(Caller, sbuId, year, lines, Aborted);
        return await finance.CapexAsync(Caller, year, Roles.IsSbuScoped(Caller.Role) ? null : sbuId, Aborted);
    }

    /// <summary>GF-08: the results of verification counts per SBU, with values and the missing assets.</summary>
    [HttpGet("verification-outcomes")]
    [RequireRole(Roles.FinanceReaders)]
    [FailureMessage("Failed to load the verification results")]
    public Task<List<VerificationOutcomeDto>> VerificationOutcomes([FromQuery] string? sbuId) =>
        finance.VerificationOutcomesAsync(Caller, RequestValues.OptionalInt(sbuId), Aborted);
}
