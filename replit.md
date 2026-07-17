# AssetIQ — Intelligent Asset Management & Tracking System

An IoT-ready, AI-assisted enterprise asset tracking system. Assets are tracked automatically via RFID readers at gates and offices, with approval workflows for gate passes and an AI anomaly detection engine. Integrates (via stub) with Microsoft Business Central.

## Run & Operate

- `pnpm --filter @workspace/asset-tracker run dev` — run the React dashboard (uses PORT env)
- `pnpm --filter @workspace/api-server run dev` — run the API server (port 8080, served at /api)
- `pnpm run typecheck` — full typecheck across all packages
- `pnpm run build` — typecheck + build all packages
- `pnpm --filter @workspace/api-spec run codegen` — regenerate API hooks and Zod schemas from the OpenAPI spec
- `pnpm --filter @workspace/db run push` — push DB schema changes (dev only)
- Required env: `DATABASE_URL` — Postgres connection string

## Stack

- pnpm workspaces, Node.js 24, TypeScript 5.9
- Frontend: React + Vite + TailwindCSS + shadcn/ui, TanStack Query, wouter
- API: Express 5 (Node.js for MVP; production target is C# ASP.NET Core on Azure)
- DB: PostgreSQL + Drizzle ORM
- Validation: Zod (`zod/v4`), `drizzle-zod`
- API codegen: Orval (from OpenAPI spec in `lib/api-spec/openapi.yaml`)
- Build: esbuild (CJS bundle)

## Where things live

- `lib/api-spec/openapi.yaml` — single source of truth for all API contracts
- `lib/db/src/schema/` — database tables (locations, assets, movements, gate_passes, rfid_readers, ai_insights)
- `artifacts/api-server/src/routes/` — Express route handlers (assets, locations, movements, gate-passes, rfid, dashboard, insights, bc)
- `artifacts/asset-tracker/src/` — React frontend
- `docs/project-plan.md` — full project plan, architecture, and degree report content

## Product

AssetIQ tracks physical assets (laptops, projectors, equipment) across office locations using RFID simulation. Key features:
- **Dashboard**: Live stats, activity feed, asset distribution by location, AI alert strip
- **Asset Registry**: Full CRUD with search/filter by category/status/location
- **Movement Log**: Audit trail of all asset movements (RFID/manual/gate pass)
- **Gate Passes**: Request/approve/reject workflow for assets leaving premises
- **RFID Simulator**: Demonstrates IoT concept — select a reader + tag, system auto-records movement
- **AI Insights**: Rule-based anomaly detection (missing assets, idle assets, gate anomalies) + natural language query
- **BC Sync**: Microsoft Business Central integration status panel (stub for MVP)

## Architecture decisions

- **OpenAPI-first**: All API contracts defined in `lib/api-spec/openapi.yaml`, never hand-written types — codegen produces React Query hooks and Zod validators
- **RFID as events**: RFID reads are modelled as `MovementEvent` records with `triggeredBy: "rfid"` — swapping the simulator for real Azure IoT Hub events requires only changing the event source, not the data model
- **AI rule-based for MVP**: Anomaly detection uses deterministic rules (idle > 7 days, missing status, gate detections) instead of ML, keeping it fast and explainable for a degree project
- **BC sync is stubbed**: The sync endpoint simulates connection state in-memory; production would call the Microsoft BC REST API with OAuth2
- **Node.js for MVP, C# for prod**: The Express backend mirrors the same route structure the C#/.NET 8 production implementation would follow on Azure App Service

## Gotchas

- After any OpenAPI spec change, always re-run codegen: `pnpm --filter @workspace/api-spec run codegen`
- `purchaseDate` and `returnDate` DB columns are stored as `text` (ISO date strings) — the Orval-generated types produce `Date` objects from the `format: date` spec, so routes must call `.toISOString().split("T")[0]` before inserting
- `purchaseValue` DB column is Drizzle `numeric()` which expects a string — routes convert with `String(value)`
- Never use `console.log` in server code — use `req.log` in route handlers and `logger` from `lib/logger` elsewhere

## User preferences

_Populate as you build — explicit user instructions worth remembering across sessions._

## Pointers

- See `docs/project-plan.md` for the full project plan, architecture diagrams, and degree report write-up
- See the `pnpm-workspace` skill for workspace structure, TypeScript setup, and package details
