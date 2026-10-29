import { boolean, integer, pgTable, serial, text, timestamp, unique, type AnyPgColumn } from "drizzle-orm/pg-core";
import { sbusTable } from "./sbus";

/**
 * Departments and employees (Phase 2): the people the HOD, SBU HR and Employee scopes are built on. Each
 * SBU has its own departments and employees; a user account can be linked to the employee it belongs to.
 * The toolbox workflows (HR-01 … HR-06) build on these in Phase 4.
 */
export const departmentsTable = pgTable("departments", {
  id: serial("id").primaryKey(),
  sbuId: integer("sbu_id").references(() => sbusTable.id).notNull(),
  name: text("name").notNull(),
  createdAt: timestamp("created_at").defaultNow().notNull(),
  updatedAt: timestamp("updated_at").defaultNow().notNull(),
}, (t) => [unique("departments_sbu_name_unique").on(t.sbuId, t.name)]);

export const employeesTable = pgTable("employees", {
  id: serial("id").primaryKey(),
  sbuId: integer("sbu_id").references(() => sbusTable.id).notNull(),
  departmentId: integer("department_id").references(() => departmentsTable.id),
  employeeNo: text("employee_no").notNull().unique(),     // HR number, e.g. TSAS-0042
  name: text("name").notNull(),
  jobRole: text("job_role"),                               // Driver, Accountant, Technician (HR-02 templates)
  lineManagerId: integer("line_manager_id").references((): AnyPgColumn => employeesTable.id),
  email: text("email"),
  phone: text("phone"),
  active: boolean("active").notNull().default(true),       // false once exit clearance completes
  createdAt: timestamp("created_at").defaultNow().notNull(),
  updatedAt: timestamp("updated_at").defaultNow().notNull(),
});
