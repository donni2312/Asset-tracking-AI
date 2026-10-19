import { describe, expect, it } from "vitest";
import {
  evaluateScan,
  idleAssetRule,
  missingAssetRule,
  outOfHoursRule,
  sbuLocationMismatchRule,
  unauthorizedGateExitRule,
  type ScanContext,
} from "./anomaly-rules";

const IN_HOURS = new Date("2026-10-14T08:00:00Z"); // Wed 10:00 Harare
const SATURDAY_2AM = new Date("2026-10-17T00:00:00Z"); // Sat 02:00 Harare

function ctx(overrides: Partial<ScanContext> = {}): ScanContext {
  return {
    asset: { id: 7, name: "Dell Latitude 5440", rfidTag: "TAG-1", sbuId: 1, sbuCode: "TSAS" },
    location: { id: 3, name: "TSAS Main Gate", type: "gate", sbuId: 1, sbuCode: "TSAS" },
    readerName: "TSAS Gate Reader",
    scannedAt: IN_HOURS,
    gate: { outcome: "authorized", gatePassId: 12 },
    timeZone: "Africa/Harare",
    workingHours: { startHour: 7, endHour: 18, days: [1, 2, 3, 4, 5] },
    ...overrides,
  };
}

const office = { id: 2, name: "TSAS IT Office", type: "office", sbuId: 1, sbuCode: "TSAS" };

describe("evaluateScan", () => {
  it("raises nothing for an authorised gate exit (no more 'every gate scan is a warning')", () => {
    expect(evaluateScan(ctx())).toEqual([]);
  });

  it("raises nothing for an asset returning through a gate", () => {
    expect(evaluateScan(ctx({ gate: { outcome: "entry", gatePassId: null } }))).toEqual([]);
  });

  it("raises nothing for an in-hours scan in the asset's own SBU", () => {
    expect(evaluateScan(ctx({ location: office, gate: { outcome: "not_gate", gatePassId: null } }))).toEqual([]);
  });

  it("combines rules: unauthorised + out-of-hours exit at another SBU's gate", () => {
    const drafts = evaluateScan(
      ctx({
        scannedAt: SATURDAY_2AM,
        location: { id: 9, name: "BAK Depot Gate", type: "gate", sbuId: 2, sbuCode: "BAK" },
        gate: { outcome: "pending_created", gatePassId: 30 },
      }),
    );
    expect(drafts.map((d) => d.rule)).toEqual(["unauthorized_gate_exit", "sbu_location_mismatch", "out_of_hours"]);
  });
});

describe("unauthorizedGateExitRule", () => {
  it("emits unauthorized_movement (warning) when a gate exit has no approved pass", () => {
    const draft = unauthorizedGateExitRule(ctx({ gate: { outcome: "pending_created", gatePassId: 12 } }));
    expect(draft).toMatchObject({ type: "unauthorized_movement", severity: "warning", assetId: 7, gatePassId: 12 });
    expect(draft?.message).toContain("pending gate pass #12 raised for approval");
  });

  it("mentions an existing pending pass instead of raising a new one", () => {
    const draft = unauthorizedGateExitRule(ctx({ gate: { outcome: "pending_existing", gatePassId: 12 } }));
    expect(draft?.message).toContain("still awaiting approval");
  });

  it("escalates to critical outside working hours", () => {
    const draft = unauthorizedGateExitRule(ctx({ scannedAt: SATURDAY_2AM, gate: { outcome: "pending_created", gatePassId: 12 } }));
    expect(draft?.severity).toBe("critical");
    expect(draft?.message).toContain("outside working hours");
  });

  it.each(["authorized", "entry", "not_gate"] as const)("stays quiet for outcome %s", (outcome) => {
    expect(unauthorizedGateExitRule(ctx({ gate: { outcome, gatePassId: null } }))).toBeNull();
  });
});

describe("sbuLocationMismatchRule", () => {
  it("flags an asset detected at a gate that belongs to a different SBU", () => {
    const draft = sbuLocationMismatchRule(ctx({ location: { id: 9, name: "BAK Depot Gate", type: "gate", sbuId: 2, sbuCode: "BAK" } }));
    expect(draft).toMatchObject({ type: "anomaly", severity: "warning", assetId: 7 });
    expect(draft?.message).toBe('Location mismatch: "Dell Latitude 5440" belongs to TSAS but was detected at gate "BAK Depot Gate" (BAK)');
  });

  it("does not fire when either side has no SBU or they match", () => {
    expect(sbuLocationMismatchRule(ctx())).toBeNull();
    expect(sbuLocationMismatchRule(ctx({ asset: { ...ctx().asset, sbuId: null, sbuCode: null } }))).toBeNull();
    expect(sbuLocationMismatchRule(ctx({ location: { ...ctx().location, sbuId: null, sbuCode: null } }))).toBeNull();
  });
});

describe("outOfHoursRule", () => {
  it("uses the scan timestamp, not the processing time", () => {
    expect(outOfHoursRule(ctx({ scannedAt: IN_HOURS }))).toBeNull();
    const draft = outOfHoursRule(ctx({ scannedAt: SATURDAY_2AM }));
    expect(draft).toMatchObject({ type: "unusual_pattern", severity: "warning" });
    expect(draft?.message).toContain("Sat 17 Oct 02:00");
  });

  it("is informational away from the gates", () => {
    expect(outOfHoursRule(ctx({ scannedAt: SATURDAY_2AM, location: office }))?.severity).toBe("info");
  });
});

describe("periodic rules", () => {
  const now = new Date("2026-10-14T08:00:00Z");
  const asset = { id: 1, name: "Epson Projector", status: "active", serialNumber: "SN1", createdAt: new Date("2026-01-01T00:00:00Z") };

  it("flags active assets idle for longer than the threshold", () => {
    expect(idleAssetRule(asset, new Date("2026-10-01T00:00:00Z"), now, 7)?.type).toBe("idle_asset");
    expect(idleAssetRule(asset, new Date("2026-10-12T00:00:00Z"), now, 7)).toBeNull();
  });

  it("measures never-moved assets from their registration date", () => {
    expect(idleAssetRule({ ...asset, createdAt: new Date("2026-10-13T00:00:00Z") }, null, now, 7)).toBeNull();
    expect(idleAssetRule(asset, null, now, 7)).not.toBeNull();
  });

  it("ignores assets that are not active, and flags missing ones as critical", () => {
    expect(idleAssetRule({ ...asset, status: "retired" }, null, now, 7)).toBeNull();
    expect(missingAssetRule({ ...asset, status: "missing" })?.severity).toBe("critical");
    expect(missingAssetRule(asset)).toBeNull();
  });
});
