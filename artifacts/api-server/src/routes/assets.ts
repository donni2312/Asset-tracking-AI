import { Router } from "express";
import { db } from "@workspace/db";
import { assetsTable, locationsTable, movementsTable, sbusTable, assetLifecycleEventsTable } from "@workspace/db";
import { eq, ilike, and, or, sql, desc } from "drizzle-orm";
import { ListAssetsQueryParams, CreateAssetBody, UpdateAssetBody } from "@workspace/api-zod";
import { hasRole, requireRole } from "../middlewares/auth";
import { HttpError, parseIdParam, sendError } from "../lib/http-error";

/**
 * Fields an operator may change in the course of their work. Everything else is
 * the asset's record (what it is and which unit owns it) and needs an admin.
 */
const OPERATOR_FIELDS = new Set(["status", "assignedTo", "currentLocationId", "notes"]);

const router = Router();

async function assetWithLocation(id: number) {
  const [asset] = await db
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
      sbuName: sbusTable.name,
      sbuCode: sbusTable.code,
      purchaseDate: assetsTable.purchaseDate,
      purchaseValue: assetsTable.purchaseValue,
      notes: assetsTable.notes,
      createdAt: assetsTable.createdAt,
      updatedAt: assetsTable.updatedAt,
    })
    .from(assetsTable)
    .leftJoin(locationsTable, eq(assetsTable.currentLocationId, locationsTable.id))
    .leftJoin(sbusTable, eq(assetsTable.sbuId, sbusTable.id))
    .where(eq(assetsTable.id, id));
  return asset ?? null;
}

// GET /assets
router.get("/assets", async (req, res) => {
  try {
    const parsed = ListAssetsQueryParams.safeParse(req.query);
    const filters = parsed.success ? parsed.data : {};
    const sbuId = req.query.sbuId ? parseInt(String(req.query.sbuId)) : null;

    const conditions = [];
    if (filters.status) conditions.push(eq(assetsTable.status, filters.status));
    if (filters.locationId != null) conditions.push(eq(assetsTable.currentLocationId, filters.locationId));
    if (sbuId != null) conditions.push(eq(assetsTable.sbuId, sbuId));
    if (filters.category) conditions.push(eq(assetsTable.category, filters.category));
    if (filters.q) {
      conditions.push(
        or(
          ilike(assetsTable.name, `%${filters.q}%`),
          ilike(assetsTable.serialNumber, `%${filters.q}%`),
          ilike(assetsTable.rfidTag, `%${filters.q}%`),
        )!
      );
    }

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
        sbuName: sbusTable.name,
        sbuCode: sbusTable.code,
        purchaseDate: assetsTable.purchaseDate,
        purchaseValue: assetsTable.purchaseValue,
        notes: assetsTable.notes,
        createdAt: assetsTable.createdAt,
        updatedAt: assetsTable.updatedAt,
      })
      .from(assetsTable)
      .leftJoin(locationsTable, eq(assetsTable.currentLocationId, locationsTable.id))
      .leftJoin(sbusTable, eq(assetsTable.sbuId, sbusTable.id))
      .where(conditions.length > 0 ? and(...conditions) : undefined)
      .orderBy(assetsTable.name);

    res.json(assets);
  } catch (err) {
    req.log.error(err);
    res.status(500).json({ error: "Failed to list assets" });
  }
});

// POST /assets
router.post("/assets", requireRole("admin"), async (req, res): Promise<void> => {
  try {
    const parsed = CreateAssetBody.safeParse(req.body);
    if (!parsed.success) { res.status(400).json({ error: parsed.error.message }); return; }
    const d = parsed.data;
    const [asset] = await db.insert(assetsTable).values({
      name: d.name,
      category: d.category,
      serialNumber: d.serialNumber,
      rfidTag: d.rfidTag,
      status: d.status ?? "active",
      assignedTo: d.assignedTo,
      currentLocationId: d.currentLocationId,
      sbuId: (d as any).sbuId,
      purchaseDate: d.purchaseDate instanceof Date ? d.purchaseDate.toISOString().split("T")[0] : d.purchaseDate,
      purchaseValue: d.purchaseValue != null ? String(d.purchaseValue) : undefined,
      notes: d.notes,
    }).returning();
    res.status(201).json(await assetWithLocation(asset.id));
  } catch (err) {
    req.log.error(err);
    res.status(500).json({ error: "Failed to create asset" });
  }
});

// GET /assets/:id
router.get("/assets/:id", async (req, res): Promise<void> => {
  try {
    const asset = await assetWithLocation(parseInt(req.params.id));
    if (!asset) { res.status(404).json({ error: "Asset not found" }); return; }
    res.json(asset);
  } catch (err) {
    req.log.error(err);
    res.status(500).json({ error: "Failed to get asset" });
  }
});

