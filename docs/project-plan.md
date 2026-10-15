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
| **AI** | Rule-based + OpenAI API | Azure OpenAI Service |
| **BC Integration** | Mocked / REST stub | Microsoft BC API (OAuth2) |
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
- `id`, `assetId`, `fromLocationId`, `toLocationId`
- `triggeredBy` (rfid / manual / gate_pass)
- `rfidReaderId`, `timestamp`

**GatePasses** (leave-premises authorization)
- `id`, `assetId`, `requestedBy`, `reason`
- `destination`, `returnDate`
- `status` (pending / approved / rejected)
- `approvedBy`, `decisionNote`

**RFIDReaders**
- `id`, `name`, `locationId`, `status`

**AIInsights**
- `id`, `type` (anomaly / idle_asset / unauthorized_movement / missing_asset)
- `assetId`, `message`, `severity` (info / warning / critical)
- `dismissed`

---

## 6. Key Features

### 6.1 Asset Registry
Register and manage all organizational assets with RFID tags. Assets carry full lifecycle metadata (purchase value, assigned staff, maintenance history).

### 6.2 Automatic Movement Tracking (RFID)
When an RFID reader at any location scans an asset tag, the system:
1. Records a `MovementEvent` with the new location
2. Updates the asset's `currentLocationId`
3. Checks if the movement triggers an anomaly
4. Checks if a gate pass is required (for gate readers)
5. Queues a Business Central sync

### 6.3 Gate Pass Workflow
When an asset is detected at a gate reader without an approved gate pass:
1. The system automatically creates a pending gate pass request
2. Managers receive a notification (email / in-app)
3. They approve or reject from the dashboard
4. The movement is allowed or flagged accordingly

### 6.4 AI Anomaly Detection
The AI engine flags:
- Assets in unexpected locations (e.g. a server moved to a public area)
- Assets idle for unusually long periods
- Assets detected outside working hours
- Movement patterns that deviate from baseline

### 6.5 Natural Language Query
Users can ask questions like:
- *"Where is the Dell laptop assigned to John Doe?"*
- *"Which assets have not moved in 30 days?"*
- *"Show me all gate passes approved this week"*

### 6.6 Business Central Integration
Changes sync to BC automatically:
- Asset status updates
- Location changes reflected in BC fixed asset module
- Gate pass completions trigger BC asset transfer entries

---

## 7. MVP Scope (by 30 November 2025)

### In Scope

| Feature | Status |
|---|---|
| Asset registry (CRUD) | ✅ Included |
| Location management | ✅ Included |
| Movement event log | ✅ Included |
| Gate pass workflow (request / approve / reject) | ✅ Included |
| RFID simulation screen | ✅ Included |
| Dashboard (summary stats, activity feed, charts) | ✅ Included |
| AI anomaly detection (rule-based + AI query) | ✅ Included |
| BC sync status page (mock connection) | ✅ Included |

### Out of Scope for MVP (Phase 2)

- Real RFID hardware integration
- Live Azure IoT Hub connection
- Real Microsoft BC API connection
- Mobile app for field staff
- Email/SMS notifications
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
- **Software Engineering**: Agile development, OpenAPI-first contract design, test-driven development
- **Emerging Technologies**: IoT architecture, AI/ML integration, cloud computing (Azure)
- **Business Process Management**: Workflow automation, ERP integration (Business Central)

---


