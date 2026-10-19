import { db, assetsTable, gatePassesTable, movementsTable } from "@workspace/db";
import { and, desc, eq, max, sql, type SQL } from "drizzle-orm";
import {
  DAY_MS,
  formatZoned,
  formatZonedDay,
  startOfZonedDay,
  startOfZonedMonth,
  startOfZonedWeek,
} from "../../lib/time";
import { listAssetsDetailed, lastMovementsFor, type AssetDetailed, type LastMovement } from "../asset-queries";
import { enrichGatePasses, type GatePassDto } from "../gate-pass-workflow";
import { categoryFromWord, type Intent, type Period } from "./intent";

/** Turns an Intent into database queries and a plain-English answer. */

export interface QueryAnswer {
  answer: string;
  relatedAssets: AssetDetailed[];
  relatedGatePasses: GatePassDto[];
}

const MAX_LISTED = 10;

export const EXAMPLE_QUESTIONS = [
  "Where is the Dell laptop assigned to John Doe?",
  "Which assets have not moved in 30 days?",
  "Show me all gate passes approved this week",
  "Which assets are missing?",
  "How many laptops do we have?",
  "Show me unassigned assets",
];

const plural = (n: number, one: string, many = `${one}s`) => `${n} ${n === 1 ? one : many}`;

function bulletList(lines: string[]): string {
  const shown = lines.slice(0, MAX_LISTED).map((l) => `• ${l}`);
  if (lines.length > MAX_LISTED) shown.push(`…and ${lines.length - MAX_LISTED} more.`);
  return shown.join("\n");
}

function triggerLabel(triggeredBy: string): string {
  if (triggeredBy === "rfid") return "RFID scan";
  if (triggeredBy === "gate_pass") return "gate pass exit";
  return "manual move";
}

/** Every word of the query name appears in the assignee (case-insensitive). */
export function assigneeMatches(assignedTo: string | null, query: string): boolean {
  if (!assignedTo) return false;
  const hay = assignedTo.toLowerCase();
  return query
    .toLowerCase()
    .split(/\s+/)
    .filter(Boolean)
    .every((part) => hay.includes(part));
}

/** How many query terms the asset matches (brand/model in the name, category words, serial, tag). */
export function scoreAsset(asset: Pick<AssetDetailed, "name" | "category" | "serialNumber" | "rfidTag">, terms: string[]): number {
  const hay = `${asset.name} ${asset.category} ${asset.serialNumber} ${asset.rfidTag}`.toLowerCase();
  let score = 0;
  for (const term of terms) {
    const category = categoryFromWord(term);
    if (category && asset.category.toLowerCase() === category.toLowerCase()) score++;
    else if (hay.includes(term.toLowerCase())) score++;
  }
  return score;
}

function describeWhereabouts(asset: AssetDetailed, last: LastMovement | undefined, timeZone: string): string {
  const sbu = asset.sbuCode ? ` [${asset.sbuCode}]` : "";
  let where: string;
  if (asset.currentLocationName) where = `is at ${asset.currentLocationName}${sbu}`;
  else if (last?.triggeredBy === "gate_pass")
    where = `is off-premises (left under gate pass #${last.gatePassId} on ${formatZoned(last.timestamp, timeZone)})`;
  else where = "has no recorded location";
  const holder = asset.assignedTo ? `, assigned to ${asset.assignedTo}` : "";
  const seen = last
    ? ` Last movement: ${formatZoned(last.timestamp, timeZone)} (${triggerLabel(last.triggeredBy)}).`
    : " No movements recorded yet.";
  return `${asset.name} (${asset.rfidTag}) ${where}${holder}.${seen}`;
}

