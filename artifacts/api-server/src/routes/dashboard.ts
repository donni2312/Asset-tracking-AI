import { Router } from "express";
import { db } from "@workspace/db";
import { assetsTable, movementsTable, gatePassesTable, locationsTable, aiInsightsTable, sbusTable, rfidReadersTable, assetLifecycleEventsTable } from "@workspace/db";
import { eq, sql, desc, and, gte } from "drizzle-orm";

const router = Router();

// GET /dashboard/summary
router.get("/dashboard/summary", async (req, res) => {
  try {
    const [{ total }] = await db.select({ total: sql<number>`cast(count(*) as int)` }).from(assetsTable);
    const [{ active }] = await db.select({ active: sql<number>`cast(count(*) as int)` }).from(assetsTable).where(eq(assetsTable.status, "active"));
    const [{ missing }] = await db.select({ missing: sql<number>`cast(count(*) as int)` }).from(assetsTable).where(eq(assetsTable.status, "missing"));
    const [{ maintenance }] = await db.select({ maintenance: sql<number>`cast(count(*) as int)` }).from(assetsTable).where(eq(assetsTable.status, "maintenance"));
    const [{ pending }] = await db.select({ pending: sql<number>`cast(count(*) as int)` }).from(gatePassesTable).where(eq(gatePassesTable.status, "pending"));
    const [{ locations }] = await db.select({ locations: sql<number>`cast(count(*) as int)` }).from(locationsTable);
    const [{ insights }] = await db.select({ insights: sql<number>`cast(count(*) as int)` }).from(aiInsightsTable).where(eq(aiInsightsTable.dismissed, false));
    const today = new Date(); today.setHours(0, 0, 0, 0);
    const [{ todayMoves }] = await db.select({ todayMoves: sql<number>`cast(count(*) as int)` })
      .from(movementsTable).where(gte(movementsTable.timestamp, today));

    // Per-SBU breakdown
    const allSbus = await db.select().from(sbusTable).orderBy(sbusTable.code);
    const sbuBreakdown = await Promise.all(allSbus.map(async (sbu) => {
      const [{ t }] = await db.select({ t: sql<number>`cast(count(*) as int)` }).from(assetsTable).where(eq(assetsTable.sbuId, sbu.id));
      const [{ a }] = await db.select({ a: sql<number>`cast(count(*) as int)` }).from(assetsTable).where(and(eq(assetsTable.sbuId, sbu.id), eq(assetsTable.status, "active")));
      const [{ m }] = await db.select({ m: sql<number>`cast(count(*) as int)` }).from(assetsTable).where(and(eq(assetsTable.sbuId, sbu.id), eq(assetsTable.status, "missing")));
      const [{ mn }] = await db.select({ mn: sql<number>`cast(count(*) as int)` }).from(assetsTable).where(and(eq(assetsTable.sbuId, sbu.id), eq(assetsTable.status, "maintenance")));
      return { sbuId: sbu.id, sbuCode: sbu.code, sbuName: sbu.name, sbuColor: sbu.color, totalAssets: t, activeAssets: a, missingAssets: m, maintenanceAssets: mn };
    }));

    res.json({ totalAssets: total, activeAssets: active, missingAssets: missing, maintenanceAssets: maintenance, pendingGatePasses: pending, todayMovements: todayMoves, totalLocations: locations, unacknowledgedInsights: insights, sbuBreakdown });
  } catch (err) {
    req.log.error(err);
    res.status(500).json({ error: "Failed to get dashboard summary" });
  }
});

