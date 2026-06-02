# Spaarke Insights Engine — Phase 1.5 (r2) — AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-05-30
> **Source**: [`design.md`](design.md) (Phase 1.5 design, 359 lines)
> **Predecessor**: [`ai-spaarke-insights-engine-r1`](../ai-spaarke-insights-engine-r1/) — Phase 1 shipped + deployed; 17/17 D-P deliverables complete
> **Architecture refs**: [`docs/architecture/INSIGHTS-ENGINE-ARCHITECTURE.md`](../../docs/architecture/INSIGHTS-ENGINE-ARCHITECTURE.md) §0a; [`docs/guides/INSIGHTS-ENGINE-GUIDE.md`](../../docs/guides/INSIGHTS-ENGINE-GUIDE.md)

---

## Executive Summary

Phase 1.5 lifts the Insights Engine from a working Phase 1 plumbing prototype (one playbook, code-defined universal-ingest, single-entity matter scope, litigation-biased classification) to a **usable, multi-tenant, multi-practice-area, multi-entity insights platform** with both pre-authored playbook and ad-hoc RAG consumption paths, prompts that SMEs can iterate without code deploys, and a Spaarke Assistant integration. The project both **unblocks the deployed `predict-matter-cost@v1` synthesis playbook** (half-day fix — Wave B, scheduled first per owner direction) and **refactors the architecture** to be JPS-compliant end-to-end, parameterized rather than multiplied, and generalized across Spaarke's actual practice-area and entity surface.

---

## Terminology (load-bearing)

This section is non-decorative — terms below define exactly what is and isn't being built. Earlier design + spec drafts conflated JPS-the-schema with the code that runs it; this section locks the meaning.

- **JPS** (JSON Prompt Schema) — the **schema/data format** for analysis actions and playbooks. JPS itself is **data, not code**. JPS data lives in Dataverse: on `sprk_analysisaction.sprk_systemprompt` (a JSON document with `$schema`, `$version`, `instruction { role, task, constraints, context }`, `input`, `parameters`) and on `sprk_playbook` rows.
- **`PlaybookExecutionEngine`** — the **code component in `Sprk.Bff.Api`** that executes JPS-defined work. Loads playbook + action rows from Dataverse, dispatches each node to its registered `INodeExecutor`, threads JPS-shaped data through the run. This is what previous spec drafts loosely called "the JPS engine."
- **`INodeExecutor`** — code-side handler for a specific analysis-action TYPE. Insights contributes new `INodeExecutor` implementations (LiveFactNode, IndexRetrieveNode, EvidenceSufficiencyNode, GroundingVerifyNode, DeclineToFindNode, ReturnInsightArtifactNode). The engine itself and JPS schema are unchanged by Insights.
- **`sprk_analysisaction`** — the **existing JPS dispatch + prompt row**. Carries: action code, action type, **JPS-formatted system prompt in `sprk_systemprompt`** (instruction + input schema + parameter schema + constraints + context), output format/schema, tags, description, sort order. **This IS the prompt-bearing primitive** — r1 already uses this for non-Insights actions (the existing "Classify Document" action carries its full JPS prompt here, including `instruction.role` / `instruction.task` / `instruction.constraints[]` / `input.document` / `parameters.categories`). Phase 1.5 retires `.txt` prompt files by populating `sprk_systemprompt` on the new Insights action rows.
- **`sprk_playbook` + `sprk_configjson`** — JPS playbook definition with per-playbook config blob. Carries playbook-specific tunables (cost cap, thresholds) and inline prompt templates that exist only for that playbook (e.g., the synthesis template owned by `predict-matter-cost@v1`).

**"Insights IS a JPS application"** means: every Insights workflow is defined as JPS data (`sprk_playbook` + `sprk_analysisaction` rows) executed by `PlaybookExecutionEngine`, with Insights contributing new `INodeExecutor` implementations + a new scope substrate (`spaarke-insights-index`) + a stable facade (`IInsightsAi`). **No parallel orchestrators.** **No new prompt-bearing entity in Phase 1.5** — prior design wording suggesting a `sprk_prompt` entity is superseded by the recognition that `sprk_analysisaction.sprk_systemprompt` already serves this role.

---

## Scope

### In Scope

**Wave B — Unblock synthesis (executes FIRST per owner direction; ~½ day)**
- B1. Create 6 `sprk_analysisaction` rows in Spaarke Dev for new Insights node ActionTypes
- B2. Update `predict-matter-cost.playbook.json` to reference the action codes
- B3. Update `Deploy-Playbook.ps1` to require action wiring (lint check; prevents future regressions)
- B4. Re-deploy playbook with action references
- B5. Live smoke verification — `predict-matter-cost` end-to-end with real matter, real LLM calls
- B6. Update Phase 1 verification doc with closed-gap status

