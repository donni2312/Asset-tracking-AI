import { boolean, integer, jsonb, numeric, pgTable, serial, text, timestamp } from "drizzle-orm/pg-core";
import { assetCategoriesTable } from "./asset_categories";
import { assetsTable } from "./assets";
import { departmentsTable, employeesTable } from "./people";
import { sbusTable } from "./sbus";
import { usersTable } from "./users";

/**
 * Phase 4b: purchase orders entered in AssetIQ (product owner, 29 Oct 2026; replaced by the Business Central feed
 * later), goods received against them (ST-01), equipment requests (LM-05, HR-02 onboarding), acknowledgements of
 * issued items (HR-03) and exit clearance (HR-04).
 */
export const purchaseOrdersTable = pgTable("purchase_orders", {
  id: serial("id").primaryKey(),
  sbuId: integer("sbu_id").references(() => sbusTable.id).notNull(),
  poNo: text("po_no").notNull().unique(),
  supplier: text("supplier").notNull(),
  status: text("status").notNull().default("open"), // open | partly_received | received | closed
  createdBy: text("created_by").notNull(),
  createdAt: timestamp("created_at").defaultNow().notNull(),
});

export const purchaseOrderLinesTable = pgTable("purchase_order_lines", {
  id: serial("id").primaryKey(),
  purchaseOrderId: integer("purchase_order_id").references(() => purchaseOrdersTable.id).notNull(),
  categoryId: integer("category_id").references(() => assetCategoriesTable.id).notNull(),
  description: text("description").notNull(),
  quantity: integer("quantity").notNull(),
  unitCost: numeric("unit_cost", { precision: 18, scale: 2 }), // Finance only
  receivedQuantity: integer("received_quantity").notNull().default(0),
});

/** ST-01: a delivery counted against a PO; short or damaged deliveries are flagged. */
export const goodsReceiptsTable = pgTable("goods_receipts", {
  id: serial("id").primaryKey(),
  purchaseOrderId: integer("purchase_order_id").references(() => purchaseOrdersTable.id).notNull(),
  receivedBy: text("received_by").notNull(),
  notes: text("notes"),
  flagged: boolean("flagged").notNull().default(false),
  flagReason: text("flag_reason"),
  receivedAt: timestamp("received_at").defaultNow().notNull(),
});

export const goodsReceiptLinesTable = pgTable("goods_receipt_lines", {
  id: serial("id").primaryKey(),
  receiptId: integer("receipt_id").references(() => goodsReceiptsTable.id).notNull(),
  purchaseOrderLineId: integer("purchase_order_line_id").references(() => purchaseOrderLinesTable.id).notNull(),
  quantityReceived: integer("quantity_received").notNull(),
  quantityDamaged: integer("quantity_damaged").notNull().default(0),
  serials: jsonb("serials").notNull().default([]),  // serial numbers counted at the door
  condition: text("condition"),
  registered: integer("registered").notNull().default(0), // items of this line registered as assets so far (ST-02)
});

/** LM-05 / HR-02: a request for equipment; approved through the approvals engine, fulfilled by Stores / IT (ST-04). */
export const equipmentRequestsTable = pgTable("equipment_requests", {
  id: serial("id").primaryKey(),
  sbuId: integer("sbu_id").references(() => sbusTable.id).notNull(),
  employeeId: integer("employee_id").references(() => employeesTable.id),     // who the item is for
  departmentId: integer("department_id").references(() => departmentsTable.id),
  categoryId: integer("category_id").references(() => assetCategoriesTable.id).notNull(),
  itemType: text("item_type").notNull(),            // "Laptop", "Safety boots"
  quantity: integer("quantity").notNull().default(1),
  issuedQuantity: integer("issued_quantity").notNull().default(0),
  reason: text("reason").notNull(),
  urgency: text("urgency").notNull().default("normal"), // low | normal | high
  status: text("status").notNull().default("pending"), // pending | approved | rejected | fulfilled
  source: text("source").notNull().default("request"), // request | onboarding
  requestedBy: text("requested_by").notNull(),
  requestedByUserId: integer("requested_by_user_id").references(() => usersTable.id),
  createdAt: timestamp("created_at").defaultNow().notNull(),
});

/** HR-03: the employee's signature for an item issued to them, in the app or through the emailed link. */
export const assetAcknowledgementsTable = pgTable("asset_acknowledgements", {
  id: serial("id").primaryKey(),
  assetId: integer("asset_id").references(() => assetsTable.id).notNull(),
  employeeId: integer("employee_id").references(() => employeesTable.id).notNull(),
  equipmentRequestId: integer("equipment_request_id").references(() => equipmentRequestsTable.id),
  token: text("token").notNull().unique(),          // the emailed link
  issuedBy: text("issued_by").notNull(),
  issuedAt: timestamp("issued_at").defaultNow().notNull(),
  acknowledgedAt: timestamp("acknowledged_at"),
  method: text("method"),                           // app | email
});

/** HR-04: exit clearance built from the employee's toolbox. */
export const exitClearancesTable = pgTable("exit_clearances", {
  id: serial("id").primaryKey(),
  employeeId: integer("employee_id").references(() => employeesTable.id).notNull(),
  reason: text("reason").notNull(),                 // resignation | termination | retirement | transfer
  status: text("status").notNull().default("open"), // open | completed
  exceptionApprovedBy: text("exception_approved_by"),
  exceptionReason: text("exception_reason"),
  createdBy: text("created_by").notNull(),
  createdAt: timestamp("created_at").defaultNow().notNull(),
  completedAt: timestamp("completed_at"),
});

export const exitClearanceItemsTable = pgTable("exit_clearance_items", {
  id: serial("id").primaryKey(),
  clearanceId: integer("clearance_id").references(() => exitClearancesTable.id).notNull(),
  assetId: integer("asset_id").references(() => assetsTable.id).notNull(),
  outcome: text("outcome").notNull().default("outstanding"), // outstanding | returned | damaged | missing
  recordedBy: text("recorded_by"),
  recordedAt: timestamp("recorded_at"),
  notes: text("notes"),
  // HR-05 (Phase 5): what happened to a missing or damaged item, with who approved it.
  recoveryOutcome: text("recovery_outcome"),           // recovered | written_off | cost_recovery
  recoveryApprovedBy: text("recovery_approved_by"),
  recoveryNote: text("recovery_note"),
  recoveryRecordedBy: text("recovery_recorded_by"),
  recoveryRecordedAt: timestamp("recovery_recorded_at"),
});