// GET /dashboard/activity
router.get("/dashboard/activity", async (req, res) => {
  try {
    const limit = Math.min(parseInt(String(req.query.limit ?? "20")), 100);
    const sbuId = req.query.sbuId ? parseInt(String(req.query.sbuId)) : null;
    const items: any[] = [];

    const movements = await db
      .select({
        id: movementsTable.id,
        assetId: movementsTable.assetId,
        assetName: assetsTable.name,
        sbuCode: sbusTable.code,
        triggeredBy: movementsTable.triggeredBy,
        timestamp: movementsTable.timestamp,
        toLocName: locationsTable.name,
      })
      .from(movementsTable)
      .innerJoin(assetsTable, eq(movementsTable.assetId, assetsTable.id))
      .leftJoin(sbusTable, eq(assetsTable.sbuId, sbusTable.id))
      .leftJoin(locationsTable, eq(movementsTable.toLocationId, locationsTable.id))
      .where(sbuId != null ? eq(assetsTable.sbuId, sbuId) : undefined)
      .orderBy(desc(movementsTable.timestamp))
      .limit(limit);

    for (const m of movements) {
      const isRfid = m.triggeredBy === "rfid";
      items.push({
        id: `mov-${m.id}`,
        type: isRfid ? "rfid_scan" : "movement",
        description: isRfid
          ? `RFID scan: ${m.assetName} detected at ${m.toLocName ?? "unknown location"}`
          : `${m.assetName} moved manually`,
        assetId: m.assetId,
        assetName: m.assetName,
        sbuCode: m.sbuCode,
        severity: null,
        timestamp: m.timestamp,
      });
    }

    const gatePasses = await db
      .select({
        id: gatePassesTable.id,
        assetId: gatePassesTable.assetId,
        status: gatePassesTable.status,
        requestedBy: gatePassesTable.requestedBy,
        createdAt: gatePassesTable.createdAt,
        updatedAt: gatePassesTable.updatedAt,
      })
      .from(gatePassesTable)
      .orderBy(desc(gatePassesTable.updatedAt))
      .limit(limit);

    const allAssets = await db.select({ id: assetsTable.id, name: assetsTable.name, sbuId: assetsTable.sbuId }).from(assetsTable);
    const allSbusArr = await db.select({ id: sbusTable.id, code: sbusTable.code }).from(sbusTable);
    const assetMap = Object.fromEntries(allAssets.map(a => [a.id, a]));
    const sbuCodeMap = Object.fromEntries(allSbusArr.map(s => [s.id, s.code]));

    for (const gp of gatePasses) {
      const asset = assetMap[gp.assetId];
      const assetName = asset?.name ?? "Unknown Asset";
      const sbuCode = asset?.sbuId != null ? (sbuCodeMap[asset.sbuId] ?? null) : null;
      if (sbuId != null && asset?.sbuId !== sbuId) continue;
      if (gp.status === "pending") {
        items.push({ id: `gp-created-${gp.id}`, type: "gate_pass_created", description: `Gate pass requested for ${assetName} by ${gp.requestedBy}`, assetId: gp.assetId, assetName, sbuCode, severity: "warning", timestamp: gp.createdAt });
      } else {
        items.push({ id: `gp-${gp.status}-${gp.id}`, type: gp.status === "approved" ? "gate_pass_approved" : "gate_pass_rejected", description: `Gate pass for ${assetName} ${gp.status}`, assetId: gp.assetId, assetName, sbuCode, severity: gp.status === "approved" ? "info" : "warning", timestamp: gp.updatedAt });
      }
    }

    // Lifecycle events in feed
    const lcEvents = await db
      .select({ id: assetLifecycleEventsTable.id, assetId: assetLifecycleEventsTable.assetId, assetName: assetsTable.name, sbuCode: sbusTable.code, eventType: assetLifecycleEventsTable.eventType, performedBy: assetLifecycleEventsTable.performedBy, timestamp: assetLifecycleEventsTable.timestamp })
      .from(assetLifecycleEventsTable)
      .innerJoin(assetsTable, eq(assetLifecycleEventsTable.assetId, assetsTable.id))
      .leftJoin(sbusTable, eq(assetsTable.sbuId, sbusTable.id))
      .where(sbuId != null ? eq(assetsTable.sbuId, sbuId) : undefined)
      .orderBy(desc(assetLifecycleEventsTable.timestamp))
      .limit(10);

    const lcLabels: Record<string, string> = { deployment: "deployed to", return_to_it: "returned to IT", repair_sent: "sent for repair", repair_returned: "returned from repair", sbu_transfer: "transferred between SBUs", note: "note added" };
    for (const e of lcEvents) {
      items.push({ id: `lc-${e.id}`, type: "lifecycle_event", description: `${e.assetName} ${lcLabels[e.eventType] ?? e.eventType} by ${e.performedBy}`, assetId: e.assetId, assetName: e.assetName, sbuCode: e.sbuCode, severity: null, timestamp: e.timestamp });
    }

    // AI insights
    const insights = await db.select().from(aiInsightsTable).where(eq(aiInsightsTable.dismissed, false)).orderBy(desc(aiInsightsTable.createdAt)).limit(5);
    for (const ins of insights) {
      const asset = ins.assetId != null ? assetMap[ins.assetId] : null;
      items.push({ id: `ins-${ins.id}`, type: "insight", description: ins.message, assetId: ins.assetId, assetName: asset?.name ?? null, sbuCode: null, severity: ins.severity, timestamp: ins.createdAt });
    }

    items.sort((a, b) => new Date(b.timestamp).getTime() - new Date(a.timestamp).getTime());
    res.json(items.slice(0, limit));
  } catch (err) {
    req.log.error(err);
    res.status(500).json({ error: "Failed to get activity feed" });
  }
});

