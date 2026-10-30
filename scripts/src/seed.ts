/**
 * Demo data for AssetIQ.
 *
 *   pnpm seed           load the demo data into an EMPTY database
 *   pnpm seed --reset   wipe every AssetIQ table first, then load the demo data
 *
 * Reads DATABASE_URL from the environment (or the repository's .env file).
 * The data covers every demo path: the plan's three NL questions, a live
 * pending gate pass raised by a gate reader, approved / rejected passes,
 * an out-of-hours scan, an SBU location mismatch and a missing asset.
 */
import {
  db,
  pool,
  aiInsightsTable,
  assetAcknowledgementsTable,
  assetAttachmentsTable,
  assetAttributeValuesTable,
  assetCategoriesTable,
  assetLifecycleEventsTable,
  assetUpgradesTable,
  assetsTable,
  auditLogTable,
  bcMockNumberSeriesTable,
  bcNumberRequestsTable,
  capexBudgetsTable,
  categoryTemplateFieldsTable,
  departmentsTable,
  employeesTable,
  fleetDetailsTable,
  gatePassItemsTable,
  gatePassesTable,
  goodsReceiptLinesTable,
  goodsReceiptsTable,
  purchaseOrderLinesTable,
  purchaseOrdersTable,
  locationsTable,
  movementsTable,
  rfidReadersTable,
  sbusTable,
  serviceLogsTable,
  serviceSchedulesTable,
  usersTable,
  vehicleDocumentsTable,
  verificationResultsTable,
  verificationScansTable,
  verificationSessionsTable,
} from "@workspace/db";
import { createHash, randomBytes, scrypt as scryptCallback } from "node:crypto";
import { promisify } from "node:util";
import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { count, eq, max, sql } from "drizzle-orm";
import { APPROVERS, ASSETS, DEMO_USERS, INACTIVE_READER, LOCATIONS, SBUS, type AssetSeed } from "./seed-data";
import { SCOPED_USERS, TEMPLATES, UPGRADES, describe } from "./seed-audit";
import { DEPARTMENTS, EMPLOYEES, PHASE2_USERS, sampleInvoicePdf } from "./seed-people";

// Same scrypt format the API verifies (api-server/src/lib/auth.ts).
const scrypt = promisify(scryptCallback) as (p: string, s: Buffer, k: number) => Promise<Buffer>;
async function hashPassword(password: string): Promise<string> {
  const salt = randomBytes(16);
  return `scrypt$${salt.toString("hex")}$${(await scrypt(password, salt, 64)).toString("hex")}`;
}

const TZ = process.env.ORG_TIMEZONE?.trim() || "Africa/Harare";
const DAY = 24 * 60 * 60 * 1000;
const NOW = new Date();

// ── time helpers (organisation timezone) ─────────────────────────────────────
const WEEKDAYS = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];
const MONTHS = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];
const fmt = new Intl.DateTimeFormat("en-US", {
  timeZone: TZ, hourCycle: "h23", year: "numeric", month: "2-digit", day: "2-digit", hour: "2-digit", minute: "2-digit", second: "2-digit", weekday: "short",
});
function parts(date: Date) {
  const p = Object.fromEntries(fmt.formatToParts(date).map((x) => [x.type, x.value]));
  return { y: +p.year, m: +p.month, d: +p.day, h: +p.hour % 24, min: +p.minute, s: +p.second, wd: WEEKDAYS.indexOf(p.weekday) };
}
function localToUtc(y: number, m: number, d: number, h: number, min: number): Date {
  const guess = Date.UTC(y, m - 1, d, h, min);
  const p = parts(new Date(guess));
  const offset = Date.UTC(p.y, p.m - 1, p.d, p.h, p.min, p.s) - guess;
  return new Date(guess - offset);
}
/** hour:minute (local) on the day `daysAgo` days before today — never later than 5 minutes ago. */
function at(daysAgo: number, hour: number, minute = 0): Date {
  const p = parts(new Date(NOW.getTime() - daysAgo * DAY));
  const t = localToUtc(p.y, p.m, p.d, hour, minute);
  return new Date(Math.min(t.getTime(), NOW.getTime() - 5 * 60 * 1000));
}
const minutesAgo = (n: number) => new Date(NOW.getTime() - n * 60 * 1000);
const dateKey = (date: Date) => {
  const p = parts(date);
  return `${p.y}-${String(p.m).padStart(2, "0")}-${String(p.d).padStart(2, "0")}`;
};
const describeLocal = (date: Date) => {
  const p = parts(date);
  return `${WEEKDAYS[p.wd]} ${p.d} ${MONTHS[p.m - 1]} ${String(p.h).padStart(2, "0")}:${String(p.min).padStart(2, "0")}`;
};
function startOfWeek(): Date {
  const p = parts(NOW);
  const monday = new Date(localToUtc(p.y, p.m, p.d, 12, 0).getTime() - ((p.wd + 6) % 7) * DAY);
  const q = parts(monday);
  return localToUtc(q.y, q.m, q.d, 0, 0);
}

