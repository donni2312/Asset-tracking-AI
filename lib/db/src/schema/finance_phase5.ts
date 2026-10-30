import { integer, numeric, pgTable, serial, text, timestamp, unique } from "drizzle-orm/pg-core";
import { assetCategoriesTable } from "./asset_categories";
import { assetsTable } from "./assets";
import { assetAttachmentsTable } from "./attachments";
import { sbusTable } from "./sbus";

/**
 * Phase 5a. SF-07: revaluations and impairments change the carrying amount from a month on (a signed amount, as a
 * capitalised upgrade adds cost); the depreciation after it is recalculated over the remaining (or revised) life.
 * Each one needs a reason and a supporting document and is written to the audit trail (GF-07).
 */
export const assetRevaluationsTable = pgTable("asset_revaluations", {
  id: serial("id").primaryKey(),
  assetId: integer("asset_id").references(() => assetsTable.id).notNull(),
  kind: text("kind").notNull(),                       // revaluation | impairment
  period: text("period").notNull(),                   // YYYY-MM: takes effect from this month
  amount: numeric("amount", { precision: 18, scale: 2 }).notNull(), // USD change to the carrying amount (negative = down)
  nbvBefore: numeric("nbv_before", { precision: 18, scale: 2 }).notNull(),
  nbvAfter: numeric("nbv_after", { precision: 18, scale: 2 }).notNull(),
  revisedLifeMonths: integer("revised_life_months"),
  reason: text("reason").notNull(),
  attachmentId: integer("attachment_id").references(() => assetAttachmentsTable.id).notNull(),
  recordedBy: text("recorded_by").notNull(),
  createdAt: timestamp("created_at").defaultNow().notNull(),
});

/**
 * SF-09 / GF-05: the CAPEX budget of an SBU per category per financial year (1 November – 31 October, named after
 * the year it ends: FY2027). Spend is what was capitalised in that year; alerts at the capex_alert_percents.
 */
export const capexBudgetsTable = pgTable("capex_budgets", {
  id: serial("id").primaryKey(),
  sbuId: integer("sbu_id").references(() => sbusTable.id).notNull(),
  financialYear: integer("financial_year").notNull(),
  categoryId: integer("category_id").references(() => assetCategoriesTable.id).notNull(),
  amount: numeric("amount", { precision: 18, scale: 2 }).notNull(), // USD
  alertedPercent: integer("alerted_percent"),           // the highest alert threshold already sent
  updatedBy: text("updated_by").notNull(),
  updatedAt: timestamp("updated_at").defaultNow().notNull(),
}, (t) => [unique("capex_budgets_sbu_year_category_unique").on(t.sbuId, t.financialYear, t.categoryId)]);
