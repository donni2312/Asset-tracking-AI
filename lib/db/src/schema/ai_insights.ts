import { pgTable, serial, text, timestamp, integer, boolean } from "drizzle-orm/pg-core";
import { createInsertSchema } from "drizzle-zod";
import { z } from "zod/v4";
import { assetsTable } from "./assets";

export const aiInsightsTable = pgTable("ai_insights", {
  id: serial("id").primaryKey(),
  type: text("type").notNull(), // anomaly | idle_asset | unauthorized_movement | missing_asset | maintenance_due | unusual_pattern
  message: text("message").notNull(),
  assetId: integer("asset_id").references(() => assetsTable.id),
  severity: text("severity").notNull().default("info"), // info | warning | critical
  dismissed: boolean("dismissed").notNull().default(false),
  createdAt: timestamp("created_at").defaultNow().notNull(),
});

export const insertAiInsightSchema = createInsertSchema(aiInsightsTable).omit({ id: true, createdAt: true });
export type InsertAiInsight = z.infer<typeof insertAiInsightSchema>;
export type AiInsight = typeof aiInsightsTable.$inferSelect;