/** Small deterministic PRNG so every seed run produces the same shape of data. */
function prng(seed: number) {
  return () => {
    seed = (seed + 0x6d2b79f5) | 0;
    let t = Math.imul(seed ^ (seed >>> 15), 1 | seed);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}
const rand = prng(20251130);

// ── main ─────────────────────────────────────────────────────────────────────
async function main() {
  const reset = process.argv.includes("--reset");
  const [{ assets }] = await db.select({ assets: count() }).from(assetsTable);
  const [{ sbus }] = await db.select({ sbus: count() }).from(sbusTable);
  if ((assets > 0 || sbus > 0) && !reset) {
    console.error(`The database already contains data (${sbus} SBUs, ${assets} assets).`);
    console.error('Run "pnpm seed --reset" to wipe it and load the demo data instead.');
    process.exitCode = 1;
    return;
  }

  await db.transaction(async (tx) => {
    if (reset) {
      await tx.execute(
        // Category templates and Group settings are reference data owned by the migrations: they are kept.
        // TRUNCATE is not stopped by the append-only / asset-number triggers (they guard UPDATE and DELETE).
        sql`TRUNCATE ai_insights, asset_lifecycle_events, movements, gate_passes, asset_attribute_values, asset_upgrades,
            depreciation_entries, depreciation_locks, bc_number_requests, bc_mock_number_series, audit_log, access_log,
            approval_steps, approval_requests, delegations, notifications, outbound_emails, asset_repairs, asset_disposals,
            capex_budgets, asset_revaluations, service_logs, service_schedules, vehicle_documents, fleet_details,
            gate_pass_items, gate_actions, verification_results, verification_scans, verification_sessions,
            exit_clearance_items, exit_clearances, asset_acknowledgements, equipment_requests, goods_receipt_lines, goods_receipts,
            purchase_order_lines, purchase_orders,
            asset_attachments, assets, users, employees, departments, rfid_readers, locations, sbus RESTART IDENTITY CASCADE`,
      );
    }

    // Sign-ins: one account per role (see DEMO_USERS for the passwords).
    await tx.insert(usersTable).values(
      await Promise.all(
        DEMO_USERS.map(async (u) => ({
          username: u.username,
          name: u.name,
          role: u.role,
          active: u.active,
          passwordHash: await hashPassword(u.password),
        })),
      ),
    );

    // SBUs, locations, readers
    const sbuRows = await tx.insert(sbusTable).values(SBUS).returning();
    const sbuId = (code: string) => sbuRows.find((s) => s.code === code)!.id;

    // Phase 1 sign-ins for the scoped roles (password = username + "-2026").
    await tx.insert(usersTable).values(
      await Promise.all(
        SCOPED_USERS.map(async (u) => ({
          username: u.username,
          name: u.name,
          role: u.role,
          sbuId: u.sbu ? sbuId(u.sbu) : null,
          accessExpiresAt: u.role === "auditor" ? new Date(NOW.getTime() + 60 * DAY) : null,
          passwordHash: await hashPassword(`${u.username}-2026`),
        })),
      ),
    );

    // Category templates (inserted by migration 0002).
    const categories = await tx.select().from(assetCategoriesTable);
    const fields = await tx.select().from(categoryTemplateFieldsTable);
    const categoryOf = (code: string) => categories.find((c) => c.code === code)!;
    const fieldsOf = (categoryId: number) => fields.filter((f) => f.categoryId === categoryId);
    // Mock Business Central number series: {SBU}-{IT | category code}-{year}-{00001}
    const series = new Map<string, number>();
    const nextNumber = (prefix: string) => {
      const n = (series.get(prefix) ?? 0) + 1;
      series.set(prefix, n);
      return `${prefix}-${String(n).padStart(5, "0")}`;
    };
    const identifier = (code: string, i: number) =>
      code === "MV" ? `AHTFR22G40${String(6012345 + i * 7).padStart(7, "0")}`
      : code === "MOB" ? `35${String(1234567890123 + i * 104729).padStart(13, "0")}`
      : `SN${String(10231 + i * 37).padStart(6, "0")}${String.fromCharCode(65 + (i % 26))}`;

    const locRows = await tx
      .insert(locationsTable)
      .values(
        LOCATIONS.map((l) => ({
          name: l.name, type: l.type, building: l.building, floor: l.floor ?? null, description: l.description ?? null, sbuId: sbuId(l.sbu),
        })),
      )
      .returning();
    const loc = (key: string) => {
      const name = LOCATIONS.find((l) => l.key === key)!.name;
      return locRows.find((r) => r.name === name)!;
    };

    const readerRows = await tx
      .insert(rfidReadersTable)
      .values([
        ...LOCATIONS.filter((l) => l.reader).map((l) => ({ name: l.reader!, locationId: loc(l.key).id, status: "active" })),
        { name: INACTIVE_READER.name, locationId: loc(INACTIVE_READER.location).id, status: "inactive" },
      ])
      .returning();
    const readerAt = (key: string) => readerRows.find((r) => r.locationId === loc(key).id && r.status === "active") ?? null;

    // Assets (final locations are adjusted by the scenarios below)
    const assetRows = await tx
      .insert(assetsTable)
      .values(
        ASSETS.map((a, i) => {
          const t = TEMPLATES[a.key];
          const category = categoryOf(t.code);
          const values: Record<string, string> = { ...t.attrs };
          const serial = t.noSerialReason ? null : identifier(t.code, i);
          if (serial && category.identifierFieldKey) values[category.identifierFieldKey] = serial;
          const purchased = at(a.registeredDaysAgo + 3, 9);
          const capitalised = !t.notCapitalised;
          const rate = t.currency?.rate ?? 1;
          const original = t.currency ? Math.round((a.value / rate) * 100) / 100 : a.value;
          const usd = Math.round(original * rate * 100) / 100;
          return {
            name: describe(category.descriptionPattern, t.sub, category.name, fieldsOf(category.id), values),
            category: t.sub,
            categoryId: category.id,
            subCategory: t.sub,
            serialNumber: serial,
            noSerialReason: t.noSerialReason ?? null,
            noSerialStatus: t.noSerialReason ? (t.noSerialApproved ? "approved" : "pending") : null,
            noSerialDecidedBy: t.noSerialApproved ? "SBU Finance" : null,
            macAddress: t.attrs.mac_address ?? null,
            rfidTag: `RFID-${a.sbu}-${String(i + 1).padStart(4, "0")}`,
            assetNumber: nextNumber(`${a.sbu}-${category.isIt ? "IT" : category.code}-${parts(purchased).y}`),
            status: a.status,
            condition: a.status === "maintenance" ? "poor" : a.registeredDaysAgo > 700 ? "fair" : "good",
            assignedTo: a.assignedTo ?? (category.isIt ? `${a.sbu} IT store` : `${a.sbu} Admin store`),
            currentLocationId: a.location ? loc(a.location).id : null,
            sbuId: sbuId(a.sbu),
            originalCost: t.missing?.includes("cost") ? null : original.toFixed(2),
            currency: t.currency?.code ?? "USD",
            exchangeRate: String(rate),
            costUsd: capitalised ? usd.toFixed(2) : null,
            capitalisedAt: capitalised ? at(a.registeredDaysAgo, 15) : null,
            capitalisedBy: capitalised ? "SBU Finance" : null,
            supplier: category.isIt ? "Axis Solutions (Pvt) Ltd" : "Harare Commercial Supplies",
            invoiceNo: t.missing?.includes("invoice") ? null : `INV-${20000 + i * 13}`,
            poNo: t.missing?.includes("po") ? null : `PO-${a.sbu}-${parts(purchased).y}-${String(100 + i).padStart(4, "0")}`,
            purchaseDate: dateKey(purchased),
            inUseDate: dateKey(at(a.registeredDaysAgo, 9)),
            warrantyEnds: category.isIt ? dateKey(at(a.registeredDaysAgo - 3 * 365, 9)) : null,
            disposalDate: a.status === "retired" ? dateKey(at(a.lastMovedDaysAgo, 9)) : null,
            purchaseValue: usd.toFixed(2),
            notes: a.notes ?? null,
            createdAt: at(a.registeredDaysAgo, 9),
            updatedAt: at(Math.min(a.lastMovedDaysAgo, a.registeredDaysAgo), 9),
          };
        }),
      )
      .returning();

    // Template values, Business Central bookkeeping and the "registered / capitalised" audit entries.
    await tx.insert(assetAttributeValuesTable).values(
      assetRows.flatMap((row, i) => {
        const t = TEMPLATES[ASSETS[i].key];
        return fieldsOf(row.categoryId)
          .filter((f) => t.attrs[f.key] !== undefined && f.key !== "mac_address")
          .map((f) => ({
            assetId: row.id, fieldId: f.id, valueText: t.attrs[f.key],
            valueNumber: f.type === "integer" || f.type === "number" ? t.attrs[f.key] : null,
          }));
      }),
    );
    await tx.insert(bcNumberRequestsTable).values(assetRows.map((r) => ({ assetId: r.id, status: "done", attempts: 1, assetNumber: r.assetNumber, createdAt: r.createdAt, updatedAt: r.createdAt })));
    await tx.insert(bcMockNumberSeriesTable).values([...series.entries()].map(([prefix, lastNumber]) => ({ prefix, lastNumber })));
    await tx.insert(auditLogTable).values(
      assetRows.flatMap((r) => [
        { entity: "asset", entityId: r.id, assetId: r.id, action: "created", field: "description", newValue: r.name, userName: "Seed (demo data)", at: r.createdAt },
        { entity: "asset", entityId: r.id, assetId: r.id, action: "asset_number_issued", field: "assetNumber", newValue: r.assetNumber, reason: "Issued by Business Central", userName: "system", at: r.createdAt },
        ...(r.capitalisedAt ? [{ entity: "asset", entityId: r.id, assetId: r.id, action: "capitalised", field: "costUsd", newValue: r.costUsd, userName: "SBU Finance", at: r.capitalisedAt }] : []),
      ]),
    );

    // People (Phase 2): departments per SBU, employees, the holders linked to their records, and the new sign-ins.
    const departmentRows = await tx
      .insert(departmentsTable)
      .values(sbuRows.flatMap((s) => DEPARTMENTS.map((name) => ({ sbuId: s.id, name }))))
      .returning();
    const departmentId = (sbu: string, name: string) => departmentRows.find((d) => d.sbuId === sbuId(sbu) && d.name === name)!.id;
    const employeeRows = await tx
      .insert(employeesTable)
      .values(EMPLOYEES.map((e) => ({
        sbuId: sbuId(e.sbu), departmentId: departmentId(e.sbu, e.department), employeeNo: e.no, name: e.name, jobRole: e.jobRole,
        email: `${e.name.toLowerCase().replace(/[^a-z]+/g, ".")}@tsl.co.zw`,
      })))
      .returning();
    const employeeNo = (no: string) => employeeRows.find((e) => e.employeeNo === no)!;
    for (const e of EMPLOYEES.filter((x) => x.manager)) {
      await tx.update(employeesTable).set({ lineManagerId: employeeNo(e.manager!).id }).where(eq(employeesTable.id, employeeNo(e.no).id));
    }
    for (const [i, row] of assetRows.entries()) {
      const holder = employeeRows.find((e) => e.name === row.assignedTo && e.sbuId === sbuId(ASSETS[i].sbu));
      if (holder) await tx.update(assetsTable).set({ custodianEmployeeId: holder.id, departmentId: holder.departmentId }).where(eq(assetsTable.id, row.id));
    }
    await tx.insert(usersTable).values(
      await Promise.all(
        PHASE2_USERS.map(async (u) => ({
          username: u.username,
          name: u.name,
          role: u.role,
          sbuId: u.sbu ? sbuId(u.sbu) : null,
          employeeId: u.employee ? employeeNo(u.employee).id : null,
          passwordHash: await hashPassword(`${u.username}-2026`),
        })),
      ),
    );

    // Upgrades (AU-07): capitalised, expensed and two waiting for SBU Finance.
    const periodMonthsAgo = (months: number) => {
      const p = parts(NOW);
      const d = new Date(Date.UTC(p.y, p.m - 1 - months, 1));
      return `${d.getUTCFullYear()}-${String(d.getUTCMonth() + 1).padStart(2, "0")}`;
    };
    await tx.insert(assetUpgradesTable).values(
      UPGRADES.map((u) => {
        const row = assetRows[ASSETS.findIndex((a) => a.key === u.key)];
        const decided = u.status !== "pending";
        return {
          assetId: row.id, upgradeDate: dateKey(at(u.daysAgo + 1, 10)), description: u.description, beforeText: u.before, afterText: u.after,
          fieldChanges: u.changes, cost: u.cost, supplier: "Axis Solutions (Pvt) Ltd", invoiceNo: `INV-U${row.id}${u.daysAgo}`,
          currency: "currency" in u ? u.currency : "USD", invoiceAmount: "invoiceAmount" in u ? u.invoiceAmount : u.cost,
          exchangeRate: "exchangeRate" in u ? u.exchangeRate : "1",
          loggedBy: u.by, loggedByRole: u.role, status: u.status, decidedBy: decided ? "SBU Finance" : null, decidedAt: decided ? at(u.daysAgo, 15) : null,
          decisionComment: u.status === "expensed" ? "Ordinary repair" : decided ? "Adds capacity / extends life" : null,
          capitalisedPeriod: u.status === "capitalised" ? periodMonthsAgo(u.monthsAgo) : null,
          createdAt: at(u.daysAgo, 10),
        };
      }),
    );
    const asset = (key: string) => assetRows[ASSETS.findIndex((a) => a.key === key)];

    // Phase 4b: a TSAS purchase order with a short and damaged delivery (ST-01), and John Doe's laptop waiting for his
    // acknowledgement (HR-03).
    const [po] = await tx.insert(purchaseOrdersTable).values({
      sbuId: sbuId("TSAS"), poNo: "PO-TSAS-2026-0300", supplier: "Axis Solutions (Pvt) Ltd", status: "partly_received", createdBy: "Chipo Mlambo",
    }).returning();
    const [poLine] = await tx.insert(purchaseOrderLinesTable).values([
      { purchaseOrderId: po.id, categoryId: categoryOf("COMP").id, description: "Dell Latitude 5450 laptop", quantity: 3, unitCost: "1250.00", receivedQuantity: 2 },
      { purchaseOrderId: po.id, categoryId: categoryOf("PER").id, description: "Epson EB-X51 projector", quantity: 1, unitCost: "640.00", receivedQuantity: 0 },
    ]).returning();
    const [receipt] = await tx.insert(goodsReceiptsTable).values({
      purchaseOrderId: po.id, receivedBy: "Tinashe Mukanya", flagged: true, flagReason: "1 damaged: Dell Latitude 5450 laptop; short 1: Dell Latitude 5450 laptop; short 1: Epson EB-X51 projector",
    }).returning();
    await tx.insert(goodsReceiptLinesTable).values({
      receiptId: receipt.id, purchaseOrderLineId: poLine.id, quantityReceived: 2, quantityDamaged: 1, serials: ["5CG4100AB1", "5CG4100AB2"], condition: "new",
    });
    await tx.insert(assetAcknowledgementsTable).values({
      assetId: asset("dell-5440").id, employeeId: employeeNo("TSAS-0002").id, token: randomBytes(16).toString("hex"), issuedBy: "Farai Ncube",
    });

    // The supplier invoice of the first TSAS laptop, as SBU Finance would attach it (AU-04).
    const invoiced = asset("dell-5440");
    const invoicePdf = sampleInvoicePdf(invoiced.invoiceNo ?? "INV", invoiced.supplier ?? "Supplier", invoiced.costUsd ?? "0.00");
    await tx.insert(assetAttachmentsTable).values({
      assetId: invoiced.id, kind: "invoice", fileName: `${invoiced.invoiceNo}.pdf`, contentType: "application/pdf", sizeBytes: invoicePdf.length,
      sha256: createHash("sha256").update(invoicePdf).digest("hex"), content: invoicePdf, uploadedBy: "Chipo Mlambo", uploadedByRole: "sbu_finance",
      uploadedAt: invoiced.capitalisedAt ?? invoiced.createdAt,
    });
    const seedOf = (key: string) => ASSETS.find((a) => a.key === key)!;

    type MovementInsert = typeof movementsTable.$inferInsert;
    const movements: MovementInsert[] = [];

    /** A move into `toKey`: an RFID read when a reader is installed there, otherwise a manual move. */
    function move(a: AssetSeed, fromKey: string | null, toKey: string, when: Date, extra: Partial<MovementInsert> = {}): void {
      const reader = readerAt(toKey);
      movements.push({
        assetId: asset(a.key).id,
        fromLocationId: fromKey ? loc(fromKey).id : null,
        toLocationId: loc(toKey).id,
        triggeredBy: reader ? "rfid" : "manual",
        rfidReaderId: reader?.id ?? null,
        movedBy: reader ? null : (a.assignedTo ?? "Facilities team"),
        notes: reader ? `Auto-detected by RFID reader: ${reader.name}` : "Relocated",
        timestamp: when,
        ...extra,
      });
    }

    // Generic history: each asset alternates between its usual locations and ends at its
    // current one exactly `lastMovedDaysAgo` days ago (assets with no other stops move once).
    for (const a of ASSETS) {
      const finalKey = a.location ?? "prop-conference";
      const others = a.visits ?? [];
      const moves = others.length === 0 ? 1 : a.moves;
      const stops: Array<{ key: string; when: Date }> = [];
      let daysAgo = a.lastMovedDaysAgo;
      for (let s = 0; s < moves; s++) {
        const key = s % 2 === 0 ? finalKey : others[Math.floor(s / 2) % others.length];
        stops.unshift({ key, when: at(daysAgo, 8 + Math.floor(rand() * 8), Math.floor(rand() * 60)) });
        daysAgo += 2 + Math.floor(rand() * 6);
      }
      let previous: string | null = null;
      for (const stop of stops) {
        move(a, previous, stop.key, stop.when);
        previous = stop.key;
      }
    }

    // ── Gate pass scenarios ──────────────────────────────────────────────────
    const weekStart = startOfWeek();
    const span = NOW.getTime() - weekStart.getTime();
    const inWeek = (fraction: number) => new Date(weekStart.getTime() + span * fraction);
    const insertPass = async (values: typeof gatePassesTable.$inferInsert) => (await tx.insert(gatePassesTable).values(values).returning())[0];
    const exitNote = (passId: number, gate: string, approver: string, destination: string, due: string) =>
      `Left the premises through ${gate} under gate pass #${passId} — approved by ${approver} — destination: ${destination} — due back ${due}`;

    // 1. Approved THIS week and the laptop is still off-premises.
    const hp = seedOf("hp-840");
    const gp1Decided = inWeek(0.35);
    const gp1Due = dateKey(new Date(NOW.getTime() + 3 * DAY));
    const gp1 = await insertPass({
      assetId: asset("hp-840").id, requestedBy: "Tendai Moyo", reason: "Quarter-end audit work at the client's office",
      destination: "Client site – Borrowdale", returnDate: gp1Due, status: "approved", approvedBy: APPROVERS.tsas,
      decisionNote: "Return by the due date", source: "manual", decidedAt: gp1Decided,
      createdAt: new Date(gp1Decided.getTime() - 3 * 60 * 60 * 1000), updatedAt: gp1Decided,
    });
    movements.push({
      assetId: asset("hp-840").id, fromLocationId: loc(hp.location!).id, toLocationId: null, triggeredBy: "gate_pass",
      rfidReaderId: readerAt("tsas-gate")!.id, gatePassId: gp1.id, movedBy: "Tendai Moyo",
      notes: exitNote(gp1.id, "TSAS Main Gate", APPROVERS.tsas, "Client site – Borrowdale", gp1Due), timestamp: inWeek(0.45),
    });

    // 2. Approved THIS week, left and has already come back.
    const dell7440 = seedOf("dell-7440");
    const gp2Decided = inWeek(0.6);
    const gp2Due = dateKey(NOW);
    const gp2 = await insertPass({
      assetId: asset("dell-7440").id, requestedBy: "Tatenda Mhlanga", reason: "Grower registration drive", destination: "Karoi satellite floor",
      returnDate: gp2Due, status: "approved", approvedBy: APPROVERS.tsas, source: "manual", decidedAt: gp2Decided,
      createdAt: new Date(gp2Decided.getTime() - 2 * 60 * 60 * 1000), updatedAt: gp2Decided,
    });
    movements.push({
      assetId: asset("dell-7440").id, fromLocationId: loc(dell7440.location!).id, toLocationId: null, triggeredBy: "gate_pass",
      rfidReaderId: readerAt("tsf-gate")!.id, gatePassId: gp2.id, movedBy: "Tatenda Mhlanga",
      notes: exitNote(gp2.id, "TSF Main Gate", APPROVERS.tsas, "Karoi satellite floor", gp2Due), timestamp: inWeek(0.65),
    });
    move(dell7440, null, "tsf-gate", inWeek(0.8), { notes: "Auto-detected by RFID reader: TSF Main Gate Reader — asset returning on site" });
    move(dell7440, "tsf-gate", "tsf-office", inWeek(0.82), { movedBy: "Tatenda Mhlanga" });

    // 3. Approved LAST week (excluded from "approved this week").
    const dell3540 = seedOf("dell-3540");
    const gp3Decided = new Date(weekStart.getTime() - 4 * DAY + 10 * 60 * 60 * 1000);
    const gp3Due = dateKey(new Date(weekStart.getTime() - 2 * DAY));
    const gp3 = await insertPass({
      assetId: asset("dell-3540").id, requestedBy: "Blessing Mutasa", reason: "Customer stock count", destination: "Msasa distribution centre",
      returnDate: gp3Due, status: "approved", approvedBy: APPROVERS.bak, source: "manual", decidedAt: gp3Decided,
      createdAt: new Date(gp3Decided.getTime() - 60 * 60 * 1000), updatedAt: gp3Decided,
    });
    movements.push({
      assetId: asset("dell-3540").id, fromLocationId: loc(dell3540.location!).id, toLocationId: null, triggeredBy: "gate_pass",
      rfidReaderId: readerAt("bak-gate")!.id, gatePassId: gp3.id, movedBy: "Blessing Mutasa",
      notes: exitNote(gp3.id, "BAK Depot Gate", APPROVERS.bak, "Msasa distribution centre", gp3Due),
      timestamp: new Date(gp3Decided.getTime() + 30 * 60 * 1000),
    });
    move(dell3540, null, "bak-gate", new Date(weekStart.getTime() - 2 * DAY + 9 * 60 * 60 * 1000), {
      notes: "Auto-detected by RFID reader: BAK Depot Gate Reader — asset returning on site",
    });
    move(dell3540, "bak-gate", "bak-office", new Date(weekStart.getTime() - 2 * DAY + 9.5 * 60 * 60 * 1000), { movedBy: "Blessing Mutasa" });

    // 4. Raised by a gate reader and REJECTED — the laptop is flagged and held at the gate.
    const probook = seedOf("probook");
    const gp4Raised = minutesAgo(190);
    const gp4 = await insertPass({
      assetId: asset("probook").id, requestedBy: "System (RFID gate reader: BAK Depot Gate Reader)",
      reason: `Auto-raised: "${probook.name}" was detected leaving through BAK Depot Gate without an approved gate pass.`,
      status: "rejected", approvedBy: APPROVERS.bak, decisionNote: "Unassigned laptop — no business reason to leave site",
      source: "rfid_gate", gateLocationId: loc("bak-gate").id, decidedAt: minutesAgo(165), createdAt: gp4Raised, updatedAt: minutesAgo(165),
    });
    move(probook, probook.location, "bak-gate", gp4Raised, {
      gatePassId: gp4.id,
      notes: `Auto-detected by RFID reader: BAK Depot Gate Reader — no approved gate pass; pending gate pass #${gp4.id} raised and asset held at the gate`,
    });

    // 5. Manual request PENDING approval.
    const epson = seedOf("epson-x51");
    const gp5 = await insertPass({
      assetId: asset("epson-x51").id, requestedBy: "Farai Ncube", reason: "Grower roadshow presentation",
      destination: "Marondera Grower Centre", returnDate: dateKey(new Date(NOW.getTime() + 5 * DAY)), status: "pending",
      source: "manual", createdAt: at(1, 11, 20), updatedAt: at(1, 11, 20),
    });

    // 6. Raised by a gate reader a moment ago and PENDING — approve it live in the demo.
    const x1 = seedOf("x1-carbon");
    const gp6Raised = minutesAgo(50);
    const gp6 = await insertPass({
      assetId: asset("x1-carbon").id, requestedBy: "System (RFID gate reader: Head Office Gate Reader)",
      reason: `Auto-raised: "${x1.name}" was detected leaving through TSL Head Office Gate without an approved gate pass. Assigned holder: ${x1.assignedTo}.`,
      status: "pending", source: "rfid_gate", gateLocationId: loc("prop-gate").id, createdAt: gp6Raised, updatedAt: gp6Raised,
    });
    move(x1, x1.location, "prop-gate", gp6Raised, {
      gatePassId: gp6.id,
      notes: `Auto-detected by RFID reader: Head Office Gate Reader — no approved gate pass; pending gate pass #${gp6.id} raised and asset held at the gate`,
    });

    // ── Anomaly scenarios ────────────────────────────────────────────────────
    // Out-of-hours scan: last Saturday 02:14 at BAK Warehouse A.
    const tc52 = seedOf("tc52");
    const today = parts(NOW);
    let saturdayDaysAgo = (today.wd - 6 + 7) % 7;
    if (saturdayDaysAgo === 0 && (today.h < 2 || (today.h === 2 && today.min < 30))) saturdayDaysAgo = 7;
    const saturdayScan = at(saturdayDaysAgo, 2, 14);
    move(tc52, "bak-office", "bak-warehouse", saturdayScan);

    // SBU location mismatch: an Agricura phone read by the TSAS IT reader yesterday.
    const a54 = seedOf("galaxy-a54");
    const mismatchAt = at(1, 14, 5);
    move(a54, a54.location, "tsas-it", mismatchAt);

    await tx.insert(movementsTable).values(movements);

    // Final locations changed by the scenarios.
    await tx.update(assetsTable).set({ currentLocationId: null }).where(eq(assetsTable.id, asset("hp-840").id));
    await tx.update(assetsTable).set({ currentLocationId: loc("bak-gate").id }).where(eq(assetsTable.id, asset("probook").id));
    await tx.update(assetsTable).set({ currentLocationId: loc("prop-gate").id }).where(eq(assetsTable.id, asset("x1-carbon").id));
    await tx.update(assetsTable).set({ currentLocationId: loc("tsas-it").id }).where(eq(assetsTable.id, asset("galaxy-a54").id));

    // Readers' last read time.
    const lastReads = await tx
      .select({ readerId: movementsTable.rfidReaderId, last: max(movementsTable.timestamp) })
      .from(movementsTable)
      .groupBy(movementsTable.rfidReaderId);
    for (const r of lastReads) {
      if (r.readerId != null && r.last) await tx.update(rfidReadersTable).set({ lastReadAt: r.last }).where(eq(rfidReadersTable.id, r.readerId));
    }

    // ── Insights raised by the scenarios (same wording as the live anomaly engine) ──
    await tx.insert(aiInsightsTable).values([
      {
        type: "unauthorized_movement", severity: "warning", dismissed: true, assetId: asset("probook").id, gatePassId: gp4.id, createdAt: gp4Raised,
        message: `Unauthorised exit attempt: "${probook.name}" (${asset("probook").rfidTag}) detected at gate "BAK Depot Gate" [BAK] without an approved gate pass — pending gate pass #${gp4.id} raised for approval`,
      },
      {
        type: "unauthorized_movement", severity: "critical", dismissed: false, assetId: asset("probook").id, gatePassId: gp4.id, createdAt: minutesAgo(165),
        message: `Flagged: gate pass #${gp4.id} for "${probook.name}" was rejected by ${APPROVERS.bak} (Unassigned laptop — no business reason to leave site) — the asset is at "BAK Depot Gate" and must not leave the premises`,
      },
      {
        type: "unauthorized_movement", severity: "warning", dismissed: false, assetId: asset("x1-carbon").id, gatePassId: gp6.id, createdAt: gp6Raised,
        message: `Unauthorised exit attempt: "${x1.name}" (${asset("x1-carbon").rfidTag}) detected at gate "TSL Head Office Gate" [PROP] without an approved gate pass — pending gate pass #${gp6.id} raised for approval`,
      },
      {
        type: "unusual_pattern", severity: "info", dismissed: false, assetId: asset("tc52").id, createdAt: saturdayScan,
        message: `Out-of-hours scan: "${tc52.name}" detected at "BAK Warehouse A" on ${describeLocal(saturdayScan)} — outside working hours (Mon–Fri 07:00–18:00)`,
      },
      {
        type: "anomaly", severity: "warning", dismissed: false, assetId: asset("galaxy-a54").id, createdAt: mismatchAt,
        message: `Location mismatch: "${a54.name}" belongs to AGRI but was detected at "TSAS House – IT Department" (TSAS)`,
      },
    ]);

    // ── Lifecycle history ────────────────────────────────────────────────────
    const approverFor = (sbu: string) => (sbu === "BAK" ? APPROVERS.bak : sbu === "PROP" ? APPROVERS.prop : APPROVERS.tsas);
    await tx.insert(assetLifecycleEventsTable).values([
      ...ASSETS.filter((a) => a.assignedTo).map((a) => ({
        assetId: asset(a.key).id, eventType: "deployment", performedBy: approverFor(a.sbu), assignedTo: a.assignedTo!,
        toSbuId: sbuId(a.sbu), notes: null, timestamp: at(a.registeredDaysAgo - 2, 10),
      })),
      {
        assetId: asset("laserjet").id, eventType: "repair_sent", performedBy: "Farai Ncube", vendor: "Printech Zimbabwe", ticketRef: "PT-2291",
        conditionBefore: "Paper feed jams on every job", timestamp: at(18, 11),
      },
      {
        assetId: asset("moisture").id, eventType: "repair_sent", performedBy: "Kudzai Banda", vendor: "SGS Calibration Services", ticketRef: "CAL-0457",
        conditionBefore: "Readings drifting ±1.5%", timestamp: at(30, 10),
      },
      {
        assetId: asset("moisture").id, eventType: "repair_returned", performedBy: "Kudzai Banda", vendor: "SGS Calibration Services",
        conditionAfter: "Calibrated to ±0.1%", timestamp: at(21, 15),
      },
      {
        assetId: asset("hilux").id, eventType: "sbu_transfer", performedBy: APPROVERS.bak, fromSbuId: sbuId("TSF"), toSbuId: sbuId("BAK"),
        notes: "Fleet consolidation into Bak Logistics", timestamp: at(120, 9),
      },
      {
        assetId: asset("epson-x41").id, eventType: "note", performedBy: APPROVERS.prop, notes: "Asset disposed. Reason: lamp housing cracked", timestamp: at(90, 12),
      },
      {
        assetId: asset("benq").id, eventType: "note", performedBy: "Tatenda Mhlanga", notes: "Reported missing after the September grower meeting — security notified",
        timestamp: at(10, 16),
      },
      {
        assetId: asset("probook").id, eventType: "note", performedBy: APPROVERS.bak, timestamp: minutesAgo(165),
        notes: `Flagged: gate pass #${gp4.id} rejected — Unassigned laptop — no business reason to leave site. The asset must not leave the premises.`,
      },
    ]);

    // Phase 3: the demo rows above use the original statuses; migration 0006 maps them, exactly as on an existing database.
    const mapping = readFileSync(resolve(import.meta.dirname, "..", "..", "lib", "db", "migrations", "0006_map_statuses.sql"), "utf8");
    for (const statement of mapping.split("--> statement-breakpoint")) await tx.execute(sql.raw(statement));

    // Phase 4c. Every pass above gets its item (as migration 0010 does on an existing database), then a two-item TSAS
    // pass: the UPS came back yesterday, the desktop is still out and overdue (SG-03, GI-06).
    const backfill = readFileSync(resolve(import.meta.dirname, "..", "..", "lib", "db", "migrations", "0010_gate_and_verification.sql"), "utf8");
    await tx.execute(sql.raw(backfill.slice(backfill.indexOf('INSERT INTO "gate_pass_items"'))));
    const desktop = asset("optiplex");
    const printer = asset("apc-ups");
    const tsasGate = loc("tsas-gate").id;
    const gp7Decided = at(4, 9, 15);
    const gp7Due = dateKey(new Date(NOW.getTime() - DAY));
    const gp7 = await insertPass({
      assetId: desktop.id, requestedBy: "Farai Ncube", reason: "Desktop and UPS for the grower centre registration desk",
      destination: "Marondera Grower Centre", returnDate: gp7Due, status: "approved", approvedBy: APPROVERS.tsas, source: "manual",
      decidedAt: gp7Decided, createdAt: new Date(gp7Decided.getTime() - 2 * 60 * 60 * 1000), updatedAt: gp7Decided,
    });
    const gp7Out = at(3, 7, 40);
    const gp7Back = at(1, 16, 5);
    await tx.insert(movementsTable).values([
      { assetId: desktop.id, fromLocationId: tsasGate, toLocationId: null, triggeredBy: "gate_pass", gatePassId: gp7.id, movedBy: "Farai Ncube",
        notes: `${exitNote(gp7.id, "TSAS Main Gate", APPROVERS.tsas, "Marondera Grower Centre", gp7Due)} — checked by Gift Mahachi`, timestamp: gp7Out },
      { assetId: printer.id, fromLocationId: tsasGate, toLocationId: null, triggeredBy: "gate_pass", gatePassId: gp7.id, movedBy: "Farai Ncube",
        notes: `${exitNote(gp7.id, "TSAS Main Gate", APPROVERS.tsas, "Marondera Grower Centre", gp7Due)} — checked by Gift Mahachi`, timestamp: gp7Out },
      { assetId: printer.id, fromLocationId: null, toLocationId: tsasGate, triggeredBy: "manual", gatePassId: gp7.id, movedBy: "Gift Mahachi",
        notes: `Returned through TSAS Main Gate under gate pass #${gp7.id} (good)`, timestamp: gp7Back },
    ]);
    await tx.insert(gatePassItemsTable).values([
      { gatePassId: gp7.id, assetId: desktop.id, exitedAt: gp7Out, exitGateId: tsasGate, exitOfficer: "Gift Mahachi" },
      { gatePassId: gp7.id, assetId: printer.id, exitedAt: gp7Out, exitGateId: tsasGate, exitOfficer: "Gift Mahachi",
        returnedAt: gp7Back, returnGateId: tsasGate, returnOfficer: "Gift Mahachi", returnCondition: "good" },
    ]);
    await tx.update(assetsTable).set({ status: "out_on_gate_pass", currentLocationId: null }).where(eq(assetsTable.id, desktop.id));
    await tx.update(assetsTable).set({ currentLocationId: tsasGate }).where(eq(assetsTable.id, printer.id));
    await tx.insert(aiInsightsTable).values({
      type: "missing_asset", severity: "warning", assetId: desktop.id, gatePassId: gp7.id,
      message: `Overdue: "${desktop.name}" was due back on ${gp7Due} under gate pass #${gp7.id} (Farai Ncube)`, createdAt: at(0, 6, 0),
    });

    // A closed count of the TSAS IT Department (SI-07): one phone not found and an untagged item, waiting for SBU Finance (SF-06).
    const tsasIt = loc("tsas-it").id;
    const [count] = await tx.insert(verificationSessionsTable).values({
      sbuId: sbuId("TSAS"), locationId: tsasIt, scope: "it", status: "closed", startedBy: "Farai Ncube", startedAt: at(1, 9, 0), closedAt: at(1, 9, 40),
    }).returning();
    const expected = await tx.select({ id: assetsTable.id, rfidTag: assetsTable.rfidTag }).from(assetsTable)
      .innerJoin(assetCategoriesTable, eq(assetCategoriesTable.id, assetsTable.categoryId))
      .where(sql`${assetsTable.currentLocationId} = ${tsasIt} AND ${assetCategoriesTable.isIt} AND ${assetsTable.status} NOT IN ('out_on_gate_pass', 'lost_stolen', 'disposed')`);
    const notFound = asset("galaxy-s23").id;
    const seen = expected.filter((e) => e.id !== notFound);
    await tx.insert(verificationScansTable).values([
      ...seen.map((e, i) => ({ sessionId: count.id, code: e.rfidTag ?? `ID-${e.id}`, assetId: e.id, scannedBy: "Farai Ncube", scannedAt: at(1, 9, 5 + i) })),
      { sessionId: count.id, code: "TAG-UNKNOWN-0417", scannedBy: "Farai Ncube", scannedAt: at(1, 9, 30) },
    ]);
    await tx.insert(verificationResultsTable).values([
      ...expected.map((e) => ({ sessionId: count.id, assetId: e.id, result: e.id === notFound ? "missing" : "found" })),
      { sessionId: count.id, code: "TAG-UNKNOWN-0417", result: "unrecorded" },
    ]);

    // Phase 4d: the BAK fleet (AM-02 … AM-04). The Hilux's ZINARA licence has expired (red on the register) and its
    // insurance renews in 20 days; the forklift is serviced every 500 hours and is due soon.
    const hiluxId = asset("hilux").id;
    const forkliftId = asset("forklift").id;
    const day = (offset: number) => dateKey(new Date(NOW.getTime() + offset * DAY));
    await tx.insert(fleetDetailsTable).values([
      { assetId: hiluxId, fleetStatus: "active", driverName: "Tonderai Gumbo (contract driver)", updatedBy: "Simbarashe Chikore" },
      { assetId: forkliftId, fleetStatus: "active", updatedBy: "Simbarashe Chikore" },
    ]);
    await tx.insert(vehicleDocumentsTable).values([
      { assetId: hiluxId, kind: "licence", reference: "VL-AGR4471-2026", expiresOn: day(160), createdBy: "Simbarashe Chikore" },
      { assetId: hiluxId, kind: "insurance", reference: "Old Mutual POL-55120", expiresOn: day(20), createdBy: "Simbarashe Chikore" },
      { assetId: hiluxId, kind: "fitness", reference: "COF-11872", expiresOn: day(95), createdBy: "Simbarashe Chikore" },
      { assetId: hiluxId, kind: "zinara", reference: "ZIN-889012", expiresOn: day(-5), createdBy: "Simbarashe Chikore" },
    ]);
    await tx.insert(serviceSchedulesTable).values([
      { assetId: hiluxId, intervalReading: 10000, intervalDays: 180, startDate: day(-120), startReading: 135000, updatedBy: "Simbarashe Chikore" },
      { assetId: forkliftId, intervalReading: 500, intervalDays: 365, startDate: day(-200), startReading: 5650, updatedBy: "Simbarashe Chikore" },
    ]);
    await tx.insert(serviceLogsTable).values([
      { assetId: hiluxId, serviceDate: day(-60), reading: 138900, garage: "Toyota Zimbabwe – Msasa", workDone: "140 000 km service, brake pads",
        cost: "385.00", currency: "USD", costBy: "Rumbidzai Dube", loggedBy: "Simbarashe Chikore" },
      { assetId: forkliftId, serviceDate: day(-90), reading: 5650, garage: "Bak Workshop", workDone: "Battery check, hydraulic oil", loggedBy: "Simbarashe Chikore" },
    ]);

    // Phase 5a: CAPEX budgets for the current financial year (1 November – 31 October, named after the year it ends).
    const fyEnd = NOW.getMonth() >= 10 ? NOW.getFullYear() + 1 : NOW.getFullYear();
    await tx.insert(capexBudgetsTable).values([
      { sbuId: sbuId("TSAS"), financialYear: fyEnd, categoryId: categoryOf("COMP").id, amount: "6000.00", updatedBy: "Chipo Mlambo" },
      { sbuId: sbuId("TSAS"), financialYear: fyEnd, categoryId: categoryOf("PER").id, amount: "1500.00", updatedBy: "Chipo Mlambo" },
      { sbuId: sbuId("BAK"), financialYear: fyEnd, categoryId: categoryOf("COMP").id, amount: "2500.00", updatedBy: "Rumbidzai Dube" },
      { sbuId: sbuId("BAK"), financialYear: fyEnd, categoryId: categoryOf("MV").id, amount: "45000.00", updatedBy: "Rumbidzai Dube" },
    ]);

    console.log("Seeded AssetIQ demo data:");
    console.log(`  ${sbuRows.length} SBUs, ${locRows.length} locations, ${readerRows.length} RFID readers, ${assetRows.length} assets`);
    console.log(`  ${movements.length} movements, 6 gate passes (2 pending, 3 approved, 1 rejected), 5 insights`);
    console.log(`  ${DEMO_USERS.length} sign-ins: ${DEMO_USERS.map((u) => `${u.username}/${u.password} (${u.role})`).join(", ")}`);
    console.log(`  ${SCOPED_USERS.length + PHASE2_USERS.length} scoped sign-ins (password = username + "-2026"): ${[...SCOPED_USERS, ...PHASE2_USERS].map((u) => u.username).join(", ")}`);
    console.log(`  ${EMPLOYEES.length} employees in ${DEPARTMENTS.length} departments per SBU; operator and viewer are disabled (Phase 2)`);
    console.log(`  ${UPGRADES.length} upgrades (2 waiting for SBU Finance), ${series.size} mock Business Central number series`);
    console.log("\nDemo pointers:");
    console.log(`  • Gate pass #${gp6.id} (auto-raised at TSL Head Office Gate) is waiting for approval.`);
    console.log(`  • Gate pass #${gp5.id} (manual request for ${epson.name}) is waiting for approval.`);
    console.log(`  • Gate pass GP-${gp7.id}: ${desktop.name} is overdue (tsas.gate → Gate); count #${count.id} waits for TSAS Finance (Verification).`);
    console.log("  • bak.admin → Fleet & Non-IT: the Hilux shows red (ZINARA expired); the forklift service is due soon.");
    console.log('  • Ask the AI assistant: "Where is the Dell laptop assigned to John Doe?"');
    console.log(`  • RFID tag for John Doe's laptop: ${asset("dell-5440").rfidTag} — scan it at "TSAS Main Gate Reader" to raise a pass.`);
  });
}

main()
  .catch((err) => {
    console.error(err);
    process.exitCode = 1;
  })
  .finally(() => pool.end());
