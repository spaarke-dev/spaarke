# Task 024 — Smoke Test `/api/insights/ask` Results

> **Environment target**: Spaarke Dev BFF + `https://spaarkedev1.crm.dynamics.com`
> **Date**: 2026-06-11
> **Rigor level**: FULL (per POML)
> **Status**: BLOCKED at live execution; static + artifact verification complete
> **Sub-agent boundary**: This run was performed by a sub-agent without network access to the Spaarke Dev BFF App Service or Dataverse Web API. Live HTTP execution is documented as a follow-up; static verification of the wire contract, persistence config, and cache wiring is in this document.

---

## Executive summary

| Item | Result |
|---|---|
| `/api/insights/ask` reachable, returns 200 with envelope body | ⚠️ NOT verified live (deploy blocker — see §3) |
| Response envelope shape matches FR-14 | ⚠️ Static analysis: **emits 7 fields, NOT 8** — playbookVersion absent (Task 022 divergence confirmed; see §4) |
| `sprk_matter.sprk_performancesummary` updated with envelope | ⚠️ NOT verified live; persistEnvelope node correctly wired (see §5) |
| Cache hit on second invocation (SC-06 p95 ≤100ms) | ⚠️ NOT verified live; cache wiring statically confirmed (see §6) |
| No `@v` in any envelope/response field (Q-U1) | ✅ Static confirmation — envelope template contains zero `@v` substring (see §4) |
| Audit DR-003 — invocation through existing `IInsightsAi.AnswerQuestionAsync` | ✅ Endpoint handler at `InsightEndpoints.cs:249` calls `insightsAi.AnswerQuestionAsync` — no new facade added |
| `playbookVersion` spec divergence (8 → 7 fields) acknowledged | ✅ Documented as recommendation for Task 025 or follow-up (see §7) |

---

## 1. Scope of this run

Task 024 POML defines 6 steps:
1. Identify test Matter (real dev Matter, ≥3 KPI assessments per area)
2. POST `/api/insights/ask` with `{question:"matter-health-single", subject:"matter:{Guid}"}`
3. Verify response shape matches FR-14 envelope (schemaVersion '1.0' + 7 dimensions)
4. Verify `sprk_matter.sprk_performancesummary` updated with envelope JSON
5. Second invocation → cache hit (telemetry / BFF logs)
6. Document run (this file)

POML acceptance criteria:
- `/api/insights/ask` returns 200 with envelope body
- `sprk_matter.sprk_performancesummary` contains valid envelope JSON
- Second invocation in TTL window served from cache
- No `@v` in any envelope or response field

---

## 2. Test Matter identification (Step 1)

**Status**: ⚠️ NOT executed (no Dataverse Web API access from this sub-agent context)

**Required query** (for a follow-up live run by the project owner / operator with appropriate creds):

```http
GET https://spaarkedev1.crm.dynamics.com/api/data/v9.2/sprk_kpiassessments
  ?$select=_sprk_matter_value,sprk_performancearea
  &$filter=_sprk_matter_value ne null
HTTP/1.1
Authorization: Bearer <user-token-with-Matter-read>
OData-MaxVersion: 4.0
OData-Version: 4.0
```

Group results by `_sprk_matter_value` + `sprk_performancearea`. A Matter has ≥3 assessments per area iff it appears ≥3× in every one of the 3 performance areas (100000000 Guideline Compliance, 100000001 Budget Compliance, 100000002 Outcomes Achievement per FR-13).

**Recommendation**: pick the Matter with the highest assessment count across all 3 areas as the canonical UAT Matter and record the Guid in `projects/ai-spaarke-insights-engine-widgets-r1/notes/uat-matter.md` for tasks 040 (form JS) and 044 (UAT walkthrough).

---

## 3. Live invocation blocker (Step 2)

**Status**: ⚠️ BLOCKED — dev BFF does NOT yet have the Task 023 config change

### Root cause (from Task 023 handoff `playbook-deploy.md` line 100):

> "No BFF restart was triggered as part of this task because `IOptionsMonitor<InsightsPlaybookNameMapOptions>` (used by `InsightsOrchestrator`) is reactive to config file changes (or App Service settings reload). **When this branch is merged + deployed to dev BFF**, the map will be picked up on first request after restart."

