import { dateKey } from "../lib/time";

/**
 * Pure gate-pass rules (no database access) — the decision logic behind the
 * plan's section 6.3 workflow. Kept separate so it can be unit tested.
 */

/** exit = the asset was on the premises; entry = it was off-premises (returning); internal = not a gate. */
export type GateDirection = "entry" | "exit" | "internal";

export type GateOutcome =
  | "not_gate" // reader is not at a gate
  | "entry" // asset returning to site — no pass needed
  | "authorized" // exit covered by a valid approved pass
  | "pending_created" // exit without a pass — a pending pass was raised automatically
  | "pending_existing"; // exit without a pass — a pending pass already exists

export interface PassValidityFields {
  id: number;
  status: string;
  returnDate: string | null; // YYYY-MM-DD
  decidedAt: Date | null;
}

/**
 * An approved pass authorises exits from the moment it is approved until the
 * end of its return date (organisation timezone). A pass with no return date
 * is open-ended ("Indefinite" in the UI) and stays valid.
 */
export function isPassValidAt(pass: PassValidityFields, at: Date, timeZone: string): boolean {
  if (pass.status !== "approved") return false;
  if (pass.decidedAt && pass.decidedAt.getTime() > at.getTime()) return false; // approved after the scan
  if (!pass.returnDate) return true;
  return pass.returnDate >= dateKey(at, timeZone);
}

/** The most recently approved pass that is valid at `at`, if any. */
export function findValidApprovedPass<T extends PassValidityFields>(passes: T[], at: Date, timeZone: string): T | undefined {
  return passes
    .filter((p) => isPassValidAt(p, at, timeZone))
    .sort((a, b) => (b.decidedAt?.getTime() ?? 0) - (a.decidedAt?.getTime() ?? 0) || b.id - a.id)[0];
}

/**
 * Gate readers in the MVP are not directional, so direction is inferred from
 * the asset's state: an asset with a known on-site location is leaving; an
 * asset that is off-premises (no current location) is coming back.
 */
export function gateDirection(isGate: boolean, assetOnPremises: boolean): GateDirection {
  if (!isGate) return "internal";
  return assetOnPremises ? "exit" : "entry";
}

export function resolveGateOutcome(args: {
  isGate: boolean;
  direction: GateDirection;
  hasValidPass: boolean;
  hasPendingPass: boolean;
}): GateOutcome {
  if (!args.isGate) return "not_gate";
  if (args.direction === "entry") return "entry";
  if (args.hasValidPass) return "authorized";
  return args.hasPendingPass ? "pending_existing" : "pending_created";
}

/** False only for gate exits that are not covered by an approved pass. */
export function isAuthorizedOutcome(outcome: GateOutcome): boolean {
  return outcome !== "pending_created" && outcome !== "pending_existing";
}

/** Returns an error message when the requested return date is in the past, otherwise null. */
export function validateReturnDate(returnDate: string | null | undefined, now: Date, timeZone: string): string | null {
  if (!returnDate) return null;
  if (!/^\d{4}-\d{2}-\d{2}$/.test(returnDate)) return "returnDate must be a date (YYYY-MM-DD)";
  return returnDate < dateKey(now, timeZone) ? "returnDate cannot be in the past" : null;
}
