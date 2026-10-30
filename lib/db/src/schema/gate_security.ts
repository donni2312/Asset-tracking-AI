import { customType, integer, pgTable, serial, text, timestamp, unique } from "drizzle-orm/pg-core";
import { assetsTable } from "./assets";
import { gatePassesTable } from "./gate_passes";
import { locationsTable } from "./locations";
import { sbusTable } from "./sbus";

const bytea = customType<{ data: Buffer }>({ dataType: () => "bytea" });

/**
 * Phase 5b. GI-07: the security actions taken for a lost or stolen device (remote wipe, account disabled, police
 * report), one row per action. SG-05: a visitor's or contractor's own equipment logged at the gate on entry and
 * checked out on exit without a gate pass. SG-06: an incident escalated from the gate (forged pass, unknown tag,
 * refused search), stored against the asset when there is one.
 */
export const securityActionsTable = pgTable("security_actions", {
  id: serial("id").primaryKey(),
  assetId: integer("asset_id").references(() => assetsTable.id).notNull(),
  action: text("action").notNull(),                  // remote_wipe | account_disable | police_report
  reference: text("reference"),                      // police case number, ticket …
  notes: text("notes"),
  doneBy: text("done_by").notNull(),
  doneAt: timestamp("done_at").defaultNow().notNull(),
}, (t) => [unique("security_actions_asset_action_unique").on(t.assetId, t.action)]);

export const visitorItemsTable = pgTable("visitor_items", {
  id: serial("id").primaryKey(),
  sbuId: integer("sbu_id").references(() => sbusTable.id).notNull(),
  gateLocationId: integer("gate_location_id").references(() => locationsTable.id).notNull(),
  ownerName: text("owner_name").notNull(),
  ownerCompany: text("owner_company"),
  item: text("item").notNull(),
  serialNumber: text("serial_number"),
  photo: bytea("photo"),
  photoContentType: text("photo_content_type"),
  checkedInBy: text("checked_in_by").notNull(),
  checkedInAt: timestamp("checked_in_at").defaultNow().notNull(),
  checkedOutBy: text("checked_out_by"),
  checkedOutAt: timestamp("checked_out_at"),
  checkedOutGateId: integer("checked_out_gate_id").references(() => locationsTable.id),
});

export const gateIncidentsTable = pgTable("gate_incidents", {
  id: serial("id").primaryKey(),
  sbuId: integer("sbu_id").references(() => sbusTable.id).notNull(),
  gateLocationId: integer("gate_location_id").references(() => locationsTable.id),
  kind: text("kind").notNull(),                      // forged_pass | unknown_tag | refused_search | other
  assetId: integer("asset_id").references(() => assetsTable.id),
  gatePassId: integer("gate_pass_id").references(() => gatePassesTable.id),
  code: text("code"),                                // the scanned tag / pass code
  notes: text("notes").notNull(),
  photo: bytea("photo"),
  photoContentType: text("photo_content_type"),
  officer: text("officer").notNull(),
  createdAt: timestamp("created_at").defaultNow().notNull(),
});
