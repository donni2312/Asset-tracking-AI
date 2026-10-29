# CLAUDE.md — AssetIQ (TSL asset tracking)

AssetIQ is an RFID-driven asset tracking MVP for TSL Group: React dashboard + ASP.NET Core API (C#, EF Core) +
PostgreSQL in a pnpm monorepo, contract-first from `lib/api-spec/openapi.yaml`. The degree-project plan (architecture, decision log,
testing) is `docs/project-plan.md`; the Replit agent's notes are `replit.md`. This file explains how to run and
test the project locally and records everything changed in the October 2026 pass.

## Commands

| Command | What it does |
|---|---|
| `pnpm install` | Install all workspace dependencies |
| `pnpm db:local` | Run a local PostgreSQL 16 in this terminal (port 5433, Ctrl+C stops it). `pnpm db:local status` / `stop` manage a leftover server |
| `pnpm db:migrate` | Apply `lib/db/migrations` in order (Phase 1 onwards). A database created earlier with `db:push` gets its baseline recorded first, then only the new migrations run |
| `pnpm db:push` | Throwaway local databases only: sync tables straight from `lib/db/src/schema` (no migration history, no triggers) |
| `pnpm seed` / `pnpm seed --reset` | Load demo data into an empty DB / wipe every AssetIQ table first |
| `pnpm dev:api` | C# API on http://localhost:8080/api with hot reload (`dotnet watch`, reads `.env`) |
| `pnpm dev:web` | Dashboard on http://localhost:5173 (Vite hot reload, proxies `/api` to 8080) |
| `pnpm test` | All automated tests (xUnit via `dotnet test`). No running database needed: a throwaway PostgreSQL is started |
| `pnpm --filter @workspace/api-server run test:watch` | Tests in watch mode (`dotnet watch test`) |
| `pnpm run typecheck` | Typecheck every TypeScript package and compile the C# solution |
| `pnpm codegen` | Regenerate React Query hooks + Zod schemas after editing `openapi.yaml` |
| Demo sign-ins | `administrator`/`admin123` (Group IT) and one account per Phase 2 role, password = username + `-2026` (`group.finance`, `tsas.it`, `tsas.gate`, `tsas.hod`, `john.doe`, `sys.admin`, …; listed by `pnpm seed`). `operator` / `viewer` are seeded disabled (retired roles). Rules: Phase 2 section |
| `pnpm run build` | Typecheck + production builds (the API is published to `artifacts/api-server/dist`) |
| `pnpm build:azure` | Build the dashboard, then publish the API with it in `dist/wwwroot` — deploy that folder to Azure App Service (section 19) |

## Local development (Windows + VS Code)

### What is installed on this PC
- **Node.js 24.22.0 LTS** (portable, no admin) in `%LOCALAPPDATA%\Programs\nodejs`, added to the **user PATH**,
  plus **pnpm 10.34.7** in the same folder. Restart VS Code once so new terminals see it
  (`.vscode/settings.json` also prepends it for VS Code terminals). To remove: delete that folder and the PATH entry.
- **.NET 8 SDK** (8.0.426) in `C:\Program Files\dotnet`, on the machine PATH. A VS Code window opened before the
  install does not see it — restart VS Code. The pnpm scripts find it anyway (`artifacts/api-server/dotnet.mjs`) and
  `.vscode/settings.json` prepends it for terminals. Recommended extension: C# (`ms-dotnettools.csharp`).
- **PostgreSQL 16** comes from the `embedded-postgres` npm package (no admin, no Docker). `pnpm db:local` keeps its
  data in `%LOCALAPPDATA%\AssetIQ\pgdata` (outside OneDrive) and its log in `%LOCALAPPDATA%\AssetIQ\postgres.log`.
  It listens on localhost only; user/password `postgres`/`postgres`, database `assetiq`, server timezone UTC.
  That cluster is already initialised and seeded.
- **`.env`** (git-ignored) was created from `.env.example`. It holds `DATABASE_URL`, `PORT`, `AUTH_SECRET` /
  `AUTH_TOKEN_TTL_HOURS` and the optional AI / working-hours settings. Any PostgreSQL works: point `DATABASE_URL` at
  it instead of using `pnpm db:local`. `AUTH_SECRET` on this PC is a random 64-character key (see section 16) —
  never commit it or copy it into another environment.

### Where the working copy lives — keep it out of OneDrive
Work from `C:\dev\Asset-Tracking-AI`. That is the active copy and the one these instructions assume.

GitHub (`origin`) is the only thing that should ever carry the project between machines. Never keep a second
working copy inside OneDrive: `pnpm install` writes tens of thousands of files into `node_modules`, which OneDrive
uploads to the TSL tenant and can lock mid-install, and two copies silently drift apart.

To set the project up on this or any other machine:

```powershell
git clone https://github.com/donni2312/Asset-tracking-AI.git C:\dev\Asset-Tracking-AI
code C:\dev\Asset-Tracking-AI
```

Then copy `.env.example` to `.env` and fill it in — `.env` is git-ignored, so a clone never brings it along.

If a stray copy ever turns up under `OneDrive - TSL Ltd\...\Donn's Coding Projects\`, delete it from
**onedrive.com**, not from Explorer. Deleting a synced folder locally does not stick: the sync client holds
handles on the directories and restores the whole tree from the cloud a minute later. Deleting it online syncs
the removal down instead, and keeps it in the online Recycle Bin for 30 days.

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
  *API: debug* (C# breakpoints), *Tests: attach to the API test host*, *Seed: debug*.
- **Hit the API live**: `artifacts/api-server/requests.http` with the REST Client extension (Send Request).
- **Tests**: Testing sidebar (C# extension) or Run Task → *Test: API (xUnit)* / *Test: API (watch mode)*.
- **Inspect the DB**: PostgreSQL extension → localhost:5433, user `postgres`, password `postgres`, database `assetiq`.

### Using Supabase (hosted PostgreSQL)
Supabase is plain PostgreSQL, so no code changes are needed: both the C# API and the Node tooling (`pnpm db:push`,
`pnpm seed`) only read `DATABASE_URL`. `pnpm test` still uses its own throwaway database and never touches Supabase.

1. **Create the project** at https://supabase.com/dashboard → **New project**. Generate a database password and keep
   it in a password manager. There is no Africa region; pick the one closest to where the API runs.
2. **Copy the Session pooler URL**: **Connect** → **Session pooler** (port 5432), of the form
   `postgresql://postgres.<project-ref>:<PASSWORD>@aws-0-<region>.pooler.supabase.com:5432/postgres`.
   - Not the **Direct connection** (`db.<ref>.supabase.co`): it is IPv6-only and fails on most networks here.
   - Not the **Transaction pooler** (port 6543): it does not support the prepared statements Npgsql uses.
   - URL-encode special characters in the password (`@` → `%40`, `#` → `%23`, `/` → `%2F`).
3. **Download the SSL certificate**: Project Settings → Database → SSL Configuration → **Download certificate**, saved
   as `certs/supabase-ca.crt`. node-postgres treats `sslmode=require` as `verify-full`, and Supabase's certificate is
   signed by its own CA, so without this file the Node tooling fails with "self-signed certificate in certificate
   chain". (It is a public CA certificate, not a secret.)
4. **Point `.env` at it**, keeping the local URL commented out so you can switch back:
   ```env
   # DATABASE_URL=postgres://postgres:postgres@localhost:5433/assetiq
   DATABASE_URL=postgresql://postgres.<project-ref>:<PASSWORD>@aws-0-<region>.pooler.supabase.com:5432/postgres?sslmode=require&sslrootcert=C:/dev/Asset-Tracking-AI/certs/supabase-ca.crt
   ```
   The API (`Configuration/DatabaseUrl.cs`) maps `sslmode=require` to Npgsql's `SslMode.Require` and ignores
   `sslrootcert`. Fallback if the certificate path is not picked up: replace `&sslrootcert=...` with
   `&uselibpqcompat=true` (still encrypted, but the certificate is not verified).
5. **Create the tables and demo data**: `pnpm db:migrate`, then `pnpm seed` (no `pnpm db:local` needed). Check
   **Table Editor** in Supabase.
6. **Run it**: `pnpm dev:api` and `pnpm dev:web` (the *Dev: full stack* task also starts the local PostgreSQL, which
   is harmless but unused). Sign in as `administrator`/`admin123` and check the dashboard shows the seeded assets.
7. **Turn off Supabase's own REST API**: Project Settings → **Data API** → disable it, or remove `public` from the
   exposed schemas. Otherwise every table in `public` is readable with the project's public key, bypassing the
   app's sign-in. (Alternative: enable Row Level Security on every table with no policies; the API connects as the
   table owner and is unaffected.)
