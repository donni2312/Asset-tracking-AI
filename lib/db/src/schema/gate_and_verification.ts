import { integer, pgTable, serial, text, timestamp } from "drizzle-orm/pg-core";
import { aiInsightsTable } from "./ai_insights";
import { assetsTable } from "./assets";
import { gatePassesTable } from "./gate_passes";
import { locationsTable } from "./locations";
import { sbusTable } from "./sbus";

/**
 * Phase 4c. Gate: the items of a gate pass and their exit / return (SG-03, partial returns on multi-item passes),
 * and what the Gate Officer did about an RFID alert (SG-02). Verification: a scan session per location (SI-07),
 * its results (found, missing, unrecorded) and how each difference was resolved (SF-06).
 */
export const gatePassItemsTable = pgTable("gate_pass_items", {
  id: serial("id").primaryKey(),
  gatePassId: integer("gate_pass_id").references(() => gatePassesTable.id).notNull(),
  assetId: integer("asset_id").references(() => assetsTable.id).notNull(),
  exitedAt: timestamp("exited_at"),
  exitGateId: integer("exit_gate_id").references(() => locationsTable.id),
  exitOfficer: text("exit_officer"),
  exitNotes: text("exit_notes"),
  returnedAt: timestamp("returned_at"),
  returnGateId: integer("return_gate_id").references(() => locationsTable.id),
  returnOfficer: text("return_officer"),
  returnCondition: text("return_condition"),
  returnNotes: text("return_notes"),
});

/** SG-02: the officer's action on an alert (stopped, released with override, escalated). */
export const gateActionsTable = pgTable("gate_actions", {
  id: serial("id").primaryKey(),
  insightId: integer("insight_id").references(() => aiInsightsTable.id),
  assetId: integer("asset_id").references(() => assetsTable.id).notNull(),
  gateLocationId: integer("gate_location_id").references(() => locationsTable.id),
  action: text("action").notNull(),                 // stopped | released_with_override | escalated
  notes: text("notes"),
  officer: text("officer").notNull(),
  at: timestamp("at").defaultNow().notNull(),
});

export const verificationSessionsTable = pgTable("verification_sessions", {
  id: serial("id").primaryKey(),
  sbuId: integer("sbu_id").references(() => sbusTable.id).notNull(),
  locationId: integer("location_id").references(() => locationsTable.id).notNull(),
  scope: text("scope").notNull(),                   // it | non_it | all (what the person running it looks after)
  status: text("status").notNull().default("open"), // open | closed (results produced) | reconciled
  startedBy: text("started_by").notNull(),
  startedAt: timestamp("started_at").defaultNow().notNull(),
  closedAt: timestamp("closed_at"),
});

export const verificationScansTable = pgTable("verification_scans", {
  id: serial("id").primaryKey(),
  sessionId: integer("session_id").references(() => verificationSessionsTable.id).notNull(),
  code: text("code").notNull(),
  assetId: integer("asset_id").references(() => assetsTable.id),
  scannedBy: text("scanned_by").notNull(),
  scannedAt: timestamp("scanned_at").defaultNow().notNull(),
});

export const verificationResultsTable = pgTable("verification_results", {
  id: serial("id").primaryKey(),
  sessionId: integer("session_id").references(() => verificationSessionsTable.id).notNull(),
  assetId: integer("asset_id").references(() => assetsTable.id),
  code: text("code"),
  result: text("result").notNull(),                 // found | missing | unrecorded (found here, recorded elsewhere or not at all)
  resolution: text("resolution"),                   // adjust | write_off | investigate | register (SF-06)
  comment: text("comment"),
  resolvedBy: text("resolved_by"),
  resolvedAt: timestamp("resolved_at"),
});