// GET /dashboard/location-breakdown
router.get("/dashboard/location-breakdown", async (req, res) => {
  try {
    const sbuId = req.query.sbuId ? parseInt(String(req.query.sbuId)) : null;
    const breakdown = await db
      .select({
        locationId: locationsTable.id,
        locationName: locationsTable.name,
        locationType: locationsTable.type,
        sbuCode: sbusTable.code,
        assetCount: sql<number>`cast(count(${assetsTable.id}) as int)`,
      })
      .from(locationsTable)
      .leftJoin(assetsTable, eq(assetsTable.currentLocationId, locationsTable.id))
      .leftJoin(sbusTable, eq(locationsTable.sbuId, sbusTable.id))
      .where(sbuId != null ? eq(locationsTable.sbuId, sbuId) : undefined)
      .groupBy(locationsTable.id, sbusTable.code)
      .orderBy(sql`count(${assetsTable.id}) desc`);
    res.json(breakdown);
  } catch (err) {
    req.log.error(err);
    res.status(500).json({ error: "Failed to get location breakdown" });
  }
});

// GET /dashboard/gate-activity
router.get("/dashboard/gate-activity", async (req, res) => {
  try {
    const limit = Math.min(parseInt(String(req.query.limit ?? "30")), 100);
    const gateMovements = await db
      .select({
        readerId: rfidReadersTable.id,
        readerName: rfidReadersTable.name,
        locationName: locationsTable.name,
        sbuCode: sbusTable.code,
        sbuName: sbusTable.name,
        assetId: assetsTable.id,
        assetName: assetsTable.name,
        rfidTag: assetsTable.rfidTag,
        timestamp: movementsTable.timestamp,
        toLocType: locationsTable.type,
      })
      .from(movementsTable)
      .innerJoin(assetsTable, eq(movementsTable.assetId, assetsTable.id))
      .innerJoin(locationsTable, eq(movementsTable.toLocationId, locationsTable.id))
      .leftJoin(rfidReadersTable, eq(movementsTable.rfidReaderId, rfidReadersTable.id))
      .leftJoin(sbusTable, eq(locationsTable.sbuId, sbusTable.id))
      .where(eq(movementsTable.triggeredBy, "rfid"))
      .orderBy(desc(movementsTable.timestamp))
      .limit(limit);

    res.json(gateMovements.map(m => ({
      readerId: m.readerId ?? 0,
      readerName: m.readerName ?? "Unknown Reader",
      locationName: m.locationName,
      sbuCode: m.sbuCode ?? "TSL",
      sbuName: m.sbuName ?? "TSL Group",
      assetId: m.assetId,
      assetName: m.assetName,
      rfidTag: m.rfidTag,
      direction: m.toLocType === "gate" ? "exit" : "internal",
      timestamp: m.timestamp,
    })));
  } catch (err) {
    req.log.error(err);
    res.status(500).json({ error: "Failed to get gate activity" });
  }
});

export default router;