8. **Deploying**: set the same `DATABASE_URL` as a secret on the host, and ship the certificate with the app if the
   Node tooling runs there (the API alone does not need it). The free plan pauses a project after about a week
   without activity; unpause it from the dashboard before a demo.

## Changes made in October 2026 — and how to test them

> Sections 1–14 were written for the Express (TypeScript) API that section 15 replaced with C#. The behaviour they
> describe is unchanged; the TypeScript files they cite map to the C# files listed in section 15.

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

### 10. Dashboard cards are drill-down links
Every card on the Operations Dashboard (`pages/dashboard.tsx`) now navigates to the screen behind its number, so a
figure is never a dead end:

| Card | Goes to |
|---|---|
| Total Assets | `/assets` |
| Missing Assets | `/assets?status=missing` |
| Pending Gate Passes | `/gate-passes` (the page already opens on the *pending* tab) |
| Today's Movements | `/movements` |
| SBU breakdown tile | `/assets?sbuId=<id>` — was `/sbus`, which ignored which SBU had been clicked |
| Recent Activity | `/movements` |
| Gate Activity | `/gate-passes` |
| Asset Distribution | `/locations` |
| *Movements per day* / *Asset status by SBU* charts | `/movements` / `/sbus` |

- The stat, SBU and list cards are **whole-card** `wouter` links (`CardLink` / `StatCard` helpers at the top of
  `dashboard.tsx`) with a hover shadow and a focus ring; the list cards carry a muted "View all →" hint
  (`ViewAllHint`) so the target is visible. That hint is a `<span>`, not a link — an `<a>` inside an `<a>` is invalid
  HTML and breaks keyboard navigation.
- The two chart cards get a **header link only** (`ViewAllLink`, optional `viewAllHref` prop in
  `components/dashboard-charts.tsx`). They contain the Chart/Table toggle and chart tooltips, so a whole-card link
  would swallow those clicks.
- `pages/assets/list.tsx` now reads **`?status=`** (validated against the generated `ListAssetsStatus` enum) and passes
  it to `useListAssets`, with a "Filtered to *Missing* assets ✕" chip to clear it. Without this the Missing Assets card
  would have landed on the unfiltered registry. `?sbuId=` already worked.

Test: no new automated tests — this is navigation only; `pnpm run typecheck` and `pnpm run build` are clean.
Manually: from the dashboard click each card and each "View all" hint; confirm Missing Assets arrives filtered and the
chip clears it, an SBU tile arrives filtered to that SBU, and the chart cards' toggles still switch Chart/Table
without navigating.