// PATCH /assets/:id
router.patch("/assets/:id", requireRole("operator"), async (req, res): Promise<void> => {
  try {
    const id = parseIdParam(req.params.id);
    const parsed = UpdateAssetBody.safeParse(req.body);
    if (!parsed.success) { res.status(400).json({ error: parsed.error.message }); return; }

    const recordFields = Object.keys(parsed.data).filter((f) => !OPERATOR_FIELDS.has(f));
    if (recordFields.length > 0 && !hasRole(req.user, "admin")) {
      throw new HttpError(
        403,
        `Changing ${recordFields.join(", ")} needs the admin role; you are signed in as ${req.user!.role}.`,
      );
    }

    await db.update(assetsTable)
      .set({ ...(parsed.data as any), updatedAt: new Date() })
      .where(eq(assetsTable.id, id));
    const asset = await assetWithLocation(id);
    if (!asset) { res.status(404).json({ error: "Asset not found" }); return; }
    res.json(asset);
  } catch (err) {
    sendError(req, res, err, "Failed to update asset");
  }
});

// DELETE /assets/:id
router.delete("/assets/:id", requireRole("admin"), async (req, res) => {
  try {
    await db.delete(assetsTable).where(eq(assetsTable.id, parseIdParam(req.params.id)));
    res.status(204).send();
  } catch (err) {
    sendError(req, res, err, "Failed to delete asset");
  }
});

// GET /assets/:id/history  — combined movements + lifecycle events sorted chronologically
router.get("/assets/:id/history", async (req, res) => {
  try {
    const id = parseInt(req.params.id);

    // Fetch movements
    const movements = await db
      .select({
        id: movementsTable.id,
        fromLocationId: movementsTable.fromLocationId,
        toLocationId: movementsTable.toLocationId,
        triggeredBy: movementsTable.triggeredBy,
        gatePassId: movementsTable.gatePassId,
        movedBy: movementsTable.movedBy,
        notes: movementsTable.notes,
        timestamp: movementsTable.timestamp,
      })
      .from(movementsTable)
      .where(eq(movementsTable.assetId, id))
      .orderBy(desc(movementsTable.timestamp));

    // Enrich locations
    const locIds = [...new Set([
      ...movements.map(m => m.fromLocationId),
      ...movements.map(m => m.toLocationId),
    ].filter((x): x is number => x != null))];
    const locs = locIds.length > 0
      ? await db.select().from(locationsTable).where(
          sql`${locationsTable.id} = ANY(ARRAY[${sql.join(locIds.map(i => sql`${i}::int`), sql`, `)}])`
        )
      : [];
    const locMap: Record<number, string> = Object.fromEntries(locs.map(l => [l.id, l.name]));

    const movementItems = movements.map(m => ({
      id: `mov-${m.id}`,
      kind: "movement" as const,
      timestamp: m.timestamp,
      fromLocationName: m.fromLocationId != null ? (locMap[m.fromLocationId] ?? null) : null,
      toLocationName: m.toLocationId != null ? (locMap[m.toLocationId] ?? null) : null,
      triggeredBy: m.triggeredBy,
      gatePassId: m.gatePassId,
      movedBy: m.movedBy,
      notes: m.notes,
      // lifecycle fields
      eventType: null,
      performedBy: null,
      fromSbuName: null,
      toSbuName: null,
      assignedTo: null,
      vendor: null,
      ticketRef: null,
      conditionBefore: null,
      conditionAfter: null,
    }));

    // Fetch lifecycle events
    const lifecycleEvents = await db
      .select()
      .from(assetLifecycleEventsTable)
      .where(eq(assetLifecycleEventsTable.assetId, id))
      .orderBy(desc(assetLifecycleEventsTable.timestamp));

    const allSbus = lifecycleEvents.some(e => e.fromSbuId != null || e.toSbuId != null)
      ? await db.select().from(sbusTable)
      : [];
    const sbuMap: Record<number, string> = Object.fromEntries(allSbus.map(s => [s.id, s.name]));

    const lifecycleItems = lifecycleEvents.map(e => ({
      id: `lc-${e.id}`,
      kind: "lifecycle" as const,
      timestamp: e.timestamp,
      // movement fields
      fromLocationName: null,
      toLocationName: null,
      triggeredBy: null,
      gatePassId: null,
      movedBy: null,
      // lifecycle fields
      eventType: e.eventType,
      performedBy: e.performedBy,
      fromSbuName: e.fromSbuId != null ? (sbuMap[e.fromSbuId] ?? null) : null,
      toSbuName: e.toSbuId != null ? (sbuMap[e.toSbuId] ?? null) : null,
      assignedTo: e.assignedTo,
      vendor: e.vendor,
      ticketRef: e.ticketRef,
      conditionBefore: e.conditionBefore,
      conditionAfter: e.conditionAfter,
      notes: e.notes,
    }));

    // Merge and sort
    const combined = [...movementItems, ...lifecycleItems]
      .sort((a, b) => new Date(b.timestamp).getTime() - new Date(a.timestamp).getTime());

    res.json(combined);
  } catch (err) {
    req.log.error(err);
    res.status(500).json({ error: "Failed to get asset history" });
  }
});

export default router;
