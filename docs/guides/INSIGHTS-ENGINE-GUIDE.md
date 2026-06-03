# Insights Engine Guide

> **Audience**: developers + technical SMEs extending the Spaarke Insights Engine
> **Last Updated**: 2026-06-02 (Phase 1.5 r2 framing — Wave A2 task 011)
> **Companion documents**:
> - [`docs/architecture/INSIGHTS-ENGINE-ARCHITECTURE.md`](../architecture/INSIGHTS-ENGINE-ARCHITECTURE.md) §0a — Phase 1 completion + Phase 1.5 framing
> - [`projects/ai-spaarke-insights-engine-r2/design.md`](../../projects/ai-spaarke-insights-engine-r2/design.md) — Phase 1.5 design (corrections + 9 architectural decisions)
> - [`projects/ai-spaarke-insights-engine-r2/spec.md`](../../projects/ai-spaarke-insights-engine-r2/spec.md) — Phase 1.5 implementation specification
> - [`projects/ai-spaarke-insights-engine-r2/plan.md`](../../projects/ai-spaarke-insights-engine-r2/plan.md) — Phase 1.5 wave structure + critical path
> - [`projects/ai-spaarke-insights-engine-r1/SPEC.md`](../../projects/ai-spaarke-insights-engine-r1/SPEC.md) — Phase 1 deliverable contract

This guide is the practical companion to the architecture doc. It answers "how do I add this?" and "when should I use this vs. that?" questions.

## Phase 1.5 terminology (load-bearing)

Earlier Phase 1 drafts conflated JPS-the-schema with the code that runs it. Phase 1.5 locks the meaning:

- **JPS** (JSON Prompt Schema) — schema/data format for analysis actions and playbooks. **Data, not code.** Lives in Dataverse on `sprk_analysisaction.sprk_systemprompt` and `sprk_playbook` rows.
- **`PlaybookExecutionEngine`** — the code component in `Sprk.Bff.Api` that executes JPS-defined work. (Earlier drafts loosely called this "the JPS engine.")
- **`INodeExecutor`** — code-side handler for a specific analysis-action TYPE.
- **`sprk_analysisaction`** — **IS** the prompt-bearing primitive. Carries a JPS-formatted JSON system prompt (`$schema`, `instruction { role, task, constraints, context }`, `input`, `parameters`) in `sprk_systemprompt`. r1 already uses this for non-Insights actions (e.g., "Classify Document"). Phase 1.5 retires `.txt` files by populating this field on Insights action rows. **No new `sprk_prompt` entity.**
- **`sprk_playbook` + `sprk_configjson`** — JPS playbook definition with per-playbook config blob (cost cap, thresholds, inline prompt templates owned by exactly one playbook).

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

> **🚧 Placeholder — final decision tree owned by [task 043 (Wave E4)](../../projects/ai-spaarke-insights-engine-r2/tasks/043-playbook-vs-rag-decision-tree.poml).** The table below is the working heuristic until E4 lands.

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

In practice: invest playbook-authoring effort in the top ~5–30 questions; everything else uses the generic RAG path. The same `spaarke-insights-index` substrate feeds both. The Phase 1.5 **intent classifier** (Wave E2, task 041) routes between paths automatically when called from Spaarke Assistant; callers can override via `forceMode: "playbook" | "rag"`.

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

> **Source of truth — `sprk_practicearea_ref` table** (per spec.md PA-1). Practice areas are NEVER hardcoded in code or docs. The table IS the catalog. Existing Spaarke Dev rows visible: APPL (Appellate), BNKF (Banking & Finance), CTRNS (Commercial Transactions), IPPAT (Intellectual Property Patents), IPTM (Intellectual Property Trademarks), MA (Mergers & Acquisitions). Query the table at task time to see the current full set.

Phase 1 only handles litigation-shaped documents implicitly; "litigation" framing is **retired** in Phase 1.5 — the per-practice-area gate logic replaces the binary `outcomeBearing` flag (per D-P15-09).

### 4.1 To onboard a new practice area (e.g., Real Estate)

