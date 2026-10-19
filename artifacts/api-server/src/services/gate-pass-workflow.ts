import {
  db,
  aiInsightsTable,
  assetLifecycleEventsTable,
  assetsTable,
  gatePassesTable,
  locationsTable,
  movementsTable,
  sbusTable,
} from "@workspace/db";
import { and, desc, eq, inArray, max, type SQL } from "drizzle-orm";
import type { Executor } from "../lib/db-types";
import { getConfig } from "../lib/config";
import { HttpError } from "../lib/http-error";
import { validateReturnDate } from "./gate-pass-rules";
import { enrichInsights, raiseInsight, type InsightDto } from "./insights";
import { applyLocationIfLatest, type MovementRow } from "./movement-helpers";

/**
 * Gate pass workflow (plan section 6.3):
 *  - passes are requested manually or raised automatically by a gate reader (see rfid-scan.ts)
 *  - approval releases the asset: if it is waiting at a gate (always the case for passes raised
 *    by a gate reader) the exit is recorded immediately as a `gate_pass` movement; a pass approved
 *    in advance records that movement when a gate reader sees the asset leave
 *  - rejection flags the asset (critical / warning insight + audit note in the asset history)
 */

export type GatePassRow = typeof gatePassesTable.$inferSelect;
export type GatePassStatus = "pending" | "approved" | "rejected";

export interface GatePassDto extends GatePassRow {
  assetName: string;
  rfidTag: string;
  sbuId: number | null;
  sbuName: string | null;
  gateLocationName: string | null;
  exitedAt: Date | null;
}

export async function enrichGatePasses(exec: Executor, passes: GatePassRow[]): Promise<GatePassDto[]> {
  if (passes.length === 0) return [];
  const assetIds = [...new Set(passes.map((p) => p.assetId))];
  const gateIds = [...new Set(passes.map((p) => p.gateLocationId).filter((id): id is number => id != null))];
  const passIds = passes.map((p) => p.id);

  const assets = await exec
    .select({
      id: assetsTable.id,
      name: assetsTable.name,
      rfidTag: assetsTable.rfidTag,
      sbuId: assetsTable.sbuId,
      sbuName: sbusTable.name,
    })
    .from(assetsTable)
    .leftJoin(sbusTable, eq(assetsTable.sbuId, sbusTable.id))
    .where(inArray(assetsTable.id, assetIds));
  const gates =
    gateIds.length > 0
      ? await exec.select({ id: locationsTable.id, name: locationsTable.name }).from(locationsTable).where(inArray(locationsTable.id, gateIds))
      : [];
  const exits = await exec
    .select({ gatePassId: movementsTable.gatePassId, exitedAt: max(movementsTable.timestamp) })
    .from(movementsTable)
    .where(and(inArray(movementsTable.gatePassId, passIds), eq(movementsTable.triggeredBy, "gate_pass")))
    .groupBy(movementsTable.gatePassId);

  const assetById = new Map(assets.map((a) => [a.id, a]));
  const gateName = new Map(gates.map((g) => [g.id, g.name]));
  const exitedAt = new Map(exits.map((e) => [e.gatePassId, e.exitedAt]));

  return passes.map((p) => {
    const asset = assetById.get(p.assetId);
    return {
      ...p,
      assetName: asset?.name ?? "Unknown",
      rfidTag: asset?.rfidTag ?? "",
      sbuId: asset?.sbuId ?? null,
      sbuName: asset?.sbuName ?? null,
      gateLocationName: p.gateLocationId != null ? (gateName.get(p.gateLocationId) ?? null) : null,
      exitedAt: exitedAt.get(p.id) ?? null,
    };
  });
}

export async function listGatePasses(filters: { status?: GatePassStatus; sbuId?: number | null }): Promise<GatePassDto[]> {
  const conditions: SQL[] = [];
  if (filters.status) conditions.push(eq(gatePassesTable.status, filters.status));
  if (filters.sbuId != null) {
    conditions.push(
      inArray(gatePassesTable.assetId, db.select({ id: assetsTable.id }).from(assetsTable).where(eq(assetsTable.sbuId, filters.sbuId))),
    );
  }
  const rows = await db
    .select()
    .from(gatePassesTable)
    .where(conditions.length > 0 ? and(...conditions) : undefined)
    .orderBy(desc(gatePassesTable.createdAt), desc(gatePassesTable.id));
  return enrichGatePasses(db, rows);
}

