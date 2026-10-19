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
  assetLifecycleEventsTable,
  assetsTable,
  gatePassesTable,
  locationsTable,
  movementsTable,
  rfidReadersTable,
  sbusTable,
} from "@workspace/db";
import { count, eq, max, sql } from "drizzle-orm";
import { APPROVERS, ASSETS, INACTIVE_READER, LOCATIONS, SBUS, type AssetSeed } from "./seed-data";

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
        sql`TRUNCATE ai_insights, asset_lifecycle_events, movements, gate_passes, assets, rfid_readers, locations, sbus RESTART IDENTITY CASCADE`,
      );
    }

    // SBUs, locations, readers
    const sbuRows = await tx.insert(sbusTable).values(SBUS).returning();
    const sbuId = (code: string) => sbuRows.find((s) => s.code === code)!.id;

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
        ASSETS.map((a, i) => ({
          name: a.name,
          category: a.category,
          serialNumber: `${a.sbu}-SN-${String(10231 + i * 37).padStart(6, "0")}`,
          rfidTag: `RFID-${a.sbu}-${String(i + 1).padStart(4, "0")}`,
          status: a.status,
          assignedTo: a.assignedTo ?? null,
          currentLocationId: a.location ? loc(a.location).id : null,
          sbuId: sbuId(a.sbu),
          purchaseDate: dateKey(at(a.registeredDaysAgo + 3, 9)),
          purchaseValue: String(a.value),
          notes: a.notes ?? null,
          createdAt: at(a.registeredDaysAgo, 9),
          updatedAt: at(Math.min(a.lastMovedDaysAgo, a.registeredDaysAgo), 9),
        })),
      )
      .returning();
    const asset = (key: string) => assetRows[ASSETS.findIndex((a) => a.key === key)];
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

    console.log("Seeded AssetIQ demo data:");
    console.log(`  ${sbuRows.length} SBUs, ${locRows.length} locations, ${readerRows.length} RFID readers, ${assetRows.length} assets`);
    console.log(`  ${movements.length} movements, 6 gate passes (2 pending, 3 approved, 1 rejected), 5 insights`);
    console.log("\nDemo pointers:");
    console.log(`  • Gate pass #${gp6.id} (auto-raised at TSL Head Office Gate) is waiting for approval.`);
    console.log(`  • Gate pass #${gp5.id} (manual request for ${epson.name}) is waiting for approval.`);
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
