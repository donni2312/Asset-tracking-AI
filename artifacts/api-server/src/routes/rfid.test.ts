import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { aiInsightsTable, assetsTable, gatePassesTable, movementsTable } from "@workspace/db/schema";
import { eq } from "drizzle-orm";
import { resetDb, testDb } from "../test/test-db";
import { seedBasics, type World } from "../test/fixtures";
import { call, startTestServer, type TestServer } from "../test/http";

/**
 * POST /api/rfid/simulate — the RFID pipeline and the gate-pass check (plan 6.2 / 6.3).
 * The clock is pinned to Wednesday 14 Oct 2026, 10:00 in Harare (inside working hours).
 */

const WED_10AM = new Date("2026-10-14T08:00:00Z");
const SAT_2AM = "2026-10-17T00:00:00.000Z";

let server: TestServer;
let w: World;

beforeAll(async () => {
  server = await startTestServer();
});
afterAll(async () => {
  await server.close();
});
beforeEach(async () => {
  vi.useFakeTimers({ toFake: ["Date"] });
  vi.setSystemTime(WED_10AM);
  await resetDb();
  w = await seedBasics();
});
afterEach(() => {
  vi.useRealTimers();
});

const scan = (rfidTag: string, readerId: number, scannedAt?: string) =>
  call(server, "POST", "/rfid/simulate", { rfidTag, readerId, ...(scannedAt ? { scannedAt } : {}) });

const assetRow = async (id: number) => (await testDb.select().from(assetsTable).where(eq(assetsTable.id, id)))[0];
const insights = () => testDb.select().from(aiInsightsTable);
const passes = () => testDb.select().from(gatePassesTable);

describe("non-gate readers", () => {
  it("records an rfid movement, moves the asset and raises nothing during working hours", async () => {
    await testDb.update(assetsTable).set({ currentLocationId: w.locations.bakWarehouse.id }).where(eq(assetsTable.id, w.assets.projector.id));
    await testDb.update(assetsTable).set({ sbuId: w.sbus.bak.id }).where(eq(assetsTable.id, w.assets.projector.id));

    const res = await scan("TAG-EP-001", w.readers.officeReader.id);

    expect(res.status).toBe(200);
    expect(res.body.gateCheck).toEqual({ isGate: false, direction: "internal", outcome: "not_gate", authorized: true });
    expect(res.body.movement).toMatchObject({
      triggeredBy: "rfid",
      fromLocationName: "BAK Warehouse",
      toLocationName: "TSAS IT Office",
      gatePassId: null,
    });
    expect((await assetRow(w.assets.projector.id)).currentLocationId).toBe(w.locations.office.id);
    // BAK asset seen in a TSAS office → location mismatch is the only insight
    expect(res.body.insights.map((i: { type: string }) => i.type)).toEqual(["anomaly"]);
  });
});

describe("gate readers — no approved pass", () => {
  it("auto-creates a pending gate pass, holds the asset at the gate and emits unauthorized_movement", async () => {
    const res = await scan("TAG-DL-001", w.readers.gateReader.id);

    expect(res.status).toBe(200);
    const { gateCheck, movement, insights: raised } = res.body;
    expect(gateCheck).toMatchObject({ isGate: true, direction: "exit", outcome: "pending_created", authorized: false });
    expect(gateCheck.gatePass).toMatchObject({
      status: "pending",
      source: "rfid_gate",
      gateLocationId: w.locations.gate.id,
      gateLocationName: "TSAS Main Gate",
      assetName: "Dell Latitude 5440",
    });
    expect(gateCheck.gatePass.reason).toContain("Assigned holder: John Doe");
    expect(movement).toMatchObject({ triggeredBy: "rfid", toLocationName: "TSAS Main Gate", gatePassId: gateCheck.gatePass.id });
    expect((await assetRow(w.assets.laptop.id)).currentLocationId).toBe(w.locations.gate.id);

    expect(raised).toHaveLength(1);
    expect(raised[0]).toMatchObject({
      type: "unauthorized_movement",
      severity: "warning",
      gatePassId: gateCheck.gatePass.id,
      assetName: "Dell Latitude 5440",
      sbuCode: "TSAS",
    });

    const pending = await call(server, "GET", "/gate-passes?status=pending");
    expect(pending.body.map((p: { id: number }) => p.id)).toEqual([gateCheck.gatePass.id]);
  });

  it("reuses the pending pass on repeated scans instead of creating duplicates or duplicate alerts", async () => {
    const first = await scan("TAG-DL-001", w.readers.gateReader.id);
    const second = await scan("TAG-DL-001", w.readers.gateReader.id);

    expect(second.body.gateCheck.outcome).toBe("pending_existing");
    expect(second.body.gateCheck.gatePass.id).toBe(first.body.gateCheck.gatePass.id);
    expect(await passes()).toHaveLength(1);
    // first alert said "raised", the second "still awaiting approval" — each is stored once
    expect((await insights()).filter((i) => i.type === "unauthorized_movement")).toHaveLength(2);

    const third = await scan("TAG-DL-001", w.readers.gateReader.id);
    expect(third.body.insights).toEqual([]);
    expect((await insights()).filter((i) => i.type === "unauthorized_movement")).toHaveLength(2);
  });

  it("treats an expired approved pass as no pass", async () => {
    await testDb.insert(gatePassesTable).values({
      assetId: w.assets.laptop.id,
      requestedBy: "John Doe",
      reason: "Client visit",
      status: "approved",
      approvedBy: "Rudo Chikwanha",
      returnDate: "2026-10-13",
      decidedAt: new Date("2026-10-12T08:00:00Z"),
    });

    const res = await scan("TAG-DL-001", w.readers.gateReader.id);
    expect(res.body.gateCheck.outcome).toBe("pending_created");
  });

  it("escalates to critical and adds an out-of-hours insight for a night-time exit attempt", async () => {
    vi.setSystemTime(new Date("2026-10-17T00:05:00Z"));
    const res = await scan("TAG-DL-001", w.readers.gateReader.id, SAT_2AM);

    const byType = Object.fromEntries(res.body.insights.map((i: { type: string; severity: string }) => [i.type, i.severity]));
    expect(byType).toEqual({ unauthorized_movement: "critical", unusual_pattern: "warning" });
    expect(res.body.movement.timestamp).toBe(SAT_2AM);
  });

  it("flags a location mismatch when the asset is detected at another SBU's gate", async () => {
    const res = await scan("TAG-DL-001", w.readers.bakGateReader.id);
    const mismatch = res.body.insights.find((i: { type: string }) => i.type === "anomaly");
    expect(mismatch.message).toBe('Location mismatch: "Dell Latitude 5440" belongs to TSAS but was detected at gate "BAK Depot Gate" (BAK)');
  });
});

