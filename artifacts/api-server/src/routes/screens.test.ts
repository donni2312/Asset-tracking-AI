import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { assetsTable, gatePassesTable, movementsTable } from "@workspace/db/schema";
import { eq } from "drizzle-orm";
import { resetDb, testDb } from "../test/test-db";
import { seedBasics, type World } from "../test/fixtures";
import { call, startTestServer, type TestServer } from "../test/http";
import { getBcSyncStatus, resetBcSyncState, triggerBcSync } from "../services/bc-sync";
import { bucketMovementsByDay } from "../services/dashboard";

/** Endpoints behind the new screens: manual movement, location create/edit, dashboard charts, BC sync. */

const WED_10AM = new Date("2026-10-14T08:00:00Z");

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
  resetBcSyncState();
  w = await seedBasics();
});
afterEach(() => {
  vi.useRealTimers();
});

describe("POST /movements (manual movement screen)", () => {
  it("records who moved the asset and updates its location", async () => {
    const res = await call(server, "POST", "/movements", {
      assetId: w.assets.projector.id,
      toLocationId: w.locations.bakWarehouse.id,
      movedBy: "Tendai Moyo",
      notes: "Loaned for stock count",
    });
    expect(res.status).toBe(201);
    expect(res.body).toMatchObject({
      triggeredBy: "manual",
      movedBy: "Tendai Moyo",
      fromLocationName: "TSAS IT Office",
      toLocationName: "BAK Warehouse",
      timestamp: WED_10AM.toISOString(),
    });
    const [asset] = await testDb.select().from(assetsTable).where(eq(assetsTable.id, w.assets.projector.id));
    expect(asset.currentLocationId).toBe(w.locations.bakWarehouse.id);
  });

  it("rejects moves to the current location, unknown locations and unknown assets", async () => {
    const same = await call(server, "POST", "/movements", { assetId: w.assets.projector.id, toLocationId: w.locations.office.id });
    expect(same.status).toBe(400);
    expect((await call(server, "POST", "/movements", { assetId: w.assets.projector.id, toLocationId: 999 })).status).toBe(404);
    expect((await call(server, "POST", "/movements", { assetId: 999, toLocationId: w.locations.gate.id })).status).toBe(404);
  });

  it("filters the movement log by SBU", async () => {
    await call(server, "POST", "/movements", { assetId: w.assets.projector.id, toLocationId: w.locations.gate.id });
    expect((await call(server, "GET", `/movements?sbuId=${w.sbus.tsas.id}`)).body).toHaveLength(1);
    expect((await call(server, "GET", `/movements?sbuId=${w.sbus.bak.id}`)).body).toHaveLength(0);
  });
});

describe("locations (create / edit screens)", () => {
  it("returns the enriched location on create and applies edits", async () => {
    const created = await call(server, "POST", "/locations", {
      name: "TSAS Server Room",
      type: "server_room",
      building: "TSAS House",
      floor: "1",
      sbuId: w.sbus.tsas.id,
    });
    expect(created.status).toBe(201);
    expect(created.body).toMatchObject({ name: "TSAS Server Room", sbuCode: "TSAS", sbuName: "Tobacco Sales Administration Services", assetCount: 0 });

    const edited = await call(server, "PATCH", `/locations/${created.body.id}`, { name: "TSAS Data Centre", floor: null });
    expect(edited.status).toBe(200);
    expect(edited.body).toMatchObject({ name: "TSAS Data Centre", floor: null, type: "server_room" });
    expect((await call(server, "PATCH", "/locations/999", { name: "x" })).status).toBe(404);
    expect((await call(server, "POST", "/locations", { name: "No type", building: "X" })).status).toBe(400);
  });
});

describe("dashboard charts", () => {
  it("buckets movements per day in the organisation timezone", () => {
    const points = bucketMovementsByDay(
      [
        { timestamp: new Date("2026-10-13T22:30:00Z"), triggeredBy: "rfid" }, // 00:30 on the 14th in Harare
        { timestamp: new Date("2026-10-13T21:30:00Z"), triggeredBy: "manual" }, // 23:30 on the 13th
        { timestamp: new Date("2026-10-14T08:00:00Z"), triggeredBy: "gate_pass" },
      ],
      ["2026-10-13", "2026-10-14"],
      "Africa/Harare",
    );
    expect(points).toEqual([
      { date: "2026-10-13", rfid: 0, manual: 1, gatePass: 0, total: 1 },
      { date: "2026-10-14", rfid: 1, manual: 0, gatePass: 1, total: 2 },
    ]);
  });

  it("GET /dashboard/movement-trend returns one point per day", async () => {
    await testDb.insert(movementsTable).values([
      { assetId: w.assets.laptop.id, toLocationId: w.locations.gate.id, triggeredBy: "rfid", timestamp: new Date("2026-10-13T09:00:00Z") },
      { assetId: w.assets.laptop.id, toLocationId: w.locations.office.id, triggeredBy: "manual", timestamp: new Date("2026-10-14T07:00:00Z") },
    ]);
    const res = await call(server, "GET", "/dashboard/movement-trend?days=3");
    expect(res.status).toBe(200);
    expect(res.body.map((p: { date: string; total: number }) => [p.date, p.total])).toEqual([
      ["2026-10-12", 0],
      ["2026-10-13", 1],
      ["2026-10-14", 1],
    ]);
    expect((await call(server, "GET", "/dashboard/movement-trend?days=0")).status).toBe(400);
  });

  it("GET /dashboard/gate-activity labels exits and entries", async () => {
    const [pass] = await testDb
      .insert(gatePassesTable)
      .values({ assetId: w.assets.laptop.id, requestedBy: "John Doe", reason: "Trip", status: "approved", approvedBy: "R", decidedAt: new Date("2026-10-13T08:00:00Z") })
      .returning();
    await call(server, "POST", "/rfid/simulate", { rfidTag: "TAG-DL-001", readerId: w.readers.gateReader.id });
    vi.setSystemTime(new Date("2026-10-14T09:00:00Z"));
    await call(server, "POST", "/rfid/simulate", { rfidTag: "TAG-DL-001", readerId: w.readers.gateReader.id });

    const res = await call(server, "GET", "/dashboard/gate-activity?limit=5");
    expect(res.body.map((g: { direction: string }) => g.direction)).toEqual(["entry", "exit"]);
    expect(pass.id).toBeGreaterThan(0);
  });
});

describe("Business Central (manual sync only)", () => {
  it("reports manual mode, no schedule and the changes waiting to be synced", async () => {
    await call(server, "POST", "/movements", { assetId: w.assets.projector.id, toLocationId: w.locations.gate.id });
    const status = await call(server, "GET", "/bc/sync-status");
    expect(status.body).toMatchObject({ connected: true, mode: "manual", status: "idle", nextSyncAt: null, pendingChanges: 1 });
  });

  it("clears pending changes after a sync and refuses overlapping syncs", async () => {
    vi.useRealTimers();
    await call(server, "POST", "/movements", { assetId: w.assets.projector.id, toLocationId: w.locations.gate.id });
    await new Promise((r) => setTimeout(r, 20));

    const started = await triggerBcSync(50);
    expect(started.status).toBe("syncing");
    await expect(triggerBcSync(50)).rejects.toThrow("already in progress");

    await new Promise((r) => setTimeout(r, 120));
    const done = await getBcSyncStatus();
    expect(done).toMatchObject({ status: "success", syncedAssets: 2, pendingChanges: 0 });
  });
});
