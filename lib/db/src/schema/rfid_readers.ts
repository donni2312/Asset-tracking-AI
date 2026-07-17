import { pgTable, serial, text, timestamp, integer } from "drizzle-orm/pg-core";
import { createInsertSchema } from "drizzle-zod";
import { z } from "zod/v4";
import { locationsTable } from "./locations";

export const rfidReadersTable = pgTable("rfid_readers", {
  id: serial("id").primaryKey(),
  name: text("name").notNull(),
  locationId: integer("location_id").references(() => locationsTable.id).notNull(),
  status: text("status").notNull().default("active"), // active | inactive
  lastReadAt: timestamp("last_read_at"),
  createdAt: timestamp("created_at").defaultNow().notNull(),
});

export const insertRfidReaderSchema = createInsertSchema(rfidReadersTable).omit({ id: true, createdAt: true });
export type InsertRfidReader = z.infer<typeof insertRfidReaderSchema>;
export type RfidReader = typeof rfidReadersTable.$inferSelect;
