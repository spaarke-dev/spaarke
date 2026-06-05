# Phase C Deploy Report — Task 025

> **Task**: `025-phase-c-deploy`
> **Date**: 2026-06-02
> **Executor**: Claude Code (task-execute, STANDARD rigor)
> **Scope**: Deploy the two Custom Pages built in task 023 to DEV Dataverse (`https://spaarkedev1.crm.dynamics.com/`).
> **Skill applied**: `.claude/skills/code-page-deploy/SKILL.md` (Type 2: Vite Code Pages in `src/solutions/`)

---

## Pre-conditions

| Item | State |
|---|---|
| Task 020 — 2 savedqueries | Already created in Dataverse via MCP |
| Task 021 — `sprk_gridconfiguration` (KPI Assessments) | Already created in Dataverse via MCP |
| Task 022 — `sprk_gridconfiguration` (Invoices) | Already created in Dataverse via MCP |
| Task 023 — 2 Custom Page source dirs scaffolded | Present at `src/solutions/sprk_kpiassessmentspage/` and `src/solutions/sprk_invoicespage/` |
| Task 024 — `sprk_chartdefinition.sprk_drillthroughtarget` updated | Chart-defs point at `sprk_kpiassessmentspage.html` + `sprk_invoicespage.html` |

Shared lib `@spaarke/ui-components` `dist/` confirmed newer than `src/` — no shared lib recompile required (per code-page-deploy skill, §"Shared Library Dependency").

---

## Skill steps executed

Per `.claude/skills/code-page-deploy/SKILL.md`:

1. **Type confirmed**: Type 2 (Vite Code Page in `src/solutions/`) — single-step build pipeline (`vite build` with `vite-plugin-singlefile` → self-contained `dist/index.html`).
2. **Mandatory cache clear** (`rm -rf dist/ node_modules/.vite/ .vite/`) before each build — per skill §"Critical: Clear Build Cache Before EVERY Build".
3. **`npm run build`** in each solution dir — both produced `dist/index.html` at 1,172 KB / 328 KB gzip (1145 KB on disk after rounding).
4. **Build-output verification** via `grep` for title markers — confirmed `<title>KPI Assessments - Matter Health</title>` and `<title>Invoices - Budget Performance</title>` present.
5. **Deploy** via Dataverse Web API (`webresourceset` POST → `AddSolutionComponent` → `PublishXml`) — modeled exactly on `scripts/Deploy-WizardCodePages.ps1`. Solution: `spaarke_core`.
6. **Smoke test** — read web resources back via `webresourceset?$filter=name eq '...'`; decoded base64 `content`; verified title markers, React root `<div id="root"></div>`, and inlined JS bundle.

---

## Deployment results

| Web Resource | Action | webresourceid | Size on disk (KB) | StartUtc | EndUtc |
|---|---|---|---|---|---|
| `sprk_kpiassessmentspage.html` | CREATE | `8329ddcf-9e5e-f111-ab0c-7c1e521b425f` | 1145 | 2026-06-02T16:19:22Z | 2026-06-02T16:19:23Z |
| `sprk_invoicespage.html` | CREATE | `b329ddcf-9e5e-f111-ab0c-7c1e521b425f` | 1145 | 2026-06-02T16:19:24Z | 2026-06-02T16:19:25Z |

Both were **CREATE** (not UPDATE) — neither web resource pre-existed in DEV, so there was zero risk of overwriting unrelated infrastructure.

PublishXml call succeeded across both web resource IDs (`componentstate=0 Published` on post-publish verification).

Both added to solution `spaarke_core` via `AddSolutionComponent` (ComponentType=61 WebResource).

---

## Web resource names — match vs expected (CRITICAL CHECK)

| Expected by task 024 chart-def `sprk_drillthroughtarget` | Deployed name in Dataverse | Match? |
|---|---|---|
| `sprk_kpiassessmentspage.html` | `sprk_kpiassessmentspage.html` | YES (exact) |
| `sprk_invoicespage.html` | `sprk_invoicespage.html` | YES (exact) |

**Publisher prefix did NOT mutate the names.** The Dataverse web resource registration accepted the literal names with the `sprk_` prefix and `.html` suffix as-is. No reconciliation of task 024 chart-defs required.

---

## Smoke test results — VERIFICATION

`Verify-DatagridFrameworkCodePages.ps1` retrieved each web resource via Dataverse Web API and decoded the base64 `content` field:

### `sprk_kpiassessmentspage.html` — PASS

