-- Phase 1 (AU-01 … AU-12): reference data, backfill of existing assets, and database-level guards.
-- Hand-written (drizzle-kit generate --custom). Safe to run on a database that already holds assets.

-- reference-data:begin
-- Category templates (AU-01, GI-02, AM-01). Depreciation policy values are placeholders until Group
-- Finance confirms them; Group Finance can change them in the app.
INSERT INTO "asset_categories" ("code", "name", "is_it", "sub_categories", "description_pattern", "identifier_field_key", "depreciation_method", "useful_life_months", "annual_rate") VALUES
  ('COMP', 'Computers & Laptops',       true,  '["Laptop","Desktop"]',                                              '{SubCategory} – {Make} {Model}, {Processor}, {Ram}, {Storage}', 'serial_number', 'straight_line',    48,   NULL),
  ('MOB',  'Mobile Phones',             true,  '["Smartphone","Feature phone","Tablet"]',                           '{SubCategory} – {Make} {Model}, {Storage}',                     'imei',          'straight_line',    36,   NULL),
  ('NET',  'Network & Servers',         true,  '["Server","Network device"]',                                       '{DeviceType} – {Make} {Model}, {PortsCapacity}',                'serial_number', 'straight_line',    60,   NULL),
  ('PER',  'Printers, Projectors & UPS', true, '["Printer","Projector","UPS","Scanner","Monitor"]',                  '{SubCategory} – {Make} {Model}',                                'serial_number', 'straight_line',    48,   NULL),
  ('MV',   'Motor Vehicles',            false, '["Light vehicle","Truck","Bus","Motorcycle","Tractor"]',            '{Make} {Model} {Year} – {RegistrationNumber}',                  'vin',           'reducing_balance', NULL, 0.200000),
  ('PG',   'Plant & Generators',        false, '["Generator","Compressor","Machinery"]',                            '{SubCategory} – {Make} {Model}, {CapacityKva}',                 'serial_number', 'straight_line',    120,  NULL),
  ('FF',   'Furniture & Fittings',      false, '["Desk","Chair","Cabinet","Table","Shelving","Other"]',             '{SubCategory} – {Make} {Model}, {Material}',                    NULL,            'straight_line',    120,  NULL),
  ('EQ',   'Tools & Specialist Equipment', false, '["Lab equipment","Tool","Other"]',                               '{SubCategory} – {Make} {Model}',                                'serial_number', 'straight_line',    60,   NULL)
ON CONFLICT ("code") DO NOTHING;

