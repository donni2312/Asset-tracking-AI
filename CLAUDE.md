# CLAUDE.md — AssetIQ (TSL asset tracking)

AssetIQ is an RFID-driven asset tracking MVP for TSL Group: React dashboard + Express API + PostgreSQL in a pnpm
monorepo, contract-first from `lib/api-spec/openapi.yaml`. The degree-project plan (architecture, decision log,
testing) is `docs/project-plan.md`; the Replit agent's notes are `replit.md`. This file explains how to run and
test the project locally and records everything changed in the October 2026 pass.

## Commands

| Command | What it does |
|---|---|
| `pnpm install` | Install all workspace dependencies |
| `pnpm db:local` | Run a local PostgreSQL 16 in this terminal (port 5433, Ctrl+C stops it). `pnpm db:local status` / `stop` manage a leftover server |
| `pnpm db:push` | Create / update tables from `lib/db/src/schema` (drizzle-kit) |
| `pnpm seed` / `pnpm seed --reset` | Load demo data into an empty DB / wipe every AssetIQ table first |
| `pnpm dev:api` | API on http://localhost:8080/api, restarts on save (tsx watch, reads `.env`) |
| `pnpm dev:web` | Dashboard on http://localhost:5173 (Vite hot reload, proxies `/api` to 8080) |
| `pnpm test` | All automated tests (Vitest). No database needed |
| `pnpm --filter @workspace/api-server run test:watch` | Tests in watch mode |
| `pnpm run typecheck` | Typecheck every package |
| `pnpm codegen` | Regenerate React Query hooks + Zod schemas after editing `openapi.yaml` |
| `pnpm run build` | Typecheck + production builds |

## Local development (Windows + VS Code)

### What is installed on this PC
- **Node.js 24.22.0 LTS** (portable, no admin) in `%LOCALAPPDATA%\Programs\nodejs`, added to the **user PATH**,
  plus **pnpm 10.34.7** in the same folder. Restart VS Code once so new terminals see it
  (`.vscode/settings.json` also prepends it for VS Code terminals). To remove: delete that folder and the PATH entry.
- **PostgreSQL 16** comes from the `embedded-postgres` npm package (no admin, no Docker). `pnpm db:local` keeps its
  data in `%LOCALAPPDATA%\AssetIQ\pgdata` (outside OneDrive) and its log in `%LOCALAPPDATA%\AssetIQ\postgres.log`.
  It listens on localhost only; user/password `postgres`/`postgres`, database `assetiq`, server timezone UTC.
  That cluster is already initialised and seeded.
- **`.env`** (git-ignored) was created from `.env.example`. It holds `DATABASE_URL`, `PORT` and the optional
  AI / working-hours settings. Any PostgreSQL works: point `DATABASE_URL` at it instead of using `pnpm db:local`.

### OneDrive warning — do this before `pnpm install`
The repo sits in a OneDrive-synced folder. `pnpm install` writes tens of thousands of files into `node_modules`,
which OneDrive would upload to the TSL tenant and can lock mid-install. Work from a copy outside OneDrive:

```powershell
robocopy "$env:USERPROFILE\OneDrive - TSL Ltd\Documents\Attachments\Desktop\Donn's Coding Projects\Asset-Tracking-AI\Asset-Tracking-AI" C:\dev\Asset-Tracking-AI /E /XD node_modules .venv dist
code C:\dev\Asset-Tracking-AI
```

(`robocopy` copies `.git` and the uncommitted changes too. Alternatively commit, push and `git clone` to `C:\dev`.)

### First run
1. Open the folder in VS Code and accept the recommended extensions (Vitest, REST Client, Tailwind, OpenAPI, PostgreSQL).
2. Terminal → Run Task → **Setup: install dependencies** (or `pnpm install`). On the TSL network the default 16
   parallel downloads time out (ETIMEDOUT); if that happens run
   `pnpm install --network-concurrency=4 --fetch-timeout=180000` (≈3 minutes the first time).
3. Run Task → **DB: local PostgreSQL** and leave it running, then run **DB: push schema** and **DB: seed demo data (reset)**.
   (On this PC the cluster in `%LOCALAPPDATA%\AssetIQ` is shared by every copy of the repo and is already seeded,
   so push and seed are only needed on another machine or to reset the demo.)

### Every day
- Run Task → **Dev: full stack** starts PostgreSQL → API (watch) → Vite, each in its own terminal; open
  http://localhost:5173. Edits to the API restart it; edits to the React app hot-reload.
- **Debug**: Run and Debug → *Full stack (debug API + Edge)* (stop the "API: dev server" task first — same port),
  *Tests: debug current API test file*, *Seed: debug*.
- **Hit the API live**: `artifacts/api-server/requests.http` with the REST Client extension (Send Request).
- **Tests**: Testing sidebar (Vitest extension) or Run Task → *Test: API (Vitest)* / *Test: API (watch mode)*.
- **Inspect the DB**: PostgreSQL extension → localhost:5433, user `postgres`, password `postgres`, database `assetiq`.

