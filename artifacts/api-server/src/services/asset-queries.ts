import { assetsTable, locationsTable, movementsTable, sbusTable } from "@workspace/db";
import { desc, eq, inArray, type SQL } from "drizzle-orm";
import type { Executor } from "../lib/db-types";

/** Assets with location and SBU names, in the shape of the API's `Asset` schema. */
export async function listAssetsDetailed(exec: Executor, where?: SQL) {
  const rows = await exec
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
    .where(where)
    .orderBy(assetsTable.name);
  return rows.map((r) => ({ ...r, purchaseValue: r.purchaseValue != null ? Number(r.purchaseValue) : null }));
}

export type AssetDetailed = Awaited<ReturnType<typeof listAssetsDetailed>>[number];

export interface LastMovement {
  assetId: number;
  timestamp: Date;
  triggeredBy: string;
  gatePassId: number | null;
}

/** Most recent movement per asset (Postgres DISTINCT ON). */
export async function lastMovementsFor(exec: Executor, assetIds: number[]): Promise<Map<number, LastMovement>> {
  if (assetIds.length === 0) return new Map();
  const rows = await exec
    .selectDistinctOn([movementsTable.assetId], {
      assetId: movementsTable.assetId,
      timestamp: movementsTable.timestamp,
      triggeredBy: movementsTable.triggeredBy,
      gatePassId: movementsTable.gatePassId,
    })
    .from(movementsTable)
    .where(inArray(movementsTable.assetId, assetIds))
    .orderBy(movementsTable.assetId, desc(movementsTable.timestamp), desc(movementsTable.id));
  return new Map(rows.map((r) => [r.assetId, r]));
}
