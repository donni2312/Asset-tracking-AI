# AssetIQ — implementation plan for the agreed user stories

Phase 0 output: gap analysis only. No code, schema or data was changed. Written 28 Oct 2026.

> **Blocker: the specification is not in the repo.** `docs/user-stories.md` and `docs/design-mockups/` do not
> exist. The story IDs below come from the developer brief, and the mockup contents from the files pasted into
> chat. So the "what's left" column is based on the brief's rules, not on each story's acceptance criteria, and the
> Permissions matrix is unknown. See §6, Q1–Q2. This table has to be re-checked against the real stories before
> Phase 1 tests can be named after their acceptance criteria.

---

## 1. Current architecture

| Layer | What it is | Where |
|---|---|---|
| API | ASP.NET Core 8 Web API, EF Core 8 + Npgsql; thin controllers, rules as pure static classes in `Services/` | `artifacts/api-server/src/AssetIQ.Api` |
| Database | PostgreSQL 16 (local `embedded-postgres`, or Supabase). **Schema is owned by Drizzle** (`lib/db/src/schema`), applied with `drizzle-kit push`. There are no migration files, and EF never migrates | `lib/db` |
| Contract | OpenAPI-first: `lib/api-spec/openapi.yaml` → `pnpm codegen` → React Query hooks (`lib/api-client-react`) + Zod (`lib/api-zod`, unused by the API) | `lib/api-spec` |
| Web | React + Vite + wouter + TanStack Query + Tailwind + shadcn/ui; light and dark theme via CSS variables in `index.css` | `artifacts/asset-tracker` |
| Auth | Own scrypt hashes + HS256 bearer tokens; role re-read from DB on every request; `[RequireRole]` with a 3-level hierarchy `viewer < operator < admin` | `Auth/` |
| Tests | xUnit + WebApplicationFactory against a throwaway PostgreSQL; `SchemaConformanceTests` compares the EF model with `tests/.../Database/schema.sql` | `artifacts/api-server/tests/AssetIQ.Api.Tests` |
| BC sync | **Mock**: in-memory state, manual trigger, counts pending movements/gate-pass decisions. No BC tenant, no fixed-asset numbers | `Services/BcSyncService.cs` |
| Seed | `pnpm seed [--reset]` → 5 SBUs (TSAS, TSF, BAK, AGRI, PROP), 17 locations, ~32 assets, 3 users | `scripts/src/seed*.ts` |
| Hosting | One Azure App Service serves API + built dashboard; Supabase DB | CLAUDE.md §19 |

Existing features that must keep working: RFID pipeline + simulator (`RfidScanService`, one transaction with
`FOR UPDATE`), gate-pass workflow (auto-raised `rfid_gate` passes, approve/reject), anomaly rules + AI insights +
NL query (OpenAI with rule-parser fallback), BC sync page, SBU colour badges, dashboard drill-downs, 5 CSV reports.

**Tooling gaps:** there is no linter in any package (no ESLint, no `dotnet format` step). "Lint" in the phase
checklist will mean `pnpm run typecheck` + C# build with warnings unless you want one added (Q14).

### Current data model (10 tables)

| Table | Key columns | Notes |
|---|---|---|
| `sbus` | code (unique), name, color | 5 rows; no Propak |
| `locations` | name, type (office/gate/warehouse/…), building, floor, sbu_id | |
| `rfid_readers` | name, location_id, status, last_read_at | |
| `assets` | name, **category (free text)**, serial_number (**unique, not null**), rfid_tag (**unique, not null**), status (`active/maintenance/retired/missing`), assigned_to (**free text**), current_location_id, sbu_id (nullable), purchase_date (text), purchase_value (numeric), notes | No asset number, cost breakdown, currency, depreciation, custodian FK |
| `movements` | asset, from/to location, triggered_by, reader, gate_pass_id, moved_by | |
| `gate_passes` | asset (single), requested_by/approved_by (**free text**), status pending/approved/rejected, return_date, source manual/rfid_gate, gate_location_id | One approver, no value threshold, no partial return |
| `ai_insights` | type, severity, asset_id, gate_pass_id, dismissed | |
| `asset_lifecycle_events` | event_type (deployment/return_to_it/repair_sent/repair_returned/sbu_transfer/note), performed_by, from/to SBU, condition before/after | Closest thing to history; not an audit log (no old/new values, editable via EF) |
| `users` | username, name, role (viewer/operator/admin), password_hash, active | No SBU, department, scope or expiry |

---

## 2. Mockups received (file names do not match contents)

The pasted `.dc.html` files are shuffled: each file's `<title>`, active nav item and internal links show what it
really is. The file sent as `user-stories.md` is the Fleet mockup, **not** the stories.

| File as received | Actually contains | Target screen | Phase |
|---|---|---|---|
| `Toolbox.dc.html` | Asset Position dashboard (Group Finance) | Main / Dashboard (AU-10, GF-01) | 1 |
| `Verification.dc.html` | Asset card, BAK-IT-2024-00031 | AssetCard (AU-08) | 1 |
| `AssetCard.dc.html` | Approvals inbox (upgrade capitalise/expense) | Approvals (AU-07, Phase 4 engine) | 1/4 |
| `CategoryReport.dc.html` | Verification — TSF, Oct 2026 + AU-11 record gaps | Verification (SI-07, SF-06, AU-11) | 1/4 |
| `Fleet.dc.html` | Receive Goods (PO match, capture, tag) | Register / Stores (ST-01..05) | 4 |
| `Register.dc.html` | Mobile gate check (390 px) | GateOfficer (SG-01..03) | 4 |
| `GateOfficer.dc.html` | Employee toolbox + exit clearance (T. Moyo) | Toolbox (HR-01, HR-03, HR-04) | 4 |
| `Approvals.dc.html` | Department equipment, needed vs held | Departments (LM-01/02, HR-07) | 4/5 |
| `user-stories.md` | Fleet & Non-IT — Agricura | Fleet (AM-01..04) | 4 |
| — | **missing** | CategoryReport (AU-09) | 1 |

I will build AU-09 from the brief's column list and the dashboard table's style unless you send the mockup (Q2).

---

## 3. Story status

