# Spaarke Insights Engine — Phase 1.5 Design (Project r2)

> **Project**: `ai-spaarke-insights-engine-r2`
> **Phase**: 1.5 (continuation of Phase 1 / project r1)
> **Created**: 2026-05-30
> **Status**: design — pre-pipeline
> **Predecessor**: [`ai-spaarke-insights-engine-r1`](../ai-spaarke-insights-engine-r1/) (Phase 1; 17 D-P deliverables shipped + deployed)
> **Related architecture**: [`docs/architecture/INSIGHTS-ENGINE-ARCHITECTURE.md`](../../docs/architecture/INSIGHTS-ENGINE-ARCHITECTURE.md) §0a — Phase 1 completion + Phase 1.5 framing
> **Related guide**: [`docs/guides/INSIGHTS-ENGINE-GUIDE.md`](../../docs/guides/INSIGHTS-ENGINE-GUIDE.md) — operator/developer reference

---

## 0. Why this project exists

Phase 1 (r1) built the Insights Engine foundation: 4-tier `InsightArtifact` envelope, JPS-extending node executors, `spaarke-insights-index` substrate, `IInsightsAi` facade, the `predict-matter-cost@v1` synthesis playbook, and the universal-ingest pipeline. All 17 D-P deliverables shipped and Phase 1 plumbing is verified live on Spaarke Dev.

Live verification surfaced two categories of follow-on work:

1. **A discrete unblock**: the `predict-matter-cost` synthesis playbook is deployed but doesn't fire end-to-end live because the new Insights node executors were never bound to `sprk_analysisaction` rows (the JPS dispatch contract). Engine logs "Node X in batch 1 failed - stopping playbook execution" and the orchestrator returns a defensive scaffold decline. Half-day fix.

2. **Architectural maturation**: the broader design discussion on 2026-05-30 surfaced four corrections to the Phase 1 framing that need follow-on implementation, plus two architectural additions that the project owner identified as critical for the system to be useful at the scale Spaarke serves:

