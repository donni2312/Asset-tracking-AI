/**
 * Local PostgreSQL 16 for development without Docker or admin rights.
 *
 *   pnpm db:local          run the server in this terminal (Ctrl+C stops it cleanly);
 *                          initialises the cluster and the `assetiq` database on first run
 *   pnpm db:local status   is a server running?
 *   pnpm db:local stop     stop a server left running (e.g. after a terminal was killed)
 *
 * Like a dev server, it runs in the foreground: VS Code's "DB: local PostgreSQL"
 * task keeps it in its own terminal. The binaries come from the `embedded-postgres`
 * npm package; the server listens on localhost only. Data lives OUTSIDE the
 * repository (default %LOCALAPPDATA%\AssetIQ\pgdata on Windows) so OneDrive never
 * syncs a live database. Override with LOCAL_PG_DATA_DIR / LOCAL_PG_PORT.
 */
import { spawn } from "node:child_process";
import { existsSync, mkdirSync } from "node:fs";
import { createRequire } from "node:module";
import os from "node:os";
import path from "node:path";
import { pathToFileURL } from "node:url";
import EmbeddedPostgres from "embedded-postgres";

const PORT = Number(process.env.LOCAL_PG_PORT ?? 5433);
const USER = "postgres";
const PASSWORD = "postgres";
const DATABASE = "assetiq";
const DATA_DIR =
  process.env.LOCAL_PG_DATA_DIR ??
  (process.platform === "win32"
    ? path.join(process.env.LOCALAPPDATA ?? path.join(os.homedir(), "AppData", "Local"), "AssetIQ", "pgdata")
    : path.join(os.homedir(), ".local", "share", "assetiq", "pgdata"));
const LOG_FILE = path.join(path.dirname(DATA_DIR), "postgres.log");
const CONNECTION_STRING = `postgres://${USER}:${PASSWORD}@localhost:${PORT}/${DATABASE}`;

// timezone=UTC: the schema uses `timestamp without time zone`, which the app reads as UTC,
// so database defaults such as now() must be produced in UTC too (Replit's Postgres already is).
const SERVER_OPTIONS = `-p ${PORT} -c timezone=UTC -c log_timezone=UTC`;

const cluster = new EmbeddedPostgres({
  databaseDir: DATA_DIR,
  port: PORT,
  user: USER,
  password: PASSWORD,
  authMethod: "scram-sha-256",
  persistent: true,
  initdbFlags: ["--encoding=UTF8", "--locale=C"],
  onLog: () => {},
  onError: (err) => console.error(err),
});

/** Resolve pg_ctl from the platform package that embedded-postgres installed. */
async function pgCtlPath(): Promise<string> {
  const platform = { win32: "windows", linux: "linux", darwin: "darwin" }[process.platform as string];
  if (!platform) throw new Error(`Unsupported platform: ${process.platform}`);
  const pkg = `@embedded-postgres/${platform}-${process.arch}`;
  const fromScripts = createRequire(import.meta.url);
  const fromEmbedded = createRequire(fromScripts.resolve("embedded-postgres"));
  const entry = fromEmbedded.resolve(pkg);
  const binaries = (await import(pathToFileURL(entry).href)) as { pg_ctl: string };
  return binaries.pg_ctl;
}

function run(command: string, args: string[]): Promise<number> {
  return new Promise((resolve, reject) => {
    const child = spawn(command, args, { stdio: "ignore", windowsHide: true });
    child.on("error", reject);
    child.on("exit", (code) => resolve(code ?? 1));
  });
}

const initialised = () => existsSync(path.join(DATA_DIR, "PG_VERSION"));

async function isRunning(pgCtl: string): Promise<boolean> {
  if (!initialised()) return false;
  return (await run(pgCtl, ["status", "-D", DATA_DIR])) === 0;
}

