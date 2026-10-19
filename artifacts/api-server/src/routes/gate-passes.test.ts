import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { aiInsightsTable, assetLifecycleEventsTable, assetsTable, movementsTable } from "@workspace/db/schema";
import { eq } from "drizzle-orm";
import { resetDb, testDb } from "../test/test-db";
import { seedBasics, type World } from "../test/fixtures";
import { call, startTestServer, type TestServer } from "../test/http";

/** Gate pass workflow: request → approve (gate_pass movement) / reject (flag the asset). */

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
  w = await seedBasics();
});
afterEach(() => {
  vi.useRealTimers();
});

const request = (body: Record<string, unknown>) => call(server, "POST", "/gate-passes", body);
const decide = (id: number, action: "approve" | "reject", body: Record<string, unknown>) =>
  call(server, "POST", `/gate-passes/${id}/${action}`, body);
const scanAtGate = (rfidTag = "TAG-DL-001") => call(server, "POST", "/rfid/simulate", { rfidTag, readerId: w.readers.gateReader.id });
const assetRow = async (id: number) => (await testDb.select().from(assetsTable).where(eq(assetsTable.id, id)))[0];
const movementsOf = (assetId: number) => testDb.select().from(movementsTable).where(eq(movementsTable.assetId, assetId));

describe("POST /gate-passes (Request gate pass form)", () => {
  it("creates a manual pending pass", async () => {
    const res = await request({
      assetId: w.assets.projector.id,
      requestedBy: "Tendai Moyo",
      reason: "Board meeting at head office",
      destination: "TSL Head Office",
      returnDate: "2026-10-16",
    });
    expect(res.status).toBe(201);
    expect(res.body).toMatchObject({
      status: "pending",
      source: "manual",
      requestedBy: "Tendai Moyo",
      returnDate: "2026-10-16",
      assetName: "Epson EB-X51 Projector",
      sbuName: "Tobacco Sales Administration Services",
      exitedAt: null,
    });
  });

  it("allows only one pending pass per asset", async () => {
    await request({ assetId: w.assets.projector.id, requestedBy: "Tendai Moyo", reason: "First" });
    const dup = await request({ assetId: w.assets.projector.id, requestedBy: "Tendai Moyo", reason: "Second" });
    expect(dup.status).toBe(409);
    expect(dup.body.error).toContain("already has a pending gate pass");
  });

  it("validates the request", async () => {
    expect((await request({ assetId: 999, requestedBy: "A", reason: "B" })).status).toBe(404);
    expect((await request({ assetId: w.assets.projector.id, requestedBy: "", reason: "B" })).status).toBe(400);
    expect((await request({ assetId: w.assets.projector.id, requestedBy: "   ", reason: "B" })).status).toBe(400);
    const past = await request({ assetId: w.assets.projector.id, requestedBy: "A", reason: "B", returnDate: "2026-10-01" });
    expect(past.status).toBe(400);
    expect(past.body.error).toContain("past");
  });
});

describe("approving", () => {
  it("records the approver, releases a held asset as a gate_pass movement and resolves the alert", async () => {
    const scan = await scanAtGate();
    const passId = scan.body.gateCheck.gatePass.id;
    vi.setSystemTime(new Date("2026-10-14T08:20:00Z"));

    const res = await decide(passId, "approve", { decidedBy: "Rudo Chikwanha", note: "Client visit confirmed" });

    expect(res.status).toBe(200);
    expect(res.body).toMatchObject({
      status: "approved",
      approvedBy: "Rudo Chikwanha",
      decisionNote: "Client visit confirmed",
      decidedAt: "2026-10-14T08:20:00.000Z",
      exitedAt: "2026-10-14T08:20:00.000Z",
    });

    const exits = (await movementsOf(w.assets.laptop.id)).filter((m) => m.triggeredBy === "gate_pass");
    expect(exits).toHaveLength(1);
    expect(exits[0]).toMatchObject({ fromLocationId: w.locations.gate.id, toLocationId: null, gatePassId: passId, movedBy: "John Doe" });
    expect((await assetRow(w.assets.laptop.id)).currentLocationId).toBeNull();

    const alerts = await testDb.select().from(aiInsightsTable).where(eq(aiInsightsTable.gatePassId, passId));
    expect(alerts.every((a) => a.dismissed)).toBe(true);
  });

  it("does not invent an exit for a pass approved in advance; the gate reader records it later", async () => {
    const created = await request({ assetId: w.assets.laptop.id, requestedBy: "John Doe", reason: "Home office", returnDate: "2026-10-20" });
    const approved = await decide(created.body.id, "approve", { decidedBy: "Rudo Chikwanha" });

    expect(approved.body.exitedAt).toBeNull();
    expect(await movementsOf(w.assets.laptop.id)).toEqual([]);
    expect((await assetRow(w.assets.laptop.id)).currentLocationId).toBe(w.locations.office.id);

    vi.setSystemTime(new Date("2026-10-14T15:30:00Z"));
    const exit = await scanAtGate();
    expect(exit.body.gateCheck.outcome).toBe("authorized");
    expect(exit.body.movement).toMatchObject({ triggeredBy: "gate_pass", gatePassId: created.body.id });

    const pass = await call(server, "GET", `/gate-passes/${created.body.id}`);
    expect(pass.body.exitedAt).toBe("2026-10-14T15:30:00.000Z");
  });

  it("refuses to decide a pass twice", async () => {
    const created = await request({ assetId: w.assets.projector.id, requestedBy: "Tendai Moyo", reason: "Event" });
    expect((await decide(created.body.id, "approve", { decidedBy: "Rudo Chikwanha" })).status).toBe(200);

    const again = await decide(created.body.id, "approve", { decidedBy: "Someone Else" });
    expect(again.status).toBe(409);
    expect(again.body.error).toBe(`Gate pass #${created.body.id} has already been approved`);
    expect((await decide(created.body.id, "reject", { decidedBy: "Someone Else" })).status).toBe(409);
  });

  it("requires a real approver name (no hard-coded 'Admin User')", async () => {
    const created = await request({ assetId: w.assets.projector.id, requestedBy: "Tendai Moyo", reason: "Event" });
    expect((await decide(created.body.id, "approve", {})).status).toBe(400);
    expect((await decide(created.body.id, "approve", { decidedBy: "" })).status).toBe(400);
    expect((await decide(created.body.id, "approve", { decidedBy: "   " })).status).toBe(400);
    expect((await decide(9999, "approve", { decidedBy: "Rudo Chikwanha" })).status).toBe(404);
    expect((await call(server, "POST", "/gate-passes/abc/approve", { decidedBy: "Rudo Chikwanha" })).status).toBe(400);
  });
});

