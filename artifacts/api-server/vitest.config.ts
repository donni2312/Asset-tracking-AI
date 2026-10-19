import { defineConfig } from "vitest/config";

export default defineConfig({
  test: {
    environment: "node",
    include: ["src/**/*.test.ts"],
    // Swaps @workspace/db for an in-memory PostgreSQL (PGlite) built from the real Drizzle schema.
    setupFiles: ["./src/test/setup.ts"],
    testTimeout: 30_000,
    hookTimeout: 120_000,
    pool: "forks",
    env: {
      LOG_LEVEL: "silent",
      ORG_TIMEZONE: "Africa/Harare",
      WORK_HOURS_START: "7",
      WORK_HOURS_END: "18",
      WORK_DAYS: "1-5",
      // Tests never call OpenAI; the OpenAI parser is exercised with a stubbed fetch.
      OPENAI_API_KEY: "",
    },
  },
});
