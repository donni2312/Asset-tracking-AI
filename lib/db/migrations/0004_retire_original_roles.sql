-- Phase 2: the original viewer / operator / admin roles are replaced by the roles of the permissions matrix.
-- Decided with the product owner (29 Oct 2026): "admin" accounts become Group IT; "operator" and "viewer"
-- accounts are disabled until Group IT gives them a role (and SBU). They are kept, not deleted, so the change
-- can be undone and their names stay on the records they made.
UPDATE "users" SET "role" = 'group_it', "updated_at" = now() WHERE "role" = 'admin';
--> statement-breakpoint
UPDATE "users" SET "active" = false, "updated_at" = now() WHERE "role" IN ('operator', 'viewer');
--> statement-breakpoint
-- Upgrades logged before multi-currency invoices were captured were all in USD.
UPDATE "asset_upgrades" SET "invoice_amount" = "cost" WHERE "invoice_amount" IS NULL;
--> statement-breakpoint
-- Attachments are audit evidence: never changed or removed once uploaded (TRUNCATE in tests is unaffected).
CREATE OR REPLACE FUNCTION asset_attachments_append_only() RETURNS trigger AS $$
BEGIN
  RAISE EXCEPTION 'asset_attachments is append-only: attachments cannot be changed or deleted';
END;
$$ LANGUAGE plpgsql;
--> statement-breakpoint
CREATE TRIGGER asset_attachments_append_only BEFORE UPDATE OR DELETE ON "asset_attachments"
  FOR EACH ROW EXECUTE FUNCTION asset_attachments_append_only();
