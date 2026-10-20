# AssetIQ — Intelligent Asset Management & Tracking System
## Degree Project Plan | Attachment Period: 2025

---

## 1. Executive Summary

AssetIQ is an intelligent, IoT-ready asset management and tracking system designed for enterprise environments (Tailored for TSL group specifically). Unlike traditional transactional systems that rely on manual data entry via web forms, AssetIQ is built around the concept of **proactive, sensor-driven asset awareness** — where the system automatically knows where assets are, flags anomalies, and triggers approval workflows without human initiation.

The system is architected for Microsoft Azure in production (C# / .NET backend, SQL Server) and integrates with Microsoft Business Central (BC) for enterprise asset accounting. The proof-of-concept MVP is delivered as a full-stack web application that simulates RFID behaviour, demonstrating the complete architecture end-to-end.

---

## 2. Problem Statement

Organizations managing large numbers of physical assets (laptops, projectors, equipment) face:

- **No real-time visibility**: Assets are tracked via spreadsheets or manual forms, leading to inaccurate records
- **Slow approval workflows**: Gate passes (authorizations to move assets off-premises) are paper-based and slow
- **No anomaly detection**: Nobody notices when an asset moves to an unexpected location or goes missing until it is too late
- **Disconnected systems**: Asset movement data is not automatically reflected in financial/ERP systems (e.g. Business Central)

The ICT executive's directive: *"We want an intelligent system that actively reads and proactively records — not a web form."*

---

## 3. Solution Overview

AssetIQ addresses this through four core capabilities:

| Capability | Description |
|---|---|
| **Auto-tracking via RFID** | RFID readers at gates, offices, and key locations automatically record asset movements |
| **Smart workflow engine** | Gate passes (leave-premises approvals) are triggered automatically and routed for approval |
| **AI anomaly detection** | The system detects unusual movement patterns, missing assets, and idle equipment |
| **BC Integration** | Asset movements and status changes are automatically reflected in Microsoft Business Central |

---

## 4. System Architecture

### 4.1 High-Level Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                        CLIENT LAYER                             │
│         React Web Dashboard (TypeScript, Tailwind CSS)          │
└────────────────────────────┬────────────────────────────────────┘
                             │ HTTPS / REST API
┌────────────────────────────▼────────────────────────────────────┐
│                        API LAYER (Azure)                        │
│              ASP.NET Core Web API  (C# / .NET 8)                │
│   ┌──────────┐ ┌──────────┐ ┌──────────┐ ┌───────────────────┐ │
│   │ Assets   │ │Movements │ │Gate Pass │ │ AI Insights       │ │
│   │ CRUD     │ │ Engine   │ │ Workflow │ │ (Anomaly Engine)  │ │
│   └──────────┘ └──────────┘ └──────────┘ └───────────────────┘ │
└──────┬──────────────────────────────────────┬───────────────────┘
       │                                      │
┌──────▼──────────┐                  ┌────────▼────────────────────┐
│  DATA LAYER     │                  │  INTEGRATIONS               │
│  Azure SQL DB   │                  │  ┌─────────────────────┐    │
│  (PostgreSQL    │                  │  │ Microsoft BC API    │    │
│   for MVP)      │                  │  └─────────────────────┘    │
└─────────────────┘                  │  ┌─────────────────────┐    │
                                     │  │ Azure IoT Hub       │    │
┌────────────────────────────────────│  │ (RFID reader feed)  │    │
│        IoT / SENSOR LAYER          │  └─────────────────────┘    │
│  RFID Readers → Azure IoT Hub      │  ┌─────────────────────┐    │
│  (Simulated in MVP via API)        │  │ Azure OpenAI        │    │
└────────────────────────────────────┘  │ (AI query engine)   │    │
                                        └─────────────────────┘    │
                                        └────────────────────────────┘
```

### 4.2 IoT Architecture (Production Target)

```
Physical World                         Cloud
─────────────                          ─────
RFID Tag (on asset)
    │ radio signal (passive)
    ▼
RFID Reader (at gate/office)  ──MQTT──► Azure IoT Hub
                                              │
                                              ▼
                                    Azure Stream Analytics
                                    (real-time event processing)
                                              │
                                              ▼
                                    AssetIQ API (movement recording,
                                    anomaly checks, BC sync trigger)
```

**In the MVP:** The RFID reader step is simulated via a "RFID Simulator" screen in the dashboard. A user selects a reader and an asset tag — the system processes it identically to a real hardware event.

### 4.3 Technology Stack

| Layer | MVP (This Project) | Production Target |
|---|---|---|
| **Frontend** | React + TypeScript + Vite | Same |
| **Backend** | Node.js + Express | C# ASP.NET Core on Azure App Service |
| **Database** | PostgreSQL | Azure SQL Database |
| **ORM** | Drizzle ORM | Entity Framework Core |
| **IoT** | Simulated via API | Azure IoT Hub + RFID readers |
| **AI** | Rule-based anomaly engine; NL queries parsed by OpenAI when configured, with the rule-based parser as fallback (see §13, D1) | Azure OpenAI Service (same OpenAI-compatible call) |
| **BC Integration** | Mocked connection, **manual sync only** (see §13, D2) | Microsoft BC API (OAuth2), automatic sync |
| **Hosting** | Replit / Render | Microsoft Azure |

---

## 5. Data Model

### Core Entities

**Assets**
- `id`, `name`, `category` (Laptop/Projector/Phone/Furniture)
- `serialNumber`, `rfidTag` (unique identifier for IoT tracking)
- `status` (active / maintenance / retired / missing)
- `currentLocationId`, `assignedTo`
- `purchaseDate`, `purchaseValue`

**Locations**
- `id`, `name`, `type` (office / gate / warehouse / meeting_room / lab)
- `building`, `floor`
- Each location may have one or more RFID readers

**MovementEvents**
- `id`, `assetId`, `fromLocationId`, `toLocationId` (null = off-premises)
- `triggeredBy` (rfid / manual / gate_pass)
- `rfidReaderId`, `gatePassId` (the pass that authorised — or was raised by — the movement), `movedBy`, `timestamp`

**GatePasses** (leave-premises authorization)
- `id`, `assetId`, `requestedBy`, `reason`
- `destination`, `returnDate` (an approved pass authorises exits until the end of this date; empty = open-ended)
- `status` (pending / approved / rejected)
- `source` (manual = requested in advance / rfid_gate = raised automatically by a gate reader), `gateLocationId`
- `approvedBy` (the named approver who decided — approve *or* reject), `decisionNote`, `decidedAt`

**RFIDReaders**
- `id`, `name`, `locationId`, `status`

**AIInsights**
- `id`, `type` (anomaly / idle_asset / unauthorized_movement / missing_asset / unusual_pattern)
- `assetId`, `gatePassId` (gate-pass related alerts), `message`, `severity` (info / warning / critical)
- `dismissed`

---

## 6. Key Features

### 6.1 Asset Registry
Register and manage all organizational assets with RFID tags. Assets carry full lifecycle metadata (purchase value, assigned staff, maintenance history). An asset's details are edited from its own page, behind sign-in and a role check (§6.7); the serial number and RFID tag are fixed once registered, so the record and the physical tag cannot drift apart.

### 6.2 Automatic Movement Tracking (RFID)
When an RFID reader at any location scans an asset tag, the system (one database transaction, `services/rfid-scan.ts`):
1. **Gate check** (gate readers only). Readers are not directional in the MVP, so direction is inferred: an asset with an on-site location is *leaving*; an off-premises asset is *returning* (entry — no pass needed). For an exit the system looks for a valid approved gate pass; if there is none, it reuses the asset's pending pass or **raises a new pending pass automatically**.
2. Records a `MovementEvent` — `gate_pass` for an authorised exit (the asset goes off-premises), otherwise `rfid` with the gate pass linked.
3. Updates the asset's `currentLocationId` (back-dated scans never overwrite a newer position).
4. Runs the anomaly rules (§6.4) and stores any insights, de-duplicated.
5. The movement counts as a pending change for the next Business Central sync (manual in the MVP — §6.6).

### 6.3 Gate Pass Workflow
Passes reach the approval queue in two ways: an employee **requests one in advance** ("Request gate pass" form), or a gate reader detects an asset leaving **without an approved pass** and the system **raises a pending pass automatically** (with an `unauthorized_movement` alert). Then:
1. Approvers receive an **in-app notification** — a toast with a *Review* action and a pending-count badge on *Gate Passes* (polled every 10 s). No email/SMS in the MVP (§13, D3).
2. They approve or reject from the dashboard. The approver's **name is required and recorded** (`approvedBy`, `decidedAt`) — there is no hard-coded approver.
3. **Approve → the movement is allowed.** If the asset is waiting at the gate (always the case for an auto-raised pass) the exit is recorded immediately as a `gate_pass` movement and the asset goes off-premises; a pass approved in advance records that movement when a gate reader sees the asset leave. The related alert is resolved.
4. **Reject → the asset is flagged:** a critical `unauthorized_movement` insight (warning if the request was made in advance) and an audit note in the asset history; the asset must not leave.

Rules: one pending pass per asset; return dates cannot be in the past; a pass can only be decided once.

The request form picks the asset by **typing part of its name, RFID tag or serial number** rather than scrolling a
list of every asset, and picks the requester (and, on the decision dialog, the approver) from the **people already on
record** — the same picker, filtered the same way — while still accepting a name the system has not seen before. The
return date is entered in the format chosen in Settings (§6.8).

### 6.4 AI Anomaly Detection
The anomaly engine (`services/anomaly-rules.ts`, pure and unit-tested) flags:

| Rule | Type / severity |
|---|---|
| Gate exit without an approved gate pass | `unauthorized_movement` — warning; **critical** outside working hours |
| Location mismatch: the asset's SBU differs from the SBU of the location (e.g. gate) that read it | `anomaly` — warning |
| Scan outside working hours, judged on the **scan timestamp** in Africa/Harare time (Mon–Fri 07:00–18:00, configurable) | `unusual_pattern` — warning at gates, info elsewhere |
| Active asset idle for more than 7 days (configurable) | `idle_asset` — info |
| Asset marked missing | `missing_asset` — critical |

Authorised gate exits and returning assets raise **nothing** (the earlier "every gate scan is a warning" behaviour was removed). Identical open alerts are not duplicated. *Movement patterns that deviate from a learned baseline* need historical data and are Phase 2.

### 6.5 Natural Language Query
Users can ask questions like (all three are covered by automated tests):
- *"Where is the Dell laptop assigned to John Doe?"* — matches the assignee and brand/category words, returns the location and last movement
- *"Which assets have not moved in 30 days?"* — any number of days / weeks / months
- *"Show me all gate passes approved this week"* — status (pending / approved / rejected) and period (today, this week, last week, this month, last N days)

plus missing / in-maintenance assets, category counts, unassigned assets and assets held by a person. A question is mapped to a structured **intent** — by OpenAI (JSON-schema structured output) when `OPENAI_API_KEY` is set, otherwise by the rule-based parser, which is also the automatic fallback. The intent is executed against the database, so **answers are always computed from live data and only the question text is sent to OpenAI** (§13, D1).

### 6.6 Business Central Integration
MVP: the BC connection is **mocked and syncs are manual** (*Sync Now* on the BC Sync page, §13, D2). Movements and gate-pass decisions recorded since the last sync are shown as **pending changes** — exactly what an automatic sync would push. Phase 2 (real BC API with OAuth2) syncs automatically:
- Asset status updates
- Location changes reflected in BC fixed asset module
- Gate pass completions trigger BC asset transfer entries

### 6.7 Authentication and roles
Changing what the registry says about an asset is the one action that needed protecting first: a wrong serial or a
silently reassigned asset undermines every other number in the system. The API therefore issues a **bearer token**
(`POST /auth/login`, scrypt-hashed passwords, HS256 token signed with `AUTH_SECRET`) and checks a **role** on the
asset write endpoints:

| Role | May do |
|---|---|
| Viewer | Read everything |
| Operator | + an asset's operational fields: status, holder, location, notes |
| Administrator | + the record itself (name, category, SBU), and registering or deleting assets |

The operator/administrator split means the day-to-day workflows (deploy, repair, dispose — all of which update the
asset) stay open to operators, while the asset's identity stays with administrators. The check is **server-side**;
the dashboard only decides which buttons to offer, and the signed-in user's name is what gets recorded on approvals
and movements. Reading remains open without signing in, and the other mutating endpoints are not yet role-checked —
the middleware is in place to extend (§13, D8).

### 6.8 Operations Dashboard
The dashboard is the entry screen: summary counters (total / missing assets, pending gate passes, today's movements),
a per-SBU asset breakdown, the movements-per-day and asset-status-by-SBU charts (§12 colour-blind-safe palette, each
with a Chart/Table toggle), a recent-activity feed, gate activity with entry/exit direction, and asset distribution by
location. Critical insights surface as a banner linking to AI Insights.

**Every card is a drill-down link** to the screen behind its number — a counter is never a dead end. Total Assets →
the registry; Missing Assets → the registry filtered to `status=missing`; Pending Gate Passes → the approval queue
(which opens on the *pending* tab); Today's Movements and Recent Activity → the movement log; an SBU tile → the
registry filtered to that SBU; Gate Activity → gate passes; Asset Distribution → locations. The stat and list cards
are clickable in full; the chart cards use a header link so their Chart/Table toggle keeps working.

### 6.9 Personalisation (appearance and date format)
A **Settings** panel in the sidebar footer holds what each user can personalise. Appearance offers **Light**, **Dark**
and **System** (follow the operating system, and keep following it while the app is open); dark mode exists for eye
comfort, which matters for the after-hours gate approvals the workflow in §6.3 creates. The same panel holds the
operator name recorded on decisions (§6.3), so the two per-user settings live in one place, and the sidebar also
carries a one-click light/dark toggle for quick switching. Choices are stored in the browser — the MVP has no user
accounts (§7, Out of Scope) — and are applied before first paint so a reload in dark mode does not flash white.

**Date format** is the second setting: **dd/mm/yyyy** (the default, as dates are written in Zimbabwe), mm/dd/yyyy or
ISO yyyy-mm-dd. One formatting module serves every screen — movement logs, asset history, gate passes, BC sync and the
chart axes — so the chosen format is used consistently system-wide and changing it updates all of them at once. Native
date pickers are the one exception: the browser renders those in the operating system's locale.

The sidebar is **pinned to the viewport** on desktop: only the content column scrolls, so navigation and the pending
gate-pass badge stay reachable from anywhere in a long list.

---

## 7. MVP Scope (by 30 November 2025)

### In Scope

| Feature | Status |
|---|---|
| Asset registry (CRUD, editing behind sign-in) | ✅ Included |
| Authentication: sign-in, roles (viewer / operator / admin) enforced on asset records | ✅ Included |
| Location management (list, create, edit) | ✅ Included |
| Movement event log + manual movement form | ✅ Included |
| Gate pass workflow (request / auto-raise at gate / approve / reject, in-app notifications) | ✅ Included |
| RFID simulation screen (incl. replaying a scan at a chosen time) | ✅ Included |
| Dashboard (summary stats, activity feed, movement-trend and SBU-status charts, every card a drill-down link) | ✅ Included |
| AI anomaly detection (rules) + natural-language query (rules, optional OpenAI) | ✅ Included |
| BC sync status page (mock connection, manual sync) | ✅ Included |
| Personalisation: light / dark / system theme, system-wide date format, operator name (Settings panel) | ✅ Included |
| Demo seed data (`pnpm seed`) | ✅ Included |
| Automated tests (`pnpm test`) | ✅ Included |

### Out of Scope for MVP (Phase 2)

- Real RFID hardware integration
- Live Azure IoT Hub connection
- Real Microsoft BC API connection and automatic BC sync
- Mobile app for field staff
- Email/SMS notifications (the MVP notifies approvers in-app)
- Baseline / ML movement-pattern anomaly detection
- Extending the role checks to the remaining mutating endpoints (gate-pass decisions, movements, RFID simulation,
  BC sync); the MVP enforces them on asset records only
- User administration in the UI (accounts are created by the seed script), password reset, and single sign-on
- Multi-tenant support

---

## 8. Project Timeline

| Phase | Dates | Deliverables |
|---|---|---|
| **Phase 1: Foundation** | July 2025 | Architecture design, data model, OpenAPI spec, database setup |
| **Phase 2: Core Features** | August 2025 | Asset CRUD, locations, movement tracking, basic dashboard |
| **Phase 3: Workflows & AI** | September 2025 | Gate pass workflow, RFID simulator, AI insights engine |
| **Phase 4: Integrations** | October 2025 | BC sync stub, AI natural language query, activity feed |
| **Phase 5: Polish & Demo** | November 2025 | UI polish, demo data, stakeholder presentation prep |

---

## 9. IoT Concept Architecture (for future implementation)

The system is designed so that when real RFID hardware is available, the only change needed is replacing the `POST /api/rfid/simulate` call with a real Azure IoT Hub event feed. The processing pipeline is identical.

**Recommended hardware stack for production:**
- **RFID tags**: UHF RAIN RFID passive tags (ISO 18000-6C) — cheap, no battery
- **RFID readers**: Zebra FX9600 fixed readers at gates and key locations
- **Protocol**: MQTT via Azure IoT Hub
- **Edge**: Azure IoT Edge for on-premises processing where connectivity is limited

---

## 10. Business Value

| Metric | Before | After (Target) |
|---|---|---|
| Time to locate an asset | 30–60 minutes (manual search) | Instant (dashboard lookup) |
| Gate pass processing time | 1–2 days (paper) | < 1 hour (digital workflow) |
| Asset audit accuracy | ~70% (manual) | >98% (RFID-tracked) |
| Time to detect missing asset | Days to weeks | Minutes (anomaly alert) |
| BC asset record accuracy | Manual, often outdated | Automatic, real-time |

---

## 11. Degree Report Alignment

This project addresses the following academic areas:

- **Systems Analysis & Design**: Requirements elicitation from stakeholder (ICT executive), use case modelling, system architecture design
- **Database Management**: Relational schema design, query optimization, data integrity
- **Software Engineering**: Agile development, OpenAPI-first contract design, test-driven development of the business rules — the RFID pipeline, gate-pass workflow, anomaly rules and NL parser are specified by an automated Vitest suite (§12)
- **Emerging Technologies**: IoT architecture, AI/ML integration, cloud computing (Azure)
- **Business Process Management**: Workflow automation, ERP integration (Business Central)

---

## 12. Testing & Quality Assurance

| Level | What it covers | Where |
|---|---|---|
| Unit | Pure rules: gate-pass validity and direction, every anomaly rule (incl. severity escalation), timezone/working-hours maths, NL question parsing, OpenAI response handling | `artifacts/api-server/src/**/*.test.ts` |
| API integration | The real Express app against an in-memory PostgreSQL (PGlite) built from the Drizzle schema: RFID scans, auto-raised passes, approve → `gate_pass` movement, reject → flag, validation (400/404/409), NL answers, BC sync, location and movement endpoints | `artifacts/api-server/src/routes/*.test.ts` |
| Security | Sign-in (wrong password is indistinguishable from an unknown user, disabled accounts, forged and expired tokens) and the role matrix on asset records, including a demotion taking effect on the next request | `src/routes/auth.test.ts` |
| Manual / demo | Seeded demo data and a scripted walkthrough in the dashboard | `pnpm seed`, `CLAUDE.md` |

Run everything with `pnpm test` (10 files, 128 tests). Tests pin the clock (Wednesday 10:00 Harare) so time-based rules are deterministic, and the RFID / gate-pass tests were checked by mutation: reintroducing the old "every gate scan is a warning" behaviour or the hard-coded approver makes them fail.

## 13. Decision Log (October 2026)

**D1 — OpenAI: implemented, with rules as the fallback.**
*Context:* the plan promised "Rule-based + OpenAI"; a rules-only parser is brittle with free-form questions, while an LLM alone is non-deterministic, costs money and could leak asset data.
*Decision:* OpenAI is used only to translate the question into a structured intent (JSON-schema structured output). The intent is executed by the same deterministic code as the rule-based parser. If no key is configured, or the call fails or times out, the rule-based parser answers.
*Consequences:* answers always come from live data; only the question text leaves the system; the demo works offline; production switches to Azure OpenAI by configuration (`OPENAI_BASE_URL`, `OPENAI_AUTH_HEADER=api-key`).

**D2 — Business Central: manual sync only in the MVP.**
*Context:* there is no BC tenant or OAuth2 app registration for the MVP, so any "automatic" sync would only drive a mock.
*Decision:* keep the mock connection and a manual *Sync Now* action; report movements and gate-pass decisions since the last sync as *pending changes*. Automatic sync on movements moves to Phase 2 with the real BC API.
*Consequences:* the demo stays honest; the integration point and the change queue are visible; no scheduled jobs to operate.

**D3 — Gate-pass notifications: in-app.**
*Context:* the workflow's value is fast approval (target < 1 hour); without a notification, auto-raised passes would wait unnoticed. Email/SMS needs mail infrastructure and is out of MVP scope.
*Decision:* in-app notifications — a pending-count badge on *Gate Passes* and a toast with a *Review* action when a new pass appears (polling every 10 s).
*Consequences:* approvers must have the dashboard open; email/Teams notifications are Phase 2.

**D4 — Dashboard cards drill down into the filtered screen.**
*Context:* the dashboard counters were static text. A user who saw "3 pending gate passes" or "2 missing assets" had to
work out which sidebar page carried that list and re-apply the filter by hand, which is the slowest step in the
approval flow the MVP is meant to speed up.
*Decision:* make every card a link to the screen behind its number, filtered where the listing supports it
(`/assets?status=missing`, `/assets?sbuId=<id>`). Stat and list cards are clickable in full; the chart cards use a
header link so their Chart/Table toggle still works. The asset registry gained `?status=` URL filtering (with a
visible chip to clear it) so the Missing Assets card lands on the right rows.
*Consequences:* one click from a number to the records behind it, and the filter state lives in the URL, so a filtered
view can be bookmarked or pasted to a colleague. Filtered deep links are limited to what the list endpoints already
accept — no new query parameters were added to the contract.

**D5 — Theme preference: per-browser, with "system" as the default.**
*Context:* dark mode was asked for as an eye-comfort feature. There is no authentication in the MVP (§7), so there is
nowhere on the server to keep a per-user preference, and the dashboard is used both in bright offices and after hours.
*Decision:* store the choice in the browser's localStorage next to the operator name, defaulting to **system** so a
first-time user gets whatever their OS already prefers. The theme is a token swap on a `dark` class on `<html>`, so
components need no theme-specific code, and an inline script applies the stored value before first paint.
*Consequences:* no contract, database or API change, and no flash of the wrong theme on reload; the preference does not
follow a user between machines or browsers — it would move into a user profile when authentication arrives in Phase 2.

**D6 — Dates: dd/mm/yyyy by default, formatted in one module.**
*Context:* dates were rendered by a hard-coded `en-US` formatter in one place and by ad-hoc `toLocaleDateString` /
`toLocaleTimeString` calls in several others, so the same system showed "Oct 20, 2026, 2:05 PM" next to a raw
`2026-12-31` return date. TSL writes dates day-first.
*Decision:* all date text comes from a single module with an explicit day/month/year order — defaulting to
**dd/mm/yyyy** rather than the browser's locale, so two users never read the same report differently — and the order is
user-selectable in Settings. Calendar-day values (return dates, purchase dates) are formatted from the string itself
rather than through `Date`, which would shift the day in a negative-offset timezone.
*Consequences:* one place to change date presentation, and the setting is consistent across every screen and chart;
new code must use that module, since a stray `toLocaleDateString` would ignore the preference. Native date-picker
inputs still follow the operating system's locale — unavoidable in a browser.

**D7 — People come from the records, not a user table.**
*Context:* requesters, approvers and the person who moved an asset were typed by hand every time, so the same person
could be entered three ways and no screen could offer a list. The MVP has no authentication and no staff directory
(§7), and adding one would duplicate the HR data that Business Central already owns.
*Decision:* derive the list of people from the names already stored — `assets.assignedTo`, `gate_passes.requestedBy`
and `approvedBy`, `movements.movedBy` — de-duplicated case-insensitively, and offer it as a type-to-filter dropdown
that still accepts a new name. No schema or contract change.
*Consequences:* names converge on one spelling and are picked rather than retyped, and the list grows as the system is
used; it is only as good as the data entered, and someone who has never appeared must be typed once. A real directory
(Business Central staff, or Azure AD once authentication exists) replaces this source in Phase 2 without changing the
pickers.

**D8 — Authentication: bearer tokens, enforced on asset records first.**
*Context:* editing an asset's details had to be restricted to the right people. The MVP had no authentication at all,
so there was no server-side identity to check; a dashboard-only guard (hiding the button) would have been security
theatre, since the API would still accept the request from anyone who could reach it.
*Decision:* add real authentication — a `users` table with scrypt-hashed passwords, a signed bearer token from
`POST /auth/login`, and `requireRole` on the server. Roles are viewer / operator / administrator, and the
administrator boundary is drawn around the asset *record* (name, category, SBU) rather than its operational fields,
so existing operator workflows keep working. The role is re-read from the database on each request, so a demotion
takes effect immediately rather than when the token expires. Tokens are signed with `AUTH_SECRET`, with a random
per-process key as the fallback — never a hard-coded one.
*Consequences:* the asset-editing function is genuinely protected rather than merely hidden, and the signed-in user's
name is what gets recorded on decisions. Reading stays open, and the other mutating endpoints are not yet
role-checked — the middleware exists, so extending them is a routine change. Accounts are created by the seed script;
user administration, password reset and SSO remain Phase 2.
The `users` table holds *sign-ins*, not the staff directory: the people pickers (D7) still read the names recorded on
assets, gate passes and movements, because most people who hold an asset never sign in to the dashboard.