describe("gate readers — approved pass", () => {
  async function approvedPass(returnDate: string | null = "2026-10-20") {
    const [p] = await testDb
      .insert(gatePassesTable)
      .values({
        assetId: w.assets.laptop.id,
        requestedBy: "John Doe",
        reason: "Client presentation",
        destination: "Borrowdale client site",
        status: "approved",
        approvedBy: "Rudo Chikwanha",
        returnDate,
        decidedAt: new Date("2026-10-13T08:00:00Z"),
      })
      .returning();
    return p;
  }

  it("records an authorised gate_pass exit, moves the asset off-premises and raises no insight", async () => {
    const pass = await approvedPass();
    const res = await scan("TAG-DL-001", w.readers.gateReader.id);

    expect(res.body.gateCheck).toMatchObject({ outcome: "authorized", authorized: true, direction: "exit" });
    expect(res.body.gateCheck.gatePass.id).toBe(pass.id);
    expect(res.body.gateCheck.gatePass.exitedAt).not.toBeNull();
    expect(res.body.movement).toMatchObject({
      triggeredBy: "gate_pass",
      toLocationId: null,
      toLocationName: null,
      gatePassId: pass.id,
      rfidReaderId: w.readers.gateReader.id,
      movedBy: "John Doe",
    });
    expect(res.body.movement.notes).toContain("destination: Borrowdale client site");
    expect((await assetRow(w.assets.laptop.id)).currentLocationId).toBeNull();
    // Regression: authorised gate scans used to raise "verify gate pass authorization" warnings
    expect(res.body.insights).toEqual([]);
    expect(await insights()).toEqual([]);
  });

  it("treats the asset's next gate scan as an entry (returning) with no pass required", async () => {
    await approvedPass();
    await scan("TAG-DL-001", w.readers.gateReader.id);
    vi.setSystemTime(new Date("2026-10-14T13:00:00Z"));

    const back = await scan("TAG-DL-001", w.readers.gateReader.id);
    expect(back.body.gateCheck).toMatchObject({ direction: "entry", outcome: "entry", authorized: true });
    expect(back.body.movement).toMatchObject({ triggeredBy: "rfid", fromLocationId: null, toLocationName: "TSAS Main Gate" });
    expect(back.body.insights).toEqual([]);
    expect((await assetRow(w.assets.laptop.id)).currentLocationId).toBe(w.locations.gate.id);
  });

  it("keeps the current location correct for back-dated scans", async () => {
    await scan("TAG-EP-001", w.readers.officeReader.id); // 10:00 — latest known position
    const earlier = await scan("TAG-EP-001", w.readers.bakGateReader.id, "2026-10-14T06:00:00.000Z");
    expect(earlier.status).toBe(200);
    expect((await assetRow(w.assets.projector.id)).currentLocationId).toBe(w.locations.office.id);
  });
});

describe("validation", () => {
  it("rejects unknown tags and readers, inactive readers and future timestamps", async () => {
    expect((await scan("NOPE", w.readers.gateReader.id)).status).toBe(404);
    expect((await scan("TAG-DL-001", 9999)).status).toBe(404);
    const inactive = await scan("TAG-DL-001", w.readers.inactiveReader.id);
    expect(inactive.status).toBe(409);
    expect(inactive.body.error).toContain("inactive");
    expect((await scan("TAG-DL-001", w.readers.gateReader.id, "2026-10-15T08:00:00.000Z")).status).toBe(400);
    expect((await call(server, "POST", "/rfid/simulate", { rfidTag: "TAG-DL-001" })).status).toBe(400);
    expect(await testDb.select().from(movementsTable)).toEqual([]);
  });
});
