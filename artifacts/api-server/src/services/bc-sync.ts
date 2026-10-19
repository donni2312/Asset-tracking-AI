import { db, assetsTable, gatePassesTable, movementsTable } from "@workspace/db";
import { and, count, gt, isNotNull } from "drizzle-orm";
import { HttpError } from "../lib/http-error";

/**
 * Business Central integration — MVP decision: MANUAL SYNC ONLY.
 *
 * The BC connection is a mock (no BC tenant / OAuth2 in the MVP), so syncs are
 * triggered from the BC Sync page. Movements and gate-pass decisions recorded
 * since the last sync are reported as `pendingChanges`, which is exactly the
 * set an automatic (Phase 2) sync would push. State is in-memory and resets
 * when the API restarts.
 */

type SyncStatus = "idle" | "syncing" | "success" | "error" | "not_configured";

interface SyncState {
  status: SyncStatus;
  lastSyncAt: Date | null;
  syncedAssets: number;
  errorMessage: string | null;
}

export const SIMULATED_SYNC_MS = 2000;

const initialState = (): SyncState => ({ status: "idle", lastSyncAt: null, syncedAssets: 0, errorMessage: null });
let state: SyncState = initialState();

export interface BcSyncStatusDto {
  connected: boolean;
  mode: "manual" | "automatic";
  status: SyncStatus;
  lastSyncAt: Date | null;
  syncedAssets: number;
  errorMessage: string | null;
  nextSyncAt: Date | null;
  pendingChanges: number;
}

/** Movements plus gate-pass decisions recorded after `since` (everything when never synced). */
export async function countPendingChanges(since: Date | null): Promise<number> {
  const [{ movements }] = await db
    .select({ movements: count() })
    .from(movementsTable)
    .where(since ? gt(movementsTable.timestamp, since) : undefined);
  const [{ decisions }] = await db
    .select({ decisions: count() })
    .from(gatePassesTable)
    .where(
      since
        ? and(isNotNull(gatePassesTable.decidedAt), gt(gatePassesTable.decidedAt, since))
        : isNotNull(gatePassesTable.decidedAt),
    );
  return movements + decisions;
}

export async function getBcSyncStatus(): Promise<BcSyncStatusDto> {
  return {
    connected: true, // mock connection — see the BC Sync page
    mode: "manual",
    status: state.status,
    lastSyncAt: state.lastSyncAt,
    syncedAssets: state.syncedAssets,
    errorMessage: state.errorMessage,
    nextSyncAt: null, // no scheduled syncs in the MVP
    pendingChanges: await countPendingChanges(state.lastSyncAt),
  };
}

/** Start a simulated sync; it completes after `delayMs`. */
export async function triggerBcSync(delayMs: number = SIMULATED_SYNC_MS): Promise<BcSyncStatusDto> {
  if (state.status === "syncing") throw new HttpError(409, "A Business Central sync is already in progress");
  const [{ total }] = await db.select({ total: count() }).from(assetsTable);
  const snapshotAt = new Date();
  state = { ...state, status: "syncing", errorMessage: null };

  const timer = setTimeout(() => {
    // Changes recorded while the sync was running stay pending for the next sync.
    state = { status: "success", lastSyncAt: snapshotAt, syncedAssets: total, errorMessage: null };
  }, delayMs);
  timer.unref?.();

  return getBcSyncStatus();
}

/** Test helper. */
export function resetBcSyncState(): void {
  state = initialState();
}
