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