**Wave A — Foundations (design docs; executes after B unblocks live verification)**
- A1. Architectural overview refresh (Phase 1.5 framing) — `INSIGHTS-ENGINE-ARCHITECTURE.md` updates as waves evolve
- A2. Operator/developer guide refresh — `INSIGHTS-ENGINE-GUIDE.md`; refined per wave
- A3. 2D taxonomy design — practice-area × document-type entity + matrix; initial practice-area scope confirmation (uses existing `sprk_practicearea_ref` rows, see Owner Clarification PA-1)
- A4. Prompt-variant + versioning + per-tenant-override design — uses the **existing `sprk_analysisaction.sprk_systemprompt` primitive** (see Terminology). Phase 1.5 does **not** introduce a new prompt entity. Wave A4 decides: (a) per-practice-area variation pattern — parametric JPS injection (`parameters.categories` array, `parameters.practiceAreaContext` string) vs. variant action rows resolved at invocation by action-code suffix (e.g., `INSIGHTS.LAYER1_CLASSIFY.CTRNS`); (b) versioning model (new action row per version vs. version field on row); (c) per-tenant override (tenant-scoped variant rows with fallthrough query, vs. override mapping table, vs. tenant blocks within the JPS schema)
- A5. Universal-ingest JPS refactor design — node breakdown (5–8 nodes) and parameterization model
- A6. Multi-entity subject design — scheme parsing, resolver registration pattern, index scope shape evolution

