CREATE TABLE "departments" (
	"id" serial PRIMARY KEY NOT NULL,
	"sbu_id" integer NOT NULL,
	"name" text NOT NULL,
	"created_at" timestamp DEFAULT now() NOT NULL,
	"updated_at" timestamp DEFAULT now() NOT NULL,
	CONSTRAINT "departments_sbu_name_unique" UNIQUE("sbu_id","name")
);
--> statement-breakpoint
CREATE TABLE "employees" (
	"id" serial PRIMARY KEY NOT NULL,
	"sbu_id" integer NOT NULL,
	"department_id" integer,
	"employee_no" text NOT NULL,
	"name" text NOT NULL,
	"job_role" text,
	"line_manager_id" integer,
	"email" text,
	"phone" text,
	"active" boolean DEFAULT true NOT NULL,
	"created_at" timestamp DEFAULT now() NOT NULL,
	"updated_at" timestamp DEFAULT now() NOT NULL,
	CONSTRAINT "employees_employee_no_unique" UNIQUE("employee_no")
);
--> statement-breakpoint
CREATE TABLE "asset_attachments" (
	"id" serial PRIMARY KEY NOT NULL,
	"asset_id" integer NOT NULL,
	"upgrade_id" integer,
	"kind" text NOT NULL,
	"file_name" text NOT NULL,
	"content_type" text NOT NULL,
	"size_bytes" integer NOT NULL,
	"sha256" text NOT NULL,
	"content" "bytea" NOT NULL,
	"uploaded_by" text NOT NULL,
	"uploaded_by_role" text NOT NULL,
	"uploaded_at" timestamp DEFAULT now() NOT NULL
);
--> statement-breakpoint
ALTER TABLE "users" ALTER COLUMN "role" SET DEFAULT 'employee';--> statement-breakpoint
ALTER TABLE "users" ADD COLUMN "employee_id" integer;--> statement-breakpoint
ALTER TABLE "asset_categories" ADD COLUMN "default_residual_percent" numeric(5, 2) DEFAULT '0' NOT NULL;--> statement-breakpoint
ALTER TABLE "assets" ADD COLUMN "custodian_employee_id" integer;--> statement-breakpoint
ALTER TABLE "assets" ADD COLUMN "department_id" integer;--> statement-breakpoint
ALTER TABLE "asset_upgrades" ADD COLUMN "currency" text DEFAULT 'USD' NOT NULL;--> statement-breakpoint
ALTER TABLE "asset_upgrades" ADD COLUMN "invoice_amount" numeric(18, 2);--> statement-breakpoint
ALTER TABLE "asset_upgrades" ADD COLUMN "exchange_rate" numeric(18, 6) DEFAULT '1' NOT NULL;--> statement-breakpoint
ALTER TABLE "departments" ADD CONSTRAINT "departments_sbu_id_sbus_id_fk" FOREIGN KEY ("sbu_id") REFERENCES "public"."sbus"("id") ON DELETE no action ON UPDATE no action;--> statement-breakpoint
ALTER TABLE "employees" ADD CONSTRAINT "employees_sbu_id_sbus_id_fk" FOREIGN KEY ("sbu_id") REFERENCES "public"."sbus"("id") ON DELETE no action ON UPDATE no action;--> statement-breakpoint
ALTER TABLE "employees" ADD CONSTRAINT "employees_department_id_departments_id_fk" FOREIGN KEY ("department_id") REFERENCES "public"."departments"("id") ON DELETE no action ON UPDATE no action;--> statement-breakpoint
ALTER TABLE "employees" ADD CONSTRAINT "employees_line_manager_id_employees_id_fk" FOREIGN KEY ("line_manager_id") REFERENCES "public"."employees"("id") ON DELETE no action ON UPDATE no action;--> statement-breakpoint
ALTER TABLE "asset_attachments" ADD CONSTRAINT "asset_attachments_asset_id_assets_id_fk" FOREIGN KEY ("asset_id") REFERENCES "public"."assets"("id") ON DELETE no action ON UPDATE no action;--> statement-breakpoint
ALTER TABLE "asset_attachments" ADD CONSTRAINT "asset_attachments_upgrade_id_asset_upgrades_id_fk" FOREIGN KEY ("upgrade_id") REFERENCES "public"."asset_upgrades"("id") ON DELETE no action ON UPDATE no action;--> statement-breakpoint
ALTER TABLE "users" ADD CONSTRAINT "users_employee_id_employees_id_fk" FOREIGN KEY ("employee_id") REFERENCES "public"."employees"("id") ON DELETE no action ON UPDATE no action;--> statement-breakpoint
ALTER TABLE "assets" ADD CONSTRAINT "assets_custodian_employee_id_employees_id_fk" FOREIGN KEY ("custodian_employee_id") REFERENCES "public"."employees"("id") ON DELETE no action ON UPDATE no action;--> statement-breakpoint
ALTER TABLE "assets" ADD CONSTRAINT "assets_department_id_departments_id_fk" FOREIGN KEY ("department_id") REFERENCES "public"."departments"("id") ON DELETE no action ON UPDATE no action;--> statement-breakpoint
ALTER TABLE "users" ADD CONSTRAINT "users_employee_id_unique" UNIQUE("employee_id");