Status: **Exists** = meets the brief's rules today; **Partial** = something usable exists; **Missing** = nothing.
Paths are relative to `artifacts/api-server/src/AssetIQ.Api/` (API) and `artifacts/asset-tracker/src/` (web).

### Priority 1 — AU (Phase 1)

| ID | Status | Current files | What's left |
|---|---|---|---|
| AU-01 Category-driven form + generated description | Partial | `Controllers/AssetsController.cs`, `pages/assets/new.tsx`, `edit.tsx` | Category is free text and `name` is typed by hand. Need `asset_categories` + `category_template_fields` tables, typed attribute values, server validation (missing required / foreign field → 400), description pattern per template, rebuild + audit on change, dynamic form, category change only by SBU Finance |
| AU-02 Group-unique serial | Partial | `assets.serial_number` unique constraint | Already unique Group-wide in DB. Left: friendly 409 naming the existing asset, log the attempt, identifier field per template (VIN, IMEI…), "No serial" + reason + SBU Finance approval, counts incomplete in AU-11. Unique constraint must allow many "no serial" rows (partial unique index) |
| AU-03 BC asset number | Missing | `Services/BcSyncService.cs` (mock) | `asset_number` (unique, immutable), `previous_asset_number`, status *Pending asset number*, outbox/queue table + retry worker, mock BC endpoint that issues numbers, DB trigger blocking update once set, numbers never reused |
| AU-04 Cost | Partial | `assets.purchase_value` (numeric, no currency) | original_cost, currency, exchange_rate, cost_usd (set at capitalisation), invoice/PO refs + attachment, total = original + capitalised upgrades, shown separately everywhere |
| AU-05 Depreciation | Missing | — | Category-level method/life/rate (Group), monthly run table, straight-line + reducing-balance, residual floor, month lock + next-month adjustment, stop on disposal, worked-example test |
| AU-06 History + audit trail | Partial | `asset_lifecycle_events`, `GET assets/{id}/history` | Append-only `audit_log` (old/new/user/time/reason) for financial fields, DB trigger rejecting UPDATE/DELETE, unified dated timeline |
| AU-07 Upgrades | Missing | — | `asset_upgrades` table, log (SBU IT / Admin Mgr / Stores), decide (SBU Finance of the asset's SBU only), capitalise from approval month over remaining (or revised) life, expense path, spec fields + description update, Approvals inbox entry |
| AU-08 Asset card | Partial | `pages/assets/detail.tsx` | Rebuild to mockup: identity tiles, cost & value, purchase details, upgrades, depreciation schedule, history; open by asset-number search or tag scan; PDF export (needs a PDF library — Q13) |
| AU-09 Category report | Missing | `Services/ReportService.cs` (CSV only) | Grouped report with subtotals, filters (SBU, period, location, category), drill-down, Excel + PDF export, invariant test vs register |
| AU-10 Management dashboard | Partial | `pages/dashboard.tsx`, `Services/DashboardService.cs` (counts/movements) | Finance table per category from the **same** calculation service as AU-09; SBU filter; click-through. Existing operational dashboard kept (Q10) |
| AU-11 Data completeness | Missing | — | Completeness rules service, list by SBU/category, % per SBU |
| AU-12 Auditor | Missing | `users` | Auditor role, all-SBU read-only, `access_expires_at` set by Group Finance, login + export logging, refuse after expiry |

### Other stories named in the brief (Phases 2–5)

Criteria for these come only from the brief; status is against that.

| ID(s) | Status | Current files | What's left |
|---|---|---|---|
| Roles & scope (Phase 2), permissions matrix | Partial | `Auth/*`, `Domain/Values.cs` `Roles`, `components/permission.tsx`, `Integration/PermissionTests.cs` | 11 roles replace viewer/operator/admin; user ↔ SBU / department / employee; scope filter applied in every query and export; **reads are currently open to signed-out users** (must close); user management (Group IT, SBU IT own SBU) |
| GF-01 | Partial | dashboard | = AU-10 |
| GF-02, GF-06 | Missing | — | Unknown criteria (Q1) |
| GF-03 | Missing | — | Group depreciation settings per category (with AU-05) |
| GF-04 | Missing | — | Disposal above threshold → Group Finance approval |
| GF-07 | Partial | lifecycle events | = AU-06 |
| GI-01..GI-04, GI-08 | Missing / unknown | — | Unknown criteria (Q1) |
| GI-02 | Partial | assets | IT category fields + MAC (optional, unique when present) — with AU-01 |
| GI-05, SI-10 Inter-SBU transfer | Partial | `lifecycle` `sbu_transfer` event (moves SBU immediately, no approval) | Approval workflow, *Transfer pending* status, move to receiving register, notify both SBU Finance |
| GI-06, SI-04, SG-01..03 Gate | Partial | `GatePassService`, `RfidScanService`, `pages/gate-passes`, `rfid-simulator` | Mobile gate-officer screen, green/red result, officer action record, approver = line manager / Admin Mgr fallback, SF-05 value threshold → SBU Finance, partial returns, multi-asset pass |
| GI-07 Lost/Stolen | Partial | status `missing` | Date, reporter, notes; excluded from active counts |
| GI-10, SI-08, SF-04 Disposal | Partial | status `retired` | Disposal request, gain/loss = proceeds − NBV, thresholds, data-wipe note + Group IT sign-off for IT, final status |
| SI-01..SI-03, SI-05, SI-06 | Missing / unknown | — | SI-02 rule known: unassigned IT assets sit with SBU IT. Rest unknown (Q1) |
| SI-07, SF-06 Verification | Missing | anomaly "missing" rule only | Scan sessions per location, found/missing/unrecorded, resolution with comment, sign-off |
| SF-01..SF-03 | Missing / unknown | — | SF-03 = depreciation. Rest unknown (Q1) |
| SF-05 | Missing | — | High-value gate pass needs SBU Finance |
| SF-10 Bulk import | Missing | — | Upload → validation report → confirm import; keeps `previous_asset_number` |
| ST-01..ST-05 Stores | Missing | — | POs, receipts (short/damaged), tagging, stock view, issue against approved request with signature, returns (ST-05 IT rule) |
| LM-01, LM-02 Departments | Missing | — | Departments, HOD scope |
| LM-03 Delegation | Missing | — | Delegate approvals while on leave |
| HR-01, HR-03, HR-04 (+HR-02, HR-05 referenced) | Missing | `assigned_to` free text, `hooks/use-people.ts` | Employees table, toolbox, acknowledgement (app/email), role templates (HR-02), exit clearance, recovery notice to SBU Finance (HR-05) |
| HR-07 Department gap (Should) | Missing | — | Phase 5 |
| AM-01..AM-04 Non-IT / fleet | Missing | — | Non-IT templates, fleet register, document expiry 30/7 days, service schedule |
| AM-07 Fuel (Could) | Missing | — | Phase 5, pool vehicles only |

---

## 4. Proposed data model changes

All changes are added in Drizzle (`lib/db/src/schema`), mirrored in EF entities and `schema.sql`. Money is
`numeric(18,2)` (`decimal` in C#); rates `numeric(18,6)`. Enums stay `text` + `CHECK` constraints, matching the
current text-valued columns, so an unknown value never breaks a read.

**Migrations (conflict C1):** the project has no migrations, only `drizzle-kit push`. I propose staying on the same
tool: `drizzle-kit generate` + `drizzle-kit migrate`, with a baseline migration of today's schema first. Custom SQL
migrations (`drizzle-kit generate --custom`) carry the triggers, partial indexes and data backfill. `db:push` stays
for throwaway local DBs only.

### New reference / configuration tables

| Table | Columns (main) | Phase |
|---|---|---|
| `group_settings` | key (PK), value (jsonb), updated_by, updated_at | 1 (seeded with the placeholder values in the brief) |
| `asset_categories` | id, code, name, is_it (bool), parent_id (sub-category), depreciation_method (`straight_line`/`reducing_balance`), useful_life_months, annual_rate, residual_pct, description_pattern, identifier_field_key, active | 1 |
| `category_template_fields` | id, category_id, key, label, type (`text/number/integer/date/list/boolean`), unit, required, allowed_values (jsonb), sort_order, is_spec (description + upgrade target), unique (category_id, key) | 1 |
| `sbu_depreciation_overrides` | category_id, sbu_id, method/life/rate, approved_by (Group Finance), approved_at | 1 |
| `departments` | id, sbu_id, name, hod_employee_id | 2 |
| `employees` | id, sbu_id, department_id, employee_no, name, job_role_id, line_manager_id, email, status | 2 |
| `job_roles` + `role_template_items` | role → category + quantity (HR-02) | 4 |

### Changes to `assets`

| Column | Change |
|---|---|
| `category_id` | new FK, **not null** after backfill from the text `category` (text column kept read-only until the dashboard stops using it) |
| `description` | generated, replaces typed `name` (name kept as an alias for existing screens/NL query) |
| `asset_number` | new, unique, nullable until BC returns it; trigger blocks change once set |
| `previous_asset_number` | new |
| `serial_number` | becomes nullable; partial unique index `WHERE serial_number IS NOT NULL`; `no_serial_reason`, `no_serial_approved_by`, `no_serial_approved_at` |
| `mac_address` | new, partial unique index |
| `rfid_tag` | becomes **nullable** (assets exist untagged before ST tagging) — unique stays. *Conflict C3* |
| `status` | new value set (§5), backfilled from the old 4 values |
| `custodian_employee_id`, `department_id`, `condition` | new; `assigned_to` text kept for the RFID/NL code paths until moved |
| `original_cost`, `currency`, `exchange_rate`, `cost_usd`, `invoice_no`, `po_no`, `supplier`, `invoice_attachment_id` | new; `purchase_value` backfilled into original_cost (USD, rate 1) |
| `in_use_date`, `residual_value`, `useful_life_months_override` | new |
| `disposed_at`, `lost_reported_at`, `lost_reported_by`, `lost_notes` | new |

`asset_attribute_values` (asset_id, field_id, value_text, value_number, value_date, PK (asset_id, field_id)): typed
so per-category filters and reports work in SQL.

### Finance and audit

| Table | Purpose |
|---|---|
| `asset_upgrades` | date, description, before/after (jsonb per spec field), cost, supplier, invoice, logged_by, status (`pending/capitalised/expensed/rejected`), decided_by, decided_at, revised_life_months, comment |
| `depreciation_entries` | asset_id, period (yyyy-mm), opening_cost, opening_accumulated, charge, adjustment, closing_accumulated, closing_nbv, unique (asset_id, period) |
| `depreciation_periods` | sbu_id, period, locked_by, locked_at |
| `audit_log` | id, entity, entity_id, field, old_value, new_value, user_id, at, reason, action. **Append-only:** trigger raises on UPDATE/DELETE; no API endpoint writes or deletes outside the service |
| `access_log` | login / export events (auditor and everyone), user, at, what, filters |
| `bc_number_requests` | asset_id, status (`queued/sent/done/failed`), attempts, next_attempt_at, last_error, bc_number |
| `attachments` | id, file name, content type, storage key, uploaded_by |

### Workflows (Phases 3–4)

`approval_requests` (type, subject, sbu, amount, step, assignee role/user, due_at, escalated_at, status) +
`approval_actions` (append-only decisions/comments), `delegations`, `disposals`, `sbu_transfers`, `purchase_orders`
+ `po_lines` + `goods_receipts`, `stock_issues` (request, signature), `verification_sessions` +
`verification_results`, `toolbox_acknowledgements`, `exit_clearances` + lines, `vehicle_documents`,
`service_schedules`. Gate passes gain `value`, `line_manager_approved_by`, `finance_approved_by`, gate-pass lines for
multiple assets and partial return, and `gate_checks` (officer action). Users gain `role` (11 values), `sbu_id`,
`employee_id`, `access_expires_at`.

---

## 5. Asset status machine (Phase 3 — draft for review)

| From \ To | Pending no. | In store | In use | Gate pass | In repair | Transfer pending | Pending disposal | Lost/Stolen | Disposed |
|---|---|---|---|---|---|---|---|---|---|
| **Pending asset number** | — | ✓ BC no. issued | | | | | | | |
| **In store** | | — | ✓ issue (tagged, has no.) | | ✓ | ✓ | ✓ | ✓ | |
| **In use** | | ✓ return | ✓ reassign | ✓ exit | ✓ | ✓ | ✓ | ✓ | |
| **Out on gate pass** | | | ✓ return | — | ✓ | | | ✓ | |
| **In repair** | | ✓ | ✓ | | — | | ✓ | ✓ | |
| **Transfer pending** | | ✓ approved/rejected | ✓ rejected | | | — | | | |
| **Pending disposal** | | ✓ rejected | ✓ rejected | | | | — | | ✓ approved |
| **Lost/Stolen** | | ✓ recovered | | | | | ✓ write-off | — | |
| **Disposed** | | | | | | | | | — (final) |

Guards: issue needs tag + asset number (ST-02); no reassignment while pending disposal (SI-08); unassigned IT goes
to SBU IT custody, not Stores (SI-02) — modelled as *In store* with `custodian = SBU IT store` (Q8); Stores accepts IT
returns only with an SBU IT disposal recommendation (ST-05). "In use → In use" is a reassignment, not a status change.

Existing values map as: `active` → In use (or In store when unassigned), `maintenance` → In repair, `retired` →
Disposed, `missing` → Lost/Stolen. Assets with no BC number yet → Pending asset number.

---

## 6. Conflicts between the brief/stories and the current code

| # | Conflict | Proposal |
|---|---|---|
| C1 | Schema via `drizzle-kit push`, no migrations; brief requires migrations | Same tool, switch to `generate`/`migrate` with a baseline (§4) |
| C2 | All GET endpoints and the dashboard work **signed out**; brief requires scope on every read | Require sign-in for all reads in Phase 2 (dashboard shows the sign-in screen) |
| C3 | `rfid_tag` and `serial_number` are `NOT NULL`; brief needs untagged and "No serial" assets | Make nullable with partial unique indexes |
| C4 | Role model is a 3-level hierarchy; brief needs 11 scoped roles | Replace `[RequireRole]` with permission checks (`[RequirePermission("asset.edit")]`) + a scope service applied to every query; existing 3 demo logins mapped to new roles (Q9) |
| C5 | Status values `active/maintenance/retired/missing` are used by the RFID pipeline, anomaly rules, dashboard counts, NL query and CSV | Backfill + update all of them in Phase 3; Phase 1 adds only *Pending asset number* handling |
| C6 | BC is an in-memory mock with no fixed-asset numbers; the brief says BC issues them | Add a BC client interface with a mock implementation that issues numbers asynchronously (and can be told to fail) behind a queue; real BC later. Number format is unknown (Q4) |
| C7 | Existing `sbu_transfer` lifecycle event moves an asset between SBUs instantly | Becomes an approval request in Phase 4; until then left as is |
| C8 | `assigned_to`, `requested_by`, `approved_by` are free text | Employees/users FKs added beside them; text kept for history |
| C9 | Brief names 6 SBUs incl. Propak; seed and mockups have 5 | Add Propak to seed (Q3) |
| C10 | Brief's UI palette is a single dark theme; the app has light + dark themes | Apply the palette as the dark theme and make dark the default; keep light (Q10) |
| C11 | HR-07 brief: Gap = Needed − Held, red if Gap > 0. Departments mockup shows Held − Needed (Finance needs 8, holds 6, shown "−2" in red) | Follow the brief's formula and show the label as "short 2" (Q11) |
| C12 | Toolbox mockup says returned items (incl. the laptop) go to BAK Stores; brief's ST-05 sends non-disposal IT returns to SBU IT | Follow ST-05 (Q12) |
| C13 | Depreciation brief says "(depreciable cost − residual) ÷ remaining months"; the worked example only works if "depreciable cost" means cost **not yet depreciated** (NBV) | Implement as (NBV − residual) ÷ remaining months, which reproduces the example exactly; flagged `// ASSUMPTION:` |
| C14 | Mockup figures don't tie out (e.g. asset card $1,420 / 48 months ≠ $29.17; Hilux is `AGRI-MV-2019-00004` on the dashboard but `AGRI-MV-2021-00003` on Fleet) | Seed data computed by the real engine, not copied from mockups |
| C15 | "Lint" required at each phase; no linter exists | Typecheck + C# warnings only, unless Q14 says otherwise |

---

## 7. Questions before Phase 1

1. **Please add `docs/user-stories.md`** (the real one, with acceptance criteria and the Permissions matrix). The file
   pasted as `user-stories.md` is the Fleet mockup. Without it I can't name tests after acceptance criteria, can't
   build Phase 2, and stories like GF-02, GF-06, GI-01, GI-03, GI-04, GI-08, SI-01, SI-03, SI-05, SI-06, SF-01, SF-02
   are only IDs to me.
2. Please add `docs/design-mockups/` with correct file names, including the missing **CategoryReport** mockup.
3. Propak: SBU code and badge colour?
4. BC asset number format: is it really `{SBU}-{CLASS}-{YYYY}-{NNNNN}` as the mockups show, and is there a BC sandbox,
   or should the mock issue numbers for now?
5. Phase 1 needs some roles (SBU Finance decides upgrades, Group Finance edits categories, Auditor). Do you want a
   minimal role + SBU scope model in Phase 1 (recommended), or keep the 3 old roles until Phase 2?
6. Currency: is USD the reporting currency, and where do exchange rates come from (entered per invoice, or a rate table)?
7. Depreciation rounding: round each monthly charge to cents and let the final month absorb the remainder?
8. SI-02: is "with SBU IT" a location/custodian (an SBU IT store) or a separate status?
9. The 3 existing logins (`administrator`, `operator`, `viewer`): map to Group IT Admin, SBU IT, Auditor? Or remove?
10. Dashboard: replace the current operational dashboard with the finance one, or add the finance view as the Group
    Finance landing page and keep the operational one for other roles?
11. HR-07 sign convention (C11).
12. ST-05 vs Toolbox mockup (C12).
13. PDF and Excel export: OK to add QuestPDF (PDF, MIT licence for small companies — check TSL's revenue) and
    ClosedXML (Excel, MIT) to the API? Alternative for PDF: browser print stylesheet.
14. Do you want ESLint (web) and `dotnet format --verify-no-changes` (API) added as the lint step?
15. Invoice attachments: where should files live (Azure Blob, Supabase storage, or DB for the MVP)?
16. Confirmed values for the Group settings (thresholds, refresh ages, alert days, reminder/escalation hours).

---

## 8. Phase 1 order of work (after answers)

1. Migration baseline, then categories + template fields + attribute values + settings (AU-01, AU-02, GI-02).
2. Audit log with DB trigger (AU-06) — everything after it writes audit entries.
3. Cost fields + backfill (AU-04); BC number queue + mock (AU-03).
4. Depreciation engine as a pure static class with the worked-example test first (AU-05), then the monthly run,
   locks and adjustments.
5. Upgrades + decision endpoint (AU-07).
6. One `AssetValuationService` feeding the category report (AU-09), dashboard (AU-10) and asset card (AU-08), with
   invariant tests.
7. Completeness service (AU-11); Auditor role + expiry + access log (AU-12).
8. Seed: templates, ~10 assets per SBU across categories, upgrades pending/decided, 22 months of depreciation.

## Baseline

`pnpm test` on 28 Oct 2026, before any change: **221 passed, 0 failed** (xUnit, 2 min 3 s). Every phase is
compared against this.

---

## 9. Phase 1 report: audit and management essentials (AU-01 … AU-12)

Done 29 Oct 2026. Answers to §7 applied: mockups and stories received in chat (Q1, Q2), Propak (Q3, see A1 below),
mock BC numbers (Q4), a minimal role and SBU-scope model in Phase 1 (Q5), QuestPDF + ClosedXML (Q13).

Checks at the end of the phase:

- `pnpm test`: **300 passed, 0 failed** (221 before; 78 new; a few existing tests updated for the new rules).
- `pnpm run typecheck`: clean (TypeScript, and C# with warnings as errors).
- `pnpm --filter @workspace/asset-tracker run build`: succeeds.
- `pnpm db:migrate`, then `pnpm seed` and `seed --reset`, against a throwaway database, both an empty one and one
  created with `db:push`.
- The API was run against the seeded data and smoke-tested as Group Finance, SBU Finance, SBU IT and the auditor.

**Not done:** the screens were type-checked and built but not looked at in a browser, so layout at phone width is
unverified. The migrations have not been applied to this PC's cluster or to Supabase.

### 9.1 Story status

| ID | Status | Tests (story-ID prefix) | Main files |
|---|---|---|---|
| AU-01 | Done (photo attachment open, Q15) | 13 `AU_01_*` | `Services/Templates/TemplateRules.cs`, `Services/AssetRegistrationService.cs`, `migrations/0002`, `pages/assets/new.tsx`, `edit.tsx`, `components/template-fields.tsx` |
| AU-02 | Done (barcode scan = scanner typing into the field) | 5 `AU_02_*` | `AssetRegistrationService.cs`, `TemplateRules.cs` |
| AU-03 | Done with the mock BC | 4 `AU_03_*` | `Services/BcNumbers.cs`, `migrations/0002` (trigger) |
| AU-04 | Done (invoice referenced by number; file attachment open, Q15) | 2 `AU_04_*` | `AssetRegistrationService.cs` (financial, capitalise) |
| AU-05 / SF-03 / GF-03 | Done (run, lock, adjustment, Excel schedule); SBU rate overrides not yet | 10 `AU_05_*` | `Services/Depreciation/*`, `FinanceController.cs` |
| AU-06 / GF-07 | Done | 2 `AU_06_*` | `Services/AuditTrail.cs`, `migrations/0002`, `AssetCardService.cs` (timeline) |
| AU-07 | Done | 4 `AU_07_*` + the worked example | `Services/UpgradeService.cs`, `pages/finance/approvals.tsx`, `pages/assets/card.tsx` |
| AU-08 | Done | `AU_08_*` | `Services/AssetCardService.cs`, `pages/assets/card.tsx` |
| AU-09 | Done | 3 `AU_09_*` (invariant, figures, exports) | `ValuationService.cs`, `FinanceReportService.cs`, `pages/finance/category-report.tsx` |
| AU-10 / GF-01 | Done | `AU_10_*` (dashboard = report) | `FinanceReportService.cs`, `pages/finance/asset-position.tsx` |
| AU-11 | Done | 5 `AU_11_*` | `CompletenessService.cs`, `pages/finance/verification.tsx` |
| AU-12 | Done | 2 `AU_12_*` | `AdministrationController.cs`, `Auth/Authentication.cs`, `AuthController.cs`, `Services/AccessLog.cs` |
| GI-02 | Done (MAC optional, unique when entered) | 2 `GI_02_*` | `TemplateRules.cs`, `AssetRegistrationService.cs` |
| AM-01 | Partly: create/edit rule and non-IT templates; fleet screens are Phase 4 | 2 `AM_01_*` | `Auth/AccessPolicy.cs` |
| Scope (Phase 2 preview) | Partly: enforced on every new endpoint and export | 6 `Scope_*` | `Auth/AccessPolicy.cs` |

Required invariant tests:

| Invariant | Test |
|---|---|
| Report totals equal register totals | `AU_09_category_subtotals…` |
| Dashboard equals report | `AU_10_…` |
| Depreciation worked example | `AU_05_worked_example…` (unit and API) |
| No cross-SBU leaks | `Scope_*` |
| The audit log can't be changed | `AU_06_the_audit_log_cannot_be_changed_or_deleted` |
| Asset numbers can't be edited | `AU_03_asset_numbers_cannot_be…` |
| No save without a category or with a required field missing | `AU_01_*` |
| Forbidden status transitions are rejected | Phase 3 |

### 9.2 Assumptions (marked `// ASSUMPTION:` in code)

- **A1 Propak** is seeded as code **PPK**, colour **#65A30D**: "Prop" clashes with TSL Properties' PROP (codes
  prefix asset numbers) and green is Agricura's colour.
- **A2 Depreciation formula** = (cost − accumulated − residual) ÷ remaining months: the only reading that reproduces
  the worked example. Charges are rounded to cents each month, so the total adds up exactly.
- **A3 Months**: the disposal month carries no charge. Locking a month locks every earlier month of that SBU. A
  locked month that was never run posts nothing; the next open month catches up.
- **A4 Upgrades**: cost is in USD. Capitalised from the approval month unless Finance gives an earlier effective
  month. On capitalise **or** expense, the "after" values update the specification.
- **A5 Furniture** captures make and model (AU-01 makes them mandatory everywhere): "Generic" when unbranded.
- **A6 Serials** are stored trimmed and upper-cased, so a lower-case duplicate is caught.
- **A7 Non-IT registration**: only the SBU Admin Manager (AM-01), not Stores, despite the matrix row "Register / tag".
- **A8 Stores scope**: until the Phase 3 "In store" status, Stores sees its SBU's assets that have no custodian or
  are still pending an asset number.
- **A9 Original roles** (viewer / operator / admin) keep their rights on the pre-Phase-1 endpoints and see every SBU
  until Phase 2. On those endpoints the scoped roles map onto them: Group IT = admin; SBU IT, SBU Admin Manager and
  Stores = operator; the finance roles = viewer.
- **A10 Existing assets** keep their status, are backfilled into a template category from their old text category,
  and are queued for BC numbers. They show as incomplete in AU-11 until their template fields are filled in.
- **A11 Uncapitalised assets** are on the register with zero cost.
- **A12 Report period** is one month. "Upgrades in the period" means capitalised from January to that month.
- **A13 QuestPDF**: the Community licence is valid only if TSL's revenue is under USD 1M; otherwise a commercial
  licence is needed before production.
- **A14 Dashboard**: finance roles and auditors land on Asset Position; everyone else on the previous dashboard (now
  also at `/operations`). Dark theme is the default.
- **A15 Sign-in**: the new screens need sign-in. The older reads stay open until Phase 2 (C2).

### 9.3 Questions

1. **Propak** code and colour (A1).
2. **Depreciation policy** per category: the seeded values are placeholders (laptops 48 months straight-line,
   vehicles 20% reducing balance, …). Group Finance can change them in Administration.
3. **AM-01 vs the matrix**: may Stores register non-IT items at receipt (A7)?
4. **Attachments** (invoice PDF, photo; still Q15): where should files be stored?
5. **Default residual value**: 0, or a % per category?
6. **Upgrade currency**: for upgrade invoices in another currency, capture the currency and rate as for assets?

### 9.4 How to demo (after `pnpm db:migrate` and `pnpm seed --reset`)

Passwords are the username + `-2026`.

1. **group.finance**: the home page is Asset Position (AU-10). Change the SBU filter, then click a category to open
   the category report (AU-09). Click a category name to see its assets in the register, then an asset to open its
   card (AU-08). Try the Excel and PDF exports.
2. **tsas.it**: Asset Register → "+ Register asset" → *Computers & Laptops*. Only that template's fields appear, and
   the description preview builds as you type. Save: BC issues the asset number. Register a second laptop with the
   same serial: it is refused, naming the first asset (AU-02). On a card, try "+ Log upgrade".
3. **tsas.finance**: Approvals shows the battery replacement on the Dell Latitude 5440. Capitalise or expense it, and
   the card's cost and schedule change (AU-07). On a new asset: Edit cost → Capitalise. Then Administration →
   Depreciation run → Lock a month.
4. **bak.finance**: sees BAK only; opening a TSAS card URL gives 403.
5. **Verification** (any finance role): record gaps and % complete per SBU (AU-11). The Propak desk's "No serial"
   reason waits for **ppk.finance** to approve it.
6. **ext.auditor**: reads and exports everything, and every edit is refused. As **group.finance**, Administration →
   Access log shows the auditor's sign-ins and exports (AU-12).

---

## 10. Phase 1 follow-ups and Phase 2 report: roles, scope and the permissions matrix

Done 29 Oct 2026. Answers to §9.3 (product owner, 29 Oct 2026):

| Q | Answer | What changed |
|---|---|---|
| 1 Propak | Keep lime green, code **PROP-H** | Seed: `PROP-H`, `#65A30D`; asset numbers `PROP-H-IT-2026-…`; sign-in `propak.finance` |
| 2 Rates | Keep the placeholders; the administrator sets the final rates | New **System Administrator** role (asked: "add a System Admin role"): categories, final rates, residual %, Group settings only |
| 3 Stores, non-IT | Yes, at receipt | `AccessPolicy.CanRegister`; editing non-IT stays the SBU Admin Manager's (AM-01) |
| 4 Attachments | Stored as attachments, shown and downloaded on the asset card | `asset_attachments` in PostgreSQL (asked: "in the database"); PDF/JPEG/PNG ≤ 10 MB, type checked from content, append-only trigger, downloads in the access log |
| 5 Residual | Default 0, editable over time | `asset_categories.default_residual_percent` (0); applied at capitalisation when Finance has not set a residual; per-asset residual stays editable by SBU Finance |
| 6 Currency | Multi-currency, invoices as invoiced | `asset_upgrades.currency / invoice_amount / exchange_rate`; USD cost = amount × rate; Finance may confirm a different rate when deciding |

Also decided before Phase 2: `admin` accounts → Group IT, `operator` / `viewer` disabled (migration 0004); minimal
departments and employees added now.

### 10.1 Story status

| ID / area | Status | Tests | Main files |
|---|---|---|---|
| Phase 2 roles (12) | Done | `PermissionTests.*`, `AuthTests.*` | `Domain/Values.cs` (`Roles` + role groups), `Auth/Authentication.cs` |
| Scope rules | Done: global query filter on assets and every table hanging off them | `Phase2_lists_reports_and_exports_hold_only_the_callers_scope`, `Phase2_group_it_sees_…` | `Auth/AccessScope.cs`, `Data/AssetIqDbContext.cs`, `Services/AssetAccess.cs` |
| Cross-SBU 403 per role | Done (read, edit, export) | `Phase2_an_sbu_a_user_reading_editing_or_exporting_an_sbu_b_record_gets_403` (8 roles) | |
| Matrix "—" cells | Done for every implemented row | `Phase2_permissions_matrix` (26 rows, every role) | `Auth/AccessPolicy.cs` |
| Gate pass approvals (HOD, Admin Mgr when no HOD, SBU Finance high-value) | Done (single approval; two-step routing is Phase 4) | `Phase2_gate_passes_go_to_the_hod_…` | `GatePassService.AuthorizeDecisionAsync` |
| GI-08 user management | Done | `GI_08_…` | `Controllers/UsersController.cs`, Administration → Users |
| SG-01 / SI-03 scan | Done (basic info outside scope) | `Phase2_a_scan_shows_the_gate_officer_…` | `Controllers/ScanController.cs`, `pages/scan.tsx` |
| Departments / employees | Minimal (HR maintains; toolbox workflows Phase 4) | `Phase2_sbu_hr_keeps_…`, `SI_02_issuing_to_an_employee_record_…` | `lib/db/src/schema/people.ts`, `Controllers/PeopleController.cs`, `pages/people` |
| AU-01 / AU-04 attachments | Done | `AU_04_the_invoice_and_photos_are_attached_…` | `Controllers/AttachmentsController.cs`, `components/attachments.tsx` |
| AU-07 currency | Done | `AU_07_an_upgrade_invoice_is_captured_in_its_own_currency_…` | `Services/UpgradeService.cs`, card upgrade dialog |
| AU-05 residual / final rates | Done | `AU_05_the_default_residual_is_0_…` | `AdministrationController.cs`, `AssetRegistrationService.CapitaliseAsync` |
| AM-01 Stores at receipt | Done | `AM_01_stores_registers_non_it_items_at_receipt_…` (API + unit) | `AccessPolicy.CanRegister` |
| Migration 0004 | Done | `Phase2_admin_accounts_become_group_it_…` | `lib/db/migrations/0003`, `0004` |

Matrix rows not testable yet because their endpoints arrive later: receive against PO, request equipment, exit
clearance, recommend / approve disposal, run verification, fleet compliance (Phases 3–5).

### 10.2 Assumptions (marked `// ASSUMPTION:` in code)

- **B1 Stores scope** (replaces A8): until the Phase 3 "In store" status, items not issued to an employee or a
  department.
- **B2 "HOD unavailable"**: the asset's department has no active HOD account; leave / delegation is Phase 4. A gate
  pass is high-value when the asset's USD cost is above `gate_pass_finance_threshold_usd` (500).
- **B3 Approver**: the signed-in user is recorded on gate pass decisions; a typed `decidedBy` is accepted but ignored.
- **B4 Gate Officer**: has an SBU; sees assets only via `/scan` and gate passes / movements of its SBU; full scan
  detail at its own SBU, basic identification otherwise.
- **B5 Employee**: sees the assets they hold (list and detail), not the asset card (cost).
- **B6 Finance views** (register, category report, Asset Position, completeness, approvals): Group Finance, SBU
  Finance, auditors only. Group IT's inventory is the asset list and CSV (GI-01).
- **B7 Gate log** (movements list / CSV): Group IT, SBU IT, Gate Officer, auditors (matrix row "Check exits").
- **B8 Locations** are kept by Group IT and the SBU Admin Manager (own SBU); SBUs by Group IT and the System
  Administrator; the BC sync by Group Finance and the System Administrator; the RFID simulator by Group IT.
- **B9 Non-IT inter-SBU transfers** are not recorded directly by anyone until the two-Admin-Manager workflow (AM-08).
- **B10 Users**: Group IT can't create auditors (Group Finance, AU-12); nobody changes their own role or access.
- **B11 Employees** holding items can't be marked as left until exit clearance (HR-04).
- **B12 Attachments** are never deleted (audit evidence); an asset with attachments can't be deleted.

### 10.3 Questions

1. Should the System Administrator also see assets (read-only), or only categories, rates and settings (as built)?
2. Should Group IT see the finance register (cost / NBV), or only the inventory (as built)?
3. Should the Employee see the cost of the items they hold?
4. Gate Officer: one per SBU (as built), or per site serving several SBUs?
5. Confirm "HOD unavailable" = no active HOD (B2) until leave / delegation exists.

### 10.4 How to demo (after `pnpm db:migrate` and `pnpm seed --reset`; password = username + `-2026`)

1. Signed out: only the sign-in screen. `operator` / `operator123` is refused (disabled, retired role).
2. **tsas.gate**: lands on Scan. Scan `RFID-TSAS-0001` → red "Not approved to leave", holder John Doe. Scan a BAK tag
   → basic identification only. The sidebar has no asset lists.
3. **tsas.it**: sees 9 TSAS IT assets only. Administration → Users: create a TSAS Stores clerk; a BAK user is refused.
4. **tsas.hod** (Rudo Chikwanha): sees Operations' items; approves a pass for John Doe's laptop.
5. **john.doe**: "My items" shows the one laptop he holds.
6. **tsas.finance**: open John Doe's laptop card → Attachments shows the seeded invoice; attach a PDF.
7. **sys.admin**: Administration → Categories: set the final life / rate and residual %; Assets are refused.
8. **group.finance**: the Hilux card shows the engine overhaul invoiced in ZAR (51 546.00 × 0.054321 = USD 2 800.03).

---

## 11. Phase 2 follow-ups and Phase 3 report: the asset status state machine

Done 29 Oct 2026. Answers to §10.3 (product owner):

| Q | Answer | Built |
|---|---|---|
| 1 System Administrator | God-mode, but logged | Passes every role gate and record rule (`AccessPolicy.Sys`), sees every SBU; every request is written to `access_log` (`action = request`, "GET /api/assets → 200"); changes are in the audit trail as for anyone |
| 2 Group IT | Asset inventory only, no financial data | Cost, value, depreciation and invoice amounts are removed from every response (`FinancialRedactionFilter`), the card PDF and the asset CSV for roles without financial access; the depreciation export is refused |
| 3 Employee | Must not see the cost of their items | Same redaction |
| 4 Gates | Many gates per SBU; the System Administrator configures them | Gate locations and RFID readers are created / changed by the System Administrator only (`POST /locations` type gate, `POST/PATCH /rfid/readers`), Administration → Gates & readers |
| 5 HOD unavailable | As recommended | Kept: the asset's department has no active HOD account (B2) |

Financial data is visible to Group Finance, SBU Finance, auditors and the System Administrator (**C1**, assumption:
SBU IT, SBU Admin Manager, Stores, HOD, SBU HR and the Gate Officer do not see it either; HR-05 will revisit SBU HR).

### 11.1 Allowed status transitions

One state machine (`Domain/AssetStatusMachine.cs`); every status change (edits, lifecycle events, gate exits and
returns, Business Central numbers, `POST /assets/{id}/status`) goes through it. A change not in this table is refused
with 409; a person also needs the role in §11.2 (403). ✓ = allowed; every empty cell is forbidden and tested.

| From \ To | Pending no. | In store | In use | Out on gate pass | In repair | Transfer pending | Pending disposal | Lost / stolen | Disposed |
|---|---|---|---|---|---|---|---|---|---|
| **Pending asset number** | — | ✓ number issued (system) | | | | | | | |
| **In store** | | — | ✓ issue | ✓ leave on pass | ✓ | ✓ request | ✓ recommend | ✓ | |
| **In use** | | ✓ return | — | ✓ leave on pass | ✓ | ✓ request | ✓ recommend | ✓ | |
| **Out on gate pass** | | ✓ back | ✓ back | — | ✓ | | | ✓ | |
| **In repair** | | ✓ | ✓ | ✓ leave on pass | — | | ✓ recommend | ✓ | |
| **Transfer pending** | | ✓ decided | ✓ decided | | | — | | | |
| **Pending disposal** | | ✓ rejected | ✓ rejected | | | | — | | ✓ approved |
| **Lost / stolen** | | ✓ recovered | | | | | ✓ write-off | — | |
| **Disposed** | | | | | | | | | — (final) |

Changes from the §5 draft: *In store → Out on gate pass* and *In repair → Out on gate pass* (spares lent or taken to a
vendor, SI-04), and *Out on gate pass → In store* (an unassigned item coming back). "In use → In use" is a
reassignment, not a status change.

Guards: issue needs a tag and an asset number (ST-02); no reassignment while pending disposal (SI-08, 409); lost /
stolen needs date, reporter and notes and leaves the active counts (GI-07); disposed is final; nobody sets
"pending asset number" (only Business Central moves an asset out of it).

### 11.2 Who may make each change (`AccessPolicy.CanChangeStatus`)

| Change | Roles |
|---|---|
| Issue (in store → in use) | Group IT, SBU IT (IT), SBU Admin Manager (non-IT), Stores |
| Return (in use → in store) | Group IT / SBU IT (IT; the holder becomes "{SBU} IT store", SI-02), SBU Admin Manager or Stores (non-IT). Stores never takes back IT items (ST-05) |
| Repair, transfer request, lost / stolen, recovery | Whoever edits the technical record |
| Decide a transfer | Group IT (IT), SBU Admin Manager (non-IT) |
| Recommend disposal | Technical owner, Stores ("return as scrap") |
| Decide a disposal, write off a lost item | SBU Finance of the SBU, Group Finance |
| Gate exit / return | The gate (system), the SBU's Gate Officer, technical owner |
| Anything | System Administrator (still only along the table) |

The role is checked before the table, so someone outside the asset's scope learns nothing about its status.

Stores' scope is now status-based: items pending their number, non-IT items in store, IT items in store until tagged
(ST-02), and IT items recommended for disposal (ST-05). Existing data is mapped by migration `0006` (active → in use /
in store / out on gate pass by holder and location; maintenance → in repair; retired → disposed; missing → lost /
stolen with placeholder incident details).

### 11.3 Story status

| Item | Status | Tests | Main files |
|---|---|---|---|
| State machine (9 statuses, every pair) | Done | `AssetStatusMachineTests.Phase3_each_listed_transition_…` (72 pairs), `…_disposed_is_final`, `…_covers_all_nine…` | `Domain/AssetStatusMachine.cs` |
| ST-02 issue untagged / unnumbered | Done | `ST_02_…` (unit, API) | machine guard, `AssetStatusService.Options` |
| SI-08 no reassignment pending disposal | Done | `SI_08_and_SF_04_…` | `AssetRegistrationService.EnsureReassignable` |
| SI-02 unassigned IT stays with SBU IT | Done | `SI_02_and_ST_05_…` | `LifecycleController`, `AssetStatusService.StoreHolder` |
| ST-05 Stores and IT returns | Done | `SI_02_and_ST_05_…` | `CanChangeStatus`, Stores scope |
| GI-07 lost / stolen | Done | `GI_07_…` (unit, API) | `assets.lost_*`, `/assets/{id}/status` |
| Disposed final | Done | `Phase3_disposed_is_final`, `SI_08_and_SF_04_…` | |
| Gate exit / return | Done | `Phase3_the_gate_moves_an_asset_out_…` | `MovementService`, `RfidScanService` |
| Migration of existing statuses | Done | `MigrationTests.Phase3_the_original_statuses_are_mapped_…` | `lib/db/migrations/0005`, `0006` |
| Follow-ups (god-mode, redaction, gates) | Done | `Phase2_group_it_and_employees_see_the_inventory_without_cost_or_value`, `Phase2_the_system_administrator_configures_gates_…`, `AU_05_…` (request log) | `Infrastructure/FinancialRedaction.cs`, `Services/AccessLog.cs`, `RfidController.cs` |

### 11.4 Assumptions

- **C1** Financial data for finance roles, auditors and the System Administrator only (see above).
- **C2** Until Phase 4's approvals (GF-04 above-threshold sign-off, GI-10 data wipe), SBU Finance or Group Finance
  decides every disposal directly.
- **C3** A gate pass exit / return applies the status only when the table allows it (an asset pending disposal that
  leaves keeps its status; the movement is still recorded).
- **C4** A transfer approved by Group IT returns the asset to "in use" if it has a holder, otherwise "in store".
- **C5** Gates and readers: System Administrator only (Group IT and the SBU Admin Manager keep the other locations).

### 11.5 Questions

1. C1: should SBU IT / the SBU Admin Manager see cost (e.g. to judge repairs), and SBU HR the NBV of missing items (HR-05)?
2. C2: disposals decided by SBU Finance alone until Phase 4 adds the Group sign-offs: acceptable for now?

### 11.6 How to demo (after `pnpm db:migrate` and `pnpm seed --reset`)

1. **tsas.it** → a TSAS laptop card → *Change status*: the list shows what the table allows from "in use" and why any
   option is refused. Choose *Lost / stolen* without notes: refused (GI-07); with date, reporter and notes: done, and
   the Operations dashboard's active count drops.
2. **tsas.it** → another laptop → *Recommend for disposal*; *Assign* is now refused (SI-08). **tsas.finance** →
   *Disposed*; no further change is possible.
3. **tsas.it** → *Assign* an untagged item: refused (ST-02).
4. **group.it** → any asset card: no cost section; the asset list shows no values. **group.finance**: values shown.
5. **sys.admin** → Administration → Gates & readers: add a gate and a reader; Access log shows every request.
