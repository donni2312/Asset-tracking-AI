import { Router } from "express";
import { db } from "@workspace/db";
import { rfidReadersTable, assetsTable, movementsTable, locationsTable, aiInsightsTable, sbusTable } from "@workspace/db";
import { eq } from "drizzle-orm";
import { SimulateRfidReadBody } from "@workspace/api-zod";

const router = Router();

// GET /rfid/readers
router.get("/rfid/readers", async (req, res) => {
  try {
    const sbuId = req.query.sbuId ? parseInt(String(req.query.sbuId)) : null;

    const readers = await db
      .select({
        id: rfidReadersTable.id,
        name: rfidReadersTable.name,
        locationId: rfidReadersTable.locationId,
        locationName: locationsTable.name,
        sbuId: locationsTable.sbuId,
        sbuCode: sbusTable.code,
        sbuName: sbusTable.name,
        status: rfidReadersTable.status,
        lastReadAt: rfidReadersTable.lastReadAt,
      })
      .from(rfidReadersTable)
      .innerJoin(locationsTable, eq(rfidReadersTable.locationId, locationsTable.id))
      .leftJoin(sbusTable, eq(locationsTable.sbuId, sbusTable.id))
      .where(sbuId != null ? eq(locationsTable.sbuId, sbuId) : undefined)
      .orderBy(sbusTable.code, rfidReadersTable.name);

    res.json(readers);
  } catch (err) {
    req.log.error(err);
    res.status(500).json({ error: "Failed to list RFID readers" });
  }
});

// POST /rfid/simulate
router.post("/rfid/simulate", async (req, res): Promise<void> => {
  try {
    const parsed = SimulateRfidReadBody.safeParse(req.body);
    if (!parsed.success) { res.status(400).json({ error: parsed.error.message }); return; }

    const { rfidTag, readerId } = parsed.data;

    const [asset] = await db.select().from(assetsTable).where(eq(assetsTable.rfidTag, rfidTag));
    if (!asset) { res.status(404).json({ error: `No asset with RFID tag: ${rfidTag}` }); return; }

    const [reader] = await db
      .select({ id: rfidReadersTable.id, locationId: rfidReadersTable.locationId, name: rfidReadersTable.name })
      .from(rfidReadersTable)
      .where(eq(rfidReadersTable.id, readerId));
    if (!reader) { res.status(404).json({ error: "RFID reader not found" }); return; }

    const prevLocationId = asset.currentLocationId;

    const [movement] = await db.insert(movementsTable).values({
      assetId: asset.id,
      fromLocationId: prevLocationId,
      toLocationId: reader.locationId,
      triggeredBy: "rfid",
      rfidReaderId: reader.id,
      notes: `Auto-detected by RFID reader: ${reader.name}`,
    }).returning();

    await db.update(assetsTable)
      .set({ currentLocationId: reader.locationId, updatedAt: new Date() })
      .where(eq(assetsTable.id, asset.id));

    await db.update(rfidReadersTable).set({ lastReadAt: new Date() }).where(eq(rfidReadersTable.id, readerId));

    const [location] = await db
      .select({ type: locationsTable.type, name: locationsTable.name, sbuId: locationsTable.sbuId })
      .from(locationsTable).where(eq(locationsTable.id, reader.locationId));

    const sbuInfo = location?.sbuId != null
      ? await db.select({ code: sbusTable.code, name: sbusTable.name }).from(sbusTable).where(eq(sbusTable.id, location.sbuId))
      : [];

    if (location?.type === "gate") {
      const sbuTag = sbuInfo[0] ? ` [${sbuInfo[0].code}]` : "";
      await db.insert(aiInsightsTable).values({
        type: "anomaly",
        message: `Asset "${asset.name}" (${rfidTag}) detected at gate "${location.name}"${sbuTag} — verify gate pass authorization`,
        assetId: asset.id,
        severity: "warning",
        dismissed: false,
      });
    }

    const [fromLoc] = prevLocationId != null
      ? await db.select({ name: locationsTable.name }).from(locationsTable).where(eq(locationsTable.id, prevLocationId))
      : [null];
    const [toLoc] = await db.select({ name: locationsTable.name }).from(locationsTable).where(eq(locationsTable.id, reader.locationId));

    res.json({
      ...movement,
      assetName: asset.name,
      rfidTag: asset.rfidTag,
      fromLocationName: fromLoc?.name ?? null,
      toLocationName: toLoc?.name ?? null,
      sbuId: location?.sbuId ?? null,
      sbuName: sbuInfo[0]?.name ?? null,
    });
  } catch (err) {
    req.log.error(err);
    res.status(500).json({ error: "RFID simulation failed" });
  }
});

export default router;
