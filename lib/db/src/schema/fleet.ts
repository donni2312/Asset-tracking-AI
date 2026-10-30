import { integer, numeric, pgTable, serial, text, timestamp } from "drizzle-orm/pg-core";
import { assetsTable } from "./assets";
import { employeesTable } from "./people";

/**
 * Phase 4d, the SBU Admin Manager's fleet (AM-02 … AM-04). The vehicle itself is an asset of the Motor Vehicles
 * category (its registration number, VIN and mileage are template fields, AM-01); these tables add what the fleet
 * register needs: the driver and fleet status, the legal documents with their expiry, and service schedules and
 * history (vehicles by mileage, generators by hours run, either by date).
 */
export const fleetDetailsTable = pgTable("fleet_details", {
  id: serial("id").primaryKey(),
  assetId: integer("asset_id").references(() => assetsTable.id).notNull().unique(),
  fleetStatus: text("fleet_status").notNull().default("active"), // active | in_service | off_road
  driverEmployeeId: integer("driver_employee_id").references(() => employeesTable.id),
  driverName: text("driver_name"),                  // a driver without an employee record (contractor)
  updatedBy: text("updated_by").notNull(),
  updatedAt: timestamp("updated_at").defaultNow().notNull(),
});

/** AM-03: licence, insurance, fitness, ZINARA. A renewal adds a row; the latest row per kind is the one in force. */
export const vehicleDocumentsTable = pgTable("vehicle_documents", {
  id: serial("id").primaryKey(),
  assetId: integer("asset_id").references(() => assetsTable.id).notNull(),
  kind: text("kind").notNull(),                     // licence | insurance | fitness | zinara
  reference: text("reference"),                     // policy / certificate / licence number
  expiresOn: text("expires_on").notNull(),          // YYYY-MM-DD
  reminded30At: timestamp("reminded_30_at"),
  reminded7At: timestamp("reminded_7_at"),
  expiredNotifiedAt: timestamp("expired_notified_at"),
  createdBy: text("created_by").notNull(),
  createdAt: timestamp("created_at").defaultNow().notNull(),
});

/** AM-04: the interval; the next service is due from the last one (or the start) plus the interval. */
export const serviceSchedulesTable = pgTable("service_schedules", {
  id: serial("id").primaryKey(),
  assetId: integer("asset_id").references(() => assetsTable.id).notNull().unique(),
  intervalReading: integer("interval_reading"),     // km (vehicles) or hours run (generators)
  intervalDays: integer("interval_days"),
  startDate: text("start_date").notNull(),          // YYYY-MM-DD: the first service is due from here
  startReading: integer("start_reading"),
  overdueNotifiedFor: text("overdue_notified_for"), // the due point already alerted (so the alert goes once)
  updatedBy: text("updated_by").notNull(),
  updatedAt: timestamp("updated_at").defaultNow().notNull(),
});

export const serviceLogsTable = pgTable("service_logs", {
  id: serial("id").primaryKey(),
  assetId: integer("asset_id").references(() => assetsTable.id).notNull(),
  serviceDate: text("service_date").notNull(),      // YYYY-MM-DD
  reading: integer("reading"),                      // mileage or hours at the service
  garage: text("garage").notNull(),
  workDone: text("work_done").notNull(),
  cost: numeric("cost", { precision: 18, scale: 2 }), // entered by SBU Finance (money stays with Finance)
  currency: text("currency"),
  costBy: text("cost_by"),
  loggedBy: text("logged_by").notNull(),
  createdAt: timestamp("created_at").defaultNow().notNull(),
});