| Correction / Addition | Phase 1 state | Phase 1.5 target |
|---|---|---|
| Insights IS a JPS application (not "extends JPS") | Mostly true except `IngestOrchestrator` | Universal-ingest refactored to JPS playbook; ALL Insights workflows are JPS |
| Prompts in source files | `classification.v1.txt`, `outcome-extraction.v1.txt`, `predict-matter-cost-synthesis.v1.txt` in source | Prompts in `sprk_analysisaction.sprk_systemprompt` (**existing** JPS primitive — already used by r1 for non-Insights actions like "Classify Document"); SME-iterable, per-tenant overridable. **No new `sprk_prompt` entity.** |
| Single ingest playbook = canonical (don't multiply) | N/A | One `universal-ingest@v1` JPS playbook with parameterized config — flexibility via config, not via spawning new playbooks |
| Classification taxonomy 1D (litigation-biased) | 8 categories hard-coded | 2D: practice-area × document-type; per-practice-area Layer 1 + Layer 2 prompts. Practice-area dimension sourced from the **existing `sprk_practicearea_ref` Dataverse table** (APPL, BNKF, CTRNS, IPPAT, IPTM, MA, …) — the table IS the source of truth; no hardcoded list in code or docs |
| Subject scheme = `matter:` only | `DataverseLiveFactResolver` queries `sprk_matter` only | Subject schemes: `matter:`, `project:`, `invoice:`, future entities; per-entity live-fact resolvers; scope shape generalized |
| Consumption surface = single endpoint | `POST /api/insights/ask` only | Hybrid: playbook + generic RAG; intent classifier; Assistant integration |
| Ingest opt-in | Job handler wired but no producer-side surface to set the flag | Producer-side surface decided + implemented |

This project executes the Phase 1.5 work above. The Phase 1.5 acceptance bar is: **the Insights Engine is usable across practice areas, across entity types, via both pre-authored playbooks and ad-hoc RAG, with prompts SME-iterable in JPS** — and the `predict-matter-cost@v1` end-to-end actually executes against real Spaarke Dev data.

---

## 1. Scope

### In scope

**Wave A — Foundations (documentation + design)**
- A1. Architectural overview (the §0a + this design.md are A1; refresh existing arch doc as Phase 1.5 evolves)
- A2. Operator/developer guide (`docs/guides/INSIGHTS-ENGINE-GUIDE.md` — authored at project start; refined per wave)
- A3. 2D taxonomy design — practice-area × document-type entity + matrix
- A4. Prompts-in-JPS design — storage shape, versioning, per-tenant override pattern
- A5. Universal-ingest JPS refactor design — node breakdown, parameterization model
- A6. Multi-entity subject design — scheme parsing, resolver pattern, index scope shape

**Wave B — Unblock synthesis (the critical half-day fix)**
- B1. Create 6 `sprk_analysisaction` rows in Spaarke Dev for new Insights ActionTypes
- B2. Update `predict-matter-cost.playbook.json` to reference action codes
- B3. Update `Deploy-Playbook.ps1` to require action wiring (lint check)
- B4. Re-deploy playbook with action references
- B5. Live smoke verification — predict-matter-cost end-to-end with real matter, real LLM calls
- B6. Update Phase 1 verification doc with closed-gap status

**Wave C — JPS compliance refactor**
- C1. Universal-ingest → JPS playbook (`universal-ingest@v1` in Dataverse with N nodes: Sanitizer, Layer1, ConditionGate, Layer2, GroundingVerify, ObservationEmitter, IndexUpsert, Mirror)
- C2. Prompts → JPS scope storage (likely new `sprk_prompt` entity OR per-playbook `sprk_configjson` — design choice in A4)
- C3. Retire `IngestOrchestrator.cs` (and orphaned interfaces) once JPS-native universal-ingest is verified
- C4. Update `IInsightsAi.RunIngestAsync` to invoke the JPS universal-ingest playbook
- C5. Universal-ingest **parameterization** — the canonical playbook accepts config parameters at invocation (tenant overrides, per-practice-area routing hints, cost-cap override) — eliminating need for many ingest playbooks

**Wave D — 2D taxonomy + multi-entity**
- D1. `sprk_documenttype_ref` entity + practice-area N:N matrix
- D2. Per-practice-area Layer 1 classification (initial: litigation, real-estate, patent, transactional)
- D3. Per-(practice-area, document-type) Layer 2 extraction schemas (initial: 3-5 high-value combinations)
- D4. Universal-ingest playbook routes classification + extraction based on `sprk_matter.sprk_practicearea` and document parent entity type
- D5. Multi-entity subject support: subject schemes `matter:`, `project:`, `invoice:`; per-entity `ILiveFactResolver` implementations; LiveFactNode config extension
- D6. `spaarke-insights-index` scope shape generalization (carry `entityType`, `projectId`, `invoiceId` etc. alongside `matterId`)
- D7. Test fixtures across practice areas + entity types

**Wave E — Hybrid consumption + Assistant integration**
- E1. `POST /api/insights/search` — generic RAG retrieval endpoint
- E2. Intent classifier — natural-language → playbook routing OR generic RAG fallback
- E3. Spaarke Assistant integration — Insights as a callable tool in the existing chat surface
- E4. Documentation: when-to-author-playbook-vs-rely-on-RAG decision tree

### Out of scope (Phase 2 or later)

- **Field auto-populations** (Dataverse triggers → `/api/insights/ask` → field render) — Phase 2
- **Customer-facing playbook authoring UI** (SME builder) — Phase 2/3
- **AI-directed playbook authoring** (natural language → drafted playbook) — Phase 3
- **Cosmos NoSQL graph backend** (D-P17 promised first Phase 1.5 deliverable; deferred to Phase 2 because Wave D + E priorities exceed budget)
- **Per-tenant monthly cost caps with hard enforcement** — Phase 1.5 still observability-only (per Phase 1 design)
- **Multi-tenant onboarding workflow** — single-tenant deployment model continues per D-52
- **SME-facing prompt iteration UI** — Phase 2 after Wave C2 storage shape stabilizes
- **MCP server contract** — Phase 1.5 still defers to Phase 2

---

## 2. Phase 1.5 acceptance bar

**Functional**:
1. `POST /api/insights/ask` with `question: "predict_matter_cost_v1"` on a real Spaarke Dev matter returns either a real `InsightArtifact` (with ≥12 evidence refs from real Observations) OR a real `DeclineResponse` (with structured `MinimumEvidenceNeeded` from `EvidenceSufficiencyNode`) — NOT the defensive scaffold decline
2. `POST /api/insights/ask` with `question` referring to a real-estate, patent, or transactional matter routes through the appropriate practice-area Layer 1/Layer 2 prompts during ingest
3. `POST /api/insights/ask` with `subject: "project:<guid>"` or `subject: "invoice:<guid>"` resolves live facts from the correct entity and returns the appropriate Inference
4. `POST /api/insights/search` (NEW) accepts a natural-language query, returns top-N ranked Observations/Precedents with an LLM-synthesized summary
5. Spaarke Assistant can invoke either path (playbook OR RAG) via intent classifier when a user asks an Insights-shaped question in chat
6. SME can edit a prompt (e.g., adjust extraction threshold language for "leniency clauses" in lease extraction) without a code deploy — the change propagates via JPS scope reload

**Architectural**:
7. `IngestOrchestrator` code path is retired; universal-ingest is a JPS playbook deployed via `Deploy-Playbook.ps1`
8. All prompts live in Dataverse-managed JPS scope storage (per Wave A4 design); zero `.txt` prompt files in `Services/Ai/Insights/Prompts/`
9. Every Insights playbook node has a `sprk_analysisaction` reference; `Deploy-Playbook.ps1` lint check prevents future regressions
10. `DataverseLiveFactResolver` abstracted; per-entity resolvers registered for matter, project, invoice
11. §3.5 grep gate continues to pass — zero forbidden Zone B imports across the new code
12. `IInsightGraph` stub remains (Cosmos still deferred — explicit Phase 2)

**Quality**:
13. Eval harness runs across at least 3 practice areas × 2 entity types (≥6 question-shape combinations) with passing baseline metrics
14. Live smoke runbook executes end-to-end (ingest a fixture → Observations produced → Precedent confirmed → projection → /ask returns real Inference) on Spaarke Dev
15. SME calibration loop: ≥50 sampled Observations marked `Pending Review` flow through the disposition queue; SME marks dispositions; iteration cycle on prompts produces measurable accuracy improvement

---

## 3. Architectural decisions (Phase 1.5)

### D-P15-01: Insights IS a JPS application (canonical framing)

**Terminology** (load-bearing — these terms got conflated in earlier drafts):
- **JPS** = the JSON Prompt Schema, i.e. the **data format**. JPS itself is data, not code. JPS data lives in Dataverse on `sprk_analysisaction.sprk_systemprompt` (with `$schema`, `$version`, `instruction { role, task, constraints, context }`, `input`, `parameters`) and on `sprk_playbook` rows.
- **`PlaybookExecutionEngine`** = the **code component in `Sprk.Bff.Api`** that executes JPS-defined work. Earlier drafts loosely called this "the JPS engine."
- **`INodeExecutor`** = code-side handler for a specific analysis-action TYPE.

All Insights workflows are realized as JPS playbooks (data) executed by the existing `PlaybookExecutionEngine` (code). Insights contributes new `INodeExecutor` implementations (LiveFactNode, IndexRetrieveNode, EvidenceSufficiencyNode, DeclineToFindNode, ReturnInsightArtifactNode, GroundingVerifyNode), adds a new scope substrate (`spaarke-insights-index`), and adds a stable facade (`IInsightsAi`). **The JPS schema and `PlaybookExecutionEngine` itself are not modified by Insights.** NO parallel orchestrators. NO code-defined playbooks except via interim migration during Wave C.

### D-P15-02: One canonical universal-ingest playbook, parameterized

`universal-ingest@v1` is THE playbook for document ingest. Flexibility comes through:
- Invocation parameters (per-tenant, per-practice-area, per-document-type hints)
- Per-playbook config (cost cap, thresholds, prompts version)
- Conditional node routing inside the playbook (Layer 1 result → per-practice-area Layer 2 prompt selection)

NOT through spawning many ingest playbooks. This keeps the canonical pattern simple and predictable.

If a use case genuinely needs a different ingest flow (e.g., a Phase 2 OCR-first pipeline), it becomes a new playbook with a different name (`universal-ingest-ocr@v1`) — but the default expectation is to extend the canonical playbook via parameters.

### D-P15-03: Multi-entity subject scheme

Subject parsing in `InsightAskRequest` accepts `<entityType>:<id>` where `entityType` ∈ {matter, project, invoice, ...}. The endpoint validates the scheme is registered (config-driven catalog of supported entities). The downstream `LiveFactNode` reads the entity type from subject + dispatches to the appropriate `ILiveFactResolver` implementation (registered keyed by entity type).

The `spaarke-insights-index` scope shape generalizes to carry `scope.entityType`, `scope.entityId`, alongside the existing `scope.matterId` (which becomes redundant but stays for backward compatibility with Phase 1 Observations).

Per-entity playbooks (e.g., `predict-project-completion@v1`, `flag-invoice-anomaly@v1`) target specific entity types via their `parameterSchema.entityType` constraint; the facade rejects mismatched subjects.

### D-P15-04: 2D classification taxonomy with per-practice-area routing

Document classification is parameterized over practice area:
- `sprk_practicearea_ref` (existing) is the first dimension; the **table IS the source of truth** (APPL, BNKF, CTRNS, IPPAT, IPTM, MA, …) — no hardcoded list anywhere in code or docs
- `sprk_documenttype_ref` (NEW Phase 1.5) is the second dimension; per-practice-area lookup
- N:N matrix entity `sprk_practicearea_documenttype` (NEW Phase 1.5) carries which document types are valid for which practice area
- Per-practice-area Layer 1 classification: handled either by **parametric injection** into a single `sprk_analysisaction` row (`parameters.categories` array, `parameters.practiceAreaContext` string) OR by **per-practice-area variant action rows** (e.g., action codes `INSIGHTS.LAYER1_CLASSIFY.CTRNS`, `INSIGHTS.LAYER1_CLASSIFY.IPPAT`) — Wave A4 decides
- Per-(practice-area, document-type) Layer 2 extraction: similarly via variant `sprk_analysisaction` rows or parametric injection per Wave A4

Universal-ingest playbook reads `sprk_matter.sprk_practicearea` (or per-entity equivalent) to select the appropriate Layer 1 action; Layer 1 output (document type) drives selection of the appropriate Layer 2 action.

`outcomeBearing` as a gate is **retired in its current form** — replaced by per-(practice-area, document-type) gate logic. For Commercial Transactions (CTRNS), the gate becomes "is this a closing-statement / asset-purchase / financing doc?" For IP Patents (IPPAT), "is this a patent application / office action / issued patent?" For Banking & Finance (BNKF), "is this a loan agreement / security agreement / payoff?" Each practice area defines its own gate semantics in its Layer 2 schema. The example set is illustrative — the initial Wave D2 scope picks the top N practice areas from `sprk_practicearea_ref` based on SME readiness (see Q-D2-1).

### D-P15-05: Prompts move from `.txt` files into the existing `sprk_analysisaction.sprk_systemprompt` primitive

**Correction from earlier draft**: prior versions of this design proposed introducing a new `sprk_prompt` entity. That is **superseded**. `sprk_analysisaction.sprk_systemprompt` already serves this role and is in active use in r1 — the existing "Classify Document" action row carries its full JPS prompt (`$schema`, `$version`, `instruction { role, task, constraints, context }`, `input`, `parameters`) in this field. Adding a parallel `sprk_prompt` entity would duplicate an existing primitive.

**How `sprk_analysisaction` relates to other JPS primitives**:

| Primitive | Role | Edited by |
|---|---|---|
| `sprk_analysisaction` (existing) | **Prompt-bearing dispatch row.** Carries action code, action type, JPS-formatted system prompt in `sprk_systemprompt`, parameter schema, output schema, tags. ONE row per action variant. | SMEs (prompt content) + devs (when adding new action types) |
| `sprk_playbook` + `sprk_configjson` (existing) | Playbook definition with per-playbook config blob: cost cap, thresholds, and inline prompt templates that exist only for that playbook | Playbook author |
| `sprk_prompt` | **Not introduced.** Would duplicate `sprk_analysisaction.sprk_systemprompt`. | n/a |

**Phase 1.5 prompt storage approach**:

- **All Insights prompt content** lives in `sprk_analysisaction.sprk_systemprompt` (JPS-formatted JSON). The 6+ new Insights action rows created in Wave B and additional rows added in Waves C/D/E each carry their canonical prompt.
- **Playbook-specific inline templates** (e.g., the synthesis template owned exclusively by `predict-matter-cost@v1`) may still live in `sprk_playbook.sprk_configjson` since they have no reuse value across playbooks.
- **No new `sprk_prompt` entity.**

**Wave A4 design doc decides the mechanism for variants, versioning, and per-tenant override WITHIN `sprk_analysisaction`**:

- **Variants** (per-practice-area): (a) variant action rows per practice area (e.g., action codes suffixed `_CTRNS`, `_IPPAT`); playbook nodes resolve the action code at invocation based on the matter's practice area. OR (b) parametric JPS injection — a single action row with runtime parameters per practice area (`parameters.categories`, `parameters.practiceAreaContext`).
- **Versioning**: new action row per version vs. version field on existing row. Initial leaning: new row per version (matches the "Classify Document" pattern of immutable rows; old versions remain queryable for rollback + A/B testing).
- **Per-tenant overrides**: tenant-scoped variant rows with fallthrough query (tenant-specific → global), vs. override mapping table, vs. tenant blocks within the JPS schema.

Wave A4 picks with explicit reasoning.

### D-P15-06: Hybrid playbook + ad-hoc RAG

The Insights Engine exposes two consumption paths:

1. **`POST /api/insights/ask`** — invokes a pre-authored JPS playbook. Structured output, evidence-sufficiency rules, structured Decline. Use for high-value/structured questions.
2. **`POST /api/insights/search`** (NEW Phase 1.5) — invokes a generic RAG flow: semantic search of `spaarke-insights-index` filtered by subject scope + (optional) `artifactType`, `predicate`, etc., then LLM synthesis with grounded citations. Use for open-ended natural-language questions.

The Spaarke Assistant intent classifier examines the user's question and routes to either path. Phase 1.5 ships a simple LLM-based intent classifier; Phase 2+ can replace with embedding-based routing or other approaches.

The same `spaarke-insights-index` substrate serves both paths — both query the same Observations + Precedents. There is no duplication of storage or extraction logic.

### D-P15-07: Phase 1.5 still defers Cosmos NoSQL graph (D-P17)

Cosmos was promised as the first Phase 1.5 deliverable in r1 SPEC §3.3. Phase 1.5 re-defers because Wave D + E priorities (practice-area depth + consumption surface) deliver more user-visible value than graph queries against an empty graph. `IInsightGraph` stub remains; Cosmos implementation moves to Phase 2 explicitly.

### D-P15-08: Ingest opt-in surface

The producer-side surface to set `AiProcessingOptions.InsightsIngest = true` is implemented as **per-tenant policy** (default opt-in for Insights-enabled tenants) with **per-upload override header** for testing:

- Tenant config flag: `Tenants:<TenantId>:Insights:IngestEnabled` (default false for safety; tenant onboarding opts in)
- Upload header override: `X-Ai-Insights-Ingest: false` can disable ingest for a specific upload (e.g., bulk imports where the operator doesn't want LLM cost)
- The job handler honors either signal

Rationale: most production scenarios want consistent behavior per tenant. Per-upload header is for the edge cases.

### D-P15-09: `outcomeBearing` retired; replaced with practice-area-specific gates

Phase 1's binary `outcomeBearing` flag was a `predict-matter-cost`-specific gate baked into Layer 1. Phase 1.5 retires it. Each practice area's Layer 1 prompt emits practice-area-appropriate signals — e.g., for Commercial Transactions (CTRNS): `is_closing_statement`, `is_asset_purchase`, `is_financing_agreement`; for IP Patents (IPPAT): `is_patent_application`, `is_office_action`, `is_issued_patent`; for Banking & Finance (BNKF): `is_loan_agreement`, `is_security_agreement`, `is_payoff_letter`. The specific signals per practice area are authored during Wave D2; the example set is illustrative only. The universal-ingest playbook's conditional gate consults the practice-area-specific Layer 2 schema to determine whether the document warrants Layer 2 extraction.

This is a larger redesign than just renaming a field — it requires per-practice-area prompt authoring (Wave D2) and per-practice-area gate logic in the universal-ingest playbook (Wave D4). Phase 1 fixtures remain compatible because their practice area defaults to the litigation/CTRNS-equivalent in Spaarke Dev seed data; new fixtures generated in Wave D7 are synthetic per practice area (per Owner Clarification TF-1 in `spec.md`).

---

## 4. Wave plan + sequencing

```
Wave A (Foundations)             ── design docs; sets framing for all subsequent waves
       │
       ▼
Wave B (Unblock synthesis)       ── half-day surgical fix; predict-matter-cost works live
       │                            (parallel-safe with A, but B's output proves Phase 1 design)
       ▼
Wave C (JPS compliance)          ── universal-ingest → JPS; prompts → JPS storage
       │                            (depends on A4, A5)
       ▼
Wave D (2D taxonomy + multi-entity) ── practice-area depth; project/invoice subjects
       │                            (depends on C complete; A3 + A6 designs)
       ▼
Wave E (Hybrid + Assistant)      ── generic RAG endpoint; intent classifier; Assistant tool
                                    (depends on D complete for full breadth of subjects/practice areas)
                                    (A6 design)
```

Estimated effort:
- Wave A: ~4 days
- Wave B: ~½ day
- Wave C: ~4-6 days
- Wave D: ~1.5-2 weeks
- Wave E: ~1.5-2 weeks

**Total Phase 1.5 scope: ~5-7 weeks** of focused work. Parallelization possible within waves (D and E touch different code areas) but cross-wave dependencies enforce the A → B → C → D → E ordering.

---

## 5. Open design questions (resolve during Wave A)

### Q-A4-1: Phase 1.5 prompt-variant + versioning + per-tenant-override pattern within `sprk_analysisaction`

(See D-P15-05 — no new entity introduced; refinement of the original "where do prompts live" question.) Wave A4 picks:

- **Variant pattern** — variant action rows per practice area (e.g., `_CTRNS`, `_IPPAT` suffixes) vs. parametric JPS injection within a single action row (`parameters.categories` array, `parameters.practiceAreaContext` string)
- **Versioning model** — new action row per version vs. version field on existing row (initial leaning: new row per version, matching the "Classify Document" immutable-row pattern)
- **Per-tenant override** — tenant-scoped variant rows with fallthrough query (tenant-specific → global) vs. override mapping table vs. tenant blocks within the JPS schema

Each option with explicit reasoning and a rollback / A-B test story.

### Q-A5-1: How many nodes in the JPS universal-ingest playbook?

The current `IngestOrchestrator` has 8 logical steps (Sanitize, Layer1Classify, Layer1Emit, Layer2Trigger, Layer2Extract, Layer2Validate, GroundingVerify, ObservationEmit + Index Upsert + Mirror as a combined final step). The JPS refactor needs to decide:
- Are these 1:1 with JPS nodes (8 nodes) or coalesced (5-6 nodes)?
- What's the right granularity for parameterization and per-node observability?

Wave A5 design doc resolves.

### Q-A6-1: Per-entity resolver naming + registration pattern

DI registration via:
- Option (a) `IDictionary<string, ILiveFactResolver>` keyed by entity type name
- Option (b) `ILiveFactResolverRegistry` service that routes
- Option (c) `ILiveFactResolver` with a method `bool CanResolve(string entityType)` and the engine iterates

Wave A6 design picks. Option (a) is the simplest; option (c) is more extensible.

### Q-D6-1: `spaarke-insights-index` scope shape evolution

Phase 1 index has `scope.matterId`. To support project + invoice subjects, options:
- Option (a) Add `scope.projectId`, `scope.invoiceId` as additional filterable fields (index re-create required)
- Option (b) Generalize to `scope.entityType` + `scope.entityId` (cleaner; requires migration of Phase 1 Observations)
- Option (c) Hybrid: keep `scope.matterId` for backward compatibility + add `scope.entityType` + `scope.entityId` for new Observations

Wave D6 design decides + plans migration.

### Q-D2-1: Initial practice-area scope

Phase 1.5 can't realistically implement all of Spaarke's practice areas in one project. The full set lives in the **existing `sprk_practicearea_ref` table** in Spaarke Dev (visible rows include APPL Appellate, BNKF Banking & Finance, CTRNS Commercial Transactions, IPPAT Intellectual Property Patents, IPTM Intellectual Property Trademarks, MA Mergers & Acquisitions — and any others added since this design was authored). **The table IS the source of truth**; no hardcoded list in code or docs.

Wave A3 selects the **top 3 practice areas** for the initial Wave D2 implementation based on:
- **SME readiness** — which areas have prompt authors available
- **Document variety** — which areas exercise more of the 2D taxonomy
- **Strategic priority** — which areas the project owner identifies as highest-value for Phase 1.5 rollout

Wave D3 picks 3 (practice-area, document-type) pairs for Layer 2 extraction schemas. Additional practice areas land per customer onboarding cadence post-Phase 1.5.

### Q-E2-1: Intent classifier approach

Options for Wave E2:
- Option (a) LLM-based classifier (small model, cheap; cold start latency)
- Option (b) Embedding-based routing (query embedded; compared to playbook description embeddings; cheapest at scale)
- Option (c) Keyword + rule-based (simplest; brittle to phrasing variation)

Wave E2 starts with option (a) for Phase 1.5 (cheap to iterate; easy to debug); option (b) is the Phase 2 optimization.

---

## 6. Risks + mitigations

| Risk | Probability | Impact | Mitigation |
|---|---|---|---|
| JPS engine doesn't cleanly support some Insights-mode semantics (e.g., conditional branch routing for sufficient/insufficient paths) | Medium | High | Wave B exercises the engine; Wave A5 design surfaces gaps; engine patches stay in JPS engine (don't fork) |
| Prompt migration from source files to Dataverse changes runtime behavior subtly | Low | High | Side-by-side comparison harness during migration; new prompts get `@v2` versions; cutover playbook-by-playbook with rollback |
| Per-practice-area prompt authoring is more time per area than estimated | Medium | Medium | Initial Wave D scope = 3 practice areas; expand per customer onboarding cadence; SMEs draft prompts; developers refine |
| `spaarke-insights-index` schema migration disrupts Phase 1 Observations | Medium | Medium | Wave D6 design includes migration plan; new fields nullable; old Observations stay queryable |
| Intent classifier mis-routes high-value questions to RAG path (lower quality output) | Medium | Medium | Phase 1.5 ships explicit fallback: caller can pass `forceMode: "playbook"` or `forceMode: "rag"` to override; classifier confidence threshold tunable |
| Spaarke Assistant integration uncovers facade contract gaps | Low | Medium | Wave E3 starts with read-only "tool call" semantics; full bidirectional integration is Phase 2 |
| Field auto-population (Phase 2) reveals latency issues not visible in chat usage | N/A this phase | Phase 2 risk | Cache layer (D-P13) plus per-playbook TTL tunability addresses; Phase 1.5 measures playbook latencies in eval harness for baseline |

---

## 7. Dependencies + interactions

### Internal Spaarke dependencies

- **JPS engine** — Phase 1.5 contributes back to JPS (any engine patches needed for Insights semantics); risk: JPS is also evolving (`ai-advanced-capabilities-development` project), coordinate
- **Spaarke Assistant** — Phase 1.5 Wave E3 integrates Insights as a tool; coordinate with Assistant team on tool-call contract
- **Dataverse schema evolution** — Wave D1 adds new entities (`sprk_documenttype_ref`, `sprk_practicearea_documenttype`); coordinate with Dataverse schema owners
- **`spaarke-files-index`** — universal-ingest reads chunks from here; if SDAP changes its schema, Insights ingest breaks (currently uses dynamic SearchDocument access to defend)

### External dependencies

- **Azure OpenAI** — Layer 1 (gpt-4o-mini), Layer 2 (gpt-4o), embeddings (text-embedding-3-large); ensure deployments stable in Spaarke Dev
- **Azure AI Search** — `spaarke-insights-index` continues to exist; Wave D6 migration may require index re-create (verify with infra team)
- **Dataverse availability** — JPS engine reads playbook + nodes from Dataverse on every invocation; cache TBD if latency becomes an issue

### Cross-project coordination

- **`sdap-bff-api-remediation-fix`** project — `IInsightsAi` facade is part of the broader BFF AI facade work; coordinate any facade contract changes
- **`ai-advanced-capabilities-development`** — LAVERN-style platform primitives (Sanitizer, GroundingVerifier) may evolve; Phase 1.5 Insights consumers should stay aligned

---

## 8. Phase 1 → Phase 1.5 handoff checklist

Before Phase 1.5 implementation starts:

- [x] Phase 1 acceptance verified (plumbing complete + deployed) — 2026-05-30
- [x] Architecture doc §0a captures Phase 1 completion + Phase 1.5 framing
- [x] Operator guide authored
- [x] Phase 1.5 design.md (this document) authored
- [ ] Phase 1 wrap-up (project r1 task 090) — lessons-learned + Phase 1.5 priority outline
- [ ] Phase 1 deployment-verification doc updated with final state + known gap
- [ ] r2 project initialized via `/project-pipeline` (SPEC.md → plan.md → task decomposition)
- [ ] Wave A foundation docs queued as first task batch in r2

---

## 9. Phase boundaries summary

| Phase | What | Status |
|---|---|---|
| **Phase 1 (r1)** | Plumbing + one example playbook (predict-matter-cost) + universal-ingest as code | ✅ shipped + deployed |
| **Phase 1.5 (r2)** | JPS compliance + 2D taxonomy + multi-entity subjects + hybrid consumption | this project |
| **Phase 2** | Cosmos NoSQL graph + customer playbook authoring UI + field auto-populations + MCP server | future |
| **Phase 3** | AI-directed playbook authoring + per-tenant SME workflows + customer onboarding scale-out | future |

---

## 10. Next actions

1. **Switch context** to the r2 project directory (`projects/ai-spaarke-insights-engine-r2/`) for subsequent work
2. **Run `/project-pipeline`** with this design.md as the design input; pipeline authors SPEC.md + plan.md + task decomposition
3. **Begin Wave A** — foundation design docs (A3, A4, A5, A6); refresh A1+A2 as they evolve
4. **Then Wave B** — unblock synthesis; immediate visible win
5. **Then Wave C → D → E** per sequencing

---

*This document is the canonical Phase 1.5 reference. Updates land here via PR until r2 project's `SPEC.md` supersedes for task-execution-level detail. The §0a callout in [`docs/architecture/INSIGHTS-ENGINE-ARCHITECTURE.md`](../../docs/architecture/INSIGHTS-ENGINE-ARCHITECTURE.md) points back here for Spaarke-wide visibility.*
