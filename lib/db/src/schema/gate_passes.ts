import { pgTable, serial, text, timestamp, integer } from "drizzle-orm/pg-core";
import { createInsertSchema } from "drizzle-zod";
import { z } from "zod/v4";
import { assetsTable } from "./assets";

export const gatePassesTable = pgTable("gate_passes", {
  id: serial("id").primaryKey(),
  assetId: integer("asset_id").references(() => assetsTable.id).notNull(),
  requestedBy: text("requested_by").notNull(),
  approvedBy: text("approved_by"),
  status: text("status").notNull().default("pending"), // pending | approved | rejected
  reason: text("reason").notNull(),
  destination: text("destination"),
  returnDate: text("return_date"), // ISO date string
  decisionNote: text("decision_note"),
  createdAt: timestamp("created_at").defaultNow().notNull(),
  updatedAt: timestamp("updated_at").defaultNow().notNull(),
});

export const insertGatePassSchema = createInsertSchema(gatePassesTable).omit({ id: true, createdAt: true, updatedAt: true });
export type InsertGatePass = z.infer<typeof insertGatePassSchema>;
export type GatePass = typeof gatePassesTable.$inferSelect;