1. **Reference data** — confirm or insert a row in `sprk_practicearea_ref` (e.g., `code=RE`, `name=Real Estate`). Use the Dataverse MCP `mcp__dataverse__read_query` to confirm; `mcp__dataverse__create_record` to add if missing.
2. **Document-type taxonomy** — define real-estate document types (lease, deed, financing, title insurance, closing statement, etc.) in `sprk_documenttype_ref` (Phase 1.5 Wave D1 entity, [task 030](../../projects/ai-spaarke-insights-engine-r2/tasks/030-document-type-and-matrix-schema.poml)).
3. **N:N matrix** — register which document types are valid for real estate via `sprk_practicearea_documenttype` (Wave D1 N:N entity).
4. **Layer 1 classification prompt** (per-practice-area) — create a `sprk_analysisaction` row with `sprk_actioncode = INSIGHTS.LAYER1_CLASSIFY.RE` (suffix per A4 variant pattern, [task 013](../../projects/ai-spaarke-insights-engine-r2/tasks/013-prompt-variant-versioning-design.poml)) and a JPS-formatted `sprk_systemprompt` JSON document with `instruction { role, task, constraints, context }`, `input.document`, `parameters.categories`. **NOT a `.txt` file** — Phase 1.5 Wave C2 ([task 021](../../projects/ai-spaarke-insights-engine-r2/tasks/021-prompts-to-jps-storage.poml)) retired Layer 1 `.txt` storage.
5. **Layer 2 extraction prompts** (per practice-area + document-type) — create one `sprk_analysisaction` per `(area, doc-type)` pair (e.g., `INSIGHTS.LAYER2_EXTRACT.RE.LEASE`, `INSIGHTS.LAYER2_EXTRACT.RE.DEED`) carrying the appropriate field schemas in `sprk_systemprompt`.
6. **Universal-ingest playbook routing** — the canonical `universal-ingest@v1` JPS playbook (Wave C1, [task 020](../../projects/ai-spaarke-insights-engine-r2/tasks/020-universal-ingest-jps-playbook.poml)) routes classification + extraction based on `sprk_matter.sprk_practicearea` (or per-entity equivalent). No new playbook needed — flexibility via parameters, not multiplication.
7. **Test fixtures** — add LLM-generated synthetic fixtures (Wave D7, [task 036](../../projects/ai-spaarke-insights-engine-r2/tasks/036-synthetic-test-fixtures.poml)) for real-estate × (lease, deed, …) under `tests/Insights/fixtures/real-estate/`.

### 4.2 Effort sizing

Count 2–3 days per practice area for prompts + testing. The supporting Phase 1.5 infrastructure (taxonomy entities, per-practice-area prompt routing, parameterized universal-ingest) lands once in Waves C+D and amortizes across all subsequent practice areas. Phase 1.5 ships initial coverage for **3 practice areas** (selected in [task 012](../../projects/ai-spaarke-insights-engine-r2/tasks/012-2d-taxonomy-design.poml) based on SME readiness + document variety); remaining areas land per customer cadence.

---

## 5. Adding a new subject entity type (Phase 1.5 Wave D5/D6)

Phase 1 hard-codes `subject: "matter:<guid>"`. Phase 1.5 generalizes to `matter:`, `project:`, `invoice:`, and future entities (Wave D5 [task 034](../../projects/ai-spaarke-insights-engine-r2/tasks/034-multi-entity-resolvers.poml) + Wave D6 [task 035](../../projects/ai-spaarke-insights-engine-r2/tasks/035-index-scope-shape-migration.poml); design [task 015](../../projects/ai-spaarke-insights-engine-r2/tasks/015-multi-entity-subject-design.poml)).

### 5.1 To add a new subject entity (e.g., `invoice:`)

