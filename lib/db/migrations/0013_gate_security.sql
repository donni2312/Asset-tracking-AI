CREATE TABLE "gate_incidents" (
	"id" serial PRIMARY KEY NOT NULL,
	"sbu_id" integer NOT NULL,
	"gate_location_id" integer,
	"kind" text NOT NULL,
	"asset_id" integer,
	"gate_pass_id" integer,
	"code" text,
	"notes" text NOT NULL,
	"photo" "bytea",
	"photo_content_type" text,
	"officer" text NOT NULL,
	"created_at" timestamp DEFAULT now() NOT NULL
);
--> statement-breakpoint
CREATE TABLE "security_actions" (
	"id" serial PRIMARY KEY NOT NULL,
	"asset_id" integer NOT NULL,
	"action" text NOT NULL,
	"reference" text,
	"notes" text,
	"done_by" text NOT NULL,
	"done_at" timestamp DEFAULT now() NOT NULL,
	CONSTRAINT "security_actions_asset_action_unique" UNIQUE("asset_id","action")
);
--> statement-breakpoint
CREATE TABLE "visitor_items" (
	"id" serial PRIMARY KEY NOT NULL,
	"sbu_id" integer NOT NULL,
	"gate_location_id" integer NOT NULL,
	"owner_name" text NOT NULL,
	"owner_company" text,
	"item" text NOT NULL,
	"serial_number" text,
	"photo" "bytea",
	"photo_content_type" text,
	"checked_in_by" text NOT NULL,
	"checked_in_at" timestamp DEFAULT now() NOT NULL,
	"checked_out_by" text,
	"checked_out_at" timestamp,
	"checked_out_gate_id" integer
);
--> statement-breakpoint
ALTER TABLE "users" ADD COLUMN "last_digest_on" text;--> statement-breakpoint
ALTER TABLE "gate_incidents" ADD CONSTRAINT "gate_incidents_sbu_id_sbus_id_fk" FOREIGN KEY ("sbu_id") REFERENCES "public"."sbus"("id") ON DELETE no action ON UPDATE no action;--> statement-breakpoint
ALTER TABLE "gate_incidents" ADD CONSTRAINT "gate_incidents_gate_location_id_locations_id_fk" FOREIGN KEY ("gate_location_id") REFERENCES "public"."locations"("id") ON DELETE no action ON UPDATE no action;--> statement-breakpoint
ALTER TABLE "gate_incidents" ADD CONSTRAINT "gate_incidents_asset_id_assets_id_fk" FOREIGN KEY ("asset_id") REFERENCES "public"."assets"("id") ON DELETE no action ON UPDATE no action;--> statement-breakpoint
ALTER TABLE "gate_incidents" ADD CONSTRAINT "gate_incidents_gate_pass_id_gate_passes_id_fk" FOREIGN KEY ("gate_pass_id") REFERENCES "public"."gate_passes"("id") ON DELETE no action ON UPDATE no action;--> statement-breakpoint
ALTER TABLE "security_actions" ADD CONSTRAINT "security_actions_asset_id_assets_id_fk" FOREIGN KEY ("asset_id") REFERENCES "public"."assets"("id") ON DELETE no action ON UPDATE no action;--> statement-breakpoint
ALTER TABLE "visitor_items" ADD CONSTRAINT "visitor_items_sbu_id_sbus_id_fk" FOREIGN KEY ("sbu_id") REFERENCES "public"."sbus"("id") ON DELETE no action ON UPDATE no action;--> statement-breakpoint
ALTER TABLE "visitor_items" ADD CONSTRAINT "visitor_items_gate_location_id_locations_id_fk" FOREIGN KEY ("gate_location_id") REFERENCES "public"."locations"("id") ON DELETE no action ON UPDATE no action;--> statement-breakpoint
ALTER TABLE "visitor_items" ADD CONSTRAINT "visitor_items_checked_out_gate_id_locations_id_fk" FOREIGN KEY ("checked_out_gate_id") REFERENCES "public"."locations"("id") ON DELETE no action ON UPDATE no action;