async function locateAsset(intent: Extract<Intent, { kind: "locate_asset" }>, timeZone: string): Promise<QueryAnswer> {
  const all = await listAssetsDetailed(db);
  const candidates = intent.assignee ? all.filter((a) => assigneeMatches(a.assignedTo, intent.assignee!)) : all;

  let matches: AssetDetailed[] = [];
  let partial = false;
  if (intent.terms.length > 0) {
    const scored = candidates.map((asset) => ({ asset, score: scoreAsset(asset, intent.terms) })).filter((s) => s.score > 0);
    const best = Math.max(0, ...scored.map((s) => s.score));
    matches = scored.filter((s) => s.score === best).map((s) => s.asset);
    partial = matches.length > 0 && best < intent.terms.length;
  } else if (intent.assignee) {
    matches = candidates;
  }

  const described = [intent.terms.join(" "), intent.assignee ? `assigned to ${intent.assignee}` : ""].filter(Boolean).join(" ");
  if (matches.length === 0) {
    const hint =
      intent.assignee && candidates.length === 0
        ? ` Nobody matching "${intent.assignee}" currently holds an asset.`
        : " Try the asset name, brand, category, serial number or RFID tag.";
    return { answer: `I couldn't find an asset matching "${described || "your description"}".${hint}`, relatedAssets: [], relatedGatePasses: [] };
  }

  const last = await lastMovementsFor(db, matches.map((m) => m.id));
  const lines = matches.map((a) => describeWhereabouts(a, last.get(a.id), timeZone));
  const prefix = partial ? "Closest match" + (matches.length > 1 ? "es" : "") + `:\n` : "";
  const answer = lines.length === 1 ? `${prefix}${lines[0]}` : `${prefix}${plural(matches.length, "matching asset")}:\n${bulletList(lines)}`;
  return { answer, relatedAssets: matches, relatedGatePasses: [] };
}

async function assetsAssignedTo(assignee: string): Promise<QueryAnswer> {
  const assets = (await listAssetsDetailed(db)).filter((a) => assigneeMatches(a.assignedTo, assignee));
  if (assets.length === 0) return { answer: `No assets are currently assigned to anyone matching "${assignee}".`, relatedAssets: [], relatedGatePasses: [] };
  const lines = assets.map((a) => `${a.name} (${a.category}) — ${a.currentLocationName ?? "no recorded location"}`);
  return {
    answer: `${plural(assets.length, "asset")} assigned to ${assets[0].assignedTo}:\n${bulletList(lines)}`,
    relatedAssets: assets,
    relatedGatePasses: [],
  };
}

async function notMoved(days: number, now: Date, timeZone: string): Promise<QueryAnswer> {
  const assets = (await listAssetsDetailed(db)).filter((a) => a.status !== "retired");
  const lastMoves = await db
    .select({ assetId: movementsTable.assetId, lastMovedAt: max(movementsTable.timestamp) })
    .from(movementsTable)
    .groupBy(movementsTable.assetId);
  const lastMoved = new Map(lastMoves.map((m) => [m.assetId, m.lastMovedAt]));
  const cutoff = now.getTime() - days * DAY_MS;

  const stale = assets
    .map((asset) => ({ asset, lastMovedAt: lastMoved.get(asset.id) ?? null }))
    .filter(({ asset, lastMovedAt }) => (lastMovedAt ?? asset.createdAt).getTime() < cutoff)
    .sort((a, b) => (a.lastMovedAt ?? a.asset.createdAt).getTime() - (b.lastMovedAt ?? b.asset.createdAt).getTime());

  if (stale.length === 0) {
    return { answer: `Every tracked asset has moved within the last ${days} days.`, relatedAssets: [], relatedGatePasses: [] };
  }
  const lines = stale.map(({ asset, lastMovedAt }) => {
    const where = asset.currentLocationName ?? "no recorded location";
    if (!lastMovedAt) return `${asset.name} (${where}) — never moved since it was registered on ${formatZonedDay(asset.createdAt, timeZone)}`;
    const ago = Math.floor((now.getTime() - lastMovedAt.getTime()) / DAY_MS);
    return `${asset.name} (${where}) — last moved ${formatZonedDay(lastMovedAt, timeZone)}, ${ago} days ago`;
  });
  return {
    answer: `${plural(stale.length, "asset")} ${stale.length === 1 ? "has" : "have"} not moved in ${days} days:\n${bulletList(lines)}`,
    relatedAssets: stale.map((s) => s.asset),
    relatedGatePasses: [],
  };
}

