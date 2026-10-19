import { DAY_MS, describeWorkingHours, formatZoned, isWithinWorkingHours, type WorkingHours } from "../lib/time";
import type { GateOutcome } from "./gate-pass-rules";

/**
 * Pure anomaly rules (plan section 6.4). Each rule inspects a context and
 * returns an InsightDraft or null. Persisting drafts (with de-duplication) is
 * the job of services/insights.ts.
 */

export type InsightType =
  | "anomaly"
  | "idle_asset"
  | "unauthorized_movement"
  | "missing_asset"
  | "maintenance_due"
  | "unusual_pattern";

export type InsightSeverity = "info" | "warning" | "critical";

export interface InsightDraft {
  /** Which rule produced the draft (for logs and tests; not stored). */
  rule: string;
  type: InsightType;
  severity: InsightSeverity;
  message: string;
  assetId: number | null;
  gatePassId?: number | null;
}

export interface ScanContext {
  asset: { id: number; name: string; rfidTag: string; sbuId: number | null; sbuCode: string | null };
  location: { id: number; name: string; type: string; sbuId: number | null; sbuCode: string | null };
  readerName: string;
  scannedAt: Date;
  gate: { outcome: GateOutcome; gatePassId: number | null };
  timeZone: string;
  workingHours: WorkingHours;
}

const outsideWorkingHours = (ctx: ScanContext) => !isWithinWorkingHours(ctx.scannedAt, ctx.timeZone, ctx.workingHours);

/**
 * Rule: a gate exit that is not covered by an approved gate pass.
 * Escalates to critical when it also happens outside working hours.
 * Authorised exits and entries raise nothing (no more "every gate scan is a warning").
 */
export function unauthorizedGateExitRule(ctx: ScanContext): InsightDraft | null {
  const { outcome, gatePassId } = ctx.gate;
  if (outcome !== "pending_created" && outcome !== "pending_existing") return null;
  const afterHours = outsideWorkingHours(ctx);
  const sbu = ctx.location.sbuCode ? ` [${ctx.location.sbuCode}]` : "";
  const passText =
    gatePassId == null
      ? "no gate pass on record"
      : outcome === "pending_created"
        ? `pending gate pass #${gatePassId} raised for approval`
        : `pending gate pass #${gatePassId} still awaiting approval`;
  return {
    rule: "unauthorized_gate_exit",
    type: "unauthorized_movement",
    severity: afterHours ? "critical" : "warning",
    message:
      `Unauthorised exit attempt: "${ctx.asset.name}" (${ctx.asset.rfidTag}) detected at gate "${ctx.location.name}"${sbu}` +
      ` without an approved gate pass${afterHours ? " outside working hours" : ""} — ${passText}`,
    assetId: ctx.asset.id,
    gatePassId,
  };
}

/** Rule: the asset's owning SBU differs from the SBU of the location (e.g. the gate) that detected it. */
export function sbuLocationMismatchRule(ctx: ScanContext): InsightDraft | null {
  const { asset, location } = ctx;
  if (asset.sbuId == null || location.sbuId == null || asset.sbuId === location.sbuId) return null;
  const where = location.type === "gate" ? `gate "${location.name}"` : `"${location.name}"`;
  return {
    rule: "sbu_location_mismatch",
    type: "anomaly",
    severity: "warning",
    message:
      `Location mismatch: "${asset.name}" belongs to ${asset.sbuCode ?? `SBU #${asset.sbuId}`}` +
      ` but was detected at ${where} (${location.sbuCode ?? `SBU #${location.sbuId}`})`,
    assetId: asset.id,
  };
}

/** Rule: a scan outside working hours, judged on the scan timestamp in the organisation timezone. */
export function outOfHoursRule(ctx: ScanContext): InsightDraft | null {
  if (!outsideWorkingHours(ctx)) return null;
  const isGate = ctx.location.type === "gate";
  return {
    rule: "out_of_hours",
    type: "unusual_pattern",
    severity: isGate ? "warning" : "info",
    message:
      `Out-of-hours scan: "${ctx.asset.name}" detected at ${isGate ? "gate " : ""}"${ctx.location.name}"` +
      ` on ${formatZoned(ctx.scannedAt, ctx.timeZone)} — outside working hours (${describeWorkingHours(ctx.workingHours)})`,
    assetId: ctx.asset.id,
  };
}

export const SCAN_RULES = [unauthorizedGateExitRule, sbuLocationMismatchRule, outOfHoursRule] as const;

/** Run every scan rule and return the drafts that fired. */
export function evaluateScan(ctx: ScanContext): InsightDraft[] {
  return SCAN_RULES.map((rule) => rule(ctx)).filter((draft): draft is InsightDraft => draft !== null);
}

// ── Periodic rules (evaluated when insights are listed) ──────────────────────

export interface PeriodicAsset {
  id: number;
  name: string;
  status: string;
  serialNumber: string;
  createdAt: Date;
}

/** Rule: an active asset that has not moved (or, if never moved, not since registration) for `idleDays`. */
export function idleAssetRule(asset: PeriodicAsset, lastMovedAt: Date | null, now: Date, idleDays: number): InsightDraft | null {
  if (asset.status !== "active") return null;
  const since = lastMovedAt ?? asset.createdAt;
  if (now.getTime() - since.getTime() < idleDays * DAY_MS) return null;
  return {
    rule: "idle_asset",
    type: "idle_asset",
    severity: "info",
    message: `Asset "${asset.name}" has not moved in over ${idleDays} days — verify its location and status`,
    assetId: asset.id,
  };
}

/** Rule: an asset whose status is "missing". */
export function missingAssetRule(asset: PeriodicAsset): InsightDraft | null {
  if (asset.status !== "missing") return null;
  return {
    rule: "missing_asset",
    type: "missing_asset",
    severity: "critical",
    message: `Asset "${asset.name}" (SN: ${asset.serialNumber}) is marked missing — initiate recovery procedure`,
    assetId: asset.id,
  };
}