export async function getGatePass(id: number): Promise<GatePassDto> {
  const [row] = await db.select().from(gatePassesTable).where(eq(gatePassesTable.id, id));
  if (!row) throw new HttpError(404, "Gate pass not found");
  const [dto] = await enrichGatePasses(db, [row]);
  return dto;
}

export interface GatePassRequestInput {
  assetId: number;
  requestedBy: string;
  reason: string;
  destination?: string;
  returnDate?: Date | string;
}

/** Manual request (the "Request gate pass" form). One pending pass per asset at a time. */
export async function createGatePassRequest(input: GatePassRequestInput, now: Date = new Date()): Promise<GatePassDto> {
  const requestedBy = input.requestedBy.trim();
  const reason = input.reason.trim();
  if (!requestedBy) throw new HttpError(400, "requestedBy is required");
  if (!reason) throw new HttpError(400, "reason is required");

  const returnDate =
    input.returnDate == null
      ? null
      : input.returnDate instanceof Date
        ? input.returnDate.toISOString().slice(0, 10)
        : input.returnDate;
  const dateError = validateReturnDate(returnDate, now, getConfig().timezone);
  if (dateError) throw new HttpError(400, dateError);

  return db.transaction(async (tx) => {
    const [asset] = await tx
      .select({ id: assetsTable.id, name: assetsTable.name })
      .from(assetsTable)
      .where(eq(assetsTable.id, input.assetId))
      .for("update");
    if (!asset) throw new HttpError(404, "Asset not found");

    const [pending] = await tx
      .select({ id: gatePassesTable.id })
      .from(gatePassesTable)
      .where(and(eq(gatePassesTable.assetId, asset.id), eq(gatePassesTable.status, "pending")))
      .limit(1);
    if (pending) throw new HttpError(409, `"${asset.name}" already has a pending gate pass (#${pending.id})`);

    const [row] = await tx
      .insert(gatePassesTable)
      .values({
        assetId: asset.id,
        requestedBy,
        reason,
        destination: input.destination?.trim() || null,
        returnDate,
        status: "pending",
        source: "manual",
        createdAt: now,
        updatedAt: now,
      })
      .returning();
    const [dto] = await enrichGatePasses(tx, [row]);
    return dto;
  });
}

export interface GatePassDecisionInput {
  decidedBy: string;
  note?: string;
}

function normaliseDecision(decision: GatePassDecisionInput) {
  const decidedBy = decision.decidedBy.trim();
  if (!decidedBy) throw new HttpError(400, "decidedBy is required — record who took the decision");
  return { decidedBy, note: decision.note?.trim() || null };
}

/** Atomically move a pass out of "pending"; 404 if it does not exist, 409 if it was already decided. */
async function claimPendingPass(
  tx: Executor,
  id: number,
  status: "approved" | "rejected",
  decidedBy: string,
  note: string | null,
  now: Date,
): Promise<GatePassRow> {
  const [updated] = await tx
    .update(gatePassesTable)
    .set({ status, approvedBy: decidedBy, decisionNote: note, decidedAt: now, updatedAt: now })
    .where(and(eq(gatePassesTable.id, id), eq(gatePassesTable.status, "pending")))
    .returning();
  if (updated) return updated;

  const [existing] = await tx.select({ status: gatePassesTable.status }).from(gatePassesTable).where(eq(gatePassesTable.id, id));
  if (!existing) throw new HttpError(404, "Gate pass not found");
  throw new HttpError(409, `Gate pass #${id} has already been ${existing.status}`);
}

async function loadAssetWithLocation(tx: Executor, assetId: number) {
  const [asset] = await tx.select().from(assetsTable).where(eq(assetsTable.id, assetId)).for("update");
  if (!asset) throw new HttpError(404, "Asset not found");
  const [location] =
    asset.currentLocationId != null
      ? await tx
          .select({ id: locationsTable.id, name: locationsTable.name, type: locationsTable.type })
          .from(locationsTable)
          .where(eq(locationsTable.id, asset.currentLocationId))
      : [];
  return { asset, location: location ?? null };
}

