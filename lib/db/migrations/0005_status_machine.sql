ALTER TABLE "assets" ALTER COLUMN "status" SET DEFAULT 'pending_asset_number';--> statement-breakpoint
ALTER TABLE "assets" ADD COLUMN "lost_date" text;--> statement-breakpoint
ALTER TABLE "assets" ADD COLUMN "lost_reported_by" text;--> statement-breakpoint
ALTER TABLE "assets" ADD COLUMN "lost_notes" text;