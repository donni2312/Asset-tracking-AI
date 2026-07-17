import { pgTable, serial, text, timestamp, integer } from "drizzle-orm/pg-core";
import { createInsertSchema } from "drizzle-zod";
import { z } from "zod/v4";
import { assetsTable } from "./assets";
import { sbusTable } from "./sbus";

/**
 * Higher-level lifecycle events for an asset — captures business-meaningful
 * state transitions beyond raw RFID gate reads:
 *
 *  deployment       — IT issues asset to a person / department
 *  return_to_it     — Asset returned to IT store / helpdesk
 *  repair_sent      — Asset sent for repair (external vendor or internal workshop)
 *  repair_returned  — Asset returned after repair, ready for redeployment
 *  sbu_transfer     — Asset shared / permanently transferred between SBUs
 *  note             — Free-text administrative note (no location change)
 */
export const assetLifecycleEventsTable = pgTable("asset_lifecycle_events", {
  id: serial("id").primaryKey(),
  assetId: integer("asset_id").references(() => assetsTable.id).notNull(),
  eventType: text("event_type").notNull(), // deployment | return_to_it | repair_sent | repair_returned | sbu_transfer | note
  performedBy: text("performed_by").notNull(),
  fromSbuId: integer("from_sbu_id").references(() => sbusTable.id), // source SBU (for transfers)
  toSbuId: integer("to_sbu_id").references(() => sbusTable.id),     // destination SBU (for transfers / deployments)
  assignedTo: text("assigned_to"),       // person / department receiving the asset
  vendor: text("vendor"),                // repair vendor or workshop name
  ticketRef: text("ticket_ref"),         // repair ticket / job order reference
  conditionBefore: text("condition_before"), // asset condition before repair
  conditionAfter: text("condition_after"),   // asset condition after repair / return
  notes: text("notes"),
  timestamp: timestamp("timestamp").defaultNow().notNull(),
});

export const insertAssetLifecycleEventSchema = createInsertSchema(assetLifecycleEventsTable)
  .omit({ id: true, timestamp: true });
export type InsertAssetLifecycleEvent = z.infer<typeof insertAssetLifecycleEventSchema>;
export type AssetLifecycleEvent = typeof assetLifecycleEventsTable.$inferSelect;
