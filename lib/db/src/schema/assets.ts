import { pgTable, serial, text, timestamp, integer, numeric } from "drizzle-orm/pg-core";
import { assetCategoriesTable } from "./asset_categories";
import { createInsertSchema } from "drizzle-zod";
import { z } from "zod/v4";
import { locationsTable } from "./locations";
import { sbusTable } from "./sbus";
import { departmentsTable, employeesTable } from "./people";

export const assetsTable = pgTable("assets", {
  id: serial("id").primaryKey(),
  // The description (AU-01). Built by the API from the category template; users never type it.
  name: text("name").notNull(),
  category: text("category").notNull(),      // category name, kept in step with category_id for the RFID / AI / report code
  categoryId: integer("category_id").references(() => assetCategoriesTable.id).notNull(),
  subCategory: text("sub_category"),
  // Group-unique identifier (AU-02): the template's identifier field (serial, VIN, IMEI). Null only with a "No serial" reason.
  serialNumber: text("serial_number").unique(),
  noSerialReason: text("no_serial_reason"),
  noSerialStatus: text("no_serial_status"),  // pending | approved | rejected (SBU Finance decides)
  noSerialDecidedBy: text("no_serial_decided_by"),
  noSerialDecidedAt: timestamp("no_serial_decided_at"),
  macAddress: text("mac_address").unique(),  // optional for now (GI-02), unique when entered
  rfidTag: text("rfid_tag").unique(),        // null until tagged; an asset cannot be tagged before it has an asset number
  // Business Central fixed-asset number (AU-03): null while pending; a trigger forbids changing it once set.
  assetNumber: text("asset_number").unique(),
  previousAssetNumber: text("previous_asset_number"), // imported assets keep their old number here
  // pending_asset_number | active | maintenance | retired | missing (the full state machine comes in Phase 3)
  // Phase 3 state machine (API Domain/AssetStatusMachine.cs): pending_asset_number | in_store | in_use | out_on_gate_pass |
  // in_repair | transfer_pending | pending_disposal | lost_stolen | disposed
  status: text("status").notNull().default("pending_asset_number"),
  lostDate: text("lost_date"),                // GI-07: YYYY-MM-DD, with the reporter and notes, when lost / stolen
  lostReportedBy: text("lost_reported_by"),
  lostNotes: text("lost_notes"),
  condition: text("condition"),              // new | good | fair | poor | damaged
  assignedTo: text("assigned_to"),           // custodian: the person holding the asset (or the store holding it)
  custodianEmployeeId: integer("custodian_employee_id").references(() => employeesTable.id), // the employee holding it (assignedTo shows the name)
  departmentId: integer("department_id").references(() => departmentsTable.id),            // the department holding it (LM-01)
  currentLocationId: integer("current_location_id").references(() => locationsTable.id),
  sbuId: integer("sbu_id").references(() => sbusTable.id), // which SBU owns / is responsible for this asset
  // Cost (AU-04). Money is numeric, never floating point.
  originalCost: numeric("original_cost", { precision: 18, scale: 2 }),
  currency: text("currency"),                // ISO 4217, e.g. USD, ZWG, ZAR
  exchangeRate: numeric("exchange_rate", { precision: 18, scale: 6 }), // currency → USD
  costUsd: numeric("cost_usd", { precision: 18, scale: 2 }), // original_cost × exchange_rate, fixed at capitalisation
  capitalisedAt: timestamp("capitalised_at"),
  capitalisedBy: text("capitalised_by"),
  residualValue: numeric("residual_value", { precision: 18, scale: 2 }).notNull().default("0"),
  // Purchase details (AU-06)
  supplier: text("supplier"),
  invoiceNo: text("invoice_no"),
  poNo: text("po_no"),
  purchaseDate: text("purchase_date"),                // stored as ISO date string (YYYY-MM-DD)
  inUseDate: text("in_use_date"),                     // YYYY-MM-DD: depreciation starts in this month
  warrantyEnds: text("warranty_ends"),                // YYYY-MM-DD
  disposalDate: text("disposal_date"),                // YYYY-MM-DD: no depreciation from this month
  purchaseValue: numeric("purchase_value"),            // legacy USD value, kept equal to cost_usd / original_cost
  notes: text("notes"),
  createdAt: timestamp("created_at").defaultNow().notNull(),
  updatedAt: timestamp("updated_at").defaultNow().notNull(),
});

export const insertAssetSchema = createInsertSchema(assetsTable).omit({ id: true, createdAt: true, updatedAt: true });
export type InsertAsset = z.infer<typeof insertAssetSchema>;
export type Asset = typeof assetsTable.$inferSelect;
