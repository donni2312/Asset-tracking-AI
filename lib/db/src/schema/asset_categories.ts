import { boolean, integer, jsonb, numeric, pgTable, serial, text, timestamp, unique } from "drizzle-orm/pg-core";

/**
 * Asset categories and their templates (AU-01, GI-02, AM-01, GF-03). Group Finance owns the categories and
 * their depreciation policy; Group IT owns the fields of IT categories and the SBU Admin Manager those of
 * non-IT categories. The registration form, the server-side validation and the generated description all
 * come from these rows: nothing about a category is hard-coded.
 */
export const assetCategoriesTable = pgTable("asset_categories", {
  id: serial("id").primaryKey(),
  code: text("code").notNull().unique(),                   // short code used in asset numbers (IT, MV, FF, …)
  name: text("name").notNull().unique(),
  isIt: boolean("is_it").notNull(),                        // IT category (SBU IT / Group IT) vs non-IT (SBU Admin Manager)
  subCategories: jsonb("sub_categories").notNull().default([]), // allowed sub-category names, e.g. ["Laptop","Desktop"]
  // e.g. "{SubCategory} – {Make} {Model}, {Processor}, {Ram}, {Storage}": tokens are field keys in PascalCase.
  descriptionPattern: text("description_pattern").notNull(),
  // Field that identifies the item for AU-02 (serial_number, vin, imei); null = the asset number is the identifier.
  identifierFieldKey: text("identifier_field_key"),
  depreciationMethod: text("depreciation_method").notNull(), // straight_line | reducing_balance
  usefulLifeMonths: integer("useful_life_months"),           // straight-line
  annualRate: numeric("annual_rate", { precision: 9, scale: 6 }), // reducing-balance, e.g. 0.250000
  // Residual value as a % of cost, proposed when an asset is capitalised; 0 until Finance decides otherwise.
  defaultResidualPercent: numeric("default_residual_percent", { precision: 5, scale: 2 }).notNull().default("0"),
  active: boolean("active").notNull().default(true),
  createdAt: timestamp("created_at").defaultNow().notNull(),
  updatedAt: timestamp("updated_at").defaultNow().notNull(),
});

export const categoryTemplateFieldsTable = pgTable("category_template_fields", {
  id: serial("id").primaryKey(),
  categoryId: integer("category_id").references(() => assetCategoriesTable.id).notNull(),
  key: text("key").notNull(),            // snake_case, e.g. ram, registration_number
  label: text("label").notNull(),
  type: text("type").notNull(),          // text | number | integer | date | list
  unit: text("unit"),
  required: boolean("required").notNull(),
  allowedValues: jsonb("allowed_values"), // list fields: ["Petrol","Diesel"]
  sortOrder: integer("sort_order").notNull().default(0),
  createdAt: timestamp("created_at").defaultNow().notNull(),
  updatedAt: timestamp("updated_at").defaultNow().notNull(),
}, (t) => [unique("category_template_fields_category_key_unique").on(t.categoryId, t.key)]);

/** One typed value per asset per template field, so values can be filtered and reported on per category. */
export const assetAttributeValuesTable = pgTable("asset_attribute_values", {
  id: serial("id").primaryKey(),
  assetId: integer("asset_id").notNull(),
  fieldId: integer("field_id").references(() => categoryTemplateFieldsTable.id).notNull(),
  valueText: text("value_text").notNull(),          // the value as entered (always set)
  valueNumber: numeric("value_number"),             // number / integer fields, for filtering and sorting
}, (t) => [unique("asset_attribute_values_asset_field_unique").on(t.assetId, t.fieldId)]);

/** Group settings editable by Group Finance / Group IT (thresholds, alert days, depreciation convention). */
export const groupSettingsTable = pgTable("group_settings", {
  key: text("key").primaryKey(),
  value: jsonb("value").notNull(),
  description: text("description").notNull(),
  editableBy: text("editable_by").notNull(),        // group_finance | group_it
  updatedBy: text("updated_by"),
  updatedAt: timestamp("updated_at").defaultNow().notNull(),
});
