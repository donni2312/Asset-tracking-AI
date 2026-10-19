import { assetsTable, movementsTable } from "@workspace/db";
import { and, eq, gt, ne } from "drizzle-orm";
import type { Executor } from "../lib/db-types";

export type MovementRow = typeof movementsTable.$inferSelect;

/**
 * Point the asset at `locationId` (null = off-premises) unless a later movement
 * already exists. This keeps the current location correct when a scan is
 * replayed with a back-dated timestamp (e.g. the simulator's "scan time").
 */
export async function applyLocationIfLatest(
  exec: Executor,
  assetId: number,
  movement: Pick<MovementRow, "id" | "timestamp">,
  locationId: number | null,
): Promise<boolean> {
  const [later] = await exec
    .select({ id: movementsTable.id })
    .from(movementsTable)
    .where(
      and(
        eq(movementsTable.assetId, assetId),
        ne(movementsTable.id, movement.id),
        gt(movementsTable.timestamp, movement.timestamp),
      ),
    )
    .limit(1);
  if (later) return false;
  await exec.update(assetsTable).set({ currentLocationId: locationId, updatedAt: new Date() }).where(eq(assetsTable.id, assetId));
  return true;
}