### 11. Dark mode + a Settings panel, and the sidebar no longer scrolls away
- **Theme tokens**: `index.css` gained a `.dark` block with the same token names as `:root` (the
  `@custom-variant dark (&:is(.dark *))` hook was already there), so every component follows without change. The
  sidebar stays darker than the page, as in light mode. `color-scheme` is set in both themes so native controls,
  scrollbars and `<select>` menus match.
- **`hooks/use-theme.ts`**: `light` / `dark` / `system`, stored under `assetiq.theme` in localStorage, same
  `useSyncExternalStore` shape as `use-operator.ts`. `system` keeps following the OS while the app is open
  (`prefers-color-scheme` listener) and a change in another tab is picked up via the `storage` event.
  `useResolvedTheme()` gives what is actually on screen — use it for labels, not the stored preference.
- **No white flash**: a small inline script in `index.html` adds the `dark` class before first paint. It mirrors the
  hook and must keep using the same storage key.
- **`components/settings-dialog.tsx`**: `SettingsButton` in the sidebar footer (next to *Acting as*) is a
  **Settings** button plus a one-click sun/moon toggle. The button opens a panel with Appearance
  (Light / Dark / System, the system option showing which one is in effect) and the operator's name — the same
  `useOperatorName` store as the badge, re-read whenever the panel opens so the two cannot drift apart.
- **Locked sidebar** (`components/layout.tsx`): the shell is `md:h-[100dvh] md:overflow-hidden` and the `<aside>` is
  `md:sticky md:top-0 md:h-[100dvh]`, so on desktop only the main column scrolls and the navigation (and the pending
  gate-pass badge) stays in view. Its own nav list still scrolls if the menu outgrows a short window. Below `md`
  the sidebar stays in normal flow above the content, as before.
