CREATE TABLE "gate_actions" (
	"id" serial PRIMARY KEY NOT NULL,
	"insight_id" integer,
	"asset_id" integer NOT NULL,
	"gate_location_id" integer,
	"action" text NOT NULL,
	"notes" text,
	"officer" text NOT NULL,
	"at" timestamp DEFAULT now() NOT NULL
);
--> statement-breakpoint
CREATE TABLE "gate_pass_items" (
	"id" serial PRIMARY KEY NOT NULL,
	"gate_pass_id" integer NOT NULL,
	"asset_id" integer NOT NULL,
	"exited_at" timestamp,
	"exit_gate_id" integer,
	"exit_officer" text,
	"exit_notes" text,
	"returned_at" timestamp,
	"return_gate_id" integer,
	"return_officer" text,
	"return_condition" text,
	"return_notes" text
);
--> statement-breakpoint
CREATE TABLE "verification_results" (
	"id" serial PRIMARY KEY NOT NULL,
	"session_id" integer NOT NULL,
	"asset_id" integer,
	"code" text,
	"result" text NOT NULL,
	"resolution" text,
	"comment" text,
	"resolved_by" text,
	"resolved_at" timestamp
);
--> statement-breakpoint
CREATE TABLE "verification_scans" (
	"id" serial PRIMARY KEY NOT NULL,
	"session_id" integer NOT NULL,
	"code" text NOT NULL,
	"asset_id" integer,
	"scanned_by" text NOT NULL,
	"scanned_at" timestamp DEFAULT now() NOT NULL
);
--> statement-breakpoint
CREATE TABLE "verification_sessions" (
	"id" serial PRIMARY KEY NOT NULL,
	"sbu_id" integer NOT NULL,
	"location_id" integer NOT NULL,
	"scope" text NOT NULL,
	"status" text DEFAULT 'open' NOT NULL,
	"started_by" text NOT NULL,
	"started_at" timestamp DEFAULT now() NOT NULL,
	"closed_at" timestamp
);
--> statement-breakpoint
ALTER TABLE "gate_actions" ADD CONSTRAINT "gate_actions_insight_id_ai_insights_id_fk" FOREIGN KEY ("insight_id") REFERENCES "public"."ai_insights"("id") ON DELETE no action ON UPDATE no action;--> statement-breakpoint
ALTER TABLE "gate_actions" ADD CONSTRAINT "gate_actions_asset_id_assets_id_fk" FOREIGN KEY ("asset_id") REFERENCES "public"."assets"("id") ON DELETE no action ON UPDATE no action;--> statement-breakpoint
ALTER TABLE "gate_actions" ADD CONSTRAINT "gate_actions_gate_location_id_locations_id_fk" FOREIGN KEY ("gate_location_id") REFERENCES "public"."locations"("id") ON DELETE no action ON UPDATE no action;--> statement-breakpoint
ALTER TABLE "gate_pass_items" ADD CONSTRAINT "gate_pass_items_gate_pass_id_gate_passes_id_fk" FOREIGN KEY ("gate_pass_id") REFERENCES "public"."gate_passes"("id") ON DELETE no action ON UPDATE no action;--> statement-breakpoint
ALTER TABLE "gate_pass_items" ADD CONSTRAINT "gate_pass_items_asset_id_assets_id_fk" FOREIGN KEY ("asset_id") REFERENCES "public"."assets"("id") ON DELETE no action ON UPDATE no action;--> statement-breakpoint
ALTER TABLE "gate_pass_items" ADD CONSTRAINT "gate_pass_items_exit_gate_id_locations_id_fk" FOREIGN KEY ("exit_gate_id") REFERENCES "public"."locations"("id") ON DELETE no action ON UPDATE no action;--> statement-breakpoint
ALTER TABLE "gate_pass_items" ADD CONSTRAINT "gate_pass_items_return_gate_id_locations_id_fk" FOREIGN KEY ("return_gate_id") REFERENCES "public"."locations"("id") ON DELETE no action ON UPDATE no action;--> statement-breakpoint
ALTER TABLE "verification_results" ADD CONSTRAINT "verification_results_session_id_verification_sessions_id_fk" FOREIGN KEY ("session_id") REFERENCES "public"."verification_sessions"("id") ON DELETE no action ON UPDATE no action;--> statement-breakpoint
ALTER TABLE "verification_results" ADD CONSTRAINT "verification_results_asset_id_assets_id_fk" FOREIGN KEY ("asset_id") REFERENCES "public"."assets"("id") ON DELETE no action ON UPDATE no action;--> statement-breakpoint
ALTER TABLE "verification_scans" ADD CONSTRAINT "verification_scans_session_id_verification_sessions_id_fk" FOREIGN KEY ("session_id") REFERENCES "public"."verification_sessions"("id") ON DELETE no action ON UPDATE no action;--> statement-breakpoint
ALTER TABLE "verification_scans" ADD CONSTRAINT "verification_scans_asset_id_assets_id_fk" FOREIGN KEY ("asset_id") REFERENCES "public"."assets"("id") ON DELETE no action ON UPDATE no action;--> statement-breakpoint
ALTER TABLE "verification_sessions" ADD CONSTRAINT "verification_sessions_sbu_id_sbus_id_fk" FOREIGN KEY ("sbu_id") REFERENCES "public"."sbus"("id") ON DELETE no action ON UPDATE no action;--> statement-breakpoint
ALTER TABLE "verification_sessions" ADD CONSTRAINT "verification_sessions_location_id_locations_id_fk" FOREIGN KEY ("location_id") REFERENCES "public"."locations"("id") ON DELETE no action ON UPDATE no action;--> statement-breakpoint
-- Every existing gate pass gets its one item; exit = the latest gate_pass movement, return = the first movement back onto the premises after it.
INSERT INTO "gate_pass_items" ("gate_pass_id", "asset_id", "exited_at", "exit_gate_id", "returned_at", "return_gate_id")
SELECT p.id, p.asset_id, x.ts, x.from_id, r.ts, r.to_id
FROM "gate_passes" p
LEFT JOIN LATERAL (
  SELECT m.timestamp AS ts, m.from_location_id AS from_id FROM "movements" m
  WHERE m.gate_pass_id = p.id AND m.triggered_by = 'gate_pass' ORDER BY m.timestamp DESC LIMIT 1
) x ON true
LEFT JOIN LATERAL (
  SELECT m.timestamp AS ts, m.to_location_id AS to_id FROM "movements" m
  WHERE x.ts IS NOT NULL AND m.asset_id = p.asset_id AND m.timestamp > x.ts AND m.to_location_id IS NOT NULL ORDER BY m.timestamp LIMIT 1
) r ON true;
