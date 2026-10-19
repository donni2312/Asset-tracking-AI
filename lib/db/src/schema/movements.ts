import { pgTable, serial, text, timestamp, integer } from "drizzle-orm/pg-core";
import { createInsertSchema } from "drizzle-zod";
import { z } from "zod/v4";
import { assetsTable } from "./assets";
import { locationsTable } from "./locations";
import { rfidReadersTable } from "./rfid_readers";
import { gatePassesTable } from "./gate_passes";

export const movementsTable = pgTable("movements", {
  id: serial("id").primaryKey(),
  assetId: integer("asset_id").references(() => assetsTable.id).notNull(),
  fromLocationId: integer("from_location_id").references(() => locationsTable.id),
  // null = off-premises (e.g. the asset left through a gate under an approved gate pass)
  toLocationId: integer("to_location_id").references(() => locationsTable.id),
  triggeredBy: text("triggered_by").notNull().default("manual"), // rfid | manual | gate_pass
  rfidReaderId: integer("rfid_reader_id").references(() => rfidReadersTable.id),
  // Gate pass that authorised this movement (gate_pass exits, authorised gate scans)
  // or that was raised because of it (unauthorised gate scans).
  gatePassId: integer("gate_pass_id").references(() => gatePassesTable.id),
  movedBy: text("moved_by"),
  notes: text("notes"),
  timestamp: timestamp("timestamp").defaultNow().notNull(),
});

export const insertMovementSchema = createInsertSchema(movementsTable).omit({ id: true, timestamp: true });
export type InsertMovement = z.infer<typeof insertMovementSchema>;
export type Movement = typeof movementsTable.$inferSelect;
