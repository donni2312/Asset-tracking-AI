using AssetIQ.Api.Configuration;
using AssetIQ.Api.Contracts;
using AssetIQ.Api.Infrastructure;

namespace AssetIQ.Api.Services.NlQuery;

/// <summary>
/// Answers a natural-language question about the asset register. OpenAI (when OPENAI_API_KEY is set)
/// maps the question to an intent; the rule-based parser is used when OpenAI is not configured, fails,
/// or can only offer "help" for a question the rules understand.
/// </summary>
public sealed class NlQueryService(
    AppConfig config,
    AppClock clock,
    IntentExecutor executor,
    OpenAiIntentParser openAi,
    ILogger<NlQueryService> logger)
{
    public async Task<AiQueryResultDto> AnswerAsync(string question, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var intent = RuleParser.ParseQuestion(question);
        var source = "rules";

        if (config.OpenAi.ApiKey is not null)
        {
            try
            {
                var llmIntent = await openAi.ParseAsync(question, config.OpenAi, ct);
                if (llmIntent.Kind != IntentKinds.Help || intent.Kind == IntentKinds.Help)
                {
                    intent = llmIntent;
                    source = "openai";
                }
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning("OpenAI intent parsing failed — using the rule-based parser: {Reason}",
                    ex is OperationCanceledException ? "timed out" : ex.Message);
            }
        }

        var answer = await executor.ExecuteAsync(intent, now, config.TimeZone, ct);
        return new AiQueryResultDto
        {
            Question = question,
            Answer = answer.Answer,
            RelatedAssets = answer.RelatedAssets,
            RelatedGatePasses = answer.RelatedGatePasses,
            Intent = intent.Kind,
            Source = source,
            Timestamp = now,
        };
    }
}
