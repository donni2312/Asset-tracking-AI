# AssetIQ — Intelligent Asset Management & Tracking System

An IoT-ready, AI-assisted enterprise asset tracking system. Assets are tracked automatically via RFID readers at gates and offices, with approval workflows for gate passes and an AI anomaly detection engine. Integrates (via stub) with Microsoft Business Central.

## Run & Operate

- `pnpm --filter @workspace/asset-tracker run dev` — run the React dashboard (uses PORT env; defaults to 5173 off Replit)
- `pnpm --filter @workspace/api-server run dev` — run the API server in watch mode (tsx; port 8080, served at /api)
- `pnpm test` — run the automated tests (Vitest; API tests use an in-memory PostgreSQL, no DATABASE_URL needed)
- `pnpm run typecheck` — full typecheck across all packages
- `pnpm run build` — typecheck + build all packages
- `pnpm --filter @workspace/api-spec run codegen` (or `pnpm codegen`) — regenerate API hooks and Zod schemas from the OpenAPI spec
- `pnpm --filter @workspace/db run push` (or `pnpm db:push`) — push DB schema changes (dev only)
- `pnpm seed` / `pnpm seed --reset` — load demo data (`--reset` wipes all AssetIQ tables first)
- Required env: `DATABASE_URL` — Postgres connection string. Optional: `OPENAI_API_KEY`, `OPENAI_MODEL`, `ORG_TIMEZONE`, `WORK_HOURS_START/END`, `WORK_DAYS`, `IDLE_ASSET_DAYS` (see `.env.example`)
- Local (non-Replit) development: see `CLAUDE.md` — `.env` file, `pnpm db:local` (embedded PostgreSQL), VS Code tasks

## Stack

- pnpm workspaces, Node.js 24, TypeScript 5.9
- Frontend: React + Vite + TailwindCSS + shadcn/ui, TanStack Query, wouter, Recharts
- API: Express 5 (Node.js for MVP; production target is C# ASP.NET Core on Azure)
- DB: PostgreSQL + Drizzle ORM
- Validation: Zod (`zod/v4`), `drizzle-zod`
- API codegen: Orval (from OpenAPI spec in `lib/api-spec/openapi.yaml`)
- Tests: Vitest + PGlite (Postgres in WebAssembly) for API integration tests
- Build: esbuild (CJS bundle)

## Where things live

- `lib/api-spec/openapi.yaml` — single source of truth for all API contracts
- `lib/db/src/schema/` — database tables (sbus, locations, assets, movements, gate_passes, rfid_readers, ai_insights, asset_lifecycle_events)
- `artifacts/api-server/src/routes/` — thin Express route handlers
- `artifacts/api-server/src/services/` — business logic: `rfid-scan.ts` (RFID pipeline), `gate-pass-workflow.ts`, `anomaly-rules.ts` + `gate-pass-rules.ts` (pure rules), `insights.ts`, `nl-query/` (NL questions), `bc-sync.ts`, `dashboard.ts`
- `artifacts/api-server/src/**/*.test.ts` — tests; `src/test/` holds the PGlite test database, fixtures and HTTP helper
- `artifacts/asset-tracker/src/` — React frontend
- `scripts/src/seed.ts`, `scripts/src/seed-data.ts` — demo data; `scripts/src/local-db.ts` — local PostgreSQL helper
- `docs/project-plan.md` — full project plan, architecture, decision log and degree report content
- `CLAUDE.md` — change log for the October 2026 work, local setup and test instructions

## Product

AssetIQ tracks physical assets (laptops, projectors, equipment) across office locations using RFID simulation. Key features:
- **Dashboard**: Live stats, movements-per-day and asset-status-by-SBU charts, activity feed, gate activity, asset distribution
- **Asset Registry**: Full CRUD with search/filter; detail page with history, active alerts, Move and Request Gate Pass actions
- **Locations**: list, create and edit screens
- **Movement Log**: Audit trail of all asset movements (RFID/manual/gate pass) + manual movement form
- **Gate Passes**: request form, passes auto-raised by gate readers, approve/reject with a named approver, in-app notifications
- **RFID Simulator**: select a reader + tag (optionally a past scan time); shows the gate-check outcome and insights raised
- **AI Insights**: rule-based anomaly detection + natural language query (OpenAI intent parsing when configured, rules as fallback)
- **BC Sync**: Business Central integration panel — mock connection, manual sync, pending-changes count

## Architecture decisions

- **OpenAPI-first**: All API contracts defined in `lib/api-spec/openapi.yaml`, never hand-written types — codegen produces React Query hooks and Zod validators
- **RFID as events**: RFID reads are modelled as `MovementEvent` records — swapping the simulator for real Azure IoT Hub events requires only changing the event source (`processRfidScan`), not the data model
- **Gate passes**: a gate exit without an approved pass auto-raises a pending pass; approval records a `gate_pass` movement (asset off-premises, `toLocationId` null); rejection flags the asset. Gate direction is inferred (on-site asset = exit, off-premises asset = entry)
- **AI**: deterministic anomaly rules; NL questions → structured intent (OpenAI optional, rules fallback) → executed against the DB, so answers always come from live data
- **BC sync is manual in the MVP**: mock connection; movements and gate-pass decisions since the last sync are shown as pending changes
- **Notifications are in-app**: pending gate passes are polled every 10 s (sidebar badge + toast)
- **Node.js for MVP, C# for prod**: The Express backend mirrors the same route structure the C#/.NET 8 production implementation would follow on Azure App Service

## Gotchas

- After any OpenAPI spec change, always re-run codegen: `pnpm --filter @workspace/api-spec run codegen`
- `purchaseDate` and `returnDate` DB columns are stored as `text` (ISO date strings) — the Orval-generated types produce `Date` objects from the `format: date` spec, so routes must call `.toISOString().split("T")[0]` before inserting
- `purchaseValue` DB column is Drizzle `numeric()` which expects a string — routes convert with `String(value)`
- Timestamps are `timestamp without time zone` holding **UTC**; the API writes them from its own clock. A Postgres server whose TimeZone isn't UTC shifts `defaultNow()` values (the local `pnpm db:local` server runs in UTC)
- Business rules ("today", "this week", working hours) use `ORG_TIMEZONE` (default Africa/Harare), not the server timezone
- Never use `console.log` in server code — use `req.log` in route handlers and `logger` from `lib/logger` elsewhere
- `pnpm-workspace.yaml` keeps the win32-x64 native binaries (esbuild, rollup, lightningcss, tailwind oxide) so the workspace also runs on Windows

## User preferences

_Populate as you build — explicit user instructions worth remembering across sessions._

## Pointers

- See `docs/project-plan.md` for the full project plan, architecture diagrams, decision log and degree report write-up
- See `CLAUDE.md` for local development, VS Code setup and how to test every feature
- See the `pnpm-workspace` skill for workspace structure, TypeScript setup, and package details