async function dismissExitAttemptAlerts(tx: Executor, gatePassId: number): Promise<void> {
  await tx
    .update(aiInsightsTable)
    .set({ dismissed: true })
    .where(
      and(
        eq(aiInsightsTable.gatePassId, gatePassId),
        eq(aiInsightsTable.type, "unauthorized_movement"),
        eq(aiInsightsTable.dismissed, false),
      ),
    );
}

/** Record the asset leaving the premises under `pass` (toLocation = null = off-premises). */
export async function recordGatePassExit(
  exec: Executor,
  args: {
    pass: GatePassRow;
    asset: { id: number; currentLocationId: number | null; assignedTo: string | null };
    gateName: string;
    rfidReaderId: number | null;
    timestamp: Date;
  },
): Promise<MovementRow> {
  const { pass, asset } = args;
  const details = [`Left the premises through ${args.gateName} under gate pass #${pass.id}`];
  if (pass.approvedBy) details.push(`approved by ${pass.approvedBy}`);
  if (pass.destination) details.push(`destination: ${pass.destination}`);
  if (pass.returnDate) details.push(`due back ${pass.returnDate}`);

  const [movement] = await exec
    .insert(movementsTable)
    .values({
      assetId: asset.id,
      fromLocationId: asset.currentLocationId,
      toLocationId: null,
      triggeredBy: "gate_pass",
      rfidReaderId: args.rfidReaderId,
      gatePassId: pass.id,
      movedBy: pass.source === "manual" ? pass.requestedBy : asset.assignedTo,
      notes: details.join(" — "),
      timestamp: args.timestamp,
    })
    .returning();
  await applyLocationIfLatest(exec, asset.id, movement, null);
  return movement;
}

export interface ApproveResult {
  gatePass: GatePassDto;
  /** The gate_pass movement recorded by the approval, or null when the asset is not at a gate yet. */
  exitMovement: MovementRow | null;
}

export async function approveGatePass(id: number, decision: GatePassDecisionInput, now: Date = new Date()): Promise<ApproveResult> {
  const { decidedBy, note } = normaliseDecision(decision);
  return db.transaction(async (tx) => {
    const pass = await claimPendingPass(tx, id, "approved", decidedBy, note, now);
    await dismissExitAttemptAlerts(tx, id);

    const { asset, location } = await loadAssetWithLocation(tx, pass.assetId);
    const exitMovement =
      location?.type === "gate"
        ? await recordGatePassExit(tx, { pass, asset, gateName: location.name, rfidReaderId: null, timestamp: now })
        : null;

    const [dto] = await enrichGatePasses(tx, [pass]);
    return { gatePass: dto, exitMovement };
  });
}

export interface RejectResult {
  gatePass: GatePassDto;
  /** The insight that flags the asset. */
  flag: InsightDto | null;
}

export async function rejectGatePass(id: number, decision: GatePassDecisionInput, now: Date = new Date()): Promise<RejectResult> {
  const { decidedBy, note } = normaliseDecision(decision);
  return db.transaction(async (tx) => {
    const pass = await claimPendingPass(tx, id, "rejected", decidedBy, note, now);
    const { asset, location } = await loadAssetWithLocation(tx, pass.assetId);
    const heldAtGate = location?.type === "gate";

    // The flag supersedes the earlier "exit attempt" warning for this pass.
    await dismissExitAttemptAlerts(tx, id);
    const flag = await raiseInsight(tx, {
      rule: "gate_pass_rejected",
      type: "unauthorized_movement",
      severity: pass.source === "rfid_gate" || heldAtGate ? "critical" : "warning",
      message:
        `Flagged: gate pass #${id} for "${asset.name}" was rejected by ${decidedBy}${note ? ` (${note})` : ""} — ` +
        (heldAtGate && location
          ? `the asset is at "${location.name}" and must not leave the premises`
          : "the asset must not leave the premises"),
      assetId: asset.id,
      gatePassId: id,
    });

    // Durable audit trail in the asset history (insights can be dismissed).
    await tx.insert(assetLifecycleEventsTable).values({
      assetId: asset.id,
      eventType: "note",
      performedBy: decidedBy,
      notes: `Flagged: gate pass #${id} rejected${note ? ` — ${note}` : ""}. The asset must not leave the premises.`,
      timestamp: now,
    });

    const [dto] = await enrichGatePasses(tx, [pass]);
    return { gatePass: dto, flag: flag ? (await enrichInsights(tx, [flag]))[0] : null };
  });
}
