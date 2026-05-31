# Insights Engine Guide

> **Audience**: developers + technical SMEs extending the Spaarke Insights Engine
> **Last Updated**: 2026-05-30
> **Companion documents**:
> - [`docs/architecture/INSIGHTS-ENGINE-ARCHITECTURE.md`](../architecture/INSIGHTS-ENGINE-ARCHITECTURE.md) §0a — Phase 1 completion + Phase 1.5 framing
> - [`projects/ai-spaarke-insights-engine-r2/design.md`](../../projects/ai-spaarke-insights-engine-r2/design.md) — Phase 1.5 project plan
> - [`projects/ai-spaarke-insights-engine-r1/SPEC.md`](../../projects/ai-spaarke-insights-engine-r1/SPEC.md) — Phase 1 deliverable contract

This guide is the practical companion to the architecture doc. It answers "how do I add this?" and "when should I use this vs. that?" questions.

---

## 1. Mental model in 90 seconds

The Insights Engine has three layers:

```
            ┌──────────────────────────────────────────────────┐
            │   CONSUMPTION (chat tools, endpoints, fields)    │
            │   ─ Spaarke Assistant tool calls                 │
            │   ─ POST /api/insights/ask (HTTP)                │
            │   ─ Future: Dataverse field auto-populations    │
            └────────────────────┬─────────────────────────────┘
                                 │  IInsightsAi facade (the only Zone B → Zone A path)
            ┌────────────────────▼─────────────────────────────┐
            │   COMPOSITION (JPS playbooks)                    │
            │   ─ predict-matter-cost@v1 (synthesis)           │
            │   ─ universal-ingest@v1 (ingest; Phase 1.5)      │
            │   ─ Future: predict-duration, summarize-financials│
            │   ─ Future: generic RAG fallback for chat        │
            │   Execution via Spaarke's JPS engine             │
            └────────────────────┬─────────────────────────────┘
                                 │
            ┌────────────────────▼─────────────────────────────┐
            │   SUBSTRATE (durable, question-agnostic)         │
            │                                                  │
            │   spaarke-insights-index (Azure AI Search):      │
            │     • Observations (extracted from documents)    │
            │     • Precedents (SME-authored patterns)         │
            │   Both filterable + vector-searchable            │
            │                                                  │
            │   Dataverse (live-read at synthesis time):       │
            │     • sprk_matter, sprk_project, sprk_invoice    │
            │     • Resolved by DataverseLiveFactResolver      │
            │                                                  │
            │   Dataverse (mirror for SME review):             │
            │     • sprk_analysis (Observation mirrors)        │
            │     • sprk_precedent (SME-authored patterns)     │
            └──────────────────────────────────────────────────┘
```

Three patterns by which content enters the substrate:

| Pattern | Source | Storage location |
|---|---|---|
| **Live-read** | `sprk_matter`/`sprk_project`/`sprk_invoice` rows | NOT stored — pulled fresh at synthesis time |
| **Ingest-then-store** | SPE document uploads → universal-ingest playbook → Observations | `spaarke-insights-index` + mirror to `sprk_analysis` |
| **Author-then-project** | SME authoring via admin endpoint → Confirm → projection | `sprk_precedent` (authority) + `spaarke-insights-index` (retrieval) |

Three patterns by which content is consumed:

| Pattern | What | When to use |
|---|---|---|
| **Pre-authored JPS playbook** | Question becomes a Dataverse-stored multi-step graph | High-value/high-stakes/structured-output (cost prediction, success likelihood, field auto-populations) |
| **Generic RAG** (Phase 1.5+) | Natural-language question → semantic search → LLM synthesis | Open-ended chat questions; ad-hoc exploration |
| **Direct retrieval API** (future) | `POST /api/insights/search` returns ranked Observations/Precedents | Power Apps + PCF surfaces that want raw retrieved evidence |

---

## 2. Decision framework: when to use what

### When to use Insights Engine (vs. other Spaarke AI)

