# Spaarke Insights Engine — Comprehensive Design (r1)

> **Status**: DRAFT — pre-implementation, with 2026-05-27 refinement integrated below.
> **Last Updated**: 2026-05-27 (refinement pass — see callout below). Original draft 2026-05-19.
> **Authors**: Spaarke Engineering
> **Companion documents**: [README.md](README.md) · [SPEC.md](SPEC.md) · [decisions.md](decisions.md) · [SPEC-phase-1-minimum.md](SPEC-phase-1-minimum.md) · [ai-inventory.md](ai-inventory.md) · [azure-inventory.md](azure-inventory.md)
> **Knowledge base**: [knowledge/azure-ai-search/](../../knowledge/azure-ai-search/) · [knowledge/cosmos-gremlin/](../../knowledge/cosmos-gremlin/) · [knowledge/azure-functions-isv/](../../knowledge/azure-functions-isv/) · [knowledge/dataverse-sync/](../../knowledge/dataverse-sync/) · [knowledge/foundry-memory-patterns/](../../knowledge/foundry-memory-patterns/)

---

## 0. Refinement integration (2026-05-28 — current; supersedes 2026-05-27)

This design document was authored against an earlier Phase 1 framing. **Two passes of refinement** have since narrowed Phase 1 scope significantly. The current canonical Phase 1 spec is [SPEC.md](SPEC.md); this design document is preserved as comprehensive design reference but in-line text may lag the spec.

### 0.1 Current (2026-05-28) — canonical direction

[SPEC.md](SPEC.md) is the canonical Phase 1 scope. The 2026-05-28 spec narrows Phase 1 to **17 deliverables (D-P1..D-P17)** centered on real Observation production end-to-end via universal layered ingest. Read [SPEC-phase-1-minimum.md](SPEC-phase-1-minimum.md) for the rationale narrative.

| Current commitment | Decisions | Where this design doc lags |
|---|---|---|
| **Single-tenant Phase 1 scope** | D-52 (stands) | §3.5, §7.2, §9.5 — callouts already added (still current) |
| **ONE derived index (`insights-index`) with `artifactType` discriminator** | D-53 **revised 2026-05-28** (was: 5 indexes) | §4.1 — in-line callout still describes 5 indexes; **treat SPEC §3.4 as authoritative** |
| **Questions-as-playbooks** | D-54 (stands) | §5 — callouts already added (still current) |
| **Universal layered ingest** (Layer 1 classification + conditional Layer 2 outcome extraction) | D-59 (new 2026-05-28) | §6.4 — in-line callout describes the older "document-extraction-design" generalization; **the new universal-ingest model supersedes** |
| **Observation review surface — MANDATORY** | D-60 (new 2026-05-28) | Not covered in this design doc; see SPEC §3.1 D-P11 + [SPEC-phase-1-minimum.md §5](SPEC-phase-1-minimum.md) |
| **Precedent two-mode authoring** | D-61 (new 2026-05-28) | §3.4 — Precedent layer described abstractly; concrete two-mode authoring in [SPEC-phase-1-minimum.md §2](SPEC-phase-1-minimum.md) |
| **Prompt versioning + targeted re-extraction** | D-62 (new 2026-05-28) | §6.7 schema evolution — version-driven re-extraction principle was already there; D-62 ratifies it as Phase 1 mandatory |
| **Confidence-threshold gating as mechanical primitive** | D-63 (new 2026-05-28) | Not covered explicitly in this design doc; see SPEC §3.1 D-P10 |
| **Three-surface presentation DEFERRED** to Phase 1.5 | D-55 **deferred 2026-05-28** | §8 — in-line callout describes three surfaces as Phase 1; **defer to Phase 1.5** |
| **Snapshot persistence DEFERRED** to Phase 2 | D-56 **deferred 2026-05-28** | §8.6 — added 2026-05-27 as new subsection; **mark deferred** |
| **Catalog index + routing DEFERRED** to Phase 2 | D-57 **deferred 2026-05-28** | §5.3 routing tool reference; **defer** |
| **Signal Contract document SUPERSEDED** — Live Facts on read | D-58 **superseded 2026-05-28** | §6.1 — in-line callout describes Mode A/B/C and Signal Contract; **Mode A is now Live Fact on read; no projection writing in Phase 1** |
| **Cosmos NoSQL graph DEFERRED** to Phase 1.5 (first deliverable); `IInsightGraph` interface ships in Phase 1 (D-P17) | original D-A6 deferred | §4.2 — Cosmos NoSQL design preserved as reference; implementation is Phase 1.5 |
| **D-A28..D-A35 renumbered to D-P1..D-P17** | per SPEC §3.1 | §10.2 — in-line table uses A-series IDs; **see SPEC §3.1 for canonical D-P IDs** |

### 0.2 Earlier (2026-05-27) — partially superseded

The 2026-05-27 [SPEC-phase-1-minimum.md](SPEC-phase-1-minimum.md) introduced D-52..D-58. Of those: **D-52 stands** (single-tenant), **D-54 stands** (questions-as-playbooks), **D-53 was revised** (1 index not 5), **D-55/D-56/D-57 were deferred** (out of Phase 1 scope), **D-58 was superseded** (Live Facts on read replaces backward-derived Signal Contract). The in-line section callouts below were added on 2026-05-27 and may reflect the older framing — when in doubt, **read SPEC.md §3 and decisions.md D-52..D-63 for the current direction**.

### 0.3 Cross-reference precedence

For Phase 1 deliverable scope, **SPEC.md is authoritative**. For decision rationale, **decisions.md is authoritative**. This design document is comprehensive architecture reference — when it conflicts with SPEC.md or decisions.md, those win until this doc is updated.

The historical text below is preserved for context. When in doubt, cross-check against:
- [SPEC.md §3.1](SPEC.md) — canonical Phase 1 deliverable list (D-P1..D-P17)
- [decisions.md](decisions.md) — D-52..D-63 (canonical refinement decisions)
- [SPEC-phase-1-minimum.md](SPEC-phase-1-minimum.md) — 2026-05-28 rationale narrative

---

## 1. Executive Summary

### 1.1 What this is

The **Spaarke Insights Engine** is the technical layer that realizes the Memory and Inference layers of the Legal IQ Stack. It is a back-end component responsible for transforming signals from systems of record (Dataverse, SharePoint Embedded, AI sessions) into context that can be honestly served to AI agents and end users across multiple surfaces (Context pane, form widgets, ribbon flyouts, Outlook/Teams add-ins, notifications, public API).

Its scope is bounded. It does NOT include:
- Source systems (Dataverse, SPE, AI session storage)
- Presentation surfaces (the Context pane, form UIs, etc.)
- General BFF endpoints (chat, document operations, analysis)

It DOES include:
- Derived artifact stores (vector index + entity graph)
- A synthesis layer that answers context questions on demand with grounded provenance
- Sync pipelines that keep derived stores in sync with sources
- Extraction pipelines that produce new derived artifacts (Observations) from milestones (e.g., matter closure)

### 1.2 The problem it solves

The IQ Stack marketing makes a specific claim: *"Based on 200 similar matters your department has handled, this one will likely cost $280K and take 14 months."* This is **organizational inference grounded in specific past matters with citable provenance** — distinct from generic AI hedging based on industry data.

Today's Spaarke stack can support **session-scoped context** (the Context pane shows playbook galleries, citations, progress) and **record-scoped lookups** (RAG against documents) — but it cannot deliver the compounding cross-matter intelligence the marketing promises. The Insights Engine is the bridge.

### 1.3 Honesty contract

Every artifact produced by the Engine carries provenance. Every Inference cites specific evidence. Every claim about evidence sufficiency is explicit. Empty states are first-class: when comparable data is insufficient, the response is *"need ~N more matters to answer reliably"* — never a silent fallback to generic AI.

### 1.4 What you'll find in this document

| Section | Content |
|---|---|
| §0 | **Refinement integration (2026-05-27)** — pointers to where SPEC.md + addendum updates land |
| §2 | Conceptual model — **four-tier** Fact / Observation / Precedent / Inference taxonomy (per D-03, D-46) |
| §3 | Architecture — component map, request flow, tenant isolation (single-tenant Phase 1 per D-52) |
| §4 | Substrate decisions — **dual-substrate** (operational + derived) per D-53; AI Search, graph, Live Facts |
| §5 | Synthesis layer — **questions-as-playbooks** per D-54; Insights orchestration in Zone A per SPEC §3.5 |
| §6 | Data flow — Dataverse → indexes/graph, document extraction (per D-A12), backfill |
| §7 | Privilege model — per-tenant + in-tenant access trimming |
| §8 | Surface integration — **three surfaces + snapshot persistence** per D-55, D-56; routing per D-57 |
| §9 | Packagability — Bicep, deployment, schema migration |
| §10 | AI inventory reconciliation — operational substrate consumed as-is per D-53 |
| §11 | Phased plan — cross-references SPEC §3.1 as canonical for Phase 1 deliverables |
| §12 | Open questions / deferred decisions — addendum §7 first-step blockers added |

---

## 2. Conceptual Model

### 2.1 The four-tier artifact taxonomy

> **Refinement note (2026-05-22 / 2026-05-27)**: Per decisions.md D-03 and D-46, the taxonomy is **four tiers** (Fact / Observation / Precedent / Inference), not three. Phase 1 ships architecture + scaffold for all four (D-A26, D-A27); Precedent lifecycle automation lands in Phase 1.5. Examples below add a Precedent row.

Every piece of context the Engine produces is one of four things, each with a different trust profile, store, and presentation rule.

| Type | Source | Confidence | Lives in | Presented as |
|---|---|---|---|---|
| **Fact** | Deterministic computation over systems of record | 1.0 always | Live query OR materialized feature view | Stated directly. No hedging. |
| **Observation** | Probabilistic extraction by playbook/LLM at a milestone | 0.0–1.0 | Insight Index (AI Search) + Insight Graph references | Stated with confidence + evidence link |
| **Precedent** | SME-confirmed institutional pattern derived from multiple supporting Observations | 0.0–1.0 (with `confirmed`/`tentative`/`underDriftReview` lifecycle states) | `insight-precedents` index + `sprk_precedent` Dataverse entity + Cosmos `Precedent` vertex (D-A26 scaffold; D-46 layer) | Stated as institutional rule with `precedent://...` provenance + supporting Observation refs |
| **Inference** | Synthesized on demand by an Insights-mode playbook (D-54) over Facts + Observations + Precedents | 0.0–1.0 | Never authoritatively stored (per-execution cache D-A32 only; explicit user save persists a `sprk_analysis` snapshot per D-A34/D-56) | Stated with confidence, comparable set, reasoning, and cited Precedents where applicable |

#### 2.1.1 Examples to anchor the taxonomy

- **Fact**: `Matter M-1234 was pending 287 days` — computed from `closedDate - openedDate`. Always true given the source. State it directly.
- **Observation**: `Matter M-1234 outcome quality: favorable (0.92)` — produced by a document-extraction playbook (per D-A12 generalization) reading documents and decisions. Carries confidence and evidence. Cite the playbook + source docs.
- **Precedent**: `In IP-licensing matters with a 12-month cure period, settlement rates rise 18%` — SME-confirmed pattern derived from N Observations across many matters; lifecycle promotes from tentative → confirmed under SME workflow (Phase 1.5). Cite the supporting Observations and the confirmation event.
- **Inference**: `Predicted cost for this new matter: ~$280K (confidence 0.74), based on 12 comparable matters` — synthesized at query time by an Insights-mode playbook (D-54) composing Facts + Observations + Precedents. Cite the 12 matters and their actual numbers; cite any Precedents applied. If only 3 are comparable, return "insufficient evidence" with the gap via the `DeclineToFindNode` deterministic decline path (D-A24/D-49).

### 2.2 The artifact envelope

Every artifact — Fact, Observation, or Inference — uses the same envelope so surfaces can render them uniformly:

```json
{
  "id": "stable-identifier",
  "type": "fact | observation | precedent | inference",
  "subject": { "entityType": "matter", "entityId": "M-1234" },
  "predicate": "totalSpend | outcomeQuality | predictedCost | ...",
  "value": {
    "raw": <typed value>,
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
  "embedding": [0.123, ...],
  "tenantId": "tenant-acme"
}
```

Notes:
- `tenantId` is a top-level field (not just inside `scope`) so it can be a filterable index field at the substrate level.
- `producedBy.version` is **mandatory for Observations** — enables re-extraction when extraction playbooks improve (per knowledge research: *"store `playbookId@version` on every Observation to enable re-extraction when playbooks ship v2"*).
- `embedding` is populated for Observations and Inferences; null for Facts (which are typically retrieved by direct filter, not similarity).
- `validFrom` / `validTo` model temporal validity — Facts age (e.g., "total spend as of 2026-05-19" might differ from "total spend as of 2026-06-30").