/** [from, to) window for a period in the organisation timezone; null bounds are open. */
export function periodWindow(period: Period, periodDays: number | null, now: Date, timeZone: string) {
  switch (period) {
    case "today":
      return { from: startOfZonedDay(now, timeZone), to: null as Date | null, label: "today" };
    case "yesterday": {
      const today = startOfZonedDay(now, timeZone);
      return { from: startOfZonedDay(new Date(today.getTime() - 12 * 60 * 60 * 1000), timeZone), to: today, label: "yesterday" };
    }
    case "this_week":
      return { from: startOfZonedWeek(now, timeZone), to: null, label: "this week" };
    case "last_week": {
      const thisWeek = startOfZonedWeek(now, timeZone);
      return { from: startOfZonedWeek(new Date(thisWeek.getTime() - 3 * DAY_MS), timeZone), to: thisWeek, label: "last week" };
    }
    case "this_month":
      return { from: startOfZonedMonth(now, timeZone), to: null, label: "this month" };
    case "last_n_days": {
      const n = periodDays ?? 7;
      return { from: new Date(now.getTime() - n * DAY_MS), to: null, label: `in the last ${n} days` };
    }
    default:
      return { from: null as Date | null, to: null as Date | null, label: "" };
  }
}

async function gatePasses(intent: Extract<Intent, { kind: "gate_passes" }>, now: Date, timeZone: string): Promise<QueryAnswer> {
  const decided = intent.status === "approved" || intent.status === "rejected";
  // Passes decided before decided_at existed fall back to updated_at (the decision time in the old schema).
  const timeExpr = decided
    ? sql`coalesce(${gatePassesTable.decidedAt}, ${gatePassesTable.updatedAt})`
    : sql`${gatePassesTable.createdAt}`;
  const { from, to, label } = periodWindow(intent.period, intent.periodDays, now, timeZone);

  const conditions: SQL[] = [];
  if (intent.status) conditions.push(eq(gatePassesTable.status, intent.status));
  // Bounds are bound as UTC ISO strings: the columns are `timestamp without time zone` holding UTC.
  if (from) conditions.push(sql`${timeExpr} >= ${from.toISOString()}`);
  if (to) conditions.push(sql`${timeExpr} < ${to.toISOString()}`);
  const rows = await db
    .select()
    .from(gatePassesTable)
    .where(conditions.length > 0 ? and(...conditions) : undefined)
    .orderBy(desc(timeExpr), desc(gatePassesTable.id));
  const passes = await enrichGatePasses(db, rows);

  const verb = intent.status === "approved" ? "approved" : intent.status === "rejected" ? "rejected" : intent.status === "pending" ? "pending" : "raised";
  const when = label ? ` ${label}` : "";
  if (passes.length === 0) {
    const none = intent.status === "pending" ? `No gate passes are pending${when}.` : `No gate passes were ${verb}${when}.`;
    return { answer: none, relatedAssets: [], relatedGatePasses: [] };
  }

  const lines = passes.map((p) => {
    const dest = p.destination ? ` → ${p.destination}` : "";
    let detail: string;
    const decidedAt = p.decidedAt ?? p.updatedAt;
    if (p.status === "approved") detail = `approved by ${p.approvedBy ?? "unknown"} on ${formatZoned(decidedAt, timeZone)}`;
    else if (p.status === "rejected") detail = `rejected by ${p.approvedBy ?? "unknown"} on ${formatZoned(decidedAt, timeZone)}`;
    else detail = `requested by ${p.requestedBy} on ${formatZoned(p.createdAt, timeZone)}${p.source === "rfid_gate" ? " (auto-raised at a gate)" : ""}`;
    return `#${p.id} ${p.assetName}${dest} — ${detail}`;
  });
  const assetIds = [...new Set(passes.map((p) => p.assetId))];
  const relatedAssets = (await listAssetsDetailed(db)).filter((a) => assetIds.includes(a.id));
  const head =
    intent.status === "pending"
      ? `${plural(passes.length, "gate pass", "gate passes")} pending${when}`
      : `${plural(passes.length, "gate pass", "gate passes")} ${verb}${when}`;
  return { answer: `${head}:\n${bulletList(lines)}`, relatedAssets, relatedGatePasses: passes };
}

