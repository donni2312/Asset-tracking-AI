import { Router } from "express";
import { db } from "@workspace/db";
import { assetLifecycleEventsTable, assetsTable, sbusTable } from "@workspace/db";
import { eq, desc, and } from "drizzle-orm";

const router = Router();

async function enrichLifecycleEvents(events: Array<typeof assetLifecycleEventsTable.$inferSelect & { assetName: string; rfidTag: string }>) {
  if (!events.length) return [];
  const allSbus = await db.select().from(sbusTable);
  const sbuMap: Record<number, { name: string; code: string }> = Object.fromEntries(allSbus.map(s => [s.id, { name: s.name, code: s.code }]));
  return events.map(e => ({
    ...e,
    fromSbuName: e.fromSbuId != null ? (sbuMap[e.fromSbuId]?.name ?? null) : null,
    toSbuName: e.toSbuId != null ? (sbuMap[e.toSbuId]?.name ?? null) : null,
  }));
}

// GET /assets/:id/lifecycle
router.get("/assets/:id/lifecycle", async (req, res) => {
  try {
    const assetId = parseInt(req.params.id);
    const [asset] = await db.select({ name: assetsTable.name, rfidTag: assetsTable.rfidTag })
      .from(assetsTable).where(eq(assetsTable.id, assetId));

    const events = await db.select().from(assetLifecycleEventsTable)
      .where(eq(assetLifecycleEventsTable.assetId, assetId))
      .orderBy(desc(assetLifecycleEventsTable.timestamp));

    const enriched = await enrichLifecycleEvents(
      events.map(e => ({ ...e, assetName: asset?.name ?? "", rfidTag: asset?.rfidTag ?? "" }))
    );
    res.json(enriched);
  } catch (err) {
    req.log.error(err);
    res.status(500).json({ error: "Failed to list lifecycle events" });
  }
});

// POST /assets/:id/lifecycle
router.post("/assets/:id/lifecycle", async (req, res): Promise<void> => {
  try {
    const assetId = parseInt(req.params.id);
    const [asset] = await db.select().from(assetsTable).where(eq(assetsTable.id, assetId));
    if (!asset) { res.status(404).json({ error: "Asset not found" }); return; }

    const { eventType, performedBy, fromSbuId, toSbuId, assignedTo, vendor, ticketRef, conditionBefore, conditionAfter, notes } = req.body;
    if (!eventType || !performedBy) { res.status(400).json({ error: "eventType and performedBy are required" }); return; }

    const [event] = await db.insert(assetLifecycleEventsTable).values({
      assetId,
      eventType,
      performedBy,
      fromSbuId: fromSbuId ?? null,
      toSbuId: toSbuId ?? null,
      assignedTo: assignedTo ?? null,
      vendor: vendor ?? null,
      ticketRef: ticketRef ?? null,
      conditionBefore: conditionBefore ?? null,
      conditionAfter: conditionAfter ?? null,
      notes: notes ?? null,
    }).returning();

    // Auto-update asset status based on event type
    const statusMap: Record<string, string> = {
      deployment: "active",
      return_to_it: "active",
      repair_sent: "maintenance",
      repair_returned: "active",
    };
    if (statusMap[eventType]) {
      await db.update(assetsTable).set({ status: statusMap[eventType], updatedAt: new Date() }).where(eq(assetsTable.id, assetId));
    }
    if (eventType === "deployment" && assignedTo) {
      await db.update(assetsTable).set({ assignedTo, updatedAt: new Date() }).where(eq(assetsTable.id, assetId));
    }
    if (eventType === "return_to_it") {
      await db.update(assetsTable).set({ assignedTo: null, updatedAt: new Date() }).where(eq(assetsTable.id, assetId));
    }
    if (eventType === "sbu_transfer" && toSbuId) {
      await db.update(assetsTable).set({ sbuId: toSbuId, updatedAt: new Date() }).where(eq(assetsTable.id, assetId));
    }

    const enriched = await enrichLifecycleEvents([{ ...event, assetName: asset.name, rfidTag: asset.rfidTag }]);
    res.status(201).json(enriched[0]);
  } catch (err) {
    req.log.error(err);
    res.status(500).json({ error: "Failed to create lifecycle event" });
  }
});

// GET /lifecycle
router.get("/lifecycle", async (req, res) => {
  try {
    const limitRaw = req.query.limit;
    const eventTypeRaw = req.query.eventType;
    const sbuIdRaw = req.query.sbuId;
    const limit = limitRaw ? Math.min(parseInt(String(limitRaw)), 200) : 50;
    const eventType = eventTypeRaw ? String(eventTypeRaw) : null;
    const sbuId = sbuIdRaw ? parseInt(String(sbuIdRaw)) : null;

    // Build where conditions
    const conditions = [];
    if (eventType) conditions.push(eq(assetLifecycleEventsTable.eventType, eventType));
    if (sbuId != null) {
      // Filter by SBU: assets belonging to that SBU
      const sbuAssets = await db.select({ id: assetsTable.id }).from(assetsTable).where(eq(assetsTable.sbuId, sbuId));
      const assetIds = sbuAssets.map(a => a.id);
      if (assetIds.length === 0) { res.json([]); return; }
    }

    const events = await db
      .select({
        id: assetLifecycleEventsTable.id,
        assetId: assetLifecycleEventsTable.assetId,
        assetName: assetsTable.name,
        rfidTag: assetsTable.rfidTag,
        eventType: assetLifecycleEventsTable.eventType,
        performedBy: assetLifecycleEventsTable.performedBy,
        fromSbuId: assetLifecycleEventsTable.fromSbuId,
        toSbuId: assetLifecycleEventsTable.toSbuId,
        assignedTo: assetLifecycleEventsTable.assignedTo,
        vendor: assetLifecycleEventsTable.vendor,
        ticketRef: assetLifecycleEventsTable.ticketRef,
        conditionBefore: assetLifecycleEventsTable.conditionBefore,
        conditionAfter: assetLifecycleEventsTable.conditionAfter,
        notes: assetLifecycleEventsTable.notes,
        timestamp: assetLifecycleEventsTable.timestamp,
      })
      .from(assetLifecycleEventsTable)
      .innerJoin(assetsTable, eq(assetLifecycleEventsTable.assetId, assetsTable.id))
      .where(conditions.length > 0 ? and(...conditions) : undefined)
      .orderBy(desc(assetLifecycleEventsTable.timestamp))
      .limit(limit);

    const enriched = await enrichLifecycleEvents(events);
    res.json(enriched);
  } catch (err) {
    req.log.error(err);
    res.status(500).json({ error: "Failed to list lifecycle events" });
  }
});

export default router;
