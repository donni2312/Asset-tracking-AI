import { getConfig } from "../../lib/config";
import { logger } from "../../lib/logger";
import { executeIntent, type QueryAnswer } from "./execute";
import type { Intent } from "./intent";
import { parseWithOpenAI } from "./openai-parser";
import { parseQuestion } from "./rule-parser";

export interface AiQueryResult extends QueryAnswer {
  question: string;
  intent: Intent["kind"];
  source: "rules" | "openai";
  timestamp: string;
}

/**
 * Answer a natural-language question about the asset register.
 * OpenAI (when OPENAI_API_KEY is set) maps the question to an intent; the
 * rule-based parser is used when OpenAI is not configured, fails, or can only
 * offer "help" for a question the rules understand.
 */
export async function answerQuestion(
  question: string,
  options: { now?: Date; fetchImpl?: typeof fetch } = {},
): Promise<AiQueryResult> {
  const now = options.now ?? new Date();
  const config = getConfig();

  let intent: Intent = parseQuestion(question);
  let source: AiQueryResult["source"] = "rules";

  if (config.openai.apiKey) {
    try {
      const llmIntent = await parseWithOpenAI(question, config.openai, options.fetchImpl ?? fetch);
      if (llmIntent.kind !== "help" || intent.kind === "help") {
        intent = llmIntent;
        source = "openai";
      }
    } catch (err) {
      logger.warn({ reason: err instanceof Error ? err.message : String(err) }, "OpenAI intent parsing failed — using the rule-based parser");
    }
  }

  const answer = await executeIntent(intent, now, config.timezone);
  return { question, ...answer, intent: intent.kind, source, timestamp: now.toISOString() };
}
