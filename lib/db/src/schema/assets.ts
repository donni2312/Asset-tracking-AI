import { pgTable, serial, text, timestamp, integer, numeric } from "drizzle-orm/pg-core";
import { createInsertSchema } from "drizzle-zod";
import { z } from "zod/v4";
import { locationsTable } from "./locations";
import { sbusTable } from "./sbus";

export const assetsTable = pgTable("assets", {
  id: serial("id").primaryKey(),
  name: text("name").notNull(),
  category: text("category").notNull(),      // Laptop | Desktop | Projector | Phone | Printer | Network Equipment | UPS | Furniture | Vehicle | Other
  serialNumber: text("serial_number").notNull().unique(),
  rfidTag: text("rfid_tag").notNull().unique(),
  status: text("status").notNull().default("active"), // active | maintenance | retired | missing
  assignedTo: text("assigned_to"),                    // current user / person holding the asset
  currentLocationId: integer("current_location_id").references(() => locationsTable.id),
  sbuId: integer("sbu_id").references(() => sbusTable.id), // which SBU owns / is responsible for this asset
  purchaseDate: text("purchase_date"),                // stored as ISO date string (YYYY-MM-DD)
  purchaseValue: numeric("purchase_value"),            // USD value
  notes: text("notes"),
  createdAt: timestamp("created_at").defaultNow().notNull(),
  updatedAt: timestamp("updated_at").defaultNow().notNull(),
});

export const insertAssetSchema = createInsertSchema(assetsTable).omit({ id: true, createdAt: true, updatedAt: true });
export type InsertAsset = z.infer<typeof insertAssetSchema>;
export type Asset = typeof assetsTable.$inferSelect;