### 2.3 Why this split is architecturally load-bearing

1. **Different stores**: Facts can live in a live query or simple document store. Observations need vector + structured. Inferences need an agent — they're never persisted as truth.
2. **Different APIs**: Facts return `{value}`. Observations and Inferences return `{value, confidence, evidence[], asOf, ...}`.
3. **Different UI rules**: Facts display directly. Observations/Inferences must show provenance + confidence + evidence count + an empty/insufficient-evidence state.
4. **Different governance**: Facts inherit lineage from their source query. Observations need extraction-quality monitoring. Inferences need evidence-sufficiency rules.
5. **Composition**: Inferences USE Facts as inputs. "Predicted cost" composes historical "totalSpend" Facts. Facts are foundational.

Without this split, "intelligence" silently mixes things-we-know with things-we-guess — which is the trick the IQ Stack article explicitly disavows.

### 2.4 Provenance is the API contract

Every artifact carries its evidence. The surface receives `{value, evidence[]}` as a minimum; rendering provenance is non-optional. If a surface cannot display provenance, it cannot display Inferences (only Facts, which need none beyond their source query).

This single rule prevents most ways the system could become dishonest.

---

## 3. Architecture

### 3.1 Where the Insights Engine sits

> **Refinement note (2026-05-27)**: The diagram below reflects the original "InsightsResolverService → Insights Agent → substrates" framing. Under D-54 (questions-as-playbooks), the **Insights Agent box is reframed**: it is now `Services/Ai/Insights/InsightsOrchestrator` (Zone A per SPEC §3.5) wrapping the existing `PlaybookExecutionEngine` (with D-A32 cache). The "Insight tools" inside that box are realized as **node executors** (D-A30: `IndexRetrieveNode`, `GraphTraverseNode`, `LiveFactNode`, `EvidenceSufficiencyNode`, `DeclineToFindNode`, `GroundingVerifyNode`, `SpeContentRetrieveNode`, `EmitObservationNode`, `ReturnInsightArtifactNode`) composable inside Insights-mode playbooks. The **three substrate boxes** also expand under D-53: the Insight Index box is the **derived substrate** (`insight-matters`, `insight-decisions`, `insight-risks`, `insight-sessions`, `insight-precedents`), and a new arrow goes to the **operational substrate** (`spaarke-files-index`, `spaarke-records-index`, `spaarke-invoices-index`, `spaarke-rag-references`) consumed as-is — composition happens inside `IndexRetrieveNode` per D-A35. The **surfaces row** is concretized under D-55 to (a) context-pane card, (b) Assistant pane (LLM-routed via catalog index D-57), (c) field-bound icon.


```
┌─────────────────────────────────────────────────────────────────────────┐
│  SURFACES (consumers — outside the Engine)                              │
│  Context pane │ Form widget │ Ribbon flyout │ Outlook │ Teams           │
│  Notification │ Workspace card │ Email digest │ Public API              │
└──────────────────────────────┬──────────────────────────────────────────┘
                               │ InsightRequest / InsightResponse
                               ▼
┌─────────────────────────────────────────────────────────────────────────┐
│  ─── SPAARKE INSIGHTS ENGINE ──────────────────────────────────────────│
│                                                                         │
│  ┌─────────────────────────────────────────────────────────────────┐   │
│  │ InsightsResolverService  (BFF — Sprk.Bff.Api)                   │   │
│  │ • Question router (catalog lookup | ad-hoc routing)             │   │
│  │ • Signal fetcher (parallel; AI Search + Graph + Live Facts)     │   │
│  │ • Analyzer dispatch (deterministic query | LLM | playbook)      │   │
│  │ • Provenance assembler                                          │   │
│  │ • Cache + freshness policy (per-question TTL)                   │   │
│  │ • Privilege trimming (accessibleMatterSet enforcement)          │   │
│  └────────────────────────────┬────────────────────────────────────┘   │
│                               ▼                                         │
│  ┌─────────────────────────────────────────────────────────────────┐   │
│  │ Insights Agent  (custom in BFF; uses existing IChatClient)      │   │
│  │ • Tool-call pipeline (Microsoft.Extensions.AI                   │   │
│  │   UseFunctionInvocation)                                        │   │
│  │ • Two-tier memory (Redis: user_profile + chat_summary)          │   │
│  │ • Insight tools: FindComparableMatters, GetMatterFacts,         │   │
│  │   AssessEvidenceSufficiency, RetrieveByGraph, ...               │   │
│  │ • Grounded synthesis with evidence-sufficiency rules            │   │
│  └────────────────────────────┬────────────────────────────────────┘   │
│                               │                                         │
│         ┌─────────────────────┼─────────────────────┐                   │
│         ▼                     ▼                     ▼                   │
│  ┌────────────────┐  ┌────────────────┐  ┌────────────────┐            │
│  │ Insight Index  │  │ Insight Graph  │  │ Live Fact      │            │
│  │ (AI Search)    │  │ (Cosmos —      │  │ Resolver       │            │
│  │ • vector +     │  │  see §4.2)     │  │ • Direct       │            │
│  │   metadata     │  │ • entities +   │  │   Dataverse    │            │
│  │ • per-tenant   │  │   typed edges  │  │   queries      │            │
│  │   indexes      │  │ • artifact     │  │ • on-demand    │            │
│  │                │  │   refs         │  │                │            │
│  └────────────────┘  └────────────────┘  └────────────────┘            │
│         ▲                     ▲                                         │
│         │  Sync + Extraction Pipelines                                  │
│         │  (Azure Functions — Flex Consumption)                         │
│  ┌──────┴────────────────────────────────────────────────────────┐    │
│  │ • Dataverse → Insight Index sync                              │    │
│  │   - Service Bus topic (real-time)                             │    │
│  │   - Timer-triggered reconciliation (change-tracking)          │    │
│  │ • Closure-extraction trigger (matter status → JPS playbook)   │    │
│  │ • Scheduled re-indexer (embedding refresh, version migration) │    │
│  └───────────────────────────────────────────────────────────────┘    │
│                                                                         │
└──────────┬─────────────────────────────────┬───────────────────────────┘
           │                                 │
           ▼                                 ▼
┌─────────────────────┐         ┌──────────────────────────────────────┐
│ SIGNALS (sources —  │         │ SHARED SUBSYSTEMS                    │
│ outside the Engine) │         │ Service Bus │ App Insights │         │
│ Dataverse │ SPE     │         │ Key Vault │ Managed Identity         │
│ AI sessions │ etc.  │         │                                      │
└─────────────────────┘         └──────────────────────────────────────┘
```

### 3.2 Component summary

> **Refinement note (2026-05-27)**: The "Insights Agent" row is reframed per D-54/D-A9 — it is **Insights orchestration in Zone A** (facade impl + playbook invocation wrapper + routing-tool registration), NOT a separate agent. Tools become node executors (D-A30). New rows for D-A30/A32/A33/A34/A35 added at the bottom.

| Component | Lives in | Purpose | Reuses existing? |
|---|---|---|---|
| **InsightsResolverService** | `Sprk.Bff.Api/Services/Insights/` (new — Zone B) | Thin orchestrator: translates API request → Insights-mode playbook invocation via `IInsightsAi` facade; enforces `accessibleMatterSet` trimming pre-invocation; assembles `InsightResponse` from playbook output | NEW (thinner than original framing under D-54) |
| **Insights orchestration (`InsightsOrchestrator`)** | `Sprk.Bff.Api/Services/Ai/Insights/` (new — Zone A; mirrors `Services/Ai/Chat/`) | `IInsightsAi` facade impl; wraps `PlaybookExecutionEngine` with D-A32 cache; registers routing tool (D-A33) with `SprkChatAgent` for Assistant pane | Reuses `IChatClient` + `PlaybookExecutionEngine` + tool framework |
| **Insights-mode node executors** | `Sprk.Bff.Api/Services/Ai/Nodes/` (new — Zone A; extends existing 10-executor registry) | Composable building blocks: `IndexRetrieveNode`, `GraphTraverseNode`, `LiveFactNode`, `EvidenceSufficiencyNode`, `DeclineToFindNode`, `GroundingVerifyNode`, `SpeContentRetrieveNode`, `EmitObservationNode`, `ReturnInsightArtifactNode` (D-A30) | Extends existing `INodeExecutor` registry |
| **Multi-index composition pattern** | `Sprk.Bff.Api/Services/Ai/Insights/Composition/` (new — Zone A) | Compose across operational + derived substrates inside `IndexRetrieveNode` (D-A35) | Reuses `RagService` / `RagQueryBuilder` |
| **Insights playbook execution cache** | `Sprk.Bff.Api/Services/Ai/Insights/InsightsPlaybookExecutionCache.cs` (new — Zone A) | Redis cache wrapping `PlaybookExecutionEngine` for Insights-mode playbooks (D-A32) | Reuses Redis + per-playbook TTL |
| **Catalog index + routing tool** | `Sprk.Bff.Api/Services/Ai/Insights/Routing/` (new — Zone A) | Catalog index of routing intent statements + `IAiToolHandler` routing tool registered with `SprkChatAgent` (D-A33) | Reuses `ReferenceIndexingService` pattern + chat-agent function calling |
| **Snapshot persistence** | `Sprk.Bff.Api/Api/Insights/InsightSnapshotEndpoints.cs` + `Services/Insights/Snapshots/` (new — Zone B) | Frozen `InsightArtifact` envelopes via `sprk_analysis` polymorphic with new source-type value (D-A34/D-56) | Reuses existing `sprk_analysis` polymorphic pattern |
| **Derived substrate (Insight Index)** | Azure AI Search (existing service, new indexes) | `insight-matters` / `insight-decisions` / `insight-risks` / `insight-sessions` / `insight-precedents` (D-A3) — derived substrate per D-53 | Existing AI Search service |
| **Operational substrate (consumed as-is)** | Existing `spaarke-files-index`, `spaarke-records-index`, `spaarke-invoices-index`, `spaarke-rag-references` | Composed into Insights queries by `IndexRetrieveNode` per D-A35 — **schema NOT modified by this project** (per D-53) | Existing infrastructure |
| **Insight Graph** | Cosmos DB (new account, see §4.2) | Entity + typed-edge traversal | NEW resource |
| **Live Fact Resolver** | `Sprk.Bff.Api/Services/Insights/Facts/` (new — Zone B) | Cheap on-read Dataverse queries; wrapped by `LiveFactNode` (D-A30) for playbook invocation | Reuses `IDataverseService` |
| **Sync Functions** (Track B) | New Azure Function App | Dataverse → derived-substrate sync (Service Bus + Timer); ingests per Signal Contract (D-A29) | Pattern from existing `DataverseIndexSyncService` |
| **Document-extraction trigger Function** (Track B) | Same Function App | Fires Insights-mode extraction playbook on matter milestones; reuses chunked content from `spaarke-files-index` via `SpeContentRetrieveNode` (D-A30) | Calls `IInsightsAi` facade |
| **Re-indexer Function** | Same Function App | Scheduled: embedding refresh, schema migration, version-bump re-extraction | NEW |

### 3.3 Request flow — typical Inference query

A surface asks "predict cost and flag risks for this new IP licensing matter against Counterparty Acme":

1. **Surface** → `POST /api/insights/ask` with `{question, subject, scope, surfaceHint}`.
2. **InsightsResolverService** receives `InsightRequest`. Resolves user's `accessibleMatterSet` from cache (per-session, sourced from existing access control system).
3. **Question router** classifies: known question template (catalog hit) OR ad-hoc (route to Insights Agent directly).
4. **Insights Agent** is invoked with the question + context. It uses tools to gather evidence:
   - `FindComparableMatters({practiceArea, dealRange, counterparty})` → vector + filter retrieval against `insight-matters` index, returns N artifacts (each with embedded numeric facts).
   - `GetMatterFacts({matterIds: [...]})` → batch live-fact lookup for current state of returned matters.
   - `TraverseGraph({startNode: 'party:acme', relations: ['OPPOSED→Matter', 'INVOLVED_PARTY→Matter'], maxHops: 2})` → graph traversal returns connected matters trimmed by accessibleMatterSet.
   - `AssessEvidenceSufficiency({comparableCount, minRequired: 12})` → returns sufficient/insufficient with gap analysis.
5. **Synthesis**: Agent produces an Inference artifact with `{value, confidence, evidence[], reasoning}`. If insufficient → returns `{type: 'insufficient_evidence', value: null, ...}` with gap explanation.
6. **InsightsResolverService** caches result (TTL per question type), assembles full provenance, returns `InsightResponse` to surface.

### 3.4 Request flow — typical Fact query

Surface asks "how long has this matter been pending":

