import { Router } from "express";
import { db } from "@workspace/db";
import { gatePassesTable, assetsTable } from "@workspace/db";
import { eq, desc } from "drizzle-orm";
import {
  CreateGatePassBody,
  ListGatePassesQueryParams,
  ApproveGatePassBody,
  RejectGatePassBody,
} from "@workspace/api-zod";

const router = Router();

async function enrichGatePasses(passes: Array<typeof gatePassesTable.$inferSelect>) {
  if (!passes.length) return [];
  const allAssets = await db.select({ id: assetsTable.id, name: assetsTable.name, rfidTag: assetsTable.rfidTag }).from(assetsTable);
  const assetMap: Record<number, { name: string; rfidTag: string }> = Object.fromEntries(
    allAssets.map(a => [a.id, { name: a.name, rfidTag: a.rfidTag }])
  );

  return passes.map(p => ({
    ...p,
    assetName: assetMap[p.assetId]?.name ?? "Unknown",
    rfidTag: assetMap[p.assetId]?.rfidTag ?? "",
  }));
}

// GET /gate-passes
router.get("/gate-passes", async (req, res) => {
  try {
    const parsed = ListGatePassesQueryParams.safeParse(req.query);
    const filters = parsed.success ? parsed.data : {};

    const passes = await db.select().from(gatePassesTable)
      .where(filters.status ? eq(gatePassesTable.status, filters.status) : undefined)
      .orderBy(desc(gatePassesTable.createdAt));

    res.json(await enrichGatePasses(passes));
  } catch (err) {
    req.log.error(err);
    res.status(500).json({ error: "Failed to list gate passes" });
  }
});

// POST /gate-passes
router.post("/gate-passes", async (req, res): Promise<void> => {
  try {
    const parsed = CreateGatePassBody.safeParse(req.body);
    if (!parsed.success) {
      res.status(400).json({ error: parsed.error.message });
      return;
    }

    const { returnDate, ...rest } = parsed.data;
    const [gatePass] = await db.insert(gatePassesTable).values({
      assetId: rest.assetId,
      requestedBy: rest.requestedBy,
      reason: rest.reason,
      destination: rest.destination,
      status: "pending",
      returnDate: returnDate instanceof Date ? returnDate.toISOString().split("T")[0] : returnDate,
    }).returning();

    const enriched = await enrichGatePasses([gatePass]);
    res.status(201).json(enriched[0]);
  } catch (err) {
    req.log.error(err);
    res.status(500).json({ error: "Failed to create gate pass" });
  }
});

// GET /gate-passes/:id
router.get("/gate-passes/:id", async (req, res): Promise<void> => {
  try {
    const id = parseInt(req.params.id);
    const [gatePass] = await db.select().from(gatePassesTable).where(eq(gatePassesTable.id, id));
    if (!gatePass) {
      res.status(404).json({ error: "Gate pass not found" });
      return;
    }
    const enriched = await enrichGatePasses([gatePass]);
    res.json(enriched[0]);
  } catch (err) {
    req.log.error(err);
    res.status(500).json({ error: "Failed to get gate pass" });
  }
});

// POST /gate-passes/:id/approve
router.post("/gate-passes/:id/approve", async (req, res): Promise<void> => {
  try {
    const id = parseInt(req.params.id);
    const parsed = ApproveGatePassBody.safeParse(req.body);
    if (!parsed.success) {
      res.status(400).json({ error: parsed.error.message });
      return;
    }

    await db.update(gatePassesTable).set({
      status: "approved",
      approvedBy: parsed.data.decidedBy,
      decisionNote: parsed.data.note,
      updatedAt: new Date(),
    }).where(eq(gatePassesTable.id, id));

    const [gatePass] = await db.select().from(gatePassesTable).where(eq(gatePassesTable.id, id));
    if (!gatePass) {
      res.status(404).json({ error: "Gate pass not found" });
      return;
    }
    const enriched = await enrichGatePasses([gatePass]);
    res.json(enriched[0]);
  } catch (err) {
    req.log.error(err);
    res.status(500).json({ error: "Failed to approve gate pass" });
  }
});

// POST /gate-passes/:id/reject
router.post("/gate-passes/:id/reject", async (req, res): Promise<void> => {
  try {
    const id = parseInt(req.params.id);
    const parsed = RejectGatePassBody.safeParse(req.body);
    if (!parsed.success) {
      res.status(400).json({ error: parsed.error.message });
      return;
    }

    await db.update(gatePassesTable).set({
      status: "rejected",
      approvedBy: parsed.data.decidedBy,
      decisionNote: parsed.data.note,
      updatedAt: new Date(),
    }).where(eq(gatePassesTable.id, id));

    const [gatePass] = await db.select().from(gatePassesTable).where(eq(gatePassesTable.id, id));
    if (!gatePass) {
      res.status(404).json({ error: "Gate pass not found" });
      return;
    }
    const enriched = await enrichGatePasses([gatePass]);
    res.json(enriched[0]);
  } catch (err) {
    req.log.error(err);
    res.status(500).json({ error: "Failed to reject gate pass" });
  }
});

export default router;
