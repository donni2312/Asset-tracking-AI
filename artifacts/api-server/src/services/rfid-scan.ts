import { db, assetsTable, gatePassesTable, locationsTable, movementsTable, rfidReadersTable, sbusTable } from "@workspace/db";
import { and, desc, eq } from "drizzle-orm";
import { getConfig } from "../lib/config";
import { HttpError } from "../lib/http-error";
import { evaluateScan } from "./anomaly-rules";
import {
  findValidApprovedPass,
  gateDirection,
  isAuthorizedOutcome,
  resolveGateOutcome,
  type GateDirection,
  type GateOutcome,
} from "./gate-pass-rules";
import { enrichGatePasses, recordGatePassExit, type GatePassDto, type GatePassRow } from "./gate-pass-workflow";
import { enrichInsights, raiseInsight, type InsightDto, type InsightRow } from "./insights";
import { applyLocationIfLatest, type MovementRow } from "./movement-helpers";

/**
 * RFID processing pipeline (plan section 6.2). The simulator calls this today;
 * an Azure IoT Hub consumer would call exactly the same function in production.
 *
 *  1. resolve asset + reader + location
 *  2. gate check (gate readers only): authorised exit, entry, or auto-raise a pending pass
 *  3. record the movement and update the asset's current location
 *  4. evaluate anomaly rules and persist any insights
 *
 * Everything runs in one transaction; the asset row is locked so concurrent
 * scans of the same tag cannot raise duplicate pending passes.
 */

export interface RfidScanInput {
  rfidTag: string;
  readerId: number;
  /** When the reader saw the tag; defaults to now. */
  scannedAt?: Date;
}

export interface ScanMovementDto extends MovementRow {
  assetName: string;
  rfidTag: string;
  fromLocationName: string | null;
  toLocationName: string | null;
  sbuId: number | null;
  sbuName: string | null;
}

export interface RfidScanResult {
  movement: ScanMovementDto;
  gateCheck: {
    isGate: boolean;
    direction: GateDirection;
    outcome: GateOutcome;
    authorized: boolean;
    gatePass?: GatePassDto;
  };
  insights: InsightDto[];
}

/** Scans more than this far in the future are rejected (clock skew allowance). */
const MAX_FUTURE_SKEW_MS = 5 * 60 * 1000;

function scanNotes(readerName: string, outcome: GateOutcome, gatePassId: number | null): string {
  const base = `Auto-detected by RFID reader: ${readerName}`;
  switch (outcome) {
    case "entry":
      return `${base} — asset returning on site`;
    case "pending_created":
      return `${base} — no approved gate pass; pending gate pass #${gatePassId} raised and asset held at the gate`;
    case "pending_existing":
      return `${base} — no approved gate pass; pending gate pass #${gatePassId} still awaiting approval`;
    default:
      return base;
  }
}