async function assetsByStatus(status: string): Promise<QueryAnswer> {
  const assets = await listAssetsDetailed(db, eq(assetsTable.status, status));
  const label = status === "maintenance" ? "in maintenance" : status === "missing" ? "missing" : status;
  if (assets.length === 0) return { answer: `No assets are currently ${label}.`, relatedAssets: [], relatedGatePasses: [] };
  const lines = assets.map((a) => `${a.name} (SN ${a.serialNumber})${a.currentLocationName ? ` — last known at ${a.currentLocationName}` : ""}`);
  return {
    answer: `${plural(assets.length, "asset")} ${assets.length === 1 ? "is" : "are"} ${label}:\n${bulletList(lines)}`,
    relatedAssets: assets,
    relatedGatePasses: [],
  };
}

async function categoryCount(category: string): Promise<QueryAnswer> {
  const assets = (await listAssetsDetailed(db)).filter((a) => a.category.toLowerCase() === category.toLowerCase());
  const byStatus = (s: string) => assets.filter((a) => a.status === s).length;
  const answer =
    assets.length === 0
      ? `There are no ${category.toLowerCase()} assets in the register.`
      : `There ${assets.length === 1 ? "is" : "are"} ${plural(assets.length, category.toLowerCase())} in the register — ` +
        `${byStatus("active")} active, ${byStatus("maintenance")} in maintenance, ${byStatus("missing")} missing, ${byStatus("retired")} retired.`;
  return { answer, relatedAssets: assets, relatedGatePasses: [] };
}

async function countSummary(): Promise<QueryAnswer> {
  const assets = await listAssetsDetailed(db);
  const byStatus = (s: string) => assets.filter((a) => a.status === s).length;
  return {
    answer:
      `Total assets: ${assets.length}. Active: ${byStatus("active")}. In maintenance: ${byStatus("maintenance")}. ` +
      `Missing: ${byStatus("missing")}. Retired: ${byStatus("retired")}.`,
    relatedAssets: [],
    relatedGatePasses: [],
  };
}

async function unassigned(): Promise<QueryAnswer> {
  const assets = (await listAssetsDetailed(db)).filter((a) => !a.assignedTo && a.status !== "retired");
  if (assets.length === 0) return { answer: "Every active asset is assigned to someone.", relatedAssets: [], relatedGatePasses: [] };
  return {
    answer: `${plural(assets.length, "asset")} ${assets.length === 1 ? "is" : "are"} currently unassigned:\n${bulletList(assets.map((a) => `${a.name} (${a.category})`))}`,
    relatedAssets: assets,
    relatedGatePasses: [],
  };
}

function help(): QueryAnswer {
  return {
    answer: `I can answer questions about the asset register, for example:\n${EXAMPLE_QUESTIONS.map((q) => `• "${q}"`).join("\n")}`,
    relatedAssets: [],
    relatedGatePasses: [],
  };
}

export async function executeIntent(intent: Intent, now: Date, timeZone: string): Promise<QueryAnswer> {
  switch (intent.kind) {
    case "locate_asset":
      return locateAsset(intent, timeZone);
    case "assets_assigned_to":
      return assetsAssignedTo(intent.assignee);
    case "not_moved":
      return notMoved(intent.days, now, timeZone);
    case "gate_passes":
      return gatePasses(intent, now, timeZone);
    case "assets_by_status":
      return assetsByStatus(intent.status);
    case "category_count":
      return categoryCount(intent.category);
    case "count_summary":
      return countSummary();
    case "unassigned":
      return unassigned();
    case "help":
      return help();
  }
}