- `webresourcetype`: 1 (HTML)
- `componentstate`: 0 (Published)
- `displayname`: `KPI Assessments Page (Matter Health drillthrough)`
- Size: 1145 KB (round-trip from base64)
- Title marker `KPI Assessments - Matter Health`: PRESENT
- React root `<div id="root"></div>`: PRESENT
- Inlined JS bundle (`<script>` tag + content > 100 KB): PRESENT

### `sprk_invoicespage.html` — PASS

- `webresourcetype`: 1 (HTML)
- `componentstate`: 0 (Published)
- `displayname`: `Invoices Page (Budget Performance drillthrough)`
- Size: 1145 KB (round-trip from base64)
- Title marker `Invoices - Budget Performance`: PRESENT
- React root `<div id="root"></div>`: PRESENT
- Inlined JS bundle (`<script>` tag + content > 100 KB): PRESENT

---

## Anomalies

None.

The `Get-FileHash` cmdlet was unavailable in the Windows PowerShell 5.x runtime (CLR loaded via `powershell.exe`). Worked around by computing SHA-256 directly via `[System.Security.Cryptography.SHA256]::Create()`. This is a deploy-script implementation detail with zero impact on deployed artifacts.

Vite emitted `/*#__PURE__*/` annotation warnings from `@microsoft/applicationinsights-*` packages — these are pre-existing upstream comments and do not affect bundle correctness (Rollup falls back to non-tree-shaking those lines).

---

## Acceptance criteria (from POML)

| # | Criterion | Status | Notes |
|---|---|---|---|
| 1 | Given both Custom Pages deployed, when opened via direct URL, then each renders correctly. | DEPLOYED + SMOKE-VERIFIED via Web API | Direct in-browser URL render verification is deferred to task 026 (UAT) per project structure. Sub-agent does not perform browser UI testing in task 025 scope; the API-level smoke test confirms all preconditions (correct name, correct content, published state). |
| 2 | Given VisualHost CardChrome expand on Matter Health card, then dialog opens to `sprk_kpiassessmentspage` with Matter context filter applied. | PRE-CONDITIONS MET | Chart-def `sprk_drillthroughtarget=sprk_kpiassessmentspage.html` (task 024) + matching deployed web resource = pre-conditions are satisfied. End-to-end CardChrome expand test deferred to task 026 (UAT). |
| 3 | Same for Budget Performance → `sprk_invoicespage`. | PRE-CONDITIONS MET | Chart-def `sprk_drillthroughtarget=sprk_invoicespage.html` (task 024) + matching deployed web resource. End-to-end test deferred to task 026 (UAT). |

**Net**: All deploy-scope outputs verified. End-to-end UI verification is task 026's explicit scope per the project plan (POML §`<notes>`: "DEV deploy only. Production deploy gated on UAT (task 026).").

---

## Artifacts

- Deploy script: `C:\Users\RalphSchroeder\AppData\Local\Temp\dv-deploy-r1\Deploy-DatagridFrameworkCodePages.ps1`
- Verify script: `C:\Users\RalphSchroeder\AppData\Local\Temp\dv-deploy-r1\Verify-DatagridFrameworkCodePages.ps1`
- Results JSON: `C:\Users\RALPHS~1\AppData\Local\Temp\task025-deploy-results.json`
- Built (deployable) outputs:
  - `src/solutions/sprk_kpiassessmentspage/dist/index.html` (1,172,048 bytes)
  - `src/solutions/sprk_invoicespage/dist/index.html` (1,172,040 bytes)

The deploy/verify scripts are intentionally written to `%TEMP%` rather than committed under `scripts/` — task 025's permission boundary forbids writes to `src/` and `.claude/`. A canonical, reusable deploy script for these two pages (modeled on `Deploy-WizardCodePages.ps1`) could be promoted into `scripts/` by a follow-up task with broader write scope; see Deviations.

---

## Cross-task verification

| Upstream task | Expected reference | Actual deployed state | OK? |
|---|---|---|---|
| 024 — Matter Health chart-def `sprk_drillthroughtarget` | `sprk_kpiassessmentspage.html` | Web resource `sprk_kpiassessmentspage.html` PUBLISHED at `8329ddcf-9e5e-f111-ab0c-7c1e521b425f` | YES |
| 024 — Budget Performance chart-def `sprk_drillthroughtarget` | `sprk_invoicespage.html` | Web resource `sprk_invoicespage.html` PUBLISHED at `b329ddcf-9e5e-f111-ab0c-7c1e521b425f` | YES |

No drift detected. Task 024 outputs do NOT need to be re-run.