1. **Surface** → `POST /api/insights/ask` with `{question: 'matter-duration', subject: 'matter:M-1234'}`.
2. **InsightsResolverService**: question router recognizes deterministic question.
3. **Live Fact Resolver**: `closedDate - openedDate`, single Dataverse query. `confidence: 1.0, evidence: [dataverse://sprk_matter/M-1234]`.
4. Cached briefly (e.g., 5 minutes) for repeat calls.
5. Returned to surface.

No Agent involvement. No retrieval. Single round-trip.

### 3.5 Tenant isolation model

> **Refinement note (2026-05-27)**: D-52 narrows Phase 1 to **single-tenant deployment only** — each customer is one Bicep parameter file = one full deployment unit. The physical-isolation mechanism below is unchanged; what changes is the multi-tenant *evolution path* (the "past ~10 tenants" inflection is deferred beyond Phase 2 per DEF-05). The `tenantId` field on derived-substrate documents is retained for partition keys, audit clarity, and future federation — but is no longer the privilege isolation mechanism in Phase 1 (physical isolation does that).

**Decision**: Physical per-tenant isolation. Each customer tenant gets:
- Own AI Search service (or shared service with per-tenant indexes — see §7 for the tradeoff)
- Own Cosmos account for the Insight Graph
- Own Function App (per-tenant UAMI, per-tenant Service Bus topic — knowledge research confirms this is the recommended ISV pattern via Flex Consumption + tenant-list-as-configuration)
- Own subset of App Service / shared resources where economically reasonable

For Spaarke r1 with a small number of tenants, **tenant-list-as-configuration in Bicep** is sufficient. ~~Past ~10 tenants, evolve to a tenant-list-as-data control plane (Phase 2 — knowledge research notes this inflection).~~ **Re-deferred 2026-05-27 per D-52 + DEF-05: single-tenant Phase 1 + Phase 2 makes the control-plane evolution non-load-bearing; revisit only if a future commercial decision overrides D-52 to pursue multi-tenant shared deployment.**

---

## 4. Substrate Decisions

### 4.1 Vector + structured retrieval — Azure AI Search

> **Refinement note (2026-05-27 — dual-substrate per D-53)**: The Engine spans **two substrates**, both on the same AI Search service. The **operational substrate** (`spaarke-files-index`, `spaarke-records-index`, `spaarke-invoices-index`, `spaarke-rag-references`) already exists with its populating plumbing in place and is **consumed as-is** by this project — no schema changes. The **derived substrate** (the `insight-*` indexes described below, plus a catalog index per D-A33/D-57) is what this project builds. Insights-mode playbooks compose across both via `IndexRetrieveNode` (D-A30) per the composition pattern in D-A35. The `tenantId` asymmetry (operational `spaarke-records-index` lacks the field; derived `insight-*` indexes have it) is handled at the composition layer — under D-52 single-tenant Phase 1 it's safe at the privilege layer, but composition logic must remain correct under future federation.

#### 4.1.1 Decision

Use the existing Azure AI Search service (already at `spaarke-search-prod` and `spaarke-search-dev`). Add new indexes for Insight artifacts (derived substrate). Reuse existing patterns from `spaarke-rag-references` (small structured docs + embedding + idempotency); the same `ReferenceIndexingService` pattern is the template for the catalog index (D-A33).

#### 4.1.2 New indexes (derived substrate per D-53)

| Index | Contents | Document size | Embedding | Populated when |
|---|---|---|---|---|
| `insight-matters` | Cohort/scope facets + aggregate features + cohort embedding content (narrow derived projection per D-58/D-A29 — does NOT re-project record fields already in `spaarke-records-index`) | Small (1-5KB structured + summary) | Yes | Phase 1 (derived projection) + Phase 2 (extraction) |
| `insight-decisions` | Decision points + rationale extracted from sessions/documents | Small | Yes | **Phase 2 (closure extraction populates; ships empty in Phase 1)** |
| `insight-risks` | Observed risk patterns and signals | Small | Yes | **Phase 2 (ships empty in Phase 1)** |
| `insight-sessions` | Notable findings from AI sessions ("save to insights" action) | Small | Yes | **Phase 2 (ships empty in Phase 1)** |
| `insight-precedents` | SME-confirmed institutional patterns (D-A26 scaffold; D-46 4th tier) | Small | Yes | Phase 1 (manual via admin endpoint D-A27) + Phase 1.5 (lifecycle automation) |
| `insights-catalog` (or partition of `spaarke-rag-references` — addendum §7 Q3 first-step blocker) | Routing intent statements + Insights-mode playbook metadata | Small | Yes | Phase 1 (per playbook publish event) |

All derived indexes share the **artifact envelope** schema from §2.2. Different `predicate` ranges, different vector content (the embedding embeds a representative text composition of the artifact), but a unified retrieval contract. The catalog index has its own schema focused on routing (subject entity types, surfaces enabled, etc.) — see D-A33.

#### 4.1.3 Index schema (illustrative — `insight-matters`)

```jsonc
{
  "name": "insight-matters",
  "fields": [
    { "name": "id", "type": "Edm.String", "key": true, "filterable": true },
    { "name": "tenantId", "type": "Edm.String", "filterable": true, "facetable": true },
    { "name": "type", "type": "Edm.String", "filterable": true },
    { "name": "subjectEntityType", "type": "Edm.String", "filterable": true, "facetable": true },
    { "name": "subjectEntityId", "type": "Edm.String", "filterable": true },
    { "name": "predicate", "type": "Edm.String", "filterable": true, "facetable": true },
    { "name": "valueRaw", "type": "Edm.String", "searchable": true },
    { "name": "valueNumeric", "type": "Edm.Double", "filterable": true, "sortable": true },
    { "name": "confidence", "type": "Edm.Double", "filterable": true, "sortable": true },
    { "name": "asOf", "type": "Edm.DateTimeOffset", "filterable": true, "sortable": true },

    // Scope (filter facets)
    { "name": "scope_practiceArea", "type": "Edm.String", "filterable": true, "facetable": true },
    { "name": "scope_jurisdiction", "type": "Edm.String", "filterable": true, "facetable": true },
    { "name": "scope_clientId", "type": "Edm.String", "filterable": true },
    { "name": "scope_matterId", "type": "Edm.String", "filterable": true },
    { "name": "scope_year", "type": "Edm.Int32", "filterable": true, "facetable": true },
    { "name": "scope_dealSizeBucket", "type": "Edm.String", "filterable": true, "facetable": true },

    // Provenance
    { "name": "producedBy_kind", "type": "Edm.String", "filterable": true },
    { "name": "producedBy_id", "type": "Edm.String", "filterable": true },
    { "name": "producedBy_version", "type": "Edm.String", "filterable": true },

    // Composed embedding content (what gets vectorized)
    { "name": "content", "type": "Edm.String", "searchable": true, "analyzer": "en.microsoft" },
    { "name": "contentVector", "type": "Collection(Edm.Single)", "dimensions": 1536, "vectorSearchProfile": "default-hnsw" },

    // Evidence references (stored as JSON for surface display)
    { "name": "evidenceJson", "type": "Edm.String", "retrievable": true, "searchable": false }
  ],
  "vectorSearch": {
    "profiles": [{ "name": "default-hnsw", "algorithm": "hnsw-default" }],
    "algorithms": [{ "name": "hnsw-default", "kind": "hnsw", "hnswParameters": { "m": 4, "efConstruction": 400 } }]
  },
  "semantic": {
    "configurations": [{
      "name": "insight-semantic",
      "prioritizedFields": {
        "contentFields": [{ "fieldName": "content" }, { "fieldName": "valueRaw" }]
      }
    }]
  }
}
```

**Per knowledge research**: use `vectorFilterMode=preFilter` for tenantId + scope filtering. This is mandatory for ACL trimming with high-cardinality groups.

#### 4.1.4 Embedding model — decided: text-embedding-3-large

We commit to **`text-embedding-3-large` (3072 dimensions)** for Insight artifact embeddings.

Rationale: legal text has rich semantic relationships; recall on "find comparable matters" is critical for the IQ Stack's headline claim. The cost differential vs. -small (~6.5x per token; 2x storage) is real but not material at the scale we operate, and the "no reason to knowingly underbuild" principle applies for a foundational choice that's expensive to change later.

**Update**: the example schema in §4.1.3 should read `"dimensions": 3072`.

Cost implications to monitor:
- At 10M artifacts/tenant: ~120GB of vector storage. Drives the S1→S2 tier consideration sooner. Plan for tier bump as a Phase 2 ops task.
- Embedding generation cost: ~$0.13 per 1M tokens (vs $0.02 for -small). At extraction throughput, this is a per-tenant operational cost line worth tracking in Cost Management.

#### 4.1.5 Tier sizing (current + future)

Current: `standard` (S1) — fine for r1 development and early pilot.

Future: Per knowledge research, **S2 (~$1,000/mo per tenant service)** is the realistic minimum for ~10M artifacts. Plan tier bump as Insight indexes grow. For physical-per-tenant model, this is a per-tenant cost that scales linearly with customers — material for pricing decisions.

### 4.2 Graph — Cosmos NoSQL with adjacency-list (committed)

#### 4.2.1 Terminology — we ARE building a real graph

Distinguish two things:
- **Graph data model**: vertices + typed edges + traversal. This is what we're building.
- **Graph database product** (Cosmos Gremlin, Neo4j, TigerGraph): purpose-built engines with graph-native query languages and deep-traversal optimizers.

**Cosmos NoSQL with adjacency-list IS a real graph implementation.** It's a graph data model on a document database. What it lacks is graph-native query syntax (Gremlin/Cypher) and in-engine optimization for 5+ hop algorithms — neither of which we need. What it gains us:

- **Vector co-location on the same node documents** — a `Person` vertex can carry an embedding, making "find people semantically similar" a single-store operation. Gremlin can't do this.
- **Microsoft's primary product investment direction** — better tooling, better SDKs, better doc cadence
- **Lower regional friction** — Gremlin has documented region-creation issues
- **One Cosmos API surface** for the team to learn, not two

The risk is real but bounded: if Phase 2+ ever needs deep multi-hop algorithmic queries (PageRank-style network analysis, community detection), adjacency-list gets painful. Mitigation: the `IInsightGraph` abstraction (see §4.2.2) keeps consumers typed in terms of named traversal patterns. Implementation swap is a contained refactor.

**Decision**: commit to **Cosmos NoSQL adjacency-list** for the Insight Graph in r1.

#### 4.2.2 The `IInsightGraph` abstraction

```csharp
public interface IInsightGraph
{
    // Vertex operations
    Task<InsightVertex> UpsertVertexAsync(InsightVertex vertex, CancellationToken ct);
    Task<InsightVertex?> GetVertexAsync(string vertexId, CancellationToken ct);
    Task DeleteVertexAsync(string vertexId, CancellationToken ct);

    // Edge operations
    Task UpsertEdgeAsync(InsightEdge edge, CancellationToken ct);
    Task DeleteEdgeAsync(string fromVertexId, string edgeType, string toVertexId, CancellationToken ct);

    // Traversals (named patterns — NOT exposing Gremlin-specific traversal syntax)
    Task<IReadOnlyList<InsightVertex>> FindConnectedEntitiesAsync(
        GraphTraversalSpec spec, CancellationToken ct);
    Task<IReadOnlyList<MatterRef>> FindMattersInvolvingPartyAsync(
        string partyId, GraphTraversalScope scope, CancellationToken ct);
    // ... other named patterns
}
```

Implementations:
- `CosmosNoSqlInsightGraph` (Phase 1 spike, primary candidate)
- `CosmosGremlinInsightGraph` (alternative if spike reveals NoSQL is wrong for our traversal patterns)

#### 4.2.3 Cosmos NoSQL adjacency-list pattern

Documents per vertex with embedded edge collections:

```jsonc
// Vertex document
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
    { "edgeType": "INVOLVED_PARTY", "sourceVertexId": "party:acme" },
    // ... mirrored for efficient reverse lookup
  ],
  "artifactRefs": [
    "insight-matter://M-1234#closure-summary",
    "insight-matter://M-1234#outcome-facts"
  ]
}
```

Partition key: `tenantId` (per knowledge research recommendation — supports per-tenant cost attribution and clean physical isolation).

#### 4.2.4 Graph schema — domain entities and edges

**Vertices**:
- `Matter` — every matter that's been opened
- `Party` — every counterparty, client, related party
- `Person` — counsel (ours + opposing), judges, partners, associates, clients
- `Firm` — opposing counsel firms, expert firms
- `Document` — significant documents (not every file; ones referenced by Observations)
- `Issue` / `Claim` — issue types or claims
- `Jurisdiction` — courts, regulatory bodies
- `Outcome` — outcome categories
- `Playbook` — JPS playbook (for traceability — "which playbook produced this Observation")

