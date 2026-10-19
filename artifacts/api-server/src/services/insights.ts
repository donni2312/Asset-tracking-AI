import { db, aiInsightsTable, assetsTable, movementsTable, sbusTable } from "@workspace/db";
import { and, eq, inArray, isNull, max } from "drizzle-orm";
import type { Executor } from "../lib/db-types";
import { getConfig } from "../lib/config";
import { logger } from "../lib/logger";
import { idleAssetRule, missingAssetRule, type InsightDraft } from "./anomaly-rules";

export type InsightRow = typeof aiInsightsTable.$inferSelect;

export interface InsightDto extends InsightRow {
  assetName: string | null;
  sbuCode: string | null;
}

/**
 * Persist an insight unless an identical one is still open.
 *  - "message": skip when an undismissed insight with the same type, asset and message exists
 *    (repeated scans of the same situation do not spam the feed).
 *  - "asset_type": skip when any undismissed insight of that type exists for the asset
 *    (periodic rules such as idle / missing).
 */
export async function raiseInsight(
  exec: Executor,
  draft: InsightDraft,
  dedupe: "message" | "asset_type" = "message",
): Promise<InsightRow | null> {
  const conditions = [
    eq(aiInsightsTable.type, draft.type),
    eq(aiInsightsTable.dismissed, false),
    draft.assetId != null ? eq(aiInsightsTable.assetId, draft.assetId) : isNull(aiInsightsTable.assetId),
  ];
  if (dedupe === "message") conditions.push(eq(aiInsightsTable.message, draft.message));

  const [existing] = await exec.select({ id: aiInsightsTable.id }).from(aiInsightsTable).where(and(...conditions)).limit(1);
  if (existing) return null;

  const [row] = await exec
    .insert(aiInsightsTable)
    .values({
      type: draft.type,
      severity: draft.severity,
      message: draft.message,
      assetId: draft.assetId,
      gatePassId: draft.gatePassId ?? null,
      dismissed: false,
      createdAt: new Date(), // app clock, like movements and gate passes
    })
    .returning();
  return row;
}

/** Attach asset name and the asset's SBU code to insight rows. */
export async function enrichInsights(exec: Executor, rows: InsightRow[]): Promise<InsightDto[]> {
  if (rows.length === 0) return [];
  const assetIds = [...new Set(rows.map((r) => r.assetId).filter((id): id is number => id != null))];
  const assets =
    assetIds.length > 0
      ? await exec
          .select({ id: assetsTable.id, name: assetsTable.name, sbuCode: sbusTable.code })
          .from(assetsTable)
          .leftJoin(sbusTable, eq(assetsTable.sbuId, sbusTable.id))
          .where(inArray(assetsTable.id, assetIds))
      : [];
  const byId = new Map(assets.map((a) => [a.id, a]));
  return rows.map((r) => {
    const asset = r.assetId != null ? byId.get(r.assetId) : undefined;
    return { ...r, assetName: asset?.name ?? null, sbuCode: asset?.sbuCode ?? null };
  });
}

/**
 * Periodic rules (idle and missing assets). Evaluated whenever insights are
 * listed, mirroring the original MVP behaviour, but with a single query for the
 * last movement of every asset instead of one query per asset.
 */
export async function runPeriodicInsightRules(now: Date = new Date()): Promise<number> {
  const { idleDays } = getConfig();
  try {
    const assets = await db
      .select({
        id: assetsTable.id,
        name: assetsTable.name,
        status: assetsTable.status,
        serialNumber: assetsTable.serialNumber,
        createdAt: assetsTable.createdAt,
      })
      .from(assetsTable);
    const lastMoves = await db
      .select({ assetId: movementsTable.assetId, lastMovedAt: max(movementsTable.timestamp) })
      .from(movementsTable)
      .groupBy(movementsTable.assetId);
    const lastMoved = new Map(lastMoves.map((m) => [m.assetId, m.lastMovedAt]));

    let created = 0;
    for (const asset of assets) {
      const drafts = [idleAssetRule(asset, lastMoved.get(asset.id) ?? null, now, idleDays), missingAssetRule(asset)];
      for (const draft of drafts) {
        if (draft && (await raiseInsight(db, draft, "asset_type"))) created++;
      }
    }
    return created;
  } catch (err) {
    // Listing insights must still work if the analysis fails, but the failure is logged.
    logger.error({ err }, "Periodic insight analysis failed");
    return 0;
  }
}
