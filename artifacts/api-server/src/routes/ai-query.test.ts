import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { assetsTable, gatePassesTable, movementsTable } from "@workspace/db/schema";
import { eq } from "drizzle-orm";
import { resetDb, testDb } from "../test/test-db";
import { seedBasics, type World } from "../test/fixtures";
import { call, startTestServer, type TestServer } from "../test/http";
import { answerQuestion } from "../services/nl-query";

/** Natural-language queries — the plan's three example questions answered from real data. */

const WED_10AM = new Date("2026-10-14T08:00:00Z"); // week started Mon 12 Oct (Harare)
const daysAgo = (n: number) => new Date(WED_10AM.getTime() - n * 24 * 60 * 60 * 1000);

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

  // Both assets were registered long ago.
  await testDb.update(assetsTable).set({ createdAt: daysAgo(200) });
  // Laptop moved two days ago; projector last moved 45 days ago.
  await testDb.insert(movementsTable).values([
    { assetId: w.assets.laptop.id, toLocationId: w.locations.office.id, triggeredBy: "rfid", timestamp: daysAgo(2) },
    { assetId: w.assets.projector.id, toLocationId: w.locations.office.id, triggeredBy: "manual", timestamp: daysAgo(45) },
  ]);
  // One pass approved this week (Tue 13 Oct), one approved last week (Thu 8 Oct), one pending.
  await testDb.insert(gatePassesTable).values([
    { assetId: w.assets.laptop.id, requestedBy: "John Doe", reason: "Client demo", destination: "Borrowdale", status: "approved", approvedBy: "Rudo Chikwanha", decidedAt: daysAgo(1) },
    { assetId: w.assets.projector.id, requestedBy: "Tendai Moyo", reason: "Expo", status: "approved", approvedBy: "Rudo Chikwanha", decidedAt: daysAgo(6) },
    { assetId: w.assets.projector.id, requestedBy: "Tendai Moyo", reason: "Board meeting", status: "pending" },
  ]);
});
afterEach(() => {
  vi.useRealTimers();
  vi.unstubAllEnvs();
});

const ask = (question: string) => call(server, "POST", "/ai/query", { question });

describe("POST /api/ai/query — plan section 6.5 examples", () => {
  it('"Where is the Dell laptop assigned to John Doe?"', async () => {
    const res = await ask("Where is the Dell laptop assigned to John Doe?");
    expect(res.status).toBe(200);
    expect(res.body).toMatchObject({ intent: "locate_asset", source: "rules" });
    expect(res.body.answer).toContain("Dell Latitude 5440 (TAG-DL-001) is at TSAS IT Office [TSAS], assigned to John Doe.");
    expect(res.body.relatedAssets.map((a: { id: number }) => a.id)).toEqual([w.assets.laptop.id]);
  });

  it('"Which assets have not moved in 30 days?"', async () => {
    const res = await ask("Which assets have not moved in 30 days?");
    expect(res.body.intent).toBe("not_moved");
    expect(res.body.answer).toMatch(/^1 asset has not moved in 30 days:/);
    expect(res.body.answer).toContain("Epson EB-X51 Projector");
    expect(res.body.answer).toContain("45 days ago");
    expect(res.body.relatedAssets.map((a: { id: number }) => a.id)).toEqual([w.assets.projector.id]);
  });

  it('"Show me all gate passes approved this week"', async () => {
    const res = await ask("Show me all gate passes approved this week");
    expect(res.body.intent).toBe("gate_passes");
    expect(res.body.answer).toMatch(/^1 gate pass approved this week:/);
    expect(res.body.answer).toContain("Dell Latitude 5440 → Borrowdale — approved by Rudo Chikwanha on Tue 13 Oct 10:00");
    expect(res.body.relatedGatePasses).toHaveLength(1);
    expect(res.body.relatedGatePasses[0]).toMatchObject({ assetId: w.assets.laptop.id, status: "approved" });
  });
});

describe("other questions", () => {
  it("answers 'last week' separately from 'this week'", async () => {
    const res = await ask("Gate passes approved last week");
    expect(res.body.relatedGatePasses.map((p: { assetId: number }) => p.assetId)).toEqual([w.assets.projector.id]);
  });

  it("falls back to updatedAt for passes decided before decidedAt existed", async () => {
    await testDb.insert(gatePassesTable).values({
      assetId: w.assets.projector.id, requestedBy: "Tendai Moyo", reason: "Legacy", status: "approved",
      approvedBy: "Legacy Approver", decidedAt: null, updatedAt: daysAgo(0.5),
    });
    const res = await ask("Show me all gate passes approved this week");
    expect(res.body.relatedGatePasses).toHaveLength(2);
    expect(res.body.answer).toContain("approved by Legacy Approver");
  });

  it("explains when nothing matches", async () => {
    const res = await ask("Where is the HP printer?");
    expect(res.body.answer).toContain("couldn't find an asset matching");
    expect(res.body.relatedAssets).toEqual([]);
  });

  it("reports an asset that left under a gate pass as off-premises", async () => {
    await testDb.update(assetsTable).set({ currentLocationId: null }).where(eq(assetsTable.id, w.assets.laptop.id));
    await testDb.insert(movementsTable).values({ assetId: w.assets.laptop.id, triggeredBy: "gate_pass", gatePassId: 1, timestamp: daysAgo(1) });
    const res = await ask("Where is John Doe's laptop?");
    expect(res.body.answer).toContain("is off-premises (left under gate pass #1");
  });

  it("offers help for unrelated questions and rejects empty ones", async () => {
    const res = await ask("What's the weather like?");
    expect(res.body.intent).toBe("help");
    expect(res.body.answer).toContain("Where is the Dell laptop assigned to John Doe?");
    expect((await ask("   ")).status).toBe(400);
  });
});

describe("answerQuestion with OpenAI configured", () => {
  const completion = (intent: Record<string, unknown>) =>
    new Response(
      JSON.stringify({
        choices: [
          {
            message: {
              content: JSON.stringify({
                intent: "help", terms: [], assignee: null, days: null, status: null, period: null, periodDays: null, category: null,
                ...intent,
              }),
            },
          },
        ],
      }),
      { status: 200 },
    );

  it("uses the model's intent and answers from the database", async () => {
    vi.stubEnv("OPENAI_API_KEY", "sk-test");
    const fetchImpl = vi.fn(async () => completion({ intent: "not_moved", days: 30 }));
    const result = await answerQuestion("Anything gathering dust for a month?", { fetchImpl: fetchImpl as unknown as typeof fetch });
    expect(fetchImpl).toHaveBeenCalledOnce();
    expect(result).toMatchObject({ source: "openai", intent: "not_moved" });
    expect(result.relatedAssets.map((a) => a.id)).toEqual([w.assets.projector.id]);
  });

  it("falls back to the rule-based parser when OpenAI fails", async () => {
    vi.stubEnv("OPENAI_API_KEY", "sk-test");
    const fetchImpl = vi.fn(async () => new Response("{}", { status: 503 }));
    const result = await answerQuestion("Where is the Dell laptop assigned to John Doe?", { fetchImpl: fetchImpl as unknown as typeof fetch });
    expect(result).toMatchObject({ source: "rules", intent: "locate_asset" });
    expect(result.answer).toContain("TSAS IT Office");
  });

  it("does not call OpenAI when no key is configured", async () => {
    const fetchImpl = vi.fn();
    const result = await answerQuestion("Which assets are missing?", { fetchImpl: fetchImpl as unknown as typeof fetch });
    expect(fetchImpl).not.toHaveBeenCalled();
    expect(result.source).toBe("rules");
  });
});
