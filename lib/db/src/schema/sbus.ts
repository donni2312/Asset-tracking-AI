import { pgTable, serial, text, timestamp } from "drizzle-orm/pg-core";
import { createInsertSchema } from "drizzle-zod";
import { z } from "zod/v4";

export const sbusTable = pgTable("sbus", {
  id: serial("id").primaryKey(),
  code: text("code").notNull().unique(), // TSAS | BAK | AGRI | PROP | TSF
  name: text("name").notNull(),          // Strategic Business Unit full name
  description: text("description"),
  headOffice: text("head_office"),        // Physical address
  color: text("color").notNull().default("#6B7280"), // UI badge colour (hex)
  createdAt: timestamp("created_at").defaultNow().notNull(),
  updatedAt: timestamp("updated_at").defaultNow().notNull(),
});

export const insertSbuSchema = createInsertSchema(sbusTable).omit({ id: true, createdAt: true, updatedAt: true });
export type InsertSbu = z.infer<typeof insertSbuSchema>;
export type Sbu = typeof sbusTable.$inferSelect;