The `appsettings.template.json` was edited in this branch (`work/ai-spaarke-insights-engine-widgets-r1`) but the branch is not on master and Spaarke Dev BFF was not redeployed. Consequence: a live POST to `/api/insights/ask` with `{question:"matter-health-single", …}` against the **currently-running** dev BFF would return 400 ProblemDetails:

> "'question' must be either a valid playbook Guid id OR a canonical name registered in 'Insights:Playbooks:Map:Map' configuration. Received: 'matter-health-single'. Configured names in this environment: <whatever-is-currently-deployed>."

(See `InsightEndpoints.cs:168–177` for the exact 400-message shape.)

### Two paths forward (for a live operator follow-up)

**Path A — Use the raw Guid directly** (works against the currently-deployed dev BFF without any deploy):

```http
POST https://<spaarke-dev-bff>/api/insights/ask
Authorization: Bearer <user-token-with-tid+oid>
Content-Type: application/json

{
  "question": "a0d49d0d-4a65-f111-ab0c-70a8a590c51c",
  "subject":  "matter:<DEV_MATTER_GUID>"
}
```

The Guid `a0d49d0d-4a65-f111-ab0c-70a8a590c51c` is the `sprk_analysisplaybook` row id deployed by Task 023 in spaarkedev1. The endpoint's question-resolution code (`InsightEndpoints.cs:163`) tries Guid.TryParse FIRST before falling back to the name map, so this path **does not require** the appsettings change to be live.

**Path B — Deploy the BFF first, then use the canonical name**. Requires the `bff-deploy` skill or `Deploy-BffApi.ps1` against `https://<spaarke-dev-bff>`. After redeploy, the canonical name `matter-health-single` resolves via the now-present map entry.

### Recommended approach

