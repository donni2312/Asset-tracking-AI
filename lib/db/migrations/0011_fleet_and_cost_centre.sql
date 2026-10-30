CREATE TABLE "fleet_details" (
	"id" serial PRIMARY KEY NOT NULL,
	"asset_id" integer NOT NULL,
	"fleet_status" text DEFAULT 'active' NOT NULL,
	"driver_employee_id" integer,
	"driver_name" text,
	"updated_by" text NOT NULL,
	"updated_at" timestamp DEFAULT now() NOT NULL,
	CONSTRAINT "fleet_details_asset_id_unique" UNIQUE("asset_id")
);
--> statement-breakpoint
CREATE TABLE "service_logs" (
	"id" serial PRIMARY KEY NOT NULL,
	"asset_id" integer NOT NULL,
	"service_date" text NOT NULL,
	"reading" integer,
	"garage" text NOT NULL,
	"work_done" text NOT NULL,
	"cost" numeric(18, 2),
	"currency" text,
	"cost_by" text,
	"logged_by" text NOT NULL,
	"created_at" timestamp DEFAULT now() NOT NULL
);
--> statement-breakpoint
CREATE TABLE "service_schedules" (
	"id" serial PRIMARY KEY NOT NULL,
	"asset_id" integer NOT NULL,
	"interval_reading" integer,
	"interval_days" integer,
	"start_date" text NOT NULL,
	"start_reading" integer,
	"overdue_notified_for" text,
	"updated_by" text NOT NULL,
	"updated_at" timestamp DEFAULT now() NOT NULL,
	CONSTRAINT "service_schedules_asset_id_unique" UNIQUE("asset_id")
);
--> statement-breakpoint
CREATE TABLE "vehicle_documents" (
	"id" serial PRIMARY KEY NOT NULL,
	"asset_id" integer NOT NULL,
	"kind" text NOT NULL,
	"reference" text,
	"expires_on" text NOT NULL,
	"reminded_30_at" timestamp,
	"reminded_7_at" timestamp,
	"expired_notified_at" timestamp,
	"created_by" text NOT NULL,
	"created_at" timestamp DEFAULT now() NOT NULL
);
--> statement-breakpoint
ALTER TABLE "assets" ADD COLUMN "cost_centre" text;--> statement-breakpoint
ALTER TABLE "fleet_details" ADD CONSTRAINT "fleet_details_asset_id_assets_id_fk" FOREIGN KEY ("asset_id") REFERENCES "public"."assets"("id") ON DELETE no action ON UPDATE no action;--> statement-breakpoint
ALTER TABLE "fleet_details" ADD CONSTRAINT "fleet_details_driver_employee_id_employees_id_fk" FOREIGN KEY ("driver_employee_id") REFERENCES "public"."employees"("id") ON DELETE no action ON UPDATE no action;--> statement-breakpoint
ALTER TABLE "service_logs" ADD CONSTRAINT "service_logs_asset_id_assets_id_fk" FOREIGN KEY ("asset_id") REFERENCES "public"."assets"("id") ON DELETE no action ON UPDATE no action;--> statement-breakpoint
ALTER TABLE "service_schedules" ADD CONSTRAINT "service_schedules_asset_id_assets_id_fk" FOREIGN KEY ("asset_id") REFERENCES "public"."assets"("id") ON DELETE no action ON UPDATE no action;--> statement-breakpoint
ALTER TABLE "vehicle_documents" ADD CONSTRAINT "vehicle_documents_asset_id_assets_id_fk" FOREIGN KEY ("asset_id") REFERENCES "public"."assets"("id") ON DELETE no action ON UPDATE no action;