- **Hard-coded light colours fixed**: ~108 class tokens across 11 page/component files had literal Tailwind palette
  colours (`bg-amber-50 text-amber-900`, status badges, alert banners, the location-type chips) that would have been
  unreadable on a dark surface; each now carries a `dark:` counterpart. The three badges whose text sits on a **solid**
  `bg-*-500` (the pending-count badges and the simulator's Trigger Scan button) were deliberately left alone — their
  dark text is correct in both themes. The chart palettes in `dashboard-charts.tsx` are mid-tone hexes that read on
  either surface and were not changed.

Test: no new automated tests (presentation only); `pnpm run typecheck` clean, `pnpm run build` succeeds, `pnpm test`
still 9 files / 111 tests. Manually: sidebar → *Settings* → switch Light / Dark / System and reload (the choice sticks
with no white flash); flip the OS theme with *System* selected; scroll a long page (Assets, Movements) and confirm the
sidebar stays put; check the amber pending badge, the dashboard alert banners, asset status badges, the location-type
chips and the RFID simulator result panels in dark mode.

### 12. One date format for the whole system (dd/mm/yyyy by default)
- **`lib/date-format.ts`** is now the only place a date is turned into text: `formatDate` (instant: `20/10/2026 14:05`),
  `formatDateOnly` (calendar day), `formatTime` and `formatDayLabel` (chart axes / table days). The preference is
  `dd/mm/yyyy` (default, as written in Zimbabwe), `mm/dd/yyyy` or `yyyy-mm-dd`, stored under `assetiq.dateFormat`.
  The clock style follows the date order — month-first gets a 12-hour clock, the other two 24-hour.
- `lib/utils.ts` re-exports those four, so the ~30 existing `import { formatDate } from "@/lib/utils"` call sites were
  left untouched. The old implementation was a hard-coded `en-US` `Intl.DateTimeFormat` ("Oct 20, 2026, 2:05 PM").
- **Dates that bypassed the formatter** now go through it: gate-pass **return dates** (decision dialog and the Gate
  Passes table) and the asset **purchase date** printed the raw `YYYY-MM-DD` from the API; the gate-activity and AI-chat
  clocks and the chart axis labels each called `toLocaleTimeString` / `toLocaleDateString` with their own locale.
- **Date-only values are not instants.** `new Date("2026-12-31")` is parsed as UTC midnight and shows 30/12 in any
  negative-offset timezone, so `YYYY-MM-DD` strings are formatted from the string's own parts and the weekday behind a
  chart label is computed in UTC. (Harare is UTC+2, so this never showed up locally — it would have in Replit's UTC
  or a US timezone.)
- **How a change reaches every screen**: the formatters are plain functions that read the stored preference, so
  `Layout` subscribes (`useDateFormat`) and keys the page-content wrapper on it — switching the format re-mounts the
  current page. The sidebar and the open Settings panel sit outside that keyed element and survive, so the new format
  is visible behind the dialog immediately. The trade-off is that page state (a search box, a filter tab) resets on a
  format change; that is a deliberate, rare action, and it is what keeps a date from being formatted two ways on one
  screen. **Keep formatting dates through `lib/date-format.ts`** — a component that calls `toLocaleDateString` itself
  will silently ignore the setting.
- Settings → **Date format** lists the three options with a live preview of the current time in the chosen format.

Test: no new automated tests (presentation only); `pnpm run typecheck` clean, `pnpm run build` succeeds, `pnpm test`
still 9 files / 111 tests. The formatters were checked directly for all three formats: instants, calendar days
(`2026-01-05` stays 5 January), times, axis labels, and `null` / unparseable input → "—".
Manually: Settings → Date format → each option, then check the Movement Log, an asset's history, the Gate Passes table
(*Due back*), BC Sync's last-sync line, the dashboard activity feed and the movements chart axis.

**Known limit:** native `<input type="date">` / `datetime-local` pickers (gate-pass return date, the simulator's replay
time) render in the **operating system's** locale — no web app can restyle them. Only displayed dates follow the setting.

### 13. Pickers: find an asset by typing, choose a person from the people on record
- **`components/searchable-select.tsx`** replaces the long native `<select>` of every asset (32 in the demo, far more
  in production). The query is matched token by token against the asset's **name, RFID tag, serial number, category,
  SBU and current location**, so "latitude dell", "tsas-0001" or a serial all find the same row; each row shows the
  tag / serial / location and the SBU badge. Used by **Request gate pass** and **Record Movement**.
- **`hooks/use-people.ts`** builds the people list. There is no users table and no authentication in the MVP, so
  people exist only as names on records: `assets.assignedTo`, `gate_passes.requestedBy / approvedBy` and
  `movements.movedBy`. The hook gathers those from the already-cached list queries, de-duplicates
  **case-insensitively** (keeping the most common spelling, so a stray "tendai moyo" doesn't replace "Tendai Moyo")
  and annotates each name with how many assets they hold and where they appear.
- The same picker in `allowCustom` mode backs **Requested by** (gate pass), **Your name (approver)** (approve/reject)
  and **Moved by** (manual movement): it offers everyone on record with the same type-to-filter, and still accepts a
  name that is not in the database yet — a new employee must not be blocked from requesting a pass. The list says so
  when the typed name is new.
- It is **not** a Radix Popover: the list renders inline, so it cannot fight the surrounding Dialog over focus.
  Keyboard: ↑/↓ move, Enter selects (without submitting the form), Esc closes the list only, Tab closes.
- **`components/date-field.tsx`** replaces `<input type="date">` on the gate-pass return date. The native control
  renders in the **operating system's** locale, which was the one place a date ignored the Settings format (§12).
  The field now reads and writes in the chosen order (`dd/mm/yyyy` placeholder), accepts `/`, `-` or `.` as
  separators, rejects impossible days (31/02, 29/02 in a non-leap year) with an inline message, and keeps a calendar
  button — the shadcn `Calendar`, rendered inline for the same focus reason. The API still receives `YYYY-MM-DD`.

Test: no new automated tests (UI only); `pnpm run typecheck` clean, `pnpm run build` succeeds, `pnpm test` still
9 files / 111 tests. `parseDateInput` and the search filter were exercised directly over all three date formats:
`1/1/2027`, `31-12-2026`, leap days (29/02/2028 valid, 29/02/2027 not), `31/02`, month/day swapped for the format,
and free text → invalid; and the filter over name / tag / serial / assignee / out-of-order tokens / non-matches.
Manually: Gate Passes → *Request gate pass* → type "dell", a serial, or an RFID tag; pick a person from the list and
then type a name that is not on it; type `31/12/2026` and check the pass shows *Due back 31/12/2026*.

**Still typed free-hand:** the RFID simulator's replay time is a `datetime-local` input (it needs a time as well as a
date) and keeps the OS format. The asset registry's own search box is unchanged — it queries the API.

### 14. Editing an asset, behind real authentication
**Before using this, run `pnpm db:push` (creates the `users` table) and `pnpm seed --reset` (creates the sign-ins).**
Demo accounts: `administrator` / `admin123`, `operator` / `operator123`, `viewer` / `viewer123`.
**Superseded by section 17 and then by Phase 2:** the role rules below are the original ones.

- **Contract** (`openapi.yaml`): `POST /auth/login` → `{ token, expiresAt, user }`, `GET /auth/me`, a `bearerAuth`
  security scheme, and `security` + 401/403 responses on the three asset write operations. Regenerated with
  `pnpm codegen`, which is where `useLogin`, `useGetCurrentUser` and the `User` / `UserRole` types come from.
- **DB** (`lib/db/src/schema/users.ts`): `users` — username, display name, role, `password_hash`, `active`,
  `last_login_at`. Additive, so `pnpm db:push` applies it without data loss. Seeded with one account per role.
- **Passwords and tokens** (`api-server/src/lib/auth.ts`): scrypt with a per-password salt and a constant-time
  compare; compact HS256 JWTs signed with `AUTH_SECRET`. Both use `node:crypto` — no new dependency, which matters
  on the TSL network. **Without `AUTH_SECRET` the process signs with a random key** generated at startup: the app
  runs, but a restart signs everyone out. Never add a hard-coded fallback secret — it would let anyone mint an
  admin token. Set it in `.env` (see `.env.example`).
- **Middleware** (`api-server/src/middlewares/auth.ts`): `authenticate` runs on every `/api` request and attaches
  `req.user` when a valid token is present — it never rejects, so the public endpoints are unaffected and each route
  states its own requirement with `requireRole`. The role is **re-read from the database on every request**, not
  trusted from the token, so demoting or disabling an account takes effect immediately instead of when the token
  expires. 401 means "sign in", 403 means "ask someone else" — the two are kept distinct.

| Role | May do |
|---|---|
| `viewer` | read everything |
| `operator` | + change an asset's **operational** fields: status, assignedTo, currentLocationId, notes |
| `admin` | + change the **record** (name, category, sbuId), register and delete assets |

  The split is enforced in `PATCH /assets/:id` by inspecting which fields the body actually contains, so an operator
  cannot smuggle `sbuId` in beside a `status` change — and the existing operator workflows (deploy, repair, dispose,
  which all PATCH the asset) keep working without an admin.
- **Front end**: `hooks/use-auth.ts` stores the session in localStorage and registers the generated client's
  `setAuthTokenGetter`, so every request carries the bearer token; signing in also sets the "Acting as" name, so
  approvals are recorded against the real user. `components/sign-in.tsx` adds the sidebar account badge and the
  sign-in dialog. `pages/assets/edit.tsx` (route `/assets/:id/edit`) is the form, reached from an **Edit** button on
  the asset detail page — shown to operators and admins, and a disabled lock with a reason for everyone else.
  Admin-only fields are disabled with a lock icon for operators, and the form **sends only the fields that changed**
  so an unchanged name does not trip the admin check. Serial number and RFID tag are read-only: they are not in
  `AssetUpdate` and identify the physical asset.
  **The UI gate is cosmetic** — it decides what is offered, never what is allowed. Anyone editing localStorage gets a
  403 from the API, not an edit.

Test: `src/routes/auth.test.ts` — 17 tests: sign-in (wrong password answers exactly like an unknown user, disabled
accounts, `lastLoginAt`, no hash in the response), `/auth/me` (missing / forged / expired tokens), and the whole
permission matrix including the smuggled-field case, demotion taking effect on the next request, and reading staying
open. Mutation-checked: dropping `requireRole` from the PATCH route or the admin field check fails 6 of them.
Suite is now **10 files, 128 tests**; `pnpm run typecheck` and `pnpm run build` clean.
Manually: open an asset, confirm Edit is locked, sign in as `operator` (can change status/holder, name is locked),
then as `admin` (everything), then sign out.

**Not yet gated (at the time):** only the asset write routes checked a role. Section 17 gates every other write.

### 15. Backend rewritten in C#: ASP.NET Core 8 + EF Core 8 + PostgreSQL
The Express API was replaced by an ASP.NET Core Web API in `artifacts/api-server` (branch `csharp-backend`; the
Express version stays on `main`). **The database is unchanged** — same tables, columns and data; `lib/db` (Drizzle
schema, `pnpm db:push`) and `scripts/` (seed, local PostgreSQL) still own it. **The dashboard is unchanged** — same
routes under `/api`, same JSON shapes, status codes and `{ "error": … }` bodies, so the generated client works as is.

| Was (TypeScript) | Now (C#, `src/AssetIQ.Api/`) |
|---|---|
| `src/app.ts`, `src/index.ts` | `Program.cs` (pipeline, DI, PORT, `.env` loading) |
| `src/routes/*.ts` | `Controllers/*Controller.cs` (thin; `[FailureMessage]` gives each its 500 text) |
| `src/services/*.ts`, `nl-query/*` | `Services/*.cs`, `Services/NlQuery/*` (same names; rules stay pure static classes) |
| `src/lib/config.ts`, `time.ts` | `Configuration/AppConfig.cs`, `Infrastructure/ZonedTime.cs` |
| `src/lib/auth.ts`, `middlewares/auth.ts` | `Auth/` — scrypt, HS256 tokens, an ASP.NET authentication handler, `[RequireRole]` policies |
| `src/lib/http-error.ts` | `Infrastructure/ApiErrors.cs` (`ApiException`, error middleware) |
| Zod request validation | `Infrastructure/JsonBody.cs` (same required / optional / nullable rules) |
| Drizzle queries | `Data/AssetIqDbContext.cs` + `Data/Entities.cs` (EF Core, Npgsql) |
| Vitest + PGlite (`src/**/*.test.ts`) | `tests/AssetIQ.Api.Tests` (xUnit + WebApplicationFactory, throwaway PostgreSQL) |

- **Mapping onto the existing schema**: entities use explicit table names and snake_case columns; foreign keys are
  `NO ACTION` as in the database; EF never creates or migrates anything. Timestamps stay UTC in
  `timestamp without time zone` columns (a value converter handles the kind), are written from the API's clock at
  millisecond precision, and go over the wire exactly like JavaScript's `toISOString()` (`…T08:20:00.000Z`).
- **Sign-ins carry over**: scrypt hashes in the seed's `scrypt$salt$hash` format verify unchanged (scrypt is
  implemented in `Auth/Scrypt.cs`, checked against RFC 7914 and hashes made by Node), and HS256 tokens are byte-for-byte
  the Node format — a browser signed in before the switch stays signed in if `AUTH_SECRET` is the same.
- **Configuration**: the same environment variables (`.env.example`). `DATABASE_URL` stays a `postgres://` URL.
- **Transactions and locks** are the same: the RFID pipeline, gate-pass request/approve/reject run in one transaction
  with `SELECT … FOR UPDATE` on the asset and an atomic "claim the pending pass" update.
- **Deliberate differences** (none visible to the dashboard): `purchaseValue` is a JSON number everywhere, as the
  contract says (the Express API sent a string on most asset endpoints); validation 400s read
  `requestedBy: String must contain at least 1 character(s)` instead of a serialised Zod issue list (the dashboard
  shows either); ids that are not positive integers get a 400 everywhere; invalid query filters are ignored instead of
  a 500; `GET /lifecycle?sbuId=` now filters (it was ignored); `/sbus/:id/assets` and `/locations/:id/assets` return
  the full `Asset` shape; `POST /assets/:id/lifecycle` saves the event and the asset change in one transaction.

Test: `pnpm test` — **181 tests** (unit tests for every rule module, crypto and parsing; integration tests ported from
every Vitest route file; `SchemaConformanceTests` fails if the EF model and `Database/schema.sql` — drizzle-kit's DDL —
ever disagree). The integration tests start a private PostgreSQL 16 from the `embedded-postgres` binaries in
`node_modules` (or use `ASSETIQ_TEST_DATABASE_URL`), never the development database.
Parity was also checked by running the old Express API and the C# API side by side on identical copies of the seeded
data and replaying 157 requests (reads, every mutation, error cases, sign-in, NL questions): all 157 responses matched.
Manually: `pnpm dev:api`, then use the dashboard as before.

### 16. A fixed AUTH_SECRET, so sign-ins survive restarts
Until now `.env` had no `AUTH_SECRET` (it predates section 14), so the API signed tokens with a key generated at
startup: every restart — including each `dotnet watch` restart after a code change — signed everyone out, and the API
logged `AUTH_SECRET is not set` at startup.

- `.env` now has `AUTH_SECRET` (64 hex characters = 32 random bytes from a cryptographic generator) and
  `AUTH_TOKEN_TTL_HOURS=12`, under `PORT`, matching `.env.example`. Nothing else changed; `.env` stays git-ignored.
- **Each environment needs its own secret** (Replit Secrets, Azure App Service settings). Never reuse this one there
  and never hard-code one: anyone who knows the key can mint an admin token.
- **Changing the secret signs everyone out** (existing tokens stop verifying) — that is also how to revoke every
  session at once.
- Generate a new one in PowerShell (`.env.example` also shows the Node.js one-liner):
  ```powershell
  $b = New-Object byte[] 32; [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($b); -join ($b | % { $_.ToString('x2') })
  ```
- `.env` is read when the API starts; `dotnet watch` does not watch it, so restart the "API: dev server (watch)" task
  after editing it.

Test: covered by the existing auth tests (fixed test secret, forged/expired tokens). Manually: start the API — the
`AUTH_SECRET is not set` warning no longer appears (verified). Once the `users` table exists (`pnpm db:push`), sign
in, restart the API task and reload the dashboard: you stay signed in.

### 17. Sign-ins created, and every change now needs the right role (superseded by Phase 2)
**Database:** `pnpm db:push` created the missing `users` table on this PC (an additive change — no other table or row
was touched; 32 assets before and after). Three accounts were inserted directly (not `pnpm seed --reset`, which would
have wiped the demo data), with scrypt hashes in the seed's format:

| Sign-in | Name on records | Role |
|---|---|---|
| `administrator` / `admin123` | Rudo Chikwanha | `admin` |
| `operator` / `operator123` | Tendai Moyo | `operator` |
| `viewer` / `viewer123` | Nyasha Gumbo | `viewer` |

`scripts/src/seed-data.ts` (`DEMO_USERS`) now uses `administrator` too, so `pnpm seed --reset` creates the same three.

**Permissions** (enforced by the API with `[RequireRole]`; viewer < operator < administrator):

| Action | Signed out | Viewer | Operator | Administrator |
|---|---|---|---|---|
| View everything (dashboard, lists, history), ask the AI assistant | ✓ | ✓ | ✓ | ✓ |
| Record an asset movement (`POST /movements`) | – | ✓ | ✓ | ✓ |
| Register, edit (**every field**, incl. name, category, SBU) or delete assets; lifecycle events; create/edit locations and SBUs; request, approve and reject gate passes; dismiss insights | – | – | ✓ | ✓ |
| RFID simulator (`POST /rfid/simulate`), Business Central sync (`POST /bc/sync`) | – | – | – | ✓ |

- A refused call is **401 "Sign in to continue"** when signed out and **403 "This action needs the operator role; you
  are signed in as viewer."** otherwise. Reading stays open, so the dashboard still works signed out.
- Changes from section 14: operators may now change the asset record itself and register / delete assets (the
  operational-vs-record field split is gone); every other write — which was open to anyone — now needs a role.
- `lib/api-spec/openapi.yaml` states the role and the 401/403 responses on each of these operations; `pnpm codegen` was
  re-run (generated client: comments and the error type of six mutations only).

**Not done at the time (both resolved in section 18):** there were no report downloads, and the dashboard did not
match these rules (admin-only record fields on the edit page; buttons shown to roles the API refuses).

Test: `pnpm test` — **203 tests**. `Integration/PermissionTests.cs` checks every protected endpoint for all four
callers; `AuthTests` covers the new operator / viewer rules. The integration tests now act as an administrator by
default (`ApiFixture.DefaultToken`); pass `ApiFixture.Anonymous` to call signed out. Checked live against this PC's
database through the dashboard proxy with the three real accounts, without changing data: every cell of the table
above behaved as listed.
Manually: sign in (sidebar) as each account and try Record Movement, editing an asset, Approve on a gate pass, the
RFID simulator and Sync Now.

### 18. CSV report downloads, and the dashboard now matches the permissions
**API** (`Controllers/ReportsController.cs`, `Services/ReportService.cs`, `Infrastructure/CsvBuilder.cs`). Every
report needs a signed-in user (viewer or above; 401 when signed out). The JSON reads stay open.

| Endpoint | Filters (all optional) |
|---|---|
| `GET /reports/assets.csv` | `status`, `locationId`, `sbuId`, `category`, `search` |
| `GET /reports/movements.csv` | `from`, `to`, `assetId`, `sbuId` |
| `GET /reports/gate-passes.csv` | `status`, `sbuId`, `from`, `to` |
| `GET /reports/insights.csv` | `includeDismissed` (default true) |
| `GET /reports/lifecycle.csv` | `eventType`, `sbuId`, `from`, `to` |

- `from` / `to` are `YYYY-MM-DD` days in the organisation time zone, both inclusive; a bad date or `from` after `to`
  returns 400. No row limit.
- CSV format: RFC 4180, CRLF line endings, UTF-8 with a BOM (so Excel shows "–" and accents correctly), Yes/No for
  booleans, and times as `yyyy-MM-dd HH:mm` in the organisation zone (the header names the zone).
- Spreadsheet formula injection is blocked: a text cell starting with `= + - @`, tab or CR gets a leading `'`.
- The file name is `assetiq-{report}-{date}.csv`. CORS exposes `Content-Disposition`.
- The OpenAPI spec has a `reports` tag; `pnpm codegen` generated `downloadAssetsReport(...)` and the other four.

**Dashboard:**
- New **Reports** page (sidebar, `/reports`): five report cards with shared SBU and date filters plus per-report
  filters, each with *Download CSV*.
- Export CSV buttons on Assets (uses the current search, SBU and status), Movements, Gate Passes (the selected tab)
  and AI Insights.
- `lib/reports.ts` (`downloadReport`) calls the generated client, so the sign-in token is attached.
- `components/permission.tsx`:
  - `usePermission(role, action)`;
  - `<Permitted>`: shows the control, or a locked button whose tooltip says which role is needed;
  - `<PermissionNotice>`: amber banner on pages the user can't use.
- Aligned with section 17:
  - Operators can edit every asset field; the admin lock is gone.
  - Register, edit, assign, dispose, gate-pass request/approve/reject, locations and dismissing insights need operator.
  - Record Movement needs viewer.
  - The RFID simulator and Sync Now need administrator.
  - The sign-in role descriptions are updated.
- The sidebar's **"Acting as"** name and the *Your name* field in Settings are gone (`operator-badge.tsx` deleted).
  `useOperatorName()` now returns the signed-in user's name, which pre-fills "Moved by", "Requested by" and the
  approver field. Signed out it is empty. The fields stay editable, e.g. to record who physically moved an asset.

Test: `pnpm test` — **221 tests**. `Integration/ReportTests.cs` covers each report's columns, filters, time zone,
BOM, escaping and the formula guard; the permission matrix covers the report endpoints. `ApiFixture.CallAsync` no
longer tries to parse non-JSON bodies. Checked live through the dashboard proxy: signed out → 401; all three accounts
download all five reports (200, correct file name, BOM present); a reversed date range returns 400.
Manually: sign in as `viewer`, open Reports and download each one. Open the files in Excel. Check that the edit,
approve and sync buttons show as locked.

### 19. One Azure App Service serves the API and the dashboard
The dashboard calls `/api` on its own origin, so on Azure the API serves the built dashboard too (on Replit the
platform still serves them separately).
- `pnpm build:azure` builds the dashboard, then publishes the API; the `.csproj` copies
  `artifacts/asset-tracker/dist/public` into `dist/wwwroot` when it exists (publish only — `bin/` never gets it).
- `Program.cs` serves `wwwroot` (resolved next to the DLL) only when it holds `index.html`, and falls back to
  `index.html` for every non-`/api` path so client-side routes survive a reload. Unknown `/api` paths still 404.
  Development, tests and Replit have no `wwwroot`, so nothing changes there.
- **Test it**: `pnpm build:azure`, then from the repo root
  `$env:PORT="8099"; dotnet artifacts\api-server\dist\AssetIQ.Api.dll` → http://localhost:8099 shows the dashboard,
  `/assets/12` loads it too, `/api/healthz` answers `{"status":"ok"}`, `/api/does-not-exist` is a 404.
- **Azure**: Linux Web App, runtime .NET 8, Free F1 plan; app settings `DATABASE_URL` (Supabase session pooler
  URL with `?sslmode=require`; `sslrootcert` is not needed by the API) and a new `AUTH_SECRET`; startup command
  `dotnet AssetIQ.Api.dll`; deploy `artifacts/api-server/dist` (e.g. VS Code's Azure App Service extension →
  *Deploy to Web App*). F1 sleeps when idle, so the first request after a pause takes a while.

### Other fixes found along the way
- `POST /locations` now returns SBU name/code; `PATCH /locations/:id` sets `updatedAt` and returns 404 for unknown ids.
- `GET /gate-passes?sbuId=` and `GET /movements?sbuId=` now filter (the parameters were ignored).
- "Today's movements" uses the organisation's day, not the server's (UTC on Replit).
- Invalid ids return 400 instead of a database error; unknown assets/locations on create return 404 instead of 500.
- Gate activity shows entry/exit direction and includes gate-pass exits; the activity feed names auto-raised passes and approvers.

## Conventions for future changes
- **Contract first**: edit `lib/api-spec/openapi.yaml` → `pnpm codegen` → commit the generated files, then make the
  C# DTOs in `Contracts/Responses.cs` match. Never hand-edit `lib/*/src/generated`. (`lib/api-zod` is no longer used
  by the API; codegen still produces it.)