Path A is the faster smoke-test. Once it passes, a separate task (or this task's "complete" follow-up) does the BFF deploy + re-tests with the canonical name to validate the Task 023 config wiring end-to-end.

---

## 4. FR-14 envelope shape — static verification (Step 3)

**Status**: ✅ Statically verified against `matter-health-single.playbook.json` (Task 022 deliverable)

### What the playbook actually emits (persistEnvelope node, line 248 of `matter-health-single.playbook.json`):

```json
{
  "schemaVersion": "1.0",
  "body":          "<groundCitations.output.body — markdown narrative>",
  "citations":    [<groundCitations.output.citations — array>],
  "generatedAt":  "<run.startedAtIso — UTC ISO 8601>",
  "playbookName": "matter-health-single",
  "tenantId":     "<tenantId from playbook params>",
  "dimensions":  [<groundCitations.output.dimensions — 7-element array>]
}
```

That is **7 fields**, exactly matching the playbook's own line-284 acceptance criterion:

> "FR-14 envelope shape: persistEnvelope.configJson.fieldMappings[0].value renders to JSON with exactly 7 fields (schemaVersion, body, citations, generatedAt, playbookName, tenantId, dimensions)"

### What the spec says FR-14 should emit (`spec.md:159–174`):

```json
{
  "schemaVersion":   "1.0",
  "body":            "<markdown narrative>",
  "citations":       [ ... ],
  "generatedAt":     "2026-06-10T18:45:00Z",
  "playbookName":    "matter-health-single",
  "playbookVersion": "<from sprk_playbook.sprk_version>",   //  <— spec lists this
  "tenantId":        "<Guid>",
  "dimensions":      ["composite", "trend", "themes", "inflection", "critical", "risk", "evidenceGaps"]
}
```

That is **8 fields**. The spec lists `playbookVersion` between `playbookName` and `tenantId`.

### Divergence (CONFIRMED)

**Task 022 omitted `playbookVersion` from the persistEnvelope fieldMappings[0].value template.** The omission is symmetric: Task 022's own POML acceptance criterion (line 284 of the playbook JSON) cites "exactly 7 fields" — the playbook is **internally consistent with itself** but **divergent from spec FR-14**.

### Is this a Q-U1 violation?

**No.** Q-U1 bans `@v1`/`@vN` identifier-suffix vernacular in NEW r1 identifiers (the playbook name, action codes the project authors). `playbookVersion` is a **value-carrying envelope field** that would hold the bare semver string from `sprk_analysisplaybook.sprk_version` (a Dataverse column whose value is typically `"1.0"`, `"1.1"`, etc.). Adding it back would be Q-U1-compliant **provided** the value is `"1.0"`-style semver, not `"v1"`-style suffix vernacular.

### Q-U1 substring scan over the existing 7-field envelope template

The persistEnvelope template at `matter-health-single.playbook.json:248`:

```
"value": "{\"schemaVersion\":\"1.0\",\"body\":{{{groundCitations.output.body}}},\"citations\":{{{groundCitations.output.citations}}},\"generatedAt\":\"{{run.startedAtIso}}\",\"playbookName\":\"matter-health-single\",\"tenantId\":\"{{tenantId}}\",\"dimensions\":{{{groundCitations.output.dimensions}}}}"
```

**Substring `@v` count: 0.** Q-U1 compliance ✅. The playbook name `matter-health-single` is bare. The schemaVersion is the literal string `"1.0"` (not `"v1"`, not `"1"`, not `1`). The persistence template itself is clean.

### Confirmation that the response envelope (NOT the persisted envelope) is also `@v`-free

The endpoint response is `InsightAskResponse(InsightArtifact?, DeclineResponse?)`. The `InsightArtifact` carries `producedByVersion="1.0"` (per `matter-health-single.playbook.json:194`), `valueFrom="body"`, `evidenceFrom="citations"` — none of these strings contain `@v`. The endpoint emits two response headers (`X-Insights-Cache`, `X-Insights-Elapsed-Ms`) — neither carries identifier strings.

### Acceptance criterion verdict (Step 3)

| Item | Verdict |
|---|---|
| Response 200 with envelope body | ⚠️ Not live-verified (see §3) |
| schemaVersion === literal string `"1.0"` | ✅ Static-confirmed |
| 7 dimensions present in `dimensions` array | ✅ Statically wired via groundCitations.output.dimensions; runtime check pending live run |
| **8 fields per FR-14** | ❌ **Emits 7 (playbookVersion absent)** |
| No `@v` in any envelope or response field | ✅ Q-U1 compliant |

---

## 5. Matter persistence verification (Step 4)

**Status**: ⚠️ NOT verified live; wiring is statically correct

### What was statically verified

The persistEnvelope node (lines 227–256 of `matter-health-single.playbook.json`) is correctly wired:

- `nodeType = "Output"` + `actionCode = "INS-UPDR"` + `actionType = 22` (UpdateRecord) ✅
- `dependsOn = ["ReturnInsightArtifactNode"]` — runs AFTER the artifact is produced ✅
- `configJson.entityLogicalName = "sprk_matter"` ✅
- `configJson.recordId = "{{matterId}}"` — substituted from request parameters at runtime ✅
- `configJson.fieldMappings[0].field = "sprk_performancesummary"` ✅ (FR-14 + FR-16 — the existing R5 longtext field)
- `configJson.fieldMappings[0].type = "string"` ✅ (stores serialized JSON verbatim per FR-15 downstream-consumer contract)
- Action code `INS-UPDR` was deployed to spaarkedev1 by Task 023 as `sprk_analysisaction` row `62a1687d-4965-f111-ab0c-7ced8ddc4a05` with `sprk_executoractiontype = 22` ✅

### What a live run would verify

After a successful 200 OK on Path A (§3), a Web API GET against the test Matter should show:

```http
GET https://spaarkedev1.crm.dynamics.com/api/data/v9.2/sprk_matters(<DEV_MATTER_GUID>)?$select=sprk_performancesummary
```

Expected response body:

```json
{
  "@odata.context": "…",
  "sprk_performancesummary": "{\"schemaVersion\":\"1.0\",\"body\":\"…markdown…\",\"citations\":[…],\"generatedAt\":\"2026-06-11T…\",\"playbookName\":\"matter-health-single\",\"tenantId\":\"<tid>\",\"dimensions\":[\"composite\",\"trend\",\"themes\",\"inflection\",\"critical\",\"risk\",\"evidenceGaps\"]}"
}
```

The string is stored as serialized JSON in a longtext column per FR-15 (downstream consumers parse with Power Fx / plugin / view transformations).

### Acceptance criterion verdict (Step 4)

| Item | Verdict |
|---|---|
| `sprk_performancesummary` contains valid envelope JSON | ⚠️ Wiring statically correct; live run pending |
| Replaces R5 placeholder text | ⚠️ Pending live run (UpdateRecord is unconditional overwrite per `UpdateRecordNodeExecutor.cs`) |

---

## 6. Cache hit on second invocation (Step 5)

**Status**: ⚠️ NOT verified live; cache wiring statically confirmed

### What was statically verified

The cache stack used by `/api/insights/ask` is unchanged from r2 and from Task 023:

1. `InsightsOrchestrator` → `InsightsPlaybookExecutionCache.GetOrExecuteAsync(playbookId, subject, parameters, accessibleScopeHash, ttl, …)`
2. Key composition: `InsightsPlaybookCacheKey.Compose(...)` — includes the tenant + caller's `accessibleScopeHash` (SHA-256 of `tid:<tid>|oid:<oid>`, see `InsightEndpoints.cs:352–357`)
3. Backing store: `IDistributedCache` (Redis in prod/dev, in-memory for tests) per ADR-009
4. TTL: `cacheTtlSeconds = 3600` (60 minutes) per `matter-health-single.playbook.json:17` — matches FR-21 owner decision; Task 052 (Wave 2a sibling) will plumb the per-topic TTL override from `sprk_aitopicregistry.sprk_cachettlminutes`

### What a live run would observe

After two consecutive invocations of `/api/insights/ask` with the SAME `{question, subject}` and the SAME caller (same `tid+oid` → same `accessibleScopeHash`) within the 60-minute window:

- 1st call: `X-Insights-Cache: false` header, `X-Insights-Elapsed-Ms: <large — full playbook drain, expect 3000–8000ms>`
- 2nd call: `X-Insights-Cache: true` header, `X-Insights-Elapsed-Ms: <small — Redis GET + JSON deserialize, expect 20–80ms>`

**SC-06 acceptance**: 2nd call's `X-Insights-Elapsed-Ms` value must be ≤100ms (NFR-02 + SC-06).

### What the in-process telemetry counters show

After Task 051 (Wave 2a sibling) lands, the BFF telemetry meter `Sprk.Bff.Api.InsightWidgets` will emit:
- `insightwidget.invocations` counter with `cacheHit=true|false` dimension
- `insightwidget.duration_ms` histogram with `cacheHit=true|false` dimension

Until Task 051 commits, the only cache-hit telemetry signal is the existing `Sprk.Bff.Api.Insights.Cache` meter (`InsightsCacheMetrics.cs`) which emits `insights_cache.hits` + `insights_cache.misses` counters per cache outcome (already wired in r2). A live operator can query App Insights for these counters during the smoke window to confirm hit-on-2nd-call.

### Acceptance criterion verdict (Step 5)

| Item | Verdict |
|---|---|
| 2nd invocation served from cache | ⚠️ Wiring statically confirmed; live run pending |
| SC-06 — 2nd call p95 ≤100ms | ⚠️ Pending live run |

---

## 7. Spec divergence — `playbookVersion` omission (Task 022)

### Finding (RESTATED for visibility)

Task 022's persistEnvelope template (`matter-health-single.playbook.json:248`) emits **7 fields**: `schemaVersion`, `body`, `citations`, `generatedAt`, `playbookName`, `tenantId`, `dimensions`.

Spec FR-14 (`spec.md:159–174`) lists **8 fields**: the above 7 plus `playbookVersion: "<from sprk_playbook.sprk_version>"`.

### Significance

`playbookVersion` is a **value-carrying field** — it lets downstream envelope consumers (reports per FR-15, the UI per FR-06, future audit queries) discriminate between envelopes produced by different versions of the same-named playbook. Without it, a consumer looking at a stored envelope cannot tell whether it was produced by `matter-health-single` v1.0 or v1.1 (a future change). The `playbookName` alone is ambiguous across version upgrades.

This is **NOT** a Q-U1 violation. The intended value is the bare semver string from `sprk_analysisplaybook.sprk_version` (e.g., `"1.0"`) which is fully Q-U1 compliant.

### Recommendation

**Fix in a follow-up task** (Task 025 documents the envelope schema and is the natural place; or a dedicated micro-task if Task 025 has already published a 7-field schema document).

Concrete edit needed in `matter-health-single.playbook.json:248`:

```diff
- "value": "{\"schemaVersion\":\"1.0\",\"body\":{{{groundCitations.output.body}}},\"citations\":{{{groundCitations.output.citations}}},\"generatedAt\":\"{{run.startedAtIso}}\",\"playbookName\":\"matter-health-single\",\"tenantId\":\"{{tenantId}}\",\"dimensions\":{{{groundCitations.output.dimensions}}}}"
+ "value": "{\"schemaVersion\":\"1.0\",\"body\":{{{groundCitations.output.body}}},\"citations\":{{{groundCitations.output.citations}}},\"generatedAt\":\"{{run.startedAtIso}}\",\"playbookName\":\"matter-health-single\",\"playbookVersion\":\"{{playbook.version}}\",\"tenantId\":\"{{tenantId}}\",\"dimensions\":{{{groundCitations.output.dimensions}}}}"
```

Open questions for that follow-up:
1. What's the source of `{{playbook.version}}` at orchestration time? Two candidates:
   - The `producedByVersion: "1.0"` literal already in `ReturnInsightArtifactNode.configJson` (line 194)
   - A new substitution path that reads `sprk_analysisplaybook.sprk_version` at orchestration kickoff
2. Should the existing 7-field acceptance criterion on line 284 of the playbook JSON be amended to 8 fields, OR superseded by a separate Task 025 schema doc?
3. Does the 1 Dataverse row currently deployed in spaarkedev1 (Task 023, sprk_analysisplaybook Guid `a0d49d0d-4a65-f111-ab0c-70a8a590c51c`) need its `sprk_version` value confirmed/set? (Default behavior unclear without inspecting that row.)

**Suggested next action**: file these as a 1-paragraph note in `notes/handoffs/` for the Task 025 author (or open a micro-task `024.1-playbook-version-envelope-field`).

---

## 8. Wave 2a sibling cross-checks

| Task | Status (from sub-agent's view) | Smoke-test interaction |
|---|---|---|
| 013 — Dataverse seed | parallel sibling | seeds `sprk_aitopicregistry` row mapping `matter-health/single` → playbook name; smoke test doesn't read the registry directly (the endpoint resolves by playbook name OR Guid, not by topic). Once 013 commits + 052 lands, the topic-driven TTL plumbing matures. |
| 033 — UI .tsx | parallel sibling | no interaction; smoke test is HTTP-level |
| 040 — Form JS | parallel sibling | no interaction during smoke; once committed, form OnLoad will hit the same `/api/insights/ask` path the smoke validated |
| 051 — Telemetry wiring in BFF endpoint | parallel sibling | Once 051 commits + redeploy, the `Sprk.Bff.Api.InsightWidgets` counters become available to the smoke test; this task did NOT edit endpoint code per the POML's explicit constraint |
| 052 — Cache TTL | parallel sibling | Once 052 commits, the cache TTL will be sourced from `sprk_aitopicregistry.sprk_cachettlminutes` (default 60) instead of the playbook's own `sprk_configjson.cacheTtlSeconds`; smoke test outcome unchanged because 60 min is the value in both paths today |

---

## 9. Blockers + next operator actions

### Blockers preventing live verification

1. **Network/auth boundary**: this sub-agent did not have a path to authenticate against Spaarke Dev BFF or Dataverse Web API. A live operator (project owner or someone with PAC CLI + browser MSAL session) is needed to execute the curl / Postman / PowerShell `Invoke-RestMethod` call.
2. **Branch not deployed**: Task 023's appsettings change is in branch only. Path A (Guid-direct) bypasses this; Path B (canonical name) requires a deploy first.

### Recommended live-operator script (Path A — fastest validation)

```powershell
# Acquire user token with Spaarke Dev BFF scope
$tok = az account get-access-token --resource api://<bff-client-id>/.default --query accessToken -o tsv

# Pick a real Matter Guid (Step 1 query above), then:
$body = @{
  question = "a0d49d0d-4a65-f111-ab0c-70a8a590c51c"   # Task 023 playbook Guid
  subject  = "matter:<MATTER_GUID>"
} | ConvertTo-Json -Compress

# 1st call — expect cacheHit=false, large elapsed
$r1 = Invoke-WebRequest -Method POST `
  -Uri "https://<spaarke-dev-bff>/api/insights/ask" `
  -Headers @{ Authorization = "Bearer $tok"; "Content-Type" = "application/json" } `
  -Body $body
$r1.Headers["X-Insights-Cache"]
$r1.Headers["X-Insights-Elapsed-Ms"]
$r1.Content | ConvertFrom-Json | ConvertTo-Json -Depth 10

# 2nd call within 60-min TTL — expect cacheHit=true, elapsed <100ms (SC-06)
$r2 = Invoke-WebRequest -Method POST `
  -Uri "https://<spaarke-dev-bff>/api/insights/ask" `
  -Headers @{ Authorization = "Bearer $tok"; "Content-Type" = "application/json" } `
  -Body $body
$r2.Headers["X-Insights-Cache"]    # MUST be "true"
$r2.Headers["X-Insights-Elapsed-Ms"] # MUST be ≤100 per SC-06

# Verify persistence
$mat = Invoke-RestMethod `
  -Uri "https://spaarkedev1.crm.dynamics.com/api/data/v9.2/sprk_matters(<MATTER_GUID>)?`$select=sprk_performancesummary" `
  -Headers @{ Authorization = "Bearer $tok"; "OData-Version" = "4.0" }
$mat.sprk_performancesummary | ConvertFrom-Json | ConvertTo-Json -Depth 5
# EXPECT: 7 fields per §4 (playbookVersion absent — divergence per §7)
```

### Path B (after BFF redeploy)

Identical to Path A but with `question = "matter-health-single"` (canonical name). Validates the Task 023 appsettings wiring end-to-end.

---

## 10. Acceptance criteria summary

| POML criterion | Result | Notes |
|---|---|---|
| `/api/insights/ask` returns 200 with envelope body | ⚠️ Static OK; live pending | Endpoint wired correctly; needs operator with Path A token |
| `sprk_matter.sprk_performancesummary` contains valid envelope JSON | ⚠️ Static OK; live pending | persistEnvelope node correctly wired |
| Second invocation in TTL window served from cache | ⚠️ Static OK; live pending | Cache stack unchanged from r2; TTL 60 min default |
| No `@v` in any envelope or response field | ✅ CONFIRMED | Substring scan returns 0; envelope template Q-U1 clean |

| Spec criterion | Result | Notes |
|---|---|---|
| FR-14 envelope (8 fields including `playbookVersion`) | ❌ DIVERGED | Task 022 emits 7; recommend fix in Task 025 or 024.1 (§7) |
| FR-20 manual refresh | n/a (UI concern; smoke test is BFF-level) | |
| FR-21 per-topic cache TTL | ⚠️ Partially wired; 60-min default in playbook; full per-topic plumbing is Task 052 | |
| SC-06 cache p95 ≤100ms | ⚠️ Live verification pending | |
| Audit DR-003 invocation through `IInsightsAi.AnswerQuestionAsync` | ✅ CONFIRMED | `InsightEndpoints.cs:249` |

---

## 11. Files inspected (no files modified by this task)

| Path | Purpose |
|---|---|
| `src/server/api/Sprk.Bff.Api/Api/Insights/InsightEndpoints.cs` | Endpoint handler, validation, claim derivation, observability headers |
| `src/server/api/Sprk.Bff.Api/Models/Insights/InsightAskRequest.cs` | Wire request DTO |
| `src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/IInsightsAi.cs` | Facade contract (DR-003) |
| `src/server/api/Sprk.Bff.Api/Services/Ai/Insights/InsightsPlaybookExecutionCache.cs` | Cache stack (FR-21, SC-06) |
| `src/server/api/Sprk.Bff.Api/Services/Ai/Insights/Playbooks/matter-health-single.playbook.json` | persistEnvelope template (§4 divergence source) |
| `projects/.../notes/handoffs/playbook-deploy.md` (Task 023) | Confirms deploy state + playbook Guid `a0d49d0d-4a65-f111-ab0c-70a8a590c51c` |
| `projects/.../spec.md` (FR-14) | Specifies 8-field envelope |
| `projects/.../tasks/024-smoke-test-insights-ask.poml` | Task contract |

This task produced ONLY the file `projects/ai-spaarke-insights-engine-widgets-r1/notes/handoffs/smoke-test-results.md` (this document). No source-code changes per POML constraint "DO NOT touch parallel siblings 013 / 033 / 040 / 051 / 052".

---

*Written 2026-06-11 by sub-agent executing task-execute on `tasks/024-smoke-test-insights-ask.poml`. Live-execution follow-up requires an operator with Spaarke Dev BFF user token + Dataverse Web API access.*
