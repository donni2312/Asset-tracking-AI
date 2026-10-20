import { boolean, pgTable, serial, text, timestamp } from "drizzle-orm/pg-core";
import { createInsertSchema } from "drizzle-zod";
import { z } from "zod/v4";

/**
 * Dashboard users. Authentication is bearer-token based (see
 * artifacts/api-server/src/lib/auth.ts); only the password *hash* is stored.
 *
 * Roles:
 *  - viewer   read only
 *  - operator day-to-day operations: movements, gate-pass decisions, and the
 *             operational fields of an asset (status, holder, location, notes)
 *  - admin    also edits and registers asset records (name, category, SBU) and
 *             may delete them
 */
export const USER_ROLES = ["viewer", "operator", "admin"] as const;
export type UserRole = (typeof USER_ROLES)[number];

export const usersTable = pgTable("users", {
  id: serial("id").primaryKey(),
  username: text("username").notNull().unique(),
  name: text("name").notNull(),                       // display name, recorded on decisions
  role: text("role").notNull().default("viewer"),     // viewer | operator | admin
  passwordHash: text("password_hash").notNull(),      // scrypt, never the password itself
  active: boolean("active").notNull().default(true),  // disabled accounts cannot sign in
  lastLoginAt: timestamp("last_login_at"),
  createdAt: timestamp("created_at").defaultNow().notNull(),
  updatedAt: timestamp("updated_at").defaultNow().notNull(),
});

export const insertUserSchema = createInsertSchema(usersTable).omit({ id: true, createdAt: true, updatedAt: true });
export type InsertUser = z.infer<typeof insertUserSchema>;
export type User = typeof usersTable.$inferSelect;
