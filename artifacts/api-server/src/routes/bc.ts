import { Router } from "express";
import { sendError } from "../lib/http-error";
import { getBcSyncStatus, triggerBcSync } from "../services/bc-sync";

const router = Router();

// GET /bc/sync-status
router.get("/bc/sync-status", async (req, res) => {
  try {
    res.json(await getBcSyncStatus());
  } catch (err) {
    sendError(req, res, err, "Failed to get BC sync status");
  }
});

// POST /bc/sync - MVP decision: Business Central syncs are manual only (see docs/project-plan.md)
router.post("/bc/sync", async (req, res) => {
  try {
    res.json(await triggerBcSync());
  } catch (err) {
    sendError(req, res, err, "BC sync failed");
  }
});

export default router;
