/**
 * Structured intents for the natural-language query engine. Both parsers (the
 * rule-based parser and the optional OpenAI parser) produce an Intent; the
 * executor turns an Intent into database queries and a written answer.
 */

export const INTENT_KINDS = [
  "locate_asset",
  "assets_assigned_to",
  "not_moved",
  "gate_passes",
  "assets_by_status",
  "category_count",
  "count_summary",
  "unassigned",
  "help",
] as const;
export type IntentKind = (typeof INTENT_KINDS)[number];

export const GATE_PASS_STATUSES = ["pending", "approved", "rejected"] as const;
export type GatePassStatusFilter = (typeof GATE_PASS_STATUSES)[number];

export const ASSET_STATUSES = ["active", "maintenance", "retired", "missing"] as const;
export type AssetStatusFilter = (typeof ASSET_STATUSES)[number];

export const PERIODS = ["today", "yesterday", "this_week", "last_week", "this_month", "last_n_days", "all"] as const;
export type Period = (typeof PERIODS)[number];

export type Intent =
  | { kind: "locate_asset"; terms: string[]; assignee: string | null }
  | { kind: "assets_assigned_to"; assignee: string }
  | { kind: "not_moved"; days: number }
  | { kind: "gate_passes"; status: GatePassStatusFilter | null; period: Period; periodDays: number | null }
  | { kind: "assets_by_status"; status: AssetStatusFilter }
  | { kind: "category_count"; category: string }
  | { kind: "count_summary" }
  | { kind: "unassigned" }
  | { kind: "help" };

/** Asset categories used by the registry (see lib/db/src/schema/assets.ts). */
export const CATEGORIES = [
  "Laptop",
  "Desktop",
  "Projector",
  "Phone",
  "Printer",
  "Network Equipment",
  "UPS",
  "Furniture",
  "Vehicle",
  "Other",
] as const;

const CATEGORY_ALIASES: Record<string, string> = {
  laptop: "Laptop",
  laptops: "Laptop",
  notebook: "Laptop",
  notebooks: "Laptop",
  desktop: "Desktop",
  desktops: "Desktop",
  pc: "Desktop",
  pcs: "Desktop",
  workstation: "Desktop",
  workstations: "Desktop",
  projector: "Projector",
  projectors: "Projector",
  phone: "Phone",
  phones: "Phone",
  smartphone: "Phone",
  smartphones: "Phone",
  mobile: "Phone",
  mobiles: "Phone",
  printer: "Printer",
  printers: "Printer",
  router: "Network Equipment",
  routers: "Network Equipment",
  switch: "Network Equipment",
  switches: "Network Equipment",
  network: "Network Equipment",
  ups: "UPS",
  furniture: "Furniture",
  chair: "Furniture",
  chairs: "Furniture",
  vehicle: "Vehicle",
  vehicles: "Vehicle",
  truck: "Vehicle",
  trucks: "Vehicle",
  car: "Vehicle",
  cars: "Vehicle",
};

/** Map a word ("laptops", "PCs") or category name to the registry's category, or null. */
export function categoryFromWord(word: string): string | null {
  const w = word.trim().toLowerCase();
  if (!w) return null;
  if (CATEGORY_ALIASES[w]) return CATEGORY_ALIASES[w];
  return CATEGORIES.find((c) => c.toLowerCase() === w) ?? null;
}
