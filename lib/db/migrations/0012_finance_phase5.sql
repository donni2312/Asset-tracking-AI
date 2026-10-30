CREATE TABLE "asset_revaluations" (
	"id" serial PRIMARY KEY NOT NULL,
	"asset_id" integer NOT NULL,
	"kind" text NOT NULL,
	"period" text NOT NULL,
	"amount" numeric(18, 2) NOT NULL,
	"nbv_before" numeric(18, 2) NOT NULL,
	"nbv_after" numeric(18, 2) NOT NULL,
	"revised_life_months" integer,
	"reason" text NOT NULL,
	"attachment_id" integer NOT NULL,
	"recorded_by" text NOT NULL,
	"created_at" timestamp DEFAULT now() NOT NULL
);
--> statement-breakpoint
CREATE TABLE "capex_budgets" (
	"id" serial PRIMARY KEY NOT NULL,
	"sbu_id" integer NOT NULL,
	"financial_year" integer NOT NULL,
	"category_id" integer NOT NULL,
	"amount" numeric(18, 2) NOT NULL,
	"alerted_percent" integer,
	"updated_by" text NOT NULL,
	"updated_at" timestamp DEFAULT now() NOT NULL,
	CONSTRAINT "capex_budgets_sbu_year_category_unique" UNIQUE("sbu_id","financial_year","category_id")
);
--> statement-breakpoint
ALTER TABLE "exit_clearance_items" ADD COLUMN "recovery_outcome" text;--> statement-breakpoint
ALTER TABLE "exit_clearance_items" ADD COLUMN "recovery_approved_by" text;--> statement-breakpoint
ALTER TABLE "exit_clearance_items" ADD COLUMN "recovery_note" text;--> statement-breakpoint
ALTER TABLE "exit_clearance_items" ADD COLUMN "recovery_recorded_by" text;--> statement-breakpoint
ALTER TABLE "exit_clearance_items" ADD COLUMN "recovery_recorded_at" timestamp;--> statement-breakpoint
ALTER TABLE "asset_revaluations" ADD CONSTRAINT "asset_revaluations_asset_id_assets_id_fk" FOREIGN KEY ("asset_id") REFERENCES "public"."assets"("id") ON DELETE no action ON UPDATE no action;--> statement-breakpoint
ALTER TABLE "asset_revaluations" ADD CONSTRAINT "asset_revaluations_attachment_id_asset_attachments_id_fk" FOREIGN KEY ("attachment_id") REFERENCES "public"."asset_attachments"("id") ON DELETE no action ON UPDATE no action;--> statement-breakpoint
ALTER TABLE "capex_budgets" ADD CONSTRAINT "capex_budgets_sbu_id_sbus_id_fk" FOREIGN KEY ("sbu_id") REFERENCES "public"."sbus"("id") ON DELETE no action ON UPDATE no action;--> statement-breakpoint
ALTER TABLE "capex_budgets" ADD CONSTRAINT "capex_budgets_category_id_asset_categories_id_fk" FOREIGN KEY ("category_id") REFERENCES "public"."asset_categories"("id") ON DELETE no action ON UPDATE no action;