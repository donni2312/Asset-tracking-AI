import { pgTable, serial, text, timestamp, integer } from "drizzle-orm/pg-core";
import { createInsertSchema } from "drizzle-zod";
import { z } from "zod/v4";
import { assetsTable } from "./assets";
import { locationsTable } from "./locations";
import { rfidReadersTable } from "./rfid_readers";

export const movementsTable = pgTable("movements", {
  id: serial("id").primaryKey(),
  assetId: integer("asset_id").references(() => assetsTable.id).notNull(),
  fromLocationId: integer("from_location_id").references(() => locationsTable.id),
  toLocationId: integer("to_location_id").references(() => locationsTable.id),
  triggeredBy: text("triggered_by").notNull().default("manual"), // rfid | manual | gate_pass
  rfidReaderId: integer("rfid_reader_id").references(() => rfidReadersTable.id),
  movedBy: text("moved_by"),
  notes: text("notes"),
  timestamp: timestamp("timestamp").defaultNow().notNull(),
});

export const insertMovementSchema = createInsertSchema(movementsTable).omit({ id: true, timestamp: true });
export type InsertMovement = z.infer<typeof insertMovementSchema>;
export type Movement = typeof movementsTable.$inferSelect;