async function ensureDatabase(): Promise<void> {
  const client = cluster.getPgClient("postgres", "localhost");
  await client.connect();
  try {
    const result = await client.query("SELECT 1 FROM pg_database WHERE datname = $1", [DATABASE]);
    if (result.rowCount === 0) {
      await client.query(`CREATE DATABASE ${DATABASE}`);
      console.log(`Created database "${DATABASE}".`);
    }
  } finally {
    await client.end();
  }
}

function printReady(alreadyRunning: boolean) {
  console.log(`Local PostgreSQL is ready on port ${PORT}${alreadyRunning ? " (already running)" : ""}.`);
  console.log(`  DATABASE_URL=${CONNECTION_STRING}`);
  console.log(`  data: ${DATA_DIR}`);
  console.log(`  log:  ${LOG_FILE}`);
}

async function start(): Promise<void> {
  const pgCtl = await pgCtlPath();
  if (!initialised()) {
    console.log(`Initialising a new PostgreSQL 16 cluster in ${DATA_DIR} …`);
    mkdirSync(path.dirname(DATA_DIR), { recursive: true });
    await cluster.initialise();
  }

  if (await isRunning(pgCtl)) {
    await ensureDatabase();
    printReady(true);
    console.log("Stop it with: pnpm db:local stop");
    return;
  }

  const code = await run(pgCtl, ["start", "-D", DATA_DIR, "-l", LOG_FILE, "-o", SERVER_OPTIONS, "-w", "-t", "60"]);
  if (code !== 0) throw new Error(`pg_ctl start failed (exit ${code}). See ${LOG_FILE}`);
  await ensureDatabase();
  printReady(false);
  console.log("First time? Run: pnpm db:push && pnpm seed");
  console.log("Press Ctrl+C to stop PostgreSQL.");

  let stopping = false;
  const shutdown = async () => {
    if (stopping) return;
    stopping = true;
    console.log("\nStopping local PostgreSQL …");
    await run(pgCtl, ["stop", "-D", DATA_DIR, "-m", "fast", "-w"]).catch(() => 1);
    console.log("Local PostgreSQL stopped.");
    process.exit(0);
  };
  process.on("SIGINT", shutdown);
  process.on("SIGTERM", shutdown);
  process.on("SIGBREAK", shutdown);
  process.on("SIGHUP", shutdown);

  // Stay in the foreground (like a dev server) and notice if the server dies.
  const watchdog = setInterval(async () => {
    if (!stopping && !(await isRunning(pgCtl))) {
      clearInterval(watchdog);
      console.error(`Local PostgreSQL is no longer running (stopped from another terminal, or it crashed — see ${LOG_FILE}).`);
      process.exit(1);
    }
  }, 10_000);
}

async function stop(): Promise<void> {
  const pgCtl = await pgCtlPath();
  if (!(await isRunning(pgCtl))) {
    console.log("Local PostgreSQL is not running.");
    return;
  }
  const code = await run(pgCtl, ["stop", "-D", DATA_DIR, "-m", "fast", "-w"]);
  if (code !== 0) throw new Error(`pg_ctl stop failed (exit ${code}). See ${LOG_FILE}`);
  console.log("Local PostgreSQL stopped.");
}

async function status(): Promise<void> {
  const pgCtl = await pgCtlPath();
  const running = await isRunning(pgCtl);
  console.log(running ? `Running on port ${PORT} — ${CONNECTION_STRING}` : "Not running. Start it with: pnpm db:local");
  console.log(`Data directory: ${DATA_DIR}`);
  process.exitCode = running ? 0 : 3;
}

const command = process.argv[2] ?? "start";
const actions: Record<string, () => Promise<void>> = { start, stop, status };
const action = actions[command];
if (!action) {
  console.error(`Unknown command "${command}". Use: start | stop | status`);
  process.exit(1);
}
action().catch((err: unknown) => {
  console.error(err instanceof Error ? err.message : err);
  process.exit(1);
});