## Changes made in October 2026 — and how to test them

All automated tests live in `artifacts/api-server/src`. Status after this pass: **9 test files, 111 tests passing**;
`pnpm run typecheck` clean (it previously failed on two dashboard / BC page errors); production builds succeed;
a 14-step browser walkthrough of the flows below passed with no console errors.

### 1. Gate-pass workflow wired into the RFID pipeline (plan 6.3)
- `services/rfid-scan.ts` (called by `POST /rfid/simulate`) runs in one transaction: gate check → movement →
  location update → anomaly rules. Gate readers infer direction: an asset with an on-site location is **leaving**;
  an off-premises asset is **returning** (entry, no pass needed).
- Exit **with** a valid approved pass (approved, not past its return date) → `gate_pass` movement, asset off-premises.
  Exit **without** one → a **pending pass is raised automatically** (`source: rfid_gate`, the gate recorded), the asset
  is held at the gate and an `unauthorized_movement` alert is raised. Repeat scans reuse the pending pass.
- **Approve** (`services/gate-pass-workflow.ts`): if the asset is at a gate (always true for auto-raised passes) the exit
  is recorded immediately as a `gate_pass` movement; a pass approved in advance records it when a gate reader sees
  the asset leave (no fictional exits). The related alert is resolved.
- **Reject**: flags the asset — critical `unauthorized_movement` insight (warning for requests made in advance) and a
  note in the asset history. Passes can be decided once (409 otherwise); one pending pass per asset.
- **Request gate pass** form (`components/gate-pass-request-dialog.tsx`, uses `useCreateGatePass`) on the Gate Passes
  page and the asset detail page.
- Contract additions: `GatePass.source / gateLocationId / gateLocationName / decidedAt / exitedAt`,
  `MovementEvent.gatePassId`, `AIInsight.gatePassId`; `POST /rfid/simulate` now returns `{ movement, gateCheck, insights }`.
- DB: new columns `gate_passes.source / gate_location_id / decided_at`, `movements.gate_pass_id`,
  `ai_insights.gate_pass_id` — additive, so `pnpm db:push` (run by Replit's post-merge script) applies them without data loss.

Test: `src/routes/rfid.test.ts`, `src/routes/gate-passes.test.ts`, `src/services/gate-pass-rules.test.ts`.
Manually: RFID Simulator → *Dell Latitude 5440* + *TSAS Main Gate Reader* → Trigger Scan (pending pass + toast) →
Gate Passes → Approve → Movements shows the *Gate pass* exit → scan the same tag at the gate again = *Entry*.

### 2. Real approver instead of "Admin User"
- Approve / reject open a dialog that **requires the approver's name** (pre-filled from "Acting as" in the sidebar,
  stored in this browser) and an optional note. The API rejects blank names (`minLength: 1` + trim) and records
  `approvedBy` + `decidedAt`.

Test: `gate-passes.test.ts` → "requires a real approver name". Manually: set your name via *Acting as* in the sidebar.

### 3. New screens (same shadcn style)
- **Location create / edit**: `/locations/new`, `/locations/:id/edit` (`pages/locations/new.tsx`, `edit.tsx`,
  `components/location-form.tsx`); *Add Location* button and a pencil on every location card.