**Edges** (typed, with attributes):
- `(Matter)-[INVOLVED_PARTY {role}]->(Party)` — opposing | client | related
- `(Person)-[REPRESENTED]->(Party)` in a Matter (note: 3-way; modeled as `Person → MatterRole vertex` if the role context matters often, else as edge with edge properties)
- `(Person)-[ADJUDICATED]->(Matter)` — judges
- `(Person)-[WORKED_ON {role}]->(Matter)` — our lawyers (counsel | partner | associate)
- `(Person)-[BELONGS_TO]->(Firm)`
- `(Matter)-[INVOLVED_ISSUE]->(Issue)`
- `(Matter)-[VENUE]->(Jurisdiction)`
- `(Matter)-[RESULTED_IN]->(Outcome)`
- `(Matter)-[RELATED_TO]->(Matter)` — deal series, parent transactions, appeals
- `(Matter)-[REFERENCES]->(Document)`
- `(Person)-[SAME_AS {confidence}]->(Person)` — probabilistic identity (see §6.5)

**Identity resolution (refined per earlier conversation)**:
- No canonical registry requiring human curation
- Probabilistic graph: `SAME_AS` edges with confidence; multiple candidate nodes can coexist
- AI-driven matching at ingestion; periodic re-evaluation as more signal accumulates
- Scope by relevance: only invest resolution effort on entities meeting a threshold (appears in N+ matters or N+ outbound edges)
- Surface duplicates honestly: queries that aggregate across likely-duplicates emit `mayIncludeDuplicates: N` flag

### 4.3 Live Facts — direct Dataverse queries

#### 4.3.1 Decision

Cheap, deterministic computations stay as live Dataverse queries. No materialization for these. Examples:

- Matter duration: `closedDate - openedDate`
- Total spend: aggregate query on financial records
- Current status, fields, related counts
- Days since last activity

#### 4.3.2 Materialized Facts (when to materialize)

Expensive aggregates that are queried often get materialized in the `insight-matters` index alongside Observations. Examples:

- Per-matter outcome metrics computed at closure (cost, duration, win rate)
- Per-client/practice-area rollups (computed nightly or on-event)
- Embedding-friendly summarizations of Facts that join with Observations during retrieval

These are still Facts (`confidence: 1.0`), just stored in the index for retrieval speed.

#### 4.3.3 Implementation

`LiveFactResolverService` in BFF:

```csharp
public class LiveFactResolverService
{
    Task<InsightArtifact> GetMatterDurationAsync(string matterId, CancellationToken ct);
    Task<InsightArtifact> GetMatterTotalSpendAsync(string matterId, CancellationToken ct);
    Task<InsightArtifact> GetMatterStatusAsync(string matterId, CancellationToken ct);
    // ... typed methods per known Fact predicate
}
```

Cached briefly (5-minute TTL via existing Redis `RequestCache` / `IDistributedCache`).

---

## 5. Synthesis Layer — Insights orchestration (questions-as-playbooks)

> **Refinement note (2026-05-27 — D-54 questions-as-playbooks)**: This section was originally titled "Synthesis Layer — Insights Agent" and described a custom agent with C# tool handlers. Under D-54, **Insights questions are implemented as Spaarke playbooks executed by the existing `PlaybookExecutionEngine`** (with Insights-mode metadata D-A31 + caching D-A32). The "agent" concept is reframed as `InsightsOrchestrator` (D-A9) — a thin Zone A wrapper that exposes `IInsightsAi` facade methods, invokes playbooks, and registers a routing tool (D-A33) with the existing `SprkChatAgent` for Assistant-pane natural-language routing. The "tools" listed in §5.3 below are realized as **node executors** (D-A30), composable inside playbooks — not as C# `IAiToolHandler` instances. The honesty contract enforcement primitives (§5.4) move from "agent reasoning rule" to "node executors with deterministic exit paths" (`EvidenceSufficiencyNode`, `DeclineToFindNode`, `GroundingVerifyNode`). The two-tier memory pattern (§5.2) still applies but at the playbook + chat-agent layer. The §5.1 / §5.5 decisions stand unchanged. **Where text below conflicts with this refinement, D-54 and SPEC §3.5 take precedence.**

### 5.1 Decision: custom BFF agent, not Foundry-hosted

Already decided in conversation; knowledge research confirms with caveats:

- Foundry's agent + memory primitives are now publicly documented (April 2026); we **borrow patterns** but don't host there
- Foundry-hosted is the right choice for *separate* surfaces (multi-day matter diligence, durable workflows, HITL); NOT for the Insights Agent
- Custom BFF agent reuses existing `IChatClient` + UseFunctionInvocation pipeline + tool framework (per AI inventory §4)

### 5.2 Two-tier memory pattern (borrowed from Foundry)

