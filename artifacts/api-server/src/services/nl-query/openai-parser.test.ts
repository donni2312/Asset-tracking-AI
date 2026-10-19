import { describe, expect, it, vi } from "vitest";
import type { OpenAIConfig } from "../../lib/config";
import { flatToIntent, parseWithOpenAI, type FlatIntent } from "./openai-parser";

const cfg: OpenAIConfig = {
  apiKey: "sk-test",
  model: "gpt-test",
  baseUrl: "https://api.example.test/v1",
  authHeader: "authorization",
  timeoutMs: 2000,
};

const flat = (overrides: Partial<FlatIntent> = {}): FlatIntent => ({
  intent: "help",
  terms: [],
  assignee: null,
  days: null,
  status: null,
  period: null,
  periodDays: null,
  category: null,
  ...overrides,
});

function fakeFetch(body: unknown, status = 200) {
  return vi.fn(async (_url: string | URL | Request, _init?: RequestInit) =>
    new Response(JSON.stringify(body), { status, headers: { "content-type": "application/json" } }),
  );
}

const completion = (content: unknown) => ({ choices: [{ message: { content: JSON.stringify(content) } }] });

describe("parseWithOpenAI", () => {
  it("sends only the question with a strict JSON schema and maps the reply to an intent", async () => {
    const fetchImpl = fakeFetch(
      completion(flat({ intent: "locate_asset", terms: ["Dell", "laptop"], assignee: "John Doe" })),
    );
    const intent = await parseWithOpenAI("Where is the Dell laptop assigned to John Doe?", cfg, fetchImpl as unknown as typeof fetch);

    expect(intent).toEqual({ kind: "locate_asset", terms: ["dell", "laptop"], assignee: "john doe" });

    const [url, init] = fetchImpl.mock.calls[0];
    expect(url).toBe("https://api.example.test/v1/chat/completions");
    expect((init?.headers as Record<string, string>).authorization).toBe("Bearer sk-test");
    const body = JSON.parse(String(init?.body));
    expect(body.model).toBe("gpt-test");
    expect(body.response_format.json_schema.strict).toBe(true);
    expect(body.messages.at(-1)).toEqual({ role: "user", content: "Where is the Dell laptop assigned to John Doe?" });
  });

  it("supports Azure-style api-key authentication", async () => {
    const fetchImpl = fakeFetch(completion(flat({ intent: "count_summary" })));
    await parseWithOpenAI("How many assets?", { ...cfg, authHeader: "api-key" }, fetchImpl as unknown as typeof fetch);
    const headers = fetchImpl.mock.calls[0][1]?.headers as Record<string, string>;
    expect(headers["api-key"]).toBe("sk-test");
    expect(headers.authorization).toBeUndefined();
  });

  it("throws (so the caller falls back to rules) on HTTP errors without leaking details", async () => {
    const fetchImpl = fakeFetch({ error: { code: "invalid_api_key", message: "Incorrect API key provided: sk-t***" } }, 401);
    await expect(parseWithOpenAI("x", cfg, fetchImpl as unknown as typeof fetch)).rejects.toThrow("HTTP 401 (invalid_api_key)");
  });

  it("throws on malformed or schema-violating replies", async () => {
    const notJson = vi.fn(async () => new Response(JSON.stringify({ choices: [{ message: { content: "not json" } }] }), { status: 200 }));
    await expect(parseWithOpenAI("x", cfg, notJson as unknown as typeof fetch)).rejects.toThrow();

    const wrongShape = fakeFetch(completion({ intent: "teleport_asset" }));
    await expect(parseWithOpenAI("x", cfg, wrongShape as unknown as typeof fetch)).rejects.toThrow();
  });

  it("refuses to run without an API key", async () => {
    await expect(parseWithOpenAI("x", { ...cfg, apiKey: null })).rejects.toThrow(/not configured/);
  });
});

describe("flatToIntent", () => {
  it("maps gate pass queries and defaults the period", () => {
    expect(flatToIntent(flat({ intent: "gate_passes", status: "approved", period: "this_week" }))).toEqual({
      kind: "gate_passes",
      status: "approved",
      period: "this_week",
      periodDays: null,
    });
    expect(flatToIntent(flat({ intent: "gate_passes" }))).toMatchObject({ period: "all", status: null });
  });

  it("normalises categories and rejects unknown ones", () => {
    expect(flatToIntent(flat({ intent: "category_count", category: "laptops" }))).toEqual({ kind: "category_count", category: "Laptop" });
    expect(() => flatToIntent(flat({ intent: "category_count", category: "spaceships" }))).toThrow();
  });

  it("rejects intents that are missing required parameters", () => {
    expect(() => flatToIntent(flat({ intent: "locate_asset" }))).toThrow();
    expect(() => flatToIntent(flat({ intent: "assets_assigned_to" }))).toThrow();
    expect(() => flatToIntent(flat({ intent: "assets_by_status", status: "approved" }))).toThrow();
  });

  it("defaults not_moved to 30 days", () => {
    expect(flatToIntent(flat({ intent: "not_moved" }))).toEqual({ kind: "not_moved", days: 30 });
  });
});
