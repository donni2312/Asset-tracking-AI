using System.Text.Json;
using AssetIQ.Api.Contracts;
using AssetIQ.Api.Data;
using AssetIQ.Api.Infrastructure;
using AssetIQ.Api.Services;
using AssetIQ.Api.Services.NlQuery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;

namespace AssetIQ.Api.Controllers;

public sealed class InsightsController(AssetIqDbContext db, InsightService insights, NlQueryService nlQuery, ILogger<InsightsController> logger)
    : ApiControllerBase
{
    /// <summary>Evaluates the periodic rules (idle / missing assets) first, then lists every insight.</summary>
    [HttpGet("insights")]
    [FailureMessage("Failed to list insights")]
    public async Task<List<InsightDto>> List()
    {
        await insights.RunPeriodicRulesAsync(Aborted);
        return await insights.ListAsync(Aborted);
    }

    [HttpPost("insights/{id}/dismiss")]
    [FailureMessage("Failed to dismiss insight")]
    public async Task<InsightDto> Dismiss(string id)
    {
        var insightId = RequestValues.Id(id);
        var updated = await db.AiInsights.Where(i => i.Id == insightId).ExecuteUpdateAsync(s => s.SetProperty(i => i.Dismissed, true), Aborted);
        if (updated == 0) throw new ApiException(StatusCodes.Status404NotFound, "Insight not found");
        return await db.AiInsights.AsNoTracking().Where(i => i.Id == insightId).Select(InsightService.ToDto).FirstAsync(Aborted);
    }

    /// <summary>Natural-language query (OpenAI intent parsing when configured, rules as the fallback).</summary>
    [HttpPost("ai/query")]
    [FailureMessage("AI query failed")]
    public async Task<AiQueryResultDto> Query([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] JsonElement? body)
    {
        var input = JsonBody.From(body);
        var question = input.RequiredString("question").Trim();
        input.ThrowIfInvalid();
        if (question.Length == 0) throw new ApiException(StatusCodes.Status400BadRequest, "question is required");

        var result = await nlQuery.AnswerAsync(question, Aborted);
        logger.LogInformation("AI query answered: intent {Intent}, source {Source}", result.Intent, result.Source);
        return result;
    }
}