export async function processRfidScan(input: RfidScanInput, now: Date = new Date()): Promise<RfidScanResult> {
  const { timezone, workingHours } = getConfig();
  const scannedAt = input.scannedAt ?? now;
  if (scannedAt.getTime() - now.getTime() > MAX_FUTURE_SKEW_MS) {
    throw new HttpError(400, "scannedAt cannot be in the future");
  }

  return db.transaction(async (tx) => {
    // 1. Resolve asset, reader and the reader's location.
    const [asset] = await tx.select().from(assetsTable).where(eq(assetsTable.rfidTag, input.rfidTag)).for("update");
    if (!asset) throw new HttpError(404, `No asset with RFID tag: ${input.rfidTag}`);

    const [reader] = await tx.select().from(rfidReadersTable).where(eq(rfidReadersTable.id, input.readerId));
    if (!reader) throw new HttpError(404, "RFID reader not found");
    if (reader.status !== "active") throw new HttpError(409, `RFID reader "${reader.name}" is inactive`);

    const [location] = await tx
      .select({
        id: locationsTable.id,
        name: locationsTable.name,
        type: locationsTable.type,
        sbuId: locationsTable.sbuId,
        sbuCode: sbusTable.code,
        sbuName: sbusTable.name,
      })
      .from(locationsTable)
      .leftJoin(sbusTable, eq(locationsTable.sbuId, sbusTable.id))
      .where(eq(locationsTable.id, reader.locationId));
    if (!location) throw new HttpError(404, "The reader's location no longer exists");

    const [assetSbu] =
      asset.sbuId != null ? await tx.select({ code: sbusTable.code }).from(sbusTable).where(eq(sbusTable.id, asset.sbuId)) : [];
    const [fromLocation] =
      asset.currentLocationId != null
        ? await tx.select({ name: locationsTable.name }).from(locationsTable).where(eq(locationsTable.id, asset.currentLocationId))
        : [];

    // 2. Gate check.
    const isGate = location.type === "gate";
    const direction = gateDirection(isGate, asset.currentLocationId != null);
    let validPass: GatePassRow | undefined;
    let pendingPass: GatePassRow | undefined;
    if (isGate && direction === "exit") {
      const approved = await tx
        .select()
        .from(gatePassesTable)
        .where(and(eq(gatePassesTable.assetId, asset.id), eq(gatePassesTable.status, "approved")));
      validPass = findValidApprovedPass(approved, scannedAt, timezone);
      if (!validPass) {
        [pendingPass] = await tx
          .select()
          .from(gatePassesTable)
          .where(and(eq(gatePassesTable.assetId, asset.id), eq(gatePassesTable.status, "pending")))
          .orderBy(desc(gatePassesTable.createdAt))
          .limit(1);
      }
    }
    const outcome = resolveGateOutcome({ isGate, direction, hasValidPass: !!validPass, hasPendingPass: !!pendingPass });

    let gatePass: GatePassRow | undefined = validPass ?? pendingPass;
    if (outcome === "pending_created") {
      [gatePass] = await tx
        .insert(gatePassesTable)
        .values({
          assetId: asset.id,
          requestedBy: `System (RFID gate reader: ${reader.name})`,
          reason:
            `Auto-raised: "${asset.name}" was detected leaving through ${location.name} without an approved gate pass.` +
            (asset.assignedTo ? ` Assigned holder: ${asset.assignedTo}.` : ""),
          status: "pending",
          source: "rfid_gate",
          gateLocationId: location.id,
          createdAt: now,
          updatedAt: now,
        })
        .returning();
    }

    // 3. Movement + current location.
    let movement: MovementRow;
    if (outcome === "authorized" && validPass) {
      // Approved exit: the asset leaves the premises under the pass.
      movement = await recordGatePassExit(tx, {
        pass: validPass,
        asset,
        gateName: location.name,
        rfidReaderId: reader.id,
        timestamp: scannedAt,
      });
    } else {
      [movement] = await tx
        .insert(movementsTable)
        .values({
          assetId: asset.id,
          fromLocationId: asset.currentLocationId,
          toLocationId: location.id,
          triggeredBy: "rfid",
          rfidReaderId: reader.id,
          gatePassId: gatePass?.id ?? null,
          notes: scanNotes(reader.name, outcome, gatePass?.id ?? null),
          timestamp: scannedAt,
        })
        .returning();
      await applyLocationIfLatest(tx, asset.id, movement, location.id);
    }

    if (!reader.lastReadAt || reader.lastReadAt.getTime() < scannedAt.getTime()) {
      await tx.update(rfidReadersTable).set({ lastReadAt: scannedAt }).where(eq(rfidReadersTable.id, reader.id));
    }

    // 4. Anomaly rules.
    const drafts = evaluateScan({
      asset: { id: asset.id, name: asset.name, rfidTag: asset.rfidTag, sbuId: asset.sbuId, sbuCode: assetSbu?.code ?? null },
      location: { id: location.id, name: location.name, type: location.type, sbuId: location.sbuId, sbuCode: location.sbuCode },
      readerName: reader.name,
      scannedAt,
      gate: { outcome, gatePassId: gatePass?.id ?? null },
      timeZone: timezone,
      workingHours,
    });
    const raised: InsightRow[] = [];
    for (const draft of drafts) {
      const row = await raiseInsight(tx, draft, "message");
      if (row) raised.push(row);
    }

    const [gatePassDto] = gatePass ? await enrichGatePasses(tx, [gatePass]) : [];
    return {
      movement: {
        ...movement,
        assetName: asset.name,
        rfidTag: asset.rfidTag,
        fromLocationName: fromLocation?.name ?? null,
        toLocationName: movement.toLocationId == null ? null : location.name,
        sbuId: location.sbuId,
        sbuName: location.sbuName,
      },
      gateCheck: {
        isGate,
        direction,
        outcome,
        authorized: isAuthorizedOutcome(outcome),
        ...(gatePassDto ? { gatePass: gatePassDto } : {}),
      },
      insights: await enrichInsights(tx, raised),
    };
  });
}
