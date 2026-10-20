import { randomBytes } from "node:crypto";
import type { WorkingHours } from "./time";

/**
 * Runtime configuration, read from environment variables with MVP defaults.
 * See `.env.example` at the repository root for the full list.
 */

export interface OpenAIConfig {
  /** null = OpenAI disabled; the rule-based parser answers every question. */
  apiKey: string | null;
  model: string;
  /** OpenAI-compatible base URL (OpenAI, or an Azure OpenAI v1 endpoint). */
  baseUrl: string;
  /** "authorization" sends `Authorization: Bearer <key>`; "api-key" sends `api-key: <key>` (Azure keys). */
  authHeader: "authorization" | "api-key";
  timeoutMs: number;
}

export interface AuthConfig {
  /** HMAC key for bearer tokens. Random per process when AUTH_SECRET is unset. */
  secret: string;
  /** How long a sign-in lasts. */
  tokenTtlHours: number;
  /** True when AUTH_SECRET was supplied: tokens then survive a restart. */
  secretConfigured: boolean;
}

export interface AppConfig {
  timezone: string;
  workingHours: WorkingHours;
  /** An active asset with no movement for this many days raises an idle_asset insight. */
  idleDays: number;
  openai: OpenAIConfig;
  auth: AuthConfig;
}

export const DEFAULT_TIMEZONE = "Africa/Harare";
export const DEFAULT_OPENAI_MODEL = "gpt-4o-mini";

function int(value: string | undefined, fallback: number, min: number, max: number): number {
  if (value == null || value.trim() === "") return fallback;
  const n = Number(value);
  return Number.isInteger(n) && n >= min && n <= max ? n : fallback;
}

/** "1-5" → [1,2,3,4,5]; "1,3,5" → [1,3,5]; "0,6" → weekends. 0 = Sunday. Defaults to Monday–Friday. */
export function parseWorkDays(raw: string | undefined): number[] {
  const fallback = [1, 2, 3, 4, 5];
  if (!raw || !raw.trim()) return fallback;
  const days = new Set<number>();
  for (const part of raw.split(",")) {
    const [from, to] = part.split("-").map((s) => Number(s.trim()));
    if (!Number.isInteger(from)) continue;
    if (to === undefined) days.add(from);
    else if (Number.isInteger(to)) for (let d = from; d <= to; d++) days.add(d);
  }
  const valid = [...days].filter((d) => d >= 0 && d <= 6).sort((a, b) => a - b);
  return valid.length > 0 ? valid : fallback;
}

function validTimeZone(tz: string | undefined): string {
  if (!tz || !tz.trim()) return DEFAULT_TIMEZONE;
  try {
    new Intl.DateTimeFormat("en-US", { timeZone: tz.trim() });
    return tz.trim();
  } catch {
    return DEFAULT_TIMEZONE;
  }
}

/**
 * Never fall back to a hard-coded signing key: an attacker who knows it can mint
 * admin tokens. Without AUTH_SECRET the process signs with a random key, so the
 * app still runs and tokens simply stop working when it restarts.
 */
function authSecret(env: NodeJS.ProcessEnv): { secret: string; configured: boolean } {
  const configured = env.AUTH_SECRET?.trim();
  if (configured && configured.length >= 16) return { secret: configured, configured: true };
  return { secret: randomBytes(48).toString("hex"), configured: false };
}

export function loadConfig(env: NodeJS.ProcessEnv = process.env): AppConfig {
  const startHour = int(env.WORK_HOURS_START, 7, 0, 23);
  const endHour = int(env.WORK_HOURS_END, 18, 1, 24);
  return {
    timezone: validTimeZone(env.ORG_TIMEZONE),
    workingHours: {
      startHour,
      endHour: endHour > startHour ? endHour : Math.min(startHour + 1, 24),
      days: parseWorkDays(env.WORK_DAYS),
    },
    idleDays: int(env.IDLE_ASSET_DAYS, 7, 1, 365),
    openai: {
      apiKey: env.OPENAI_API_KEY?.trim() || null,
      model: env.OPENAI_MODEL?.trim() || DEFAULT_OPENAI_MODEL,
      baseUrl: (env.OPENAI_BASE_URL?.trim() || "https://api.openai.com/v1").replace(/\/+$/, ""),
      authHeader: env.OPENAI_AUTH_HEADER?.trim().toLowerCase() === "api-key" ? "api-key" : "authorization",
      timeoutMs: int(env.OPENAI_TIMEOUT_MS, 8000, 1000, 60000),
    },
    auth: (() => {
      const { secret, configured } = authSecret(env);
      return { secret, tokenTtlHours: int(env.AUTH_TOKEN_TTL_HOURS, 12, 1, 720), secretConfigured: configured };
    })(),
  };
}

/**
 * Configuration is read on demand so tests and restarts pick up environment
 * changes. The auth secret is the exception: it is resolved once, otherwise a
 * process without AUTH_SECRET would sign every token with a different random key
 * and no session would survive a single request.
 */
let cachedAuth: AuthConfig | null = null;

export function getConfig(): AppConfig {
  const config = loadConfig(process.env);
  cachedAuth ??= config.auth;
  return { ...config, auth: cachedAuth };
}

/** Testing seam: forget the resolved auth secret. */
export function resetAuthConfigForTests(): void {
  cachedAuth = null;
}