- **Controllers stay thin**; business logic goes in `artifacts/api-server/src/AssetIQ.Api/Services`, with rules as pure
  static methods so they can be unit tested. Throw `ApiException` for 4xx; give every action a `[FailureMessage]`.
- **Schema changes** happen in `lib/db/src/schema`, then `pnpm --filter @workspace/db exec drizzle-kit generate
  --config ./drizzle.config.ts --name <what>` writes the migration (`--custom` for hand-written SQL: triggers,
  backfills). Never edit an applied migration. Update the EF entity too; the tests build their database from the
  migrations (`Database/schema.sql` is gone);
  `SchemaConformanceTests` catches a mismatch.
- **Time**: columns are `timestamp without time zone` holding UTC, written from the API's clock (`AppClock`, never
  `DateTime.Now` or the database's `now()`). "Today", "this week" and working hours use `ZonedTime` with
  `ORG_TIMEZONE`, never the server's timezone.
- **New rule or workflow step?** Add a failing test first (unit test for the rule, integration test for the behaviour).
- Server code logs through `ILogger<T>`, never `Console.WriteLine` (scripts may print).

## Not verified
- The OpenAI call has not been tried against the live API (no key here). It is covered by tests with a stubbed
  response, and any failure falls back to the rules.
- Replit and Azure deployments were not re-run. On Replit, run `pnpm --filter db push` (post-merge does it) to add the
  new columns before using the new features. The C# API also needs the .NET 8 SDK on the Repl (`artifact.toml` runs
  `dotnet artifacts/api-server/dist/AssetIQ.Api.dll`); that has not been tried.