**Wave C — JPS compliance refactor (depends on A4, A5)**
- C1. `universal-ingest@v1` JPS playbook authored in Dataverse with nodes: Sanitizer, Layer1Classify, ConditionGate, Layer2Extract, GroundingVerify, ObservationEmitter, IndexUpsert, Mirror
- C2. Prompt content migrated from `.txt` files in `Services/Ai/Insights/Prompts/` into `sprk_analysisaction.sprk_systemprompt` (JPS-formatted JSON, mirroring r1's "Classify Document" pattern) per A4 design — zero `.txt` prompt files remain; the 6+ new Insights `sprk_analysisaction` rows created in Wave B plus additional action rows added in Waves C/D/E each carry their canonical JPS prompt
- C3. `IngestOrchestrator.cs` retired (plus orphaned interfaces) once JPS-native universal-ingest is live-verified
- C4. `IInsightsAi.RunIngestAsync` updated to invoke the JPS universal-ingest playbook
- C5. Universal-ingest parameterization — playbook accepts config parameters at invocation (tenant override, per-practice-area routing hints, cost-cap override) — flexibility via config, not via new playbooks

**Wave D — 2D taxonomy + multi-entity (depends on C complete; A3, A6 designs)**
- D1. `sprk_documenttype_ref` entity + `sprk_practicearea_documenttype` N:N matrix
- D2. Per-practice-area Layer 1 classification prompts (initial set per Owner Clarification PA-1)
- D3. Per-(practice-area, document-type) Layer 2 extraction schemas — 3–5 high-value combinations
- D4. Universal-ingest playbook routes classification + extraction based on `sprk_matter.sprk_practicearea` (or per-entity equivalent)
- D5. Multi-entity subject schemes: `matter:`, `project:`, `invoice:`; per-entity `ILiveFactResolver` implementations; LiveFactNode config extension
- D6. `spaarke-insights-index` scope shape generalization — carries `entityType`, `entityId` alongside existing `matterId` (backward compat); migration plan per A6 + Q-D6-1
- D7. Synthetic test fixtures across the initial practice areas × entity types (LLM-generated per Owner Clarification TF-1)

**Wave E — Hybrid consumption + Assistant integration (depends on D complete)**
- E1. `POST /api/insights/search` — generic RAG retrieval endpoint (semantic search over `spaarke-insights-index` filtered by subject + optional `artifactType` / `predicate`; LLM synthesis with grounded citations)
- E2. Intent classifier — natural-language → playbook routing OR generic RAG fallback (Phase 1.5 ships LLM-based; embedding-based routing is Phase 2)
- E3. Spaarke Assistant integration — Insights as a callable tool; **Wave E3 owns authoring the tool-call contract** (no pre-existing contract per Owner Clarification AC-1); coordinate with Assistant team
- E4. Decision-tree documentation: when to author a playbook vs rely on RAG

### Out of Scope (Phase 2 or later)

- Field auto-populations (Dataverse triggers → `/api/insights/ask` → field render) — Phase 2
- Customer-facing playbook authoring UI (SME builder) — Phase 2/3
- AI-directed playbook authoring (natural language → drafted playbook) — Phase 3
- Cosmos NoSQL graph backend (D-P17) — re-deferred from r1 promise; Wave D + E priorities deliver more user-visible value; `IInsightGraph` stub remains
- Per-tenant monthly cost caps with hard enforcement — Phase 1.5 stays observability-only per Phase 1 design
- Multi-tenant onboarding workflow — single-tenant deployment model continues per D-52
- SME-facing prompt iteration UI — Phase 2 after Wave C2 storage shape stabilizes
- MCP server contract — Phase 1.5 still defers to Phase 2
- **r1 wrap-up tasks** (Phase 1 task 090 lessons-learned; Phase 1 deployment-verification doc final update) — owner direction excludes these from r2 scope; tracked in r1 separately
- Embedding-based intent classifier — Phase 2 optimization
- Bidirectional Assistant integration beyond read-only tool-call — Phase 2

### Affected Areas

- `src/server/api/Sprk.Bff.Api/Services/Ai/Insights/` — all Phase 1.5 service work (ingest refactor, resolvers, classifier, RAG)
- `src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/IInsightsAi.cs` — facade methods may extend for `/search`
- `src/server/api/Sprk.Bff.Api/Endpoints/` — new `/api/insights/search` endpoint per Wave E1
- `src/server/api/Sprk.Bff.Api/Infrastructure/DI/` — register per-entity `ILiveFactResolver` implementations + intent classifier + RAG retriever (watch ADR-010 ≤15 non-framework limit)
- `src/server/api/Sprk.Bff.Api/Models/Insights/` — `InsightAskRequest` subject scheme parsing; `InsightSearchRequest` (new)
- `src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookExecution/` — `PlaybookExecutionEngine`: confirm conditional branch routing supports Insights semantics (Wave A5 risk; engine patches stay in the engine, do not fork)
- `infra/insights/schemas/` — `spaarke-insights-index` schema migration (Wave D6)
- `infra/insights/modules/` — universal-ingest playbook artifact migration
- `scripts/Deploy-Playbook.ps1` — action-wiring lint check (Wave B3); JPS-managed prompts hydration (Wave C2)
- Dataverse schema additions (Wave D1):
  - `sprk_documenttype_ref` (new entity)
  - `sprk_practicearea_documenttype` (new N:N matrix)
  - **No new `sprk_prompt` entity** — Phase 1.5 prompt storage uses the existing `sprk_analysisaction.sprk_systemprompt` field (see Terminology + PR-1). New Insights `sprk_analysisaction` rows added in Waves B/C/D/E are data, not schema changes.
- `tests/unit/Sprk.Bff.Api.Tests/Services/Insights/` — coverage for new resolvers, classifier, RAG path
- `tests/unit/Sprk.Bff.Api.Tests/Models/Insights/` — coverage for subject-scheme parsing + multi-entity scope
- `tests/integration/` — eval harness expansion (Wave D7)

---

## Requirements

### Functional Requirements

1. **FR-01** — `POST /api/insights/ask` with `question: "predict_matter_cost_v1"` on a real Spaarke Dev matter returns either a real `InsightArtifact` (≥12 evidence refs from real Observations) OR a real `DeclineResponse` with structured `MinimumEvidenceNeeded` from `EvidenceSufficiencyNode`. **NOT** the defensive scaffold decline.
   - **Acceptance**: Live smoke run against a real matter on Spaarke Dev; orchestrator log shows successful node dispatch; response body matches `InsightArtifact` or `DeclineResponse` JSON shape (not scaffold).

2. **FR-02** — `POST /api/insights/ask` with `question` referring to a Banking & Finance, IP Patents, or Commercial Transactions matter (per Owner Clarification PA-1) routes through the appropriate practice-area Layer 1/Layer 2 prompts during ingest.
   - **Acceptance**: Eval harness shows per-practice-area Layer 1 prompt selection; Layer 2 schema matches `(practice-area, document-type)`; recorded Observations carry practice-area-appropriate fields.

3. **FR-03** — `POST /api/insights/ask` with `subject: "project:<guid>"` or `subject: "invoice:<guid>"` resolves live facts from the correct Dataverse entity and returns the appropriate Inference.
   - **Acceptance**: Per-entity `ILiveFactResolver` registered in DI; subject parser routes by scheme; integration test with project + invoice fixtures returns a non-scaffold Inference.

4. **FR-04** — `POST /api/insights/search` (NEW) accepts a natural-language query, returns top-N ranked Observations/Precedents with an LLM-synthesized summary and grounded citations.
   - **Acceptance**: Endpoint exists; auth filter applied per ADR-008/ADR-028; integration test against Spaarke Dev index returns ≥1 citation with `observationId` + `predicate` + `confidence`.

5. **FR-05** — Spaarke Assistant can invoke either path (playbook OR RAG) via the Phase 1.5 LLM-based intent classifier when a user asks an Insights-shaped question in chat.
   - **Acceptance**: Assistant tool-call invokes the Insights facade; classifier returns `{path: "playbook" | "rag", playbookId?, confidence}`; classifier confidence below threshold falls back to RAG. Caller can override via `forceMode: "playbook" | "rag"`.

6. **FR-06** — SME can edit a prompt (e.g., extraction threshold language for "leniency clauses" in lease extraction) without a code deploy. Edit happens on the relevant `sprk_analysisaction.sprk_systemprompt` JSON content (or a new variant row per A4 versioning model); next invocation of the dependent playbook uses the new content.
   - **Acceptance**: Edit `sprk_systemprompt` on an Insights `sprk_analysisaction` row in Dataverse; next invocation of the dependent playbook uses the new content (no app restart, no code deploy); version/rollback history queryable per A4 design.

### Non-Functional Requirements

- **NFR-01** — **JPS-only orchestration**: every Insights workflow runs through `PlaybookExecutionEngine`. After Wave C: zero parallel orchestrators, zero code-defined ingest paths.
- **NFR-02** — **Prompts in Dataverse**: zero `.txt` prompt files in `Services/Ai/Insights/Prompts/` after Wave C2. All prompt content lives in `sprk_analysisaction.sprk_systemprompt` (or in per-playbook `sprk_configjson` for playbook-specific inline templates that no other playbook references).
- **NFR-03** — **Action wiring enforced**: every Insights playbook node has a `sprk_analysisaction` reference; `Deploy-Playbook.ps1` lint check rejects deploys missing references.
- **NFR-04** — **BFF publish-size hygiene**: Phase 1.5 additions stay within the ratcheted size baseline per ADR-029 and `.claude/constraints/azure-deployment.md`. Verify `dotnet publish` size before merging any wave that adds NuGet packages.
- **NFR-05** — **DI count ceiling**: Wave D5's per-entity resolver registrations + Wave E's classifier + RAG registrations stay within ADR-010's ≤15 non-framework registration target; if exceeded, consolidate via registry pattern (Q-A6-1 option (b)).
- **NFR-06** — **No HIGH-severity CVEs**: Wave C / D / E NuGet additions verified clean via `dotnet list package --vulnerable --include-transitive` before merge.
- **NFR-07** — **§3.5 grep gate continues to pass**: zero forbidden Zone B imports across the new code.
- **NFR-08** — **Backward compatibility**: Phase 1 Observations (with `scope.matterId` only) remain queryable after Wave D6 index shape migration.
- **NFR-09** — **Eval harness baseline**: at least 3 practice areas × 2 entity types (≥6 question-shape combinations) with passing baseline metrics before Phase 1.5 is declared done.
- **NFR-10** — **SME calibration loop**: ≥50 sampled Observations marked `Pending Review` flow through the disposition queue; SME marks dispositions; iteration on prompts produces measurable accuracy improvement.

---

## Technical Constraints

### Applicable ADRs

- **ADR-001** — Minimal API + BackgroundService. `POST /api/insights/search` (Wave E1) MUST follow the minimal-API + endpoint-filter pattern. No Azure Functions.
- **ADR-008** — Endpoint filters for auth. New `/api/insights/search` endpoint MUST apply the same per-route auth filter as `/api/insights/ask`. No global auth middleware.
- **ADR-010** — DI minimalism (≤15 non-framework registrations). Wave D5 (per-entity resolvers × 3+) and Wave E (classifier + RAG retriever) add multiple registrations; if the ceiling is exceeded, consolidate via a registry/factory pattern.
- **ADR-013** — AI Architecture (extend BFF; AI Tool Framework). Phase 1.5 is canonically AI work. The refined 2026-05-20 facade pattern applies: CRUD code MUST consume Insights only via `IInsightsAi` (the `Services/Ai/PublicContracts/` facade) — never inject `IOpenAiClient`, `IPlaybookService`, or other AI-internal types directly into CRUD code.
- **ADR-027** — Subscription Isolation + Dataverse Solution Management. Phase 1.5 schema additions (`sprk_documenttype_ref`, `sprk_practicearea_documenttype`, possibly `sprk_prompt`) MUST flow through the managed-solution promotion path to prod.
- **ADR-028** — Spaarke Auth v2. New `/api/insights/search` endpoint MUST register via the function-based contract and use a named API key scheme + audit middleware where applicable.
- **ADR-029** — BFF Publish Hygiene. Phase 1.5 wave merges MUST verify publish-size impact + the transitive CVE override pattern.
- **ADR-030** — BFF Null-Object Kill-Switch Pattern (NEW 2026-06-01). Any new service in a `*Module.cs` `if (flag) { ... }` block consumed by an unconditionally-mapped endpoint MUST apply one of: P1 (Promote-to-unconditional if no AI-deps), P2 (Quiet no-op — FORBIDDEN for query services), P3 (Fail-fast Null-Object via `FeatureDisabledException` → 503 ProblemDetails). Affects Wave C (020 universal-ingest service additions, 023 facade re-wire), Wave D (034 multi-entity resolvers), Wave E (040 search endpoint, 041 intent classifier).

### MUST Rules (from ADRs + CLAUDE.md §10)

- ✅ MUST use `IInsightsAi` facade for any CRUD code that needs Insights capability (refined ADR-013, 2026-05-20)
- ✅ MUST use endpoint filters for auth on the new `/search` endpoint (ADR-008)
- ✅ MUST declare BFF Placement Justification per CLAUDE.md §10 (see "BFF Placement Review" section below)
- ✅ MUST verify publish-size impact before merging if NuGet packages are added (ADR-029)
- ✅ MUST verify no new HIGH-severity CVE from `dotnet list package --vulnerable --include-transitive`
- ❌ MUST NOT spawn parallel orchestrators or code-defined playbooks (D-P15-01)
- ❌ MUST NOT spawn many universal-ingest variants (D-P15-02) — parameterize the canonical playbook instead
- ❌ MUST NOT inject `IOpenAiClient` / `IPlaybookService` directly into CRUD code (refined ADR-013, 2026-05-20)
- ❌ MUST NOT leave `.txt` prompt files in `Services/Ai/Insights/Prompts/` after Wave C2

### BFF Placement Review (per CLAUDE.md §10 — binding)

This project extends Phase 1 (r1) BFF work in-place. The 2026-05-20 BFF AI extraction assessment (`docs/assessments/bff-ai-extraction-assessment-2026-05-20.md`) validated that AI work in `Sprk.Bff.Api` is structurally AI-dominant but **operationally justified to keep unified**. Phase 1.5 components placement decisions:

| Component | Placement | Rationale |
|---|---|---|
| `POST /api/insights/search` endpoint (E1) | `Sprk.Bff.Api/Endpoints/` | Endpoint placement follows the existing `/ask` pattern; auth + audit middleware integration requires BFF |
| Universal-ingest JPS playbook (C1) | Dataverse (not source code) | Playbook is data, not code; deployed via `Deploy-Playbook.ps1`. Code is in JPS node executors (already in BFF) |
| Per-entity `ILiveFactResolver` implementations (D5) | `Sprk.Bff.Api/Services/Ai/Insights/` | Resolvers depend on `Spaarke.Dataverse` types + JPS engine context; consistent with Phase 1 placement |
| Intent classifier (E2) | `Sprk.Bff.Api/Services/Ai/Insights/` | LLM-based classifier reuses `IOpenAiClient` and Phase 1 facade plumbing |
| RAG retriever (E1) | **Existing `Sprk.Bff.Api/Services/Ai/IRagService` + `RagService` + `NullRagService` per 2026-06-01 master refactor.** Wave E1 endpoint wraps `IRagService` — no parallel implementation. If Insights-specific subject filtering needs extension, extend the existing service. | Avoids duplicating substrate access; aligns with refined ADR-013 facade pattern |
| `IInsightsAi` facade extensions (E1, D5) | `Sprk.Bff.Api/Services/Ai/PublicContracts/` | Facade is the canonical CRUD ↔ AI seam per refined ADR-013 |
| Schema additions (D1) | Dataverse managed solution | Standard Dataverse promotion path per ADR-027 |
| Prompts (C2) | Dataverse — `sprk_analysisaction.sprk_systemprompt` (existing field, JPS-formatted JSON) | Existing JPS primitive already used in r1 (see Terminology). Phase 1.5 retires `.txt` files by populating action rows. **No new entity needed for prompt content.** Per-playbook inline templates still allowed in `sprk_playbook.sprk_configjson` for prompts that exist in exactly one playbook. |

**Placement summary**: Phase 1.5 keeps all new code in `Sprk.Bff.Api` consistent with the r1 precedent and the 2026-05-20 assessment. **No extraction to `Spaarke.Core` is justified in Phase 1.5** — the new services are tightly coupled to BFF-hosted infrastructure (JPS engine, OpenAI client, Azure AI Search client, audit middleware). This decision is **load-bearing** and must be reviewed if a future phase adds non-BFF Insights consumers.

`.claude/constraints/bff-extensions.md` MUST be loaded by each Wave C / D / E task that adds endpoints, services, DI registrations, or NuGet packages.

### Existing Patterns to Follow

- **JPS playbook authoring** — see existing `predict-matter-cost.playbook.json` + `Deploy-Playbook.ps1`
- **Node executor** — see `c:\code_files\spaarke-wt-ai-spaarke-insights-engine-r1\src\server\api\Sprk.Bff.Api\Services\Insights\Graph\...` for Phase 1 node implementations
- **Endpoint pattern** — see existing `/api/insights/ask` for the minimal-API + filter + facade pattern
- **DI registration** — see existing Phase 1 wiring in `Infrastructure/DI/InsightsServiceCollectionExtensions.cs`
- **Constraint reference (binding before adding to BFF)** — `.claude/constraints/bff-extensions.md`
- **AI architecture overview** — `docs/architecture/AI-ARCHITECTURE.md`

---

## Success Criteria

(Maps to design.md §2 acceptance bar; renumbered SC-01..SC-15)

1. [ ] **SC-01** — `POST /api/insights/ask` predict-matter-cost end-to-end returns real `InsightArtifact` or real `DeclineResponse` on Spaarke Dev. Verify by: live smoke run + log review.
2. [ ] **SC-02** — Per-practice-area routing demonstrably works for ≥3 practice areas. Verify by: eval harness output.
3. [ ] **SC-03** — Multi-entity subject (`project:`, `invoice:`) resolves live facts correctly. Verify by: integration test with project + invoice fixtures.
4. [ ] **SC-04** — `POST /api/insights/search` returns ranked Observations + LLM-synthesized summary with citations. Verify by: integration test against Spaarke Dev index.
5. [ ] **SC-05** — Spaarke Assistant invokes Insights via either path through intent classifier. Verify by: end-to-end chat-surface test; `forceMode` override works.
6. [ ] **SC-06** — SME edits a prompt; next invocation uses new content; version history retained. Verify by: manual prompt edit + re-invocation; query version history.
7. [ ] **SC-07** — `IngestOrchestrator` code path retired; universal-ingest runs as JPS playbook. Verify by: grep confirms class removed; deployment confirms playbook executes.
8. [ ] **SC-08** — Zero `.txt` prompt files in `Services/Ai/Insights/Prompts/`; all corresponding prompts present in `sprk_analysisaction.sprk_systemprompt` rows. Verify by: directory listing + grep + Dataverse query for the Insights action codes.
9. [ ] **SC-09** — Every Insights playbook node has a `sprk_analysisaction` reference; lint check enforces. Verify by: `Deploy-Playbook.ps1` rejects a deliberately broken playbook.
10. [ ] **SC-10** — `DataverseLiveFactResolver` abstracted; per-entity resolvers registered for matter, project, invoice. Verify by: DI inspection + unit tests.
11. [ ] **SC-11** — §3.5 grep gate passes (zero forbidden Zone B imports). Verify by: existing CI step.
12. [ ] **SC-12** — `IInsightGraph` stub remains; Cosmos explicitly out of scope. Verify by: interface present but no implementation registered.
13. [ ] **SC-13** — Eval harness runs across ≥3 practice areas × 2 entity types with passing baseline. Verify by: harness output report.
14. [ ] **SC-14** — Live smoke runbook end-to-end on Spaarke Dev. Verify by: runbook execution log.
15. [ ] **SC-15** — SME calibration loop closes ≥50 Observations with measurable accuracy improvement. Verify by: disposition queue metrics + before/after eval scores.

---

## Dependencies

### Prerequisites

- Phase 1 (r1) plumbing complete + deployed on Spaarke Dev (✅ verified 2026-05-30)
- Architecture doc §0a captures Phase 1.5 framing (✅ done)
- Operator guide authored (✅ done)
- Phase 1.5 design.md authored (✅ done — `design.md`)
- **r1 wrap-up tasks** (Phase 1 task 090, deployment-verification doc) — **NOT a prerequisite per owner direction**; runs in r1 separately
- Spaarke Dev environment accessible with valid Insights tenant config

### Internal Spaarke Dependencies

- **`PlaybookExecutionEngine`** (in `Sprk.Bff.Api`) — Phase 1.5 contributes patches if needed for Insights semantics (conditional branch routing); coordinate with `ai-advanced-capabilities-development` (LAVERN primitives) so Phase 1.5 consumers stay aligned
- **Spaarke Assistant team** — Wave E3 owns authoring the tool-call contract (no pre-existing contract per Owner Clarification AC-1); coordinate on schema and rollout
- **Dataverse schema owners** — Wave D1 adds entities (`sprk_documenttype_ref`, `sprk_practicearea_documenttype`, possibly `sprk_prompt`); coordinate on naming + solution membership
- **`spaarke-files-index`** — universal-ingest reads chunks from here; SDAP schema changes could break Insights ingest (current dynamic-SearchDocument access defends)
- **`sdap-bff-api-remediation-fix`** — `IInsightsAi` facade extensions in Wave E coordinate with broader BFF AI facade work

### External Dependencies

- **Azure OpenAI** — Layer 1 (gpt-4o-mini), Layer 2 (gpt-4o), embeddings (text-embedding-3-large); deployments stable in Spaarke Dev
- **Azure AI Search** — `spaarke-insights-index` schema migration in Wave D6 may require index re-create; coordinate with infra team
- **Dataverse availability** — JPS engine reads playbook + nodes on every invocation; caching TBD if latency becomes an issue

---

## Owner Clarifications

| Topic | Question | Answer | Impact |
|---|---|---|---|
| **WB-1** Wave B timing | Should Wave B (unblock synthesis) execute before Wave A (foundations), or follow strict A → B sequencing? | **Wave B first.** Document the re-sequencing in spec.md. | Project execution order is B → A → C → D → E. Wave B's half-day fix delivers an immediate visible win and proves the Phase 1 design before Wave C's architectural refactor. Wave A foundation design docs follow B and inform Waves C–E. |
| **BFF-1** BFF placement review | CLAUDE.md §10 requires a Placement Justification section. How should this project handle it? | **Ensure BFF implications are reviewed and documented**; this work is an extension of r1. | spec.md includes a "BFF Placement Review" section (above). All Phase 1.5 components stay in `Sprk.Bff.Api` per the 2026-05-20 assessment + r1 precedent. Decision is load-bearing — re-evaluate if Phase 2 adds non-BFF Insights consumers. |
| **R1-1** r1 wrap-up | Should r1 wrap-up work (task 090, deployment-verification doc) be pulled into r2 scope? | **No — exclude from r2.** | Spec.md "Out of Scope" lists r1 wrap-up explicitly. r1 closes those items separately. r2 does not block on them. |
| **PA-1** Practice areas | Initial practice-area scope for Wave D2: design said 4–5 areas (litigation, real-estate, patent, transactional). Which is the actual set? | **Use the rows in `sprk_practicearea_ref` in Spaarke Dev.** Visible rows include: APPL (Appellate), BNKF (Banking & Finance), CTRNS (Commercial Transactions), IPPAT (Intellectual Property Patents), IPTM (Intellectual Property Trademarks), MA (Mergers & Acquisitions). The full set lives in the table. | Wave A3 (taxonomy design) confirms the full set via Dataverse query at task time and selects the **initial 3 practice areas** for Wave D2 prompt authoring + Wave D3 Layer 2 schemas. The remaining areas land per customer onboarding cadence. Phase 1's "litigation" framing is retired; per-practice-area gate logic replaces the binary `outcomeBearing` flag per D-P15-09. |
| **TF-1** Test fixture source | What's the source for Wave D7 test fixtures across practice areas + entity types? | **Synthetic / LLM-generated.** | Fast iteration, no PII concerns. Wave D7 includes a fixture-generation step using LLM-seeded characteristics per (practice-area, document-type, entity-type). Realism limitations are accepted for Phase 1.5; Phase 2 may add anonymized real fixtures. |
| **AC-1** Assistant contract | Has the Spaarke Assistant tool-call contract been established, or does Wave E3 define it? | **Contract has NOT been created. Wave E3 defines it.** | Wave E3 scope expands to include contract authoring as its first task. Coordinate with Assistant team on schema, request/response shape, error model, and rollout. Treat as a §6 design risk — initial coordination uncovers facade contract gaps; mitigation is read-only tool-call semantics for Phase 1.5 with full bidirectional integration deferred to Phase 2. |
| **PR-1** JPS terminology + prompt storage | (a) What is "the JPS engine"? (b) Is `sprk_analysisaction` really not a prompt? | (a) **JPS is the schema/data format, not code.** `PlaybookExecutionEngine` (in `Sprk.Bff.Api`) is the runtime that executes JPS data; `INodeExecutor` implementations are the per-action-TYPE handlers. Earlier spec wording loosely called this "the JPS engine." (b) **`sprk_analysisaction` IS prompt-bearing** — `sprk_systemprompt` holds the full JPS-formatted prompt (`$schema`, `$version`, `instruction { role, task, constraints, context }`, `input`, `parameters`). Verified against the existing "Classify Document" action row in Spaarke Dev. | Three load-bearing consequences: (1) **No new `sprk_prompt` entity** — Phase 1.5 prompts move from `.txt` files into `sprk_analysisaction.sprk_systemprompt`. (2) Per-practice-area variation goes through either JPS `parameters` injection or variant action rows; Wave A4 decides. (3) "Terminology" section added to top of spec to lock the meaning of JPS / `PlaybookExecutionEngine` / `INodeExecutor` / `sprk_analysisaction` going forward. |

---

## Assumptions

*Proceeding with these assumptions (owner did not specify or design left open):*

- **Wave B execution order**: Wave B runs as the FIRST wave (per WB-1). Wave A starts immediately after Wave B's verification completes.
- **Initial Wave D2 practice area count**: Wave A3 design task confirms by Dataverse query; initial Wave D2 implementation targets **3 high-value practice areas** (selection deferred to A3 based on SME readiness + document variety).
- **Cosmos NoSQL re-deferral approved**: r1 SPEC promised D-P17 as first Phase 1.5 deliverable; design.md re-defers explicitly with rationale; this spec proceeds on that re-deferral.
- **Spaarke Assistant team availability**: Wave E3 can coordinate with the Assistant team within the project window. If unavailable, Wave E3 ships a stub callable interface and contract authoring slips to Phase 2.
- **`PlaybookExecutionEngine` supports Insights semantics**: Wave A5 design surfaces gaps. Engine patches stay in the engine and do not fork. If a fundamental gap surfaces, Wave A5 escalates to a `PlaybookExecutionEngine` project task before proceeding with Wave C.
- **Prompt storage**: `sprk_analysisaction.sprk_systemprompt` is the canonical storage per PR-1 (existing JPS primitive; r1 already uses it for non-Insights actions like "Classify Document"). Phase 1.5 does **not** add a new prompt entity. Wave A4 decides the variant + versioning + override mechanism within this primitive.
- **Universal-ingest node count** — Wave A5 design picks 5–8 nodes; spec assumes 8 nodes for sizing (per design Q-A5-1).
- **Index re-create** required for Wave D6 migration; coordinate with infra team during D6 task; new fields nullable; Phase 1 Observations remain queryable.
- **Per-entity resolver registration pattern** — defaults to option (a) `IDictionary<string, ILiveFactResolver>` keyed by entity type (simplest); Wave A6 confirms.
- **Intent classifier Phase 1.5 approach** — LLM-based small model (gpt-4o-mini); embedding-based routing is Phase 2.
- **Tenant ingest opt-in defaults** — `Tenants:<TenantId>:Insights:IngestEnabled` defaults to `false` for safety; tenant onboarding opts in. `X-Ai-Insights-Ingest: false` header overrides per-upload.

---

## Unresolved Questions

*Still need answers — Wave A foundation tasks resolve most of these:*

- [ ] **Q-A4-1** — Phase 1.5 prompt-variant + versioning + per-tenant-override pattern **within `sprk_analysisaction.sprk_systemprompt`** (no new entity per PR-1). Variant options: (a) per-practice-area variant action rows (action codes suffixed `_CTRNS`, `_IPPAT`, etc.) resolved at invocation by playbook nodes; (b) parametric JPS injection (single action row with runtime parameters per practice area). Versioning: new row per version vs. version field on row. Tenant override: tenant-scoped variant rows with fallthrough vs. override mapping table. **Blocks**: Wave C2 (prompt content migration), Wave D2 (per-practice-area prompts).
- [ ] **Q-A5-1** — How many nodes in the JPS universal-ingest playbook (1:1 with current 8 logical steps, or coalesced to 5–6)? **Blocks**: Wave C1 (playbook authoring) + node observability design.
- [ ] **Q-A6-1** — Per-entity resolver naming + registration pattern (dictionary, registry service, or `CanResolve` iteration). **Blocks**: Wave D5 (multi-entity resolver implementation).
- [ ] **Q-D6-1** — `spaarke-insights-index` scope shape evolution: add new fields (a), generalize to `entityType` + `entityId` (b), or hybrid (c). **Blocks**: Wave D6 (index migration) — also affects backward-compat behavior for Phase 1 Observations.
- [ ] **Q-D2-1** — **Resolved partially by PA-1**: which 3 (of the full `sprk_practicearea_ref` set) ship in initial Wave D2? **Blocks**: Wave D2 (per-practice-area Layer 1 prompts).
- [ ] **Q-E2-1** — Intent classifier approach: confirmed LLM-based for Phase 1.5; explicit threshold tuning + caller override mechanism details. **Blocks**: Wave E2 finalization.
- [ ] **Q-WB-1** (NEW) — Wave B placeholder action codes — confirm naming convention with Spaarke Dev tenant before B1 row creation. **Blocks**: Wave B1.
- [ ] **Q-AC-1** (NEW) — Assistant tool-call schema shape — initial proposal authored in Wave E3 then validated with Assistant team. **Blocks**: Wave E3 implementation start.

---

## Wave Sequencing (per Owner Clarification WB-1)

```
Wave B (Unblock synthesis)         ── EXECUTES FIRST per owner direction
       │                              ~½ day; predict-matter-cost end-to-end works live
       │                              Proves Phase 1 design before Wave C refactor
       ▼
Wave A (Foundations)               ── design docs A1–A6; sets framing for C, D, E
       │                              ~4 days
       ▼
Wave C (JPS compliance refactor)   ── universal-ingest → JPS playbook; prompts → JPS storage
       │                              ~4–6 days; depends on A4, A5
       ▼
Wave D (2D taxonomy + multi-entity) ── practice-area depth; project/invoice subjects
       │                              ~1.5–2 weeks; depends on C + A3 + A6
       ▼
Wave E (Hybrid + Assistant)        ── /search RAG endpoint; intent classifier; Assistant tool
                                      ~1.5–2 weeks; depends on D + A6
                                      Includes E3 tool-call contract authoring (no precursor)
```

**Total Phase 1.5 scope**: ~5–7 weeks of focused work. Parallelization possible within waves (D and E touch different code areas); cross-wave dependencies enforce B → A → C → D → E ordering.

---

## Risks (carried from design.md §6)

| Risk | Probability | Impact | Mitigation |
|---|---|---|---|
| JPS engine doesn't cleanly support Insights conditional-branch semantics | Medium | High | Wave B exercises engine; Wave A5 surfaces gaps; patches stay in JPS engine (don't fork) |
| Prompt migration changes runtime behavior subtly | Low | High | Side-by-side comparison harness during migration; new prompts get `@v2` versions; cutover playbook-by-playbook with rollback |
| Per-practice-area prompt authoring is more time per area than estimated | Medium | Medium | Initial Wave D scope = 3 practice areas; expand per customer cadence; SMEs draft, devs refine |
| `spaarke-insights-index` schema migration disrupts Phase 1 Observations | Medium | Medium | Wave D6 migration plan; new fields nullable; old Observations stay queryable |
| Intent classifier mis-routes high-value questions to RAG (lower quality) | Medium | Medium | Caller `forceMode` override; confidence threshold tunable |
| Assistant integration uncovers facade contract gaps (Wave E3) | Medium | Medium | E3 starts with read-only tool-call semantics; bidirectional integration is Phase 2; contract authoring is the first E3 sub-task per AC-1 |
| Field auto-population (Phase 2) reveals latency issues | N/A this phase | Phase 2 risk | D-P13 cache layer + per-playbook TTL; Phase 1.5 eval harness records baseline latencies |

---

*AI-optimized specification. Original design: [`design.md`](design.md). This spec.md is the canonical Phase 1.5 reference for `/project-pipeline` consumption.*
