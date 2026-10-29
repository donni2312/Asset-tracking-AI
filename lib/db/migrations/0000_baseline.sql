CREATE TABLE "users" (
	"id" serial PRIMARY KEY NOT NULL,
	"username" text NOT NULL,
	"name" text NOT NULL,
	"role" text DEFAULT 'viewer' NOT NULL,
	"password_hash" text NOT NULL,
	"active" boolean DEFAULT true NOT NULL,
	"last_login_at" timestamp,
	"created_at" timestamp DEFAULT now() NOT NULL,
	"updated_at" timestamp DEFAULT now() NOT NULL,
	CONSTRAINT "users_username_unique" UNIQUE("username")
);
--> statement-breakpoint
CREATE TABLE "sbus" (
	"id" serial PRIMARY KEY NOT NULL,
	"code" text NOT NULL,
	"name" text NOT NULL,
	"description" text,
	"head_office" text,
	"color" text DEFAULT '#6B7280' NOT NULL,
	"created_at" timestamp DEFAULT now() NOT NULL,
	"updated_at" timestamp DEFAULT now() NOT NULL,
	CONSTRAINT "sbus_code_unique" UNIQUE("code")
);
--> statement-breakpoint
CREATE TABLE "locations" (
	"id" serial PRIMARY KEY NOT NULL,
	"name" text NOT NULL,
	"type" text NOT NULL,
	"building" text NOT NULL,
	"floor" text,
	"description" text,
	"sbu_id" integer,
	"created_at" timestamp DEFAULT now() NOT NULL,
	"updated_at" timestamp DEFAULT now() NOT NULL
);
--> statement-breakpoint
CREATE TABLE "rfid_readers" (
	"id" serial PRIMARY KEY NOT NULL,
	"name" text NOT NULL,
	"location_id" integer NOT NULL,
	"status" text DEFAULT 'active' NOT NULL,
	"last_read_at" timestamp,
	"created_at" timestamp DEFAULT now() NOT NULL
);
--> statement-breakpoint
CREATE TABLE "assets" (
	"id" serial PRIMARY KEY NOT NULL,
	"name" text NOT NULL,
	"category" text NOT NULL,
	"serial_number" text NOT NULL,
	"rfid_tag" text NOT NULL,
	"status" text DEFAULT 'active' NOT NULL,
	"assigned_to" text,
	"current_location_id" integer,
	"sbu_id" integer,
	"purchase_date" text,
	"purchase_value" numeric,
	"notes" text,
	"created_at" timestamp DEFAULT now() NOT NULL,
	"updated_at" timestamp DEFAULT now() NOT NULL,
	CONSTRAINT "assets_serial_number_unique" UNIQUE("serial_number"),
	CONSTRAINT "assets_rfid_tag_unique" UNIQUE("rfid_tag")
);
--> statement-breakpoint
CREATE TABLE "movements" (
	"id" serial PRIMARY KEY NOT NULL,
	"asset_id" integer NOT NULL,
	"from_location_id" integer,
	"to_location_id" integer,
	"triggered_by" text DEFAULT 'manual' NOT NULL,
	"rfid_reader_id" integer,
	"gate_pass_id" integer,
	"moved_by" text,
	"notes" text,
	"timestamp" timestamp DEFAULT now() NOT NULL
);
--> statement-breakpoint
CREATE TABLE "gate_passes" (
	"id" serial PRIMARY KEY NOT NULL,
	"asset_id" integer NOT NULL,
	"requested_by" text NOT NULL,
	"approved_by" text,
	"status" text DEFAULT 'pending' NOT NULL,
	"reason" text NOT NULL,
	"destination" text,
	"return_date" text,
	"decision_note" text,
	"source" text DEFAULT 'manual' NOT NULL,
	"gate_location_id" integer,
	"decided_at" timestamp,
	"created_at" timestamp DEFAULT now() NOT NULL,
	"updated_at" timestamp DEFAULT now() NOT NULL
);
--> statement-breakpoint
CREATE TABLE "ai_insights" (
	"id" serial PRIMARY KEY NOT NULL,
	"type" text NOT NULL,
	"message" text NOT NULL,
	"asset_id" integer,
	"gate_pass_id" integer,
	"severity" text DEFAULT 'info' NOT NULL,
	"dismissed" boolean DEFAULT false NOT NULL,
	"created_at" timestamp DEFAULT now() NOT NULL
);
--> statement-breakpoint
CREATE TABLE "asset_lifecycle_events" (
	"id" serial PRIMARY KEY NOT NULL,
	"asset_id" integer NOT NULL,
	"event_type" text NOT NULL,
	"performed_by" text NOT NULL,
	"from_sbu_id" integer,
	"to_sbu_id" integer,
	"assigned_to" text,
	"vendor" text,
	"ticket_ref" text,
	"condition_before" text,
	"condition_after" text,
	"notes" text,
	"timestamp" timestamp DEFAULT now() NOT NULL
);
--> statement-breakpoint
ALTER TABLE "locations" ADD CONSTRAINT "locations_sbu_id_sbus_id_fk" FOREIGN KEY ("sbu_id") REFERENCES "public"."sbus"("id") ON DELETE no action ON UPDATE no action;--> statement-breakpoint
ALTER TABLE "rfid_readers" ADD CONSTRAINT "rfid_readers_location_id_locations_id_fk" FOREIGN KEY ("location_id") REFERENCES "public"."locations"("id") ON DELETE no action ON UPDATE no action;--> statement-breakpoint
ALTER TABLE "assets" ADD CONSTRAINT "assets_current_location_id_locations_id_fk" FOREIGN KEY ("current_location_id") REFERENCES "public"."locations"("id") ON DELETE no action ON UPDATE no action;--> statement-breakpoint
ALTER TABLE "assets" ADD CONSTRAINT "assets_sbu_id_sbus_id_fk" FOREIGN KEY ("sbu_id") REFERENCES "public"."sbus"("id") ON DELETE no action ON UPDATE no action;--> statement-breakpoint
ALTER TABLE "movements" ADD CONSTRAINT "movements_asset_id_assets_id_fk" FOREIGN KEY ("asset_id") REFERENCES "public"."assets"("id") ON DELETE no action ON UPDATE no action;--> statement-breakpoint
ALTER TABLE "movements" ADD CONSTRAINT "movements_from_location_id_locations_id_fk" FOREIGN KEY ("from_location_id") REFERENCES "public"."locations"("id") ON DELETE no action ON UPDATE no action;--> statement-breakpoint
ALTER TABLE "movements" ADD CONSTRAINT "movements_to_location_id_locations_id_fk" FOREIGN KEY ("to_location_id") REFERENCES "public"."locations"("id") ON DELETE no action ON UPDATE no action;--> statement-breakpoint
ALTER TABLE "movements" ADD CONSTRAINT "movements_rfid_reader_id_rfid_readers_id_fk" FOREIGN KEY ("rfid_reader_id") REFERENCES "public"."rfid_readers"("id") ON DELETE no action ON UPDATE no action;--> statement-breakpoint
ALTER TABLE "movements" ADD CONSTRAINT "movements_gate_pass_id_gate_passes_id_fk" FOREIGN KEY ("gate_pass_id") REFERENCES "public"."gate_passes"("id") ON DELETE no action ON UPDATE no action;--> statement-breakpoint
ALTER TABLE "gate_passes" ADD CONSTRAINT "gate_passes_asset_id_assets_id_fk" FOREIGN KEY ("asset_id") REFERENCES "public"."assets"("id") ON DELETE no action ON UPDATE no action;--> statement-breakpoint
ALTER TABLE "gate_passes" ADD CONSTRAINT "gate_passes_gate_location_id_locations_id_fk" FOREIGN KEY ("gate_location_id") REFERENCES "public"."locations"("id") ON DELETE no action ON UPDATE no action;--> statement-breakpoint
ALTER TABLE "ai_insights" ADD CONSTRAINT "ai_insights_asset_id_assets_id_fk" FOREIGN KEY ("asset_id") REFERENCES "public"."assets"("id") ON DELETE no action ON UPDATE no action;--> statement-breakpoint
ALTER TABLE "ai_insights" ADD CONSTRAINT "ai_insights_gate_pass_id_gate_passes_id_fk" FOREIGN KEY ("gate_pass_id") REFERENCES "public"."gate_passes"("id") ON DELETE no action ON UPDATE no action;--> statement-breakpoint
ALTER TABLE "asset_lifecycle_events" ADD CONSTRAINT "asset_lifecycle_events_asset_id_assets_id_fk" FOREIGN KEY ("asset_id") REFERENCES "public"."assets"("id") ON DELETE no action ON UPDATE no action;--> statement-breakpoint
ALTER TABLE "asset_lifecycle_events" ADD CONSTRAINT "asset_lifecycle_events_from_sbu_id_sbus_id_fk" FOREIGN KEY ("from_sbu_id") REFERENCES "public"."sbus"("id") ON DELETE no action ON UPDATE no action;--> statement-breakpoint
ALTER TABLE "asset_lifecycle_events" ADD CONSTRAINT "asset_lifecycle_events_to_sbu_id_sbus_id_fk" FOREIGN KEY ("to_sbu_id") REFERENCES "public"."sbus"("id") ON DELETE no action ON UPDATE no action;