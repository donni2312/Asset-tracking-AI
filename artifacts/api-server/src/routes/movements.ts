import { Router } from "express";
import { db } from "@workspace/db";
import { movementsTable, assetsTable, locationsTable } from "@workspace/db";
import { eq, desc, sql } from "drizzle-orm";
import { CreateMovementBody } from "@workspace/api-zod";

const router = Router();

async function enrichMovements(movements: Array<{
  id: number; assetId: number; assetName: string; rfidTag: string;
  fromLocationId: number | null; toLocationId: number | null;
  triggeredBy: string; rfidReaderId: number | null; movedBy: string | null;
  notes: string | null; timestamp: Date;
}>) {
  if (!movements.length) return [];
  const locationIds = [...new Set([
    ...movements.map(m => m.fromLocationId),
    ...movements.map(m => m.toLocationId),
  ].filter((x): x is number => x != null))];

  const locations = locationIds.length > 0
    ? await db.select().from(locationsTable).where(
        sql`${locationsTable.id} = ANY(ARRAY[${sql.join(locationIds.map(id => sql`${id}::int`), sql`, `)}])`
      )
    : [];
  const locMap: Record<number, string> = Object.fromEntries(locations.map(l => [l.id, l.name]));

  return movements.map(m => ({
    ...m,
    fromLocationName: m.fromLocationId != null ? (locMap[m.fromLocationId] ?? null) : null,
    toLocationName: m.toLocationId != null ? (locMap[m.toLocationId] ?? null) : null,
  }));
}

// GET /movements
router.get("/movements", async (req, res) => {
  try {
    const limitRaw = req.query.limit;
    const assetIdRaw = req.query.assetId;
    const limit = limitRaw ? Math.min(parseInt(String(limitRaw)), 200) : 50;
    const assetId = assetIdRaw ? parseInt(String(assetIdRaw)) : null;

    const rows = await db
      .select({
        id: movementsTable.id,
        assetId: movementsTable.assetId,
        assetName: assetsTable.name,
        rfidTag: assetsTable.rfidTag,
        fromLocationId: movementsTable.fromLocationId,
        toLocationId: movementsTable.toLocationId,
        triggeredBy: movementsTable.triggeredBy,
        rfidReaderId: movementsTable.rfidReaderId,
        movedBy: movementsTable.movedBy,
        notes: movementsTable.notes,
        timestamp: movementsTable.timestamp,
      })
      .from(movementsTable)
      .innerJoin(assetsTable, eq(movementsTable.assetId, assetsTable.id))
      .where(assetId != null ? eq(movementsTable.assetId, assetId) : undefined)
      .orderBy(desc(movementsTable.timestamp))
      .limit(limit);

    res.json(await enrichMovements(rows));
  } catch (err) {
    req.log.error(err);
    res.status(500).json({ error: "Failed to list movements" });
  }
});

// POST /movements
router.post("/movements", async (req, res): Promise<void> => {
  try {
    const parsed = CreateMovementBody.safeParse(req.body);
    if (!parsed.success) {
      res.status(400).json({ error: parsed.error.message });
      return;
    }

    const [asset] = await db.select().from(assetsTable).where(eq(assetsTable.id, parsed.data.assetId));
    if (!asset) {
      res.status(404).json({ error: "Asset not found" });
      return;
    }

    const [movement] = await db.insert(movementsTable).values({
      assetId: parsed.data.assetId,
      fromLocationId: asset.currentLocationId,
      toLocationId: parsed.data.toLocationId,
      triggeredBy: "manual",
      movedBy: parsed.data.movedBy,
      notes: parsed.data.notes,
    }).returning();

    await db.update(assetsTable)
      .set({ currentLocationId: parsed.data.toLocationId, updatedAt: new Date() })
      .where(eq(assetsTable.id, parsed.data.assetId));

    const enriched = await enrichMovements([{ ...movement, assetName: asset.name, rfidTag: asset.rfidTag }]);
    res.status(201).json(enriched[0]);
  } catch (err) {
    req.log.error(err);
    res.status(500).json({ error: "Failed to create movement" });
  }
});

// GET /movements/:id
router.get("/movements/:id", async (req, res): Promise<void> => {
  try {
    const id = parseInt(req.params.id);
    const [movement] = await db
      .select({
        id: movementsTable.id,
        assetId: movementsTable.assetId,
        assetName: assetsTable.name,
        rfidTag: assetsTable.rfidTag,
        fromLocationId: movementsTable.fromLocationId,
        toLocationId: movementsTable.toLocationId,
        triggeredBy: movementsTable.triggeredBy,
        rfidReaderId: movementsTable.rfidReaderId,
        movedBy: movementsTable.movedBy,
        notes: movementsTable.notes,
        timestamp: movementsTable.timestamp,
      })
      .from(movementsTable)
      .innerJoin(assetsTable, eq(movementsTable.assetId, assetsTable.id))
      .where(eq(movementsTable.id, id));

    if (!movement) {
      res.status(404).json({ error: "Movement not found" });
      return;
    }
    const enriched = await enrichMovements([movement]);
    res.json(enriched[0]);
  } catch (err) {
    req.log.error(err);
    res.status(500).json({ error: "Failed to get movement" });
  }
});

export default router;
