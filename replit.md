# AssetIQ — Intelligent Asset Management & Tracking System

An IoT-ready, AI-assisted enterprise asset tracking system. Assets are tracked automatically via RFID readers at gates and offices, with approval workflows for gate passes and an AI anomaly detection engine. Integrates (via stub) with Microsoft Business Central.

## Run & Operate

- `pnpm --filter @workspace/asset-tracker run dev` — run the React dashboard (uses PORT env; defaults to 5173 off Replit)
- `pnpm --filter @workspace/api-server run dev` — run the C# API with hot reload (`dotnet watch`; port 8080, served at /api)
- `pnpm test` — run the automated tests (xUnit; the API tests start a throwaway PostgreSQL, no DATABASE_URL needed)
- `pnpm run typecheck` — full typecheck across all packages
- `pnpm run build` — typecheck + build all packages
- `pnpm --filter @workspace/api-spec run codegen` (or `pnpm codegen`) — regenerate API hooks and Zod schemas from the OpenAPI spec
- `pnpm --filter @workspace/db run push` (or `pnpm db:push`) — push DB schema changes (dev only)
- `pnpm seed` / `pnpm seed --reset` — load demo data (`--reset` wipes all AssetIQ tables first)
- Required env: `DATABASE_URL` — Postgres connection string. Optional: `OPENAI_API_KEY`, `OPENAI_MODEL`, `ORG_TIMEZONE`, `WORK_HOURS_START/END`, `WORK_DAYS`, `IDLE_ASSET_DAYS` (see `.env.example`)
- Local (non-Replit) development: see `CLAUDE.md` — `.env` file, `pnpm db:local` (embedded PostgreSQL), VS Code tasks

## Stack

- pnpm workspaces, Node.js 24, TypeScript 5.9 (frontend, schema and scripts); .NET 8 SDK (API)
- Frontend: React + Vite + TailwindCSS + shadcn/ui, TanStack Query, wouter, Recharts
- API: ASP.NET Core 8 Web API in C# (controllers + services), EF Core 8 with Npgsql
- DB: PostgreSQL; the schema is defined with Drizzle ORM in `lib/db` (`pnpm db:push`, seed script), mapped by EF Core
- API codegen: Orval (from OpenAPI spec in `lib/api-spec/openapi.yaml`) for the React Query client
- Tests: xUnit + WebApplicationFactory against a throwaway PostgreSQL 16 (embedded-postgres binaries)
- Build: `dotnet publish` → `artifacts/api-server/dist`

## Where things live

- `lib/api-spec/openapi.yaml` — single source of truth for all API contracts
- `lib/db/src/schema/` — database tables (sbus, locations, assets, movements, gate_passes, rfid_readers, ai_insights, asset_lifecycle_events)
- `artifacts/api-server/src/AssetIQ.Api/Controllers/` — thin ASP.NET Core controllers
- `artifacts/api-server/src/AssetIQ.Api/Services/` — business logic: `RfidScanService` (RFID pipeline), `GatePassService`, `AnomalyRules` + `GatePassRules` (pure rules), `InsightService`, `NlQuery/` (NL questions), `BcSyncService`, `DashboardService`
- `artifacts/api-server/src/AssetIQ.Api/Data/` — EF Core entities and `AssetIqDbContext` (mapped onto the existing tables)
- `artifacts/api-server/tests/AssetIQ.Api.Tests/` — unit and integration tests; `Infrastructure/` holds the throwaway PostgreSQL, fixtures and HTTP helper
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
- **C# backend**: ASP.NET Core + EF Core on PostgreSQL (rewritten from the original Express API with identical routes and JSON), ready for Azure App Service

## Gotchas

- After any OpenAPI spec change, always re-run codegen: `pnpm --filter @workspace/api-spec run codegen`
- `purchaseDate` and `returnDate` DB columns are stored as `text` (ISO date strings, `YYYY-MM-DD`); the API converts the incoming date before writing
- `purchaseValue` is `numeric` in the database and `decimal` in C#; the API returns it as a JSON number
- Timestamps are `timestamp without time zone` holding **UTC**; the API writes them from its own clock (`AppClock`), never `now()`. EF Core converts UTC `DateTime`s for these columns
- Business rules ("today", "this week", working hours) use `ORG_TIMEZONE` (default Africa/Harare), not the server timezone
- A schema change in `lib/db/src/schema` also needs the EF entity and `tests/AssetIQ.Api.Tests/Database/schema.sql` updated — `SchemaConformanceTests` fails otherwise
- Never use `Console.WriteLine` in server code — use `ILogger<T>`
- `pnpm-workspace.yaml` keeps the win32-x64 native binaries (esbuild, rollup, lightningcss, tailwind oxide) so the workspace also runs on Windows

## User preferences

_Populate as you build — explicit user instructions worth remembering across sessions._

## Pointers

- See `docs/project-plan.md` for the full project plan, architecture diagrams, decision log and degree report write-up
- See `CLAUDE.md` for local development, VS Code setup and how to test every feature
- See the `pnpm-workspace` skill for workspace structure, TypeScript setup, and package details