- The Supabase setup ("Using Supabase") has not been run end to end. The URL parsing it relies on is covered by
  `PlumbingTests`; the dashboard menu names may have moved.
- **.NET 8 support ends on 10 November 2026.** Moving to .NET 10 (LTS) is a version bump: `TargetFramework` in
  `artifacts/api-server/Directory.Build.props` plus the package versions in the two `.csproj` files.

## Phase 1 — audit and management essentials (AU-01 … AU-12), October 2026

The plan, story status, assumptions and open questions are in `docs/implementation-plan.md` (Phase 1 report).

- **Database**: migrations in `lib/db/migrations` (`0000_baseline` = the schema as pushed before, `0001` new
  tables/columns, `0002` reference data, backfill of existing assets and the triggers that make `audit_log` /
  `access_log` append-only and asset numbers permanent). Run `pnpm db:migrate` on this PC's cluster and on Supabase
  before starting the new API; existing assets are backfilled into template categories and queued for asset numbers.
- **API**: `Auth/AccessPolicy.cs` (who may see / change what), `Services/AssetRegistrationService.cs`,
  `Services/Templates/TemplateRules.cs`, `Services/Depreciation/*` (engine + ledger + run/lock), `ValuationService`
  (one calculation behind card, register, category report and dashboard), `UpgradeService`, `CompletenessService`,
  `BcNumbers.cs` (mock Business Central numbers, queue + retry worker), exports in `Infrastructure/TableExport.cs`.
