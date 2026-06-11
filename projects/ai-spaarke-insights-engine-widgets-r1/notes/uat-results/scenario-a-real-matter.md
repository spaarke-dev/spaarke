# Scenario A — Real Matter (≥3 KPI assessments) — UAT Results

> **Task**: 061 — End-to-end UAT (SC-05 + SC-06 cache hit)
> **Wave**: 5 (parallel with 062-066)
> **Date**: 2026-06-11
> **Rigor**: STANDARD (POML `<rigor>STANDARD</rigor>`)
> **Persona**: **Lara — Legal Operations Analyst** (per Task 060 scenario)
> **Targets**: **SC-05** (e2e narrative + citations + persistence) + **SC-06** (cache hit <100ms)
> **Mode**: Sub-agent static verification + operator playbook (sub-agent boundary — cannot live-invoke BFF or Dataverse). Same pattern as Task 024 `smoke-test-results.md`.
> **Visible-render caveat (Task 043 P1 gap)**: r2 IIFE bundle not yet shipped; r1 verifies SC-05 via **BFF envelope + Dataverse field write + telemetry**, NOT visible card content.

---

## Executive summary

| SC | Verification path (r1) | Static result | Live result | Verdict |
|---|---|---|---|---|
| **SC-05** | Envelope shape + `sprk_performancesummary` write + `widget.insightcard.invoked` telemetry | ✅ Statically verified | ⏳ Pending operator | ⚠️ **PARTIAL (deferred portion: visible card render → r2)** |
| **SC-06** | Network timing + `cacheHit=true` dim on 2nd invocation; p95 <100ms | ✅ Cache stack statically confirmed | ⏳ Pending operator | ⚠️ **Pending operator live run; fully verifiable in r1 once executed** |

**No blockers preventing operator live execution.** Path-A (Guid-direct) playbook deploy (Task 023) + endpoint telemetry wiring (Task 051) + Matter form OnLoad pre-warm (Tasks 040-043) all landed. The operator script in §6 below is ready to copy/paste.

---

## 1. Scope of this run

Task 061 POML defines 5 steps:

1. Open Matter form (Scenario A target)
2. Click sparkle → verify card renders 7-dimension narrative + citations
3. Verify envelope on record (`sprk_performancesummary` contains envelope)
4. Second click → verify cache hit telemetry + sub-100ms response
5. Document (screenshots + telemetry refs)

POML acceptance criteria:

- SC-05 pass
- SC-06 pass with sub-100ms cache hit

Sub-agent boundary (per Task 024 precedent): no network/auth access to Spaarke Dev BFF or Dataverse Web API; no Chrome integration for MDA UI; live execution is the **operator's responsibility** with the script in §6.

---

## 2. Persona prerequisites (Lara)

Per `notes/uat-scenarios.md` Scenario A persona:

