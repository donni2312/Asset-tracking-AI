import { Router } from "express";
import { db, rfidReadersTable, locationsTable, sbusTable } from "@workspace/db";
import { eq } from "drizzle-orm";
import { SimulateRfidReadBody } from "@workspace/api-zod";
import { processRfidScan } from "../services/rfid-scan";
import { sendError } from "../lib/http-error";

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
      .where(sbuId != null && !Number.isNaN(sbuId) ? eq(locationsTable.sbuId, sbuId) : undefined)
      .orderBy(sbusTable.code, rfidReadersTable.name);

    res.json(readers);
  } catch (err) {
    req.log.error(err);
    res.status(500).json({ error: "Failed to list RFID readers" });
  }
});

// POST /rfid/simulate - processed exactly like a hardware read (see services/rfid-scan.ts)
router.post("/rfid/simulate", async (req, res): Promise<void> => {
  try {
    const parsed = SimulateRfidReadBody.safeParse(req.body);
    if (!parsed.success) { res.status(400).json({ error: parsed.error.message }); return; }

    const result = await processRfidScan(parsed.data);
    req.log.info(
      {
        assetId: result.movement.assetId,
        readerId: parsed.data.readerId,
        outcome: result.gateCheck.outcome,
        insights: result.insights.map((i) => i.type),
      },
      "RFID scan processed",
    );
    res.json(result);
  } catch (err) {
    sendError(req, res, err, "RFID simulation failed");
  }
});

export default router;