1. **Subject scheme parser** — extend `InsightAskRequest` subject-scheme parsing to accept the new scheme (e.g., `invoice:<guid>`).
2. **Live-fact resolver** — add a per-entity `ILiveFactResolver` implementation (e.g., `DataverseInvoiceLiveFactResolver`) in `src/server/api/Sprk.Bff.Api/Services/Ai/Insights/`. Register keyed by entity type — default pattern is `IDictionary<string, ILiveFactResolver>` per [task 015](../../projects/ai-spaarke-insights-engine-r2/tasks/015-multi-entity-subject-design.poml) Q-A6-1 option (a). **DI minimalism ceiling**: if the additions exceed ADR-010's ≤15 non-framework registration target, consolidate via a registry/factory.
3. **`LiveFactNode` config** — extend to consult the registered resolver based on subject scheme parsed from the playbook input.
4. **Index scope shape** — extend `spaarke-insights-index` scope fields to carry `entityType` + `entityId` alongside the existing `matterId` (backward compat per NFR-08; Phase 1 Observations remain queryable). Migration plan: Wave D6 ([task 035](../../projects/ai-spaarke-insights-engine-r2/tasks/035-index-scope-shape-migration.poml)) — new fields nullable; coordinate with infra team for index re-create.
5. **Universal-ingest routing** — the JPS universal-ingest playbook (Wave C1) routes ingest based on document parent entity (a document on a project → project-context Observations; on an invoice → invoice-context Observations) via parameters, not new playbooks.
6. **Per-subject playbooks** — author Insights playbooks targeting the new entity type (e.g., `predict_project_completion_v1` taking `subject: "project:<guid>"`). Each new playbook is a new `sprk_playbook` row + supporting `sprk_analysisaction` rows.

### 5.2 What's unchanged

The 4-tier `InsightArtifact` taxonomy (Fact / Observation / Precedent / Inference), the `PlaybookExecutionEngine`, the `IInsightsAi` facade, and the `spaarke-insights-index` substrate are all unchanged. What changes is the input contract (subject scheme), per-entity adapters (resolvers), and index scope-shape fields. The `IInsightGraph` stub remains; Cosmos NoSQL is explicitly out of Phase 1.5 scope (re-deferred to Phase 2 per spec.md Out of Scope).

---

## 6. Authoring + iterating prompts (Phase 1.5)

### 6.1 Canonical storage: `sprk_analysisaction.sprk_systemprompt`

**Phase 1.5 (Wave C2, [task 021](../../projects/ai-spaarke-insights-engine-r2/tasks/021-prompts-to-jps-storage.poml))** retires Phase 1's `.txt` files in `Services/Ai/Insights/Prompts/`. All prompt content lives in the **existing** `sprk_analysisaction.sprk_systemprompt` Dataverse field as a JPS-formatted JSON document:

```jsonc
{
  "$schema": "https://spaarke/jps/v1",
  "$version": "1",
  "instruction": {
    "role": "You are a Spaarke Insights extraction analyst …",
    "task": "Extract leniency-clause indicators from this lease …",
    "constraints": [
      "Quote verbatim from the source document",
      "Return null if the clause is not present"
    ],
    "context": "{ practiceArea, documentType, … }"
  },
  "input":      { "document": "…" },
  "parameters": { "categories": ["…"], "practiceAreaContext": "…" }
}
```

**Why `sprk_analysisaction` and not a new `sprk_prompt` entity** (spec.md PR-1):
- It's the **existing** JPS dispatch + prompt primitive — r1 already uses it for non-Insights actions (e.g., "Classify Document" carries its full JPS prompt in `sprk_systemprompt`)
- Adding a new entity would duplicate the JPS schema's prompt-bearing slot
- Phase 1.5 leverages r1 infrastructure without a schema change

### 6.2 Edit a prompt without a code deploy (operator/SME procedure)

1. **Locate the row** — query Dataverse for the relevant `sprk_analysisaction`:
   ```
   mcp__dataverse__read_query
   FROM sprk_analysisactions
   WHERE sprk_actioncode = 'INSIGHTS.LAYER2_EXTRACT.RE.LEASE'
   ```
