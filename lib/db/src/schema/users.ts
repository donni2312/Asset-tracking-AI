import { boolean, integer, pgTable, serial, text, timestamp } from "drizzle-orm/pg-core";
import { sbusTable } from "./sbus";
import { employeesTable } from "./people";
import { createInsertSchema } from "drizzle-zod";
import { z } from "zod/v4";

/**
 * Dashboard users. Authentication is bearer-token based; only the password *hash* is stored.
 *
 * Roles (Phase 2, docs/user-stories.md Permissions matrix; the API's Domain/Values.cs `Roles` is the
 * source of truth): the Group roles act across SBUs, the SBU roles on `sbu_id` only. The original
 * viewer / operator / admin roles were retired by migration 0004.
 */
export const USER_ROLES = [
  "group_finance", "group_it", "sbu_it", "sbu_finance", "gate_officer", "hod", "sbu_hr", "stores", "sbu_admin",
  "auditor", "employee", "system_admin",
] as const;
export type UserRole = (typeof USER_ROLES)[number];

export const usersTable = pgTable("users", {
  id: serial("id").primaryKey(),
  username: text("username").notNull().unique(),
  name: text("name").notNull(),                       // display name, recorded on decisions
  role: text("role").notNull().default("employee"),   // one of USER_ROLES
  sbuId: integer("sbu_id").references(() => sbusTable.id),   // SBU roles act on this SBU only
  employeeId: integer("employee_id").references(() => employeesTable.id).unique(), // HOD / SBU HR / Employee: who the account belongs to
  accessExpiresAt: timestamp("access_expires_at"),           // auditors: set by Group Finance; refused afterwards
  passwordHash: text("password_hash").notNull(),      // scrypt, never the password itself
  active: boolean("active").notNull().default(true),  // disabled accounts cannot sign in
  lastLoginAt: timestamp("last_login_at"),
  createdAt: timestamp("created_at").defaultNow().notNull(),
  updatedAt: timestamp("updated_at").defaultNow().notNull(),
});

export const insertUserSchema = createInsertSchema(usersTable).omit({ id: true, createdAt: true, updatedAt: true });
export type InsertUser = z.infer<typeof insertUserSchema>;
export type User = typeof usersTable.$inferSelect;