INSERT INTO "category_template_fields" ("category_id", "key", "label", "type", "unit", "required", "allowed_values", "sort_order")
SELECT c."id", f."key", f."label", f."type", f."unit", f."required", f."allowed_values"::jsonb, f."sort_order"
FROM (VALUES
  ('COMP', 'make',              'Brand',                   'text',    NULL,    true,  NULL, 1),
  ('COMP', 'model',             'Model',                   'text',    NULL,    true,  NULL, 2),
  ('COMP', 'serial_number',     'Serial number',           'text',    NULL,    true,  NULL, 3),
  ('COMP', 'processor',         'Processor',               'text',    NULL,    true,  NULL, 4),
  ('COMP', 'ram',               'RAM',                     'integer', 'GB',    true,  NULL, 5),
  ('COMP', 'storage',           'Storage (ROM/SSD/HDD)',   'text',    NULL,    true,  NULL, 6),
  ('COMP', 'operating_system',  'Operating system',        'text',    NULL,    true,  NULL, 7),
  ('COMP', 'mac_address',       'MAC address',             'text',    NULL,    false, NULL, 8),
  ('MOB',  'make',              'Brand',                   'text',    NULL,    true,  NULL, 1),
  ('MOB',  'model',             'Model',                   'text',    NULL,    true,  NULL, 2),
  ('MOB',  'imei',              'IMEI',                    'text',    NULL,    true,  NULL, 3),
  ('MOB',  'storage',           'Storage',                 'integer', 'GB',    true,  NULL, 4),
  ('MOB',  'phone_number',      'Phone number',            'text',    NULL,    true,  NULL, 5),
  ('NET',  'make',              'Brand',                   'text',    NULL,    true,  NULL, 1),
  ('NET',  'model',             'Model',                   'text',    NULL,    true,  NULL, 2),
  ('NET',  'serial_number',     'Serial number',           'text',    NULL,    true,  NULL, 3),
  ('NET',  'device_type',       'Device type',             'list',    NULL,    true,  '["Server","Switch","Router","Firewall","Access point","Storage"]', 4),
  ('NET',  'ports_capacity',    'Ports or capacity',       'text',    NULL,    true,  NULL, 5),
  ('NET',  'ip_address',        'IP address',              'text',    NULL,    false, NULL, 6),
  ('NET',  'mac_address',       'MAC address',             'text',    NULL,    false, NULL, 7),
  ('PER',  'make',              'Brand',                   'text',    NULL,    true,  NULL, 1),
  ('PER',  'model',             'Model',                   'text',    NULL,    true,  NULL, 2),
  ('PER',  'serial_number',     'Serial number',           'text',    NULL,    true,  NULL, 3),
  ('MV',   'registration_number', 'Registration number',   'text',    NULL,    true,  NULL, 1),
  ('MV',   'vin',               'Chassis number (VIN)',    'text',    NULL,    true,  NULL, 2),
  ('MV',   'engine_number',     'Engine number',           'text',    NULL,    true,  NULL, 3),
  ('MV',   'make',              'Make',                    'text',    NULL,    true,  NULL, 4),
  ('MV',   'model',             'Model',                   'text',    NULL,    true,  NULL, 5),
  ('MV',   'year',              'Year',                    'integer', NULL,    true,  NULL, 6),
  ('MV',   'colour',            'Colour',                  'text',    NULL,    true,  NULL, 7),
  ('MV',   'capacity',          'Capacity (tonnes or seats)', 'text', NULL,    true,  NULL, 8),
  ('MV',   'mileage',           'Mileage',                 'integer', 'km',    true,  NULL, 9),
  ('MV',   'fuel_type',         'Fuel type',               'list',    NULL,    true,  '["Diesel","Petrol","Electric","Hybrid","Gas"]', 10),
  ('PG',   'make',              'Make',                    'text',    NULL,    true,  NULL, 1),
  ('PG',   'model',             'Model',                   'text',    NULL,    true,  NULL, 2),
  ('PG',   'serial_number',     'Serial number',           'text',    NULL,    true,  NULL, 3),
  ('PG',   'capacity_kva',      'Capacity',                'number',  'kVA',   true,  NULL, 4),
  ('PG',   'hours_run',         'Hours run',               'integer', 'h',     true,  NULL, 5),
  ('PG',   'fuel_type',         'Fuel type',               'list',    NULL,    true,  '["Diesel","Petrol","Gas","Electric"]', 6),
  -- ASSUMPTION: AU-01 makes make and model mandatory for every asset, so furniture captures them too
  -- ("Generic" when unbranded); the brief's furniture template (type, material, room) is otherwise kept,
  -- with "type" being the sub-category.
  ('FF',   'make',              'Make / supplier brand',   'text',    NULL,    true,  NULL, 1),
  ('FF',   'model',             'Model / range',           'text',    NULL,    true,  NULL, 2),
  ('FF',   'material',          'Material',                'text',    NULL,    true,  NULL, 3),
  ('FF',   'room',              'Room',                    'text',    NULL,    true,  NULL, 4),
  ('EQ',   'make',              'Make',                    'text',    NULL,    true,  NULL, 1),
  ('EQ',   'model',             'Model',                   'text',    NULL,    true,  NULL, 2),
  ('EQ',   'serial_number',     'Serial number',           'text',    NULL,    true,  NULL, 3)
) AS f("category", "key", "label", "type", "unit", "required", "allowed_values", "sort_order")
JOIN "asset_categories" c ON c."code" = f."category"
ON CONFLICT ON CONSTRAINT "category_template_fields_category_key_unique" DO NOTHING;

-- Group settings: placeholders from the developer brief until the confirmed values are supplied.
INSERT INTO "group_settings" ("key", "value", "description", "editable_by") VALUES
  ('disposal_group_finance_threshold_usd', '1000',  'Disposals with NBV above this (USD) need Group Finance approval', 'group_finance'),
  ('gate_pass_finance_threshold_usd',      '500',   'Gate passes for assets above this value (USD) also need SBU Finance approval', 'group_finance'),
  ('device_refresh_age_years',             '{"laptop":4,"desktop":5,"server":5}', 'Device age that triggers the hardware refresh report', 'group_it'),
  ('warranty_alert_days',                  '60',    'Days before warranty expiry that an alert is sent', 'group_it'),
  ('capex_alert_percents',                 '[80,100]', 'CAPEX budget usage that raises an alert (%)', 'group_finance'),
  ('approval_reminder_hours',              '24',    'Hours before a pending approval gets a reminder', 'group_it'),
  ('approval_escalation_hours',            '48',    'Hours before a pending approval escalates to the next approver', 'group_it'),
  ('depreciation_convention',              '"full_month_from_in_use"', 'Depreciation starts with a full charge in the in-use month', 'group_finance')