- **Power Apps maker access** on `spaarkedev1` (required for advanced find + Web API GET to confirm `sprk_performancesummary`)
- **Reviewer-level technical comfort** — DevTools Console + Network tab visible
- Signed-in browser as `lara@spaarkedev1.onmicrosoft.com` (or operator's equivalent — `tid+oid` claims drive the cache key per Task 024 §6)

Prerequisites (per Task 060 scenario doc, restated):

1. **Dev BFF healthy**: `GET https://spaarke-bff-dev.azurewebsites.net/healthz` → `200 OK`
2. **Kill-switches OFF**: `DocumentIntelligence:Enabled=true` AND `Insights:Enabled=true` (default dev state)
3. **UAT Matter exists** with ≥3 KPI assessments per area (3 areas × ≥3 = ≥9 rows; the more restrictive `sprk_kpiassessmentcount ge 12` filter in the scenario doc is a strong proxy)
4. **`sprk_aitopicregistry` row deployed** for `matter-health-single` with `sprk_cachettlminutes=60` (verified in Task 027)
5. **Telemetry meter `Sprk.Bff.Api.InsightWidgets` deployed** (Tasks 050 + 051 verified — see `notes/handoffs/telemetry-events-verified.md`)

---

## 3. SC-05 — Static verification (envelope + persistence + telemetry)

### 3.1 Envelope shape (FR-14)

Per Task 024 `smoke-test-results.md` §4 — verified against `matter-health-single.playbook.json:248` persistEnvelope template:

```json
{
  "schemaVersion":  "1.0",
  "body":           "<markdown narrative>",
  "citations":      [ <array> ],
  "generatedAt":    "<UTC ISO 8601>",
  "playbookName":   "matter-health-single",
  "tenantId":       "<Guid>",
  "dimensions":     [ <7-element array — composite, trend, themes, inflection, critical, risk, evidenceGaps> ]
}
```

**Status**:
- ✅ `schemaVersion` literal `"1.0"` (Q-U1 compliant — no `@v`)
- ✅ 7 dimensions wired via `groundCitations.output.dimensions`
- ❌ **`playbookVersion` field omitted** (Task 022 divergence — documented in Task 024 §7 as recommended fix for Task 025 or follow-up `024.1`). This is **NOT a Task 061 blocker** — SC-05 acceptance language in spec.md:282 reads "narrative + citations rendered" + "`sprk_performancesummary` updated with JSON envelope"; both are satisfied by the 7-field shape. Task 024 §7 already captured the spec-divergence finding for tracking.

### 3.2 Persistence wiring (FR-14 + FR-16)

Per Task 024 §5 — persistEnvelope node (lines 227-256 of `matter-health-single.playbook.json`):

- ✅ `nodeType = "Output"`, `actionCode = "INS-UPDR"`, `actionType = 22` (UpdateRecord)
- ✅ `dependsOn = ["ReturnInsightArtifactNode"]` (runs after artifact production)
- ✅ `entityLogicalName = "sprk_matter"`, `recordId = "{{matterId}}"`, `fieldMappings[0].field = "sprk_performancesummary"`
- ✅ Action code `INS-UPDR` deployed in spaarkedev1 (Task 023, `sprk_analysisaction` row `62a1687d-4965-f111-ab0c-7ced8ddc4a05`)

A successful first-click invocation will overwrite the R5 placeholder text in `sprk_performancesummary` with the serialized JSON envelope per FR-15 downstream-consumer contract (unconditional overwrite per `UpdateRecordNodeExecutor.cs`).

### 3.3 Telemetry events (per Task 051)

The BFF endpoint emits via the `Sprk.Bff.Api.InsightWidgets` meter (per `notes/handoffs/telemetry-events-verified.md`):

| Event | Type | Bounded dims | When |
|---|---|---|---|
| `widget.insightcard.invoked` | counter | `topic`, `mode`, `outcome`, `cacheHit`, `tenant.id` | every non-cancellation/non-validation exit |
| `widget.insightcard.duration` | histogram | same | same |

> **Note on event-name precision**: The task description and `current-task.md` reference `InsightWidgets.Invocation` — that is the **meter name** (`Sprk.Bff.Api.InsightWidgets`) plus a conceptual event name. The **actual metric name** emitted to App Insights `customMetrics` per Task 051 §"Dimension mapping" is `widget.insightcard.invoked` (counter) + `widget.insightcard.duration` (histogram). Operator queries below use the actual emitted name.

For SC-05, Lara's first sparkle click produces:

- `widget.insightcard.invoked` counter `+1` with dims `{topic="matter-health", mode="single", outcome="success", cacheHit="false", tenant.id="<tid>"}`
- `widget.insightcard.duration` histogram entry with same dims and end-to-end ms (cache lookup + playbook execution + serialization + persist)

Activity span `InsightSummaryCard.Invoke` with high-cardinality tags (`subject = matter:<GUID>`, `correlationId`, etc.) per ADR-014/015 cardinality discipline.

### 3.4 SC-05 visible-render gap (Task 043 P1)

Per `notes/uat-scenarios.md` §"Phase 4 IIFE-bundle visible-render gap":

> "the React `InsightSummaryCard` IIFE bundle is **NOT deployed** as a `WebResource` control on `tab_report card_section_3` of the Matter form. … the mount-glue script (`sprk_matter_insight_card.js`) loads and the fire-and-forget POST fires correctly (FR-17, FR-18 verified), but **no visible card renders** until r2 ships the IIFE bundle + control binding."

**Implication for Task 061**: SC-05's "narrative + citations rendered" is verified in r1 via **BFF response shape (§3.1) + Dataverse field write (§3.2) + telemetry (§3.3)**, NOT visual card content. Visible-render verification is **deferred to r2 / P1 follow-up** once the IIFE bundle ships.

This caveat is consistent with Task 043 handoff `form-deploy.md` §"Documented Gap" and Task 044 handoff `phase-4-e2e-test.md` §FR-19 (PARTIAL).

---

## 4. SC-06 — Static verification (cache hit <100ms p95)

Per Task 024 §6 — cache stack unchanged from r2:

1. `InsightsOrchestrator` → `InsightsPlaybookExecutionCache.GetOrExecuteAsync(playbookId, subject, parameters, accessibleScopeHash, ttl, …)`
2. Key composition: tenant + caller's `accessibleScopeHash` (SHA-256 of `tid:<tid>|oid:<oid>` per `InsightEndpoints.cs:352-357`)
3. Backing store: `IDistributedCache` (Redis in dev/prod) per ADR-009
4. TTL: 60 minutes (FR-21 default for `matter-health`; Task 052 plumbs per-topic TTL override)

A live second-click within the 60-minute TTL window with the **same caller** (same `tid+oid`) will:

- Hit cache; `X-Insights-Cache: true` response header; `X-Insights-Elapsed-Ms: <small>`
- Emit `widget.insightcard.invoked` with `outcome="cache_hit"`, `cacheHit="true"`
- Emit `widget.insightcard.duration` histogram entry with the cache-hit ms

**SC-06 acceptance**: 2nd call `X-Insights-Elapsed-Ms` ≤100 ms; `cacheHit=true` telemetry on 2nd event. NFR-02 + SC-06 align.

**No r2 deferral for SC-06.** Visible-render gap does NOT affect cache hit measurement — it's a server-side timing + telemetry concern.

---

## 5. Step-by-step operator playbook (Lara)

This expands the 5 steps in the POML into an executable script. Operator runs this against the **deployed** Spaarke Dev BFF + `spaarkedev1` Dataverse, signed in as Lara (or equivalent dev account with Matter read + Insights:Ask permissions).

### Pre-flight (operator)

```powershell
# 1. Confirm dev BFF health (anonymous)
Invoke-WebRequest -Uri "https://spaarke-bff-dev.azurewebsites.net/healthz" -UseBasicParsing
# Expect: 200 OK

# 2. Confirm kill-switches OFF (browse Azure Portal → App Service config)
# DocumentIntelligence__Enabled = true ; Insights__Enabled = true

# 3. Pick a qualifying UAT Matter (≥3 KPI assessments per area)
$tok = az account get-access-token --resource api://<bff-client-id>/.default --query accessToken -o tsv
# Use Dataverse Web API to find one — see Task 024 §2 query for ≥9 row filter
# (or follow Scenario A prerequisite query: $filter=sprk_kpiassessmentcount ge 12)
# Record the Matter GUID for Steps 1-4 below.
$MATTER_GUID = "<Guid>"  # ← fill in
```

### Step 1 — Open Matter form (FR-17 OnLoad signal)

1. Browser: navigate to `https://spaarkedev1.crm.dynamics.com` → open the qualifying Matter form
2. Open DevTools → Console + Network tabs visible
3. **Expected console**: `[sprk-matter-insight-card] mount-glue loaded` (FR-17 signal)
4. **Expected within ≤2s**: `[sprk-matter-insight-card] pre-warm POST fired` with `{question: "matter-health-single", subject: "matter:<GUID>"}` (FR-18 fire-and-forget)
5. **Expected Network**: `POST /api/insights/ask` returning `200 OK` (1st invocation — populates cache + writes envelope)

### Step 2 — Click sparkle icon

1. Click the sparkle icon on `tab_report card_section_3` (record header AI affordance)
2. **Expected console**: `[sprk-matter-insight-card] sparkle click → fetch`
3. **Expected Network**: a second `POST /api/insights/ask` — **if within 60-min TTL of Step 1's pre-warm, this is cache hit**; response body contains envelope with `body`, `citations[]`, `dimensions[]`, `schemaVersion: "1.0"` (7 fields per §3.1)
4. **Note (visible-render gap)**: **No card visible until r2 IIFE bundle.** Lara verifies the envelope via Network tab → response body inspection, NOT visual card content.

### Step 3 — Verify persistence (Lara via Web API)

```powershell
# Refresh Matter form (F5) to force Dataverse reload, then:
$mat = Invoke-RestMethod `
  -Uri "https://spaarkedev1.crm.dynamics.com/api/data/v9.2/sprk_matters($MATTER_GUID)?`$select=sprk_performancesummary" `
  -Headers @{ Authorization = "Bearer $tok"; "OData-Version" = "4.0" }

$envelope = $mat.sprk_performancesummary | ConvertFrom-Json
$envelope | ConvertTo-Json -Depth 5
# EXPECT: schemaVersion="1.0", playbookName="matter-health-single", body=<markdown>,
#         citations=<array>, dimensions=<7-element array>, tenantId=<Guid>, generatedAt=<ISO>
# EXPECT: R5 placeholder text overwritten (unconditional UpdateRecord per FR-14/15)
```

### Step 4 — Second sparkle click → cache hit (SC-06 verification)

1. Click sparkle icon again **within the 60-min TTL window** (the same browser session, no F5 in between)
2. **Expected Network**: `POST /api/insights/ask` returns in **<100 ms** with `X-Insights-Cache: true` header and `X-Insights-Elapsed-Ms: <small ms value>`
3. **Telemetry confirmation** (App Insights — Lara or Priya runs):

```kql
// Cache hit on 2nd invocation
customMetrics
| where name == "widget.insightcard.invoked"
| where customDimensions["topic"] == "matter-health"
| where timestamp > ago(30m)
| project timestamp, outcome=tostring(customDimensions["outcome"]), cacheHit=tostring(customDimensions["cacheHit"])
| order by timestamp asc

// Expect: ≥2 rows; 1st with outcome="success" cacheHit="false"; 2nd with outcome="cache_hit" cacheHit="true"

// p95 duration on cache-hit path (SC-06 ≤100ms)
customMetrics
| where name == "widget.insightcard.duration"
| where customDimensions["cacheHit"] == "true"
| where timestamp > ago(30m)
| summarize p95 = percentile(value, 95)
// Expect: p95 ≤ 100 ms
```

### Step 5 — Document (operator)

Operator captures:
- **Screenshot 1**: Network tab showing 1st `POST /api/insights/ask` with body + `X-Insights-Cache: false`
- **Screenshot 2**: Network tab showing 2nd `POST /api/insights/ask` with `X-Insights-Cache: true` + elapsed <100ms
- **Screenshot 3** (optional): Dataverse Web API response showing `sprk_performancesummary` envelope
- **Screenshot 4**: App Insights KQL result showing both invocation events with correct outcome/cacheHit dims
- **Append** to §7 sign-off table below.

---

## 6. Operator command summary (copy/paste ready)

Full reference script (mirrors Task 024 §6 Path A — uses playbook **Guid** directly to avoid dependency on canonical-name map deploy):

```powershell
# Setup
$tok = az account get-access-token --resource api://<bff-client-id>/.default --query accessToken -o tsv
$MATTER_GUID = "<qualifying-matter-guid>"
$body = @{
  question = "a0d49d0d-4a65-f111-ab0c-70a8a590c51c"   # matter-health-single playbook Guid (Task 023 deploy)
  subject  = "matter:$MATTER_GUID"
} | ConvertTo-Json -Compress

# Step 2 surrogate — 1st invocation (operator sparkle click is browser-driven; PowerShell call is for cleanroom validation)
$r1 = Invoke-WebRequest -Method POST `
  -Uri "https://spaarke-bff-dev.azurewebsites.net/api/insights/ask" `
  -Headers @{ Authorization = "Bearer $tok"; "Content-Type" = "application/json" } `
  -Body $body
$r1.Headers["X-Insights-Cache"]      # EXPECT: "false" (1st call)
$r1.Headers["X-Insights-Elapsed-Ms"] # EXPECT: large (3000-8000 ms — full playbook drain)
$envelope1 = $r1.Content | ConvertFrom-Json
$envelope1.schemaVersion  # EXPECT: "1.0"
$envelope1.playbookName   # EXPECT: "matter-health-single"
$envelope1.dimensions.Count  # EXPECT: 7

# Step 3 — Verify persistence
$mat = Invoke-RestMethod `
  -Uri "https://spaarkedev1.crm.dynamics.com/api/data/v9.2/sprk_matters($MATTER_GUID)?`$select=sprk_performancesummary" `
  -Headers @{ Authorization = "Bearer $tok"; "OData-Version" = "4.0" }
$persisted = $mat.sprk_performancesummary | ConvertFrom-Json
$persisted.schemaVersion  # EXPECT: "1.0"
$persisted.playbookName   # EXPECT: "matter-health-single"

# Step 4 — 2nd invocation (cache hit)
$r2 = Invoke-WebRequest -Method POST `
  -Uri "https://spaarke-bff-dev.azurewebsites.net/api/insights/ask" `
  -Headers @{ Authorization = "Bearer $tok"; "Content-Type" = "application/json" } `
  -Body $body
$r2.Headers["X-Insights-Cache"]      # EXPECT: "true"  (SC-06)
$r2.Headers["X-Insights-Elapsed-Ms"] # EXPECT: ≤100 ms (SC-06)
```

The browser-driven sparkle clicks in Steps 2 + 4 produce the **same telemetry events** as the PowerShell calls (same endpoint, same caller claims). Operator can choose to verify in-browser OR via PowerShell OR both (the latter gives a cleaner Network trace).

---

## 7. Acceptance criteria summary

| POML criterion | Verification path | Result | Notes |
|---|---|---|---|
| **SC-05 pass** | Envelope shape (§3.1) + `sprk_performancesummary` write (§3.2) + telemetry (§3.3) | ⚠️ **PARTIAL** — static OK; live pending operator; **visible card render DEFERRED to r2** per Task 043 P1 | Operator script in §5/§6; r2 IIFE bundle restores full SC-05 |
| **SC-06 pass with sub-100ms cache hit** | Cache stack (§4) + Network timing + `cacheHit=true` telemetry | ⚠️ **PENDING OPERATOR** — static OK | Fully verifiable in r1 (no r2 deferral); operator captures `X-Insights-Elapsed-Ms ≤ 100` on 2nd call |

| Cross-task signal | Verdict |
|---|---|
| Audit DR-003 invocation through `IInsightsAi.AnswerQuestionAsync` | ✅ CONFIRMED (per Task 024 §4 — `InsightEndpoints.cs:249`) |
| Q-U1 compliance (no `@v` in envelope) | ✅ CONFIRMED (per Task 024 §4 substring scan = 0) |
| Telemetry meter `Sprk.Bff.Api.InsightWidgets` emits invocation + duration | ✅ CONFIRMED at source (Tasks 050 + 051 — see `notes/handoffs/telemetry-events-verified.md`) |
| `playbookVersion` field present (FR-14 8-field shape) | ❌ DIVERGED — 7 fields emitted; Task 024 §7 captured as `024.1` follow-up; **not a Task 061 blocker** per SC-05 acceptance language |

### Sign-off ledger (operator-completed during live run)

| Signer | Role | Date | SC-05 (partial) | SC-06 | Comments |
|---|---|---|---|---|---|
| | Lara / operator | | ⏳ | ⏳ | (e.g., "SC-05 visible-render deferred to r2 per §3.4 caveat") |
| | | | | | |

---

## 8. Blockers + recommended next operator actions

### Blockers preventing live verification by this sub-agent

1. **Network/auth boundary**: no path to authenticate against Spaarke Dev BFF or `spaarkedev1` Web API. A live operator (Lara, project owner, or any operator with BFF user token + Web API read on Matter) is needed.
2. **No Chrome integration**: cannot drive MDA UI for in-browser sparkle clicks. Operator handles browser steps; PowerShell §6 script is the cleanroom alternative.

### NOT blockers

- ✅ Playbook deployed (Task 023 — Guid `a0d49d0d-4a65-f111-ab0c-70a8a590c51c`)
- ✅ Mount-glue + OnLoad pre-warm deployed (Tasks 040-043)
- ✅ Telemetry meter wired in endpoint (Task 051)
- ✅ `sprk_aitopicregistry` deployed with 60-min TTL (Task 027)
- ✅ UAT scenarios authored with persona + step granularity (Task 060)

### Recommended next action

Operator runs §6 PowerShell script + §5 browser walkthrough during a single ~10-min window. Two PowerShell invocations within 60 min suffice to verify SC-06; envelope inspection on `r1.Content` covers SC-05 §3.1/§3.2; KQL queries on App Insights cover SC-05 §3.3 and SC-06 telemetry confirmation.

Sign-off ledger entry goes in §7 above; aggregation handoff to Task 066 (UAT close-out) per `TASK-INDEX.md`.

---

## 9. Files inspected (no source code modified by this task)

| Path | Purpose |
|---|---|
| `projects/.../tasks/061-e2e-uat-execution.poml` | Task contract |
| `projects/.../notes/uat-scenarios.md` | Scenario A persona + steps + caveats (Task 060) |
| `projects/.../notes/handoffs/smoke-test-results.md` | Task 024 static verification pattern (precedent) |
| `projects/.../notes/handoffs/telemetry-events-verified.md` | Task 051 telemetry wiring + KQL queries |
| `projects/.../notes/handoffs/phase-4-e2e-test.md` | Task 044 FR-17/18/19 verdicts + visible-render gap |
| `projects/.../spec.md` SC-05, SC-06, FR-14, FR-21, NFR-02 | Acceptance contract |
| `projects/.../CLAUDE.md` | Project context, ADR map, audit DR map |

This task produced ONLY the file `projects/ai-spaarke-insights-engine-widgets-r1/notes/uat-results/scenario-a-real-matter.md` (this document). No source-code changes; no other-task `.poml` touched (Wave 5 parallel-safe — siblings 062-066 each write own scenario / aggregation file).

---

*Written 2026-06-11 by sub-agent executing `task-execute` on `tasks/061-e2e-uat-execution.poml`. Live UAT execution by Lara (or operator surrogate) is the gating action; once §7 sign-off table is filled in, Task 066 aggregates across Scenarios A/B/C for UAT close-out.*
