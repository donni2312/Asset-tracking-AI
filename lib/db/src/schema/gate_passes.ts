import { pgTable, serial, text, timestamp, integer } from "drizzle-orm/pg-core";
import { createInsertSchema } from "drizzle-zod";
import { z } from "zod/v4";
import { assetsTable } from "./assets";
import { locationsTable } from "./locations";

export const gatePassesTable = pgTable("gate_passes", {
  id: serial("id").primaryKey(),
  assetId: integer("asset_id").references(() => assetsTable.id).notNull(),
  requestedBy: text("requested_by").notNull(),
  approvedBy: text("approved_by"), // the person who decided (approved OR rejected) the pass
  status: text("status").notNull().default("pending"), // pending | approved | rejected
  reason: text("reason").notNull(),
  destination: text("destination"),
  returnDate: text("return_date"), // ISO date string (YYYY-MM-DD); approved pass is valid until this date
  decisionNote: text("decision_note"),
  // manual   — requested in advance through the dashboard / API
  // rfid_gate — raised automatically when a gate reader saw the asset without an approved pass
  source: text("source").notNull().default("manual"),
  gateLocationId: integer("gate_location_id").references(() => locationsTable.id), // gate where the asset was detected (rfid_gate passes)
  decidedAt: timestamp("decided_at"), // when the pass was approved / rejected
  createdAt: timestamp("created_at").defaultNow().notNull(),
  updatedAt: timestamp("updated_at").defaultNow().notNull(),
});

export const insertGatePassSchema = createInsertSchema(gatePassesTable).omit({ id: true, createdAt: true, updatedAt: true });
export type InsertGatePass = z.infer<typeof insertGatePassSchema>;
export type GatePass = typeof gatePassesTable.$inferSelect;
