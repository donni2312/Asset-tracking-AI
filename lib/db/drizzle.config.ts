import { defineConfig } from "drizzle-kit";
import fs from "fs";
import path from "path";

// Local development: read DATABASE_URL from the repository's .env file when it is
// not already set (Replit injects it as a secret, so nothing changes there).
const rootEnv = path.join(__dirname, "..", "..", ".env");
if (!process.env.DATABASE_URL && fs.existsSync(rootEnv)) {
  process.loadEnvFile(rootEnv);
}

if (!process.env.DATABASE_URL) {
  throw new Error("DATABASE_URL, ensure the database is provisioned");
}

export default defineConfig({
  // drizzle-kit globs this path, so it must use forward slashes (path.join gives backslashes on Windows).
  schema: path.join(__dirname, "./src/schema/index.ts").split(path.sep).join("/"),
  dialect: "postgresql",
  dbCredentials: {
    url: process.env.DATABASE_URL,
  },
});
