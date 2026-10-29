import { integer, jsonb, numeric, pgTable, serial, text, timestamp } from "drizzle-orm/pg-core";
import { assetsTable } from "./assets";
import { sbusTable } from "./sbus";
import { usersTable } from "./users";

/**
 * Phase 4 approvals engine: one request per thing to decide (repair, disposal, transfer, gate pass), with one or
 * more steps routed by role, SBU and threshold. A request is approved when every step is; any rejection rejects it.
 * Steps not answered in time get a reminder, then escalate to the next approver (group settings
 * approval_reminder_hours / approval_escalation_hours). Every decision is also written to the audit trail.
 */
export const approvalRequestsTable = pgTable("approval_requests", {
  id: serial("id").primaryKey(),
  kind: text("kind").notNull(),                       // repair | disposal | transfer | gate_pass | equipment
  assetId: integer("asset_id").references(() => assetsTable.id), // null for an equipment request (no asset yet)
  sbuId: integer("sbu_id").references(() => sbusTable.id).notNull(), // the SBU the request belongs to (sending SBU for a transfer)
  subjectId: integer("subject_id"),                   // gate_passes.id / asset_repairs.id / asset_disposals.id
  status: text("status").notNull().default("pending"), // pending | approved | rejected
  summary: text("summary").notNull(),
  details: jsonb("details").notNull().default({}),    // kind-specific: transfer target SBU, reason …
  requestedBy: text("requested_by").notNull(),
  requestedByUserId: integer("requested_by_user_id").references(() => usersTable.id),
  decidedAt: timestamp("decided_at"),
  createdAt: timestamp("created_at").defaultNow().notNull(),
});

export const approvalStepsTable = pgTable("approval_steps", {
  id: serial("id").primaryKey(),
  requestId: integer("request_id").references(() => approvalRequestsTable.id).notNull(),
  stepNo: integer("step_no").notNull(),                // steps with the same number may be decided in any order
  role: text("role").notNull(),                       // the approver role (see ApprovalRouting)
  sbuId: integer("sbu_id").references(() => sbusTable.id), // the approver's SBU, null for Group roles
  departmentId: integer("department_id"),             // HOD steps: the asset's department
  label: text("label").notNull(),                     // "Line manager", "SBU Finance (high-value)", "Group IT data-wipe sign-off"
  status: text("status").notNull().default("pending"), // waiting | pending | approved | rejected
  decidedBy: text("decided_by"),
  decidedByUserId: integer("decided_by_user_id").references(() => usersTable.id),
  onBehalfOf: text("on_behalf_of"),                   // delegation (LM-03): whose step the delegate decided
  decidedAt: timestamp("decided_at"),
  comment: text("comment"),
  remindedAt: timestamp("reminded_at"),
  escalatedToRole: text("escalated_to_role"),
  escalatedAt: timestamp("escalated_at"),
  createdAt: timestamp("created_at").defaultNow().notNull(),
});

/** LM-03: an approver on leave; their steps go to the delegate (or, with none, are treated as unavailable). */
export const delegationsTable = pgTable("delegations", {
  id: serial("id").primaryKey(),
  userId: integer("user_id").references(() => usersTable.id).notNull(),
  delegateUserId: integer("delegate_user_id").references(() => usersTable.id),
  startsAt: timestamp("starts_at").notNull(),
  endsAt: timestamp("ends_at").notNull(),
  reason: text("reason"),
  createdAt: timestamp("created_at").defaultNow().notNull(),
});

/** In-app notifications, one row per recipient. */
export const notificationsTable = pgTable("notifications", {
  id: serial("id").primaryKey(),
  userId: integer("user_id").references(() => usersTable.id).notNull(),
  kind: text("kind").notNull(),                       // approval | reminder | escalation | decision | transfer | acknowledgement …
  title: text("title").notNull(),
  body: text("body").notNull(),
  link: text("link"),
  readAt: timestamp("read_at"),
  createdAt: timestamp("created_at").defaultNow().notNull(),
});

/** Emails the app would send. Until Microsoft 365 / SMTP is connected they are logged here, not sent. */
export const outboundEmailsTable = pgTable("outbound_emails", {
  id: serial("id").primaryKey(),
  userId: integer("user_id").references(() => usersTable.id),
  toAddress: text("to_address"),
  subject: text("subject").notNull(),
  body: text("body").notNull(),
  status: text("status").notNull().default("logged"), // logged | sent | failed
  createdAt: timestamp("created_at").defaultNow().notNull(),
});

/** SI-05: a fault and its repair. IT records the fault and the work; the cost is recorded by Finance only. */
export const assetRepairsTable = pgTable("asset_repairs", {
  id: serial("id").primaryKey(),
  assetId: integer("asset_id").references(() => assetsTable.id).notNull(),
  fault: text("fault").notNull(),
  action: text("action"),                             // the work proposed / done
  parts: text("parts"),
  vendor: text("vendor"),
  downtimeDays: integer("downtime_days"),
  cost: numeric("cost", { precision: 18, scale: 2 }), // USD, entered by Finance (approval or completion)
  status: text("status").notNull().default("requested"), // requested | approved | rejected | completed
  previousStatus: text("previous_status").notNull(),  // where the asset goes back to if the repair is rejected
  requestedBy: text("requested_by").notNull(),
  completedAt: timestamp("completed_at"),
  createdAt: timestamp("created_at").defaultNow().notNull(),
});

/** SI-08 / SF-04 / GF-04 / GI-10: a recommended disposal and how it was completed. */
export const assetDisposalsTable = pgTable("asset_disposals", {
  id: serial("id").primaryKey(),
  assetId: integer("asset_id").references(() => assetsTable.id).notNull(),
  reason: text("reason").notNull(),
  condition: text("condition"),
  recommendedBy: text("recommended_by").notNull(),
  previousStatus: text("previous_status").notNull(),
  method: text("method"),                             // sale | scrap | donation | write_off (set by SBU Finance)
  disposalDate: text("disposal_date"),
  proceeds: numeric("proceeds", { precision: 18, scale: 2 }),
  buyer: text("buyer"),
  nbvAtDisposal: numeric("nbv_at_disposal", { precision: 18, scale: 2 }),
  gainLoss: numeric("gain_loss", { precision: 18, scale: 2 }), // proceeds − NBV at disposal
  dataWipeNote: text("data_wipe_note"),               // GI-10, IT assets: recorded at Group IT's sign-off
  status: text("status").notNull().default("pending"), // pending | completed | rejected
  createdAt: timestamp("created_at").defaultNow().notNull(),
});
