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
Register and manage all organizational assets with RFID tags. Assets carry full lifecycle metadata (purchase value, assigned staff, maintenance history).

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

---

## 7. MVP Scope (by 30 November 2025)

### In Scope

| Feature | Status |
|---|---|
| Asset registry (CRUD) | ✅ Included |
| Location management (list, create, edit) | ✅ Included |
| Movement event log + manual movement form | ✅ Included |
| Gate pass workflow (request / auto-raise at gate / approve / reject, in-app notifications) | ✅ Included |
| RFID simulation screen (incl. replaying a scan at a chosen time) | ✅ Included |
| Dashboard (summary stats, activity feed, movement-trend and SBU-status charts) | ✅ Included |
| AI anomaly detection (rules) + natural-language query (rules, optional OpenAI) | ✅ Included |
| BC sync status page (mock connection, manual sync) | ✅ Included |
| Demo seed data (`pnpm seed`) | ✅ Included |
| Automated tests (`pnpm test`) | ✅ Included |

### Out of Scope for MVP (Phase 2)

- Real RFID hardware integration
- Live Azure IoT Hub connection
- Real Microsoft BC API connection and automatic BC sync
- Mobile app for field staff
- Email/SMS notifications (the MVP notifies approvers in-app)
- Baseline / ML movement-pattern anomaly detection
- Authentication and roles (the MVP records the operator's name on decisions)
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
| Manual / demo | Seeded demo data and a scripted walkthrough in the dashboard | `pnpm seed`, `CLAUDE.md` |

Run everything with `pnpm test`. Tests pin the clock (Wednesday 10:00 Harare) so time-based rules are deterministic, and the RFID / gate-pass tests were checked by mutation: reintroducing the old "every gate scan is a warning" behaviour or the hard-coded approver makes them fail.

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