2. **Branch the variant (don't edit in place)** — per A4 variant + versioning design ([task 013](../../projects/ai-spaarke-insights-engine-r2/tasks/013-prompt-variant-versioning-design.poml)), either:
   - Create a new variant row with a versioned action code (e.g., `…RE.LEASE.V2`), OR
   - Update the version field on the row (A4 decides the exact mechanism).
3. **Update `sprk_systemprompt`** — edit the JSON `instruction.task` / `instruction.constraints` / `parameters` content via Dataverse MCP `mcp__dataverse__update_record`.
4. **Calibration run** — re-run the eval harness (`PredictMatterCostEvalHarnessTests` or `<question>EvalHarnessTests`) against the new variant; compare groundedness pass rate, decline correctness, cost-band overlap, cohort-size match to baseline.
5. **SME review** — surface a sample of new outputs in the Observation review queue (`sprk_analysis` rows with `sprk_disposition=PendingReview`) for SME validation.
6. **Cutover** — update the playbook to reference the new variant; old variant stays available for A/B comparison + rollback.
7. **No App Service restart required** — next invocation of the dependent playbook reads the updated row directly (Dataverse-cached via existing JPS engine cache; cache TTL controls when the new content is picked up).

### 6.3 Per-tenant + per-practice-area variation

Phase 1.5 supports prompt variation by:

- **Per-practice-area variants** — variant action rows resolved at invocation by action-code suffix (e.g., `INSIGHTS.LAYER1_CLASSIFY.CTRNS`, `…IPPAT`) OR parametric JPS injection (single action row + runtime `parameters.practiceAreaContext`). Wave A4 ([task 013](../../projects/ai-spaarke-insights-engine-r2/tasks/013-prompt-variant-versioning-design.poml)) selects the pattern.
- **Per-tenant override** — tenant-scoped variant rows with fallthrough to default, OR override mapping table, OR tenant blocks within the JPS schema. Wave A4 finalizes; this guide will be refreshed to document the final shape once A4 lands.

### 6.4 Per-playbook inline prompts

For prompts that exist in exactly one playbook (e.g., the synthesis template owned by `predict-matter-cost@v1`), inline storage in `sprk_playbook.sprk_configjson` is still allowed. Reserve `sprk_analysisaction.sprk_systemprompt` for prompts that are dispatched by reference across one or more playbooks.

---

## 7. Querying the Insights index directly — `POST /api/insights/search` (Phase 1.5 NEW)

Phase 1.5 Wave E1 ([task 040](../../projects/ai-spaarke-insights-engine-r2/tasks/040-insights-search-rag-endpoint.poml)) ships the generic RAG retrieval endpoint. Per the 2026-06-02 re-scope: the endpoint **wraps the existing `IRagService`** (in `src/server/api/Sprk.Bff.Api/Services/Ai/IRagService.cs`) — no parallel implementation. If Insights-specific subject filtering needs extension, extend the existing service.

### 7.1 Request shape

```jsonc
{
  "query": "natural language question or keywords",
  "subject": "matter:<guid>",           // optional — narrows scope
  "filters": {
    "artifactType": "observation",      // or "precedent"
    "predicate": "settlementAmount",    // optional
    "scope.practiceArea": "IPPAT",      // sourced from sprk_practicearea_ref code
    "scope.entityType": "matter"        // optional — Phase 1.5 D6 generalized scope shape
  },
  "topK": 10
}
```

### 7.2 Response shape

Top-N ranked Observations/Precedents with full envelopes + an LLM-synthesized summary citing the retrieved evidence (each citation carries `observationId` + `predicate` + `confidence` per FR-04 acceptance).

### 7.3 Auth + governance

- **Auth filter**: same per-route endpoint filter as `/api/insights/ask` (per ADR-008 + ADR-028) — registered via the function-based contract; named API key scheme + audit middleware applied.
- **Kill-switch**: if `IRagService` is feature-disabled per ADR-032, the endpoint returns 503 ProblemDetails via `FeatureDisabledException` → `FeatureDisabledResults.AsFeatureDisabled503()`.
- **BFF placement**: endpoint lives in `Sprk.Bff.Api/Endpoints/` following the existing `/ask` pattern (per spec.md BFF Placement Review).

### 7.4 Until E1 ships

Ad-hoc index queries can use the Azure AI Search REST API directly with the per-environment admin key (operator runbook).

---

## 7A. Intent classifier (Phase 1.5 Wave E2)

[Task 041](../../projects/ai-spaarke-insights-engine-r2/tasks/041-intent-classifier.poml) ships an **LLM-based** intent classifier (gpt-4o-mini per spec.md assumption) that routes Insights-shaped natural-language questions to the correct path:

```
caller question ──▶ IntentClassifier ──▶ { path: "playbook" | "rag",
                                           playbookId?: string,
                                           confidence: number }
                            │
                            ▼ (if confidence < threshold)
                    fall through to generic RAG
```

- **Caller override**: any caller can bypass classification with `forceMode: "playbook" | "rag"` (FR-05 acceptance).
- **Phase 1.5 = LLM-based only**: embedding-based routing is Phase 2 (per spec.md Out of Scope + assumptions).
- **DI placement**: `src/server/api/Sprk.Bff.Api/Services/Ai/Insights/` (reuses `IOpenAiClient` and Phase 1 facade plumbing per spec.md BFF Placement Review).
- **Mitigation for mis-routing**: confidence threshold is tunable per environment; caller `forceMode` is the human-in-the-loop escape hatch.

---

## 7B. Spaarke Assistant integration (Phase 1.5 Wave E3)

[Task 042](../../projects/ai-spaarke-insights-engine-r2/tasks/042-spaarke-assistant-integration.poml) integrates Insights as a callable tool in the existing Spaarke Assistant chat surface.

### Sub-task A: Tool-call contract authoring (contract-first per AC-1)

**No pre-existing contract** — Wave E3 owns authoring the schema. Phase 1.5 scope is **read-only tool-call semantics**; bidirectional integration is deferred to Phase 2.

When the Assistant detects an Insights-shaped question, it calls into the BFF facade. The intent classifier routes to playbook OR RAG; the caller (Assistant) can pass `forceMode` to override.

### Coordination

Cross-team work with the Spaarke Assistant team is required. Coordinate early per spec.md Dependencies — E3 is the longest Wave E task (~1 week including coordination).

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
| `/api/insights/search` returns 503 ProblemDetails with `featureDisabled: true` | `IRagService` is `NullRagService` (kill-switch active per ADR-032) | Enable the RAG feature flag in App Service config; restart |
| Intent classifier confidence is consistently low for in-domain questions | Threshold too high OR classifier prompt drift | Tune `Insights:IntentClassifier:ConfidenceThreshold`; consider caller `forceMode` override; re-calibrate against eval harness |

---

## 11. Reference: file locations

| What | Where |
|---|---|
| 4-tier types | `src/server/api/Sprk.Bff.Api/Models/Insights/InsightArtifact.cs` |
| Facade interface | `src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/IInsightsAi.cs` |
| Orchestrator (Zone A impl of facade) | `src/server/api/Sprk.Bff.Api/Services/Ai/Insights/InsightsOrchestrator.cs` |
| Insights node executors (6) | `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/{LiveFact,IndexRetrieve,EvidenceSufficiency,DeclineToFind,ReturnInsightArtifact,GroundingVerify}Node.cs` |
| Layer 1/2 prompts (Phase 1.5 → Dataverse) | `sprk_analysisaction.sprk_systemprompt` rows (action codes `INSIGHTS.LAYER1_CLASSIFY.*`, `INSIGHTS.LAYER2_EXTRACT.*.*`). Phase 1 `Services/Ai/Insights/Prompts/*.txt` files are **retired** by Wave C2 ([task 021](../../projects/ai-spaarke-insights-engine-r2/tasks/021-prompts-to-jps-storage.poml)). |
| Universal-ingest JPS playbook (Phase 1.5) | Dataverse `sprk_playbook` row `universal-ingest@v1` (Wave C1 [task 020](../../projects/ai-spaarke-insights-engine-r2/tasks/020-universal-ingest-jps-playbook.poml)) — replaces `IngestOrchestrator.cs` |
| RAG service (existing, wrapped by `/search`) | `src/server/api/Sprk.Bff.Api/Services/Ai/IRagService.cs` + `RagService.cs` + `NullRagService.cs` (ADR-032 kill-switch) |
| `FeatureDisabledException` plumbing | `src/server/api/Sprk.Bff.Api/Configuration/FeatureDisabledException.cs` + `FeatureDisabledResults.AsFeatureDisabled503()` |
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

- **JPS** (JSON Prompt Schema) — schema/data format for analysis actions and playbooks. **Data, not code.** Lives on `sprk_analysisaction.sprk_systemprompt` and `sprk_playbook` rows.
- **`PlaybookExecutionEngine`** — the code component in `Sprk.Bff.Api` that executes JPS-defined work. (Earlier Phase 1 docs loosely called this "the JPS engine.")
- **`INodeExecutor`** — code-side handler for a specific analysis-action TYPE. Phase 1.5 contributes new ones (LiveFactNode, IndexRetrieveNode, EvidenceSufficiencyNode, GroundingVerifyNode, DeclineToFindNode, ReturnInsightArtifactNode).
- **`sprk_analysisaction`** — existing JPS dispatch + prompt row. **IS the prompt-bearing primitive** (`sprk_systemprompt` carries JPS-formatted JSON). Phase 1.5 retires `.txt` prompts by populating this field. **No new `sprk_prompt` entity** per PR-1.
- **Zone A / Zone B** — architectural boundary; Zone A = `Services/Ai/`; Zone B = everything else. Only `Services/Ai/PublicContracts/` can be imported from Zone B.
- **4-tier taxonomy** — Fact / Observation / Precedent / Inference. The data model spine.
- **2D taxonomy** — Phase 1.5 classification model: **practice-area × document-type**. Practice areas sourced from `sprk_practicearea_ref` (Phase 1.5 source of truth); document types in `sprk_documenttype_ref` (Wave D1).
- **D-NN** — Numbered architectural decision (see `projects/.../decisions.md` or `projects/.../design.md`)
- **D-P-NN** — Numbered Phase 1 deliverable (see r1 `SPEC.md` §3.1)
- **D-P15-NN** — Numbered Phase 1.5 correction (see Phase 1.5 design.md)
- **Layer 1** — Document classification (cheap LLM call; gates Layer 2). Phase 1.5: per-practice-area variant.
- **Layer 2** — Outcome extraction (expensive LLM call). Phase 1.5: per-(practice-area, document-type) schema.
- **Honesty contract** — D-04 + D-49; system returns structured `DeclineResponse` rather than hallucinating when evidence is insufficient.
- **GroundingVerifier** — Mechanical (zero-LLM) substring + sliding-window citation check; D-47.
- **EvidenceSufficiencyNode** — Playbook node that evaluates whether retrieved evidence meets the playbook's stated minimum (e.g., `comparableMatters: {min: 12}`).
- **Hybrid pattern** — Phase 1.5 consumption model: pre-authored JPS playbooks for high-value questions + generic RAG (`/api/insights/search`) for long-tail; **intent classifier** (LLM-based, Wave E2) routes between. Caller `forceMode` override is the escape hatch.
- **Multi-entity subjects** — Phase 1.5 extension: questions can target Matter / Project / Invoice / future entities (Phase 1 hard-coded to Matter). Per-entity `ILiveFactResolver` registered keyed by scheme.
- **Universal-ingest@v1** — Phase 1.5 Wave C1 canonical JPS ingest playbook. Replaces Phase 1's `IngestOrchestrator.cs`. Parameterized (per-tenant, per-practice-area, cost-cap override) — flexibility via config, NOT via multiplication.
- **`IRagService`** — existing RAG retrieval canonical entry point (per 2026-06-01 master refactor). Phase 1.5 `/api/insights/search` endpoint (Wave E1) wraps this; ADR-032 P3 kill-switch via `NullRagService` → 503 ProblemDetails.
- **Wave B / A / C / D / E** — Phase 1.5 execution sequence (B first per WB-1 owner direction): Unblock synthesis → Foundations (design docs) → JPS compliance refactor → 2D taxonomy + multi-entity → Hybrid consumption + Assistant.