ON CONFLICT ("key") DO NOTHING;
-- reference-data:end

-- Backfill existing assets: the old free-text category becomes a sub-category of a template category.
UPDATE "assets" a SET "category_id" = c."id", "sub_category" = m."sub_category"
FROM (VALUES
  ('Laptop', 'COMP', 'Laptop'), ('Desktop', 'COMP', 'Desktop'), ('Phone', 'MOB', 'Smartphone'),
  ('Network Equipment', 'NET', 'Network device'), ('UPS', 'PER', 'UPS'), ('Projector', 'PER', 'Projector'),
  ('Printer', 'PER', 'Printer'), ('Furniture', 'FF', 'Other'), ('Vehicle', 'MV', 'Light vehicle')
) AS m("legacy", "code", "sub_category")
JOIN "asset_categories" c ON c."code" = m."code"
WHERE a."category" = m."legacy" AND a."category_id" IS NULL;
--> statement-breakpoint
UPDATE "assets" SET "category_id" = (SELECT "id" FROM "asset_categories" WHERE "code" = 'EQ'), "sub_category" = 'Other'
WHERE "category_id" IS NULL;
--> statement-breakpoint
-- Existing purchase values become the original cost in USD (rate 1); they are not capitalised until SBU Finance does so.
UPDATE "assets" SET "original_cost" = "purchase_value", "currency" = 'USD', "exchange_rate" = 1
WHERE "purchase_value" IS NOT NULL AND "original_cost" IS NULL;
--> statement-breakpoint
-- Every existing asset asks Business Central for its fixed-asset number (AU-03).
INSERT INTO "bc_number_requests" ("asset_id") SELECT "id" FROM "assets" WHERE "asset_number" IS NULL
ON CONFLICT ("asset_id") DO NOTHING;
--> statement-breakpoint
-- AU-01: an asset without a category cannot be saved.
ALTER TABLE "assets" ALTER COLUMN "category_id" SET NOT NULL;
--> statement-breakpoint
ALTER TABLE "asset_attribute_values" ADD CONSTRAINT "asset_attribute_values_asset_id_assets_id_fk" FOREIGN KEY ("asset_id") REFERENCES "public"."assets"("id") ON DELETE no action ON UPDATE no action;
--> statement-breakpoint

-- AU-06 / GF-07: the audit log is append-only.
CREATE OR REPLACE FUNCTION "assetiq_audit_log_is_append_only"() RETURNS trigger AS $$
BEGIN
  RAISE EXCEPTION 'audit_log is append-only: % is not allowed', TG_OP USING ERRCODE = 'P0001';
END;
$$ LANGUAGE plpgsql;
--> statement-breakpoint
CREATE TRIGGER "audit_log_no_update_or_delete" BEFORE UPDATE OR DELETE ON "audit_log"
  FOR EACH ROW EXECUTE FUNCTION "assetiq_audit_log_is_append_only"();
--> statement-breakpoint
CREATE TRIGGER "access_log_no_update_or_delete" BEFORE UPDATE OR DELETE ON "access_log"
  FOR EACH ROW EXECUTE FUNCTION "assetiq_audit_log_is_append_only"();
--> statement-breakpoint

-- AU-03: an issued asset number can never be edited, and the asset that carries it can never be deleted
-- (so the number can never be reused, even after disposal).
CREATE OR REPLACE FUNCTION "assetiq_asset_number_is_permanent"() RETURNS trigger AS $$
BEGIN
  IF TG_OP = 'DELETE' THEN
    IF OLD."asset_number" IS NOT NULL THEN
      RAISE EXCEPTION 'asset % has asset number % and cannot be deleted', OLD."id", OLD."asset_number" USING ERRCODE = 'P0001';
    END IF;
    RETURN OLD;
  END IF;
  IF OLD."asset_number" IS NOT NULL AND NEW."asset_number" IS DISTINCT FROM OLD."asset_number" THEN
    RAISE EXCEPTION 'asset number % cannot be changed', OLD."asset_number" USING ERRCODE = 'P0001';
  END IF;
  RETURN NEW;
END;
$$ LANGUAGE plpgsql;
--> statement-breakpoint
CREATE TRIGGER "assets_asset_number_is_permanent" BEFORE UPDATE OR DELETE ON "assets"
  FOR EACH ROW EXECUTE FUNCTION "assetiq_asset_number_is_permanent"();
