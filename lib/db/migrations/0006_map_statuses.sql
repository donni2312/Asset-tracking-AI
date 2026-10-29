-- Phase 3: the original statuses (active / maintenance / retired / missing) become the state machine's statuses.
-- active splits by where the asset is: off premises after a gate pass exit → out_on_gate_pass; held by an employee,
-- a department or a named person → in_use; otherwise (a store, or nobody) → in_store. scripts/src/seed.ts runs the
-- same mapping on its demo data.
UPDATE "assets" a SET "status" = CASE
  WHEN a."status" = 'maintenance' THEN 'in_repair'
  WHEN a."status" = 'retired' THEN 'disposed'
  WHEN a."status" = 'missing' THEN 'lost_stolen'
  WHEN a."status" = 'active' AND a."current_location_id" IS NULL AND EXISTS (
    SELECT 1 FROM "movements" m WHERE m."asset_id" = a."id" AND m."triggered_by" = 'gate_pass' AND m."to_location_id" IS NULL
  ) THEN 'out_on_gate_pass'
  WHEN a."status" = 'active' AND (a."custodian_employee_id" IS NOT NULL OR a."department_id" IS NOT NULL
    OR (a."assigned_to" IS NOT NULL AND a."assigned_to" NOT ILIKE '%store%')) THEN 'in_use'
  WHEN a."status" = 'active' THEN 'in_store'
  ELSE a."status"
END, "updated_at" = now()
WHERE a."status" IN ('active', 'maintenance', 'retired', 'missing');
--> statement-breakpoint
-- GI-07: a lost / stolen asset records when, by whom and what happened; assets marked missing earlier get a placeholder.
UPDATE "assets" SET "lost_date" = to_char("updated_at", 'YYYY-MM-DD'), "lost_reported_by" = 'Recorded before Phase 3',
  "lost_notes" = 'Marked missing before the Phase 3 statuses; complete the incident details.'
WHERE "status" = 'lost_stolen' AND "lost_date" IS NULL;
--> statement-breakpoint
UPDATE "assets" SET "disposal_date" = to_char("updated_at", 'YYYY-MM-DD') WHERE "status" = 'disposed' AND "disposal_date" IS NULL;
