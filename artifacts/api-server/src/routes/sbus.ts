import { Router } from "express";
import { db } from "@workspace/db";
import { sbusTable, assetsTable, locationsTable } from "@workspace/db";
import { eq, sql, count } from "drizzle-orm";

const router = Router();

async function buildSbuList(whereId?: number) {
  // Count assets per SBU
  const assetCounts = await db
    .select({ sbuId: assetsTable.sbuId, cnt: count() })
    .from(assetsTable)
    .groupBy(assetsTable.sbuId);
  const assetCountMap = Object.fromEntries(assetCounts.map(r => [r.sbuId, r.cnt]));

  // Count locations per SBU
  const locCounts = await db
    .select({ sbuId: locationsTable.sbuId, cnt: count() })
    .from(locationsTable)
    .groupBy(locationsTable.sbuId);
  const locCountMap = Object.fromEntries(locCounts.map(r => [r.sbuId, r.cnt]));

  const query = db.select().from(sbusTable).orderBy(sbusTable.code);
  const rows = whereId ? await query.where(eq(sbusTable.id, whereId)) : await query;
  return rows.map(r => ({
    ...r,
    assetCount: assetCountMap[r.id] ?? 0,
    locationCount: locCountMap[r.id] ?? 0,
  }));
}

// GET /sbus
router.get("/sbus", async (req, res) => {
  try {
    const sbus = await buildSbuList();
    res.json(sbus);
  } catch (err) {
    req.log.error(err);
    res.status(500).json({ error: "Failed to list SBUs" });
  }
});

// POST /sbus
router.post("/sbus", async (req, res): Promise<void> => {
  try {
    const { code, name, description, headOffice, color } = req.body;
    if (!code || !name) { res.status(400).json({ error: "code and name are required" }); return; }
    const [sbu] = await db.insert(sbusTable).values({ code, name, description, headOffice, color: color ?? "#6B7280" }).returning();
    res.status(201).json({ ...sbu, assetCount: 0, locationCount: 0 });
  } catch (err) {
    req.log.error(err);
    res.status(500).json({ error: "Failed to create SBU" });
  }
});

// GET /sbus/:id
router.get("/sbus/:id", async (req, res): Promise<void> => {
  try {
    const rows = await buildSbuList(parseInt(req.params.id));
    if (!rows[0]) { res.status(404).json({ error: "SBU not found" }); return; }
    res.json(rows[0]);
  } catch (err) {
    req.log.error(err);
    res.status(500).json({ error: "Failed to get SBU" });
  }
});

// PATCH /sbus/:id
router.patch("/sbus/:id", async (req, res): Promise<void> => {
  try {
    const id = parseInt(req.params.id);
    const { code, name, description, headOffice, color } = req.body;
    await db.update(sbusTable)
      .set({ ...(code && { code }), ...(name && { name }), description, headOffice, ...(color && { color }), updatedAt: new Date() })
      .where(eq(sbusTable.id, id));
    const rows = await buildSbuList(id);
    if (!rows[0]) { res.status(404).json({ error: "SBU not found" }); return; }
    res.json(rows[0]);
  } catch (err) {
    req.log.error(err);
    res.status(500).json({ error: "Failed to update SBU" });
  }
});

// GET /sbus/:id/assets
router.get("/sbus/:id/assets", async (req, res) => {
  try {
    const id = parseInt(req.params.id);
    const assets = await db
      .select({
        id: assetsTable.id,
        name: assetsTable.name,
        category: assetsTable.category,
        serialNumber: assetsTable.serialNumber,
        rfidTag: assetsTable.rfidTag,
        status: assetsTable.status,
        assignedTo: assetsTable.assignedTo,
        currentLocationId: assetsTable.currentLocationId,
        currentLocationName: locationsTable.name,
        sbuId: assetsTable.sbuId,
        purchaseDate: assetsTable.purchaseDate,
        purchaseValue: assetsTable.purchaseValue,
        notes: assetsTable.notes,
        createdAt: assetsTable.createdAt,
        updatedAt: assetsTable.updatedAt,
      })
      .from(assetsTable)
      .leftJoin(locationsTable, eq(assetsTable.currentLocationId, locationsTable.id))
      .where(eq(assetsTable.sbuId, id));
    res.json(assets);
  } catch (err) {
    req.log.error(err);
    res.status(500).json({ error: "Failed to get SBU assets" });
  }
});

export default router;
