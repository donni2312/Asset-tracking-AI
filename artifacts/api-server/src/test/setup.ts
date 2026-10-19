import { vi } from "vitest";

// Every module that imports `db` from @workspace/db gets the PGlite-backed test database instead.
vi.mock("@workspace/db", async () => {
  const schema = await import("@workspace/db/schema");
  const { testDb } = await import("./test-db");
  return { ...schema, db: testDb, pool: undefined };
});
