CREATE TABLE "asset_attribute_values" (
	"id" serial PRIMARY KEY NOT NULL,
	"asset_id" integer NOT NULL,
	"field_id" integer NOT NULL,
	"value_text" text NOT NULL,
	"value_number" numeric,
	CONSTRAINT "asset_attribute_values_asset_field_unique" UNIQUE("asset_id","field_id")
);
--> statement-breakpoint
CREATE TABLE "asset_categories" (
	"id" serial PRIMARY KEY NOT NULL,
	"code" text NOT NULL,
	"name" text NOT NULL,
	"is_it" boolean NOT NULL,
	"sub_categories" jsonb DEFAULT '[]'::jsonb NOT NULL,
	"description_pattern" text NOT NULL,
	"identifier_field_key" text,
	"depreciation_method" text NOT NULL,
	"useful_life_months" integer,
	"annual_rate" numeric(9, 6),
	"active" boolean DEFAULT true NOT NULL,
	"created_at" timestamp DEFAULT now() NOT NULL,
	"updated_at" timestamp DEFAULT now() NOT NULL,
	CONSTRAINT "asset_categories_code_unique" UNIQUE("code"),
	CONSTRAINT "asset_categories_name_unique" UNIQUE("name")
);
--> statement-breakpoint
CREATE TABLE "category_template_fields" (
	"id" serial PRIMARY KEY NOT NULL,
	"category_id" integer NOT NULL,
	"key" text NOT NULL,
	"label" text NOT NULL,
	"type" text NOT NULL,
	"unit" text,
	"required" boolean NOT NULL,
	"allowed_values" jsonb,
	"sort_order" integer DEFAULT 0 NOT NULL,
	"created_at" timestamp DEFAULT now() NOT NULL,
	"updated_at" timestamp DEFAULT now() NOT NULL,
	CONSTRAINT "category_template_fields_category_key_unique" UNIQUE("category_id","key")
);
--> statement-breakpoint
CREATE TABLE "group_settings" (
	"key" text PRIMARY KEY NOT NULL,
	"value" jsonb NOT NULL,
	"description" text NOT NULL,
	"editable_by" text NOT NULL,
	"updated_by" text,
	"updated_at" timestamp DEFAULT now() NOT NULL
);
--> statement-breakpoint
CREATE TABLE "asset_upgrades" (
	"id" serial PRIMARY KEY NOT NULL,
	"asset_id" integer NOT NULL,
	"upgrade_date" text NOT NULL,
	"description" text NOT NULL,
	"before_text" text NOT NULL,
	"after_text" text NOT NULL,
	"field_changes" jsonb DEFAULT '{}'::jsonb NOT NULL,
	"cost" numeric(18, 2) NOT NULL,
	"supplier" text,
	"invoice_no" text,
	"logged_by" text NOT NULL,
	"logged_by_role" text NOT NULL,
	"status" text DEFAULT 'pending' NOT NULL,
	"decided_by" text,
	"decided_at" timestamp,
	"decision_comment" text,
	"capitalised_period" text,
	"revised_life_months" integer,
	"created_at" timestamp DEFAULT now() NOT NULL
);
--> statement-breakpoint
CREATE TABLE "depreciation_entries" (
	"id" serial PRIMARY KEY NOT NULL,
	"asset_id" integer NOT NULL,
	"period" text NOT NULL,
	"opening_cost" numeric(18, 2) NOT NULL,
	"opening_accumulated" numeric(18, 2) NOT NULL,
	"charge" numeric(18, 2) NOT NULL,
	"adjustment" numeric(18, 2) NOT NULL,
	"closing_accumulated" numeric(18, 2) NOT NULL,
	"closing_nbv" numeric(18, 2) NOT NULL,
	"created_at" timestamp DEFAULT now() NOT NULL,
	CONSTRAINT "depreciation_entries_asset_period_unique" UNIQUE("asset_id","period")
);
--> statement-breakpoint
CREATE TABLE "depreciation_locks" (
	"id" serial PRIMARY KEY NOT NULL,
	"sbu_id" integer NOT NULL,
	"period" text NOT NULL,
	"locked_by" text NOT NULL,
	"locked_at" timestamp DEFAULT now() NOT NULL,
	CONSTRAINT "depreciation_locks_sbu_period_unique" UNIQUE("sbu_id","period")
);
--> statement-breakpoint
CREATE TABLE "access_log" (
	"id" serial PRIMARY KEY NOT NULL,
	"user_id" integer,
	"username" text NOT NULL,
	"role" text NOT NULL,
	"action" text NOT NULL,
	"detail" text,
	"at" timestamp DEFAULT now() NOT NULL
);
--> statement-breakpoint
CREATE TABLE "audit_log" (
	"id" serial PRIMARY KEY NOT NULL,
	"entity" text NOT NULL,
	"entity_id" integer,
	"asset_id" integer,
	"action" text NOT NULL,
	"field" text,
	"old_value" text,
	"new_value" text,
	"reason" text,
	"user_id" integer,
	"user_name" text NOT NULL,
	"at" timestamp DEFAULT now() NOT NULL
);
--> statement-breakpoint
CREATE TABLE "bc_mock_number_series" (
	"id" serial PRIMARY KEY NOT NULL,
	"prefix" text NOT NULL,
	"last_number" integer NOT NULL,
	CONSTRAINT "bc_mock_number_series_prefix_unique" UNIQUE("prefix")
);
--> statement-breakpoint
CREATE TABLE "bc_number_requests" (
	"id" serial PRIMARY KEY NOT NULL,
	"asset_id" integer NOT NULL,
	"status" text DEFAULT 'queued' NOT NULL,
	"attempts" integer DEFAULT 0 NOT NULL,
	"next_attempt_at" timestamp DEFAULT now() NOT NULL,
	"last_error" text,
	"asset_number" text,
	"created_at" timestamp DEFAULT now() NOT NULL,
	"updated_at" timestamp DEFAULT now() NOT NULL,
	CONSTRAINT "bc_number_requests_asset_id_unique" UNIQUE("asset_id")
);
--> statement-breakpoint
ALTER TABLE "assets" ALTER COLUMN "serial_number" DROP NOT NULL;--> statement-breakpoint
ALTER TABLE "assets" ALTER COLUMN "rfid_tag" DROP NOT NULL;--> statement-breakpoint
ALTER TABLE "users" ADD COLUMN "sbu_id" integer;--> statement-breakpoint
ALTER TABLE "users" ADD COLUMN "access_expires_at" timestamp;--> statement-breakpoint
ALTER TABLE "assets" ADD COLUMN "category_id" integer;--> statement-breakpoint
ALTER TABLE "assets" ADD COLUMN "sub_category" text;--> statement-breakpoint
ALTER TABLE "assets" ADD COLUMN "no_serial_reason" text;--> statement-breakpoint
ALTER TABLE "assets" ADD COLUMN "no_serial_status" text;--> statement-breakpoint
ALTER TABLE "assets" ADD COLUMN "no_serial_decided_by" text;--> statement-breakpoint
ALTER TABLE "assets" ADD COLUMN "no_serial_decided_at" timestamp;--> statement-breakpoint
ALTER TABLE "assets" ADD COLUMN "mac_address" text;--> statement-breakpoint
ALTER TABLE "assets" ADD COLUMN "asset_number" text;--> statement-breakpoint
ALTER TABLE "assets" ADD COLUMN "previous_asset_number" text;--> statement-breakpoint
ALTER TABLE "assets" ADD COLUMN "condition" text;--> statement-breakpoint
ALTER TABLE "assets" ADD COLUMN "original_cost" numeric(18, 2);--> statement-breakpoint
ALTER TABLE "assets" ADD COLUMN "currency" text;--> statement-breakpoint
ALTER TABLE "assets" ADD COLUMN "exchange_rate" numeric(18, 6);--> statement-breakpoint
ALTER TABLE "assets" ADD COLUMN "cost_usd" numeric(18, 2);--> statement-breakpoint
ALTER TABLE "assets" ADD COLUMN "capitalised_at" timestamp;--> statement-breakpoint
ALTER TABLE "assets" ADD COLUMN "capitalised_by" text;--> statement-breakpoint
ALTER TABLE "assets" ADD COLUMN "residual_value" numeric(18, 2) DEFAULT '0' NOT NULL;--> statement-breakpoint
ALTER TABLE "assets" ADD COLUMN "supplier" text;--> statement-breakpoint
ALTER TABLE "assets" ADD COLUMN "invoice_no" text;--> statement-breakpoint
ALTER TABLE "assets" ADD COLUMN "po_no" text;--> statement-breakpoint
ALTER TABLE "assets" ADD COLUMN "in_use_date" text;--> statement-breakpoint
ALTER TABLE "assets" ADD COLUMN "warranty_ends" text;--> statement-breakpoint
ALTER TABLE "assets" ADD COLUMN "disposal_date" text;--> statement-breakpoint
ALTER TABLE "asset_attribute_values" ADD CONSTRAINT "asset_attribute_values_field_id_category_template_fields_id_fk" FOREIGN KEY ("field_id") REFERENCES "public"."category_template_fields"("id") ON DELETE no action ON UPDATE no action;--> statement-breakpoint
ALTER TABLE "category_template_fields" ADD CONSTRAINT "category_template_fields_category_id_asset_categories_id_fk" FOREIGN KEY ("category_id") REFERENCES "public"."asset_categories"("id") ON DELETE no action ON UPDATE no action;--> statement-breakpoint
ALTER TABLE "asset_upgrades" ADD CONSTRAINT "asset_upgrades_asset_id_assets_id_fk" FOREIGN KEY ("asset_id") REFERENCES "public"."assets"("id") ON DELETE no action ON UPDATE no action;--> statement-breakpoint
ALTER TABLE "depreciation_entries" ADD CONSTRAINT "depreciation_entries_asset_id_assets_id_fk" FOREIGN KEY ("asset_id") REFERENCES "public"."assets"("id") ON DELETE no action ON UPDATE no action;--> statement-breakpoint
ALTER TABLE "depreciation_locks" ADD CONSTRAINT "depreciation_locks_sbu_id_sbus_id_fk" FOREIGN KEY ("sbu_id") REFERENCES "public"."sbus"("id") ON DELETE no action ON UPDATE no action;--> statement-breakpoint
ALTER TABLE "users" ADD CONSTRAINT "users_sbu_id_sbus_id_fk" FOREIGN KEY ("sbu_id") REFERENCES "public"."sbus"("id") ON DELETE no action ON UPDATE no action;--> statement-breakpoint
ALTER TABLE "assets" ADD CONSTRAINT "assets_category_id_asset_categories_id_fk" FOREIGN KEY ("category_id") REFERENCES "public"."asset_categories"("id") ON DELETE no action ON UPDATE no action;--> statement-breakpoint
ALTER TABLE "assets" ADD CONSTRAINT "assets_mac_address_unique" UNIQUE("mac_address");--> statement-breakpoint
ALTER TABLE "assets" ADD CONSTRAINT "assets_asset_number_unique" UNIQUE("asset_number");