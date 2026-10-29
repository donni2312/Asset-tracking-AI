import { customType, integer, pgTable, serial, text, timestamp } from "drizzle-orm/pg-core";
import { assetsTable } from "./assets";
import { assetUpgradesTable } from "./asset_finance";

const bytea = customType<{ data: Buffer }>({ dataType: () => "bytea" });

/**
 * Files attached to an asset (AU-01 photo, AU-04 / SF-01 invoice, AU-07 upgrade invoice), shown and
 * downloaded on the asset card. Kept in the database so they are backed up with the register and work the
 * same locally and on Supabase. Attachments are evidence for the audit, so the API never deletes them.
 */
export const assetAttachmentsTable = pgTable("asset_attachments", {
  id: serial("id").primaryKey(),
  assetId: integer("asset_id").references(() => assetsTable.id).notNull(),
  upgradeId: integer("upgrade_id").references(() => assetUpgradesTable.id), // set for an upgrade's invoice
  kind: text("kind").notNull(),                 // invoice | photo | other
  fileName: text("file_name").notNull(),
  contentType: text("content_type").notNull(),  // application/pdf | image/jpeg | image/png
  sizeBytes: integer("size_bytes").notNull(),
  sha256: text("sha256").notNull(),
  content: bytea("content").notNull(),
  uploadedBy: text("uploaded_by").notNull(),
  uploadedByRole: text("uploaded_by_role").notNull(),
  uploadedAt: timestamp("uploaded_at").defaultNow().notNull(),
});