- **Manual movement**: `/movements/new` (`pages/movements/new.tsx`), opened from *Record Movement* on the Movement Log
  or *Move* on an asset. Warns when the destination is a gate (a move doesn't authorise an exit) or another SBU.
- **Dashboard charts** (`components/dashboard-charts.tsx`): movements per day by trigger (stacked columns, new
  `GET /dashboard/movement-trend`) and asset status by SBU. Palette validated for colour-blind safety; each chart has a
  Chart/Table toggle.

Test: `src/routes/screens.test.ts` (endpoints). Manually: create a location, edit it, record a movement into it.

### 4. Anomaly engine (`services/anomaly-rules.ts`, pure functions)
- **Location mismatch**: asset SBU ≠ SBU of the location that read it → `anomaly` (warning).
- **Out-of-hours**: scan timestamp outside Mon–Fri 07:00–18:00 in `ORG_TIMEZONE` → `unusual_pattern`
  (warning at gates, info elsewhere). The simulator can replay a scan at any past time (`scannedAt`).
- **Unauthorised exit**: gate exit without an approved pass → `unauthorized_movement` (warning, **critical** at night).
- Authorised exits and entries raise **nothing** — the "every gate scan is a warning" insight is gone.
- Identical open alerts are de-duplicated; idle/missing rules use one query instead of one per asset; never-moved
  assets are measured from registration (a new asset is no longer "idle").

Test: `src/services/anomaly-rules.test.ts`, `src/lib/time.test.ts`. Manually: Simulator → tick *Replay the scan* →
*Last night 02:00*; or scan an Agricura asset at a TSAS reader.

### 5. Natural-language queries (`services/nl-query/`)
- Answers the plan's three questions — *"Where is the Dell laptop assigned to John Doe?"*, *"Which assets have not
  moved in 30 days?"*, *"Show me all gate passes approved this week"* — plus missing/maintenance, category counts,
  unassigned assets and assets held by a person. Answers link the related assets / gate passes and say which parser
  understood the question.

Test: `src/services/nl-query/rule-parser.test.ts`, `openai-parser.test.ts`, `src/routes/ai-query.test.ts`.
Manually: AI Insights → click the three example chips.

### 6. Decisions (recorded in `docs/project-plan.md` §13; plan sections 4.3, 5, 6.2–6.6, 7, 11, 12 updated to match)
- **OpenAI: implemented with rules as fallback.** OpenAI only maps the question to an intent (JSON-schema structured
  output); the database answers. Only the question text is sent. Set `OPENAI_API_KEY` (and optionally
  `OPENAI_MODEL`, default `gpt-4o-mini`; Azure: `OPENAI_BASE_URL` + `OPENAI_AUTH_HEADER=api-key`). No key, an error or
  a timeout (`OPENAI_TIMEOUT_MS`, 8 s) → rule-based parser.
- **Business Central: manual sync only for the MVP.** Mock connection, *Sync Now*, and a *pending changes* count
  (movements + gate-pass decisions since the last sync). The old page could never sync (button disabled until
  "connected") — fixed.
- **Gate-pass notifications: in-app.** Pending passes polled every 10 s → amber badge on *Gate Passes* and a toast
  with *Review* when a new pass appears.

### 7. Tests (`pnpm test`)
- Vitest in `artifacts/api-server`. Pure rule modules have unit tests. Route tests start the real Express app against
  **PGlite** (in-memory PostgreSQL built from the Drizzle schema via `drizzle-kit/api`); `src/test/setup.ts` swaps
  `@workspace/db` for it. Tests pin the clock to Wednesday 14 Oct 2026, 10:00 Harare.
- Mutation-checked: reintroducing "every gate scan is a warning" or the hard-coded approver fails 9 tests.

### 8. Seed script (`scripts/src/seed.ts`, data in `seed-data.ts`)
- 5 SBUs, 17 locations, 12 readers (one offline), 32 assets, ~96 movements, 6 gate passes (one auto-raised and
  pending — approve it live), lifecycle history, and alerts for an out-of-hours scan, a location mismatch and a
  rejected pass. Dates are relative to "now". Refuses to run on a non-empty DB unless `--reset`.

### 9. Environment + this file
- Windows support: cross-platform `preinstall` (`scripts/preinstall.mjs`), win32-x64 native binaries no longer
  stripped in `pnpm-workspace.yaml`, API dev script uses `tsx watch` (no `export`), drizzle schema path fixed for
  Windows, Vite falls back to port 5173 / base `/` and proxies `/api` when not on Replit (Replit behaviour unchanged).
  The mockup sandbox gets the same off-Replit defaults, so `pnpm run build` now succeeds locally.
- `.env.example`, `.vscode/` (tasks, launch, settings, extensions), `artifacts/api-server/requests.http`,
  root scripts (`pnpm test`, `pnpm seed`, `pnpm db:local`, `pnpm dev:api`, …).

### Other fixes found along the way
- `POST /locations` now returns SBU name/code; `PATCH /locations/:id` sets `updatedAt` and returns 404 for unknown ids.
- `GET /gate-passes?sbuId=` and `GET /movements?sbuId=` now filter (the parameters were ignored).
- "Today's movements" uses the organisation's day, not the server's (UTC on Replit).
- Invalid ids return 400 instead of a database error; unknown assets/locations on create return 404 instead of 500.
- Gate activity shows entry/exit direction and includes gate-pass exits; the activity feed names auto-raised passes and approvers.

## Conventions for future changes
- **Contract first**: edit `lib/api-spec/openapi.yaml` → `pnpm codegen` → commit the generated files. Never hand-edit
  `lib/*/src/generated`.
- **Routes stay thin**; business logic goes in `artifacts/api-server/src/services`, with rules as pure functions so
  they can be unit tested. Throw `HttpError` for 4xx; routes render it via `sendError`.
- **Time**: columns are `timestamp without time zone` holding UTC, written from the API's clock. "Today", "this
  week" and working hours use `ORG_TIMEZONE` helpers in `src/lib/time.ts`, never the server's timezone.
- **New rule or workflow step?** Add a failing test first (unit test for the rule, route test for the behaviour).
- Server code logs with `req.log` / `logger`, never `console.log` (scripts may print).
- DB schema changes must stay additive for Replit (`pnpm --filter db push` runs on merge).

## Not verified
- The OpenAI call has not been tried against the live API (no key here). It is covered by tests with a stubbed
  response, and any failure falls back to the rules.
- Replit and Azure deployments were not re-run. On Replit, run `pnpm --filter db push` (post-merge does it) to add the
  new columns before using the new features.
