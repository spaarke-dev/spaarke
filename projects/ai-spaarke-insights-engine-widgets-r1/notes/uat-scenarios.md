# UAT Scenarios — Insights Engine Widgets r1

> **Author**: Task 060 (Wave 4)
> **Date**: 2026-06-11
> **Target SCs**: SC-05, SC-06, SC-07, SC-08 (from `spec.md`)
> **Environment**: Spaarke Dev BFF (`https://spaarke-bff-dev.azurewebsites.net`) + `spaarkedev1` Dataverse
> **Phase 6 consumers**: Tasks 061-066 execute these scenarios in parallel agents — each scenario below is **self-contained** and may be executed without reference to the others.

---

## Phase 4 IIFE-bundle visible-render gap (applies to Scenario A)

**As of Task 044 close (Phase 4 demo)**, the React `InsightSummaryCard` IIFE bundle is **NOT deployed** as a `WebResource` control on `tab_report card_section_3` of the Matter form. This is a known Task 043 P1 gap — the mount-glue script (`sprk_matter_insight_card.js`) loads and the fire-and-forget POST fires correctly (FR-17, FR-18 verified), but **no visible card renders** until r2 ships the IIFE bundle + control binding.

**Implication for UAT**: Scenarios that target FR-19 "visible card render" must verify via **console-observable signals + BFF response shape + Dataverse field write + telemetry** rather than visual card content. Visible-render verification is **deferred to r2 / P1 follow-up**.

This caveat is embedded in **Scenario A** (the only scenario where visible-render would otherwise be tested). Scenarios B and C verify their targets via BFF response / Network-tab inspection, which is **not blocked** by the IIFE gap.

---

## Scenario A — Real Matter (≥3 KPI assessments per area)

**Targets**: SC-05 (end-to-end narrative + citations + Dataverse persistence) + SC-06 (cache hit <100ms on second click)

### Persona

**Lara — Legal Operations Analyst, Spaarke Internal**
- Power Apps maker access to `spaarkedev1`
- Reviews Matter health summaries weekly
- Comfortable with Network tab + DevTools console (light technical user)

### Prerequisites

1. **Dev BFF healthy**: `GET https://spaarke-bff-dev.azurewebsites.net/healthz` returns `200 OK`
2. **Kill-switch OFF**: `DocumentIntelligence:Enabled=true` AND `Insights:Enabled=true` in dev BFF App Service config (default state)
3. **UAT Matter exists**: A `sprk_matter` record in `spaarkedev1` with **≥3 KPI assessments per area** across the registered scopes (status, deliverables, finance, risk, etc.). Operator must identify by querying:
   ```
   GET /api/data/v9.2/sprk_matters?$filter=sprk_kpiassessmentcount ge 12&$select=sprk_matterid,sprk_name&$top=5
   ```
   If no qualifying Matter exists, **seed one via** `scripts/Seed-UATMatter.ps1` (not in r1 scope — operator escalates if absent).
4. **`sprk_aitopicregistry` row deployed**: row for topic `matter-health-single` exists with `sprk_cachettlminutes=60` (verified in Task 027).
5. **Browser**: Chrome/Edge, signed in as `lara@spaarkedev1.onmicrosoft.com`, DevTools open (Console + Network tabs visible).

### Steps

| # | Action | Expected console / network signal |
|---|---|---|
| 1 | Navigate to the qualifying Matter form in `spaarkedev1` model-driven app | Form loads. Console shows `[sprk-matter-insight-card] mount-glue loaded` (FR-17 signal). |
| 2 | Wait for OnLoad pre-warm to complete (≤2s) | Console shows `[sprk-matter-insight-card] pre-warm POST fired` with `{question: "matter-health-single", subject: "matter:<GUID>"}`. Network tab shows `POST /api/insights/ask` returning `200 OK`. |
| 3 | Click the sparkle icon on `tab_report card_section_3` (record header AI affordance) | Console shows `[sprk-matter-insight-card] sparkle click → fetch`. Network tab shows a **second** `POST /api/insights/ask` → response body contains envelope with `narrative`, `citations[]`, `confidence`, `schemaVersion: "1.0"`. |
| 4 | Refresh the Matter form (F5) to reload from Dataverse | Open Advanced Find or Web API `GET /api/data/v9.2/sprk_matters(<GUID>)?$select=sprk_performancesummary`. Verify the field contains the JSON envelope from Step 3 (R5 placeholder text overwritten). |
| 5 | Click sparkle icon a **second time** within 60min TTL window | Network tab shows `POST /api/insights/ask` returns `<100ms`. Response header `x-cache-hit: true` OR App Insights `cacheHit=true` event. |
| 6 | Verify telemetry in App Insights (or operator-friendly Kusto saved query) | `customEvents | where name == "InsightWidgets.Invocation" and customDimensions.topic == "matter-health-single"` returns ≥2 events (one pre-warm, one user-click), one with `cacheHit=false`, one with `cacheHit=true`. |

