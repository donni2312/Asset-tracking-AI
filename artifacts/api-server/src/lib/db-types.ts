import type { ExtractTablesWithRelations } from "drizzle-orm";
import type { PgDatabase } from "drizzle-orm/pg-core";
import type { NodePgQueryResultHKT } from "drizzle-orm/node-postgres";
import type * as schema from "@workspace/db/schema";

/**
 * Anything that can run queries: the shared `db` handle or a transaction.
 * Services accept an Executor so the same helper can run inside or outside a
 * transaction (and against the in-memory PGlite database used by the tests).
 */
export type Executor = PgDatabase<NodePgQueryResultHKT, typeof schema, ExtractTablesWithRelations<typeof schema>>;