| Question kind | Use Insights | Use other |
|---|---|---|
| "How does this matter/project/invoice compare to similar ones?" | ✅ Insights | |
| "Predict cost / duration / risk for this matter" | ✅ Insights | |
| "Summarize this single document's contents" | | Use existing **RAG document search** (`spaarke-files-index`) |
| "Extract specific fields from this contract" | | Use existing **JPS analysis playbooks** (Contract Review, Lease Review, etc.) |
| "Chat with me about this matter generally" | ✅ Insights (when Phase 1.5+ Assistant integration lands) — falls back to RAG for unstructured questions | Use existing **Spaarke Assistant** for general dialog |
| "Field auto-population (e.g., Financial Summary)" | ✅ Insights playbook triggered from Dataverse | |

The dividing line: **Insights = cross-matter comparative reasoning grounded in accumulated organizational memory**. Single-document tasks belong to other Spaarke AI.

### When to use a pre-authored JPS playbook vs. generic RAG (Phase 1.5+)

| Indicator | → Playbook | → Generic RAG |
|---|---|---|
| Output shape matters for UI rendering (field needs `{p25, p50, p75}`) | ✅ | |
| Audit trail / regulatory requirement | ✅ | |
| Structured Decline required when evidence is insufficient | ✅ | |
| Question is asked > 10 times/week | ✅ (worth the authoring effort) | |
| Specific evidence-sufficiency rules (e.g., "need ≥12 comparable matters") | ✅ | |
| Open-ended natural-language exploration | | ✅ |
| Long-tail question with no pre-authored playbook | | ✅ |
| Pure information retrieval ("show me 5 similar matters") | | ✅ (RAG returns retrieved evidence + a summary) |

In practice: invest playbook-authoring effort in the top ~5-30 questions; everything else uses the generic RAG path. The same `spaarke-insights-index` substrate feeds both.

---

## 3. Adding a new Insights playbook (developer process)

Phase 1 process — hand-authored JSON spec, deployed via PowerShell script. Phase 1.5+ will add tooling; Phase 2+ adds SME-friendly UI. For now:

### 3.1 Author the JSON spec

Location: `src/server/api/Sprk.Bff.Api/Services/Ai/Insights/Playbooks/<question-name>.playbook.json`

Reference example: `predict-matter-cost.playbook.json`

Minimum structure:

```jsonc
{
  "playbook": {
    "name": "<question-name>@v1",           // canonical name; @v1 versioning
    "description": "...",
    "isPublic": true,
    "isSystemPlaybook": true,
    "sprk_playbooktype": 0,                  // 0 = AiAnalysis (existing JPS enum)
    "sprk_configjson": {
      "mode": "insights",                    // discriminator: Insights-mode vs analysis-mode
      "tier": "inference",                   // or "observation"
      "cacheTtlSeconds": 300,
      "evidenceRule": { ... },               // MANDATORY for synthesis playbooks (D-06)
      "insufficientEvidenceTemplate": "...", // structured Decline template (D-49)
      "parameterSchema": { ... }             // JSON Schema for invocation params
    }
  },
  "nodes": [
    {
      "name": "resolveLiveFacts",
      "actionCode": "INS-LIVE-FACT",         // references sprk_analysisaction (Phase 1.5 Wave B)
      "nodeType": "AI Analysis",
      "outputVariable": "currentMatterFacts",
      "configJson": { ... },
      "dependsOn": []
    },
    // ... more nodes (LiveFact, IndexRetrieve, EvidenceSufficiency, Synthesize,
    //     GroundingVerify, ReturnInsightArtifact, DeclineToFind)
  ]
}
```

### 3.2 Author the synthesis prompt (if Layer 2 / Synthesize node is used)

Location (Phase 1): `src/server/api/Sprk.Bff.Api/Services/Ai/Insights/Prompts/<question-name>-synthesis.v1.txt`
Location (Phase 1.5+): JPS scope storage in Dataverse (TBD per Wave C2 design)

Prompt structure:
- Role declaration
- Output schema (JSON-mode structured output)
- Hard rules (e.g., "cite ≥12 evidence refs", "every claim must have verbatim quote")
- Decline guidance (when output would fabricate, return null)

### 3.3 Wire to JPS actions (Phase 1.5 Wave B critical step)

For EACH node in the playbook, ensure a corresponding `sprk_analysisaction` row exists in Dataverse. Phase 1 didn't do this, which is why `predict-matter-cost@v1` synthesis doesn't execute live yet.

Required actions for Insights node types (one-time setup per environment):

