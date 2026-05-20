# Spaarke Insights Engine Architecture

> **Last Updated**: 2026-05-20
> **Last Reviewed**: 2026-05-20
> **Revision**: r2 — adds source data and corpus model (§4), surfacing model (§10), evaluation and quality (§14), external integration via MCP (§15), and future considerations (§22). Refines streaming-verification spec, sync re-fetch backpressure, adjacency-list write amplification, embedding lifecycle, and BFF decomposition signals. Moves historical backfill and the evaluation harness into Phase 1.
> **Status**: Pre-implementation (Phase 1 SPEC.md is pipeline-ready; design.md is canon)
> **Purpose**: Spaarke-wide canonical architecture document for the Insights Engine — the back-end subsystem that produces honestly-grounded organizational context for AI agents and end users.
> **Note on length**: This document is comprehensive (~1500 lines) by explicit user override of the standard `docs/architecture/` "decisions-only" convention. Project-internal authority remains [`projects/ai-spaarke-insights-engine-r1/design.md`](../../projects/ai-spaarke-insights-engine-r1/design.md); when they disagree, `design.md` wins until this doc is updated.

---

## Table of contents

1. [Overview](#1-overview)
2. [Where it fits in the Spaarke solution](#2-where-it-fits-in-the-spaarke-solution)
3. [Conceptual model](#3-conceptual-model)
4. [Source data and corpus](#4-source-data-and-corpus)
5. [Component structure](#5-component-structure)
6. [Data flow](#6-data-flow)
7. [Integration points](#7-integration-points)
8. [Substrate decisions](#8-substrate-decisions)
9. [Synthesis layer](#9-synthesis-layer)
10. [Surfacing model and user experience](#10-surfacing-model-and-user-experience)
11. [Sync and extraction pipelines](#11-sync-and-extraction-pipelines)
12. [Auth model](#12-auth-model)
13. [Privilege model](#13-privilege-model)
14. [Evaluation and quality measurement](#14-evaluation-and-quality-measurement)
15. [External integration via MCP](#15-external-integration-via-mcp)
16. [Azure resources](#16-azure-resources)
17. [Other resources and dependencies](#17-other-resources-and-dependencies)
18. [Packagability and deployment](#18-packagability-and-deployment)
19. [Design decisions](#19-design-decisions)
20. [Constraints](#20-constraints)
21. [Phased plan](#21-phased-plan)
22. [Future considerations](#22-future-considerations)
23. [Related](#23-related)

---

## 1. Overview

The **Spaarke Insights Engine** is a back-end subsystem that transforms organizational signals (matters, documents, AI sessions) into honestly-grounded context for AI agents and end users. It realizes the Memory and Inference layers of the Legal IQ Stack — the marketing positioning that promises *"Based on 200 similar matters your department has handled, this one will likely cost $280K and take 14 months."* That claim is **organizational inference grounded in specific past matters with citable provenance** — distinct from generic AI hedging based on industry data. The Engine is the bridge between today's Spaarke (session-scoped context, record-scoped RAG lookups) and that compounding cross-matter intelligence.

Its scope is bounded by what it **owns** vs what it **consumes** vs what it **emits to**. The Engine **owns** the resolver + synthesis layer, the sync/extraction pipelines, and the derived stores (Insight Index + Insight Graph). It **consumes — but does not own** the source systems (Dataverse, SharePoint Embedded, AI session storage) as signal inputs; data flows from them into the Engine via well-defined interfaces, but their schemas, lifecycles, and deployment are not the Engine's responsibility. It **emits to — but does not own** the presentation surfaces (Context pane, form widgets, ribbon flyouts, Outlook/Teams add-ins) and general BFF endpoints (chat, document operations, analysis) that render `InsightResponse` payloads. Sources are upstream signal providers; surfaces are downstream consumers. This ownership boundary is the load-bearing architectural decision (D-02) — it determines what ships when "Insights Engine v2" deploys (the Engine's components) vs what doesn't (the sources and surfaces, which evolve on their own cadence). It is the reason the component is called an "Engine" rather than a "platform" or "stack."

Every artifact produced by the Engine carries provenance, every Inference cites specific evidence, and every claim about evidence sufficiency is explicit. Empty states are first-class: when comparable data is insufficient, the response is *"need ~N more matters to answer reliably"* — never a silent fallback to generic AI. This is the **honesty contract** (D-04) — the architectural mechanism that prevents the system from becoming dishonest by construction.

### 1.1 What this revision (r2) adds

The original document was strong on *how* the Engine works internally and weaker on *what flows through it*, *how users experience it*, and *how its honesty is measured*. This revision adds five sections without changing the core architecture:

- **§4 Source data and corpus** — explicit answer to "what data, from where, in what volumes, and how much is needed before the Engine is useful." Names the three signal categories (Dataverse structured records, SPE documents at milestones, AI session content), maps each to artifact types, gives per-question minimum-corpus thresholds, and introduces historical backfill as a Phase 1 capability rather than a deferred cleanup.
- **§10 Surfacing model and user experience** — how Facts, Observations, and Inferences are rendered across the three interaction modes (Glance / Investigate / Refine) and across the surface map (Insight Cards on the matter form, Context pane, ribbon flyovers, Outlook/Teams add-ins, Copilot Chat, notifications). Establishes the rendering invariants that make the honesty contract visible in pixels.
- **§14 Evaluation and quality measurement** — the synthetic corpus, the golden dataset, and the RAG-triad evaluators (Retrieval / Groundedness / Relevance) that turn the honesty contract from a structural promise into a measurable property. Phase 1 ships with the harness wired into CI.
- **§15 External integration via MCP** — the Engine exposes its question catalog as an MCP server (`Sprk.Insights.Mcp`), so M365 Copilot, Copilot Studio agents, Microsoft Agent Framework workflows, and future Foundry agents can consume Spaarke intelligence without the Engine giving up its taxonomy or its honesty contract. The integration posture is "be consumed by Microsoft's tools," not "consume them."
- **§22 Future considerations** — Foundry IQ agentic retrieval as a Phase 2+ option for narrative-content evidence only; Engine extraction from the BFF when monolith strain warrants it; cross-customer benchmarks subject to legal/privacy work.

Refinements within existing sections cover streaming-verification mechanism (rule-based + embedding-similarity, not LLM-call), sync re-fetch backpressure mitigation, adjacency-list write amplification at high-degree vertices, embedding model lifecycle, and the BFF decomposition signal.

---

## 2. Where it fits in the Spaarke solution

The Spaarke platform is composed of several long-lived subsystems, each documented in `docs/architecture/`. The Insights Engine adds a new substrate-and-orchestration layer that sits alongside the existing AI platform (described in [AI-ARCHITECTURE.md](AI-ARCHITECTURE.md)), reusing its primitives while introducing new derived stores and a dedicated synthesis agent.

### 2.1 Spaarke subsystem landscape (relevant subset)

| Subsystem | Role | This Engine's relationship |
|---|---|---|
| **BFF API** (`Sprk.Bff.Api`) | Minimal API runtime hosting all server-side endpoints, AI orchestration, jobs | **Host**. Engine's resolver, agent, and Live Facts run inside the BFF. |
| **AI platform** (Scope library, tool framework, RAG, chat) | Four-tier AI substrate per [AI-ARCHITECTURE.md](AI-ARCHITECTURE.md) | **Reused as foundation**. Engine borrows `IChatClient`, tool framework, RAG indexing pipeline, embedding cache, chunking. |
| **Dataverse** | System of record for matters, parties, documents, financials | **Signal source**. Engine consumes via Live Facts (read) and sync (event-driven projection). |
| **SharePoint Embedded** | Document storage | **Signal source**. Closure-extraction reads documents via existing SPE access patterns. |
| **AI Search** (existing service) | Vector + structured retrieval | **Reused service, new indexes**. Engine adds `insight-*` indexes alongside existing `spaarke-records-index`, `spaarke-rag-references`, knowledge/discovery indexes. |
| **Azure OpenAI** | LLM endpoint | **Reused**. Engine calls existing OpenAI account via `IOpenAiClient`. |
| **Redis** | Cache layer | **Reused**. Engine adds two-tier memory keys + per-question TTL cache. |
| **Service Bus** | Async backbone (existing for jobs) | **Reused/extended**. Engine adds new Dataverse-changes topic for sync. |
| **Surfaces** (Context pane, form widgets, ribbon flyouts, Outlook/Teams, public API) | Presentation | **Downstream consumers — emitted to, not owned by, the Engine.** Receive `InsightResponse` payloads via `POST /api/insights/ask`. |
| **JPS playbook system** (per [playbook-architecture.md](playbook-architecture.md)) | Configuration-driven AI workflows | **Reused**. Closure-extraction is a JPS playbook ending with `DeliverToIndexNodeExecutor`. |

### 2.2 How a typical request flows across subsystems

A user opens the Context pane on a new matter. The pane asks the Engine "predict cost and flag risks":

```
[Context pane / Code Page surface]
        │  authenticatedFetch → POST /api/insights/ask
        ▼
[Sprk.Bff.Api  ── endpoint: InsightEndpoints]
        │  authorization filter (per ADR-008)
        │  rate limiting (per ADR-016)
        ▼
[InsightsResolverService]
   ├─ question router (catalog or ad-hoc)
   ├─ accessibleMatterSet resolver (per-session cache)
   ├─ signal fetcher (parallel)
   │     ├──→ [Live Fact Resolver] ──→ Dataverse (direct query, 5-min cache)
   │     ├──→ [AI Search]            ──→ insight-matters (vector + filter)
   │     └──→ [IInsightGraph]        ──→ Cosmos (named traversals)
   ├─ Insights Agent
   │     ├─ IChatClient + UseFunctionInvocation pipeline
   │     ├─ tools: FindComparableMatters, GetMatterFacts, RetrieveByGraph,
   │     │        AssessEvidenceSufficiency, ComposeInference
   │     └─ evidence-sufficiency rule check
   ├─ provenance assembler
   └─ per-question TTL cache
        │
        ▼
   InsightResponse  →  back through BFF  →  rendered by pane
```

Asynchronously, when Dataverse mutates, the sync pipeline keeps the substrate fresh:

```
[Dataverse change event]
        │  webhook (HTTPS POST)
        ▼
[Intake Function — AUTH TRUST BOUNDARY]
   • clientState (transitional) / HMAC (target, Phase C #044)
   • Validate; publish to Service Bus via UAMI (no SAS)
        ▼
[Service Bus topic: spaarke-dataverse-changes-{tenant}]
        │  subscribers (UAMI-authorized)
        ▼
[InsightsSyncFunction]
   • Re-fetch via DefaultAzureCredential
   • Project → InsightArtifact envelope
   • Push to AI Search (UAMI)
   • Update graph vertex/edges (UAMI to Cosmos)
   • Emit milestone events (status → closed → SBus)
        │
        ▼ (when matter status transitions to closed)
[ClosureExtractionTrigger]
   • Calls BFF API endpoint to run closure-extraction JPS playbook
   • Playbook output flows through DeliverToIndexNodeExecutor
   • New Observations land in insight-matters (+ graph updates)
```

### 2.3 What's new vs reused

| | New for the Engine | Reused from existing Spaarke |
|---|---|---|
| **Code** | `InsightsResolverService`, `Insights Agent`, `IInsightGraph` + `CosmosNoSqlInsightGraph`, `LiveFactResolverService`, `IInsightArtifactStore`, Sync/Reconciliation/Extraction Functions | `IChatClient`, `UseFunctionInvocation` pipeline, `IAiToolHandler` + `IToolHandlerRegistry`, `RagIndexingPipeline`, `ReferenceIndexingService` patterns, `EmbeddingCache`, `SemanticDocumentChunker`, `IOpenAiClient`, `PlaybookExecutionEngine`, `DataverseService`, `DeliverToIndexNodeExecutor` |
| **Azure** | Cosmos NoSQL account (new), Function App (new — narrowed ADR-001 permits), Service Bus topic for Dataverse changes (new), `insight-*` indexes in existing AI Search service | AI Search service, Azure OpenAI account, Redis, Key Vault, App Insights, Log Analytics, Managed Identity |
| **Schema** | `InsightArtifact` envelope (C# types), 4 new AI Search index schemas, Cosmos graph schema (vertex types + edge types), question catalog with evidence-sufficiency rules | JPS playbook schema (existing) — closure-extraction is a JPS playbook |

The Engine is intentionally additive. It does not replace any existing AI subsystem; it sits beside them and consumes their primitives.

---

## 3. Conceptual model

### 3.1 The three-artifact taxonomy

Every piece of context the Engine produces is one of three things, each with a different trust profile, store, and presentation rule. This taxonomy is the most important architectural invariant — mixing the three is what makes "intelligence" silently dishonest.

| Type | Source | Confidence | Lives in | Presented as |
|---|---|---|---|---|
| **Fact** | Deterministic computation over systems of record | 1.0 always | Live Dataverse query OR materialized feature view in Insight Index | Stated directly. No hedging. |
| **Observation** | Probabilistic extraction by playbook/LLM at a milestone | 0.0–1.0 | Insight Index (AI Search) + Insight Graph references | Stated with confidence + evidence link |
| **Inference** | Synthesized on demand by the Insights Agent over Facts + Observations | 0.0–1.0 | Never authoritatively stored (cached only) | Stated with confidence, comparable set, reasoning |

#### 3.1.1 Examples to anchor the taxonomy

- **Fact**: `Matter M-1234 was pending 287 days` — computed from `closedDate - openedDate`. Always true given the source. State directly.
- **Observation**: `Matter M-1234 outcome quality: favorable (0.92)` — produced by a closure-extraction playbook reading documents and decisions. Carries confidence and evidence. Cite the playbook + source docs.
- **Inference**: `Predicted cost for this new matter: ~$280K (confidence 0.74), based on 12 comparable matters` — synthesized at query time. Cite the 12 matters and their actual numbers. If only 3 are comparable, return *"insufficient evidence"* with the gap.

### 3.2 The unified artifact envelope

Every artifact uses the same envelope so surfaces can render uniformly. Fields like `tenantId`, `confidence`, `evidence[]`, `asOf`, `validFrom/To`, and `producedBy.version` are non-negotiable.

```json
{
  "id": "stable-identifier",
  "type": "fact | observation | inference",
  "subject": { "entityType": "matter", "entityId": "M-1234" },
  "predicate": "totalSpend | outcomeQuality | predictedCost | ...",
  "value": {
    "raw": "<typed value>",
    "displayHint": "currency-usd | percentage | duration-days | enum | text"
  },
  "confidence": 0.0,
  "evidence": [
    { "refType": "fact-source", "ref": "dataverse://sprk_matter/M-1234#totalSpend" },
    { "refType": "document", "ref": "spe://drive/abc/item/xyz" },
    { "refType": "comparable-matter", "ref": "matter://M-0567" },
    { "refType": "playbook-run", "ref": "playbook://closure-extraction@v3/run-2026-04-12" }
  ],
  "asOf": "2026-05-19T08:30:00Z",
  "validFrom": "2026-05-19T08:30:00Z",
  "validTo": null,
  "producedBy": {
    "kind": "query | playbook | agent",
    "id": "playbook://closure-extraction@v3 | query://matter-duration | agent://insights-v1",
    "version": "v3"
  },
  "scope": {
    "tenantId": "tenant-acme",
    "matterId": "M-1234",
    "clientId": null,
    "practiceArea": "ip-licensing",
    "jurisdiction": "us-ca",
    "year": 2024
  },
  "embedding": [0.123],
  "tenantId": "tenant-acme"
}
```

Notes:
- `tenantId` is a **top-level field** (not just nested in `scope`) so it is a filterable index field. Per D-12 it is first-class on every new index — the existing `spaarke-records-index` omits this and the Engine MUST NOT repeat that gap.
- `producedBy.version` is **mandatory for Observations** (D-05) — enables selective re-extraction when a playbook ships v2.
- `embedding` is populated for Observations and Inferences; null for Facts (which are retrieved by direct filter, not similarity).
- `validFrom` / `validTo` model temporal validity (e.g., "total spend as of 2026-05-19" differs from "total spend as of 2026-06-30").

### 3.3 Provenance is the API contract

Every artifact carries its evidence. The surface receives `{value, evidence[]}` as a minimum; rendering provenance is non-optional. **A surface that cannot display provenance cannot display Inferences** — only Facts, which need none beyond their source query (D-04). This single rule prevents most ways the system could become dishonest.

---

## 4. Source data and corpus

This section answers the question stakeholders ask first: *what data flows into the Engine, where does it come from, how does it get delivered, and how much is needed before the Engine produces useful answers?* The honest answer is that the Engine consumes three categorically different kinds of data, each feeding different artifact types with different volume and freshness requirements.

### 4.1 The three signal categories

| Category | What it is | Feeds which artifacts | Delivery mechanism |
|---|---|---|---|
| **Dataverse structured records** | Existing Spaarke entities (matter, invoice, party, person, firm, document metadata, AI analyses, KPI records) | Live Facts directly; structured Observation inputs; graph vertices and edges | Dataverse webhook → Intake Function → Service Bus → `InsightsSyncFunction` (§11) |
| **SPE documents at milestones** | Closure summaries, settlement agreements, judgments, pleadings, OCGs, final invoices, internal closure memos | Observations via closure-extraction JPS playbook | Matter-milestone Service Bus event → `ClosureExtractionTrigger` → BFF API runs JPS playbook → `DeliverToIndexNodeExecutor` |
| **AI session content** | Outputs from existing Document Intelligence analyses, AI Playbooks, AI Tool runs (with confidence and provenance) | Observations and behavioral signals | Playbook completion event → if tagged Observation-producing → same `DeliverToIndexNodeExecutor` path |

The Engine does **not** index every document in SPE continuously — that is what `spaarke-rag-references` does for chat-with-document scenarios. The Engine consumes documents *bounded by milestone* through a deliberate extraction step. This is the architectural difference between RAG (passages from everywhere, retrieved on demand) and structured organizational inference (typed evidence from designated decision points). Stakeholders sometimes conflate the two; the distinction matters because it bounds extraction cost, governs what gets vector-indexed, and explains why the Engine's value compounds slowly rather than instantly.

### 4.2 Dataverse field-level mapping

The Engine consumes specific fields from existing Spaarke entities. This mapping is the contract between the Engine and the upstream data model — if these entities lose fields the Engine depends on, syncs degrade. Coordinated with the entity owners.

| Entity | Fields the Engine reads | Used for |
|---|---|---|
| `sprk_matter` | Identity, dates (opened/closed/key milestones), practice area, jurisdiction, status, deal size, counterparty refs, billing arrangement, lead counsel | Live Facts (`matterDuration`, `status`, `daysSinceLastActivity`), scope filtering on retrieval, comparability dimensions for Inferences |
| `sprk_invoice` + line items (JSON) | Amounts, dates, billing codes, fee earners, narratives | `totalSpend` Live Fact; spend trend Observations; budget variance signals; aggregate spend Observations materialized in `insight-matters` |
| `sprk_billingevent` / `sprk_spendsnapshot` / `sprk_spendsignal` | Pre-computed spend artifacts from the Finance Intelligence pipeline | Direct consumption — already Engine-shaped; referenced as Facts with `confidence: 1.0` |
| `sprk_party` | Counterparties, clients, related parties | `Party` graph vertices; key comparability dimension for counterparty-aware Inferences |
| `sprk_person` | Counsel, judges, partners, associates, clients | `Person` graph vertices; `WORKED_ON`, `ADJUDICATED`, `REPRESENTED` edges |
| `sprk_firm` | Opposing firms, expert firms | `Firm` graph vertices; `BELONGS_TO` edges |
| `sprk_document` | Document metadata, type, sensitivity, matter association | `Document` graph vertices, source filter for closure-extraction selection |
| `sprk_analysis` | Existing AI outputs (current AI results entity) | Promoted to Observations when confidence and provenance are well-formed; otherwise consumed as evidence |
| Performance Assessment entities (OCG Compliance, Budget Compliance, Outcome Success per Matter Report Card) | KPI definitions, calculation results, source-tagged inputs | First-class Observations with structured confidence; polymorphic input handling per the Matter Report Card workstream |
| `sprk_playbook` runs | JPS playbook executions with run IDs, version, output | Provenance for Observations; `Playbook` graph vertices for traceability |

### 4.3 SPE document selection — milestone-bounded, not continuous

The Engine's document extraction is triggered by *matter milestones*, not by document upload. The milestones that fire `ClosureExtractionTrigger`:

- Matter status transition to `closed`
- Settlement event recorded
- Final judgment or order entered
- Final invoice paid
- Explicit manual trigger by lead counsel ("declare this matter knowledge-complete")

At each milestone, the closure-extraction playbook selects a bounded document set from the matter's SPE container (typically 5–20 documents per matter) and produces Observations. Documents that are never associated with a milestone are never extracted by the Engine — they remain in SPE, indexed by `spaarke-rag-references` if the document-RAG pipeline reaches them, but they do not contribute to the Insight corpus. **This bounded extraction is what makes the Engine cost-tractable; an "index every document for inference" approach would not be.**

### 4.4 AI session content — promotion to Observations

The Engine doesn't run AI analysis itself; it *consumes* AI work that already happens elsewhere on the platform. When a Document Intelligence analysis, AI Playbook, or AI Tool produces output with confidence and provenance, and that output is tagged as Observation-producing in the playbook definition, the output flows through `DeliverToIndexNodeExecutor` to `insight-*` indexes exactly like closure-extraction output.

This means existing Spaarke AI features (invoice extraction, OCG compliance checks, risk detection, document classification) become Observation producers when their playbook definitions are updated to emit envelope-shaped outputs. No new AI surface area; existing AI work, made compounding.

### 4.5 Volumes — small data, by Azure standards

For a mid-size legal department running on Spaarke for two years, expect:

| Signal | Order of magnitude |
|---|---|
| Matters | 2,000–10,000 |
| Invoice line items | 50,000–500,000 |
| Documents in SPE | 10,000–100,000 |
| Parties / persons / firms (after identity resolution) | 1,000–10,000 |
| Closed matters with closure-extraction Observations | 800–6,000 |
| Observations total (10–30 per closed matter) | 8,000–180,000 |
| Graph vertices | 5,000–50,000 |
| Graph edges | 25,000–500,000 |

Dataverse can serve this; AI Search can index it at S1; Cosmos can graph it on serverless or low-end autoscale. **Volume is not the constraint on this Engine. Fidelity, history, and corpus completeness are.**

### 4.6 The minimum useful corpus, per artifact type

This is the question stakeholders most need a direct answer to. The Engine produces useful output at different points in a customer's deployment:

| Artifact | Minimum useful corpus | What "useful" means |
|---|---|---|
| **Facts** | Day 1 | Anything in Dataverse can be computed immediately. `matterDuration`, `totalSpend`, `status` — useful with one record. |
| **Observations** (per matter) | One closed matter | Closure-extraction on a single closed matter produces useful Observations *about that matter*. No corpus needed. |
| **Inferences — counterparty-specific** | ~5 matters with that counterparty | "How does Acme typically negotiate?" — comparability is high, so the bar is lower. |
| **Inferences — narrow** | ~12 comparable matters | Per the evidence-sufficiency rule for `predict-matter-cost`. Same rule applies to `predict-duration`, `predict-outcome`. |
| **Inferences — practice-area-wide** | ~50 matters in the practice area | "What's typical for IP licensing matters" — needs a population, not just comparables. |
| **Inferences — cross-practice / portfolio** | ~200–500 matters total | The "Legal IQ" headline claim. |

**Strategic implication**: the Engine has *something useful to show* from Day 1 (Facts) and from Day 30 (single-matter Observations from any matter the customer closes during the period), but the *headline* Inference capability needs roughly 12 comparables per question to fire, and a representative cross-practice claim needs hundreds. For an existing Spaarke customer with two years of matter history, this is plausible at deployment. **For a brand-new customer with no historical data, the corpus problem is real and needs explicit answers — which §4.7 addresses.**

### 4.7 Bootstrapping the corpus — historical backfill

The original document treated `BulkReExtraction` as a Phase 3 admin endpoint. This revision moves the customer-onboarding workflow that *uses* `BulkReExtraction` into Phase 1 / early Phase 2, because without it, every new customer sees insufficient-evidence responses for the first six months — commercially unacceptable regardless of how honest the architecture is.

**The historical backfill workflow (D-39)**:

1. **Ingest historical matter records** — pull matter, party, person, firm, document metadata, invoice, and analysis records from the customer's prior system (CounselLink, TeamConnect, internal Dataverse) into the Spaarke Dataverse model. This is a customer-onboarding ETL step (the M&A-style migration), not an Engine concern, but the Engine assumes it has happened.
2. **Trigger closure-extraction backfill** — `BulkReExtraction` iterates closed historical matters in priority order (most recent first, customer's most strategically important practice areas first) and runs the closure-extraction playbook over each.
3. **Emit progress events** — the backfill emits "matter N of M extracted; insight-matters index now at K artifacts; question-template X is now answerable for N matters" events to App Insights and to a customer-facing progress dashboard.
4. **Declare "Insights-ready" milestones** — when the corpus crosses the per-question minimum thresholds, the surface promotes those questions from `insufficient_evidence` defaults to active. The customer sees their Engine "wake up" question-by-question as the backfill completes.
5. **Idempotent** — `producedBy.version` check skips records already at current playbook version, so backfill can be paused, resumed, and re-run safely.

For a customer with five years of prior matter history (~3,000 closed matters), this turns a six-month corpus-build into a two-week migration. The backfill is expensive in OpenAI tokens (each closure-extraction is roughly 20–50K tokens of input + output) but is a one-time cost amortized across the customer relationship; budget for ~$2,000–10,000 in extraction cost per customer onboarding depending on history depth.

### 4.8 The Day-1 / Week-1 / Month-1 capability arc

A useful framing for sales conversations and stakeholder expectation-setting:

| Phase | What works | What doesn't (yet) |
|---|---|---|
| **Day 1** (after deployment) | All Facts; one-question chat against `predict-matter-cost` returns structured `insufficient_evidence` with the gap statement | No Observations; no Inferences |
| **Week 1** (historical backfill running) | Facts; Observations on backfilled matters as they complete; Inferences begin firing for practice areas where backfill has crossed threshold | Inferences for less-covered practice areas still return `insufficient_evidence` |
| **Month 1** (backfill complete, organic flow active) | All artifact types; most question templates firing for active practice areas | Cross-practice portfolio Inferences may still be evidence-thin |
| **Month 6+** (organic flow accumulated) | Full capability per the question catalog; cross-practice Inferences confident | — |

This arc should appear in customer-facing materials. The honest framing — that capabilities come online progressively as the corpus fills — is a better commercial story than the alternative ("the Engine is fully active from Day 1") because it converts the inevitable corpus-build period from a defect into a roadmap.

### 4.9 Synthetic and golden data for development and evaluation

The Engine needs two kinds of non-customer data, serving distinct purposes that often get conflated. Both are Phase 1 deliverables.

**Synthetic data** (development corpus):

- Generated corpus of 200–500 fictional matters with realistic distributions of practice area (IP licensing, employment, commercial litigation, regulatory, M&A, real estate), deal size, duration, outcome, parties, persons, firms, jurisdictions.
- Generated documents (closure summaries, settlement agreements, fee narratives, internal memos) authored by an LLM with realistic legal-style content; templated, not original — these are test fixtures, not training data.
- Generated by a script with weighted random distributions; refreshed quarterly.
- Used for: Bicep deployment verification, end-to-end pipeline testing, Phase 1 demos before any real customer data is loaded, performance testing the resolver at realistic corpus size, training new team members on the data model.
- Lives in `tests/Insights/fixtures/synthetic-corpus/` and is loaded by a fixture-loader script that targets dev environments.

**Golden evaluation data** (D-40):

- Curated set of 50–100 *question / expected-answer / expected-evidence* triples, designed to test the honesty contract end-to-end.
- Each entry specifies: a question (from the catalog), the matter context, the expected Inference value with acceptable range, the expected confidence band, the expected evidence pointers (which matters should appear in `evidence[]`), and — critically — a list of questions that *should* trigger insufficient-evidence responses given the test corpus configuration.
- Curated by domain experts (legal ops, senior counsel) with review by the Engine team.
- Smaller than the synthetic corpus; much higher value per record; much more expensive to produce.
- Used for: the RAG-triad evaluation harness (Retrieval / Groundedness / Relevance) — see §14.
- Lives in `tests/Insights/golden/` and runs as a CI gate on every Engine release per D-40.

Both are needed. Synthetic data enables development against realistic data shapes without exposing customer data. Golden data enables measurement of the honesty contract against expert-validated ground truth. The synthetic corpus is the *substrate* for evaluation; the golden dataset is the *test plan*.

---

## 5. Component structure

The Engine spans the BFF API, a new Function App, and three substrate stores. The C# code lives under `Sprk.Bff.Api/Services/Insights/` for resolver/agent/facts and a new `infra/insights/` for Bicep modules.

### 5.1 BFF-hosted components (new)

| Component | Path (new) | Responsibility | Reuse anchor |
|---|---|---|---|
| `InsightsResolverService` | `Sprk.Bff.Api/Services/Insights/InsightsResolverService.cs` | Top-level orchestrator: question router → signal fetcher → analyzer dispatch → provenance assembler → cache → access trimming | Mirrors `AnalysisOrchestrationService` pattern |
| `Insights Agent` factory | `Sprk.Bff.Api/Services/Insights/Agent/` | Tool-driven grounded synthesis using existing `IChatClient` + `UseFunctionInvocation` | Reuses `SprkChatAgentFactory` plumbing without conversational session/history overhead |
| `LiveFactResolverService` | `Sprk.Bff.Api/Services/Insights/Facts/LiveFactResolverService.cs` | Typed Dataverse queries for deterministic Facts; 5-min Redis cache | Wraps existing `IDataverseService` |
| `IInsightGraph` + `CosmosNoSqlInsightGraph` | `Sprk.Bff.Api/Services/Insights/Graph/` | Adjacency-list graph over Cosmos NoSQL with named traversals | NEW — no existing graph |
| `IInsightArtifactStore` | `Sprk.Bff.Api/Services/Insights/Index/` | Wraps `SearchClient` for Insight-specific operations (envelope serialization, idempotent upsert, evidence preservation) | Wraps existing AI Search SDK; pattern from `ReferenceIndexingService` |
| Insight tool handlers | `Sprk.Bff.Api/Services/Insights/Tools/` | `IFindComparableMattersTool`, `IGetMatterFactsTool`, `IRetrieveByGraphTool`, `IGetObservationsTool`, `IAssessEvidenceSufficiencyTool`, `IComposeInferenceTool` | Implements existing `IAiToolHandler` pattern |
| Question catalog | `Sprk.Bff.Api/Services/Insights/Questions/` | Per-question evidence-sufficiency rules, insufficient-evidence response shapes | NEW — first entry: `predict-matter-cost` |
| `InsightEndpoints` | `Sprk.Bff.Api/Api/Insights/InsightEndpoints.cs` | `POST /api/insights/ask`, endpoint-filter auth (ADR-008), rate limiting (ADR-016), ProblemDetails errors (ADR-019) | Mirrors existing endpoint patterns |
| `AnalysisServicesModule`-style DI module | `Sprk.Bff.Api/Infrastructure/DI/InsightsModule.cs` | Feature module wiring all of the above | Mirrors `AnalysisServicesModule.cs` |

### 5.2 Function App components (new)

A new Function App, permitted by the narrowed ADR-001 (commit `84cec9f9` permits Functions for narrow out-of-band integration). Hosted on Flex Consumption per D-17 and the Microsoft ISV pattern.

| Function | Trigger | Responsibility |
|---|---|---|
| `DataverseWebhookIntake` | HTTPTrigger | **Auth trust boundary**. Validates `clientState` (transitional) / HMAC-SHA256 (target, Phase C #044). Publishes validated event to Service Bus via UAMI. The only Function-App endpoint that accepts external traffic. |
| `InsightsSyncFunction` | ServiceBusTrigger | Subscribes to `spaarke-dataverse-changes-{tenant}`. Re-fetches Dataverse record (UAMI), projects to `InsightArtifact`, pushes to AI Search index (UAMI), updates graph vertex/edges (UAMI to Cosmos), emits milestone events. |
| `InsightsReconciliation` | TimerTrigger (`0 30 2 * * *` nightly) | Queries Dataverse change-tracking for records modified since last successful sync; re-fetches + re-projects; reconciles index counts vs Dataverse counts; alerts on drift. Idempotent. |
| `ClosureExtractionTrigger` | ServiceBusTrigger (`spaarke-matter-milestones-{tenant}`) | On matter milestone (closure, settlement, judgment), invokes the closure-extraction JPS playbook by calling the BFF API endpoint (Option A per design.md §6.4.1). Playbook output flows through `DeliverToIndexNodeExecutor` to AI Search. |
| `BulkReExtraction` | Admin-triggered (HTTPTrigger, JWT-authorized) | Backfill / re-extraction over historical records, skipping records already at the current playbook version via `producedBy.version` check. Emits progress events for monitoring. |
| `ScheduledReIndexer` | TimerTrigger | Embedding refresh, schema migration support, version-bump re-extraction. |

All non-webhook endpoints on the Function App use `Microsoft.Identity.Web.AddMicrosoftIdentityWebApi` for inbound JWT validation, mirroring `Sprk.Bff.Api/Program.cs`. All outbound calls use `DefaultAzureCredential` (no `ClientSecretCredential` — per D-27 and Phase C tasks 041/042).

### 5.3 Reused BFF components (Engine composes, does not reimplement)

From [`ai-inventory.md`](../../projects/ai-spaarke-insights-engine-r1/ai-inventory.md):

| Component | Path | Engine's use |
|---|---|---|
| `IChatClient` + `UseFunctionInvocation` pipeline | DI: `AiModule.cs` | Insights Agent's tool-calling backbone |
| `IAiToolHandler` + `IToolHandlerRegistry` + `ToolExecutionContext` | `Services/Ai/Tools/*` | Insight tool implementations follow this pattern |
| `IOpenAiClient` + `OpenAiClient` | `Services/Ai/IOpenAiClient.cs` | LLM calls for closure-extraction (Observations) and Insights Agent (Inferences) |
| `EmbeddingCache` | `Services/Ai/EmbeddingCache.cs` | Reduces OpenAI cost on Insight artifact embedding |
| `SemanticDocumentChunker` + `TextChunkingService` | `Services/Ai/` | Reuse for chunking long Observation content |
| `RagIndexingPipeline` | `Services/Ai/RagIndexingPipeline.cs` | Pattern reference; content-chunked Observations flow through similar pipeline |
| `ReferenceIndexingService` | `Services/Ai/ReferenceIndexingService.cs` | Pattern reference for idempotent re-indexing of structured artifacts |
| `RagService` + `RagQueryBuilder` | `Services/Ai/RagService.cs` | Hybrid keyword + vector + semantic-reranker search wrapped by `IInsightArtifactStore` |
| `PlaybookExecutionEngine` + `INodeExecutor` registry | `Services/Ai/` | Closure-extraction is a JPS playbook ending in `DeliverToIndexNodeExecutor` |
| `DeliverToIndexNodeExecutor` | `Services/Ai/Nodes/` | Writes playbook output (Observations) to AI Search indexes |
| `DataverseService` (`IDataverseService`) | `Services/Dataverse/` | Live Fact queries + sync re-fetches |

### 5.4 Stores (substrate)

| Store | Where | Contains |
|---|---|---|
| **Insight Index** | AI Search (existing service, new indexes) | Observations + materialized Facts; vector + structured fields per artifact envelope; 4 indexes: `insight-matters`, `insight-decisions`, `insight-risks`, `insight-sessions` |
| **Insight Graph** | Cosmos NoSQL (new account) | Vertices (Matter, Party, Person, Firm, Document, Issue, Jurisdiction, Outcome, Playbook) + typed edges (INVOLVED_PARTY, WORKED_ON, REPRESENTED, ADJUDICATED, BELONGS_TO, INVOLVED_ISSUE, VENUE, RESULTED_IN, RELATED_TO, REFERENCES, SAME_AS) + artifact refs |
| **Live Facts** | Direct Dataverse queries | No storage — deterministic computation over `sprk_matter`, `sprk_invoice`, etc. with 5-minute Redis cache |
| **Two-tier memory** | Redis | `user_profile` (30-day sliding) + `chat_summary` (session-scoped sliding); per-question TTL cache for resolver responses |
| **Audit log** | Existing audit Cosmos (append-only, immutable) | Every Insight request and response logged for compliance |

---

## 6. Data flow

### 6.1 Synchronous query — Inference

A surface asks "predict cost and flag risks for this new IP licensing matter against Counterparty Acme":

1. **Surface** → `POST /api/insights/ask` with `{question, subject, scope, surfaceHint}`. Bearer token in `Authorization`.
2. `InsightEndpoints` accepts the request. Endpoint filter performs resource authorization (ADR-008); rate limiting (ADR-016) is applied per principal.
3. `InsightsResolverService` receives `InsightRequest`. Resolves user's `accessibleMatterSet` from session cache (sourced from existing access control system — see [`uac-access-control.md`](uac-access-control.md)).
4. **Question router** classifies: known question template (catalog hit, fetch evidence-sufficiency rule) OR ad-hoc (route to Insights Agent directly).
5. `Insights Agent` is invoked with question + context. It uses tools to gather evidence (calls below run in parallel via `UseFunctionInvocation`):
   - `FindComparableMatters({practiceArea, dealRange, counterparty})` → vector + filter retrieval against `insight-matters` (with `vectorFilterMode=preFilter`, `tenantId` + `accessibleMatterSet` filter); returns N artifacts with similarity scores.
   - `GetMatterFacts({matterIds: […]})` → batch live-fact lookup for current state of returned matters via `LiveFactResolverService`.
   - `RetrieveByGraph({startNode: 'party:acme', relations: ['OPPOSED→Matter', 'INVOLVED_PARTY→Matter'], maxHops: 2})` → graph traversal returning connected matters, trimmed by `accessibleMatterSet`.
   - `AssessEvidenceSufficiency({comparableCount, minRequired: 12})` → returns `{sufficient, count, threshold, gap}` per question catalog rule.
6. **Synthesis**: Agent produces an Inference artifact with `{value, confidence, evidence[], reasoning}`. If insufficient → returns `{type: 'insufficient_evidence', value: null, message: 'Only N comparable matters found; need ~M for reliable estimate.', actionableGap: '...'}`. Never silent fallback to generic AI (D-06).
7. `InsightsResolverService` caches result (TTL per question type), assembles full provenance, returns `InsightResponse` to surface.

### 6.2 Synchronous query — Fact

Surface asks "how long has this matter been pending":

1. **Surface** → `POST /api/insights/ask` with `{question: 'matter-duration', subject: 'matter:M-1234'}`.
2. `InsightsResolverService`: question router recognizes deterministic question.
3. `LiveFactResolverService`: `closedDate - openedDate` (or `today - openedDate` if pending), single Dataverse query.
4. Returns artifact with `confidence: 1.0, evidence: [{refType: 'fact-source', ref: 'dataverse://sprk_matter/M-1234'}]`.
5. Cached briefly (5-minute Redis) for repeat calls.
6. Returned to surface.

No Agent involvement, no retrieval, single round-trip.

### 6.3 Asynchronous sync — Dataverse change → indexes/graph

Every Dataverse mutation on a tracked entity flows through the sync pipeline:

1. **Dataverse change** triggers registered webhook (HTTPS POST to Intake Function). Auth on this hop is `clientState` (transitional, validated against value stored in Key Vault) → HMAC-SHA256 (target state, Phase C #044). Validation code is **copied from BFF webhook handlers** — not reimplemented.
2. `DataverseWebhookIntake` validates the request, validates the payload shape, and publishes the event to Service Bus topic `spaarke-dataverse-changes-{tenant}` using `DefaultAzureCredential` (UAMI; Azure RBAC; no SAS).
3. `InsightsSyncFunction` (ServiceBusTrigger) consumes the message. **Re-fetches** the full Dataverse record (per knowledge research: webhooks are triggers only — 256KB payload truncation + out-of-order delivery; consumers re-fetch). Re-fetch uses `DefaultAzureCredential` per Phase C task 042.
   - **Backpressure mitigation (D-43)**: at high mutation rates (bulk invoice imports, end-of-quarter close operations, historical backfill), naive per-event re-fetch can trigger Dataverse throttling. `InsightsSyncFunction` applies a 30-second deduplication window per `(entityType, entityId)` — multiple mutations on the same record within the window collapse into a single re-fetch. Lock duration on the Service Bus subscription is set to allow this. For known-burst operations (backfill, bulk import), the function reads a per-tenant rate-limit config and applies token-bucket throttling on outbound Dataverse calls. Drift is acceptable; correctness is maintained by the nightly reconciliation Function (§6.4) catching any missed updates.
4. Projects record to `InsightArtifact` envelope. Generates embedding via `IOpenAiClient` (`text-embedding-3-large`, 3072 dim, cached via `EmbeddingCache`).
5. Idempotent upsert to AI Search index (`insight-matters` for `sprk_matter` records) using UAMI.
6. Updates graph: upserts vertex; for newly-discovered references (party, document, etc.), upserts target vertices; upserts typed edges.
7. If record event is a milestone (status transition to `closed`, etc.), emits a message to `spaarke-matter-milestones-{tenant}` for downstream extraction.

**Net at target auth state**: zero SAS keys, zero long-lived secrets. The only transitional secret is the `clientState` on the Dataverse → Intake hop, which disappears post Phase C #044.

### 6.4 Reconciliation

Nightly Timer trigger (`InsightsReconciliation`):

1. Queries Dataverse change-tracking API for records modified since last successful sync timestamp.
2. For each record: re-fetch + re-project + upsert to Search + graph (same as 5.3 steps 3–6).
3. Compares Search index counts vs Dataverse record counts; emits drift alerts to App Insights.
4. Updates last-sync timestamp on success.

Catches missed events, late-binding edges, schema drift. Idempotent — re-running on a healthy index is a no-op.

### 6.5 Closure-extraction

Triggered by Service Bus message on matter milestone:

1. `ClosureExtractionTrigger` receives milestone event.
2. Loads matter context (Dataverse re-fetch).
3. Invokes the closure-extraction JPS playbook by calling the BFF API endpoint (Option A — single playbook invocation path; the Function is a thin trigger, BFF runs the playbook).
4. Playbook reads matter documents, decisions, outcomes (via existing JPS infrastructure); produces a set of Observations.
5. Each Observation flows through `DeliverToIndexNodeExecutor` → AI Search (`insight-matters`, `insight-decisions`, `insight-risks`, `insight-sessions` per Observation type).
6. Graph vertices/edges updated based on extracted facts (e.g., new `(Matter)-[RESULTED_IN]->(Outcome)` edge).
7. Every Observation carries `producedBy.id = 'playbook://closure-extraction@v3'` and `producedBy.version = 'v3'` (D-05).

### 6.6 Identity resolution (probabilistic, no human curation)

When sync or extraction encounters a person/org name:

1. Normalize name (trim, case-fold, common-abbrev expansion).
2. Search graph for existing vertices of same type with similar name (fuzzy match + bar number + email domain + prior co-occurrences).
3. Score:
   - High-confidence (≥0.85): create `SAME_AS` edge automatically.
   - Medium (0.5–0.85): create new vertex with `proposedSameAs` ref; periodic batch job re-scores as more signal accumulates.
   - Low (<0.5): create new vertex with no link.
4. Scope by relevance — only invest effort on entities meeting threshold (N+ matters or N+ outbound edges). Long-tail mentions stay as standalone vertices (D-30).
5. Queries follow `SAME_AS` edges (configurable confidence threshold, default ≥0.7).
6. Aggregate Insights flag possible duplicates: *"Acme Corp has appeared in 8 matters (may include 2 likely duplicate references)"*.

### 6.7 Schema evolution

When `insight-matters` schema needs a field added or `closure-extraction` ships v4:

1. **Versioned indexes**: new index name (`insight-matters-v2`) created via Bicep.
2. **Dual-write** during migration: sync writes to both v1 and v2.
3. **Re-extract**: scheduled Function iterates v1, re-extracts at new playbook version, writes to v2. Targeted: queries for `producedBy.version < currentPlaybookVersion`.
4. **Cutover**: search reads switch to v2 (config flag).
5. **Decommission v1** after grace period.

---

## 7. Integration points

### 7.1 Upstream (Engine depends on)

| Depends on | Interface | Used for |
|---|---|---|
| Dataverse (existing) | `IDataverseService`, webhook, change-tracking API | Live Facts, sync re-fetch, reconciliation |
| SharePoint Embedded (existing) | Existing OBO + app-only Graph patterns | Document content for closure-extraction (read via existing SPE access) |
| AI Search (existing service) | `SearchClient` SDK via `IInsightArtifactStore` | Insight Index persistence + retrieval |
| Azure OpenAI (existing) | `IOpenAiClient` | Embeddings (`text-embedding-3-large`), Inference synthesis |
| Cosmos DB (new account) | `CosmosClient` via `IInsightGraph` | Insight Graph vertices + edges |
| Service Bus (new topic on existing namespace) | `ServiceBusClient` (UAMI) | Sync pipeline async backbone, milestone events |
| Redis (existing) | `IDistributedCache` / `RequestCache` | Two-tier memory, per-question TTL cache, Live Fact 5-min cache |
| Existing access control system | `accessibleMatterSet` resolver (DEP-3 open: own source or unified access control project) | Per-tenant in-tenant access trimming |
| BFF auth pipeline (`Microsoft.Identity.Web`) | `AddMicrosoftIdentityWebApi` | Inbound JWT validation on `/api/insights/ask` and Function-App non-webhook endpoints |
| Phase C auth work | Tasks #041 (Graph outbound), #042 (Dataverse outbound), #044 (HMAC webhook), #047 (explicit TenantId) | Outbound MI, webhook HMAC, per-tenant config |

### 7.2 Downstream (consumes Engine output)

| Consumer | Interface | Uses |
|---|---|---|
| Context pane (Code Page surface) | `POST /api/insights/ask` → `InsightResponse` | Tiered Insight rendering (Record / History / Matter Memory / Org Intelligence) |
| Form widgets (PCF / Code Pages) | Same endpoint | Single-Insight cards on matter form |
| Ribbon flyouts (PCF / Dataverse ribbon) | Same endpoint | Aggregate-only Insights in popover (counts, ranges) |
| Outlook / Teams add-ins | Same endpoint | Subset of pane content for the matter the message pertains to |
| Notifications | Push of newly-derived Insights via existing notification surface | Closure summary ready, risk signal detected — deep-link to pane |
| Public API | `GET /api/insights/…` (Phase 3) | Partner consumers; same envelope; same provenance rules |
| SSE events (extends unification design) | `insight_response`, `insight_highlight`, `insight_invalidate` | Cross-surface push for invalidation and pivot |

### 7.3 Adjacent (alongside, not directly coupled)

| Adjacent subsystem | Relationship |
|---|---|
| [AI platform](AI-ARCHITECTURE.md) | Engine reuses tool framework, IChatClient, RAG primitives; not a consumer or provider |
| [Playbook system](playbook-architecture.md) | Engine's closure-extraction is a JPS playbook; no new playbook infrastructure |
| [RAG architecture](rag-architecture.md) | Engine retrieval composes RagService; the Insight Index is a sibling, not a replacement |
| [Chat architecture](chat-architecture.md) | The Insights Agent is a tool-driven (not conversational) reuse of the same `IChatClient` pipeline; Insights questions can be asked from SprkChat surfaces but the Agent doesn't manage chat history (memory is the two-tier Redis pattern) |
| [Background workers](background-workers-architecture.md) | The Engine's `PlaybookIndexingBackgroundService` (existing) may eventually migrate to a Function (Phase 3 cleanup per `DEF-03`) |

---

## 8. Substrate decisions

### 8.1 Vector + structured retrieval — Azure AI Search

**Decision (D-07)**: Use the existing Azure AI Search service. Reuse `spaarke-search-prod` / `spaarke-search-dev`. Add new indexes (`insight-matters`, `insight-decisions`, `insight-risks`, `insight-sessions`) following the `spaarke-rag-references` pattern (small structured docs + embedding + idempotency).

**Why not a separate service**: AI Search is already provisioned, supports multi-index, and the per-tenant question is solved by separate services-per-tenant at physical isolation (§13), not by adding services for the Engine.

**Embedding model (D-08)**: `text-embedding-3-large` (3072 dim). Rationale: legal text has rich semantic relationships; recall on "find comparable matters" is critical for the IQ Stack's headline claim. Cost differential vs `-small` (~6.5× per token; 2× storage) is real but not material at our scale; *"no reason to knowingly underbuild"* applies for a foundational choice that's expensive to migrate later.

**Cost monitoring**: ~120GB of vector storage per 10M artifacts/tenant — drives S1→S2 tier consideration earlier than -small would. Embedding generation cost ~$0.13/1M tokens (vs $0.02 for -small) — per-tenant operational cost line worth tracking.

**Tier sizing**: Start at S1 (sufficient for r1 dev and early pilot). Plan S2 bump (~$1,000/mo per tenant service) when artifact volume approaches ~5M per tenant. For physical-per-tenant model, this is per-tenant operational cost that scales linearly with customers (`DEF-02`).

**ACL enforcement**: `vectorFilterMode=preFilter` is **mandatory** (D-33) for ACL trimming in vector queries with high-cardinality access groups, per Microsoft guidance.

**Embedding model lifecycle (D-45)**: `text-embedding-3-large` will not be the current model forever. When Microsoft ships `text-embedding-4` (or any successor), the entire Insight Index needs re-embedding — this is a known operational event with a documented playbook:

1. Provision a new `insight-matters-v{N+1}-emb{NewModel}` index with the new embedding dimensions.
2. `ScheduledReIndexer` (Function) iterates the current index, reads each document, re-embeds via the new model, writes to the new index. Idempotent and resumable.
3. Resolver runs dual-read for a configurable grace period (returns results from both indexes, scoring with the new model's vectors and reranking).
4. Cutover via config flag once validation confirms relevance parity or improvement.
5. Decommission old index.

This is a 30-day operation per tenant at full corpus scale, dominated by OpenAI embedding cost (~$0.13/1M tokens × the full corpus token count). The operational pattern is the same as the playbook-version migration (§6.7); only the trigger is different.

### 8.2 Graph — Cosmos NoSQL with adjacency-list

**Decision (D-09)**: **Cosmos NoSQL with adjacency-list documents**, **NOT** Cosmos Gremlin.

This builds a real graph data model on a document database. What it lacks is graph-native query syntax (Gremlin/Cypher) and in-engine optimization for 5+ hop algorithms — neither of which we need. Our traversals are 2–3 hops with filters, not deep graph algorithms (PageRank, community detection). What it gains:

- **Vector co-location on the same node documents** — a `Person` vertex can carry an embedding, making "find people semantically similar" a single-store operation. Gremlin can't do this.
- **Microsoft's primary product investment direction** — better tooling, SDKs, doc cadence.
- **Lower regional friction** — Gremlin has documented region-creation issues.
- **One Cosmos API surface** for the team to learn.

**Abstraction (D-10)**: `IInsightGraph` exposes named traversals (`FindMattersInvolvingPartyAsync`), not raw query syntax. Consumers depend on the abstraction; if Phase 2+ ever needs purpose-built graph DB, swap is a contained refactor.

**Adjacency-list pattern**: Documents per vertex with embedded edge collections (both outgoing and incoming for efficient reverse lookup):

```jsonc
{
  "id": "matter:M-1234",
  "tenantId": "tenant-acme",
  "vertexType": "Matter",
  "properties": {
    "displayName": "...",
    "practiceArea": "ip-licensing",
    "jurisdiction": "us-ca",
    "openedDate": "2024-03-12",
    "status": "closed"
  },
  "outgoingEdges": [
    { "edgeType": "INVOLVED_PARTY", "targetVertexId": "party:acme", "edgeProperties": { "role": "opposing" } },
    { "edgeType": "WORKED_ON", "targetVertexId": "person:smith.j", "edgeProperties": { "role": "lead-counsel" } },
    { "edgeType": "VENUE", "targetVertexId": "jurisdiction:us-ca", "edgeProperties": {} }
  ],
  "incomingEdges": [
    { "edgeType": "INVOLVED_PARTY", "sourceVertexId": "party:acme" }
  ],
  "artifactRefs": [
    "insight-matter://M-1234#closure-summary",
    "insight-matter://M-1234#outcome-facts"
  ]
}
```

**Partition key**: `tenantId` (per knowledge research recommendation — supports per-tenant cost attribution and clean physical isolation).

**Domain schema**:

| Vertex type | Notes |
|---|---|
| `Matter` | Every matter opened |
| `Party` | Every counterparty, client, related party |
| `Person` | Counsel (ours + opposing), judges, partners, associates, clients |
| `Firm` | Opposing counsel firms, expert firms |
| `Document` | Significant documents (Observations reference these — not every file) |
| `Issue` / `Claim` | Issue types or claims |
| `Jurisdiction` | Courts, regulatory bodies |
| `Outcome` | Outcome categories |
| `Playbook` | JPS playbook (for traceability) |

| Edge type | From → To | Edge properties |
|---|---|---|
| `INVOLVED_PARTY` | Matter → Party | role: opposing \| client \| related |
| `REPRESENTED` | Person → Party (in a Matter context) | 3-way: modeled as edge with edge properties; promoted to vertex if role context matters often |
| `ADJUDICATED` | Person → Matter | Judges |
| `WORKED_ON` | Person → Matter | role: counsel \| partner \| associate |
| `BELONGS_TO` | Person → Firm | — |
| `INVOLVED_ISSUE` | Matter → Issue | — |
| `VENUE` | Matter → Jurisdiction | — |
| `RESULTED_IN` | Matter → Outcome | — |
| `RELATED_TO` | Matter → Matter | deal series, parent transactions, appeals |
| `REFERENCES` | Matter → Document | — |
| `SAME_AS` | Person → Person (or Party → Party) | confidence; probabilistic identity (§6.6) |

**Write amplification at high-degree vertices (D-44)**: An active judge or major firm vertex can accumulate thousands of `WORKED_ON` / `ADJUDICATED` / `BELONGS_TO` edges. Cosmos NoSQL has a 2MB per-document limit and per-document RU cost scales with size. The adjacency-list pattern is correct for r1 development and for typical vertices (≤500 edges), but the Engine must monitor edge counts and migrate high-degree vertices to an **edges-as-documents** model when the threshold is approached. The trigger: when any vertex exceeds 500 outbound edges or 200KB document size, the Engine's nightly reconciliation Function migrates that vertex by splitting outbound edges into separate `edge:{sourceId}:{edgeType}:{targetId}` documents partitioned by source vertex. The `IInsightGraph` abstraction's named traversals remain unchanged; the implementation flips for migrated vertices transparently. Plan the migration tooling in Phase 2; the trigger threshold and migration is unlikely to fire in Phase 1 with a freshly-bootstrapped corpus but is essential at scale.

### 8.3 Live Facts — direct Dataverse queries

**Decision (D-11)**: Cheap, deterministic computations stay as live Dataverse queries — no materialization. Expensive aggregates that are queried often get materialized in `insight-matters` alongside Observations (still `confidence: 1.0`).

| Live Facts (initial set) | Materialized Facts (when needed) |
|---|---|
| `matterDuration` (closedDate − openedDate) | Per-matter outcome metrics at closure (cost, duration, win rate) |
| `totalSpend` (aggregate query on financial records) | Per-client / practice-area rollups (computed nightly or on-event) |
| `status` (current Dataverse field) | Embedding-friendly summarizations that join with Observations during retrieval |
| `daysSinceLastActivity` | |
| `documentCount` | |

5-minute Redis cache via existing `RequestCache` / `IDistributedCache`. Avoids hammering Dataverse for repeat calls in the same session.

### 8.4 Two-tier memory (borrowed from Foundry; implemented in Redis)

**Decision (D-15)**: Per knowledge research, Foundry uses a two-tier model: `user_profile` (static) + `chat_summary` (contextual). We adopt the pattern locally via Redis (we are NOT in Foundry per D-13).

| Tier | Contents | TTL | Key |
|---|---|---|---|
| **User profile** | User's role, practice areas, preferred analytical depth, often-asked questions | 30 days, sliding | `insights:profile:{tenantId}:{userId}` |
| **Chat summary** | Last N turns of active Insights session (when chat-driven), accumulated context | Session lifetime, sliding | `insights:session:{tenantId}:{sessionId}` |

Memory shapes the question and the synthesis style; evidence still comes from indexes/graph/facts (memory doesn't bypass retrieval).

---

## 9. Synthesis layer — Insights Agent

### 9.1 Custom BFF agent, NOT Foundry-hosted

**Decision (D-13)**: Implement the Insights Agent as a custom component in `Sprk.Bff.Api`, not Foundry-hosted. Foundry's superpowers (durable agents, HITL, A2A workflows) aren't used by the Insights Agent — it's a stateless tool-driven Q&A pattern. Foundry is the right choice for *separate* surfaces (multi-day matter diligence, durable workflows) and remains a future option (`DEF-06`) for those.

**Reuses (D-14)**: `IChatClient`, `UseFunctionInvocation` pipeline (Microsoft.Extensions.AI), tool framework (`IAiToolHandler`). All scaffolding exists in `Sprk.Bff.Api` per [`ai-inventory.md`](../../projects/ai-spaarke-insights-engine-r1/ai-inventory.md).

### 9.2 Tools

| Tool | Purpose | Returns |
|---|---|---|
| `FindComparableMatters` | Vector + filter retrieval over `insight-matters` | List of `MatterArtifact` with similarity scores |
| `GetMatterFacts` | Live-fact lookup for one or more matters | Map of `matterId → {factPredicate → value}` |
| `RetrieveByGraph` | Named graph traversals | List of vertex refs + edge context |
| `GetObservations` | Retrieve Observations matching a predicate + scope filter | List of Observation artifacts |
| `AssessEvidenceSufficiency` | Check if comparable set is large/diverse enough for an Inference | `{sufficient: bool, count: N, threshold: M, gap: '…'}` |
| `ComposeInference` | Final synthesis — wraps conclusion in envelope with evidence | Inference artifact |

Each tool follows the existing `IAiToolHandler` pattern; no new tool registry.

### 9.3 Evidence-sufficiency rules — non-negotiable

Every Inference question carries (in the question catalog) an evidence-sufficiency rule:

```jsonc
{
  "questionId": "predict-matter-cost",
  "tier": "inference",
  "requiredEvidence": {
    "comparableMatters": { "min": 12, "preferred": 25 },
    "scopeMatchOn": ["practiceArea"],
    "scopeMatchPreferOn": ["counterparty", "jurisdiction"],
    "freshness": "comparables from last 3 years preferred"
  },
  "insufficientResponse": {
    "type": "insufficient_evidence",
    "message": "Only {actual} comparable matters found; need ~{required} for a reliable estimate.",
    "actionableGap": "Need more {practiceArea} matters with comparable {dealSize}."
  }
}
```

`AssessEvidenceSufficiency` reads this rule and either proceeds with synthesis or returns the structured insufficient-evidence response. **This is the architectural enforcement of the honesty contract** (D-06). Without explicit sufficiency rules per question, the Agent defaults to "make something up with hedging language" — the exact behavior the marketing claims to differ from.

Phase 1 ships one question (`predict-matter-cost`); Phase 2 adds 10+ question templates.

### 9.4 Safety controls

- **Streaming + verification (D-42)** — the Insights Agent streams the Inference value, comparable count, and top citations early; the safety verifier runs in parallel with streaming and gates the final commit of the response within roughly 200ms after generation completes. **Mechanism**: rule-based + embedding-similarity, *not* a second LLM call. The verifier checks:
  - **Claim-evidence binding** — every numeric value in the Inference (`$280K`, `12 comparables`, `87% confidence`) must trace to a value present in the assembled evidence set. Mismatch → flag.
  - **Citation existence** — every matter ID in `evidence[]` must exist in the index and be in `accessibleMatterSet`. Missing or inaccessible → drop the citation and flag if the remaining set falls below sufficiency.
  - **Embedding-similarity check** — the natural-language `reasoning` field is embedded and compared against the embedding of the concatenated evidence content; similarity below threshold (configurable, default 0.55) → flag and add a `verification.warning` field to the response. Above threshold passes silently.
  - **Confidence-band sanity** — confidence must be consistent with comparable count and scope match (e.g., 12 comparables across mismatched practice areas → cap confidence at "Medium"); enforced by a rules table per question.

  This is cheap (one embedding call already cached in most cases, plus dictionary lookups) and bounded — no risk of an LLM verifier hallucinating during verification. The 200ms budget is achievable because no second LLM round-trip is involved.
- **Strip retrieved content on cross-matter pivot** (existing rule, extended): when user pivots Matter context mid-session, prior retrieved evidence is cleared from history (D-35). Aggregate conclusions retained (they don't contain matter-specific privileged details).
- **Tool-call gating** (extends existing `PendingPlanManager`): for write-back operations (e.g., "save this Inference back to `insight-sessions`"), require explicit user approval.

### 9.5 Tool inventory — Phase 1 scope and evolution

Phase 1 ships the six tools listed in §9.2 — `FindComparableMatters`, `GetMatterFacts`, `RetrieveByGraph`, `GetObservations`, `AssessEvidenceSufficiency`, `ComposeInference`. These wrap the Engine's own substrate (AI Search via `IInsightArtifactStore`, Cosmos via `IInsightGraph`, Dataverse via `LiveFactResolverService`).

Phase 2+ may add additional tools as the corpus and question catalog expand — `FindRelatedMatters` (graph + similarity hybrid), `ComputeSpendTrend`, `AssessRisk`, `SummarizeMatterCorpus`. The tool framework supports this additively; each new tool implements `IAiToolHandler` and registers in the Insights module's DI registration.

**Foundry IQ / Agentic Retrieval is not part of the Phase 1 tool inventory.** It is evaluated as a Phase 2+ option specifically for narrative-content evidence questions, where Microsoft's query-planning capabilities may add measurable value over plain hybrid search. See §22.1 for the future-state evaluation criteria. The `IInsightArtifactStore` and tool abstractions preserve the swap path; no Phase 1 code commits to consuming any external retrieval orchestrator.

---

## 10. Surfacing model and user experience

The Engine produces honestly-grounded `InsightResponse` payloads. How those payloads become a usable experience depends on the surface, the interaction mode, and a small set of rendering invariants that make the honesty contract visible in pixels. This section is the design contract between the Engine and any client that renders its output.

### 10.1 The three rendering patterns (one per artifact type)

The three-artifact taxonomy (D-03) is also the *visual* invariant. A surface that renders Facts and Inferences with the same chrome undermines the entire taxonomy. Each artifact type gets a distinct rendering pattern that the Spaarke design system enforces:

| Artifact | Visual pattern | Confidence indicator | Evidence display |
|---|---|---|---|
| **Fact** | Render directly. Treat like a spreadsheet cell. | None — visual confidence indicator would imply uncertainty that doesn't exist. | Tiny "from Dataverse" attribution footnote only. |
| **Observation** | Value + confidence band + "from [playbook]" badge | Three-state band: High (≥0.85, green), Medium (0.5–0.85, amber), Low (<0.5 — filtered out at the resolver, never rendered). | One-line evidence pointer with link to source documents. |
| **Inference** | Value + confidence band + comparable count *visible without clicking* + top 3 cited matter IDs *visible by default* | Same three-state band, derived from sufficiency and scope match. | Full evidence panel one click away; never default-hidden behind multiple clicks. |
| **Insufficient evidence** | Informational card (NOT error styling) | None — this is a structured result, not a failure. | Actionable gap statement: "Need ~N more X matters with Y characteristics." |

**The most important rendering rule**: an Inference card must show its comparable count and top citations *by default*, not behind an expander. The architectural commitment to provenance becomes operational only when the user *sees* the evidence without effort. Surfaces that hide provenance behind clicks degrade to feeling-like-generic-AI regardless of the backend.

### 10.2 The three interaction modes

The same `InsightResponse` payload serves three distinct intentionality levels. Each mode has different UX shapes and different resolver behavior:

| Mode | What it is | Surface examples | Resolver behavior |
|---|---|---|---|
| **Glance** | Pre-computed Insights for *this* record; user didn't ask | Matter form Insight Cards, ribbon flyovers, Outlook add-in | Resolver runs for question catalog entries flagged `surfaceMode: ambient` on record load; response cached aggressively |
| **Investigate** | User asks a specific question or clicks a question chip | Context pane, Teams chat, AiToolAgent PCF | Resolver runs the requested question with full evidence assembly; surfaces the Inference card with provenance |
| **Refine** | User adjusts scope or filters on an answered question | Context pane after initial answer, evidence drawer faceted filters | Same `questionId`, modified `scope` / `filter` block; per-question TTL cache keyed on the modified parameters; cheap to re-fire |

The `surfaceMode` field on question catalog entries (`ambient` | `intentional` | `both`) tells the resolver which questions to pre-compute for Glance surfaces vs. which require an explicit ask.

### 10.3 Surface map and intentionality

| Surface | Primary mode | What it shows | Auth |
|---|---|---|---|
| **Matter form Insight Cards** (Code Page or PCF embedded in form) | Glance | 2–4 pre-computed insights for the current matter: predicted cost, top risks, counterparty history snapshot, related matters | User token; `accessibleMatterSet` trimmed |
| **Context pane** (Code Page in app sidebar) | Investigate + Refine | Full question catalog scoped to current record (chips along top); freeform asks via text input; evidence drawer for the Inference panel | User token; `accessibleMatterSet` trimmed |
| **Ribbon flyovers** (PCF in ribbon button) | Glance | Aggregate counts only (e.g., "8 prior matters with Acme") — never specific record IDs (per D-34 counts-vs-IDs rule) | User token; aggregate-only |
| **Outlook add-in** | Glance | One Insight for the matter the email pertains to; "open in app" deep-link to Context pane | NAA (Nested App Authentication); user token |
| **Teams add-in** | Investigate + Refine | Conversational entry to the Engine; same agent, different shell | NAA; user token |
| **M365 Copilot Chat** | Conversational | Routed through pro-code declarative agent (M365 Agents Toolkit); agent consumes the Engine's MCP server (§15) | Copilot token + OBO to Engine |
| **Notifications** | Push | New Insight emerged (closure summary ready, risk signal detected); deep-link to Context pane | User token |
| **Power BI / formatted reports** | Reporting | Portfolio-level aggregate Insights | App-only with audit trail; aggregate-only |

### 10.4 The Refine contract — what makes interaction real

Refine is what makes the Engine feel like an analyst rather than a search box. After a user sees an Inference card, the natural follow-ups are:

- **"Show me the 12 matters."** Click cited-matters chips → opens an evidence drawer with all comparables, their key Facts (deal size, duration, outcome), and faceted filters.
- **"Exclude the small-deal ones."** Faceted filters in the evidence drawer are stateful — toggle a deal-size range, the Inference card recomputes with the new scope.
- **"Why so high?"** Reasoning panel shows which subqueries the agent ran, which evidence weighted most, where the prediction landed in the comparable distribution.
- **"Compare to last year's IP licensing."** Pivot case — new scope on the same question. Cross-matter pivot rule (D-35) fires; prior matter-specific evidence is cleared from session state.

Each of these is a `POST /api/insights/ask` with a refined `scope` or `filter` block on the same `questionId`. The resolver's per-question TTL cache is keyed on `(questionId, subject, scope, filter)` so refinement is cheap. **This is the architectural reason refinement is a UX-cheap operation** — and it's worth surfacing as a deliberate capability in product copy, because it's what makes the Engine feel like an analyst.

### 10.5 Insufficient-evidence as a feature, not a failure

The Insufficient-evidence card is the most important to design well. The rendering rules:

- **Not red, not warning, not error styling**. Informational tone (e.g., blue accent).
- **Constructive copy**, not apologetic: "We need ~9 more IP licensing matters with comparable deal size before we can answer this reliably."
- **Action statement when possible**: "Closing matter M-2103 should bring this online." (Tightly coupled to the `actionableGap` field of `insufficientResponse` in the question catalog.)
- **No false confidence offer**: do not provide a "best guess anyway" toggle. The honesty contract is undermined if the system has an escape hatch back into hedged generic AI.

Implementation requires that the `insufficientResponse.actionableGap` field in the question catalog be *good copy*, not auto-generated boilerplate. Assign ownership to a single content designer who writes all gap copy across all questions, because consistency of voice is what makes it feel like a competent analyst rather than an error message.

### 10.6 The Day-1 surface story (what users see in early deployment)

Tied to the capability arc in §4.8:

- **Day 1**: All Glance surfaces show Facts immediately for any matter (duration, totalSpend, status). Inference cards are present but mostly show Insufficient-evidence with constructive gap statements. Users learn the system's vocabulary on real data without overpromising.
- **Week 1 (backfill running)**: Insight Cards begin showing real Inferences for practice areas that crossed sufficiency. Surface flags update silently as more questions come online. Users notice the system "wakes up" without retraining.
- **Month 1+ (organic flow active)**: Full surface population. Notifications begin firing as new closures produce material Insights about active matters.

This progressive activation should be visible in product copy too — a small in-app banner during early days ("Spaarke is learning your matters — N of M backfilled") sets expectations and converts the corpus-build period into a roadmap rather than a defect.

### 10.7 Surface-side design contracts that must be honored

These are the rendering invariants the Engine relies on. Surface implementations MUST:

- Display `evidence[]` for every Inference; never collapse it behind multiple clicks.
- Render confidence bands using the three-state visual vocabulary (never raw numbers as primary, never hide them entirely).
- Use `displayHint` from the artifact's `value` block to choose formatting (currency-usd, percentage, duration-days, enum, text).
- Render Insufficient-evidence cards as informational, not as errors.
- Respect `surfaceMode` on question catalog entries — don't pre-compute `intentional`-only questions on Glance surfaces; don't hide `ambient` questions behind explicit asks.
- Apply `value.displayHint`-aware rendering rules (currency rounds appropriately, durations show units, enums show display labels not raw codes).
- For Copilot Chat / Teams add-in / declarative-agent surfaces — quote the Engine's value and cite the evidence verbatim; never paraphrase confidence away or restate `insufficient_evidence` as a normal hedged answer. This is a prompt-engineering discipline that applies to *every* agent that consumes the Engine; the Engine's MCP server (§15) ships system-prompt fragments that enforce this contract.

### 10.8 Phase 1 design deliverables (small but load-bearing)

Most of the surfacing model is Phase 2+ implementation work, but a few design decisions need to be made *before* Phase 1 ships or the implementation forecloses options:

- The Insight Card visual language (the three rendering patterns above) documented as a Spaarke design-system entry — even if only one card type ships in Phase 1, the design language must be consistent across the three when they arrive.
- The `surfaceMode` field added to the question catalog schema.
- The `displayHint` rendering rules documented per supported type.
- The Refine contract — the `InsightRequest` schema's `scope` and `filter` blocks must be parameterizable by the surface and re-submittable; the resolver cache key must compose `(questionId, subject, scope, filter)` correctly.
- The Insufficient-evidence visual treatment standardized and reused across all surfaces from Day 1.

These are cheap to design now and expensive to retrofit later.

---

## 11. Sync and extraction pipelines

(Most details consolidated in §6; this section captures the implementation specifics and ADR-001 rationale.)

### 11.1 Why Functions (ADR-001 narrowed)

The original ADR-001 said "no Azure Functions" — that constraint was kept to prevent fragmenting the BFF runtime. The narrowed ADR-001 (commit `84cec9f9`, D-38) permits Functions for **narrow out-of-band integration**:

> ADR-001 narrowed: BFF endpoints MUST be Minimal API (no Functions hosting BFF endpoints); Azure Functions permitted for narrow out-of-band integration; Durable Functions still rejected.

The Engine's sync/extraction Functions fit this exactly — they are out-of-band integration with Dataverse (event-driven, can't be hosted in a BFF that doesn't accept Dataverse webhooks directly).

**Durable Functions remain rejected** (D-20). Multi-step orchestration uses Service Bus + state machine, not Durable.

### 11.2 Flex Consumption (D-17)

Per knowledge research, Microsoft's current default for ISV pattern is **Azure Functions on Flex Consumption**:
- Bicep-deployable per tenant with UAMI.
- Cold-start improvement over Y1 Consumption.
- Per-tenant capacity scaling.

Verify Flex Consumption availability in westus2 before Bicep (EXT-1 in [SPEC.md](../../projects/ai-spaarke-insights-engine-r1/SPEC.md)). Spaarke Demo environment already operates two Functions on Y1 Consumption (`spaarke-linkedin-refresh`, `spaarke-content-reminder`) — proving Functions are deployable in Spaarke; Phase 1 just standardizes on Flex.

### 11.3 No plugins, no Power Automate (D-19)

**NO Dataverse plugin assemblies**, **NO Power Automate flows** for sync integration. Plugins have the same SAS limitation as native Service Bus integration; Power Automate adds operational complexity without solving the auth problem. Webhook registration via the plugin registration tool is acceptable (that's just the registration mechanism, not custom plugin code).

### 11.4 Backfill and customer onboarding (D-39)

The `BulkReExtraction` Function is not just an admin endpoint — it is the engine of **customer onboarding** and the answer to the cold-start problem (§4.7). The original document treated this as a Phase 3 cleanup; r2 promotes the customer-onboarding workflow that *uses* it into Phase 1 / early Phase 2.

**Capabilities**:

- Bulk extraction Function with admin trigger via JWT-authorized endpoint.
- Iterates Dataverse records in priority order:
  1. Most recent closed matters first (recency-biased).
  2. Customer's most strategically important practice areas first (parameterized per tenant).
  3. Most-comparable matters for the highest-value question templates (e.g., for `predict-matter-cost`, prioritize matters with full financial detail).
- Throttle-aware: respects Dataverse RU limits, OpenAI rate limits, and the per-tenant rate-limit config (§6.3).
- Skips records already at current playbook version (`producedBy.version` check on existing Observations) — idempotent, resumable.
- Emits progress events to App Insights and to a customer-facing onboarding dashboard.
- Declares "Insights-ready" milestones as the corpus crosses minimum thresholds for question templates (the surface then promotes those questions from `insufficient_evidence` defaults to active rendering — see §10.6).

**For a customer with five years of prior matter history (~3,000 closed matters)**: backfill turns a six-month organic corpus-build into a two-week migration. Budget for $2,000–10,000 in OpenAI extraction cost per customer onboarding depending on history depth and document length. This is amortized across the customer relationship and is the single best per-dollar investment for customer-facing value.

**Pre-condition**: the customer's historical matter records must be present in the Spaarke Dataverse model. This is a separate ETL step (typically by Customer Success or a partner) — the Engine assumes it's done. The Engine's backfill operates on Dataverse and SPE content that's already in the Spaarke tenant.

---

## 12. Auth model

The Engine's auth is **the** most carefully-designed part of this architecture, because Dataverse's native Service Bus integration uses SAS-only (Microsoft documented constraint) and Spaarke's security standard forbids SAS keys in production. The full auth model is in [`decisions.md`](../../projects/ai-spaarke-insights-engine-r1/decisions.md) D-22 through D-28. Summary:

### 12.1 Trust boundary

`DataverseWebhookIntake` is the **only entry point that accepts external traffic** (D-22). Everything past the intake is internal, authenticated by Managed Identity + Azure RBAC.

### 12.2 Per-hop auth at target state (post Phase C #044)

| Hop | Auth | Secret type |
|---|---|---|
| Dataverse → Intake Function | HMAC-SHA256 signature (Phase C #044) | None at target state |
| Intake Function → Service Bus | Azure RBAC via UAMI | None |
| Service Bus → consumer Functions | Azure RBAC via UAMI | None |
| Consumer Functions → Dataverse (re-fetch) | Azure RBAC via UAMI (Phase C #042) | None |
| Consumer Functions → AI Search | Azure RBAC via UAMI | None |
| Consumer Functions → Cosmos | Azure RBAC via UAMI | None |

**Net at target**: zero SAS keys and zero long-lived secrets anywhere in the pipeline.

### 12.3 Transitional state (until Phase C #044 lands)

Same as target except Dataverse → Intake Function uses **`clientState`** (shared secret in header) — a single transitional secret in Key Vault, programmatically rotated. The Engine ships initially with `clientState`; HMAC drops in when Phase C #044 lands. Validation code is **copied from BFF webhook handlers** — not reimplemented (D-23).

### 12.4 Inbound JWT on non-webhook Function endpoints (D-25, D-28)

Admin / management / status endpoints on the Function App require JWT. Use **`Microsoft.Identity.Web.AddMicrosoftIdentityWebApi`** — the canonical .NET inbound JWT validator. Reference implementation: [`Sprk.Bff.Api/Program.cs`](../../src/server/api/Sprk.Bff.Api/Program.cs).

**Do NOT** use `@spaarke/auth` for inbound validation — `@spaarke/auth` is **client-side TypeScript only** (`src/client/shared/Spaarke.Auth/`), uses MSAL.js to acquire tokens for outbound calls; does NOT validate inbound tokens.

**Do NOT** call out to a separate "auth service" for JWT validation (D-28). Validation is a local crypto check; `Microsoft.Identity.Web` validates `iss`, `aud`, `tid`, signature, expiry locally with cached AAD metadata. An extra hop adds latency with no security benefit.

### 12.5 TenantId — explicit GUID, never `common` (D-26)

`AzureAd:TenantId` is the explicit tenant GUID for the per-tenant deployment. **Never** `common` or `organizations`. This is the per-tenant threat model (Spaarke convention D-AUTH-5) and is coordinated with Phase C task #047 which fixes the template that still says `common`.

### 12.6 Outbound — `DefaultAzureCredential` only (D-27)

All outbound calls use `DefaultAzureCredential`. **No `ClientSecretCredential` in new Functions**. Aligns with Phase C tasks #041 (Graph outbound) and #042 (Dataverse outbound). New Functions inherit the managed-identity-only discipline from day one.

In Bicep, the Function's UAMI gets role assignments to each target resource (Service Bus Data Sender/Receiver, AI Search Index Data Contributor, Cosmos DB Data Contributor, Dataverse role assignment). Locally, `DefaultAzureCredential` falls back to Azure CLI / Visual Studio credentials.

### 12.7 Coordination with Phase C (auth work in flight)

| Phase C task | Coordination | Phase 1 impact |
|---|---|---|
| #041 — Managed identity for Graph outbound | Engine Functions inherit `DefaultAzureCredential` discipline | New Functions use MI from day one |
| #042 — Managed identity for Dataverse outbound | Same | Re-fetch in `InsightsSyncFunction` uses `DefaultAzureCredential` |
| #044 — HMAC-SHA256 webhook signature validation | Ship Phase 1 with `clientState`; HMAC drops in when #044 lands | Copy validator code; do not fork |
| #047 — Non-`common` `TenantId` in template | Insights Engine adopts the fix from day one | Per-tenant explicit TenantId in every Bicep deployment |
| `.claude/AUDIT-FINDINGS-AUTH-SYSTEM.md` | Pre-v2 audit doc; canonical auth ADR is now [ADR-028](../../.claude/adr/ADR-028-spaarke-auth-architecture.md) | Engine auth design must be consistent with this canon |

---

## 13. Privilege model

### 13.1 Two principles

**Principle 1 — Substrate stores everything; queries trim at execution.** The Insight Index and Graph contain the union of all accessible data for the tenant. Every query layer applies the user's `accessibleMatterSet` filter at execution time. This is slower than per-user pre-materialized views but is correct under access changes (granting access doesn't require rebuilding views) — D-32.

**Principle 2 — Counts vs IDs distinction** (D-34). It is acceptable to say *"Acme Corp has appeared in 8 of your firm's prior matters"* even when the user can only access 3 of them — provided "click to see them" only returns the 3. This distinguishes:
- **Aggregate Insights** (counts, ranges, distributions): trim post-aggregation
- **Specific Insights** (linked matter IDs, document refs): trim pre-aggregation

The artifact envelope's `value.displayHint` signals which category an artifact is in.

### 13.2 Per-tenant isolation (physical)

**Decision (D-31)**: Physical per-tenant isolation. Legal data privilege boundaries are physical, not just logical.

| Resource | Per-tenant deployment |
|---|---|
| AI Search service | Per-tenant service (preferred for legal clients) OR per-tenant indexes in a shared service (acceptable with strict `tenantId` preFilter) |
| Cosmos account | Per-tenant account (clean partition + cost attribution) |
| Function App | Per-tenant app with per-tenant UAMI |
| Service Bus topic | Per-tenant topic |
| App Insights | Shared with workspace-based separation; correlation IDs include `tenantId` |

Cross-tenant data leakage is structurally impossible because there is no path between the resources.

### 13.3 In-tenant access trimming

Within a tenant, users see different matter subsets. The `accessibleMatterSet` is sourced from the existing access control system and cached per session.

**In AI Search queries** — every query includes:

```
filter: tenantId eq '{tenantId}' and 
        search.in(scope_matterId, '{accessibleMatterCsv}', ',')
```

With `vectorFilterMode=preFilter` for vector queries (D-33).

**In graph traversals** — every traversal includes:

```
matterFilter: accessibleMatterSet
unaccessibleHandling: 'redactReference' | 'omit' | 'countOnly'
```

`countOnly` lets aggregate queries return counts without leaking IDs.

**In Inference synthesis** — all tools receive `accessibleMatterSet` as part of `ToolExecutionContext`. Tools fail-safe: if a tool cannot trim correctly, it returns an error rather than potentially over-disclosing.

### 13.4 Cross-matter pivot rule (D-35)

When a user pivots Matter context mid-session (Matter A → Matter B), retrieved evidence about Matter A is stripped from conversation history. Extension of existing Spaarke rule for documents to graph evidence:

- Specific entity refs from prior matter: cleared.
- Aggregate conclusions: retained.
- User notified: *"Switching matter context — prior matter details cleared from context."*

### 13.5 Auditability

Every Insight request and response is logged to the existing audit log (append-only Cosmos with immutable policy):

- User ID, tenant ID
- Question asked (sanitized)
- Artifacts returned (IDs only, not values)
- Evidence cited (refs)
- Confidence + sufficiency result

This is a compliance artifact, not a debugging tool.

---

## 14. Evaluation and quality measurement

The honesty contract (D-04, D-06) is enforced structurally — by the envelope, the per-question sufficiency rules, and the insufficient-evidence response shape. But *structural* honesty does not guarantee *quality*. A well-formed `insufficient_evidence` response that fires when there's actually enough evidence is dishonest by omission. A confident Inference whose `evidence[]` doesn't actually support the value is dishonest by misalignment. The Engine needs measurement, not just structure.

This section is the answer to "how do we know the Engine is working, not just running."

### 14.1 The RAG triad as the measurement framework (D-40)

The Engine adopts the canonical RAG-triad evaluators that have become the Microsoft Foundry / Azure AI Evaluators standard:

| Metric | What it measures | What "good" looks like |
|---|---|---|
| **Retrieval quality** | Did the right comparables get pulled? Are the evidence pointers actually relevant to the question? | Top-K retrieved matters overlap with expert-curated "should-have-found" set ≥ 80% |
| **Groundedness** | Is every claim in the Inference value/reasoning supported by something in `evidence[]`? | 100% — claims with no grounding fail the safety verifier (§9.4) and don't ship; this becomes a regression gate |
| **Relevance** | Does the response actually answer the question asked? (Not "is it true," but "is it on-topic and useful?") | Domain-expert pass rate ≥ 90% on the golden dataset |

These three together cover the failure modes that the structural honesty contract can't catch: retrieving the wrong evidence, confabulating beyond the evidence, and answering a different question than was asked.

### 14.2 The golden dataset (D-40)

50–100 curated `(question, matter-context, expected-answer, expected-evidence, expected-confidence-band)` tuples. Authored by domain experts; reviewed by the Engine team. Each tuple specifies:

- **Question** — from the catalog (questionId + parameters)
- **Matter context** — the subject matter ID and any scope overrides
- **Expected answer range** — for numeric Inferences, an acceptable band (e.g., predicted cost in `$240K–$320K` is a pass; outside is a miss)
- **Expected confidence band** — High / Medium / Low
- **Expected evidence pointers** — which matters / Observations / Facts should appear in `evidence[]` (subset constraint: any of these missing is a miss; extras are tolerable)
- **Expected reasoning fragments** — key phrases that should appear in the rendered reasoning (substring match)
- **Insufficient-evidence cases** — explicit tuples where the corpus is configured to be under-evidenced, and the test passes only if the response is `insufficient_evidence` with the right gap statement

Stored in `tests/Insights/golden/` as JSON files, one per question template. Source-controlled with the codebase; reviewed on PRs that change Engine behavior.

### 14.3 The synthetic corpus as evaluation substrate

The synthetic corpus (§4.9) is loaded into a dedicated eval environment before each evaluation run. Properties:

- **Deterministic** — same seed produces the same corpus.
- **Configurable coverage** — the synthetic corpus can be generated with deliberate gaps (e.g., "only 8 IP licensing matters, all from 2023") to trigger insufficient-evidence test cases.
- **Refreshable** — quarterly regeneration with current distributions to avoid the eval corpus drifting from realistic data shapes.

### 14.4 The evaluation harness — CI integration (D-40)

Phase 1 ships with the eval harness wired into CI:

```
on: pull_request (changes to Sprk.Bff.Api/Services/Insights/** or playbooks/closure-extraction*.json)
  1. Spin up eval environment (Bicep deploys with synthetic corpus loaded).
  2. Run golden dataset through POST /api/insights/ask.
  3. Compare actual responses against expected per the rules in §14.2.
  4. Emit metrics: retrieval-overlap %, groundedness-fail count, relevance-pass %, insufficient-evidence-correctness %.
  5. Fail the build if any of:
     - Groundedness-fail count > 0 (zero tolerance — any claim ungrounded is a regression)
     - Retrieval-overlap < 75% (below threshold means tool retrieval has degraded)
     - Relevance-pass < 85% (below threshold means synthesis has degraded)
     - Insufficient-evidence cases where the response is anything other than insufficient_evidence (zero tolerance)
  6. Publish metrics to App Insights for trend analysis.
```

These thresholds are aspirational for Phase 1 (the actual numbers will be calibrated against the first runs of the synthetic-on-synthetic baseline) but the gate-shape is fixed: groundedness regressions and false-confidence regressions block the build.

### 14.5 Production observability — measuring honesty in the wild

The eval harness measures pre-shipment quality on synthetic data. Production observability measures actual customer-facing quality:

- **Insufficient-evidence rate per question** — high rate means the corpus is gappy in that question; low rate with low confidence means the Engine is over-firing.
- **Confidence band distribution per question** — bias toward Low confidence means the question's sufficiency rule may be too strict.
- **Re-fire rate after Refine** — when users Refine an Inference, what fraction of refines lead to a meaningfully different answer? Low rate means the refinement is cosmetic; high rate means scope/filter changes are doing real work.
- **Surface engagement on evidence panel** — what fraction of users open the evidence drawer after seeing an Inference? Low rate may indicate the evidence is being trusted blindly (good or bad depending on baseline).
- **Notification action rate** — when the Engine pushes a new Insight notification, what fraction of users act on it within 24 hours? Proxy for "Insights actually useful."

All emitted to App Insights with `tenantId`, `userId` (hashed), `questionId`, and confidence-band tags. Dashboards in production environments. Aggregated review monthly by the Engine team and surfaced to customer success for tenant-specific review.

### 14.6 What the eval harness does NOT do

To be honest about boundaries:

- The eval harness does not validate that Inference answers are *true in the world*. It validates that they are well-formed, grounded in their evidence, and on-topic. Truth in the world is a customer-validation activity, not a CI gate — but groundedness and the insufficient-evidence pattern *together* are a strong proxy for "the system is not making things up."
- It does not catch regressions in surface rendering — UI behavior is a separate test surface (Playwright / Cypress on the surfaces in §10).
- It does not measure latency directly — that is App Insights territory; the eval harness has a soft budget (synthetic-corpus eval should complete in ≤ 15 minutes) but does not fail on latency.

### 14.7 Evaluation as a Phase 1 deliverable, not a nice-to-have

The original document's Phase 1 acceptance criteria included "ADR compliance verified" but not "groundedness rate above X% on golden dataset, insufficient-evidence response triggered correctly on Y% of under-evidenced questions." This revision adds explicit evaluation acceptance criteria to Phase 1 (§21.2). Without measurement, the honesty contract is a promise without enforcement. With measurement, it's an enforced property of the system. This is the single most important addition for making the marketing claim defensible to enterprise legal buyers, who will (correctly) ask "how do you know?"

---

## 15. External integration via MCP

The Engine's V1 architecture answers a question the original document didn't address: **how do M365 Copilot, Copilot Studio agents, Microsoft Agent Framework workflows, and (eventually) Foundry agents consume Spaarke intelligence?** The answer is *not* "the Engine calls Microsoft's retrieval surface to ground its own answers" — that integration shape doesn't fit (the Engine produces typed structured records and per-question sufficiency rules, not document chunks; see §22.1). The answer is the inversion: **the Engine exposes its question catalog as an MCP (Model Context Protocol) server, and Microsoft's tools consume it as a peer.**

This is the integration posture that matches the architecture without distorting it.

### 15.1 The MCP server — `Sprk.Insights.Mcp` (D-41)

A new MCP server component, co-hosted with the BFF (or independently deployed once §22.2 extraction lands), exposing the Engine's question catalog as MCP-callable tools. Auth is OBO — the calling agent presents the user's token; the MCP server validates it and forwards user context to the Engine; the existing `accessibleMatterSet` trimming enforces what the user can see regardless of which agent is calling.

**Tools** — the question catalog, surfaced as MCP tool definitions:

| MCP Tool | Mapped Engine question | Returns |
|---|---|---|
| `predict_matter_cost(matter_id, scope_overrides?)` | `predict-matter-cost` | InsightResponse with Inference or insufficient_evidence |
| `find_comparable_matters(matter_id, filters?)` | `find-comparables` | List of comparable matters with similarity scores |
| `assess_matter_risks(matter_id)` | `assess-risks` | List of Observations + insufficiency flags per risk type |
| `summarize_matter_closure(matter_id)` | `summarize-closure` | Aggregated closure-extraction Observations |
| `get_matter_facts(matter_ids, predicates?)` | Direct LiveFactResolverService call | Map of `matterId → {factPredicate → value}` |
| `analyze_spend_trends(scope, period)` | `analyze-spend` | Aggregate spend Observations with confidence |

(The full list grows with the question catalog. Each new question template that's safe to expose externally gets a corresponding MCP tool.)

**Resources** — read-only views:

- `insight://matter/{matter_id}` — current Insights for a matter (returns the same payload as the Glance surface)
- `insight://question-catalog` — list of available questions with parameters and descriptions (lets calling agents introspect capabilities)
- `insight://playbook/{playbook_id}` — playbook metadata for citation purposes

**Prompts** — pre-built system-prompt fragments that teach calling agents how to handle the Engine's response shape:

- `insight-rendering-rules` — the honesty-contract enforcement prompt that agents inject into their system prompt: "quote confidence values verbatim; never paraphrase Insufficient-evidence as a hedged answer; cite evidence by matter ID; never invent matter IDs not in the evidence array."
- `insight-question-routing` — heuristics for which MCP tool to call given a user's natural-language request.

### 15.2 Why MCP, why now

MCP is Microsoft's chosen Agent-to-Agent protocol. Copilot Studio supports MCP. Microsoft Agent Framework supports MCP. Foundry IQ Knowledge Sources will support MCP (currently private preview as of late 2025; expected GA mid-to-late 2026). M365 Copilot's tool inventory consumes MCP through the pro-code declarative agent pattern.

Building the Engine as an MCP server in Phase 1 (even with limited tool exposure initially) means:

- **One source of truth for Engine capabilities**. The MCP server is the canonical tool inventory; all external consumers route through it; there is no separate "Copilot adapter" or "Teams adapter" to maintain.
- **Honesty contract survives all consumption paths**. Every agent that calls Engine tools receives the structured envelope and the renderer prompt fragments; the contract is enforced at the protocol boundary, not at each integration.
- **Foundry IQ optionality preserved without commitment**. When the Foundry IQ MCP Knowledge Source path goes GA, the Engine can be added as a knowledge source for cross-agent retrieval workflows — without changing Engine code.
- **Microsoft Agent Framework workflows** can compose Engine tools alongside Word Copilot, Outlook, and other MCP tools — the Engine becomes part of the Microsoft agent ecosystem as a peer.

### 15.3 What the MCP server does NOT do

- It does NOT bypass the honesty contract for chatty agent consumption. Inference responses returned via MCP are the same envelope as direct API responses; agents that ignore the rendering prompts and paraphrase will produce dishonest output — that's a calling-agent design problem, not an Engine problem.
- It does NOT replace the direct BFF API for Spaarke's own surfaces. The Context pane, Insight Cards, AiToolAgent PCF, and other in-app surfaces continue calling `POST /api/insights/ask` directly. MCP is for external consumers; the direct API is for the home application.
- It does NOT expose write operations in V1. No "save this Inference," no "register a feedback rating," no mutation. Read-only inference and reference. Write capabilities are evaluated post-V1 once the audit and authorization model around agent-initiated writes is hardened (§22.4).

### 15.4 Phase 1 design vs Phase 2 implementation

**Phase 1 design deliverables (small)**:

- The MCP server contract document — tool signatures, resource URIs, prompt fragments, auth model. Drafted alongside the question catalog so the two evolve together.
- The decision on hosting (co-hosted with BFF initially per ADR-001 spirit; extract to `Sprk.Insights.Mcp.Api` if and when BFF decomposition happens per §22.2).
- The OBO auth flow for MCP — validated against the existing BFF auth model.

**Phase 2 implementation deliverables**:

- MCP server with the four highest-value tools live (`predict_matter_cost`, `find_comparable_matters`, `assess_matter_risks`, `summarize_matter_closure`).
- The pro-code declarative agent for M365 Copilot wired through the MCP server.
- Copilot Studio "Legal Spend Analyst" agent template demonstrating composition of MCP tools.

**Phase 3+ deliverables**:

- Foundry IQ MCP Knowledge Source integration (when the path goes GA).
- Microsoft Agent Framework workflow templates.
- Outlook/Teams add-in cards consuming MCP via NAA.

### 15.5 The strategic posture

The Engine doesn't compete with Foundry IQ, Microsoft Agent Framework, or Copilot — it complements them. The competitive moat isn't *building agentic retrieval* (Microsoft is doing that better and at scale than any third party can); it is *owning the typed organizational inference layer that those tools can call.* The MCP server is how that ownership becomes consumable.

When a customer asks "how does Spaarke fit into our M365 Copilot strategy?" the answer is: "Copilot calls Spaarke's MCP tools when the user asks anything about matters, spend, risk, or comparables. The intelligence comes from the Spaarke Engine, with full provenance and the honesty contract intact. The conversational UX comes from Copilot." That's the durable position.

---

## 16. Azure resources

This section consolidates the Engine's resource footprint per environment. Authoritative source: [`projects/ai-spaarke-insights-engine-r1/azure-inventory.md`](../../projects/ai-spaarke-insights-engine-r1/azure-inventory.md). All names below follow the Spaarke naming convention (see [AZURE-RESOURCE-NAMING-CONVENTION.md](AZURE-RESOURCE-NAMING-CONVENTION.md)).

### 16.1 New resources (provisioned by the Engine's Bicep)

| Resource | Type | Per-tenant SKU recommendation | Bicep module |
|---|---|---|---|
| **Cosmos DB account (NoSQL/SQL API)** | `Microsoft.DocumentDB/databaseAccounts` | Serverless or autoscale 400–4000 RU/s (start serverless for Phase 1; promote to autoscale at ~1M vertices) | `cosmos-graph.bicep` |
| **Cosmos DB database** | `Microsoft.DocumentDB/databaseAccounts/sqlDatabases` | — | `cosmos-graph.bicep` |
| **Cosmos DB container** | `Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers` | Partition key: `/tenantId`; indexing policy excludes `embedding` field from default index path | `cosmos-graph.bicep` |
| **Function App (Flex Consumption)** | `Microsoft.Web/sites` (kind: functionapp,linux) | Flex Consumption plan; per-tenant scale; .NET 8 isolated | `functions.bicep` |
| **Function App Hosting Plan** | `Microsoft.Web/serverfarms` | Flex Consumption SKU | `functions.bicep` |
| **AI Search indexes (×4)** | `Microsoft.Search/searchServices/indexes` (deployed via deployment script — Management API, not Bicep-native for index schema) | `insight-matters`, `insight-decisions`, `insight-risks`, `insight-sessions` — JSON schemas in `infra/insights/schemas/` | `search-indexes.bicep` + deployment script |
| **Service Bus topic** | `Microsoft.ServiceBus/namespaces/topics` | `spaarke-dataverse-changes-{tenant}` on existing or new namespace (Standard SKU minimum — topics + sessions required) | `servicebus-topic.bicep` |
| **Service Bus subscriptions** | `Microsoft.ServiceBus/namespaces/topics/subscriptions` | `insights-sync`, plus per-consumer subscriptions | `servicebus-topic.bicep` |
| **Service Bus topic (milestones)** | `Microsoft.ServiceBus/namespaces/topics` | `spaarke-matter-milestones-{tenant}` — subscriber: `ClosureExtractionTrigger` | `servicebus-topic.bicep` |
| **User-Assigned Managed Identity** | `Microsoft.ManagedIdentity/userAssignedIdentities` | Per-tenant UAMI for Function App | `managed-identity.bicep` |
| **UAMI role assignments** | `Microsoft.Authorization/roleAssignments` | Service Bus Data Sender/Receiver, AI Search Index Data Contributor, Cosmos DB Data Contributor, Dataverse role, Key Vault Secrets User | `managed-identity.bicep` |
| **Key Vault secret references** | `Microsoft.KeyVault/vaults/secrets` (existing KV) | `clientState` (transitional, until Phase C #044), App Insights connection string (if not reusing) | `keyvault-secrets.bicep` |
| **App Insights connection** | (uses existing App Insights workspace) | Per-tenant correlation IDs via tags | `monitoring.bicep` |

### 16.2 Existing resources the Engine reuses

| Resource | Type | Used for |
|---|---|---|
| `spaarke-search-{env}` | AI Search (standard tier) | Hosts the 4 new `insight-*` indexes alongside existing `spaarke-records-index`, `spaarke-rag-references`, knowledge/discovery indexes |
| `spaarke-openai-{env}` | Azure OpenAI / AIServices | Embedding (`text-embedding-3-large`, 3072 dim — verify deployment exists per EXT-2), LLM synthesis |
| `spe-redis-{env}` / `spaarke-demo-prod-cache` / `spaarke-demo-cache` | Redis | Two-tier memory, per-question TTL cache, Live Fact 5-min cache |
| `spaarke-{demo|prod}-sbus` | Service Bus Standard | Hosts new topics (per-tenant) |
| `sprk-{env}-kv` / `sprk-demo-kv` | Key Vault | Transitional `clientState`, future webhook signing keys, any other secrets |
| `spe-insights-{env}` / `sprk-{env}-insights` | App Insights | Correlated tracing, drift alerts, auth failure alerts |
| `spe-logs-{env}` / `sprk-{env}-logs` | Log Analytics | Backing workspace for App Insights |
| `spaarke-bff-{env}` | App Service | Hosts `InsightsResolverService`, Insights Agent, Live Facts (no new App Service needed — Engine resolver runs in existing BFF) |
| `mi-bff-api-{env}` | User-Assigned Managed Identity | BFF identity (existing) — gets additional role assignments for new Insight indexes and Cosmos |

### 16.3 Per-environment current state

Per [`azure-inventory.md`](../../projects/ai-spaarke-insights-engine-r1/azure-inventory.md):

| Environment | Subscription | RG | BFF Plan | Search Replicas | Cosmos | Has Functions? |
|---|---|---|---|---|---|---|
| **Dev** | Spaarke Dev | `spe-infrastructure-westus2` | **P2v3** (⚠️ larger than prod) | 2 (⚠️ wasteful for non-HA dev) | `spe-cosmos-dev-ai` (NoSQL — but for AI work history, not graph) | None |
| **Demo** | Spaarke Demo | `rg-spaarke-demo` | B1 | 1 | None | Yes — 2 Functions on Y1 Consumption (proves the pattern) |
| **Prod** | Spaarke Dev (prod RG in dev sub) | `rg-spaarke-platform-prod` | P1v3 | 2 (HA-appropriate) | None | None |
| **demo-prod** (intermediate?) | Spaarke Dev | `rg-spaarke-demo-prod` | None | None | None | None — purpose TBD (open question in inventory) |

**Cost-savings flags from inventory** (not Engine-specific but relevant context):
- Dev App Service Plan P2v3 → P1v3: ~$140/mo savings, low risk.
- Dev AI Search 2 replicas → 1 replica: ~$125/mo savings, no HA needed in dev.
- Foundry hub (`sprkspaarkedev-aif-hub` + supporting resources) utilization audit — may be reclaimable.

### 16.4 Out-of-scope subscriptions (per project owner)

- **SPRK Power Platform 1** — not relevant to Insights Engine.
- **Spaarke Legal Rules Solution** — not relevant to Insights Engine.

### 16.5 Region considerations

All Engine resources deploy to **westus2** (matches existing dev/prod RGs). OpenAI accounts may be in westus3 (current placement of `spaarke-openai-prod`); cross-region calls are acceptable for OpenAI from westus2 compute. Verify Flex Consumption availability in westus2 before Bicep (EXT-1).

---

## 17. Other resources and dependencies

### 17.1 Schema and code artifacts (in repo, deployed)

| Artifact | Path | Purpose |
|---|---|---|
| AI Search index schemas | `infra/insights/schemas/insight-*.index.json` | Declarative JSON schemas; deployed via deployment script calling Management API |
| Bicep modules | `infra/insights/modules/*.bicep` | Per-resource deployment templates |
| Bicep parameter files | `infra/insights/parameters/tenant-*.json` | Per-tenant configuration (tenantId, region, SKU choices) |
| Closure-extraction JPS playbook | `playbooks/closure-extraction-v1.json` (location TBD — JPS storage convention) | The playbook that produces Observations from matter closure milestones |
| `InsightArtifact` POCOs | `Sprk.Bff.Api/Models/Insights/*.cs` | C# envelope types (Fact / Observation / Inference) |
| Question catalog | `Sprk.Bff.Api/Services/Insights/Questions/catalog.json` (or Dataverse-backed) | Per-question evidence-sufficiency rules |
| Smoke tests | `tests/Insights/*` | Unit (envelope serialization, `IInsightGraph` contracts, `LiveFactResolverService` against mocked Dataverse), integration (`InsightsResolverService` end-to-end with mock data), Bicep verification (round-trip schema deploy + read) |

### 17.2 External dependencies

| Dependency | Source | Purpose |
|---|---|---|
| `Microsoft.Identity.Web` | NuGet | Inbound JWT validation on Function App non-webhook endpoints |
| `Azure.Identity` (`DefaultAzureCredential`) | NuGet | Outbound auth (Graph, Dataverse, Service Bus, Cosmos, AI Search) |
| `Microsoft.Azure.Cosmos` | NuGet | Cosmos NoSQL client (graph implementation) |
| `Azure.Messaging.ServiceBus` | NuGet | Service Bus client (Functions + intake) |
| `Azure.Search.Documents` | NuGet | AI Search client (existing; reuse) |
| `Microsoft.Extensions.AI` | NuGet | `IChatClient`, `UseFunctionInvocation` (existing) |
| Microsoft Dataverse SDK | NuGet | Re-fetch and Live Facts (existing `IDataverseService` wraps) |
| Dataverse plugin registration tool | Microsoft tool | Webhook registration (NOT a custom plugin assembly — D-19) |

### 17.3 Knowledge base (researcher-authored, 2026-05-19)

These curated knowledge documents informed the Engine's design and are referenced from `decisions.md`. The `researcher` subagent (`.claude/agents/researcher.md`) consults them first before falling back to web search.

| Knowledge area | Location | Relevance |
|---|---|---|
| Azure AI Search (vector search, integrated vectorization, security trimming) | [`knowledge/azure-ai-search/`](../../knowledge/azure-ai-search/) (with `insights-engine-supplement.md`) | Substrate decisions §8.1; `vectorFilterMode=preFilter` rule |
| Cosmos Gremlin strategic signals | [`knowledge/cosmos-gremlin/`](../../knowledge/cosmos-gremlin/) | Supports D-09 (NoSQL not Gremlin) |
| Azure Functions ISV patterns | [`knowledge/azure-functions-isv/`](../../knowledge/azure-functions-isv/) | Flex Consumption + per-tenant UAMI patterns |
| Dataverse sync patterns | [`knowledge/dataverse-sync/`](../../knowledge/dataverse-sync/) | Service Bus + Timer pattern; webhook payload caveats |
| Foundry memory patterns | [`knowledge/foundry-memory-patterns/`](../../knowledge/foundry-memory-patterns/) | Two-tier memory pattern reference |

### 17.4 Internal cross-references (architecture docs)

| Architecture doc | What the Engine borrows |
|---|---|
| [AI-ARCHITECTURE.md](AI-ARCHITECTURE.md) | Four-tier framework; tool framework details; the Engine extends this with the Insight Index and graph layers |
| [playbook-architecture.md](playbook-architecture.md) | JPS playbook system; closure-extraction is a JPS playbook |
| [chat-architecture.md](chat-architecture.md) | `IChatClient` + UseFunctionInvocation pipeline; Engine reuses without managing chat session history |
| [rag-architecture.md](rag-architecture.md) | Hybrid retrieval primitives; Engine composes RagService |
| [scope-architecture.md](scope-architecture.md) | Scope resolution; Engine's `scope_*` fields in the artifact envelope follow the same vocabulary |
| [background-workers-architecture.md](background-workers-architecture.md) | Where `PlaybookIndexingBackgroundService` lives today; Engine's Functions may absorb it in Phase 3 |
| [jobs-architecture.md](jobs-architecture.md) | Service Bus job pattern; sync pipeline is conceptually similar but on its own topic |
| [caching-architecture.md](caching-architecture.md) | Redis-first caching, TTL tiers, fail-open pattern; Engine applies these to two-tier memory + per-question TTL |
| [resilience-architecture.md](resilience-architecture.md) | Circuit breakers + retry policies; Engine's resilient AI Search and Cosmos calls follow these |
| [auth-security-boundaries.md](auth-security-boundaries.md) | Trust zones; Intake Function = boundary; details in §12 |
| [AUTH-AND-BFF-URL-PATTERN.md](AUTH-AND-BFF-URL-PATTERN.md) | Inbound JWT pattern; mirrored on Function App non-webhook endpoints |
| [multi-environment-portability-strategy.md](multi-environment-portability-strategy.md) | Alternate keys, option sets, environment variables; per-tenant parameter files follow this |
| [INFRASTRUCTURE-PACKAGING-STRATEGY.md](INFRASTRUCTURE-PACKAGING-STRATEGY.md) | Bicep module organization; Engine's `infra/insights/` follows the convention |

### 17.5 Internal cross-references (ADRs)

| ADR | Relevance |
|---|---|
| [ADR-001](../adr/ADR-001-minimal-api-and-workers.md) | BFF runtime + narrowed Functions permission (commit `84cec9f9`) |
| [ADR-008](../adr/ADR-008-endpoint-filter-authorization.md) | Endpoint filter authorization on `POST /api/insights/ask` |
| [ADR-009](../adr/ADR-009-redis-first-caching.md) | Redis-first caching applied to two-tier memory and per-question TTL |
| [ADR-010](../adr/ADR-010-di-minimalism.md) | DI minimalism; `InsightsModule.cs` follows the registration-audit pattern |
| [ADR-013](../adr/ADR-013-ai-architecture.md) | AI architecture; Engine is an extension of this |
| [ADR-016](../adr/ADR-016-ai-cost-rate-limit-and-backpressure.md) | Rate limiting on Insight endpoints |
| [ADR-019](../adr/ADR-019-problemdetails.md) | ProblemDetails error envelope |
| [ADR-028](../../.claude/adr/ADR-028-spaarke-auth-architecture.md) | Canonical Spaarke Auth v2 architecture (accepted 2026-05-19); Engine auth design must remain consistent |

### 17.6 Other Spaarke projects coordinating with this Engine

| Project | Coordination |
|---|---|
| **Spaarke auth Phase C** (in flight) | Tasks #041, #042, #044, #047 — see §12.7 |
| **Spaarke AI Platform Unification (r2)** | The design that surfaced the need for this Engine ([`design.md`](../../projects/spaarke-ai-platform-unification-r2/design.md)) |
| **Unified access control project** | Open question DEP-3 / O-02: does `accessibleMatterSet` come from this project or do we maintain our own source? |

---

## 18. Packagability and deployment

### 18.1 Per-tenant deployment unit

Everything is deployable per tenant via Bicep. The deployment unit includes:

- AI Search indexes (provisioned + initial schema)
- Cosmos account (Insight Graph)
- Function App (with all functions: sync, reconciliation, extraction, re-indexing)
- Service Bus topic + subscriptions
- Per-tenant UAMI
- Key Vault references for secrets
- App Insights connection
- Bicep parameters per tenant (tenant ID, region, search tier, Cosmos throughput, etc.)

### 18.2 Bicep module layout

```
infra/insights/
├── main.bicep                         entry — composes modules
├── parameters/
│   ├── tenant-acme.dev.json
│   ├── tenant-acme.prod.json
│   ├── tenant-beta.dev.json
│   └── …
├── modules/
│   ├── search-indexes.bicep           AI Search indexes via deployment script
│   ├── cosmos-graph.bicep             Cosmos account + database + container
│   ├── functions.bicep                Function App (Flex Consumption)
│   ├── servicebus-topic.bicep         Sync topic + subscriptions
│   ├── managed-identity.bicep         UAMI + role assignments
│   ├── keyvault-secrets.bicep         Secret references
│   └── monitoring.bicep               App Insights connection
└── schemas/
    ├── insight-matters.index.json
    ├── insight-decisions.index.json
    ├── insight-risks.index.json
    └── insight-sessions.index.json
```

### 18.3 Deployment flow

```
1. Validate Bicep parameters (tenant ID, region availability)
2. Provision resources (AI Search service if not shared, Cosmos, Functions, SBus)
3. Deploy AI Search indexes from schema JSONs (deployment script — Management API)
4. Set up Cosmos containers + indexing policies
5. Configure Function App settings + secrets via Key Vault refs
6. Set up Service Bus topic + subscriptions; assign UAMI roles
7. Run initial backfill (admin endpoint trigger)
8. Verify: smoke-test endpoints (/healthz/insights), assert indexes exist with expected schema
```

### 18.4 Multi-tenant control plane

For r1 with a small number of tenants, **tenant-list-as-configuration** (D-37) — each tenant has a Bicep parameters file; CI deploys when files change.

Per knowledge research, evolve to a **tenant-list-as-data control plane** around ~10 tenants (`DEF-05`). Design TBD; not Phase 1.

### 18.5 Per-environment within a tenant

Within a tenant, you still have dev / staging / prod. Bicep parameter file naming captures both axes: `tenant-acme.dev.json`, `tenant-acme.prod.json`. The Bicep is identical; only parameters differ.

---

## 19. Design decisions

The Engine's decisions are formally tracked in [`projects/ai-spaarke-insights-engine-r1/decisions.md`](../../projects/ai-spaarke-insights-engine-r1/decisions.md) (38 numbered decisions). Highlights below; cite the `D-NN` ID when referencing.

### 19.1 Identity and boundary

| Decision | Choice | Rationale | ADR |
|---|---|---|---|
| Component name | "Spaarke Insights Engine" (internal: `Insights Engine`, code: `Insights*`) — D-01 | "Engine" honestly scopes: signals in, context out. Distinct from IQ Stack (marketing umbrella). | — |
| Boundary | Engine **owns** the resolver + synthesis + sync/extraction Functions + Insight Index + Insight Graph. Source systems are **consumed but not owned**; presentation surfaces are **emitted to but not owned** — D-02 | Single responsibility; replaceable; independently deployable. | — |

### 19.2 Conceptual model

| Decision | Choice | Rationale | ADR |
|---|---|---|---|
| Artifact taxonomy | Three-type: Fact / Observation / Inference — D-03 | Different trust profiles, stores, APIs, presentation rules. Mixing them is what makes "intelligence" silently dishonest. | — |
| Provenance | API contract: every Observation/Inference carries `evidence[]`. Surfaces that can't render provenance can't display Inferences — D-04 | Architectural mechanism preventing dishonesty by construction. | — |
| Observation versioning | `producedBy.version` mandatory on every Observation — D-05 | Enables selective re-extraction when extraction playbooks ship v2. | — |
| Evidence sufficiency | Per-question rule in catalog; insufficient → structured `insufficient_evidence` response — D-06 | Architectural enforcement of honesty contract. | — |

### 19.3 Substrate

| Decision | Choice | Rationale | ADR |
|---|---|---|---|
| Vector + structured retrieval | Azure AI Search (existing service) with new `insight-*` indexes — D-07 | Already in stack. Mirrors proven `spaarke-rag-references` pattern. | — |
| Embedding model | `text-embedding-3-large` (3072 dim) — D-08 | Recall on "comparable matters" is critical; cost differential not material at our scale. | — |
| Graph | Cosmos NoSQL with adjacency-list documents — D-09 | Traversals are 2–3 hops with filters; vector co-location is a real benefit; MS strategic direction. | — |
| Graph abstraction | `IInsightGraph` exposes named traversals — D-10 | Preserves swap path. | — |
| Live Facts | Direct Dataverse queries; expensive aggregates materialized in `insight-matters` — D-11 | Cheap, fresh; fast retrieval for expensive aggregates. | — |
| `tenantId` first-class on every new index | D-12 | Existing `spaarke-records-index` omits this (acknowledged gap); MUST NOT repeat. | — |

### 19.4 Synthesis

| Decision | Choice | Rationale | ADR |
|---|---|---|---|
| Insights Agent hosting | Custom in `Sprk.Bff.Api`, NOT Foundry-hosted — D-13 | Packagability + consistency with current Spaarke pattern. Foundry's superpowers aren't used here. | ADR-013 |
| Reuses | `IChatClient` + `UseFunctionInvocation` + `IAiToolHandler` framework — D-14 | All scaffolding exists. | ADR-013 |
| Memory | Two-tier (user_profile + chat_summary) via Redis — D-15 | Foundry's pattern, implemented locally. | — |
| Safety | Streaming + verification + cross-matter strip controls extend to Insights Agent — D-16 | Consistency with existing rules. | — |

### 19.5 Sync architecture

| Decision | Choice | Rationale | ADR |
|---|---|---|---|
| Compute | Azure Functions on Flex Consumption — D-17 | MS current default; Bicep-deployable per tenant with UAMI; permitted by narrowed ADR-001. | ADR-001 |
| Async backbone | Service Bus topic + Timer-triggered Function (reconciliation over change-tracking) — D-18 | Canonical MS pattern per knowledge research. | — |
| No plugins / no Power Automate | D-19 | Plugins have same SAS limitation; Power Automate adds complexity without solving auth. Webhook registration via plugin registration tool is acceptable. | — |
| No Durable Functions | D-20 | ADR-001. Orchestration via Service Bus + state machine. | ADR-001 |
| Closure-extraction | JPS playbook ending in `DeliverToIndexNodeExecutor`; Function triggers via BFF API endpoint — D-21 | Single playbook execution path; no duplicate orchestration in Function. | — |

### 19.6 Auth (CRITICAL)

| Decision | Choice | Rationale | ADR |
|---|---|---|---|
| Trust boundary | Intake Function — only external entry — D-22 | Dataverse native SBus integration is SAS-only. | ADR-028 |
| Dataverse → Intake | `clientState` (transitional) → HMAC-SHA256 (target, Phase C #044). **Copy validator from BFF** — D-23 | Documented Dataverse webhook auth options. | ADR-028 |
| All other hops | Managed Identity + Azure RBAC (UAMI). Zero SAS at target — D-24 | Transitional `clientState` is the only secret; gone post #044. | ADR-028 |
| Non-webhook endpoints on Function App | `Microsoft.Identity.Web.AddMicrosoftIdentityWebApi`. NOT `@spaarke/auth` (client-side only) — D-25 | Canonical .NET inbound JWT validator. | ADR-028 |
| TenantId | Explicit GUID, never `common` or `organizations` — D-26 | Per-tenant deployment threat model. Coordinate with Phase C #047. | ADR-028 |
| Outbound | `DefaultAzureCredential` only. No `ClientSecretCredential` in new Functions — D-27 | Aligns with Phase C #041 + #042. | ADR-028 |
| JWT validation | Local (no auth-service hop) — D-28 | `Microsoft.Identity.Web` validates locally with cached AAD metadata; extra hop adds latency without security benefit. | ADR-028 |

### 19.7 Identity resolution, privilege, packagability

| Decision | Choice | Rationale | ADR |
|---|---|---|---|
| Identity resolution | Probabilistic graph with `SAME_AS` edges + confidence; no canonical registry requiring human curation — D-29 | Human curation isn't realistic at law-firm scale. | — |
| Resolution scoping | Only invest effort on entities meeting threshold (N+ matters or N+ outbound edges) — D-30 | Don't over-invest. Standalone duplicates don't pollute aggregates. | — |
| Tenant isolation | Physical per-tenant — D-31 | Legal data privilege boundaries are physical, not just logical. | — |
| In-tenant trimming | Queries trim at execution via `accessibleMatterSet` at every Search query + every graph traversal — D-32 | Substrate stores everything for the tenant; users see different subsets per session. | — |
| ACL in vector queries | `vectorFilterMode=preFilter` mandatory — D-33 | Per MS guidance for high-cardinality access groups. | — |
| Counts vs IDs | Aggregate Insights can cross the access boundary; specific Insights cannot — D-34 | Allows useful aggregates without leaking inaccessible details. | — |
| Cross-matter pivot | Extension of existing Spaarke rule to graph evidence — D-35 | Privilege leakage vector. | — |
| Packagability | Everything as code — D-36 | Multi-tenant ISV requirement. | — |
| Multi-tenant config | Tenant-list-as-configuration in Bicep for r1; evolve to data control plane around ~10 tenants — D-37 | Fits current scale; inflection point documented. | — |

### 19.8 Repo governance

| Decision | Choice | Rationale | ADR |
|---|---|---|---|
| ADR-001 narrowed | BFF endpoints MUST be Minimal API; Functions permitted for narrow out-of-band integration; Durable Functions still rejected — D-38 | Original concern (don't fragment BFF runtime) preserved; new scope unblocks Insights Engine sync. | ADR-001 |

### 19.9 r2 additions — data, evaluation, surfacing, MCP, refinements

| Decision | Choice | Rationale | ADR |
|---|---|---|---|
| Historical backfill as Phase 1 capability — D-39 | `BulkReExtraction` Function and customer-onboarding workflow ship in Phase 1 / early Phase 2, not Phase 3 | Without backfill, new customers see insufficient-evidence responses for 6+ months — commercially unacceptable. Backfill turns a 6-month organic build into a 2-week migration for customers with prior history. | — |
| Golden dataset + eval harness as Phase 1 CI gate — D-40 | 50–100 curated `(question, expected-answer, expected-evidence)` tuples; RAG-triad evaluators (Retrieval / Groundedness / Relevance); CI gate fails on groundedness regression or insufficient-evidence miscalls | The honesty contract becomes measurable rather than just structural. Required for defensibility to enterprise legal buyers. | — |
| MCP server (`Sprk.Insights.Mcp`) as Phase 1 contract, Phase 2 implementation — D-41 | Engine exposes question catalog as MCP tools + resources + prompts; OBO auth; read-only in V1 | The integration posture is "be consumed by Microsoft's tools." Honesty contract survives all consumption paths via the renderer prompts. | — |
| Streaming verification mechanism — D-42 | Rule-based + embedding-similarity, NOT a second LLM call. Checks claim-evidence binding, citation existence, embedding-similarity threshold (default 0.55), confidence-band sanity | Cheap, bounded, no LLM-verifier hallucination risk; achievable within 200ms budget. | — |
| Sync re-fetch backpressure mitigation — D-43 | 30-second deduplication window per `(entityType, entityId)`; per-tenant token-bucket throttling for known-burst operations; nightly reconciliation catches drift | Prevents Dataverse throttling during bulk imports, end-of-quarter closures, and historical backfill. Correctness via reconciliation. | — |
| Graph adjacency-list write amplification — D-44 | Adjacency-list pattern for Phase 1; migrate high-degree vertices (≥500 edges OR ≥200KB document size) to edges-as-documents in Phase 2 via nightly reconciliation; `IInsightGraph` abstraction unchanged | Cosmos document-size and RU cost limits at scale. Phase 1 trigger unlikely to fire on freshly-bootstrapped corpus; migration tooling designed in Phase 2. | — |
| Embedding model lifecycle — D-45 | Dual-write versioned index pattern: provision `insight-matters-v{N+1}-emb{NewModel}`, re-embed via ScheduledReIndexer, dual-read grace period, cutover via config flag, decommission old | When `text-embedding-3-large` is succeeded, the entire Insight Index needs re-embedding. Same pattern as playbook-version migration; only the trigger differs. | — |

### 19.10 Explicit "do not do"

- Do not host the Insights Agent in Foundry (D-13).
- Do not use Durable Functions (D-20).
- Do not use Dataverse plugin assemblies or Power Automate flows for sync integration (D-19).
- Do not put SAS keys on Service Bus (D-22, D-24).
- Do not use `ClientSecretCredential` in new Function code (D-27).
- Do not call a separate "auth service" for JWT validation (D-28).
- Do not use `common` or `organizations` as `TenantId` (D-26).
- Do not use `@spaarke/auth` for server-side inbound validation (D-25).
- Do not create any new index without `tenantId` as a first-class field (D-12).
- Do not require human curation of identity resolution (D-29).
- Do not return generic AI hedging when Inference evidence is insufficient — return structured `insufficient_evidence` (D-06).
- Do not put document content into cross-matter aggregates (privilege leakage — §13.4).

### 19.11 Deferred (will revisit)

- Purpose-built graph DB (Cosmos Gremlin or Neo4j) — if/when Phase 2+ needs deep multi-hop algorithmic queries (`DEF-01`).
- AI Search S2 tier bump — when indexed artifacts approach ~5M per tenant (`DEF-02`).
- Migrate `PlaybookIndexingBackgroundService` from BackgroundService to a Function — Phase 3 cleanup (`DEF-03`).
- Migrate `spaarke-records-index` to add `tenantId` — Phase 3 cleanup (`DEF-04`).
- Tenant control plane evolution (config-driven → data-driven) — when tenant count approaches 10 (`DEF-05`).
- Foundry-hosted agent for separate multi-day diligence surfaces — when those surfaces are designed (`DEF-06`).
- Cross-tenant insights (industry benchmarks) — opt-in only; possibly Phase 3+ with major privacy/legal work first (`DEF-07`).
- **Foundry IQ / Agentic Retrieval as a narrow-scope evidence tool** — Phase 2+ evaluation for narrative-content evidence channels only; gate on A/B-test improvement against plain hybrid search (`DEF-08`; see §22.1).
- **Engine extraction from BFF (`Sprk.Insights.Api`)** — Phase 3 candidate; trigger criteria in §22.2 (`DEF-09`).
- **MCP server write operations** — Phase 2+ once agent-initiated-write auth/audit model is hardened (`DEF-10`; see §22.4).
- **Spaarke-specific fine-tuned models** — Phase 3+ option once corpus and eval harness exist (`DEF-11`; see §22.5).

---

## 20. Constraints

This section lists the MUST / MUST NOT rules that govern modifications to the Engine. These are the constraints `task-execute` and `code-review` skills must enforce.

### 20.1 MUST

- **MUST** carry `tenantId` as a first-class top-level field on every Insight artifact and on every new index schema (D-12).
- **MUST** carry `producedBy.version` on every Observation; never write an Observation without it (D-05).
- **MUST** apply `accessibleMatterSet` trimming at every AI Search query (with `filter: tenantId eq '{tenantId}' and search.in(scope_matterId, '{accessibleMatterCsv}', ',')`) AND every graph traversal vertex-touch.
- **MUST** use `vectorFilterMode=preFilter` on every vector query (D-33).
- **MUST** return a structured `insufficient_evidence` response when an Inference's evidence-sufficiency rule fails — never silent fallback to generic AI hedging (D-06).
- **MUST** validate `clientState` (transitional) or HMAC-SHA256 signature (target) on every webhook request to the Intake Function — copy validator code from BFF webhook handlers (D-23).
- **MUST** use `Microsoft.Identity.Web.AddMicrosoftIdentityWebApi` for all non-webhook JWT validation on the Function App (D-25, D-28).
- **MUST** use `DefaultAzureCredential` for all outbound calls in new Function code (D-27).
- **MUST** set `AzureAd:TenantId` to the explicit tenant GUID in every deployment — never `common` or `organizations` (D-26).
- **MUST** route every cross-matter pivot through the strip-prior-evidence rule (D-35).
- **MUST** use the BFF API endpoint to invoke the closure-extraction playbook from the Function (Option A; D-21) — keeps single playbook execution path.
- **MUST** carry evidence-sufficiency rules in the question catalog for every Inference question.
- **MUST** carry a golden-dataset entry for every Inference question template before that question can ship to production (D-40, §14.2).
- **MUST** run the eval harness on every PR touching `Sprk.Bff.Api/Services/Insights/**` or `playbooks/closure-extraction*.json` (D-40, §14.4).
- **MUST** use the rule-based + embedding-similarity safety verifier — never a second LLM call — for streaming claim verification (D-42, §9.4).
- **MUST** apply the 30-second deduplication window per `(entityType, entityId)` in `InsightsSyncFunction` to prevent Dataverse throttling during high-mutation periods (D-43, §6.3).
- **MUST** monitor graph vertex edge counts; **MUST** migrate any vertex exceeding 500 outbound edges OR 200KB document size to the edges-as-documents pattern via nightly reconciliation (D-44, §8.2).
- **MUST** include `surfaceMode` (`ambient` | `intentional` | `both`) on every question catalog entry (§10.2).
- **MUST** include `displayHint` on every artifact value field per the rendering rules in §10.7.
- **MUST** validate user OBO token on every MCP server request; **MUST** apply `accessibleMatterSet` trimming on every MCP tool call exactly as on direct API calls (§15.1, §15.3).
- **MUST** render `evidence[]` for every Inference visibly by default on every surface; never collapse it behind multiple clicks (§10.1, §10.7).

### 20.2 MUST NOT

- **MUST NOT** create any new AI Search index without `tenantId` as a first-class filterable field.
- **MUST NOT** use SAS keys anywhere in the production sync pipeline (D-22, D-24). The transitional `clientState` is the only allowed shared secret and is gone post Phase C #044.
- **MUST NOT** introduce `ClientSecretCredential` in new Function code (D-27).
- **MUST NOT** use Dataverse plugin assemblies or Power Automate flows for sync integration (D-19) — webhook registration via the plugin registration tool is acceptable; custom plugin code is not.
- **MUST NOT** use Durable Functions (D-20).
- **MUST NOT** host the Insights Agent in Foundry (D-13).
- **MUST NOT** use `@spaarke/auth` for server-side inbound validation — it's client-side TypeScript only (D-25).
- **MUST NOT** call a separate "auth service" for JWT validation (D-28).
- **MUST NOT** put document content into cross-matter aggregates (privilege leakage — §13.4).
- **MUST NOT** return generic AI hedging when Inference evidence is insufficient (D-06).
- **MUST NOT** require human curation for identity resolution (D-29).
- **MUST NOT** mix artifact types in a single response without explicit `type` tagging — Facts state directly; Observations/Inferences require provenance + confidence.
- **MUST NOT** expose Gremlin syntax (or any raw query language) to `IInsightGraph` consumers — they depend on named traversals only (D-10).
- **MUST NOT** ship an Inference question template without a golden-dataset entry (D-40).
- **MUST NOT** ship a build that fails the eval harness's groundedness regression gate or insufficient-evidence-correctness gate (D-40, §14.4).
- **MUST NOT** use a second LLM call as the streaming safety verifier — verifier must be rule-based + embedding-similarity (D-42).
- **MUST NOT** expose write operations through the MCP server in V1 — read-only inference and reference only (§15.3, `DEF-10`).
- **MUST NOT** consume Foundry IQ, agentic retrieval, or any external retrieval orchestrator in V1 tool implementations — V1 retrieval stays direct against `IInsightArtifactStore` (§9.5, §22.1).
- **MUST NOT** render a Fact with a confidence indicator — Facts are 1.0 by construction; visual confidence implies false uncertainty (§10.1).
- **MUST NOT** style Insufficient-evidence cards as errors or warnings on any surface — informational treatment with constructive copy only (§10.5).
- **MUST NOT** paraphrase an Inference's confidence or evidence in calling-agent prompts — values and citations must be quoted verbatim from the envelope (§10.7, §15.3).

---

## 21. Phased plan

### 21.1 Phase 0 — Spike phase — RESOLVED (no formal spikes required)

Three potential spikes were considered and resolved through architectural discussion:

| Original spike | Resolution |
|---|---|
| S1: Cosmos NoSQL vs Gremlin | Resolved by reasoning — commit to Cosmos NoSQL adjacency-list (D-09). `IInsightGraph` abstraction preserves swap path. |
| S2: Embedding model | Resolved by principle — commit to `text-embedding-3-large` (D-08). |
| S3: Dataverse → Service Bus auth | Resolved by Microsoft documentation review — adopt intermediate-Function pattern (D-22, §7.2). |

Net: skip directly to Phase 1.

### 21.2 Phase 1 — Foundation (current)

Phase 1 is split into **Track A (auth-independent, in scope NOW)** and **Track B (auth-coupled, blocked on Phase C)**. Track A is the immediate path; Track B unblocks once Phase C resolves.

**Track A deliverables** (per [SPEC.md](../../projects/ai-spaarke-insights-engine-r1/SPEC.md) D-A1 through D-A14, plus r2 additions D-A15 through D-A20):

*Substrate and code*

1. Project scaffolding (folder structure for `Services/Insights/`, `infra/insights/`, `schemas/`, `tests/Insights/`).
2. Bicep modules — AI Search indexes, Cosmos account + DB + container, Service Bus topic + subscriptions, Function App shell (compute only), Key Vault refs, App Insights connection, per-tenant UAMI.
3. AI Search index schemas (JSON, declarative): 4 indexes per §8.1.
4. `InsightArtifact` C# types (POCOs) — includes `tenantId`, `producedBy.version`, `evidence[]`, `displayHint`, `surfaceMode` fields.
5. `IInsightGraph` interface.
6. `CosmosNoSqlInsightGraph` implementation — adjacency-list document model; `FindMattersInvolvingPartyAsync`, `FindConnectedEntitiesAsync` named traversals; edge-count monitoring telemetry per D-44.
7. `LiveFactResolverService` — initial Facts (`matterDuration`, `totalSpend`, `status`, `daysSinceLastActivity`, `documentCount`); 5-minute Redis cache.
8. `InsightsResolverService` skeleton — orchestration, provenance assembler, cache, access trimming. Cache key composes `(questionId, subject, scope, filter)` per §10.4 Refine contract.
9. `Insights Agent` shell with tool interfaces; stub implementations returning mock data. Streaming safety verifier (rule-based + embedding-similarity per D-42).
10. `predict-matter-cost` Inference question definition — catalog entry, evidence-sufficiency rule (`comparableMatters.min: 12`), insufficient-evidence response shape, `surfaceMode` field, `actionableGap` copy reviewed by content designer.
11. `POST /api/insights/ask` Minimal API endpoint with endpoint filter auth, rate limiting, ProblemDetails.
12. Closure-extraction JPS playbook DESIGN document (not implementation).
13. Initial Bicep deployment to dev environment — verifies modules + documents per-tenant parameter file pattern.

*Evaluation and quality (r2 additions)*

14. **Synthetic corpus generator** (D-A15) — script producing 200–500 fictional matters with realistic distributions; loadable into the eval environment.
15. **Golden dataset v1** (D-A16, D-40) — 10–15 initial `(question, expected-answer, expected-evidence)` tuples for `predict-matter-cost`, including 3–5 insufficient-evidence cases. Curated by domain expert; reviewed by Engine team.
16. **Evaluation harness** (D-A17, §14.4) — CI gate that runs golden dataset through the resolver, computes RAG-triad metrics (Retrieval / Groundedness / Relevance), fails the build on groundedness regression or insufficient-evidence miscalls.
17. **Production observability** (D-A18, §14.5) — telemetry emitters for insufficient-evidence rate, confidence band distribution, refine rate; dashboards in App Insights.

*Surfacing and integration design (r2 additions; design docs, not implementation)*

18. **Surfacing design document** (D-A19) — Insight Card visual language for the three rendering patterns, surface map, the Refine contract, Insufficient-evidence treatment standardized per §10.7. Reviewed by design and Engine teams.
19. **MCP server contract document** (D-A20, §15.4) — tool signatures, resource URIs, prompt fragments, OBO auth flow. Drafted alongside the question catalog so they evolve together. Implementation deferred to Phase 2.
20. **Customer-onboarding workflow design** (D-A21, §11.4) — historical backfill priority algorithm, "Insights-ready" milestone definitions, customer-facing progress dashboard mockup. Implementation begins in Phase 1 if Track B unblocks; otherwise early Phase 2.

*Tests*

21. Smoke tests — unit + integration + AI Search index provisioning verification.
22. Eval harness smoke run — golden dataset executes end-to-end against synthetic corpus with stub-implemented agent; baseline metrics captured.

**Track B deliverables** (blocked on Phase C auth work):

- Dataverse webhook intake + registration.
- `InsightsSyncFunction` (ServiceBusTrigger) — with 30-second deduplication window per D-43.
- `InsightsReconciliation` (TimerTrigger).
- `ClosureExtractionTrigger` (ServiceBusTrigger).
- `BulkReExtraction` Function — implements the customer-onboarding workflow designed in D-A21.
- HMAC-SHA256 validation upgrade.
- End-to-end real-data Inference response (replaces Track A mock data).

**Phase 1 acceptance** (Track A):

*Infrastructure and code*

- All Bicep modules deploy cleanly to Spaarke Dev (`spe-infrastructure-westus2`).
- All 4 AI Search indexes provisioned with correct schema (tenantId, 3072-dim vectors, vectorFilterMode-preFilter friendly).
- Cosmos NoSQL account + database + containers provisioned.
- `Sprk.Bff.Api` builds and existing tests pass after additions (no regressions).
- `POST /api/insights/ask` with `{question: "predict-matter-cost", subject: "matter:X"}` returns a structured `InsightResponse` (with mock data) demonstrating envelope shape, provenance pointers (`evidence[]`), either an Inference with citations OR a structured `insufficient_evidence` response.

*ADR and security compliance*

- All ADR compliance verified via `/adr-check` skill.
- Zero new SAS keys, zero new `ClientSecretCredential` usages.
- Streaming safety verifier in place and gating responses (D-42).

*Evaluation and quality (r2)*

- Synthetic corpus loadable into dev environment; deterministic regeneration with seed.
- Golden dataset v1 (10–15 entries) exists in `tests/Insights/golden/`.
- Eval harness runs as a CI step on PRs touching `Sprk.Bff.Api/Services/Insights/**`.
- Eval harness baseline metrics captured on synthetic corpus: groundedness rate, retrieval overlap, relevance pass rate, insufficient-evidence correctness. Numbers are calibration baselines for Phase 2 gates; Phase 1 ships with the *gate* in place but the *threshold* set permissively (e.g., groundedness ≥ 95% rather than 100%) because mock-data behavior won't match real-data behavior.
- Production observability telemetry emitting to App Insights with correct tags (`tenantId`, `questionId`, confidence band).

*Design completeness (r2)*

- Surfacing design document approved by design and Engine teams.
- MCP server contract document approved; tool signatures aligned with question catalog.
- Customer-onboarding workflow design approved; backfill priority algorithm documented.

### 21.3 Phase 2 — Expansion (8–12 weeks after Phase 1)

*Originally planned*

- Additional Insight indexes online (`insight-decisions`, `insight-risks`, `insight-sessions`).
- Closure-extraction JPS playbook IMPLEMENTATION (the compounding loop comes online).
- Additional graph entities (Person, Firm, Judge, Issue, Jurisdiction).
- Additional question templates (10+).
- Additional surfaces (form widget, ribbon flyout).
- Multi-tenant — parameter files for 2–3 customer tenants.
- Identity resolution SAME_AS edges with periodic re-evaluation.

*r2 additions*

- **MCP server implementation** (§15.4) — four highest-value tools live: `predict_matter_cost`, `find_comparable_matters`, `assess_matter_risks`, `summarize_matter_closure`. OBO auth wired. System-prompt fragments published for calling agents.
- **Customer-onboarding backfill implementation** (§11.4) — `BulkReExtraction` Function shipped; first pilot customer onboards using the workflow. "Insights-ready" milestones surfacing in dashboard.
- **Pro-code declarative agent** consuming the MCP server (the M365 Copilot path).
- **Copilot Studio "Legal Spend Analyst" agent template** demonstrating composition of MCP tools.
- **Context pane Code Page** surface — Investigate + Refine modes, evidence drawer with faceted filters, reasoning panel.
- **Insight Cards** on matter form (Glance mode).
- **Eval harness Phase 2 thresholds** — groundedness gate tightened to 100% on golden dataset; retrieval-overlap minimum 75%; relevance pass rate minimum 85%. Golden dataset expanded to 30–50 entries across the question templates that ship.
- **Notification surface** — SSE events (`insight_highlight`, `insight_invalidate`) wired to Context pane.
- **Graph migration tooling** (per D-44) — design and prototype edges-as-documents migration for high-degree vertices; trigger threshold instrumentation in place; actual migration only if a vertex crosses the threshold (unlikely in Phase 2 corpus sizes).

### 21.4 Phase 3 — Production polish

*Originally planned*

- Cost / utilization monitoring (the inventory recommendations from `azure-inventory.md`).
- Schema evolution tooling (v2 index migration UI/scripts).
- Bulk re-extraction workflow generalization.
- Tenant onboarding automation (the control-plane evolution from §18.4).
- Public API + partner consumers.
- Post-MVP migrations:
  - `PlaybookIndexingBackgroundService` → Function App (`DEF-03`).
  - `DataverseIndexSyncService` → Service Bus + Function (`DEF-04` adjacent — decouple from BFF lifecycle).
  - Add `tenantId` to `spaarke-records-index` + backfill (`DEF-04`).
- AI Search S2 tier bump if/when artifact volume warrants.

*r2 additions*

- **Office Add-in surfaces** — Outlook and Teams cards once the broader Office Add-in maturity work clears (§22.6).
- **Foundry IQ A/B evaluation** for narrative-content evidence questions only (§22.1, `DEF-08`); narrow, gated on measured improvement on the golden dataset.
- **Engine extraction (`Sprk.Insights.Api`) trigger review** (§22.2, `DEF-09`) — evaluate BFF strain metrics; extract if any trigger criterion is met.
- **Embedding model migration playbook** validated end-to-end on a non-production tenant per D-45 / §8.1, in preparation for the inevitable `text-embedding-3-large` successor.
- **Production observability prescriptive feedback loops** (§22.7) — automated question-catalog tuning based on insufficient-evidence rate and confidence-band distribution.

### 21.5 Phase C coordination items (from [`decisions.md`](../../projects/ai-spaarke-insights-engine-r1/decisions.md))

| Phase C task | Coordination requirement | Phase 1 impact |
|---|---|---|
| #041 — MI for Graph outbound | Engine Functions inherit `DefaultAzureCredential` discipline | New Functions use MI from day one |
| #042 — MI for Dataverse outbound | Same | Re-fetch in `InsightsSyncFunction` uses `DefaultAzureCredential` |
| #044 — HMAC-SHA256 webhook validation | Ship Phase 1 with `clientState`; HMAC drops in when #044 lands | Copy the same validator code; do not fork |
| #047 — Non-`common` `TenantId` in template | Insights Engine Function appsettings adopt the fix from day one | Per-tenant explicit TenantId in every Bicep deployment |
| `.claude/AUDIT-FINDINGS-AUTH-SYSTEM.md` | Pre-v2 audit doc; canonical auth ADR is now [ADR-028](../../.claude/adr/ADR-028-spaarke-auth-architecture.md) | Engine auth design must remain consistent |

---

## 22. Future considerations

This section catalogs the architecturally significant options that are *not* part of V1, with clear evaluation criteria for when each might enter the active roadmap. The goal is to preserve optionality without committing to specific futures.

### 22.1 Foundry IQ / Agentic Retrieval as a future evidence channel

**Status**: Phase 2+ option, narrowly scoped, not part of V1.

**What it is**: Microsoft's agentic retrieval surface (Azure AI Search Knowledge Bases, exposed via `2026-04-01` REST API and through Foundry IQ). Treats retrieval as a reasoning task — an LLM decomposes complex queries into focused subqueries, runs them in parallel against one or more Knowledge Sources, fuses results with semantic reranking. Microsoft benchmarks show 36–40% relevance improvement on complex multi-hop queries compared to single-shot RAG.

**Why it is not V1**:

- Foundry IQ's data model is **passages and document chunks**. The Engine's primary substrate is **typed structured records** (envelope-shaped Observations with confidence, version, evidence). These are different operations: Foundry IQ returns passages with relevance scores; the Engine returns Inferences with structured evidence and a sufficiency contract.
- Foundry IQ has no concept of **per-question sufficiency rules**. The honesty contract (D-06) is not expressible in Knowledge Base configuration.
- The Engine's **graph layer** (Cosmos NoSQL) is not a Knowledge Source type. The Engine's **Live Facts layer** (deterministic Dataverse queries) does not benefit from query planning.
- Forcing the integration in V1 would degrade the design (lose graph + facts) and shift the synthesis surface away from where the honesty contract lives.

**Where it might add value in Phase 2+**: Specifically for **narrative-content evidence channels** where the Engine's Observations contain meaningful prose (closure summaries, decision narratives, expert-witness summaries) and the user's question is open-ended ("what did we learn from the Acme series about their negotiation patterns?"). For these specific evidence-retrieval moments — and only these — agentic retrieval against a `narrative-content` index might outperform the Engine's current vector-filter retrieval.

**The evaluation gate**: Phase 2+ work would treat agentic retrieval as **one tool among many** in the Engine's tool inventory, called from within the existing tool framework, used for narrative-content evidence only. Not the retrieval surface, not the orchestration layer. The eval harness (§14) would A/B-test the narrative-content tool against the current plain-hybrid implementation on the golden dataset; agentic retrieval is adopted only if the measured improvement on narrative-content questions exceeds 15% on relevance and groundedness without regression on cost.

**Architectural insurance**: the `IInsightArtifactStore` abstraction and the tool framework preserve the swap path. No V1 code commits to consuming any external retrieval orchestrator; no V1 architectural decision precludes adding one later.

### 22.2 Extracting the Engine from the BFF (`Sprk.Insights.Api`)

**Status**: Not V1; trigger-driven Phase 3 candidate.

**What it would be**: a separate ASP.NET Core Minimal API service hosting `InsightsResolverService`, `Insights Agent`, the tool handlers, `LiveFactResolverService`, `IInsightGraph` consumers, and `IInsightArtifactStore`. Same auth model, same envelope contract, called by BFF and other surfaces (including the MCP server in §15) over HTTPS.

**Why it might be needed**:

- The BFF is acknowledged to be evolving toward monolith. Adding the Engine's surface area (resolver, agent, tools, fact resolver, graph consumer, artifact store consumer) accelerates that trajectory.
- The AI workload has very different latency / throughput characteristics than CRUD endpoints — independent scaling is valuable once volume justifies it.
- A dedicated service makes the MCP server (§15) cleaner — it consumes the Insights API directly rather than embedding in the BFF process.

**Trigger criteria** (any one):

- BFF p95 latency on non-AI endpoints degrades by ≥ 30% after Phase 2 question catalog expansion.
- Engine workload accounts for ≥ 40% of BFF compute spend.
- A new client surface (Office Add-in, external partner API) needs Engine access without the rest of BFF — extracting is cheaper than building an adapter.
- The MCP server (§15) graduates to its own deployment and the BFF-co-hosted shape becomes architecturally awkward.

**Cost**: one more service to operate, monitor, and deploy. The auth model already supports the multi-service shape (UAMI, OBO, etc.), so the marginal complexity is bounded.

### 22.3 Cross-customer benchmark Insights

**Status**: Not V1; deferred as `DEF-07`; subject to substantial legal / privacy work before any pursuit.

**What it would be**: Engine-derived Insights that span multiple Spaarke customers — "industry-typical cost for IP licensing matters of this size" sourced from aggregated cross-customer data, rather than only from a single customer's own corpus.

**Why it's strategically attractive**: the headline two-sided-data competitive moat. It also is the answer to the cold-start problem for brand-new customers — a Day-1 customer with no historical corpus could see industry-benchmark Inferences while their own corpus builds.

**Why it's structurally hard**:

- Legal data privilege boundaries are physical, not statistical (D-31). Cross-tenant aggregation must be done in a way that no specific matter, party, or attorney can be re-identified.
- Customer opt-in is required, with clear contractual language about what is and is not aggregated.
- Aggregation must be on **counts and distributions only** — never specific matter IDs, never specific document content, never specific party names.
- Identity resolution across customers (does "Acme Corp" mean the same entity?) is an order of magnitude harder than within-tenant identity resolution.
- Regulatory review per jurisdiction (some practice areas — healthcare, defense — may not be aggregable at all).

**Pre-requisites before any pursuit**:

- Customer opt-in contractual framework reviewed by outside counsel.
- Differential-privacy or k-anonymity model designed and validated.
- Regulatory review per major practice area.
- A separate aggregation service (NOT in the per-tenant Engine) with its own threat model.

This is a multi-quarter undertaking even after legal/privacy clearance. It is correctly out of V1 scope; it is correctly the long-term differentiator.

### 22.4 Engine-initiated writes via MCP

**Status**: Not V1.

**What it would be**: extending the MCP server (§15) with write operations — "save this Inference," "register a thumbs-up rating on this Inference," "tag this comparable as 'not actually comparable.'"

**Why it's deferred**: write capabilities require an audit and authorization model that treats agent-initiated writes as a first-class threat surface. The existing `PendingPlanManager` pattern (gated user approval for write tool calls) is a good starting point, but the MCP-mediated case is harder — the calling agent is not the human, and the human's approval needs to be captured by the calling surface (Copilot, Teams, etc.) rather than the Engine.

V1 ships read-only inference and reference. V2+ adds writes when the auth/audit model is hardened.

### 22.5 Spaarke-specific fine-tuned models

**Status**: Not V1; possibly Phase 3+.

The Engine uses general-purpose Azure OpenAI models (GPT-4o for synthesis, `text-embedding-3-large` for embeddings) in V1. A fine-tuned model — trained on legal-domain documents and Engine-style envelope outputs — could improve closure-extraction quality and Inference reasoning quality.

The path: collect (with consent) anonymized closure-extraction outputs from production usage → curate a fine-tuning dataset → train a Spaarke-specific model → A/B test against the general-purpose baseline on the golden dataset (§14). Adopt only if the measured improvement justifies the operational complexity (model versioning, per-tenant deployment, fine-tuning cost).

This is a Phase 3+ option that depends on the corpus and the eval harness existing first — both are V1 commitments.

### 22.6 Office Add-in surfaces (Outlook, Teams, Word)

**Status**: Phase 2+; coupled to Office Add-in maturation work already in the broader Spaarke roadmap.

The Engine's `POST /api/insights/ask` and the MCP server (§15) are both consumable from Office Add-ins via NAA (Nested App Authentication). The blockers are not Engine-side — they are the Office Add-in maturity items already tracked elsewhere (VersionOverrides manifest issues; NAA implementation patterns).

The rendering rules in §10 already cover Outlook and Teams as Glance surfaces. When the Office Add-in work matures, the Engine can be consumed from those surfaces with no additional Engine-side work.

### 22.7 Engine telemetry feedback loops

**Status**: Phase 3+.

The production observability metrics in §14.5 (insufficient-evidence rate, refine rate, evidence-panel engagement) are descriptive in V1. A future capability is making them *prescriptive* — using the metrics to automatically tune question-catalog sufficiency rules, prioritize backfill of under-covered practice areas, and surface tenant-specific feedback to the customer success team.

This requires the production metrics infrastructure to be running and the corpus to have stabilized — both Phase 3+ pre-conditions.

---

## 23. Related

### 23.1 Project documents (canonical source for Engine specifics)

- [`projects/ai-spaarke-insights-engine-r1/README.md`](../../projects/ai-spaarke-insights-engine-r1/README.md) — Project README
- [`projects/ai-spaarke-insights-engine-r1/decisions.md`](../../projects/ai-spaarke-insights-engine-r1/decisions.md) — 38 numbered decisions (anchor doc)
- [`projects/ai-spaarke-insights-engine-r1/design.md`](../../projects/ai-spaarke-insights-engine-r1/design.md) — Comprehensive design (1268 lines, 13 sections)
- [`projects/ai-spaarke-insights-engine-r1/SPEC.md`](../../projects/ai-spaarke-insights-engine-r1/SPEC.md) — Phase 1 spec (pipeline-ready)
- [`projects/ai-spaarke-insights-engine-r1/ai-inventory.md`](../../projects/ai-spaarke-insights-engine-r1/ai-inventory.md) — DI-anchored AI subsystem inventory
- [`projects/ai-spaarke-insights-engine-r1/azure-inventory.md`](../../projects/ai-spaarke-insights-engine-r1/azure-inventory.md) — Azure resource inventory

### 23.2 Spaarke architecture docs (adjacent subsystems)

- [AI-ARCHITECTURE.md](AI-ARCHITECTURE.md)
- [playbook-architecture.md](playbook-architecture.md)
- [chat-architecture.md](chat-architecture.md)
- [rag-architecture.md](rag-architecture.md)
- [scope-architecture.md](scope-architecture.md)
- [background-workers-architecture.md](background-workers-architecture.md)
- [jobs-architecture.md](jobs-architecture.md)
- [caching-architecture.md](caching-architecture.md)
- [resilience-architecture.md](resilience-architecture.md)
- [auth-security-boundaries.md](auth-security-boundaries.md)
- [AUTH-AND-BFF-URL-PATTERN.md](AUTH-AND-BFF-URL-PATTERN.md)
- [multi-environment-portability-strategy.md](multi-environment-portability-strategy.md)
- [INFRASTRUCTURE-PACKAGING-STRATEGY.md](INFRASTRUCTURE-PACKAGING-STRATEGY.md)
- [AZURE-RESOURCE-NAMING-CONVENTION.md](AZURE-RESOURCE-NAMING-CONVENTION.md)
- [uac-access-control.md](uac-access-control.md)

### 23.3 ADRs

- [ADR-001 — Minimal API and Workers](../adr/ADR-001-minimal-api-and-workers.md) (narrowed in commit `84cec9f9` — permits Functions for narrow out-of-band integration)
- [ADR-008 — Endpoint Filter Authorization](../adr/ADR-008-endpoint-filter-authorization.md)
- [ADR-009 — Redis-First Caching](../adr/ADR-009-redis-first-caching.md)
- [ADR-010 — DI Minimalism](../adr/ADR-010-di-minimalism.md)
- [ADR-013 — AI Architecture](../adr/ADR-013-ai-architecture.md)
- [ADR-016 — AI Cost, Rate Limit, and Backpressure](../adr/ADR-016-ai-cost-rate-limit-and-backpressure.md)
- [ADR-019 — ProblemDetails](../adr/ADR-019-problemdetails.md)
- [ADR-028](../../.claude/adr/ADR-028-spaarke-auth-architecture.md) — Canonical Spaarke Auth v2 architecture (accepted 2026-05-19). Engine auth design must remain consistent.

### 23.4 Knowledge base (researcher-authored, 2026-05-19)

- [knowledge/azure-ai-search/](../../knowledge/azure-ai-search/) (with `insights-engine-supplement.md`)
- [knowledge/cosmos-gremlin/](../../knowledge/cosmos-gremlin/)
- [knowledge/azure-functions-isv/](../../knowledge/azure-functions-isv/)
- [knowledge/dataverse-sync/](../../knowledge/dataverse-sync/)
- [knowledge/foundry-memory-patterns/](../../knowledge/foundry-memory-patterns/)

### 23.5 Source code anchors (existing — Engine reuses)

- [`Sprk.Bff.Api/Program.cs`](../../src/server/api/Sprk.Bff.Api/Program.cs) — inbound JWT validation pattern (mirrored on Function App)
- [`Sprk.Bff.Api/Infrastructure/DI/AnalysisServicesModule.cs`](../../src/server/api/Sprk.Bff.Api/Infrastructure/DI/AnalysisServicesModule.cs) — feature module pattern (Insights module follows)
- [`Sprk.Bff.Api/Infrastructure/DI/AiModule.cs`](../../src/server/api/Sprk.Bff.Api/Infrastructure/DI/AiModule.cs) — `IChatClient` + tool framework + registration audit
- [`Sprk.Bff.Api/Services/RecordMatching/DataverseIndexSyncService.cs`](../../src/server/api/Sprk.Bff.Api/Services/RecordMatching/DataverseIndexSyncService.cs) — existing Dataverse → AI Search sync (template for Track B Functions)
- [`Sprk.Bff.Api/Services/Ai/ReferenceIndexingService.cs`](../../src/server/api/Sprk.Bff.Api/Services/Ai/ReferenceIndexingService.cs) — existing idempotent indexer pattern (template for InsightArtifact indexing)
- [`Sprk.Bff.Api/Services/Ai/PlaybookExecutionEngine.cs`](../../src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookExecutionEngine.cs) — JPS playbook execution (used by closure-extraction)
- [`Sprk.Bff.Api/Services/Ai/Nodes/DeliverToIndexNodeExecutor.cs`](../../src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/DeliverToIndexNodeExecutor.cs) — terminal node for closure-extraction playbook

---

*Source of truth: the code in `src/server/api/Sprk.Bff.Api/Services/Insights/` (when implemented) and the Bicep in `infra/insights/`. When code and this document disagree, fix this document.*
