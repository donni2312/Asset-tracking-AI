import { integer, pgTable, serial, text, timestamp } from "drizzle-orm/pg-core";

/**
 * Append-only audit trail (AU-06, GF-07). A database trigger (migration 0002) rejects UPDATE and DELETE,
 * and the API has no endpoint that changes or removes a row.
 */
export const auditLogTable = pgTable("audit_log", {
  id: serial("id").primaryKey(),
  entity: text("entity").notNull(),        // asset | upgrade | category | category_field | setting | user
  entityId: integer("entity_id"),
  assetId: integer("asset_id"),            // set on every entry about an asset, for its timeline
  action: text("action").notNull(),        // created | updated | category_changed | capitalised | duplicate_serial_attempt | …
  field: text("field"),
  oldValue: text("old_value"),
  newValue: text("new_value"),
  reason: text("reason"),
  userId: integer("user_id"),
  userName: text("user_name").notNull(),
  at: timestamp("at").defaultNow().notNull(),
});

/** Sign-ins and exports (AU-12 requires it for auditors; it is recorded for everyone). */
export const accessLogTable = pgTable("access_log", {
  id: serial("id").primaryKey(),
  userId: integer("user_id"),
  username: text("username").notNull(),
  role: text("role").notNull(),
  action: text("action").notNull(),        // login | login_refused | export
  detail: text("detail"),
  at: timestamp("at").defaultNow().notNull(),
});

/**
 * Requests for a Business Central fixed-asset number (AU-03): queued when an asset is created and retried
 * with back-off while BC is unreachable. A number is never made up locally.
 */
export const bcNumberRequestsTable = pgTable("bc_number_requests", {
  id: serial("id").primaryKey(),
  assetId: integer("asset_id").notNull().unique(),
  status: text("status").notNull().default("queued"), // queued | done
  attempts: integer("attempts").notNull().default(0),
  nextAttemptAt: timestamp("next_attempt_at").defaultNow().notNull(),
  lastError: text("last_error"),
  assetNumber: text("asset_number"),
  createdAt: timestamp("created_at").defaultNow().notNull(),
  updatedAt: timestamp("updated_at").defaultNow().notNull(),
});

/**
 * Number series of the MOCK Business Central, used until the real BC integration: one counter per
 * SBU / category code / year, so a number such as "BAK-IT-2026-00047" is never issued twice.
 */
export const bcMockNumberSeriesTable = pgTable("bc_mock_number_series", {
  id: serial("id").primaryKey(),
  prefix: text("prefix").notNull().unique(), // BAK-IT-2026
  lastNumber: integer("last_number").notNull(),
});
