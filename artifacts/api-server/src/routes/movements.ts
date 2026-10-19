import { Router } from "express";
import { db } from "@workspace/db";
import { movementsTable, assetsTable, locationsTable } from "@workspace/db";
import { and, desc, eq, inArray, type SQL } from "drizzle-orm";
import { CreateMovementBody } from "@workspace/api-zod";
import { HttpError, parseIdParam, sendError } from "../lib/http-error";

const router = Router();

const movementColumns = {
  id: movementsTable.id,
  assetId: movementsTable.assetId,
  assetName: assetsTable.name,
  rfidTag: assetsTable.rfidTag,
  fromLocationId: movementsTable.fromLocationId,
  toLocationId: movementsTable.toLocationId,
  triggeredBy: movementsTable.triggeredBy,
  rfidReaderId: movementsTable.rfidReaderId,
  gatePassId: movementsTable.gatePassId,
  movedBy: movementsTable.movedBy,
  notes: movementsTable.notes,
  timestamp: movementsTable.timestamp,
};

type MovementListRow = {
  id: number; assetId: number; assetName: string; rfidTag: string;
  fromLocationId: number | null; toLocationId: number | null;
  triggeredBy: string; rfidReaderId: number | null; gatePassId: number | null; movedBy: string | null;
  notes: string | null; timestamp: Date;
};

async function enrichMovements(movements: MovementListRow[]) {
  if (!movements.length) return [];
  const locationIds = [...new Set([
    ...movements.map(m => m.fromLocationId),
    ...movements.map(m => m.toLocationId),
  ].filter((x): x is number => x != null))];

  const locations = locationIds.length > 0
    ? await db.select({ id: locationsTable.id, name: locationsTable.name }).from(locationsTable).where(inArray(locationsTable.id, locationIds))
    : [];
  const locMap: Record<number, string> = Object.fromEntries(locations.map(l => [l.id, l.name]));

  return movements.map(m => ({
    ...m,
    fromLocationName: m.fromLocationId != null ? (locMap[m.fromLocationId] ?? null) : null,
    toLocationName: m.toLocationId != null ? (locMap[m.toLocationId] ?? null) : null,
  }));
}

// GET /movements?assetId=&sbuId=&limit=
router.get("/movements", async (req, res) => {
  try {
    const limitRaw = req.query.limit ? parseInt(String(req.query.limit)) : 50;
    const limit = Number.isNaN(limitRaw) ? 50 : Math.min(Math.max(limitRaw, 1), 200);
    const assetId = req.query.assetId ? parseInt(String(req.query.assetId)) : null;
    const sbuId = req.query.sbuId ? parseInt(String(req.query.sbuId)) : null;

    const conditions: SQL[] = [];
    if (assetId != null && !Number.isNaN(assetId)) conditions.push(eq(movementsTable.assetId, assetId));
    if (sbuId != null && !Number.isNaN(sbuId)) conditions.push(eq(assetsTable.sbuId, sbuId));

    const rows = await db
      .select(movementColumns)
      .from(movementsTable)
      .innerJoin(assetsTable, eq(movementsTable.assetId, assetsTable.id))
      .where(conditions.length > 0 ? and(...conditions) : undefined)
      .orderBy(desc(movementsTable.timestamp), desc(movementsTable.id))
      .limit(limit);

    res.json(await enrichMovements(rows));
  } catch (err) {
    sendError(req, res, err, "Failed to list movements");
  }
});

// POST /movements - manual movement recorded by a person
router.post("/movements", async (req, res): Promise<void> => {
  try {
    const parsed = CreateMovementBody.safeParse(req.body);
    if (!parsed.success) { res.status(400).json({ error: parsed.error.message }); return; }
    const { assetId, toLocationId } = parsed.data;

    const [asset] = await db.select().from(assetsTable).where(eq(assetsTable.id, assetId));
    if (!asset) throw new HttpError(404, "Asset not found");
    const [destination] = await db.select({ id: locationsTable.id, name: locationsTable.name }).from(locationsTable).where(eq(locationsTable.id, toLocationId));
    if (!destination) throw new HttpError(404, "Destination location not found");
    if (asset.currentLocationId === toLocationId) throw new HttpError(400, `"${asset.name}" is already at ${destination.name}`);

    const movement = await db.transaction(async (tx) => {
      const [row] = await tx.insert(movementsTable).values({
        assetId,
        fromLocationId: asset.currentLocationId,
        toLocationId,
        triggeredBy: "manual",
        movedBy: parsed.data.movedBy?.trim() || null,
        notes: parsed.data.notes?.trim() || null,
        timestamp: new Date(),
      }).returning();
      await tx.update(assetsTable)
        .set({ currentLocationId: toLocationId, updatedAt: new Date() })
        .where(eq(assetsTable.id, assetId));
      return row;
    });

    const [enriched] = await enrichMovements([{ ...movement, assetName: asset.name, rfidTag: asset.rfidTag }]);
    res.status(201).json(enriched);
  } catch (err) {
    sendError(req, res, err, "Failed to create movement");
  }
});

// GET /movements/:id
router.get("/movements/:id", async (req, res): Promise<void> => {
  try {
    const id = parseIdParam(req.params.id);
    const [movement] = await db
      .select(movementColumns)
      .from(movementsTable)
      .innerJoin(assetsTable, eq(movementsTable.assetId, assetsTable.id))
      .where(eq(movementsTable.id, id));

    if (!movement) throw new HttpError(404, "Movement not found");
    const [enriched] = await enrichMovements([movement]);
    res.json(enriched);
  } catch (err) {
    sendError(req, res, err, "Failed to get movement");
  }
});

export default router;
