# SPEC — Spaarke Insights Engine, Phase 1 (Foundation)

> **Status**: Pipeline-ready
> **Last Updated**: 2026-05-28 (integrates [SPEC-phase-1-minimum.md](SPEC-phase-1-minimum.md) as the canonical Phase 1 scope, with three preservations from the prior SPEC — see §1 below. Supersedes the 2026-05-27 [SPEC-phase-1-minimum.md](SPEC-phase-1-minimum.md), which is preserved as historical rationale.)
> **Anchor docs**: [decisions.md](decisions.md) (read first — D-52, D-54 stand; D-53/D-55/D-56/D-57/D-58 revised or superseded; new D-59–D-63) · [design.md](design.md) (full design; refinement callouts at §0 + affected sections) · [SPEC-phase-1-minimum.md](SPEC-phase-1-minimum.md) (rationale narrative for the 2026-05-28 direction)
> **Scope**: Phase 1 of the Insights Engine project. Ships real Observation production from real documents end-to-end via universal layered ingest, plus one synthesis question (`predict-matter-cost`) over real Observations + SME-authored Precedents.

---

## 1. Overview

The Spaarke Insights Engine is **Spaarke's context production service** — the system that produces, persists, and serves structured contextual claims about the organization's work, with provenance, confidence where applicable, and evidence-sufficiency rules where applicable. Context production includes (a) **deterministic claims** computed from source data, (b) **probabilistic claims** extracted from document content via LLM, and (c) **synthesized claims** combining the above. AI is one technique among several; the Engine's job is to orchestrate all of them uniformly through one envelope (`InsightArtifact`), one facade (`IInsightsAi`), and one execution path (Insights-mode playbooks on the existing `PlaybookExecutionEngine`).

Phase 1 ships **real Observation production from real documents end-to-end**, not infrastructure with mock data. The acceptance bar: a single API call returns either a structurally-honest grounded Inference or a structured decline — composed from Observations that were extracted from actual SPE documents by Phase 1's ingest pipeline.

**Integration history**: this SPEC integrates [SPEC-phase-1-minimum.md](SPEC-phase-1-minimum.md) (2026-05-28) which itself supersedes specific sections of [SPEC-phase-1-minimum.md](SPEC-phase-1-minimum.md) (2026-05-27). The three preservations from the prior SPEC carried into this revision:
1. **§3.5 facade boundary placement table** extended to the D-P deliverables — binding
2. **`IInsightGraph` interface design** (D-P17) ships in Phase 1 even though Cosmos implementation (the prior D-A6) defers to Phase 1.5 — preserves the swap path at trivial cost
3. **Cosmos NoSQL graph implementation** is the **first Phase 1.5 deliverable** so Phase 1 work doesn't shape itself in ways that complicate adding graph later

## 2. Goals (Phase 1)

1. Provision the **one new derived AI Search index** (`insights-index` per D-P2 — holds Observations + Precedents differentiated by an `artifactType` discriminator) plus shell infrastructure (Function App per ADR-001, Service Bus subscriptions, Key Vault refs, App Insights, single-tenant Bicep parameter file pattern per D-52).
2. Implement the `InsightArtifact` envelope C# types (four-tier: Fact / Observation / Precedent / Inference) per design.md §2.2 + decisions.md D-03/D-46.
3. Implement the **universal layered ingest playbook** that runs on every SPE upload: Layer 1 (document classification) + conditional Layer 2 (outcome extraction with verbatim-quote evidence). Cheap layers gate expensive ones per D-59.
4. Implement the three mechanical post-processing gates: `GroundingVerifier` substring/sliding-window check (D-P9), per-field confidence threshold gating (D-P10/D-63), per-field Observation emission with `producedBy.version` propagation (D-62).
5. Ship the **Observation review surface** (Dataverse model-driven view) for sample-based QA per D-P11/D-60 — without it the honesty contract is unverified.
6. Implement `sprk_precedent` Dataverse entity + admin endpoint for manual SME authoring (D-P3) + projection sync to `insights-index` on Confirmed (D-P4) — Phase 1 mode of D-61 (two-mode authoring).
7. Implement `LiveFactResolverService` (direct Dataverse queries — Live Facts on read; no projection writing) consumed via `LiveFactNode` (D-P12 subset).
8. Implement the new Insights-mode node executors (D-P12) and the `predict-matter-cost` synthesis playbook (D-P14) with evidence-sufficiency rule + insufficient-evidence response template.
9. Expose `POST /api/insights/ask` on the BFF through the `IInsightsAi` facade per §3.5 boundary (D-P15).
10. Smoke test + small golden dataset + eval harness baseline (D-P16) over real Observations.

## 3. Scope

### 3.1 In scope — Phase 1 deliverables (D-P1 … D-P17)