- **Dashboard**: Asset Register (`/register`), asset card (`/assets/:id/card`), category report (`/reports/category`),
  Asset Position dashboard (home for finance roles; `/operations` keeps the previous dashboard), Approvals,
  Verification (AU-11), Administration. Dark theme is the default.
- **Sign-ins** (`pnpm seed --reset`): the three original ones plus scoped accounts, password = username + `-2026`
  (`group.finance`, `tsas.finance`, `tsas.it`, `bak.finance`, `ext.auditor`, … listed by the seed).
- **Tests**: `pnpm test` — 300 tests (78 new; names start with the story ID).

## Phase 2 — roles, scope and the permissions matrix, October 2026

The plan, story status, assumptions and open questions are in `docs/implementation-plan.md` (Phase 2 report).

- **Roles**: the 12 roles of `Domain/Values.cs` `Roles` (the matrix's 9, Auditor, Employee, System Administrator).
  viewer / operator / admin are retired: migration `0004` made `admin` accounts Group IT and disabled `operator` /
  `viewer` accounts (kept, not deleted); a retired role signs nobody in.
- **Every endpoint needs a sign-in** (fallback policy) except `/healthz` and `/auth/login`. Endpoints name a role group
  (`[RequireRole(Roles.GatePassRaisers)]`); `Auth/AccessPolicy.cs` then checks the record (own SBU, IT / non-IT,
  department). Auditors pass on GET only.
- **Scope is a global query filter**: `Auth/AccessScope.cs` (per request, narrowed by the token handler) filters
  `assets` and every table hanging off an asset in `AssetIqDbContext`, so lists, dashboards, CSV / Excel / PDF exports
  and AI answers hold only the caller's scope. Rules that must look across SBUs (Group-unique serials, RFID reads, the
  gate pass workflow, the AI rules, scans) say `IgnoreQueryFilters()`; by-id endpoints use `AssetAccess` so another
  SBU's record is a 403, not a 404. Contexts made outside a request (tests, the BC worker) are unrestricted.
