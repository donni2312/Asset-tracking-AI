/**
 * `pnpm db:migrate` — applies lib/db/migrations in order (drizzle-orm's migrator, the same tool that
 * generated them with `drizzle-kit generate`).
 *
 * Databases created before migrations existed (`pnpm db:push`, the Supabase project, this PC's local
 * cluster) already hold the tables of 0000_baseline but have no migration history. For those, the baseline
 * is recorded as applied first, so only the later migrations run. A database that is empty gets everything.
 */
import crypto from "node:crypto";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { drizzle } from "drizzle-orm/node-postgres";
import { migrate } from "drizzle-orm/node-postgres/migrator";
import pg from "pg";

const migrationsFolder = path.join(path.dirname(fileURLToPath(import.meta.url)), "..", "..", "lib", "db", "migrations");

async function main() {
  const url = process.env.DATABASE_URL;
  if (!url) throw new Error("DATABASE_URL must be set (see .env.example)");
  const client = new pg.Client({ connectionString: url });
  await client.connect();
  try {
    await recordBaselineIfPushedBefore(client);
    await migrate(drizzle(client), { migrationsFolder });
    const { rows } = await client.query<{ count: string }>(`SELECT count(*) FROM drizzle.__drizzle_migrations`);
    console.log(`Migrations up to date (${rows[0].count} applied).`);
  } finally {
    await client.end();
  }
}

async function recordBaselineIfPushedBefore(client: pg.Client) {
  const { rows } = await client.query<{ pushed: boolean; tracked: boolean }>(`
    SELECT to_regclass('public.users') IS NOT NULL AS pushed,
           to_regclass('drizzle.__drizzle_migrations') IS NOT NULL AS tracked`);
  if (!rows[0].pushed || rows[0].tracked) return;

  const journal = JSON.parse(fs.readFileSync(path.join(migrationsFolder, "meta", "_journal.json"), "utf8")) as {
    entries: { tag: string; when: number }[];
  };
  const baseline = journal.entries[0];
  const sql = fs.readFileSync(path.join(migrationsFolder, `${baseline.tag}.sql`), "utf8");
  // Same hash and timestamp drizzle-orm's migrator records when it applies a migration itself.
  const hash = crypto.createHash("sha256").update(sql).digest("hex");
  await client.query(`CREATE SCHEMA IF NOT EXISTS drizzle`);
  await client.query(`CREATE TABLE IF NOT EXISTS drizzle.__drizzle_migrations (id SERIAL PRIMARY KEY, hash text NOT NULL, created_at bigint)`);
  await client.query(`INSERT INTO drizzle.__drizzle_migrations (hash, created_at) VALUES ($1, $2)`, [hash, baseline.when]);
  console.log(`Existing schema found without migration history: recorded ${baseline.tag} as applied.`);
}

main().catch((error) => {
  console.error(error);
  process.exit(1);
});
