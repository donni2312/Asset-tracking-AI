import { Router } from "express";
import { db } from "@workspace/db";
import { locationsTable, assetsTable, sbusTable } from "@workspace/db";
import { eq, sql, and } from "drizzle-orm";
import { CreateLocationBody, UpdateLocationBody } from "@workspace/api-zod";
import { HttpError, parseIdParam, sendError } from "../lib/http-error";

const router = Router();

async function locationWithSbu(id: number) {
  const [loc] = await db
    .select({
      id: locationsTable.id,
      name: locationsTable.name,
      type: locationsTable.type,
      building: locationsTable.building,
      floor: locationsTable.floor,
      description: locationsTable.description,
      sbuId: locationsTable.sbuId,
      sbuName: sbusTable.name,
      sbuCode: sbusTable.code,
      createdAt: locationsTable.createdAt,
      assetCount: sql<number>`cast(count(${assetsTable.id}) as int)`,
    })
    .from(locationsTable)
    .leftJoin(assetsTable, eq(assetsTable.currentLocationId, locationsTable.id))
    .leftJoin(sbusTable, eq(locationsTable.sbuId, sbusTable.id))
    .groupBy(locationsTable.id, sbusTable.name, sbusTable.code)
    .where(eq(locationsTable.id, id));
  return loc ?? null;
}

// GET /locations
router.get("/locations", async (req, res) => {
  try {
    const sbuId = req.query.sbuId ? parseInt(String(req.query.sbuId)) : null;
    const type = req.query.type ? String(req.query.type) : null;

    const conditions = [];
    if (sbuId != null) conditions.push(eq(locationsTable.sbuId, sbuId));
    if (type) conditions.push(eq(locationsTable.type, type));

    const locations = await db
      .select({
        id: locationsTable.id,
        name: locationsTable.name,
        type: locationsTable.type,
        building: locationsTable.building,
        floor: locationsTable.floor,
        description: locationsTable.description,
        sbuId: locationsTable.sbuId,
        sbuName: sbusTable.name,
        sbuCode: sbusTable.code,
        createdAt: locationsTable.createdAt,
        assetCount: sql<number>`cast(count(${assetsTable.id}) as int)`,
      })
      .from(locationsTable)
      .leftJoin(assetsTable, eq(assetsTable.currentLocationId, locationsTable.id))
      .leftJoin(sbusTable, eq(locationsTable.sbuId, sbusTable.id))
      .groupBy(locationsTable.id, sbusTable.name, sbusTable.code)
      .where(conditions.length > 0 ? and(...conditions) : undefined)
      .orderBy(sbusTable.code, locationsTable.name);

    res.json(locations);
  } catch (err) {
    req.log.error(err);
    res.status(500).json({ error: "Failed to list locations" });
  }
});

// POST /locations
router.post("/locations", async (req, res): Promise<void> => {
  try {
    const parsed = CreateLocationBody.safeParse(req.body);
    if (!parsed.success) { res.status(400).json({ error: parsed.error.message }); return; }
    const [loc] = await db.insert(locationsTable).values({
      ...parsed.data,
      sbuId: parsed.data.sbuId ?? null,
    }).returning();
    // Return the same enriched shape as GET (SBU name/code, asset count).
    res.status(201).json(await locationWithSbu(loc.id));
  } catch (err) {
    sendError(req, res, err, "Failed to create location");
  }
});

// GET /locations/:id
router.get("/locations/:id", async (req, res): Promise<void> => {
  try {
    const loc = await locationWithSbu(parseIdParam(req.params.id));
    if (!loc) throw new HttpError(404, "Location not found");
    res.json(loc);
  } catch (err) {
    sendError(req, res, err, "Failed to get location");
  }
});

// PATCH /locations/:id
router.patch("/locations/:id", async (req, res): Promise<void> => {
  try {
    const id = parseIdParam(req.params.id);
    const parsed = UpdateLocationBody.safeParse(req.body);
    if (!parsed.success) { res.status(400).json({ error: parsed.error.message }); return; }
    const [updated] = await db.update(locationsTable)
      .set({ ...parsed.data, updatedAt: new Date() })
      .where(eq(locationsTable.id, id))
      .returning({ id: locationsTable.id });
    if (!updated) throw new HttpError(404, "Location not found");
    res.json(await locationWithSbu(id));
  } catch (err) {
    sendError(req, res, err, "Failed to update location");
  }
});

// GET /locations/:id/assets
router.get("/locations/:id/assets", async (req, res) => {
  try {
    const assets = await db
      .select().from(assetsTable)
      .where(eq(assetsTable.currentLocationId, parseIdParam(req.params.id)));
    res.json(assets);
  } catch (err) {
    sendError(req, res, err, "Failed to get location assets");
  }
});

export default router;