- **New**: departments and employees (`lib/db/src/schema/people.ts`; users link to an employee, assets to a custodian
  employee and department), user management (`UsersController`, GI-08), attachments stored in PostgreSQL
  (`AttachmentsController`, append-only by trigger), `/scan` (the Gate Officer's only view of an asset), upgrade
  invoices in any currency, a default residual % per category (0).
- **Dashboard**: `lib/roles.ts` mirrors the API's role groups; the sidebar shows each entry only to the roles that may
  use it; signed out, only the sign-in screen shows. New screens: Scan, People & Toolboxes, Departments, Administration
  → Users; attachments on the asset card.
- **Tests**: `PermissionTests` (cross-SBU 403 per role, every "—" cell of the implemented matrix rows),
  `RolesAndPeopleTests`, `MigrationTests` (0004); `RoleWorld` seeds one account per role.

## Phase 3 — asset status state machine, October 2026

Report and the transition table: `docs/implementation-plan.md` §11.

- **Statuses**: `Domain/Values.cs` `AssetStatuses` (9); the table is `Domain/AssetStatusMachine.cs`. Change a status
  only through `Services/AssetStatusService.cs` (`Change` for a person: role + table + guards; `TrySystemChange` for
  the gate / Business Central). `POST /assets/{id}/status` and `GET /assets/{id}/transitions`; the card's
  *Change status* dialog uses them. Migration `0006` maps the old statuses (the seed runs the same file).
- **Follow-ups**: the System Administrator may do everything (`AccessPolicy.Sys`, every request in `access_log`);
  financial figures are removed for roles without `AccessPolicy.CanSeeFinancials` (`Infrastructure/FinancialRedaction.cs`,
  a global result filter); gates and readers are the System Administrator's (`/rfid/readers`).