### Expected outcome

- **SC-05 partial verification**: BFF returns a well-formed envelope with narrative + ≥1 citation; `sprk_performancesummary` field on the Matter is updated with the new JSON envelope (R5 placeholder overwritten). **Visible card render is NOT verified in r1** — see [Phase 4 IIFE caveat](#phase-4-iife-bundle-visible-render-gap-applies-to-scenario-a) above. Full SC-05 verification (visible narrative + citation chips on the form) is a **r2 / P1 follow-up** once the IIFE bundle ships.
- **SC-06 full verification**: Second sparkle click returns `<100ms` with `cacheHit=true` telemetry. No code-path differences expected once the IIFE bundle lands.

### Sign-off

| Signer | Role | Date | Pass/Fail | Comments (e.g., "SC-05 visible-render deferred to r2") |
|---|---|---|---|---|
| | | | | |
| | | | | |

---

## Scenario B — Low-data Matter (<2 KPI assessments)

**Targets**: SC-07 (decline rendering when data is insufficient)

### Persona

**Marc — Matter Lead, Spaarke Internal**
- Standard model-driven app user (no maker privileges required)
- Opens his own newly-created Matters frequently before data lands
- Non-technical (does NOT use DevTools); judges by what the UI shows

### Prerequisites

1. **Dev BFF healthy** (same as Scenario A)
2. **Kill-switch OFF** (default — same as Scenario A)
3. **Low-data Matter exists**: A `sprk_matter` record with **<2 KPI assessments** (e.g., newly-created Matter with only `sprk_name` and `sprk_clientid` populated). Operator can create one via the standard "+ New Matter" flow and leave it un-populated.
4. **Browser**: Chrome/Edge, signed in as `marc@spaarkedev1.onmicrosoft.com`.

### Steps

| # | Action | Expected outcome |
|---|---|---|
| 1 | Navigate to the low-data Matter form in `spaarkedev1` MDA | Form loads. (No special console signal required — Marc is non-technical.) |
| 2 | (Optional — operator with DevTools) Watch Network tab for the OnLoad pre-warm POST | `POST /api/insights/ask` returns `200 OK` with envelope where `decline=true` and `declineReason` populated (e.g., "Insufficient KPI assessments: 1 found, ≥2 required"). `narrative` and `citations` are empty/null. |
| 3 | Click the sparkle icon | Same envelope returned (cached). Response is fast and graceful. |
| 4 | (When IIFE bundle lands in r2) Card displays "Insufficient data to generate Matter Health summary" message with NO error toast, NO red banner, NO 500 / 503 status. | r1 verification: BFF response envelope's `decline=true` flag + non-empty `declineReason` text is the contract; visible decline rendering is r2. |

### Expected outcome

- **SC-07 verification**: BFF returns `200 OK` with `decline=true` envelope (NOT a 4xx/5xx error). The envelope's `declineReason` field contains operator-readable text. No exception thrown. No `sprk_performancesummary` update on the Matter (decline envelopes are NOT persisted — verified by re-querying the field after Step 3 and confirming it is unchanged from its prior value).
- **Visible decline UI** (the card showing "Insufficient data...") deferred to r2 along with the IIFE bundle.

### Sign-off

| Signer | Role | Date | Pass/Fail | Comments |
|---|---|---|---|---|
| | | | | |
| | | | | |

---

## Scenario C — Kill-switch ON (`DocumentIntelligence:Enabled=false`)

**Targets**: SC-08 (graceful 503 ProblemDetails per ADR-018 + ADR-019 + ADR-032)

### Persona

**Priya — Platform Operations Engineer, Spaarke Internal**
- Azure App Service contributor on `spaarke-bff-dev`
- Can toggle App Service configuration values + restart the service
- Uses DevTools Network tab routinely

### Prerequisites

1. **Operator has Contributor RBAC** on the Spaarke Dev App Service resource group
2. **Any Matter form** is openable in `spaarkedev1` MDA (does NOT need to be data-rich; even an empty Matter is fine — the kill-switch fires before any data check)
3. **Browser**: Chrome/Edge, DevTools open (Network tab active)

### Steps

| # | Action | Expected outcome |
|---|---|---|
| 1 | In Azure Portal → `spaarke-bff-dev` App Service → Configuration → Application settings, set `DocumentIntelligence__Enabled=false` (and/or `Insights__Enabled=false` per ADR-032 facade Null peer) | App Service restarts (~30s). |
| 2 | Verify `GET /healthz` returns `200 OK` (kill-switch does NOT take down the health endpoint) | Confirmed in browser or `curl`. |
| 3 | Open any Matter form in `spaarkedev1` MDA, DevTools Network tab visible | Form loads normally (kill-switch does NOT block form load — only the AI invocation). |
| 4 | Wait for OnLoad pre-warm POST OR click sparkle icon | `POST /api/insights/ask` returns **HTTP 503 Service Unavailable** with `Content-Type: application/problem+json`. |
| 5 | Inspect response body | Body matches ADR-019 ProblemDetails shape: <br>`{ "type": "https://spaarke.dev/errors/feature-disabled", "title": "Feature disabled", "status": 503, "detail": "...FeatureDisabledException...", "instance": "/api/insights/ask" }` |
| 6 | (When IIFE bundle lands in r2) Card displays graceful "AI temporarily unavailable" message. No JS exception in console; no white-screen. | r1 verification: 503 + ProblemDetails shape is the contract. Operator confirms via Network tab inspection. |
| 7 | **Cleanup**: Restore `DocumentIntelligence__Enabled=true` in App Service config; await restart; re-verify Scenario A Step 1 works | Restored to default state. |

### Expected outcome

- **SC-08 verification**: `POST /api/insights/ask` returns **HTTP 503** (NOT 500) with a valid `application/problem+json` body matching ADR-019 ProblemDetails shape. The kill-switch is enforced by the BFF facade Null peer (`FeatureDisabledException`) per ADR-032 §F.1. No silent failure. No `sprk_performancesummary` write occurs.
- **Visible graceful-error UI** (the card showing "AI temporarily unavailable") deferred to r2 along with the IIFE bundle.

### Sign-off

| Signer | Role | Date | Pass/Fail | Comments |
|---|---|---|---|---|
| | | | | |
| | | | | |

---

## Success Criteria Mapping (explicit)

| Success Criterion | Scenario | r1 verification path | r2 follow-up |
|---|---|---|---|
| **SC-05** — End-to-end narrative + citations + Dataverse persistence | A | BFF envelope shape + `sprk_performancesummary` field write + telemetry | Visible card render (FR-19) once IIFE bundle deployed |
| **SC-06** — Cache hit <100ms on second click | A | Network timing + `cacheHit=true` telemetry / response header | (none — fully verified in r1) |
| **SC-07** — Decline rendering for insufficient data | B | BFF envelope `decline=true` + `declineReason` text; no persistence | Visible decline card UI once IIFE bundle deployed |
| **SC-08** — Graceful kill-switch 503 ProblemDetails | C | HTTP 503 + `application/problem+json` body matching ADR-019 shape | Visible graceful-error UI once IIFE bundle deployed |

---

## Phase 6 Execution Pointers (Tasks 061-066)

Each scenario above is **self-contained** so Tasks 061-066 may run scenarios in parallel agents:

- **Task 061** — Operator-driven execution of Scenario A (Lara persona)
- **Task 062** — Operator-driven execution of Scenario B (Marc persona)
- **Task 063** — Operator-driven execution of Scenario C (Priya persona)
- **Tasks 064-066** — Aggregation, sign-off ledger, and UAT close-out (per `TASK-INDEX.md`)

Sub-agent boundary: scenarios run via operator + (optionally) sub-agent assist on Network/Console inspection. Sub-agents WRITE only to `projects/ai-spaarke-insights-engine-widgets-r1/notes/` (safe).

---

*UAT scenarios authored 2026-06-11 by Task 060. Visible-render gaps for FR-19 are explicitly deferred to r2 per Task 043 P1 finding.*