| ID | Deliverable | Layer |
|---|---|---|
| **D-P1** | `InsightArtifact` envelope POCOs (Fact / Observation / Precedent / Inference) in `Sprk.Bff.Api/Models/Insights/` per design.md §2.2 | Domain types (Zone B) |
| **D-P2** | `insights-index` schema + provisioning via Bicep — ONE derived index holding Observations and Precedents differentiated by `artifactType` field (per §3.4). Includes `tenantId` field retained per D-52. | Infra |
| **D-P3** | `sprk_precedent` Dataverse entity + relationship tables + admin endpoint `POST /api/insights/admin/precedents` (JWT-authorized; admin role per ADR-008) for **manual SME authoring** — Phase 1 mode of D-61 two-mode authoring. Concretized per [SPEC-phase-1-minimum.md §2](SPEC-phase-1-minimum.md) | Entity + API (Zone B) |
| **D-P4** | Precedent → `insights-index` projection sync — small job fires on Precedent status → Confirmed; reuses `DataverseIndexSyncService` patterns | Substrate (Zone B) |
| **D-P5** | **Layer 1 — document classification** — Insights-mode playbook node + prompt template `classification@v1` per [SPEC-phase-1-minimum.md §3.3](SPEC-phase-1-minimum.md). Realized via existing `AiAnalysisNodeExecutor` (per Q5 audit — no new node needed for prompt-bearing layer). First-step blocker: SME review of document-type taxonomy. | Extraction pipeline (Zone A) |
| **D-P6** | **Layer 2 — outcome extraction** — Insights-mode playbook node + prompt template `outcome-extraction@v1` per [SPEC-phase-1-minimum.md §3.4](SPEC-phase-1-minimum.md). Realized via existing `AiAnalysisNodeExecutor`. First-step blocker: confidence threshold values confirmed by product/SME. | Extraction pipeline (Zone A) |
| **D-P7** | **Universal ingest playbook** orchestrating Layer 1 → conditional Layer 2 → mechanical gates → Observation emission. Single playbook published per D-54 (questions-as-playbooks); runs on every SPE upload via D-P8. | Extraction pipeline (Zone A) |
| **D-P8** | **New consumer on SPE-upload events** that triggers the ingest playbook. New BackgroundService or Function per ADR-001. Reads document content from `spaarke-files-index` (already chunked by the existing pipeline) — does NOT re-fetch from SPE. First-step blocker: confirm SPE-upload event source + dispatch shape. | Infra (Zone B per dispatch boundary; calls `IInsightsAi` to invoke playbook) |
| **D-P9** | **`GroundingVerifier` mechanical citation check** in `Sprk.Bff.Api/Services/Ai/CitationVerification/` — substring + sliding-window match against `spaarke-files-index` chunks; 10K-char DoS cap; zero-LLM. Realized as `GroundingVerifyNode` per Q5 audit. Shared platform primitive — also consumed by Action Engine (LAVERN ADR 10.6). | Platform primitive (Zone A) |
| **D-P10** | **Confidence threshold gating + per-field Observation emission** as a post-processing primitive invoked after Layer 2. Each surviving field becomes its own Observation; `producedBy = "outcome-extraction@v1"`. | Extraction pipeline (Zone A) |
| **D-P11** | **Observation review surface** — Dataverse model-driven view + disposition workflow (Correct / Incorrect / Unclear) for sample-based QA per [SPEC-phase-1-minimum.md §5](SPEC-phase-1-minimum.md). Mirror writes one `sprk_analysis` row per Observation (first-step blocker: confirm polymorphic source-type via Dataverse MCP) as a side-effect of the ingest playbook. Phase 1 sampling: ~10% initial 4–6 weeks, then 1–2% ongoing — admin-tunable. | UI / Operations (Zone B) |
| **D-P12** | **New Insights-mode node executors** in `Sprk.Bff.Api/Services/Ai/Nodes/`: `LiveFactNode`, `IndexRetrieveNode`, `EvidenceSufficiencyNode`, `DeclineToFindNode`, `GroundingVerifyNode`, `ReturnInsightArtifactNode`. Per Q5 audit — 6 nodes (not 9 — `GraphTraverseNode` deferred with Cosmos; `SpeContentRetrieveNode` collapses into D-P7 reading from `spaarke-files-index`; `EmitObservationNode` collapses into D-P10 post-processing). | Platform primitive (Zone A) |
| **D-P13** | **Insights playbook execution caching** — Redis layer wrapping `PlaybookExecutionEngine` for Insights-mode playbooks only. Cache key: `(playbookId, subject, parameters, accessibleScopeHash)`. Per ADR-009. Side-quest opportunity per Q5: extract a generic `IDistributedCacheExtensions.GetOrCreateAsync<T>` helper while building this. | Platform (Zone A) |
| **D-P14** | **`predict-matter-cost` synthesis playbook** end-to-end — published as Insights-mode playbook with metadata (tier=inference, evidence rule `comparableMatters.min: 12`, insufficient-evidence response template, subject entity type, parameter schema, cache TTL). Node graph: `LiveFactNode` → `IndexRetrieveNode` (cohort + Precedents from `insights-index`) → `EvidenceSufficiencyNode` → AiAnalysis synthesis → `GroundingVerifyNode` → `ReturnInsightArtifactNode`; decline path → `DeclineToFindNode`. Per [SPEC-phase-1-minimum.md §6](SPEC-phase-1-minimum.md). | Question (Zone A — playbook authoring) |
| **D-P15** | **API endpoint `POST /api/insights/ask`** in `Sprk.Bff.Api/Api/Insights/InsightEndpoints.cs` — accepts `{question, subject, parameters}`, returns `InsightArtifact` or `DeclineResponse`. Endpoint filter for resource auth per ADR-008. Rate limiting per ADR-016. ProblemDetails errors per ADR-019. Calls `IInsightsAi` facade per §3.5 (NOT `PlaybookExecutionEngine` or `IChatClient` directly). | API (Zone B) |
| **D-P16** | **Smoke test + small golden dataset + eval harness baseline** — end-to-end test that ingests a small fixture document corpus (3–5 closing letters), produces real Observations, runs `predict-matter-cost` against them, verifies Inference + DeclineResponse paths both work and `GroundingVerifier` strips one synthetic bad citation. 10–15 golden tuples for `predict-matter-cost` in `tests/Insights/golden/predict-matter-cost.json`; CI baseline run (RAG-triad metrics permissive thresholds for Phase 1; harden Phase 1.5+). | Tests / Evaluation |
| **D-P17** ⭐ | **`IInsightGraph` interface design** in `Sprk.Bff.Api/Services/Insights/Graph/` — typed named traversal patterns (`FindMattersInvolvingPartyAsync`, `FindConnectedEntitiesAsync`, etc.) per design.md §4.2.2. **Interface + DTOs + stub implementation only** (returns empty/throws `NotImplementedException`). Preserved as Phase 1 deliverable per preservation #2 — Cosmos implementation defers to Phase 1.5. **Cost**: ~1–2 hours; benefit: preserves swap path; synthesis playbook authors learn the abstraction without depending on it. | Domain types (Zone B) |

> **Implicit dependencies on existing infrastructure** (reused as-is per Q5 audit, not new deliverables):
> - `AiAnalysisNodeExecutor` (existing — hosts the L1/L2 prompts for D-P5/D-P6)
> - `PlaybookExecutionEngine` (existing — orchestrates D-P7 ingest and D-P14 synthesis)
> - `IAiToolHandler` + `IToolHandlerRegistry` (existing — pattern for the AI handlers behind D-P5/D-P6)
> - `IOpenAiClient` + `EmbeddingCache` (existing — embedding generation for `insights-index`)
> - `IDataverseService` (existing — Live Facts via D-P12 `LiveFactNode`)
> - `spaarke-files-index` (existing — read by D-P7 ingest and D-P9 grounding verification; **NOT modified**)
> - `DataverseIndexSyncService` patterns (template for D-P4 Precedent projection sync)
> - `ReferenceIndexingService` patterns (template for `insights-index` upsert; **small refactor**: parameterize `ReferenceIndexingService` to accept index name + schema mapper so both `spaarke-rag-references` AND `insights-index` use the same code path — eliminates duplication; ~half-day work; not a separate deliverable)

### 3.2 Track B — auth-blocked work (mostly dissolved by D-P architecture)

The original Track B (Dataverse webhook → Service Bus → consumer Function pattern for record-projection sync) is **largely dissolved** by the D-P architecture. Mode A "narrow derived projection" is replaced by Live Facts on read (D-58 superseded); the only writes to `insights-index` are (a) ingest playbook on SPE upload (D-P7/D-P8 — NOT Dataverse webhook), and (b) Precedent projection on `sprk_precedent` Confirmed status (D-P4 — small in-process job). Neither needs the Phase C HMAC/clientState webhook auth work.

**What remains in Track B for Phase 1.5+**:
- If the Phase 1.5 system-proposed Tentative Precedent workflow (nightly cluster job per [SPEC-phase-1-minimum.md §2.2](SPEC-phase-1-minimum.md)) needs Dataverse change-tracking, the original D-B1..D-B6 auth-coupled work returns as Phase 1.5 scope.
- If Phase 2+ adds Dataverse-driven cache invalidation for synthesis results, similar reasoning applies.

