import { PGlite } from "@electric-sql/pglite";
import { drizzle } from "drizzle-orm/pglite";
import * as schema from "@workspace/db/schema";

/**
 * In-memory PostgreSQL (PGlite, Postgres compiled to WebAssembly) with the
 * real Drizzle schema applied. Each test file gets its own instance.
 */
const client = new PGlite();
export const testDb = drizzle(client, { schema });

let ready: Promise<void> | undefined;

async function createSchema(): Promise<void> {
  // Generate CREATE TABLE statements straight from the Drizzle schema, so the
  // tests can never drift from lib/db/src/schema.
  const { generateDrizzleJson, generateMigration } = await import("drizzle-kit/api");
  const statements = await generateMigration(generateDrizzleJson({}), generateDrizzleJson(schema));
  for (const statement of statements) await client.exec(statement);
}

export function ensureSchema(): Promise<void> {
  ready ??= createSchema();
  return ready;
}

const TABLES = [
  "ai_insights",
  "asset_lifecycle_events",
  "movements",
  "gate_passes",
  "assets",
  "rfid_readers",
  "locations",
  "sbus",
];

/** Empty every table and reset id sequences. */
export async function resetDb(): Promise<void> {
  await ensureSchema();
  await client.exec(`TRUNCATE ${TABLES.map((t) => `"${t}"`).join(", ")} RESTART IDENTITY CASCADE`);
}
