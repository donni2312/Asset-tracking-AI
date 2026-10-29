import { integer, jsonb, numeric, pgTable, serial, text, timestamp, unique } from "drizzle-orm/pg-core";
import { assetsTable } from "./assets";
import { sbusTable } from "./sbus";

/**
 * Upgrades (AU-07): work that increased an asset's value or capacity. SBU IT, the SBU Admin Manager or
 * Stores log it; only SBU Finance of the asset's SBU decides capitalise or expense.
 */
export const assetUpgradesTable = pgTable("asset_upgrades", {
  id: serial("id").primaryKey(),
  assetId: integer("asset_id").references(() => assetsTable.id).notNull(),
  upgradeDate: text("upgrade_date").notNull(),      // YYYY-MM-DD
  description: text("description").notNull(),
  beforeText: text("before_text").notNull(),        // "8 GB"
  afterText: text("after_text").notNull(),          // "16 GB"
  fieldChanges: jsonb("field_changes").notNull().default({}), // template field key → value after the upgrade
  cost: numeric("cost", { precision: 18, scale: 2 }).notNull(),   // USD = invoice_amount × exchange_rate
  currency: text("currency").notNull().default("USD"),             // the invoice's currency, as invoiced
  invoiceAmount: numeric("invoice_amount", { precision: 18, scale: 2 }), // the invoice amount in that currency
  exchangeRate: numeric("exchange_rate", { precision: 18, scale: 6 }).notNull().default("1"), // currency → USD
  supplier: text("supplier"),
  invoiceNo: text("invoice_no"),
  loggedBy: text("logged_by").notNull(),
  loggedByRole: text("logged_by_role").notNull(),
  status: text("status").notNull().default("pending"), // pending | capitalised | expensed | rejected
  decidedBy: text("decided_by"),
  decidedAt: timestamp("decided_at"),
  decisionComment: text("decision_comment"),
  capitalisedPeriod: text("capitalised_period"),    // YYYY-MM: added to cost and depreciated from this month
  revisedLifeMonths: integer("revised_life_months"), // remaining life entered by Finance, counted from capitalisedPeriod
  createdAt: timestamp("created_at").defaultNow().notNull(),
});

/** Monthly depreciation, one row per asset per month (AU-05, SF-03). */
export const depreciationEntriesTable = pgTable("depreciation_entries", {
  id: serial("id").primaryKey(),
  assetId: integer("asset_id").references(() => assetsTable.id).notNull(),
  period: text("period").notNull(),                 // YYYY-MM
  openingCost: numeric("opening_cost", { precision: 18, scale: 2 }).notNull(),
  openingAccumulated: numeric("opening_accumulated", { precision: 18, scale: 2 }).notNull(),
  charge: numeric("charge", { precision: 18, scale: 2 }).notNull(),
  adjustment: numeric("adjustment", { precision: 18, scale: 2 }).notNull(), // catch-up for changes that hit locked months
  closingAccumulated: numeric("closing_accumulated", { precision: 18, scale: 2 }).notNull(),
  closingNbv: numeric("closing_nbv", { precision: 18, scale: 2 }).notNull(),
  createdAt: timestamp("created_at").defaultNow().notNull(),
}, (t) => [unique("depreciation_entries_asset_period_unique").on(t.assetId, t.period)]);

/** A month locked by SBU Finance after review: it and every earlier month can no longer be recalculated. */
export const depreciationLocksTable = pgTable("depreciation_locks", {
  id: serial("id").primaryKey(),
  sbuId: integer("sbu_id").references(() => sbusTable.id).notNull(),
  period: text("period").notNull(),
  lockedBy: text("locked_by").notNull(),
  lockedAt: timestamp("locked_at").defaultNow().notNull(),
}, (t) => [unique("depreciation_locks_sbu_period_unique").on(t.sbuId, t.period)]);
