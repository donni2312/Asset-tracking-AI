import { Router } from "express";
import { db } from "@workspace/db";
import { assetsTable } from "@workspace/db";
import { sql } from "drizzle-orm";

const router = Router();

// Simulated BC sync state (in-memory for MVP)
let syncState = {
  connected: false,
  lastSyncAt: null as string | null,
  status: "not_configured" as "idle" | "syncing" | "success" | "error" | "not_configured",
  syncedAssets: 0,
  errorMessage: null as string | null,
  nextSyncAt: null as string | null,
};

// GET /bc/sync-status
router.get("/bc/sync-status", async (req, res) => {
  try {
    res.json(syncState);
  } catch (err) {
    req.log.error(err);
    res.status(500).json({ error: "Failed to get BC sync status" });
  }
});

// POST /bc/sync
router.post("/bc/sync", async (req, res) => {
  try {
    // Simulate a sync
    syncState.status = "syncing";
    syncState.connected = true;

    // Simulate async delay and result
    const [{ count }] = await db.select({ count: sql<number>`cast(count(*) as int)` }).from(assetsTable);

    // Simulate success after brief delay
    setTimeout(() => {
      syncState.status = "success";
      syncState.lastSyncAt = new Date().toISOString();
      syncState.syncedAssets = count;
      syncState.errorMessage = null;
      syncState.nextSyncAt = new Date(Date.now() + 60 * 60 * 1000).toISOString(); // +1 hour
    }, 2000);

    // Return immediately with syncing status
    const nextSync = new Date(Date.now() + 60 * 60 * 1000).toISOString();
    res.json({
      ...syncState,
      status: "syncing",
      syncedAssets: count,
      nextSyncAt: nextSync,
    });
  } catch (err) {
    req.log.error(err);
    syncState.status = "error";
    syncState.errorMessage = "Sync failed — BC API endpoint unavailable";
    res.status(500).json({ error: "BC sync failed" });
  }
});

export default router;