| ActionType enum value | Suggested `sprk_actioncode` | Purpose |
|---|---|---|
| `LiveFact = 80` | `INS-LIVE-FACT` | LiveFactNode dispatch |
| `IndexRetrieve = 90` | `INS-INDEX-RETRIEVE` | IndexRetrieveNode dispatch |
| `EvidenceSufficiency = 100` | `INS-EVIDENCE-SUFFICIENCY` | EvidenceSufficiencyNode dispatch |
| `DeclineToFind = 110` | `INS-DECLINE-TO-FIND` | DeclineToFindNode dispatch |
| `ReturnInsightArtifact = 120` | `INS-RETURN-ARTIFACT` | ReturnInsightArtifactNode dispatch |
| `GroundingVerify = 70` | `INS-GROUNDING-VERIFY` | GroundingVerifyNode dispatch |

These are infrastructure rows; create once per environment, reuse across all Insights playbooks.

### 3.4 Deploy to Dataverse

```powershell
$env:DATAVERSE_URL = "https://spaarkedev1.crm.dynamics.com"
.\scripts\Deploy-Playbook.ps1 `
    -DefinitionFile src\server\api\Sprk.Bff.Api\Services\Ai\Insights\Playbooks\<question-name>.playbook.json
```

Capture the playbook GUID from the output (e.g., `Playbook : <question-name>@v1 (63b80630-...)`).

### 3.5 Register the friendly-name → GUID mapping

In App Service Configuration (`spaarke-bff-dev`):

```
Name:  Insights__Playbooks__Map__<friendly_name_snake_case_v1>
Value: <playbook-GUID-from-step-3.4>
```

**App Service env-var constraint**: keys must match POSIX `[A-Za-z_][A-Za-z0-9_]*`. NO `@`, NO `-`. Use snake_case_only. The Dataverse `sprk_name` can still use `@v1`/kebab — the map key is purely the API-facing alias.

### 3.6 Smoke test

```powershell
$token = az account get-access-token --resource api://<API_APP_ID> --query accessToken -o tsv
$body = @{
  question = "<friendly_name_snake_case_v1>"
  subject  = "matter:<real-matter-guid>"
  parameters = @{ ... }
} | ConvertTo-Json

Invoke-RestMethod -Uri "https://spaarke-bff-dev.azurewebsites.net/api/insights/ask" `
    -Method POST -Headers @{ Authorization = "Bearer $token" } `
    -Body $body -ContentType 'application/json'