Per knowledge research, Foundry uses a two-tier model: `user_profile` (static) + `chat_summary` (contextual). We adopt this via Redis (since we're not in Foundry):

| Tier | Contents | TTL | Backed by |
|---|---|---|---|
| **User profile** | User's role, practice areas they work in, preferred analytical depth, often-asked questions | 30 days, sliding | Redis (`insights:profile:{tenantId}:{userId}`) |
| **Chat summary** | Last N turns of the active Insights session (when chat-driven), accumulated context | Session lifetime, sliding | Redis (`insights:session:{tenantId}:{sessionId}`) |

These are inputs to the Agent's context — they don't bypass retrieval. Memory shapes the question and the synthesis style; evidence still comes from indexes/graph/facts.

### 5.3 Capabilities exposed by Insights-mode node executors (D-A30 — supersedes original "tools" framing)

> **Refinement note (2026-05-27)**: The original §5.3 listed C# `IAiToolHandler` instances. Under D-54 these are realized as **node executors** composable inside playbooks. The conceptual capabilities are unchanged; their implementation surface moves to `Services/Ai/Nodes/`. Names below are illustrative; confirm against existing `*NodeExecutor` patterns before creating files.

| Capability concept | Node executor (D-A30) | Returns |
|---|---|---|
| `FindComparableMatters` | `IndexRetrieveNode` composing `spaarke-records-index` (operational current-state filter) ∩ `insight-matters` (derived cohort similarity) per D-A35 | List of matter refs with similarity scores |
| `GetMatterFacts` | `LiveFactNode` (wraps `LiveFactResolverService`) | Map of `matterId → {factPredicate → value}` |
| `RetrieveByGraph` | `GraphTraverseNode` (wraps `IInsightGraph` named traversals) | List of vertex refs + edge context |
| `GetObservations` | `IndexRetrieveNode` against derived substrate (`insight-decisions` / `insight-risks` / `insight-sessions`) | List of Observation artifacts |
| `AssessEvidenceSufficiency` | `EvidenceSufficiencyNode` (applies rule from playbook metadata D-A31) | `{sufficient: bool, count: N, threshold: M, gap: '...'}` |
| `DeclineToFind` (insufficient path) | `DeclineToFindNode` (emits structured `DeclineResponse` per D-A24/D-49) | `DeclineResponse` artifact (NOT freely-composed prose) |
| `ComposeInference` (final synthesis) | Existing `AiAnalysisNodeExecutor` invoked from inside the playbook + `ReturnInsightArtifactNode` for envelope serialization | Inference artifact |
| `GroundingVerify` (post-synthesis) | `GroundingVerifyNode` (wraps `GroundingVerifier` per D-A22/D-47) — runs at end of every Insights-mode playbook | Verified-or-stripped citation set |
| `SpeContentRetrieve` (Mode C extraction — Phase 2) | `SpeContentRetrieveNode` reading from `spaarke-files-index`, wired through `ISanitizer` (D-A25/D-50) | Sanitized document content |
| `EmitObservation` (Mode C extraction — Phase 2) | `EmitObservationNode` (wraps extraction output in Observation envelope with provenance) | Observation artifact |
| `Precedent search + cite` | composed inside playbook via D-A26 scaffold (`ISearchPrecedentsTool` and `ICitePrecedentTool` realized as node executors per D-A30) | Precedent artifact + citation |

Each node follows the existing `INodeExecutor` pattern (per AI inventory §8). Each evidence-bearing node has `EvidenceGuard.Validate` (D-A23/D-48) protecting against silent empty-evidence returns.

In addition, an **`IAiToolHandler` routing tool** (D-A33) is registered with the existing `SprkChatAgent` for Assistant-pane natural-language routing — it returns `{playbookId, parameters, confidence}` but does NOT itself produce the answer. The Assistant pane's chat agent then invokes the routed playbook through `IInsightsAi.AnswerQuestionAsync` per D-55.

### 5.4 Evidence-sufficiency rules — non-negotiable

> **Refinement note (2026-05-27 — D-06 mechanism update under D-54)**: The principle is unchanged: every Inference question carries an evidence-sufficiency rule and enforcement is non-negotiable. Under D-54, the rule no longer lives in a "question catalog" — it lives in **Insights-mode playbook metadata (D-A31)**. The Agent's `AssessEvidenceSufficiency` tool is realized as **`EvidenceSufficiencyNode` (D-A30)**; the insufficient-evidence response is emitted by **`DeclineToFindNode` (D-A24/D-49)** — both deterministic node-graph paths, not LLM-coercible. The JSON example below remains accurate as the metadata shape; conceptually it now lives in playbook metadata rather than a separate registration entity.

Every Inference question carries (in Insights-mode playbook metadata, D-A31) an evidence-sufficiency rule:

```jsonc
{
  "questionId": "predict-matter-cost",
  "tier": "inference",
  "requiredEvidence": {
    "comparableMatters": { "min": 12, "preferred": 25 },
    "scopeMatchOn": ["practiceArea"],  // must match these
    "scopeMatchPreferOn": ["counterparty", "jurisdiction"],  // bonus
    "freshness": "comparables from last 3 years preferred"
  },
  "insufficientResponse": {
    "type": "insufficient_evidence",
    "message": "Only {actual} comparable matters found; need ~{required} for a reliable estimate.",
    "actionableGap": "Need more {practiceArea} matters with comparable {dealSize}."
  }
}
```

The playbook's `EvidenceSufficiencyNode` (D-A30) reads this rule from playbook metadata (D-A31) and either proceeds with synthesis or routes to `DeclineToFindNode` (D-A24/D-49) which emits the structured insufficient-evidence response.

**Why this matters**: this is the architectural enforcement of the honesty contract. Without explicit sufficiency rules per playbook AND deterministic node-graph routing on the insufficient path, synthesis will default to "make something up with hedging language" — the exact behavior the marketing claims to differ from. Under D-54 + D-49, the decline path is a node executor (mechanical), not an LLM judgment call (coercible).

### 5.5 Safety controls

- **Streaming + verification** (already a Spaarke pattern from the unification design): Agent streams response; safety verifier checks claims against retrieved evidence within 200ms of completion. Unverified claims flagged.
- **Strip retrieved content on cross-matter pivot** (already a Spaarke rule): when user pivots Matter context mid-session, prior retrieved content is cleared from conversation history. Conclusions remain.
- **Tool-call gating** (extends existing `PendingPlanManager`): for write-back operations (e.g., "save this Inference back to the insight-sessions index"), require explicit user approval.

---

## 6. Data Flow

> **Refinement note (2026-05-27 — Signal Contract per D-58)**: Phase 1 ingestion is driven by the **Signal Contract** (D-A29 — `signal-contract.md`), derived backward from the question catalog (currently `predict-matter-cost`) per the backward-design principle. Three collection modes: **Mode A** (narrow Dataverse-derived projection — does NOT re-project record fields already in `spaarke-records-index`), **Mode B** (graph vertices + edges only, no new metadata index), **Mode C** (typed document-content extraction at milestones; Phase 2 implementation via D-A30 nodes per §6.4 below). The §6.1–§6.3 sync flow remains accurate for Modes A and B once Track B unblocks; §6.4 is generalized below to "document extraction" per D-A12.

### 6.1 Architecture per knowledge research + auth constraints

The canonical Microsoft pattern for Dataverse → Azure AI Search sync is:

- **Service Bus topic** as real-time backbone (Dataverse → ... → Service Bus → Functions)
- **Timer-triggered Function** over Dataverse change-tracking for reconciliation
- Webhooks are TRIGGERS ONLY — handlers must re-fetch the Dataverse record (256KB payload truncation + out-of-order delivery)
- Microsoft 365 Copilot connectors target Microsoft Graph, not our AI Search — orthogonal

**Critical auth constraint** (confirmed via Microsoft docs review): **Dataverse's native Azure integration to Service Bus uses SAS authorization only.** It does NOT support Entra ID / Managed Identity for the webhook → Service Bus hop. This is documented at:
- [Configure Azure integration](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/configure-azure-integration)
- [Configure SAS integration walkthrough](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/walkthrough-configure-azure-sas-integration)

Spaarke's security standard requires no SAS keys on the bus. Therefore we use the recommended intermediate-component pattern: **Dataverse webhook → HTTP-triggered Function (Entra OAuth auth) → Service Bus topic (Managed Identity / Azure RBAC).** The intake Function is the auth-trust boundary.

We do NOT use Dataverse plugins or Power Automate for this integration. Plugins would have the same SAS limitation; Power Automate adds operational complexity without solving the auth problem.

### 6.2 Real-time sync flow (auth-correct)

```
┌──────────────────┐
│  Dataverse       │
│  (matter, party, │
│   document, ...) │
└────────┬─────────┘
         │ change event → webhook (HTTPS POST)
         │ Auth on this hop:
         │   • TRANSITIONAL: clientState (shared secret in header)
         │                   — copy from BFF webhook handlers,
         │                     do NOT recreate
         │   • TARGET: HMAC-SHA256 signature validation
         │             — lands with Phase C task 044
         ▼
┌─────────────────────────────────────────────────────────────┐
│ Function: DataverseWebhookIntake (HTTPTrigger)              │
│  AUTH TRUST BOUNDARY — only entry point that accepts        │
│  external traffic. Everything past here is internal.        │
│                                                             │
│  1. Validate clientState / HMAC signature on the request    │
│     (reuse BFF webhook validation code; see Phase C #044)   │
│  2. Validate webhook payload shape + originating entity     │
│  3. Publish to Service Bus topic using UAMI                 │
│     (DefaultAzureCredential; Azure RBAC — no SAS keys)      │
│                                                             │
│  Note: For ANY non-webhook endpoint on this Function App    │
│  (e.g., admin endpoints), use Microsoft.Identity.Web JWT    │
│  validation — same pattern as Sprk.Bff.Api (see §6.2.3).    │
└────────┬────────────────────────────────────────────────────┘
         │ Service Bus SDK call with DefaultAzureCredential (UAMI)
         ▼
┌──────────────────────────────┐
│ Service Bus Topic            │
│ "spaarke-dataverse-          │
│  changes-{tenant}"           │
│ Auth: Azure RBAC via UAMI    │
│ Subscribers (UAMI):          │
│  - insights-sync             │
│  - records-sync (existing)   │
│  - other consumers           │
└────────┬─────────────────────┘
         │ ServiceBusTrigger (UAMI)
         ▼
┌─────────────────────────────────────┐
│ Function: InsightsSyncFunction      │
│  1. Re-fetch full Dataverse record  │
│     (DefaultAzureCredential; per    │
│      Phase C task 042 — no          │
│      ClientSecretCredential)        │
│  2. Project to InsightArtifact      │
│     envelope                        │
│  3. Push to AI Search index (UAMI;  │
│     vectorFilterMode=preFilter)     │
│  4. Update graph vertex/edges       │
│     (UAMI to Cosmos)                │
│  5. Emit milestone events           │
│     (status → closed → SBus)        │
└────────┬────────────────────────────┘
         │
         ▼
   AI Search + Graph + (if milestone) Service Bus message → Extraction Function
```

#### 6.2.1 Auth summary — secrets in the pipeline

| Hop | Auth mechanism (target) | Auth mechanism (transitional) | Secret type |
|---|---|---|---|
| Dataverse → Intake Function | **HMAC-SHA256** signature (Phase C #044) | **clientState** shared secret in header | Shared secret (transitional only); none at target |
| Intake Function → Service Bus | Azure RBAC via UAMI | (same) | None |
| Service Bus → consumer Functions | Azure RBAC via UAMI | (same) | None |
| Consumer Functions → Dataverse (re-fetch) | Azure RBAC via UAMI (Phase C #042) | DefaultAzureCredential | None at target |
| Consumer Functions → AI Search | Azure RBAC via UAMI | (same) | None |
| Consumer Functions → Cosmos | Azure RBAC via UAMI | (same) | None |

**Net at target state**: zero SAS keys and zero long-lived secrets anywhere in the pipeline.

**Net at transitional state**: a single shared `clientState` secret on the Dataverse → Intake hop, stored in Key Vault, rotated programmatically. This is the only secret in the pipeline and it disappears when Phase C task 044 lands.

#### 6.2.2 Auth answers — resolved (no longer open)

| # | Question | Resolution |
|---|---|---|
| **AUTH-1** | Does `@spaarke/auth` validate inbound Bearer tokens at the Function/API edge? | **No.** `@spaarke/auth` is **client-side TypeScript only** (`src/client/shared/Spaarke.Auth/`, `@spaarke/auth@2.0.0`). It uses MSAL.js to acquire tokens for outbound calls to the BFF — it does NOT validate them. Inbound JWT validation lives in `Sprk.Bff.Api` via `Microsoft.Identity.Web`'s `AddMicrosoftIdentityWebApi`. |
| **AUTH-2** | Is `@spaarke/auth` a NuGet package or a service? | **Neither — it's a workspace-local npm package.** A .NET Function App **cannot** reference it. The Function uses the same .NET stack as `Sprk.Bff.Api`: install `Microsoft.Identity.Web` (NuGet) and call `AddMicrosoftIdentityWebApi(configuration.GetSection("AzureAd"))`. |
| **AUTH-3** | Per-tenant validation (correct tenant + audience)? | **Yes — via `Microsoft.Identity.Web` config, not `@spaarke/auth`.** `AzureAd:TenantId` is an explicit tenant GUID (**NOT** `common` or `organizations` — Phase C task 047 fixes the template that still says `common`). `AzureAd:ClientId` is the Function's own app registration ID; only tokens minted FOR this audience are accepted. `Microsoft.Identity.Web` validates `iss`, `aud`, `tid`, signature, expiry by default. Per-tenant deployment threat model (D-AUTH-5) is the Spaarke convention. |
| **AUTH-4** | Established pattern for "Dataverse calling our endpoint"? | **Yes, mid-upgrade.** For webhook endpoints (Dataverse webhook → external HTTP), current pattern is **clientState** (shared secret in header), being replaced by **HMAC-SHA256** signature validation in Phase C task 044. **Copy from the BFF's webhook handler — do NOT recreate validation logic.** For OBO/user-context calls: validate via `Microsoft.Identity.Web` (same as AUTH-1). For app-only/S2S: managed identity (`DefaultAzureCredential`) per Phase C tasks 041–042 — **no `ClientSecretCredential` in new Functions**. |

#### 6.2.3 Canonical inbound JWT validation pattern (for non-webhook endpoints)

The Function App will have some non-webhook endpoints (admin, manual trigger, status). These use Microsoft.Identity.Web JWT validation — same pattern as `Sprk.Bff.Api`. Reference implementation: [`Sprk.Bff.Api/Program.cs`](../../src/server/api/Sprk.Bff.Api/Program.cs) + [`Sprk.Bff.Api/Infrastructure/DI/AuthorizationModule.cs`](../../src/server/api/Sprk.Bff.Api/Infrastructure/DI/AuthorizationModule.cs).

```csharp
// Program.cs — Insights Engine intake Function (mirrors Sprk.Bff.Api)
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"));

builder.Services.AddAuthorization();

// Webhook endpoints — DO NOT require JWT; they use clientState/HMAC validation
app.MapPost("/intake/dataverse-webhook", DataverseWebhookHandler);  // no .RequireAuthorization()

// Admin/management endpoints — DO require JWT
app.MapPost("/intake/admin/reconcile", ReconcileHandler).RequireAuthorization();
app.MapGet("/intake/admin/health-detailed", HealthHandler).RequireAuthorization();
```

```jsonc
// appsettings.json — NOT appsettings.template.json (template uses 'common' placeholder
// per Phase C task 047 — must be fixed before deployment)
{
  "AzureAd": {
    "Instance": "https://login.microsoftonline.com/",
    "TenantId": "<the customer tenant's GUID — explicit, NOT 'common'>",
    "ClientId": "<intake Function's own app registration ID>"
  }
}
```

**Critical: do NOT call out to a separate "auth service" for JWT validation.** JWT validation is a local crypto check (signature, exp, iss, aud, tid); `Microsoft.Identity.Web` does it correctly and caches AAD metadata. An "auth service" hop adds latency with no security benefit.

#### 6.2.4 Outbound auth — managed identity only

Per Phase C tasks 041 (Graph outbound) and 042 (Dataverse outbound), **all new Functions use `DefaultAzureCredential` from day one. No `ClientSecretCredential`.**

```csharp
// Outbound to Dataverse (record re-fetch)
var credential = new DefaultAzureCredential();
var dataverseClient = new ServiceClient(new Uri(dataverseUrl), tokenProviderFunction, ...);

// Outbound to Service Bus
var sbusClient = new ServiceBusClient(namespace, new DefaultAzureCredential());

// Outbound to AI Search
var searchClient = new SearchClient(endpoint, indexName, new DefaultAzureCredential());

// Outbound to Cosmos
var cosmosClient = new CosmosClient(accountEndpoint, new DefaultAzureCredential());
```

In Bicep, the Function's UAMI gets role assignments to each target resource. Locally, `DefaultAzureCredential` falls back to Azure CLI / Visual Studio credentials.

#### 6.2.5 Authoritative references for auth patterns

| Concern | Source |
|---|---|
| Inbound JWT validation reference implementation | [`Sprk.Bff.Api/Program.cs`](../../src/server/api/Sprk.Bff.Api/Program.cs), [`AuthorizationModule.cs`](../../src/server/api/Sprk.Bff.Api/Infrastructure/DI/AuthorizationModule.cs) |
| Per-tenant config + non-`common` TenantId fix | Phase C task 047 (in flight) — fixing `appsettings.template.json` |
| Webhook signature pattern (current `clientState`, future HMAC) | Phase C task 044 (in flight) |
| OBO server-side flow | [`.claude/patterns/auth/obo-flow.md`](../../.claude/patterns/auth/obo-flow.md) |
| Managed identity for outbound (Graph + Dataverse) | Phase C tasks 041 (Graph), 042 (Dataverse) |
| Active auth design canon (until ADR-027 lands) | [`.claude/AUDIT-FINDINGS-AUTH-SYSTEM.md`](../../.claude/AUDIT-FINDINGS-AUTH-SYSTEM.md) |

**Coordination requirement**: the Insights Engine Phase 1 work must coordinate with the `@spaarke/auth` Phase C effort. Specifically:
- Adopt task 047's per-tenant TenantId pattern from day one (no `common`)
- Use task 044's HMAC validation when it lands; ship initially with `clientState` copied from BFF webhook handlers
- Inherit the managed-identity-only outbound discipline from tasks 041–042

### 6.3 Reconciliation flow

Timer trigger nightly (or more frequent):

```
┌──────────────────────────────────────┐
│ Function: InsightsReconciliation     │
│  (TimerTrigger: "0 30 2 * * *")      │
│                                      │
│  1. Query Dataverse change-tracking  │
│     for records modified since last  │
│     successful sync timestamp        │
│  2. For each: re-fetch + re-project  │
│     + upsert to Search + graph       │
│  3. Compare Search index counts vs.  │
│     Dataverse counts; alert on drift │
│  4. Update last-sync timestamp       │
└──────────────────────────────────────┘
```

Catches missed events, schema drift, late-binding edges. Idempotent — re-running on a healthy index is a no-op.

### 6.4 Document-extraction flow (generalized per D-A12)

> **Refinement note (2026-05-27)**: This was originally "Closure-extraction flow". Per D-A12, the design is **generalized** to "document extraction" covering multiple document types (closing letters / closure summaries, settlement agreements, decision memos, pleadings) and two extraction archetypes (Archetype 1 direct vs. Archetype 2 analytical — see SPEC-phase-1-minimum.md §2.5). The pipeline is composed of new node executors (D-A30): `SpeContentRetrieveNode` (reads from `spaarke-files-index` — does NOT re-ingest from SPE — wired through `ISanitizer` D-A25/D-50) → existing `AiAnalysisNodeExecutor` (LLM extraction) → `EmitObservationNode` (envelope-wrapping with provenance) → existing `DeliverToIndexNodeExecutor` (writes to derived `insight-*` indexes). Phase 1 ships the design only (D-A12); Phase 2 ships implementation. The first document type for Phase 2 implementation is a first-step blocker on D-A12 (addendum §7 Q6 — closing letters / closure summaries are the natural starting point).

Triggered by Service Bus message when a matter reaches a milestone (closure, settlement, judgment):

```
┌────────────────────────────────────────────┐
│ Function: DocumentExtractionTrigger         │
│ ServiceBusTrigger:                          │
│   "spaarke-matter-milestones-{tenant}"      │
│                                             │
│  1. Load matter context                     │
│  2. Invoke Insights-mode extraction         │
│     playbook (via BFF API IInsightsAi       │
│     facade — see §6.4.1)                    │
│  3. Playbook node graph:                    │
│       SpeContentRetrieveNode                │
│         (reads spaarke-files-index;         │
│          sanitizes via ISanitizer)          │
│       → AiAnalysisNodeExecutor              │
│         (LLM extraction by archetype)       │
│       → EmitObservationNode                 │
│         (envelope + provenance)             │
│       → DeliverToIndexNodeExecutor          │
│         (writes to derived insight-*)       │
│  4. Graph vertices/edges updated            │
│     based on extracted facts                │
└────────────────────────────────────────────┘
```

#### 6.4.1 BFF call vs. direct playbook engine call

Two options for how the Function invokes the closure-extraction playbook:

| Option | Pros | Cons |
|---|---|---|
| **A. Function calls BFF API endpoint** | Single playbook invocation path; auth + observability mirror BFF | Couples to BFF availability; HTTP call adds latency |
| **B. Function directly invokes `PlaybookExecutionEngine`** | Independent of BFF; faster | Duplicates DI wiring; risk of drift between BFF and Function playbook execution |

**Recommendation: Option A (BFF API call)** — keeps a single playbook execution path. The Function is a thin trigger; the actual work runs in the BFF. This is the right separation for r1; revisit if BFF latency becomes a bottleneck.

### 6.5 Identity resolution flow (probabilistic, no human curation)

When sync or extraction encounters a person/org name:

```
1. Normalize name (trim, case-fold, common-abbrev expansion)
2. Search graph for existing vertices of same type with similar name
   (fuzzy match + bar number + email domain + prior co-occurrences)
3. Scoring:
   - High-confidence match (≥ 0.85): create SAME_AS edge automatically
   - Medium (0.5–0.85): create new vertex with proposedSameAs ref;
     periodic batch job re-scores as more signal accumulates
   - Low (< 0.5): create new vertex with no link
4. Scope by relevance:
   - Only invest resolution effort on entities meeting threshold
     (appears in N+ matters or N+ outbound edges)
   - Long-tail mentions stay as standalone vertices
5. Queries follow SAME_AS edges (configurable confidence threshold,
   default 0.7+)
6. Aggregate Insights flag possible duplicates:
   - "Acme Corp has appeared in 8 matters (may include 2 likely
     duplicate references)"
```

### 6.6 Backfill

For initial provisioning (new tenant) or recovery (failed reconciliation):

- **Bulk extraction Function** with manual trigger via Admin endpoint
- Iterates Dataverse records in batches (avoid throttling)
- Skips records already at current playbook version (`producedBy.version` check on existing Observations)
- Emits progress events for monitoring

### 6.7 Schema evolution

When `insight-matters` schema needs a field added or `closure-extraction` playbook ships v4:

1. **Versioned indexes**: new index name (`insight-matters-v2`) created via Bicep
2. **Dual-write** during migration: sync writes to both v1 and v2
3. **Re-extract**: scheduled Function iterates v1, re-extracts at new playbook version, writes to v2
4. **Cutover**: search reads switch to v2 (config flag)
5. **Decommission v1** after grace period

For Observations specifically: the `producedBy.version` field enables targeted re-extraction. Don't blindly re-extract; query for `producedBy.version < currentPlaybookVersion`.

---

## 7. Privilege Model

### 7.1 Two principles

#### Principle 1: Graph + index contain everything; queries trim at execution

The substrate stores the union of all accessible data for the tenant. Every query layer applies the user's `accessibleMatterSet` filter at execution time. This is slower than per-user pre-materialized views but is correct under access changes (granting access doesn't require rebuilding views).

#### Principle 2: Counts are less sensitive than IDs

It's acceptable to say "Acme Corp has appeared in 8 of your firm's prior matters" even when the user can only access 3 of them — provided "click to see them" only returns the 3.

This distinguishes:
- **Aggregate Insights** (counts, ranges, distributions): trim post-aggregation
- **Specific Insights** (linked matter IDs, document refs): trim pre-aggregation

The artifact envelope's `value.displayHint` field signals which category an artifact is in.

### 7.2 Per-tenant isolation (physical)

> **Refinement note (2026-05-27 — D-52 single-tenant Phase 1)**: The physical-isolation mechanism is unchanged and remains the privilege boundary. What changes is that Phase 1 has **exactly one tenant per deployment** — each customer = one Bicep parameter file = one full deployment unit. The cross-tenant federation marketing is explicitly deferred (DEF-14). The `tenantId` field on derived indexes is retained for partition keys, audit clarity, and future federation but is not load-bearing for privilege isolation under D-52.

Each tenant has its own AI Search service or per-tenant indexes within a shared service, own Cosmos account, own Function App. The privilege boundary is **physical**: cross-tenant data leakage is structurally impossible because there's no path between them.

| Resource | Per-tenant deployment |
|---|---|
| AI Search service | Per-tenant service (preferred for legal clients) OR per-tenant indexes in shared service (acceptable with strict `tenantId` preFilter) |
| Cosmos account | Per-tenant account (clean partition + cost attribution) |
| Function App | Per-tenant app with per-tenant UAMI |
| Service Bus topic | Per-tenant topic |
| Application Insights | Shared with workspace-based separation; correlation IDs include `tenantId` |

### 7.3 In-tenant access trimming

Within a tenant, users see different matter subsets. The `accessibleMatterSet` is sourced from the existing access control system (Dataverse security model or unified access control project) and cached per session.

#### 7.3.1 In AI Search queries

Every query includes a filter:

```
filter: tenantId eq '{tenantId}' and 
        search.in(scope_matterId, '{accessibleMatterCsv}', ',')
```

With `vectorFilterMode=preFilter` (per knowledge research) for vector queries.

#### 7.3.2 In graph traversals

Every traversal hops include matter-set filter at vertex-touch points:

```
TraverseGraph spec includes:
  matterFilter: accessibleMatterSet
  unaccessibleHandling: 'redactReference' | 'omit' | 'countOnly'
```

`'countOnly'` lets aggregate queries return counts without leaking IDs.

#### 7.3.3 In Inference synthesis

The Agent's tools all receive `accessibleMatterSet` as part of the tool context. Tools fail-safe: if a tool cannot trim correctly, it returns an error rather than potentially over-disclosing.

### 7.4 Cross-matter pivot rule (extension of existing rule)

When a user pivots Matter context mid-session (Matter A → Matter B), retrieved evidence about Matter A is stripped from conversation history. This is already a Spaarke rule for documents ([design.md:925](../spaarke-ai-platform-unification-r2/design.md#L925)); we extend it to graph evidence:

- Specific entity refs from prior matter: cleared
- Aggregate conclusions: retained (they don't contain matter-specific privileged details)
- User notified: *"Switching matter context — prior matter details cleared from context."*

### 7.5 Auditability

Every Insight request and response is logged to the existing audit log (per [unification design.md §5.5](../spaarke-ai-platform-unification-r2/design.md)):

- User ID, tenant ID
- Question asked (sanitized)
- Artifacts returned (IDs only, not values)
- Evidence cited (refs)
- Confidence + sufficiency result

Append-only Cosmos with immutable policy. This is a compliance artifact, not a debugging tool.

---

## 8. Surface Integration

> **Refinement note (2026-05-27 — three surfaces + snapshot persistence per D-55, D-56, D-57)**: Phase 1 commits to **three concrete surfaces** with a single execution path: (a) **context-pane Insights card** (relevance-ranked per D-A19 Phase 1 ruleset; calls `POST /api/insights/relevant`), (b) **Assistant pane** (LLM-routed via catalog index D-A33; calls `POST /api/insights/route` then `POST /api/insights/ask`), (c) **field/section-bound AI icon** (PCF static binding to a specific `playbookId`; calls `POST /api/insights/ask`). All three call `IInsightsAi` through the same Zone A path; surfaces differ only in HOW the playbookId is selected. **Critical**: the LLM does NOT synthesize Insight answers — it routes the request to a playbook; the playbook (with `GroundingVerifyNode`) produces the answer with provenance. The original §8.3/§8.4 surface enumeration (form widget, ribbon, Outlook, Teams, notification, public API) reframes as "future surfaces"; Outlook/Teams remain DEF/Phase 2+ per SPEC §3.6. New §8.6 below covers snapshot persistence per D-A34/D-56.

### 8.1 Surface-agnostic delivery

The Engine produces `InsightResponse` payloads. Each surface knows how to render the response in its container. The Engine doesn't know about specific surfaces; surfaces don't know about the Engine's internals.

### 8.2 `InsightResponse` contract

```jsonc
{
  "requestId": "uuid",
  "asOf": "2026-05-19T08:30:00Z",
  "answers": [
    {
      "artifact": <full Insight artifact envelope per §2.2>,
      "displayHints": {
        "title": "Predicted cost",
        "subtitle": "Based on 12 comparable matters",
        "primary": "$280,000",
        "secondary": "Confidence: 74%",
        "drilldownAvailable": true,
        "evidenceCount": 12
      }
    }
  ],
  "freshness": "live | cached-{ttlSeconds}",
  "warnings": [],  // e.g., "mayIncludeDuplicates: 2"
  "diagnostics": {
    "tier": "inference",
    "questionId": "predict-matter-cost",
    "evidenceSufficient": true,
    "trimmedFromAccess": 0  // count of artifacts trimmed by accessibleMatterSet
  }
}
```

### 8.3 Surface-specific rendering

| Surface | Renders | Specific notes |
|---|---|---|
| **Context pane** | Stratified by tier (Record / History / Matter Memory / Org Intelligence) with explicit tier labels. Each Insight shows value + provenance + drill-through | Per earlier conversation: tier labels are mandatory; "intelligence panel" framing is dropped |
| **Form widget** | Single Insight as a card on the record form (e.g., "Predicted cost" card on the matter form) | Compact mode; click to expand provenance |
| **Ribbon flyout** | One-shot Insight in a popover | Aggregate-only by default (counts, ranges); detailed Insights require pane navigation |
| **Outlook add-in** | Insights about the matter the email pertains to | Subset of pane content; "View more in Spaarke" CTA |
| **Notification** | Push of newly-derived Insights (closure summary ready, risk signal detected) | Lightweight; deep-link to pane |
| **Public API** | `GET /api/insights/...` for partner consumers | Same envelope; same provenance rules |

### 8.4 The Context pane — stratified surface

Per the earlier strategic conversation, the Context pane shows four tiers explicitly labeled, with different evidence rules:

| Tier | Source | Honest because… |
|---|---|---|
| 1. Record | Direct Dataverse (Live Facts) | Data, nothing claimed |
| 2. History | Audit log + work history | Receipts — every item has timestamp + actor |
| 3. Matter Memory | MatterMemoryService + Observations scoped to this matter | Each fact has provenance to the source action |
| 4. Organizational Intelligence | Insights Engine (Inferences over Observations + materialized Facts) | Cited to specific past matters; similarity criteria visible; honest empty states |

The pane renders responses from the Engine grouped by tier. The pane is one consumer; the same Engine responses flow to other surfaces unchanged.

### 8.5 SSE event integration (extends unification design)

The current unification design defines `context_update` SSE events. We generalize:

- `insight_response` — full `InsightResponse` payload (broader than `context_update`; consumable by any surface)
- `insight_highlight` — cross-pane jump (e.g., from a comparable-matter reference → open that matter)
- `insight_invalidate` — surface should refresh (e.g., a matter status changed, dependent Insights are stale)
- `insight_snapshot_drift` (new per D-A34/D-56) — surface should signal that a pinned snapshot's revalidation has detected material drift from current values

These coexist with existing `context_update` events during migration; eventually `context_update` becomes a deprecated alias for `insight_response`.

### 8.6 Snapshot persistence (per D-A34, D-56) — new

> **Refinement note (2026-05-27)**: This subsection is new under the 2026-05-27 refinement. Insights are derived on demand by default (per-playbook TTL cache D-A32 wraps execution). When a user **explicitly saves/pins/attaches** an Insight (board report, client email, attached to matter record), a **snapshot** of the `InsightArtifact` envelope is persisted with evidence references frozen — providing a citeable stable reference even if the underlying data changes later.

**Persistence mechanism (D-56)**: snapshots live as `sprk_analysis` polymorphic rows with a new source-type value (NOT a new `sprk_insightsnapshot` entity). The polymorphic pattern is the existing Spaarke route for AI outputs; first-step blocker on D-A34 confirms the pattern is in use for this case (addendum §7 Q4).

**API surface (D-A34 — additions to §8.2 contracts)**:
- `POST /api/insights/snapshot` — accepts an `InsightArtifact` from a prior `/ask` response + optional `savedReason` / `savedTo`; returns snapshot ID + stable URL
- `GET /api/insights/snapshot/{id}` — returns the frozen snapshot exactly as persisted
- `GET /api/insights/snapshot/{id}/revalidate` — re-derives with original parameters; returns `{original, current, drift}` so the surface can show drift if material

**Snapshot properties**:
- Full `InsightArtifact` envelope captured as JSON (frozen at save)
- Evidence references preserved as-is (remain valid pointers even if underlying entities change)
- `superseded_by` link populated when user re-pins (preserves audit chain, allows freshening)
- Stable URL/ID citeable in reports, emails, exports
- Drift detection on revalidation surfaces material divergence without invalidating the citation

**Out of scope for Phase 1** (per SPEC §3.6): report builder integration, email template integration, drift visualization in PCF — D-A34 ships the API + persistence; UX deliverables are separate.

---

## 9. Packagability — Bicep + Deployment

### 9.1 Per-tenant deployment unit

Everything in the Engine is deployable per tenant via Bicep. The deployment unit includes:

- AI Search indexes (provisioned + initial schema)
- Cosmos account (Insight Graph)
- Function App (with all functions: sync, reconciliation, extraction, re-indexing)
- Service Bus topic + subscriptions
- Per-tenant UAMI
- Key Vault references for secrets
- App Insights connection
- Bicep parameters per tenant (tenant ID, region, search tier, Cosmos throughput, etc.)

### 9.2 Bicep module layout (proposed)

```
infra/insights/
├── main.bicep                         # entry — composes modules
├── parameters/
│   ├── tenant-acme.json
│   ├── tenant-beta.json
│   └── ...
├── modules/
│   ├── search-indexes.bicep           # AI Search indexes + schemas
│   ├── cosmos-graph.bicep             # Cosmos account + database + container
│   ├── functions.bicep                # Function App (Flex Consumption)
│   ├── servicebus-topic.bicep         # Sync topic + subscriptions
│   ├── managed-identity.bicep         # UAMI + role assignments
│   ├── keyvault-secrets.bicep         # Secret references
│   └── monitoring.bicep               # App Insights connection
└── schemas/
    ├── insight-matters.index.json     # AI Search index schema
    ├── insight-decisions.index.json
    ├── insight-risks.index.json
    └── insight-sessions.index.json
```

### 9.3 Index schemas as code

Each AI Search index is defined as JSON in the repo (`schemas/*.json`). Bicep deploys via a deployment script that calls the AI Search Management API. Versioning of schemas (`insight-matters.v2.json`) supports the schema evolution flow from §6.7.

### 9.4 Deployment script flow

```
1. Validate Bicep parameters (tenant ID, region availability)
2. Provision resources (AI Search service if not shared, Cosmos, Functions, SBus)
3. Deploy AI Search indexes from schema JSONs
4. Set up Cosmos containers + indexing policies
5. Configure Function App settings + secrets via Key Vault refs
6. Set up Service Bus topic + subscriptions
7. Run initial backfill (admin endpoint trigger)
8. Verify: smoke-test endpoints (`/healthz/insights`), assert indexes exist with expected schema
```

### 9.5 Multi-tenant control plane (deferred per D-52)

> **Refinement note (2026-05-27 — D-52 supersedes the ~10-tenant inflection)**: Under D-52 + DEF-05, the multi-tenant control-plane evolution is **deferred beyond Phase 2** pending privacy/legal work on cross-tenant federation. Phase 1 ships with the parameter-file-per-deployment pattern (D-A28); each customer = one deployment unit. The text below is preserved for historical context; revisit only if a future commercial decision overrides D-52.

For r1 with a small number of tenants — in fact, with **exactly one tenant per deployment** under D-52 — **tenant-list-as-configuration** is sufficient (each customer has a Bicep parameters file; CI deploys when files change).

~~Per knowledge research, evolve to a **tenant-list-as-data control plane** around ~10 tenants. Design TBD; not Phase 1.~~ **Deferred 2026-05-27 per D-52 + DEF-05.**

### 9.6 Per-environment vs. per-tenant

Within a tenant, you still have dev / staging / prod environments. Bicep parameters file naming captures both axes: `tenant-acme.dev.json`, `tenant-acme.prod.json`. The Bicep is identical; only parameters differ.

---

## 10. AI Inventory Reconciliation

### 10.1 What the Engine reuses (from `ai-inventory.md`)

| Insights Engine component | Existing service | Notes |
|---|---|---|
| Dataverse sync pattern | `DataverseIndexSyncService` (in `Services/RecordMatching/`) | Reuse `EntityConfig` model; migrate to event-driven Function for new indexes |
| Indexing pipeline | `RagIndexingPipeline` | Reuse for chunked Observations |
| Index admin (idempotent re-indexing) | `ReferenceIndexingService` pattern | Mirror for `insight-*` indexes |
| Retrieval (hybrid + semantic) | `RagService` + `RagQueryBuilder` + `EmbeddingCache` | Wrap with `InsightRetrievalService` for typed access |
| Chunking | `SemanticDocumentChunker` + `TextChunkingService` | Reuse as-is |
| Record search | `RecordSearchService` | Template for new `insight-matters` retrieval |
| OpenAI client | `IOpenAiClient` + `OpenAiClient` | Reuse |
| Playbook execution | `PlaybookExecutionEngine` + `INodeExecutor` registry | Closure-extraction IS a playbook ending in `DeliverToIndexNodeExecutor` |
| Tool framework | `IAiToolHandler` + `IToolHandlerRegistry` + `ToolExecutionContext` | Insights Agent tools follow this pattern |
| Chat / agent factory | `SprkChatAgentFactory` + `IChatClient` + UseFunctionInvocation | Insights Agent reuses the pipeline |

### 10.2 What's NEW

> **Refinement note (2026-05-27)**: Original list preserved; rows updated for D-54 (no separate question catalog) and rows added for D-A30 / D-A32 / D-A33 / D-A34 / D-A35 / D-A28.

| Component | Why new |
|---|---|
| `InsightsResolverService` (Zone B) | The orchestration layer doesn't exist; under D-54 it is a thin orchestrator that calls into `IInsightsAi` facade |
| `InsightsOrchestrator` + `IInsightsAi` facade (Zone A, D-A9) | Existing chat agent factory creates conversational agents; Insights orchestration wraps `PlaybookExecutionEngine` with Insights-mode caching + routing-tool registration |
| `IInsightGraph` + `CosmosNoSqlInsightGraph` | No existing graph in the stack |
| `LiveFactResolverService` | Existing services compose dataverse queries case-by-case; we centralize the Fact production |
| Derived-substrate AI Search indexes (`insight-matters`, `insight-decisions`, `insight-risks`, `insight-sessions`, `insight-precedents`) | New schemas (derived substrate per D-53) |
| Catalog index (`insights-catalog` new index OR partition of `spaarke-rag-references`, D-A33/D-57) | Routes natural-language queries → playbookId for Assistant pane |
| **Insights-mode node executors (D-A30)** — `IndexRetrieveNode`, `GraphTraverseNode`, `LiveFactNode`, `EvidenceSufficiencyNode`, `DeclineToFindNode`, `GroundingVerifyNode`, `SpeContentRetrieveNode`, `EmitObservationNode`, `ReturnInsightArtifactNode` | Extends existing 10-executor registry under `Services/Ai/Nodes/`; realizes the "tools" concept from original §5.3 as composable nodes |
| **Insights-mode playbook metadata extension (D-A31)** | New fields on Spaarke playbook schema for Insights-mode (tier, evidence rule, surfaces, routing intent statements, cache TTL, etc.) — first-step blocker on extension-vs-sibling-entity (addendum §7 Q2) |
| **Insights playbook execution cache (D-A32)** | Redis cache wrapping `PlaybookExecutionEngine` invocation for Insights-mode playbooks; cache key includes `accessibleScopeHash` + per-index staleness tokens per D-A35 |
| **Routing tool (D-A33)** | `IAiToolHandler` registered with `SprkChatAgent` for Assistant-pane natural-language routing |
| **Snapshot endpoints + `sprk_analysis` integration (D-A34)** | `POST/GET /api/insights/snapshot` with revalidate-and-drift contract; `sprk_analysis` polymorphic source-type for snapshots (NO new entity) |
| **Multi-index retrieval composition pattern (D-A35)** | Implementation pattern for `IndexRetrieveNode` composing operational + derived substrates per D-53 |
| **Signal Contract document (D-A29)** | Backward-derived enumeration of Mode A + Mode B ingestion needs from the Phase 1 question catalog per D-58 |
| **Single-tenant deployment configuration (D-A28)** | Bicep parameter file pattern: one customer = one parameter file = one deployment unit per D-52 |
| `document-extraction-design.md` (D-A12, generalized from closure-extraction) | Phase 1 design / Phase 2 implementation; pipeline composed of D-A30 nodes |
| Sync Function App (Track B) | New deployment artifact (no existing Function Apps; new with the narrowed ADR-001) |
| ~~Question catalog (Foundry agent skills or BFF config)~~ | **Withdrawn 2026-05-27 per D-54** — the question catalog IS the playbook library filtered to Insights-mode; NO parallel `sprk_insightquestion` entity |
| `IInsightArtifactStore` (abstraction over AI Search indexes for Insight artifacts) | Wraps `SearchClient` with Insight-specific operations |

### 10.3 What gets migrated (post-MVP)

> **Refinement note (2026-05-27 — D-53 boundary)**: Operational-substrate schema changes (e.g., adding `tenantId` to `spaarke-records-index`) are **explicitly out of scope for this project**. The Insights Engine consumes the operational substrate as-is per D-53 and handles the `tenantId` asymmetry at the composition layer (D-A35). Migrations below remain Phase 3 cleanup owned outside this project; they are not blockers for r1.

| Today | Future state | Why migrate | Owned by |
|---|---|---|---|
| `PlaybookIndexingBackgroundService` (BFF hosted service, ADR-001 workaround) | Move to a Function | Pure out-of-band integration; ADR-001 now permits | Phase 3 cleanup (outside this project) |
| `DataverseIndexSyncService` (BFF HTTP-coupled) | Migrate triggering to Service Bus + Function | Decouple from BFF lifecycle; align with new sync infrastructure | Phase 3 cleanup (outside this project) |
| `spaarke-records-index` (no tenantId field) | Add tenantId + backfill | Multi-tenant correctness; pattern established by Insight indexes — **explicitly out of scope for this project per D-53; not a Phase 1 blocker since D-52 single-tenant makes the privilege concern non-load-bearing** | Phase 3 cleanup (outside this project) |

These are NOT blockers for r1; they're cleanup that happens once the Engine is stable, and they're owned by teams outside the Insights Engine project.

### 10.4 What gets retired

None known. The AI inventory found no obvious dead code in the AI subsystem.

---

## 11. Phased Plan

### 11.1 Spike phase — RESOLVED via discussion (no formal spikes required)

Three potential spikes were considered and resolved through architectural discussion (see [decisions.md](decisions.md) once authored):

| Original spike | Resolution |
|---|---|
| **S1: Cosmos NoSQL vs Gremlin** | **Resolved by reasoning**: commit to Cosmos NoSQL adjacency-list. Our traversal patterns are 2-3 hops with filters (not deep graph algorithms); vector co-location is a meaningful benefit; Gremlin's strategic signals are too uncertain. `IInsightGraph` abstraction preserves swap path. See §4.2. |
| **S2: Embedding model** | **Resolved by principle**: commit to `text-embedding-3-large` (3072 dim). "No reason to knowingly underbuild" for a foundational choice that's expensive to migrate later. Cost differential is real but not material at our scale. See §4.1.4. |
| **S3: Dataverse → Service Bus auth** | **Resolved by Microsoft documentation review**: Dataverse native Azure integration uses SAS only — no Entra/MI for the webhook → Service Bus hop. Adopt the intermediate-Function pattern: Dataverse → HTTP Function (Entra) → Service Bus (UAMI). Result: zero SAS keys in pipeline. See §6.2. |

Net: skip directly to Phase 1. Decisions documented in §4 and §6.

### 11.2 Phase 1 — Foundation (8-12 weeks)

> **Refinement note (2026-05-27)**: The canonical Phase 1 deliverable list is now **[SPEC.md §3.1](SPEC.md)** (D-A1–D-A35), with [SPEC.md §8 phasing](SPEC.md) the canonical wave structure for `/project-pipeline`. The list below is preserved as a high-level historical summary; for any conflict, SPEC.md takes precedence. Key changes since the original list: D-A9 is "Insights orchestration in Zone A" (NOT a separate agent); D-A10 ships "one Insights-mode playbook end-to-end" (NOT a "question catalog entry"); the API surface (D-A11) ships `/ask` + `/relevant` + `/route` (NOT just `/ask`); D-A34 adds snapshot endpoints; D-A28–D-A35 are new.

**Goal**: Substrate + Insights orchestration in Zone A + one end-to-end Insights-mode playbook (`predict-matter-cost`) working end-to-end against mock data. (Track B sync wiring blocked on Phase C.)

Deliverables (high-level summary — see SPEC.md §3.1 for canonical list):
1. Function App (Flex Consumption) provisioned via Bicep in dev environment, with own app registration + UAMI
2. **Intake Function** (HTTPTrigger) — the auth trust boundary
   - Webhook endpoints: clientState validation initially; HMAC-SHA256 when Phase C task 044 lands. **Copy the validation code from BFF webhook handlers — do not recreate.**
   - Non-webhook endpoints: `Microsoft.Identity.Web` JWT validation, mirroring `Sprk.Bff.Api/Program.cs`
   - `AzureAd:TenantId` is the explicit tenant GUID (never `common`) — coordinate with Phase C task 047
3. Dataverse webhook registration (via plugin registration tool, NOT Power Automate, NOT custom plugin assemblies) pointing at the intake Function with `clientState` configured
4. Service Bus topic + subscriptions (UAMI role assignments via Bicep)
5. `InsightsSyncFunction` (ServiceBusTrigger, UAMI) + `InsightsReconciliation` (TimerTrigger) — covers `sprk_matter` initially
6. All outbound calls use `DefaultAzureCredential` (no `ClientSecretCredential`) — aligns with Phase C tasks 041–042
7. `insight-matters` index provisioned with schema (with tenantId; 3072-dim vectors using text-embedding-3-large)
8. `IInsightGraph` interface + `CosmosNoSqlInsightGraph` implementation
9. Graph populated from sync (Matter + Party vertices + INVOLVED_PARTY edges)
10. `LiveFactResolverService` with first 3-5 Facts (matterDuration, totalSpend, status, daysSinceLastActivity)
11. `InsightsResolverService` skeleton in `Sprk.Bff.Api/Services/Insights/`
12. `Insights Agent` with 3 tools (`FindComparableMatters`, `GetMatterFacts`, `AssessEvidenceSufficiency`)
13. One question: `predict-matter-cost` with evidence-sufficiency rule
14. `POST /api/insights/ask` endpoint
15. Context pane rendering (one Insight card)

**Phase 1 coordination requirements** (must align before / during Function work):
- Phase C task 044 (HMAC webhook validation) — copy when landed; ship with `clientState` initially
- Phase C task 047 (non-`common` TenantId in templates) — Insights Engine adopts the fix from day one
- Phase C tasks 041 + 042 (managed identity outbound) — inherit the discipline; no `ClientSecretCredential`

Acceptance criteria:
- Asking "predict cost for matter M-X" returns either an Inference with citations OR an honest insufficient-evidence response
- All trimming + provenance + tenantId enforcement verified
- Bicep deploys to a clean tenant in < 30 minutes
- **Zero SAS keys** in any part of the pipeline (the transitional `clientState` is the only shared secret and is gone after Phase C #044)
- Function App auth setup matches `Sprk.Bff.Api/Program.cs` pattern exactly for non-webhook endpoints
- All outbound credential acquisition uses `DefaultAzureCredential`

### 11.3 Phase 2 — Expansion (8-12 weeks after Phase 1)

- Additional Insight indexes (`insight-decisions`, `insight-risks`, `insight-sessions`)
- Closure-extraction JPS playbook (the compounding loop comes online)
- Additional graph entities (Person, Firm, Judge, Issue, Jurisdiction)
- Additional question templates (10+)
- Additional surfaces (form widget, ribbon flyout)
- Multi-tenant: parameter files for 2-3 customer tenants

### 11.4 Phase 3 — Production polish

- Cost / utilization monitoring (the inventory recommendations from `azure-inventory.md`)
- Schema evolution tooling (v2 index migration UI/scripts)
- Bulk re-extraction workflow
- Tenant onboarding automation (the control-plane evolution from §9.5)
- Public API + partner consumers
- Post-MVP migrations from §10.3

---

## 12. Open Questions / Deferred Decisions

### 12.1 Open (need answers before/during Phase 1)

| # | Question | Owner | When |
|---|---|---|---|
| O1 | Will the document-extraction playbook (formerly "closure-extraction" per D-A12) be JPS or a new specialized format? | Architecture | Before Phase 1 design freeze (also tracked as DEP-4 in SPEC §6.1) |
| O2 | Does `accessibleMatterSet` come from the unified access control project, or do we maintain our own source? | Architecture | Before Phase 1 sync wiring (also tracked as DEP-3 in SPEC §6.1) |
| O3 | What's the minimum `comparableMatters.min` per playbook (under D-54) — set globally or per playbook? | Product | Before first Insights-mode playbook ships |
| O4 | Outlook/Teams surface integration — Phase 2 or Phase 1? | Product | During Phase 1 (Phase 1 commits to three surfaces per D-55; Outlook/Teams remain Phase 2+ per SPEC §3.6) |

**Auth questions (AUTH-1 to AUTH-4) — RESOLVED** in §6.2.2. The remaining auth-related work is coordination with Phase C tasks 044, 047, 041, 042 (see §6.2.5).

#### 12.1.1 Spec-refinement first-step blockers (addendum §7 — embedded in per-task POMLs)

> **Refinement note (2026-05-27)**: The six questions from [SPEC-phase-1-minimum.md §7](SPEC-phase-1-minimum.md) do NOT add hard pipeline DEPs; they are embedded as **first-step blockers inside affected tasks** per SPEC §6.1. Each task's Step 0 resolves the relevant question with a documented answer before proceeding to implementation.

| # | Question | Task that owns the first-step | Method |
|---|---|---|---|
| Q1 | Confirm AI Search inventory — `spaarke-files-index` and `spaarke-invoices-index` exist as separate production indexes (per Spaarke team confirmation) | D-A29 (Signal Contract) | Run `az search index list` against the Spaarke AI Search service; document the snapshot |
| Q2 | Playbook entity extension vs. sibling configuration for Insights-mode metadata (D-A31) | D-A31 (playbook metadata extension) | Read current Spaarke playbook schema via Dataverse MCP; propose extension diff |
| Q3 | Catalog index: new index `insights-catalog` vs partition of existing `spaarke-rag-references`? | D-A33 (catalog + routing) | Decision criteria: lifecycle, access pattern, ownership |
| Q4 | Snapshot entity: confirm `sprk_analysis` polymorphic pattern is in use for this case; identify source-type field | D-A34 (snapshot endpoints) | Read `sprk_analysis` schema via Dataverse MCP; identify source-type field; raise design question if pattern not yet wired for snapshots |
| Q5 | Relevance ruleset location: in playbook metadata (D-A31) or separate configuration entity? | D-A19 (surfacing design) | Design decision documented in `surfacing-design.md` |
| Q6 | First document type for Phase 2 implementation (closing letters / closure summaries are the natural starting point) | D-A12 (document-extraction-design) | Product/architecture call; documented in `document-extraction-design.md` |

### 12.2 Deferred (can answer post-MVP)

| # | Question | When |
|---|---|---|
| D1 | Add graph layer for true multi-hop traversal queries (vs. adjacency-list)? | Reassess after Phase 1 spike |
| D2 | When to bump AI Search to S2 tier? | When indexed artifacts approach 5M per tenant |
| D3 | Move `PlaybookIndexingBackgroundService` from BackgroundService to Function | Phase 3 cleanup |
| D4 | Migrate `spaarke-records-index` to add tenantId | Phase 3 cleanup |
| D5 | Tenant control plane (config → data) | When tenant count approaches 10 |
| D6 | Add Foundry-hosted agent for separate multi-day diligence surfaces | When those surfaces are designed |
| D7 | Cross-tenant insights (e.g., industry benchmarks) — only if explicitly opted-in by tenants | Possibly Phase 3+; major privacy/legal work |

### 12.3 Hard "do not do" decisions

For clarity, things we have decided NOT to do. **Canonical list is now [decisions.md "Explicit do not do — the negative space"](decisions.md)** (extended 2026-05-27 with 8 new entries from D-52–D-58). Summary:

- **Do not host the Insights orchestration in Foundry** (per §5.1; D-13)
- **Do not use Durable Functions** (ADR-001; D-20 — orchestration via Service Bus + state machine)
- **Do not put document content in cross-matter aggregates** (privilege leakage — §7.4)
- **Do not let any new derived-substrate index lack a `tenantId` field** (§4.1, §10.3; D-12 — retained per D-52 for partition keys + future federation)
- **Do not require human curation of identity resolution** (§4.2.4, §6.5; D-29)
- **Do not return generic AI hedging when Inference evidence is insufficient** (§5.4 — explicit insufficient-evidence response via `DeclineToFindNode`; D-06 + D-49)
- **Do not create a `sprk_insightquestion` entity** (D-54 — questions ARE playbooks; the catalog IS the playbook library filtered to Insights-mode)
- **Do not create a `sprk_insightsnapshot` entity** (D-56 — snapshots ARE `sprk_analysis` rows)
- **Do not re-project record fields already in `spaarke-records-index` into derived `insight-*` indexes** (D-53, D-58 — Mode A is narrow derived projection ONLY)
- **Do not silently rely on `tenantId` filtering on the operational substrate leg** of cross-substrate compositions (D-53 — `spaarke-records-index` lacks the field; D-52 makes this safe at privilege layer but composition logic must remain correct under future federation)
- **Do not let the LLM synthesize Insight answers in the Assistant pane** (D-55, D-57 — the LLM routes the request to a playbook; the playbook with `GroundingVerifyNode` produces the answer with provenance)
- **Do not modify schemas of operational substrate indexes** as part of this project (D-53 — operational substrate consumed as-is; schema fixes are Phase 3 cleanup owned elsewhere)
- **Do not treat Phase 1 multi-tenant infrastructure** (separate tenant control plane, cross-tenant federation) **as in-scope** (D-52 — deferred beyond Phase 2 pending privacy/legal work)

---

## 13. References

### 13.1 Internal documents

- [README.md](README.md) — Project README
- [ai-inventory.md](ai-inventory.md) — AI subsystem inventory (DI-anchored)
- [azure-inventory.md](azure-inventory.md) — Azure resource inventory
- [Spaarke AI Platform Unification (r2) design](../spaarke-ai-platform-unification-r2/design.md) — the design that surfaced the need for this Engine
- [ADR-001](../../docs/adr/ADR-001-minimal-api-and-workers.md) — BFF runtime + Function permissions (commit `84cec9f9`)
- [ADR-013](../../docs/adr/ADR-013-ai-architecture.md) — AI architecture
- [`.claude/AUDIT-FINDINGS-AUTH-SYSTEM.md`](../../.claude/AUDIT-FINDINGS-AUTH-SYSTEM.md) — active auth design canon (until ADR-027 lands)
- [`.claude/patterns/auth/obo-flow.md`](../../.claude/patterns/auth/obo-flow.md) — OBO server-side flow pattern
- [`Sprk.Bff.Api/Program.cs`](../../src/server/api/Sprk.Bff.Api/Program.cs) — reference inbound JWT validation setup
- [`Sprk.Bff.Api/Infrastructure/DI/AuthorizationModule.cs`](../../src/server/api/Sprk.Bff.Api/Infrastructure/DI/AuthorizationModule.cs) — reference authorization wiring
- [Legal IQ Stack article](https://spaarke.com/why-spaarke/the-iq-stack) — the marketing positioning this Engine honestly delivers

### 13.2 Knowledge base (researcher-authored, 2026-05-19)

- [knowledge/azure-ai-search/](../../knowledge/azure-ai-search/) (with `insights-engine-supplement.md`)
- [knowledge/cosmos-gremlin/](../../knowledge/cosmos-gremlin/)
- [knowledge/azure-functions-isv/](../../knowledge/azure-functions-isv/)
- [knowledge/dataverse-sync/](../../knowledge/dataverse-sync/)
- [knowledge/foundry-memory-patterns/](../../knowledge/foundry-memory-patterns/)

### 13.3 External (current MS guidance, accessed 2026-05-19)

- Azure AI Search vector search: `learn.microsoft.com/en-us/azure/search/vector-search-overview` (updated 2026-05-13)
- AI Search integrated vectorization: `learn.microsoft.com/en-us/azure/search/vector-search-integrated-vectorization` (updated 2026-03-18)
- Cosmos Gremlin overview: `learn.microsoft.com/en-us/azure/cosmos-db/gremlin/overview`
- Cosmos vector search: `learn.microsoft.com/en-us/azure/cosmos-db/vector-search`
- Azure Functions Flex Consumption: `learn.microsoft.com/en-us/azure/azure-functions/flex-consumption-plan`
- Multi-tenant deployment: `learn.microsoft.com/en-us/azure/architecture/guide/multitenant/approaches/deployment-configuration` (updated 2026-04-30)
- Dataverse webhooks: `learn.microsoft.com/en-us/power-apps/developer/data-platform/use-webhooks` (updated 2026-04-01)
- Dataverse Azure integration: `learn.microsoft.com/en-us/power-apps/developer/data-platform/azure-integration` (updated 2026-04-01)
- Foundry agent memory (concepts): `learn.microsoft.com/en-us/azure/foundry/agents/concepts/what-is-memory` (updated 2026-05-19)
- Foundry agent memory (usage): `learn.microsoft.com/en-us/azure/foundry/agents/how-to/memory-usage` (updated 2026-05-18)