Phase 1 has **no remaining auth-blocked work**. DEP-1 / DEP-2 (Phase C task coordination) become Phase 1.5 dependencies, not Phase 1 blockers.

### 3.3 Phase 1.5 deferrals — first up: Cosmos graph

**Phase 1.5 — first deliverable (preservation #3)**: **`CosmosNoSqlInsightGraph` implementation** (the original D-A6) — implements the `IInsightGraph` interface (D-P17) with adjacency-list document model + named traversals (`FindMattersInvolvingPartyAsync`, `FindConnectedEntitiesAsync`). Bicep modules for Cosmos account + database + containers. Enables `GraphTraverseNode` (D-P12 extension), enables matter-party + cross-matter relationship queries needed for `find-comparable-matters` / `counterparty-history` Phase 1.5 questions. **Listed first** to ensure Phase 1 work does not shape itself in ways that complicate adding graph later (preservation #3).

**Other Phase 1.5 deliverables** (after Cosmos lands):
- Phase 1.5 extraction layers — entity extraction, deal-terms extraction, decision extraction, risk extraction
- Configured cluster category entity (admin-managed list of pattern detection rules)
- Nightly cluster summarization job (system-proposed Tentative Precedents per [SPEC-phase-1-minimum.md §2.2](SPEC-phase-1-minimum.md) Phase 1.5+ mode of D-61)
- Precedent review queue UI (the mocked screen from [SPEC-phase-1-minimum.md §2](SPEC-phase-1-minimum.md))
- Precedent lifecycle automation (decay, drift detection, effectiveness scoring) — per DEF-12 thresholds calibrated with real data
- Additional synthesis questions: `predict-matter-duration`, `find-comparable-matters`, `counterparty-history`, `budget-overrun-risk`
- Three-surface presentation (context-pane card + Assistant pane + field-bound icon — D-55 deferred from Phase 1)
- `/api/insights/relevant` endpoint + relevance ruleset
- Dataverse change-tracking sync (if needed for Tentative Precedent automation — Track B remnant)

### 3.4 The single derived index — operational substrate consumption discipline

The Engine spans the existing **operational substrate** (`spaarke-files-index`, `spaarke-records-index`, `spaarke-invoices-index`, `spaarke-rag-references` — consumed as-is, **no schema changes by this project**) and a single new **derived index** (`insights-index` per D-P2).

**Operational substrate consumption in Phase 1**:
- `spaarke-files-index` — read by D-P7 ingest playbook (Layer 2 reads document content for extraction) and by D-P9 `GroundingVerifier` (verifies extracted quotes against source chunks)
- `spaarke-records-index` / `spaarke-invoices-index` / `spaarke-rag-references` — **not directly queried** by Phase 1 synthesis; `predict-matter-cost` queries `insights-index` for cohort Observations + Precedents and uses `LiveFactNode` for current-matter Facts via Dataverse direct
- The dual-substrate "composition discipline" from prior SPEC versions (compose across operational + derived in a single retrieval) is **not needed in Phase 1** — the dataflow is "operational → ingest → insights-index → synthesis", with no in-synthesis composition across both legs

**Derived index — `insights-index` schema** (per [SPEC-phase-1-minimum.md §4.2](SPEC-phase-1-minimum.md)):
```
artifactType: "observation" | "precedent"   (discriminator)
subject: "matter:M-1234" | "document:abc" | "party:acme"
predicate: <string>
value: <JSON>
confidence: 0.0–1.0   (Observations only; Precedents SME-confirmed)
evidence: <JSON array of refs>
producedBy: <string>   ("outcome-extraction@v1" | "manual-sme-author")
asOf: <ISO datetime>
content: <string>          (searchable text)
contentVector: <embedding> (similarity retrieval)
status: <lifecycle state>
tenantId: <string>   (retained per D-52)
```

**`tenantId` asymmetry**: `spaarke-records-index` lacks `tenantId` (acknowledged gap, Phase 3 cleanup outside this project per D-53 revised). The new `insights-index` has `tenantId` as first-class. Under D-52 single-tenant Phase 1, this asymmetry is safe at the privilege layer (physical isolation handles tenant separation); it's preserved as belt-and-suspenders for future federation.

#### 3.4.1 Worked example — Observation row (D-P6 outcome extraction output)

Scenario: closing letter uploaded to SPE for matter M-2024-0341 (IP licensing dispute against BigFirm LLP). D-P7 universal ingest playbook fires: D-P5 Layer 1 classifies the document as `closing_letter` (confidence 0.92); D-P6 Layer 2 extracts outcome fields with verbatim quotes; D-P9 `GroundingVerifier` verifies each quote against `spaarke-files-index` chunks; D-P10 confidence gating passes (`outcomeCategory: 0.91 ≥ 0.75`, `settlementAmount: 0.94 ≥ 0.85`, `outcomeDate: 0.97 ≥ 0.85`, `matterDurationDays: 0.88 ≥ 0.75`). Each surviving field becomes its own Observation row.

Example `outcomeCategory` Observation row in `insights-index`:

```jsonc
{
  "id": "obs:M-2024-0341:outcomeCategory:doc-abc123",
  "tenantId": "tenant-acme",
  "artifactType": "observation",
  "subject": "matter:M-2024-0341",
  "predicate": "outcomeCategory",
  "value": { "raw": "favorable_to_client", "displayHint": "enum" },
  "confidence": 0.91,
  "evidence": [
    {
      "refType": "document",
      "ref": "spe://drive/acme-matters/item/closing-letter-M-2024-0341.docx",
      "quote": "The matter concluded with terms favorable to our client, securing all material rights sought in the original complaint."
    },
    { "refType": "playbook-run", "ref": "playbook://outcome-extraction@v1/run-2026-04-12T08:30:00Z" }
  ],
  "asOf": "2026-04-12T08:30:00Z",
  "producedBy": "outcome-extraction@v1",
  "content": "Matter M-2024-0341 outcome: favorable to client. Quote: \"The matter concluded with terms favorable to our client, securing all material rights sought in the original complaint.\"",
  "contentVector": [0.0234, 0.1872, ...],   // 3072-dim embedding of content
  "status": "produced"
}
```

A parallel `settlementAmount` Observation row for the same matter+document carries `value: { raw: 310000, displayHint: "currency-usd" }`, its own evidence quote, etc. Each extracted field is its own row — emission per D-P10.

#### 3.4.2 Worked example — Precedent row (D-P3 manual SME authoring output)

Scenario: an attorney recognizes a pattern across multiple IP licensing matters with BigFirm LLP and authors a Precedent via D-P3 admin endpoint per [SPEC-phase-1-minimum.md §2](SPEC-phase-1-minimum.md). The `sprk_precedent` row is created; on status → Confirmed, D-P4 projects to `insights-index`:

```jsonc
{
  "id": "prec:bigfirm-cure-period-survives:v1",
  "tenantId": "tenant-acme",
  "artifactType": "precedent",
  "subject": "pattern:ip-licensing-bigfirm-llp",
  "predicate": "pattern",
  "value": {
    "raw": {
      "patternTitle": "IP licensing matters with BigFirm LLP — cure-period clauses survive",
      "scope": { "matterType": "IP licensing", "opposingCounsel": "BigFirm LLP" },
      "supportingMatters": ["M-2024-0341", "M-2024-0188", "M-2024-0099", "...", "M-2022-0211"],
      "sampleSize": 14,
      "patternConsistency": 0.86,
      "dateRange": { "from": "2022-01-01", "to": "2024-08-31" }
    },
    "displayHint": "precedent-statement"
  },
  "confidence": null,   // Precedents are SME-confirmed; no probabilistic confidence
  "evidence": [
    { "refType": "supporting-matter", "ref": "matter://M-2024-0341" },
    { "refType": "supporting-matter", "ref": "matter://M-2024-0188" },
    "...12 more supporting-matter refs"
  ],
  "asOf": "2026-05-15T14:22:00Z",
  "producedBy": "manual-sme-author",
  "content": "In IP licensing matters where BigFirm LLP represents the counterparty, cure-period clauses survived final negotiation in 12 of 14 matters reviewed (86%). Settlement amounts in these matters ranged from $185K to $520K, with a median of $310K. Average matter duration from filing to closure: 8.4 months.",
  "contentVector": [0.0421, 0.2103, ...],   // 3072-dim embedding of pattern statement
  "status": "confirmed"
}
```

The `content` field — the pattern statement — is what the synthesis playbook retrieves via vector similarity. The `value.raw.scope` filters narrow which Precedents apply to a given matter context. The supporting-matter refs let surfaces drill through to the basis matters.

#### 3.4.3 Worked example — synthesis query (D-P14 `predict-matter-cost`)

When `POST /api/insights/ask {question:"predict-matter-cost", subject:"matter:M-NEW-0042"}` invokes the playbook, the `IndexRetrieveNode` (D-P12) issues TWO queries against `insights-index`:

**Query 1 — cohort Observations (Phase 1 only retrieves the discriminator subset needed)**:
```
filter: tenantId eq 'tenant-acme' 
        and artifactType eq 'observation'
        and predicate eq 'outcomeCategory'
        and value/raw/matterType eq 'IP licensing'
        and value/raw/dealSizeBucket eq 'mid-market'
search: <vector similarity against the new matter's cohort embedding>
top: 25
```

Result: up to 25 `outcomeCategory` Observations from comparable matters; the playbook then passes the matter IDs to a parallel query for `settlementAmount` Observations. `EvidenceSufficiencyNode` (D-P12) checks `comparableMatters ≥ 12`; if not, the playbook routes to `DeclineToFindNode` with structured `DeclineResponse`.

**Query 2 — applicable Precedents**:
```
filter: tenantId eq 'tenant-acme'
        and artifactType eq 'precedent'
        and status eq 'confirmed'
        and value/raw/scope/matterType eq 'IP licensing'
        and value/raw/scope/opposingCounsel eq 'BigFirm LLP'
search: <vector similarity against the new matter's context>
top: 5
```

Result: any Confirmed Precedents whose scope filters match the new matter. The `predict-matter-cost` Inference cites both comparable matters (from Query 1) and Precedents (from Query 2) in its `evidence[]` array per D-04.

This is the entire substrate-side dataflow for Phase 1: ingest writes Observations → SME authors Precedents → synthesis reads both via `IndexRetrieveNode`. No cross-substrate composition (per §3.4 above); no graph traversals (Cosmos deferred to Phase 1.5 per D-P17); no separate question-catalog system (per D-54).

### 3.5 Spaarke BFF AI Facade — architectural boundary compliance (binding)

This project lives inside `Sprk.Bff.Api/` and shares its codebase with parallel work in [`projects/sdap-bff-api-remediation-fix/`](../sdap-bff-api-remediation-fix/) (BFF remediation). That project's **Outcome E** introduces a facade at `Sprk.Bff.Api/Services/Ai/PublicContracts/` to enforce a clean separation between AI internals and domain code. The Insights Engine project MUST comply with this boundary from day one — getting it wrong creates couplings that the remediation project's CI gate (FR-C6, lands in `sdap-bff-api-remediation-fix` Phase 6 task 082) will reject.

#### 3.5.1 The two zones

**Zone A — AI-internal** (anything under `Services/Ai/`)
- May freely import `IOpenAiClient`, `IPlaybookService`, `PlaybookExecutionEngine`, `IChatClient`, `UseFunctionInvocation`, `Microsoft.Extensions.AI.*`, `Microsoft.SemanticKernel.*`, `OpenAI.*`, `Azure.AI.*`

**Zone B — Domain / CRUD** (everything else — `Services/Insights/`, `Services/Workspace/`, `Services/Finance/`, `Services/Jobs/` outside `Services/Ai/Jobs/`, `Services/Dataverse/`, `Services/Communication/`, `Api/`, `Endpoints/`, `Filters/`, `Models/`)
- Must NOT import AI internals listed above
- May ONLY consume AI via interfaces under `Services/Ai/PublicContracts/`

#### 3.5.2 Insights Engine deliverable placement (extended for D-P)

| Deliverable | Placement | Zone |
|---|---|---|
| D-P1 `InsightArtifact` envelope POCOs | `Models/Insights/` | B (POCOs only — no AI imports) |
| D-P2 `insights-index` schema (JSON) | `infra/insights/schemas/` | (infra — out of zone framing) |
| D-P3 `sprk_precedent` entity + admin endpoint | `Api/Insights/PrecedentAdminEndpoints.cs` + Dataverse | B (Dataverse access only) |
| D-P4 Precedent projection sync | `Services/Insights/Precedents/PrecedentProjectionSync.cs` | B (Dataverse → AI Search; calls indexing helper) |
| **D-P5 Layer 1 classification playbook node config** | Authored against existing `AiAnalysisNodeExecutor` at `Services/Ai/Nodes/` | A (uses LLM internals) |
| **D-P6 Layer 2 outcome extraction playbook node config** | Authored against existing `AiAnalysisNodeExecutor` at `Services/Ai/Nodes/` | A (uses LLM internals) |
| **D-P7 Universal ingest playbook authoring** | Playbook definition (Dataverse playbook entity); execution in `Services/Ai/Insights/IngestOrchestrator.cs` | A |
| **D-P8 SPE-upload event consumer** | New `Services/Jobs/SpeUploadConsumer.cs` OR new Function | B at the dispatch boundary; **must call `IInsightsAi` facade** to invoke the ingest playbook (NOT `PlaybookExecutionEngine` directly) |
| D-P9 `GroundingVerifier` (and `GroundingVerifyNode` wrapper in D-P12) | `Services/Ai/CitationVerification/` | A |
| **D-P10 Confidence gating + per-field emission post-processor** | `Services/Ai/Insights/Extraction/` | A |
| D-P11 Observation review surface | Dataverse model-driven view + `Services/Insights/Observations/ObservationMirrorSync.cs` (Dataverse mirror) | B |
| **D-P12 New Insights-mode node executors** | `Services/Ai/Nodes/` (extends existing 10-executor registry) | A |
| **D-P13 Insights playbook execution caching** | `Services/Ai/Insights/InsightsPlaybookExecutionCache.cs` (wraps `PlaybookExecutionEngine`) | A |
| **D-P14 `predict-matter-cost` synthesis playbook** | Playbook definition (Dataverse playbook entity); orchestration in `Services/Ai/Insights/InsightsOrchestrator.cs` (also hosts `IInsightsAi` impl) | A |
| D-P15 `POST /api/insights/ask` endpoint | `Api/Insights/InsightEndpoints.cs` | B (calls `IInsightsAi` facade) |
| D-P16 Smoke test + golden dataset | `tests/Insights/` | (test) |
| **D-P17 `IInsightGraph` interface design + stub** | `Services/Insights/Graph/` | B (POCOs + interface; stub throws `NotImplementedException`) |

#### 3.5.3 The `IInsightsAi` facade contract

A new interface `Sprk.Bff.Api/Services/Ai/PublicContracts/IInsightsAi.cs` exposes Insights orchestration to Zone B. Phase 1 surface (small, focused, named after the domain need — NOT the AI mechanism):

```csharp
public interface IInsightsAi
{
    Task<InsightsAgentResult> AnswerQuestionAsync(
        InsightsAgentRequest request,
        CancellationToken ct);

    Task<InsightsIngestResult> RunIngestAsync(
        InsightsIngestRequest request,   // {spaarkeFilesItemRef, matterId, tenantId}
        CancellationToken ct);
}
```

Implementation in `Services/Ai/Insights/InsightsOrchestrator.cs` (Zone A) wires `PlaybookExecutionEngine` with D-P13 caching for `AnswerQuestionAsync`, and invokes the universal ingest playbook for `RunIngestAsync`. D-P8 consumer (Zone B) calls `RunIngestAsync`; D-P15 endpoint (Zone B) calls `AnswerQuestionAsync`. Neither sees `IChatClient` or `PlaybookExecutionEngine` directly.

If the facade is missing a method Zone B needs, add ONE method to `IInsightsAi` — do NOT widen the facade with raw `IChatClient` or `PlaybookExecutionEngine` access.

#### 3.5.4 Forbidden imports in Zone B

In `Services/Insights/**` (other than D-P9 Zone A primitive), `Api/Insights/**`, `Models/Insights/**`, `Services/Jobs/SpeUploadConsumer*.cs`, the following imports are FORBIDDEN:

- `Microsoft.Extensions.AI.*` (incl. `IChatClient`, `UseFunctionInvocation`)
- `Microsoft.SemanticKernel.*`
- `OpenAI.*`, `Azure.AI.OpenAI.*`
- `Sprk.Bff.Api.Services.Ai.IOpenAiClient`
- `Sprk.Bff.Api.Services.Ai.IPlaybookService` / `PlaybookExecutionEngine`
- `Sprk.Bff.Api.Services.Ai.Chat.*` (any Chat agent/tool/factory)
- `Sprk.Bff.Api.Services.Ai.Insights.*` (Insights orchestration itself — only the facade interface is allowed)
- `Sprk.Bff.Api.Services.Ai.Nodes.*` (the new node executors — Zone A only)
- Direct construction of `KernelBuilder`, `OpenAIClient`, etc.

Allowed AI-related imports in Zone B:
- `Sprk.Bff.Api.Services.Ai.PublicContracts.*` (the facade interfaces)
- `Sprk.Bff.Api.Models.Ai.*` (POCO request/response shapes)

#### 3.5.5 Verification

A grep-based verification gate (FR-C6 from `sdap-bff-api-remediation-fix` Phase 6 task 082) will block PRs that violate the boundary. Until that lands, Phase 1 acceptance includes a manual grep check (see §5.1.1 below).

### 3.6 Explicitly NOT in scope — Phase 1

- **Cosmos NoSQL graph implementation** (deferred to Phase 1.5; interface design `IInsightGraph` D-P17 ships in Phase 1 to preserve swap path)
- **Five-index dual-substrate** (D-53 revised: one derived index suffices for Phase 1)
- **Mode A record-projection sync function** (replaced by Live Facts on read; no Dataverse change-tracking sync in Phase 1)
- **Three-surface presentation** (context-pane card, Assistant pane, field-bound icon — deferred to Phase 1.5; Phase 1 ships programmatic API only)
- **Catalog index + routing tool + `/api/insights/route`** (deferred to Phase 2 — no Assistant pane consumer in Phase 1)
- **Snapshot persistence + `/api/insights/snapshot`** (deferred to Phase 2 — no save/pin/attach surfaces in Phase 1)
- **`/api/insights/relevant`** (deferred to Phase 1.5 — depends on context-pane surface)
- **Document extraction layers beyond outcome** (entity, deal terms, decision, risk — Phase 1.5)
- **Precedent lifecycle automation** (decay, drift, promotion — Phase 1.5; D-A26 scaffold not needed since D-P3 covers admin authoring directly)
- **MCP server contract document** (deferred to Phase 1.5)
- **Customer-onboarding workflow design** (deferred to Phase 1.5)
- **Surfacing design document** (deferred to Phase 1.5 with surfaces)
- **Automated backfill** (admin-triggered only per [SPEC-phase-1-minimum.md §3.6](SPEC-phase-1-minimum.md))
- **Cross-tenant federation / multi-tenant control plane** (deferred beyond Phase 2 per D-52, DEF-14)
- **Migrate `PlaybookIndexingBackgroundService` to Function** (Phase 3 cleanup)
- **EvaluatorGate** (LAVERN Pattern #2 — Phase 2+ quality upgrade)
- **Full GateResolver consumption** (Phase 2+ when write-back paths land)

## 4. Architecture summary

Per [decisions.md](decisions.md), Phase 1 commits to:

- **Engine framing** (D-59, integrating [SPEC-phase-1-minimum.md §0](SPEC-phase-1-minimum.md)): Spaarke's context production service — deterministic + probabilistic + synthesized claims orchestrated uniformly through one envelope and one facade
- **Taxonomy**: **four-tier** Fact / Observation / Precedent / Inference per D-03 and D-46
- **Single derived index** (D-53 revised): operational substrate consumed as-is + one new `insights-index` holding Observations and Precedents with `artifactType` discriminator
- **Universal layered ingest** (D-59): every SPE upload → Layer 1 classification → conditional Layer 2 outcome extraction; cheap layers gate expensive ones
- **Three mechanical post-processing gates** (D-63): `GroundingVerifier` (D-P9) + per-field confidence threshold gating + per-field Observation emission. Wired into the ingest playbook between extraction and persistence.
- **Prompt versioning + targeted re-extraction** (D-62): `producedBy.version` on every Observation enables `v1 → v2` re-extraction without re-extracting unchanged-version Observations
- **Observation review surface** (D-60, mandatory): Dataverse model-driven view + sample-based QA + disposition workflow. Without it the honesty contract is performative.
- **Precedent two-mode authoring** (D-61): Phase 1 ships manual SME authoring (D-P3); Phase 1.5 adds system-proposed Tentative Precedents via nightly cluster job
- **Questions-as-playbooks** (D-54): `predict-matter-cost` and future questions ARE Insights-mode playbooks on the existing `PlaybookExecutionEngine`; NO parallel question-catalog entity
- **Embedding**: `text-embedding-3-large` (3072 dim) on `insights-index`
- **Synthesis runtime**: `InsightsOrchestrator` in `Sprk.Bff.Api/Services/Ai/Insights/` (Zone A) wraps `PlaybookExecutionEngine` with D-P13 cache. Exposed to Zone B via `Services/Ai/PublicContracts/IInsightsAi` facade per §3.5.
- **LAVERN-derived enforcement primitives**:
  - `ISanitizer` (D-A25 / D-50) — strips prompt-injection vectors before any LLM step; wired into D-P7 ingest before Layer 1
  - `GroundingVerifier` (D-P9 / D-A22 / D-47) — mechanical post-extraction + post-synthesis citation check
  - `EvidenceGuard.Validate` (D-A23 / D-48) — runtime non-empty guard inside evidence-bearing nodes
  - `IDeclineToFindTool` (D-A24 / D-49) — deterministic decline path, realized as `DeclineToFindNode` in D-P12
- **Auth**: `Microsoft.Identity.Web` for inbound JWT (mirror `Sprk.Bff.Api/Program.cs`); `DefaultAzureCredential` for outbound. **Track B Dataverse-webhook auth dependency dissolves in Phase 1** under the D-P architecture (Phase 1 has no Dataverse-webhook consumer).
- **Tenant isolation** (D-52): single-tenant Phase 1 deployment; one customer = one Bicep parameter file = one full deployment unit. `tenantId` retained on derived index for partition keys + audit + future federation.
- **Graph (D-P17 + Phase 1.5)**: `IInsightGraph` interface ships in Phase 1 (stub impl); `CosmosNoSqlInsightGraph` is the first Phase 1.5 deliverable.

Refinement narrative: [SPEC-phase-1-minimum.md](SPEC-phase-1-minimum.md) (canonical Phase 1 rationale), [SPEC-phase-1-minimum.md](SPEC-phase-1-minimum.md) (historical, partially superseded), [lavern-pattern-assessment.md](lavern-pattern-assessment.md) (LAVERN pattern adoption rationale).

## 5. Acceptance criteria (Phase 1)

### 5.1 Track A acceptance

- [ ] Bicep deploys cleanly to Spaarke Dev environment (`spe-infrastructure-westus2`) using single-tenant parameter file pattern (D-52)
- [ ] **`insights-index`** provisioned with correct schema — `artifactType` discriminator, 3072-dim vectors, vectorFilterMode-preFilter friendly, `tenantId` first-class
- [ ] `sprk_precedent` Dataverse entity provisioned and queryable; relationship tables created
- [ ] `sprk_analysis` polymorphic source-type for Observation mirroring resolved and documented (first-step blocker on D-P11)
- [ ] `Sprk.Bff.Api` builds and existing tests pass after additions (no regressions)
- [ ] **4-tier envelope** round-trips through `InsightArtifact` C# types (Fact / Observation / Precedent / Inference all serialize/deserialize correctly)
- [ ] `IInsightGraph` interface (D-P17) compiles and stub registered in DI; resolves but throws on traversal calls
- [ ] **End-to-end ingest smoke**: a fixture closing-letter document uploaded to SPE triggers D-P8 consumer → D-P7 universal ingest playbook → Layer 1 classifies as `closing_letter` (confidence ≥ 0.7) → Layer 2 extracts outcome fields → `GroundingVerifier` verifies all quotes → confidence gating passes ≥ thresholds → per-field Observations persist to `insights-index` AND mirror to `sprk_analysis` for the review surface
- [ ] **End-to-end Precedent smoke**: Precedent created via `POST /api/insights/admin/precedents` → D-P4 projection writes to `insights-index` with `artifactType=precedent` → retrievable by `IndexRetrieveNode` query
- [ ] **End-to-end synthesis smoke**: `POST /api/insights/ask {question:"predict-matter-cost", subject:"matter:M-FIXTURE"}` returns either:
  - An Inference `InsightArtifact` with predicted cost, confidence, evidence refs (12+ comparable matter IDs from real Observations + any cited Precedents), reasoning summary; OR
  - A structured `DeclineResponse` per D-49 with `MinimumEvidenceNeeded` populated
- [ ] **`GroundingVerifier` mechanical check** strips one synthetic bad citation from an extraction output in unit tests
- [ ] **`DeclineToFindNode`** produces structured `DeclineResponse` (not freely-composed prose) on low-evidence path
- [ ] **Confidence threshold gating** rejects a synthetic low-confidence extraction in unit tests; threshold values match [SPEC-phase-1-minimum.md §3.4](SPEC-phase-1-minimum.md) starter values
- [ ] **Observation review surface** loads recent Observations in a Dataverse model-driven view; reviewer can mark Correct/Incorrect/Unclear with note; disposition writes back to the mirrored `sprk_analysis` row
- [ ] **Prompt versioning** — every persisted Observation carries `producedBy = "classification@v1"` or `"outcome-extraction@v1"`; a `v1 → v2` targeted re-extraction admin job query (no need to wire the job in Phase 1; query proves the pattern works) returns the right document set
- [ ] **Cache hit/miss telemetry** (D-P13) emitted; cache invalidation on access-scope change verified
- [ ] **Eval harness baseline** (D-P16) runs golden dataset (10–15 tuples for `predict-matter-cost`); RAG-triad metrics computed; baseline pass at permissive Phase 1 thresholds
- [ ] All ADR compliance verified via `/adr-check` skill (no new violations)
- [ ] Zero new SAS keys, zero new `ClientSecretCredential` usages (per D-24, D-27)

### 5.1.1 §3.5 AI facade boundary acceptance (binding)

- [ ] `Services/Ai/PublicContracts/IInsightsAi.cs` interface created with both `AnswerQuestionAsync` and `RunIngestAsync` methods; registered in DI
- [ ] D-P12 node executors live at `Services/Ai/Nodes/` and registered with existing node executor registry
- [ ] D-P14 synthesis playbook orchestration lives at `Services/Ai/Insights/InsightsOrchestrator.cs` (Zone A)
- [ ] D-P8 SPE-upload consumer (Zone B at dispatch boundary) injects `IInsightsAi` only — verified by grep
- [ ] D-P15 `/api/insights/ask` endpoint injects `IInsightsAi` only — verified by grep
- [ ] Zero hits for `IChatClient`, `IOpenAiClient`, `IPlaybookService`, `PlaybookExecutionEngine`, `Microsoft.Extensions.AI`, `Microsoft.SemanticKernel`, `OpenAI`, `Azure.AI.OpenAI`, `Services.Ai.Chat`, `Services.Ai.Insights[^.P]`, `Services.Ai.Nodes` in:
  - `Services/Insights/**/*.cs` (except D-P9 Zone A primitive at `Services/Ai/CitationVerification/`)
  - `Api/Insights/**/*.cs`
  - `Models/Insights/**/*.cs`
  - `Services/Jobs/SpeUploadConsumer*.cs` (D-P8)

  Suggested verification command:
  ```bash
  grep -rE "IChatClient|IOpenAiClient|IPlaybookService|PlaybookExecutionEngine|Microsoft\.Extensions\.AI|Microsoft\.SemanticKernel|using OpenAI|Azure\.AI\.OpenAI|Services\.Ai\.Chat|Services\.Ai\.Insights[^.P]|Services\.Ai\.Nodes" \
    src/server/api/Sprk.Bff.Api/Services/Insights/ \
    src/server/api/Sprk.Bff.Api/Api/Insights/ \
    src/server/api/Sprk.Bff.Api/Models/Insights/ \
    src/server/api/Sprk.Bff.Api/Services/Jobs/SpeUploadConsumer*.cs
  # Expect: zero matches (or only `Services.Ai.PublicContracts` references)
  ```
- [ ] Insights Engine Phase 1 PR(s) reference §3.5 in description as compliance check; future post-Phase-1 PRs touching Zone B Insights paths MUST pass the same grep before merge (interim manual check until `sdap-bff-api-remediation-fix` Phase 6 task 082 lands the FR-C6 CI gate)

## 6. Dependencies and blockers

### 6.1 Internal (Spaarke team)

| # | Dependency | Owner | Status | Blocking |
|---|---|---|---|---|
| DEP-3 | Resolution of O-02 (decisions.md): does `accessibleMatterSet` come from unified access control project or do we maintain our own source? Under D-52 single-tenant, no longer a privilege blocker; still needed for within-tenant trimming. | Architecture | Open | D-P14 synthesis playbook trimming; D-P13 cache-key scope hash |
| DEP-5 | LAVERN ADRs **10.1** (Precedent Board — D-P3/D-P4 scaffold reference), **10.6** (Sanitization + Citation Verification — D-P9). Proposed; not yet ratified. | Both projects (joint) | Proposed | D-P9, D-P3 design freeze |
| DEP-6 | `IGateResolver` interface (LAVERN ADR 10.3) — built by Action Engine MVP; Insights consumes for Phase 2+ write-back paths only. | Action Engine team | Pending Action Engine pipeline | No Phase 1 impact |
| DEP-7 | **AI facade boundary compliance per §3.5** — coordinates with `projects/sdap-bff-api-remediation-fix/` Outcome E task 046 (facade scaffold) and Phase 6 task 082 (FR-C6 CI gate). | sdap-bff-api-remediation-fix project | Outcome E task 046 ETA: post-Phase-3 baseline | D-P15 facade compliance; §5.1.1 acceptance gate |

> **Note**: DEP-1, DEP-2, DEP-4 (Phase C auth coordination, closure-extraction format) from prior SPEC are **no longer Phase 1 blockers** under the D-P architecture (Track B Dataverse-webhook auth work dissolved per §3.2; D-A12 closure-extraction design doc closed by [SPEC-phase-1-minimum.md §3](SPEC-phase-1-minimum.md) universal ingest design). They return as Phase 1.5 dependencies if/when Phase 1.5 nightly cluster job needs Dataverse change-tracking.

**First-step blockers embedded in tasks** (resolved as task Step 0, not pipeline blockers):

| First-step blocker | Owns the resolution |
|---|---|
| `insights-index` final name (e.g., `spaarke-insights`, `insights-index`) | D-P2 Step 0 |
| Document classification taxonomy SME review of Layer 1 enum values | D-P5 Step 0 |
| Layer 2 confidence threshold starter values confirmed by product/SME | D-P6 Step 0 |
| SPE-upload event source confirmed (event mechanism, dispatch shape, auth context) | D-P8 Step 0 |
| `sprk_analysis` polymorphic source-type for Observation mirroring confirmed via Dataverse MCP | D-P11 Step 0 |
| Phase 1 Precedent seeding count from SME engagement (zero OK; 1–2 sufficient for smoke test) | D-P3 Step 0 |
| Sampling percentage for Observation review (10% initial / 1–2% ongoing — admin confirmation) | D-P11 Step 0 |
| **Production live-ingest cost projection** (Layer 1 LLM call per SPE upload at projected production volumes) — admin/finance sign-off before D-P8 enables on real production traffic | D-P8 Step 0 (or product gating) |

### 6.2 External (Azure / Microsoft)

| # | Dependency | Status | Notes |
|---|---|---|---|
| EXT-1 | Function App / BackgroundService runtime for D-P8 consumer per ADR-001 | Verify before D-P8 | Confirm consumer architecture choice (Flex Consumption Function vs BackgroundService); ADR-001 permits Function for out-of-band integration |
| EXT-2 | `text-embedding-3-large` model deployed in `spaarke-openai-dev` | Verify before D-P5/D-P6 | Existing OpenAI account may need explicit model deployment |

## 7. Rigor and quality

- Per [CLAUDE.md §8](../../CLAUDE.md), this is a **FULL rigor** project (tags include `bff-api`, modifies `.cs` files, 17 deliverables, dependencies on multiple tasks).
- All tasks run via `task-execute` skill with code-review + adr-check quality gates at Step 9.5.
- **Spec-refinement coordination** is a continuous concern — every task must reference decisions.md §D-52 to D-63 + [SPEC-phase-1-minimum.md](SPEC-phase-1-minimum.md) rationale.
- LAVERN coordination — every LAVERN-derived task (D-P9; Sanitizer wiring; D-P12 `DeclineToFindNode`) references decisions.md D-47 / D-49 / D-50 and the LAVERN ADR proposals.

## 8. Phasing within Phase 1

A natural ordering for Phase 1. Each task's POML resolves its first-step blocker as Step 0 (see §6.1 table).

| Wave | Tasks | Rationale |
|---|---|---|
| W1 | D-P1, D-P17 | Foundation: `InsightArtifact` envelope POCOs + `IInsightGraph` interface design (stub) — both small, no runtime dependencies |
| W2 | D-P2, D-P3 | Infrastructure: `insights-index` schema + Bicep + provisioning; `sprk_precedent` entity provisioning. Parallel. First-step blockers Q-name, Q-seeding. |
| W3 | D-P9, D-P10, D-P12, D-P13 | Platform primitives (Zone A): `GroundingVerifier`; confidence gating + emission post-processor; new node executors; playbook execution cache. Parallel; all independent. Side-quest: generic `IDistributedCacheExtensions.GetOrCreateAsync<T>` helper. |
| W3.5 | `ReferenceIndexingService` parameterization refactor | Half-day refactor — parameterize for index name + schema mapper so both `spaarke-rag-references` AND `insights-index` use one code path. Not a separate D-P deliverable; foundation for D-P4 and D-P11 mirror writes. |
| W4 | D-P5, D-P6 | Layer 1 + Layer 2 prompt-bearing nodes (authored against existing `AiAnalysisNodeExecutor`). First-step blockers Q-taxonomy, Q-thresholds. |
| W5 | D-P7, D-P4 | Universal ingest playbook authoring (D-P7 — depends on W3 + W4); Precedent projection sync (D-P4 — depends on W3.5). Parallel. |
| W6 | D-P8, D-P11 | SPE-upload event consumer (D-P8 — depends on D-P7 + facade D-P15-in-progress). Observation review surface (D-P11 — Dataverse view + mirror sync, depends on W5 to have Observations to review). First-step blockers Q-event-source, Q-polymorphic, Q-sampling, Q-cost-projection. |
| W7 | D-P14, D-P15 | `predict-matter-cost` synthesis playbook (D-P14); `POST /api/insights/ask` endpoint via `IInsightsAi` facade (D-P15). Parallel where possible — D-P15 endpoint can scaffold against the facade before D-P14 playbook is fully wired. |
| W8 | D-P16 | Smoke tests + golden dataset + eval harness baseline. End-to-end ingest → review → synthesize cycle on fixture corpus. |

Tasks within a wave can be parallel where deps allow. The §5.1 acceptance criteria are checked in W8.

## 9. References

### 9.1 Project documents
- [decisions.md](decisions.md) — anchor doc; 63 numbered decisions (D-01 baseline + r2 D-39–D-45 + LAVERN-derived D-46–D-51 + spec-refinement D-52–D-58 with revisions + minimum-revision D-59–D-63)
- [design.md](design.md) — comprehensive design (13 sections); 2026-05-27 refinement callouts at §0 (refinement integration table)
- [SPEC-phase-1-minimum.md](SPEC-phase-1-minimum.md) — **canonical Phase 1 rationale narrative** (integrated into this SPEC 2026-05-28)
- [SPEC-phase-1-minimum.md](SPEC-phase-1-minimum.md) — historical (partially superseded by SPEC-phase-1-minimum.md per §9 of that doc)
- [ai-inventory.md](ai-inventory.md) — DI-anchored existing AI service inventory; informed Q5 duplication audit
- [azure-inventory.md](azure-inventory.md) — Dev + Demo Azure inventories
- [README.md](README.md) — project overview
- [lavern-pattern-assessment.md](lavern-pattern-assessment.md) — LAVERN pattern-by-pattern analysis

### 9.1a Related Spaarke projects
- [`projects/sdap-bff-api-remediation-fix/`](../sdap-bff-api-remediation-fix/) — **AI facade source-of-truth (§3.5, DEP-7)**
- [`projects/ai-advanced-capabilities-development/`](../ai-advanced-capabilities-development/) — source of LAVERN ADR proposals 10.1, 10.3, 10.6
- [`projects/ai-spaarke-action-engine-r1/`](../ai-spaarke-action-engine-r1/) — sister project; consumer of `GroundingVerifier` (D-P9) and Precedents

### 9.2 Knowledge base (researcher-authored)
- [knowledge/azure-ai-search/](../../knowledge/azure-ai-search/) — vector search, integrated vectorization, security trimming
- [knowledge/cosmos-gremlin/](../../knowledge/cosmos-gremlin/) — relevant when Phase 1.5 Cosmos lands
- [knowledge/azure-functions-isv/](../../knowledge/azure-functions-isv/) — Flex Consumption + per-tenant UAMI patterns
- [knowledge/dataverse-sync/](../../knowledge/dataverse-sync/) — Service Bus + Timer patterns (Phase 1.5 if needed)

### 9.3 ADRs
- [ADR-001](../../docs/adr/ADR-001-minimal-api-and-workers.md) — BFF runtime + Functions permitted for narrow out-of-band integration
- [ADR-008](../../docs/adr/ADR-008-endpoint-filter-authorization.md) — endpoint filter authorization
- [ADR-009](../../docs/adr/ADR-009-redis-first-caching.md) — Redis-first caching
- [ADR-010](../../docs/adr/ADR-010-di-minimalism.md) — DI minimalism
- [ADR-013](../../docs/adr/ADR-013-ai-architecture.md) — AI architecture
- [ADR-016](../../docs/adr/ADR-016-ai-cost-rate-limit-and-backpressure.md) — Rate limiting
- [ADR-019](../../docs/adr/ADR-019-problemdetails.md) — ProblemDetails errors

### 9.4 Source code references (informed by Q5 duplication audit)
- [`Sprk.Bff.Api/Program.cs`](../../src/server/api/Sprk.Bff.Api/Program.cs) — reference inbound JWT pattern
- [`Sprk.Bff.Api/Infrastructure/DI/AiModule.cs`](../../src/server/api/Sprk.Bff.Api/Infrastructure/DI/AiModule.cs) — `IChatClient` registration, tool framework
- [`Sprk.Bff.Api/Services/Ai/PlaybookExecutionEngine.cs`](../../src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookExecutionEngine.cs) + [`Sprk.Bff.Api/Services/Ai/Nodes/`](../../src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/) — engine + 10 existing node executors that D-P12 extends
- [`Sprk.Bff.Api/Services/Ai/Nodes/AiAnalysisNodeExecutor.cs`](../../src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/AiAnalysisNodeExecutor.cs) — hosts D-P5/D-P6 Layer 1/2 prompts; existing 3-tier retrieval (L1/L2/L3) informs D-P12 `IndexRetrieveNode` design
- [`Sprk.Bff.Api/Services/Ai/ReferenceIndexingService.cs`](../../src/server/api/Sprk.Bff.Api/Services/Ai/ReferenceIndexingService.cs) — template for `insights-index` upsert (parameterize per W3.5)
- [`Sprk.Bff.Api/Services/RecordMatching/DataverseIndexSyncService.cs`](../../src/server/api/Sprk.Bff.Api/Services/RecordMatching/DataverseIndexSyncService.cs) — pattern for D-P4 Precedent projection sync

## 10. Pipeline next step

Run from this worktree:

```
/project-pipeline projects/ai-spaarke-insights-engine-r1
```

The pipeline will decompose this SPEC into POML tasks based on the wave structure in §8, prioritizing the 17 D-P deliverables. Each task incorporates its first-step blocker (per §6.1 table) as Step 0. Track B (Dataverse webhook sync) is dissolved in Phase 1; revisit if Phase 1.5 nightly cluster job needs Dataverse change-tracking.

When Phase 1 ships, author `SPEC-phase-1.5.md` for Cosmos implementation + additional extraction layers + Precedent automation + additional synthesis questions.