```

Expected: 200 OK with `{artifact: {...}, decline: null}` (sufficient evidence) OR `{artifact: null, decline: {...}}` (insufficient).

---

## 4. Adding a new practice area (Phase 1.5 Wave D)

Phase 1 only handles litigation-shaped documents implicitly. To add e.g. real-estate:

1. **Reference data**: confirm `sprk_practicearea_ref` row exists for "Real Estate" (or add it)
2. **Document-type taxonomy**: define real-estate document types (lease, deed, financing, title insurance, closing statement, etc.) in `sprk_documenttype_ref` (new entity per Phase 1.5 design)
3. **N:N matrix**: register which document types are valid for real estate via `sprk_practicearea_documenttype` (new N:N entity)
4. **Layer 1 classification prompt** (per-practice-area): author `classification.real-estate.v1.txt` (Phase 1 location) or JPS scope record (Phase 1.5+ location)
5. **Layer 2 extraction prompts** (per practice-area + document-type): author `extraction.real-estate.lease.v1.txt`, `extraction.real-estate.deed.v1.txt`, etc. with the appropriate field schemas
6. **Update universal-ingest playbook**: route classification + extraction to the appropriate prompts based on `sprk_matter.sprk_practicearea`
7. **Test fixtures**: add real-estate sample documents to `tests/Insights/fixtures/real-estate/`

This is substantial — count 2-3 days per practice area for prompts + testing. The supporting Phase 1.5 infrastructure (taxonomy entities, per-practice-area prompt routing) lands once and amortizes across all subsequent practice areas.

---

## 5. Adding a new subject entity type (Phase 1.5 Wave C+D)

Phase 1 hard-codes `subject: "matter:<guid>"`. To support project / invoice / etc.:

1. **Subject scheme registration**: update endpoint validation to accept new scheme (e.g., `project:`, `invoice:`)
2. **Live-fact resolver**: add per-entity resolver (e.g., `DataverseProjectLiveFactResolver`) implementing `ILiveFactResolver` interface, registered keyed by entity type
3. **LiveFactNode config**: extend to know which entity type to query (subject scheme parsing)
4. **Index scope shape**: extend `spaarke-insights-index` scope fields to carry per-entity context (e.g., `scope.projectId`, `scope.invoiceId` alongside `scope.matterId`)
5. **Ingest playbook**: route ingest based on document parent entity (a document on a project → project-context Observations; on an invoice → invoice-context Observations)
6. **Per-subject playbooks**: author Insights playbooks targeting the new entity type (e.g., `predict_project_completion_v1` taking `subject: "project:<guid>"`)

The 4-tier taxonomy and JPS engine are unchanged. What changes is the input contract and the per-entity adapters.

---

## 6. Authoring + iterating prompts

### Phase 1 (today): file-based

Prompts live in `src/server/api/Sprk.Bff.Api/Services/Ai/Insights/Prompts/` as `.txt` files. Versioned in git; edited via PR; deployed atomically with code via `Deploy-BffApi.ps1`.

Pros: PR review; deterministic deploy; test-friendly.
Cons: requires deploy to change; no per-tenant variation; SMEs can't iterate.

### Phase 1.5+ (planned per Wave C2): JPS scope storage

Prompts move to Dataverse-managed scope records (exact entity TBD per Wave C2 design — likely a new `sprk_prompt` entity OR per-playbook `sprk_configjson` storage). This enables:

- SME edits without redeploy
- Per-tenant prompt variants
- Visible in maker portal alongside other JPS scope catalog
- Future AI-assisted prompt authoring via builder UI

The migration plan is part of the Phase 1.5 r2 project.

### Prompt iteration discipline

Regardless of storage, when changing a prompt:

1. **Version bump**: `@v1` → `@v2`. Never edit in place — keep `@v1` for rollback.
2. **Calibration run**: re-run the eval harness (`PredictMatterCostEvalHarnessTests` or its equivalent) against the new prompt; compare metrics to baseline.
3. **SME review**: surface a sample of the new prompt's outputs in the Observation review queue (sprk_analysis with `sprk_disposition=PendingReview`) for SME validation before promoting to default.
4. **Cutover**: update the playbook to reference the new prompt version; old playbook stays available for A/B comparison.

---

## 7. Querying the Insights index directly (Phase 1.5+)

When the generic RAG endpoint lands (`POST /api/insights/search`), it will accept:

```jsonc
{
  "query": "natural language question or keywords",
  "subject": "matter:<guid>",           // optional — narrows scope
  "filters": {
    "artifactType": "observation",      // or "precedent"
    "predicate": "settlementAmount",    // optional
    "scope.practiceArea": "ip-licensing"
  },
  "topK": 10
}
```

Return shape: top-N ranked Observations/Precedents with their full envelopes + an LLM-synthesized summary citing the retrieved evidence.

Until that lands, ad-hoc index queries can use the Azure AI Search REST API directly with the per-environment admin key (operator runbook).

---

## 8. Operational concerns

### 8.1 Insights ingest opt-in (Phase 1.5 critical)

Phase 1 wired the `InsightsIngestJobHandler` (consumer side) but did NOT wire the producer-side surface to set `AiProcessingOptions.InsightsIngest = true` on upload jobs. Today, no upload triggers Insights ingest in practice.

Phase 1.5 must decide + implement ONE of:

- **All uploads for Insights-enabled tenants** (single config flag per tenant)
- **Per-matter-type policy** (e.g., only litigation matters get Layer 2 extraction)
- **Per-upload header** (clients pass `X-Ai-Insights-Ingest: true`)
- **Document-type-driven** (Layer 1 on everything cheap; Layer 2 only if classification gates pass)

This is in the Phase 1.5 design.md as a critical decision.

### 8.2 Mirror configuration prerequisite

`DataverseObservationMirror` requires `Insights:Mirror:InsightsObservationActionId` to be set to a real `sprk_analysisaction` row GUID for the mirror to actually write rows. When unset (empty GUID), the mirror logs a warning and skips writes — dev-safe but means no `sprk_analysis` rows are produced.

Per-environment setup checklist (operator runbook):

1. Create `sprk_analysisaction` row: `actioncode=INS-OBS`, `name=Insights Observation Mirror`
2. Capture the GUID
3. Set App Service config: `Insights__Mirror__InsightsObservationActionId = <GUID>`
4. Restart BFF App Service

Validation: subsequent ingest runs should produce `sprk_analysis` rows visible via `mcp__dataverse__read_query` filtered by `sprk_searchprofile='insights-observation@v1'`.

### 8.3 Cache behavior

`InsightsPlaybookExecutionCache` caches successful Inference artifacts for 5 minutes (configurable per-playbook via `sprk_configjson.cacheTtlSeconds`). Declines are NEVER cached — their reason is "insufficient evidence," and that condition changes the moment a new Observation lands. A cached decline would become stale immediately.

Cache key includes `playbookId + subject + parameters + accessibleScopeHash` so the same caller asking the same question gets the cached result; different callers with different access scopes never collide.

### 8.4 Cost guardrails

- **Layer 1 classification**: ~$0.001/document (small LLM call)
- **Layer 2 extraction**: ~$0.05/document (larger LLM call + structured output)
- **Synthesis (predict-matter-cost call)**: ~$0.02-$0.10 per call depending on cohort size
- **Embedding generation**: ~$0.0001/Precedent — negligible

Phase 1 per-document hard cap: $0.10 (observability-only Phase 1; per-tenant monthly cap deferred to Phase 1.5). Logged via App Insights metric `insights.ingest.cost.cap_exceeded`.

---

## 9. Testing patterns

### 9.1 Unit tests (in-process, mocked LLM)

- Mock `IInsightsAi` at the facade boundary for endpoint/handler/sync tests
- Mock `IOpenAiClient` to drive playbook execution tests deterministically
- Use `WebApplicationFactory<Program>` for full DI graph + endpoint round-trips
- Existing pattern: see `tests/unit/Sprk.Bff.Api.Tests/Api/Insights/InsightEndpointsTests.cs` (17 tests)
- Insights subset total today: **379 tests passing** as of Phase 1 close

### 9.2 Eval harness (in-process, golden dataset)

- Author golden tuples in `tests/Insights/golden/<question-name>.json`
- Each tuple: parameters + mocked cohort size + expected response shape + expected metric bands
- Eval harness runs all tuples + computes: groundedness pass rate, decline correctness, cost-band overlap, cohort-size match
- Phase 1 thresholds: 95% groundedness, 93% decline correctness — passable but loose; tighten in Phase 1.5+ once SME-calibration data accumulates

### 9.3 Live smoke runbook (manual, against deployed env)

- Author at `projects/<project>/notes/phase-N-live-smoke-runbook.md`
- Stepwise instructions for SME or operator to validate end-to-end against real Spaarke Dev / customer env
- Includes cost ceiling per run (~$0.10-$0.50 per fixture)
- Not in CI — manual execution during deploy verification

---

## 10. Troubleshooting common issues

| Symptom | Likely cause | Fix |
|---|---|---|
| `/api/insights/ask` returns 400 "'question' must be either a valid playbook Guid id OR a canonical name registered in 'Insights:Playbooks:Map' configuration" | Friendly name not in App Service config map for this env | Add `Insights__Playbooks__Map__<name>` setting; restart App Service |
| `/api/insights/ask` returns 200 with `decline.reason="no-artifact-produced"` and the explanation mentions "no InsightArtifact and no DeclineResponse" | Engine ran but no terminal node fired (LiveFactNode dispatch failed, OR playbook misconfigured) | Check App Insights for `"Node X in batch 1 failed - stopping playbook execution"`. Phase 1.5 Wave B fixes the dispatch case by wiring sprk_analysisaction rows. |
| Engine logs `"Built execution graph for playbook X: 0 active nodes"` | Playbook nodes have `sprk_isactive = false` (Dataverse column default) | Patch existing nodes via `mcp__dataverse__update_record` to set true; re-run `Deploy-Playbook.ps1` for new playbooks (script now sets this explicitly per the post-2026-05-30 fix) |
| Ingest job never runs after upload | `AiProcessingOptions.InsightsIngest = true` not set on upload | Phase 1.5 needs producer-side surface; today no upload triggers Insights ingest |
| `DataverseObservationMirror skipped: InsightsObservationActionId is unset` | Per-environment config GUID not configured | Follow §8.2 prerequisite checklist |
| Insights tests pass locally but live smoke shows different behavior | Tests mock `IInsightsAi`; real DI graph never exercised | Phase 1.5: add a DI-resolution test that resolves `IInsightsAi` from real container without mocks |

---

## 11. Reference: file locations

| What | Where |
|---|---|
| 4-tier types | `src/server/api/Sprk.Bff.Api/Models/Insights/InsightArtifact.cs` |
| Facade interface | `src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/IInsightsAi.cs` |
| Orchestrator (Zone A impl of facade) | `src/server/api/Sprk.Bff.Api/Services/Ai/Insights/InsightsOrchestrator.cs` |
| Insights node executors (6) | `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/{LiveFact,IndexRetrieve,EvidenceSufficiency,DeclineToFind,ReturnInsightArtifact,GroundingVerify}Node.cs` |
| Layer 1/2 prompts (Phase 1 file-based) | `src/server/api/Sprk.Bff.Api/Services/Ai/Insights/Prompts/*.txt` |
| Predict-matter-cost playbook spec | `src/server/api/Sprk.Bff.Api/Services/Ai/Insights/Playbooks/predict-matter-cost.playbook.json` |
| Universal-ingest orchestrator (code; Phase 1.5 refactors to JPS) | `src/server/api/Sprk.Bff.Api/Services/Ai/Insights/Ingest/IngestOrchestrator.cs` |
| Dataverse live-fact resolver | `src/server/api/Sprk.Bff.Api/Services/Insights/LiveFacts/DataverseLiveFactResolver.cs` |
| Observation mirror | `src/server/api/Sprk.Bff.Api/Services/Insights/Observations/DataverseObservationMirror.cs` |
| Precedent projection sync | `src/server/api/Sprk.Bff.Api/Services/Insights/Precedents/PrecedentProjectionSync.cs` |
| HTTP endpoints | `src/server/api/Sprk.Bff.Api/Api/Insights/{InsightEndpoints,PrecedentAdminEndpoints}.cs` |
| Ingest job handler | `src/server/api/Sprk.Bff.Api/Services/Jobs/Insights/InsightsIngestJobHandler.cs` |
| Deploy scripts | `scripts/Deploy-BffApi.ps1`, `scripts/Deploy-Playbook.ps1`, `scripts/Deploy-ObservationReviewSurface.ps1` |
| Phase 1 acceptance verification doc | `projects/ai-spaarke-insights-engine-r1/notes/phase-1-live-smoke-runbook.md` |
| Phase 1.5 project plan | `projects/ai-spaarke-insights-engine-r2/design.md` |

---

## 12. Glossary

- **JPS** (JSON Prompt Schema) — Spaarke's canonical AI workflow architecture; Dataverse-stored playbook definitions + node executors + engine
- **Zone A / Zone B** — architectural boundary; Zone A = `Services/Ai/`; Zone B = everything else. Only `Services/Ai/PublicContracts/` can be imported from Zone B
- **4-tier taxonomy** — Fact / Observation / Precedent / Inference. The data model spine.
- **D-NN** — Numbered architectural decision (see `projects/.../decisions.md`)
- **D-P-NN** — Numbered Phase 1 deliverable (see `projects/.../SPEC.md` §3.1)
- **Layer 1** — Document classification (cheap LLM call; gates Layer 2)
- **Layer 2** — Outcome extraction (expensive LLM call; only runs on classified outcome-bearing documents)
- **Honesty contract** — D-04 + D-49; system returns structured `DeclineResponse` rather than hallucinating when evidence is insufficient
- **GroundingVerifier** — Mechanical (zero-LLM) substring + sliding-window citation check; D-47
- **EvidenceSufficiencyNode** — Playbook node that evaluates whether retrieved evidence meets the playbook's stated minimum (e.g., `comparableMatters: {min: 12}`)
- **Hybrid pattern** — Phase 1.5+ consumption model: pre-authored JPS playbooks for high-value questions + generic RAG for long-tail; intent classifier routes between
- **Multi-entity subjects** — Phase 1.5+ extension: questions can target Matter / Project / Invoice / future entities (Phase 1 hard-coded to Matter)
