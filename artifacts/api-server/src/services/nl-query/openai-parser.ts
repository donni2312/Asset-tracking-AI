import { z } from "zod";
import type { OpenAIConfig } from "../../lib/config";
import {
  ASSET_STATUSES,
  CATEGORIES,
  GATE_PASS_STATUSES,
  INTENT_KINDS,
  PERIODS,
  categoryFromWord,
  type AssetStatusFilter,
  type GatePassStatusFilter,
  type Intent,
} from "./intent";

/**
 * Optional OpenAI layer: the model only translates the question into a
 * structured intent (JSON Schema structured output). The intent is then
 * executed by the same deterministic code as the rule-based parser, so:
 *  - only the question text is sent to OpenAI — no asset data leaves the system
 *  - answers are always computed from the database, never invented by the model
 *  - any failure (no key, timeout, HTTP error, invalid JSON) falls back to rules
 */

const STATUS_VALUES = [...GATE_PASS_STATUSES, ...ASSET_STATUSES] as const;

export const SYSTEM_PROMPT = `You translate questions about TSL AssetIQ, an enterprise asset register, into a JSON intent.
Choose exactly one intent:
- locate_asset: where a specific asset is. Put brand/model/category words in "terms" (e.g. ["dell","laptop"]) and a person's name in "assignee" if the asset is described by who holds it.
- assets_assigned_to: list the assets held by a person ("assignee" required).
- not_moved: assets that have not moved for a number of days ("days"; convert weeks/months to days, default 30).
- gate_passes: list gate passes. "status" is pending, approved, rejected or null for all; "period" is one of ${PERIODS.join(", ")} ("periodDays" only for last_n_days).
- assets_by_status: assets whose status is ${ASSET_STATUSES.join(", ")} ("status").
- category_count: how many / which assets of a category ("category" is one of ${CATEGORIES.join(", ")}).
- count_summary: overall totals.
- unassigned: assets not assigned to anyone.
- help: anything else.
Use null (or [] for terms) for fields that do not apply. Never invent asset names, tags or people.`;

/** Strict JSON schema sent to the Chat Completions API (every key required; null = not applicable). */
export const INTENT_JSON_SCHEMA = {
  type: "object",
  additionalProperties: false,
  required: ["intent", "terms", "assignee", "days", "status", "period", "periodDays", "category"],
  properties: {
    intent: { type: "string", enum: [...INTENT_KINDS] },
    terms: { type: "array", items: { type: "string" } },
    assignee: { type: ["string", "null"] },
    days: { type: ["integer", "null"] },
    status: { type: ["string", "null"], enum: [...STATUS_VALUES, null] },
    period: { type: ["string", "null"], enum: [...PERIODS, null] },
    periodDays: { type: ["integer", "null"] },
    category: { type: ["string", "null"] },
  },
} as const;

const FlatIntentSchema = z.object({
  intent: z.enum(INTENT_KINDS),
  terms: z.array(z.string()),
  assignee: z.string().nullable(),
  days: z.number().int().nullable(),
  status: z.enum(STATUS_VALUES).nullable(),
  period: z.enum(PERIODS).nullable(),
  periodDays: z.number().int().nullable(),
  category: z.string().nullable(),
});
export type FlatIntent = z.infer<typeof FlatIntentSchema>;

const isGatePassStatus = (s: string | null): s is GatePassStatusFilter =>
  s !== null && (GATE_PASS_STATUSES as readonly string[]).includes(s);
const isAssetStatus = (s: string | null): s is AssetStatusFilter => s !== null && (ASSET_STATUSES as readonly string[]).includes(s);

/** Convert the model's flat JSON into an Intent; throws when required parameters are missing. */
export function flatToIntent(flat: FlatIntent): Intent {
  const assignee = flat.assignee?.trim().toLowerCase() || null;
  switch (flat.intent) {
    case "locate_asset": {
      const terms = flat.terms.map((t) => t.trim().toLowerCase()).filter(Boolean);
      if (terms.length === 0 && !assignee) throw new Error("locate_asset needs terms or an assignee");
      return { kind: "locate_asset", terms, assignee };
    }
    case "assets_assigned_to":
      if (!assignee) throw new Error("assets_assigned_to needs an assignee");
      return { kind: "assets_assigned_to", assignee };
    case "not_moved":
      return { kind: "not_moved", days: flat.days && flat.days > 0 ? flat.days : 30 };
    case "gate_passes": {
      const period = flat.period ?? "all";
      return {
        kind: "gate_passes",
        status: isGatePassStatus(flat.status) ? flat.status : null,
        period,
        periodDays: period === "last_n_days" ? Math.max(1, flat.periodDays ?? 7) : null,
      };
    }
    case "assets_by_status":
      if (!isAssetStatus(flat.status)) throw new Error("assets_by_status needs an asset status");
      return { kind: "assets_by_status", status: flat.status };
    case "category_count": {
      const category = flat.category ? categoryFromWord(flat.category) : null;
      if (!category) throw new Error("category_count needs a known category");
      return { kind: "category_count", category };
    }
    case "count_summary":
      return { kind: "count_summary" };
    case "unassigned":
      return { kind: "unassigned" };
    case "help":
      return { kind: "help" };
  }
}

type ChatCompletionPayload = {
  choices?: Array<{ message?: { content?: string | null; refusal?: string | null } }>;
};

export async function parseWithOpenAI(question: string, cfg: OpenAIConfig, fetchImpl: typeof fetch = fetch): Promise<Intent> {
  if (!cfg.apiKey) throw new Error("OPENAI_API_KEY is not configured");

  const headers: Record<string, string> = { "content-type": "application/json" };
  if (cfg.authHeader === "api-key") headers["api-key"] = cfg.apiKey;
  else headers.authorization = `Bearer ${cfg.apiKey}`;

  const response = await fetchImpl(`${cfg.baseUrl}/chat/completions`, {
    method: "POST",
    headers,
    body: JSON.stringify({
      model: cfg.model,
      messages: [
        { role: "system", content: SYSTEM_PROMPT },
        { role: "user", content: question },
      ],
      response_format: {
        type: "json_schema",
        json_schema: { name: "asset_query_intent", strict: true, schema: INTENT_JSON_SCHEMA },
      },
    }),
    signal: AbortSignal.timeout(cfg.timeoutMs),
  });

  if (!response.ok) {
    // Only the status and error code are surfaced — never the request or the key.
    let code = "";
    try {
      const body = (await response.json()) as { error?: { code?: string; type?: string } };
      code = body.error?.code ?? body.error?.type ?? "";
    } catch {
      // ignore unparsable error bodies
    }
    throw new Error(`OpenAI request failed with HTTP ${response.status}${code ? ` (${code})` : ""}`);
  }

  const payload = (await response.json()) as ChatCompletionPayload;
  const message = payload.choices?.[0]?.message;
  if (message?.refusal) throw new Error("OpenAI refused to parse the question");
  if (!message?.content) throw new Error("OpenAI returned an empty response");

  return flatToIntent(FlatIntentSchema.parse(JSON.parse(message.content)));
}