describe("rejecting", () => {
  it("flags an asset caught at the gate: critical alert + audit note, and the asset stays held", async () => {
    const scan = await scanAtGate();
    const passId = scan.body.gateCheck.gatePass.id;

    const res = await decide(passId, "reject", { decidedBy: "Rudo Chikwanha", note: "No business reason" });
    expect(res.status).toBe(200);
    expect(res.body).toMatchObject({ status: "rejected", approvedBy: "Rudo Chikwanha", decisionNote: "No business reason" });

    const alerts = await testDb.select().from(aiInsightsTable).where(eq(aiInsightsTable.gatePassId, passId));
    const open = alerts.filter((a) => !a.dismissed);
    expect(open).toHaveLength(1);
    expect(open[0]).toMatchObject({ type: "unauthorized_movement", severity: "critical", assetId: w.assets.laptop.id });
    expect(open[0].message).toContain('the asset is at "TSAS Main Gate" and must not leave the premises');

    const notes = await testDb.select().from(assetLifecycleEventsTable).where(eq(assetLifecycleEventsTable.assetId, w.assets.laptop.id));
    expect(notes).toHaveLength(1);
    expect(notes[0]).toMatchObject({ eventType: "note", performedBy: "Rudo Chikwanha" });

    expect((await assetRow(w.assets.laptop.id)).currentLocationId).toBe(w.locations.gate.id);
    expect((await movementsOf(w.assets.laptop.id)).some((m) => m.triggeredBy === "gate_pass")).toBe(false);

    const history = await call(server, "GET", `/assets/${w.assets.laptop.id}/history`);
    expect(history.body.some((e: { notes: string | null }) => e.notes?.includes(`gate pass #${passId} rejected`))).toBe(true);
  });

  it("raises a warning (not critical) when a request made in advance is rejected", async () => {
    const created = await request({ assetId: w.assets.projector.id, requestedBy: "Tendai Moyo", reason: "Weekend use" });
    await decide(created.body.id, "reject", { decidedBy: "Rudo Chikwanha" });
    const [flag] = await testDb.select().from(aiInsightsTable).where(eq(aiInsightsTable.gatePassId, created.body.id));
    expect(flag).toMatchObject({ severity: "warning", dismissed: false });
  });
});

describe("GET /gate-passes", () => {
  it("filters by status and SBU", async () => {
    await scanAtGate();
    const manual = await request({ assetId: w.assets.projector.id, requestedBy: "Tendai Moyo", reason: "Event" });
    await decide(manual.body.id, "approve", { decidedBy: "Rudo Chikwanha" });

    expect((await call(server, "GET", "/gate-passes?status=pending")).body).toHaveLength(1);
    expect((await call(server, "GET", "/gate-passes?status=approved")).body).toHaveLength(1);
    expect((await call(server, "GET", `/gate-passes?sbuId=${w.sbus.tsas.id}`)).body).toHaveLength(2);
    expect((await call(server, "GET", `/gate-passes?sbuId=${w.sbus.bak.id}`)).body).toHaveLength(0);
  });
});
