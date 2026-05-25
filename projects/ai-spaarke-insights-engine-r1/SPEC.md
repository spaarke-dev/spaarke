# SPEC — Spaarke Insights Engine, Phase 1 (Foundation)

> **Status**: Pipeline-ready
> **Last Updated**: 2026-05-19
> **Anchor docs**: [decisions.md](decisions.md) (read first) · [design.md](design.md) (full design)
> **Scope**: Phase 1 of the Insights Engine project. Split into **Track A (auth-independent, in scope NOW)** and **Track B (auth-coupled, blocked on Phase C)**.

---

## 1. Overview

The Spaarke Insights Engine is a back-end component that transforms organizational signals (matters, documents, AI sessions) into honestly-grounded context for AI agents and end users across multiple surfaces. Phase 1 delivers the **foundation** — substrate, type system, orchestration shell, and one end-to-end Inference question — proving the architecture works end-to-end.

The full architecture is in [design.md](design.md). The committed decisions (38 numbered) are in [decisions.md](decisions.md).

## 2. Goals (Phase 1)

1. Provision and configure all substrate resources (AI Search indexes, Cosmos account, Service Bus topic, Function App shell) via Bicep — per-tenant deployable.
2. Implement the `InsightArtifact` type system (Fact / Observation / Inference envelope) in C#.
3. Implement the `IInsightGraph` abstraction with a Cosmos NoSQL adjacency-list backend.
4. Implement the `LiveFactResolverService` (direct Dataverse queries for 3-5 Facts).
5. Implement the `InsightsResolverService` shell (orchestration + provenance + cache + access trimming).
6. Implement the `Insights Agent` shell with tool interfaces; one Inference question (`predict-matter-cost`) wired end-to-end with evidence-sufficiency rules.
7. Expose `POST /api/insights/ask` endpoint on the BFF (Minimal API, endpoint-filter auth per ADR-008).
8. Smoke-test the full pipeline with mock data — proving the architecture before real sync data lands.

Track A delivers (1)–(8) without dependency on the Phase C auth work. Track B (sync wiring) is paused until Phase C resolves.

## 3. Scope

### 3.1 In scope (Track A — proceed now)

| ID | Deliverable | Layer |
|---|---|---|
| D-A1 | Project scaffolding: folder structure for `Services/Insights/`, `infra/insights/`, `schemas/`, `tests/Insights/` | Repo structure |
| D-A2 | Bicep modules for resource provisioning: AI Search indexes, Cosmos account + database + containers, Service Bus topic + subscriptions, Function App shell (compute only, no functions deployed yet), Key Vault references, App Insights connection, per-tenant UAMI | Infra |
| D-A3 | AI Search index schemas (JSON, declarative): `insight-matters`, `insight-decisions`, `insight-risks`, `insight-sessions` per [design.md §4.1.3](design.md) — fields, vector profile, semantic config, tenantId as first-class | Infra |
| D-A4 | `InsightArtifact` envelope C# types in `Sprk.Bff.Api/Models/Insights/` — POCOs for the four-tier taxonomy (Fact / Observation / Precedent / Inference) per [design.md §2.2](design.md) and decisions.md D-03 | Domain types |
| D-A5 | `IInsightGraph` interface in `Sprk.Bff.Api/Services/Insights/Graph/` — typed named traversal patterns per [design.md §4.2.2](design.md) | Domain types |
| D-A6 | `CosmosNoSqlInsightGraph` implementation — adjacency-list document model; vertex + edge upsert/get/delete; `FindMattersInvolvingPartyAsync`, `FindConnectedEntitiesAsync` named traversals; per-tenant partition key | Substrate |
| D-A7 | `LiveFactResolverService` in `Sprk.Bff.Api/Services/Insights/Facts/` — direct Dataverse queries via existing `IDataverseService`. Initial Facts: `matterDuration`, `totalSpend`, `status`, `daysSinceLastActivity`, `documentCount`. 5-minute Redis cache | Domain logic |
| D-A8 | `InsightsResolverService` skeleton in `Sprk.Bff.Api/Services/Insights/` — orchestration: question router, signal fetcher composing `IInsightGraph` + `LiveFactResolverService` + AI Search, provenance assembler, per-question TTL cache, `accessibleMatterSet` enforcement at every query | Domain logic |
| D-A9 | `Insights Agent` shell in **`Sprk.Bff.Api/Services/Ai/Insights/`** (parallel to existing `Services/Ai/Chat/`) — extends existing `IChatClient` + tool framework. Tool interfaces: `IFindComparableMattersTool`, `IGetMatterFactsTool`, `IAssessEvidenceSufficiencyTool` in `Services/Ai/Insights/Tools/`. Stub implementations that return mock data. **Exposed to D-A8 Resolver via `Services/Ai/PublicContracts/IInsightsAi` facade — see §3.5.** | AI-internal (Zone A) |
| D-A10 | `predict-matter-cost` Inference question definition — question catalog entry, evidence-sufficiency rule (`comparableMatters.min: 12`), insufficient-evidence response shape, registered with the Insights Agent | Domain logic |
| D-A11 | `POST /api/insights/ask` Minimal API endpoint in `Sprk.Bff.Api/Api/Insights/InsightEndpoints.cs` — accepts `InsightRequest`, returns `InsightResponse`. Endpoint filter for resource auth per ADR-008. Rate limiting per ADR-016. ProblemDetails errors per ADR-019 | API |
| D-A12 | Closure-extraction JPS playbook DESIGN document (not implementation) in `projects/ai-spaarke-insights-engine-r1/closure-extraction-playbook-design.md` — what it emits, version handling, target indexes | Design |
| D-A13 | Initial Bicep deployment to dev environment — provisions resources only (no functions deployed); verifies all Bicep modules work; documents the per-tenant parameter file pattern | DevOps |
| D-A14 | Smoke tests: unit tests for envelope serialization, `IInsightGraph` interface contracts, `LiveFactResolverService` against mocked Dataverse; integration tests for `InsightsResolverService` end-to-end with mock data; AI Search index provisioning verification (round-trip schema deploy + read) | Tests |
| D-A15 | **Synthetic corpus generator** (per `INSIGHTS-ENGINE-ARCHITECTURE.md` §4.9 / D-39) — script producing 200–500 fictional matters with realistic distributions (practice areas, deal sizes, jurisdictions, outcomes, durations) following calibration parameters in `tests/Insights/synthetic/calibration.json`. Loadable into the eval environment via deterministic seed for reproducibility. Powers golden-dataset evaluation (D-A16) and Inference end-to-end smoke tests on mock data. | Evaluation |
| D-A16 | **Golden dataset v1** (per D-40, §14.2) — 10–15 initial `(question, expected-answer, expected-evidence)` tuples for `predict-matter-cost`, including 3–5 insufficient-evidence cases. Lives in `tests/Insights/golden/predict-matter-cost.json`. Curated by domain expert; reviewed by Engine team. Each entry has: question parameters, expected confidence band, expected `evidence[]` IDs, expected `displayHint`, and (for insufficient cases) expected `actionableGap` copy. | Evaluation |
| D-A17 | **Evaluation harness** (per D-40, §14.4) — CI gate that runs golden dataset through `InsightsResolverService`, computes RAG-triad metrics (Retrieval overlap, Groundedness pass rate, Relevance score), and fails the build on groundedness regression OR insufficient-evidence miscalls. Phase 1 thresholds set permissively (e.g., groundedness ≥ 95% rather than 100%) because mock-data behavior won't match real-data behavior; Phase 2 hardens. | Evaluation |
| D-A18 | **Production observability** (per D-40, §14.5) — App Insights telemetry emitters for `insufficient_evidence_rate`, `confidence_band_distribution`, `refine_rate`, `evidence_count_by_question`, with tags `tenantId`, `questionId`, `evidenceSufficient`. Dashboards: per-question health, per-tenant insufficient-rate trend, weekly groundedness regression alert. | Observability |
| D-A19 | **Surfacing design document** in `projects/ai-spaarke-insights-engine-r1/surfacing-design.md` (per §10.7, §10.8) — Insight Card visual language for the three rendering patterns (Fact / Observation / Inference; Precedent visual added per D-46), surface map, the Refine contract, Insufficient-evidence treatment standardized. Reviewed and approved by design and Engine teams before D-A11 endpoint ships. | Design |
| D-A20 | **MCP server contract document** in `projects/ai-spaarke-insights-engine-r1/mcp-contract.md` (per D-41, §15.4) — tool signatures (`predict_matter_cost`, `find_comparable_matters`, `assess_matter_risks`, `summarize_matter_closure`), resource URIs, prompt fragments, OBO auth flow. Drafted alongside the question catalog so they evolve together. **Implementation deferred to Phase 2** — Phase 1 ships the contract document only. | Design |
| D-A21 | **Customer-onboarding workflow design** in `projects/ai-spaarke-insights-engine-r1/customer-onboarding-design.md` (per D-39, §11.4) — historical backfill priority algorithm, "Insights-ready" milestone definitions per question template, customer-facing progress dashboard mockup, throttle policy per tenant. Implementation begins in Phase 1 if Track B unblocks; otherwise early Phase 2. | Design |
| D-A22 | `GroundingVerifier` post-Agent step in `Sprk.Bff.Api/Services/Ai/CitationVerification/` — mechanical (regex + substring + sliding window, 10K-char DoS cap) zero-LLM verifier. Runs in `InsightsResolverService` after the Insights Agent before returning. Failed citations stripped or annotated `[citation could not be verified]`. Platform primitive — also exposed for Action Engine consumption (LAVERN ADR 10.6). Closes the D-04 gap between provenance promise and code enforcement. | Platform primitive |
| D-A23 | `EvidenceGuard.Validate(result)` utility — runtime non-empty check on every tool handler returning evidence-bearing artifacts. Applied to `IFindComparableMattersTool`, `IGetMatterFactsTool`, `IAssessEvidenceSufficiencyTool`, `ISearchPrecedentsTool`. Throws `EvidenceRequiredException`. Belt-and-suspenders with C# type system per LAVERN Pattern #6. | Domain logic |
| D-A24 | `IDeclineToFindTool` in Insights Agent tool set — returns structured `DeclineResponse { Reason, Explanation, MinimumEvidenceNeeded, SuggestedActions, ConfidenceInDecline }`. Deterministic exit path; replaces "Agent decides to decline" prose with a tool the Agent must invoke when `IAssessEvidenceSufficiencyTool` returns insufficient. Makes D-06 enforcement mechanical, not LLM-coercible. | Domain logic |
| D-A25 | `ISanitizer` + `Smacl1Sanitizer` in `Sprk.Bff.Api/Services/Ai/IngestSanitization/` — strips zero-width Unicode (U+200B–U+200F, U+202A–U+202E, U+2060–U+206F, U+FEFF), HTML comments, ANSI escapes, control chars before any LLM sees text. Audit log to App Insights custom events. Platform primitive — also exposed for Action Engine consumption (LAVERN ADR 10.6). Wired into stub closure-extraction ingest entry points so the Phase 2 real-document path inherits sanitization by default, not as a retrofit. | Platform primitive |
| D-A26 | **Precedent layer — architecture + scaffold (NOT lifecycle automation).** Implements the 4th tier per decisions.md D-03 and D-46. Includes: (a) `InsightArtifact` envelope supports `type: "precedent"` natively (envelope schema + C# types in D-A4). (b) `sprk_precedent` Dataverse entity + relationship tables (`sprk_precedent_observation` N:N, `sprk_precedent_related` N:N self) provisioned via PAC CLI per `dataverse-create-schema` skill. (c) `IPrecedentBoard` C# interface in `Sprk.Bff.Api/Services/Insights/Precedents/` with stub `DataversePrecedentBoard` implementation registered in DI. (d) Graph schema extension: `Precedent` vertex type + `OBSERVATION_SUPPORTS_PRECEDENT`, `PRECEDENT_RELATED_TO_PRECEDENT` Cosmos edge types (extends D-A6). (e) AI Search `insight-precedents` embedding index (schema designed, deployed via Bicep — extends D-A3). (f) `ISearchPrecedentsTool`, `ICitePrecedentTool` stub interfaces in Insights Agent tool set (extends D-A9). Cross-references LAVERN ADR 10.1. **Lifecycle automation deferred to Phase 1.5 — see §3.3.** | Domain types + Substrate |
| D-A27 | Admin endpoint `POST /api/insights/admin/precedents` (JWT-authorized; admin role per ADR-008 endpoint filter) — manual create/confirm/deprecate of Precedents for end-to-end testing in Phase 1. Enables Phase 1 acceptance: a manually-created Confirmed Precedent can be retrieved by `ISearchPrecedentsTool`, cited by Insights Agent in an Inference response with provenance link `precedent://M-1234#cure-period-ip`. Becomes optional/admin-only when Phase 1.5 lifecycle automation lands. | API |

> **Note on numbering**: D-A15 through D-A21 are architecture-doc r2 additions (evaluation / surfacing / MCP / customer-onboarding scope) back-propagated into this SPEC on 2026-05-22 alongside the LAVERN-derived D-A22–D-A27. The previous DEF-15 tracking item is resolved. Numbering is now contiguous from D-A1 to D-A27. Cross-references: `INSIGHTS-ENGINE-ARCHITECTURE.md` §21.2 (full Phase 1 deliverable narrative); `decisions.md` D-39–D-45 (the canonical decisions backing D-A15–D-A21); `decisions.md` D-46–D-51 (the canonical decisions backing D-A22–D-A27).

### 3.2 Out of scope (Track B — blocked on Phase C auth work)

| ID | Deliverable | Blocked by |
|---|---|---|
| D-B1 | `DataverseWebhookIntake` Function (HTTPTrigger) with clientState validation copied from BFF webhook handlers | Auth team A1, A2 |
| D-B2 | Dataverse webhook registration (via plugin registration tool) pointing at Intake Function | D-B1 |
| D-B3 | `InsightsSyncFunction` (ServiceBusTrigger) — projects Dataverse records → InsightArtifacts → AI Search + Graph | D-B1 |
| D-B4 | `InsightsReconciliation` Function (TimerTrigger) — Dataverse change-tracking + idempotent re-sync | D-B1 |
| D-B5 | `ClosureExtractionTrigger` Function — fires JPS closure-extraction playbook on matter milestones | D-B1, D-A12 |
| D-B6 | HMAC-SHA256 validation upgrade when Phase C task 044 lands | Phase C #044 |
| D-B7 | End-to-end real-data Inference response (replaces Track A mock data) | D-B1 through D-B5 |

### 3.3 Deferred to Phase 1.5 (the immediate next phase, after Track A lands)

Phase 1 ships the Precedent layer **architecture + scaffold** (D-A26, D-A27). Phase 1.5 ships the lifecycle automation, with thresholds and SME workflow design informed by real Observations flowing through Track B and real customer SME input:

- `PrecedentDecayJob` — daily background job decaying `effectivenessScore` on stale Precedents
- `PrecedentPromotionJob` — daily consolidation pass; promotes Tentative → Confirmed when `timesUsed ≥ CONFIRM_THRESHOLD` AND all outcomes positive
- Drift detection — `negativeOutcomes ≥ 2` flips Confirmed → UnderDriftReview
- Hybrid retrieval dedup — Azure AI Search vector + BM25 match for semantic dedup on `sprk_patternsignature`
- Curator consolidation logic — re-scoring Tentative Precedents as more signal accumulates
- SME review queue UI — workspace context pane / dedicated Code Page / Teams app (surface choice DEF-09)
- Inference layer updated to cite Precedents by reference instead of re-deriving from Observations
- Mode 4 (Precedent curation) and Mode 6 (weekly briefing of newly Confirmed Precedents) per `ADVANCED-AI-USE-CASE-PATTERNS.md`

Phase 1.5 calibration of thresholds (CONFIRM_THRESHOLD, decay rate, drift threshold) is intentionally deferred — see DEF-08 in `decisions.md`.

### 3.5 Spaarke BFF AI Facade — architectural boundary compliance (binding)

This project lives inside `Sprk.Bff.Api/` and shares its codebase with parallel work in [`projects/sdap-bff-api-remediation-fix/`](../sdap-bff-api-remediation-fix/) (BFF remediation). That project's **Outcome E** introduces a facade at `Sprk.Bff.Api/Services/Ai/PublicContracts/` to enforce a clean separation between AI internals and domain code. The Insights Engine project MUST comply with this boundary from day one — getting it wrong now creates 27+ new direct couplings that the remediation project's CI gate (FR-C6, lands in `sdap-bff-api-remediation-fix` Phase 6 task 082) will reject.

#### 3.5.1 The two zones

`Sprk.Bff.Api/Services/` has two zones with different rules:

**Zone A — AI-internal** (anything under `Services/Ai/`)
- Knows about LLM clients, prompts, embedding models, playbook engines, tool framework, retrieval pipelines
- May freely import `IOpenAiClient`, `IPlaybookService`, `IChatClient`, `UseFunctionInvocation`, `Microsoft.Extensions.AI.*`, `Microsoft.SemanticKernel.*`, `OpenAI.*`, `Azure.AI.*`, etc.
- Free to depend on other Zone A code

**Zone B — Domain / CRUD** (everything else — `Services/Insights/`, `Services/Workspace/`, `Services/Finance/`, `Services/Jobs/` outside `Services/Ai/Jobs/`, `Services/Dataverse/`, `Services/Communication/`, `Api/`, `Endpoints/`, `Filters/`, `Models/`)
- Must NOT import AI internals listed above
- May ONLY consume AI via interfaces under `Services/Ai/PublicContracts/`

#### 3.5.2 Insights Engine deliverable placement

| Deliverable | Original placement | Required placement | Zone |
|---|---|---|---|
| D-A4 `InsightArtifact` envelope POCOs | `Models/Insights/` | unchanged | B (POCOs only — no AI imports) |
| D-A5/A6 `IInsightGraph` + `CosmosNoSqlInsightGraph` | `Services/Insights/Graph/` | unchanged | B |
| D-A7 `LiveFactResolverService` | `Services/Insights/Facts/` | unchanged | B |
| D-A8 `InsightsResolverService` | `Services/Insights/` | unchanged — but MUST call AI only via `IInsightsAi` facade (no direct `IChatClient`, `IOpenAiClient`, `IPlaybookService`, or `InsightsAgent` injection) | B |
| **D-A9 Insights Agent + tools** | ~~`Services/Insights/Agent/`~~ | **`Services/Ai/Insights/`** (mirrors `Services/Ai/Chat/`) | **A (moved)** |
| D-A11 `POST /api/insights/ask` | `Api/Insights/InsightEndpoints.cs` | unchanged — calls `InsightsResolverService` | B |
| D-A22 `GroundingVerifier` | `Services/Ai/CitationVerification/` | unchanged | A (correctly placed) |
| D-A25 `Smacl1Sanitizer` | `Services/Ai/IngestSanitization/` | unchanged | A (correctly placed) |
| D-A26 `IPrecedentBoard` + `DataversePrecedentBoard` | `Services/Insights/Precedents/` | unchanged — Dataverse access, no AI imports | B |

The D-A9 move matches the existing convention for the SprkChat system: `Services/Ai/Chat/SprkChatAgent.cs` is the AI-internal agent; CRUD callers route to it through endpoint+facade. Insights follows the same pattern.

#### 3.5.3 The IInsightsAi facade contract

A new interface `Sprk.Bff.Api/Services/Ai/PublicContracts/IInsightsAi.cs` exposes the Insights Agent's capabilities to Zone B. Small, focused, named after the domain need — NOT after the AI mechanism:

```csharp
public interface IInsightsAi
{
    Task<InsightsAgentResult> AnswerQuestionAsync(
        InsightsAgentRequest request,
        CancellationToken ct);
}
```

Implementation in `Services/Ai/Insights/InsightsAgent.cs` (Zone A) wires `IChatClient`, registers tools, invokes function-calling. `InsightsResolverService` (Zone B) injects `IInsightsAi` and never sees `IChatClient` directly.

If the facade is missing a method the Resolver needs, add ONE method to `IInsightsAi` — do NOT widen the facade with raw `IChatClient` access.

#### 3.5.4 Forbidden imports in Zone B

In `Services/Insights/**` (other than D-A22/A25 Zone A primitives), `Api/Insights/**`, `Models/Insights/**`, the following imports are FORBIDDEN:

- `Microsoft.Extensions.AI.*` (incl. `IChatClient`, `UseFunctionInvocation`)
- `Microsoft.SemanticKernel.*`
- `OpenAI.*`, `Azure.AI.OpenAI.*`
- `Sprk.Bff.Api.Services.Ai.IOpenAiClient`
- `Sprk.Bff.Api.Services.Ai.IPlaybookService`
- `Sprk.Bff.Api.Services.Ai.Chat.*` (any Chat agent/tool/factory)
- `Sprk.Bff.Api.Services.Ai.Insights.*` (Insights Agent itself — only the facade interface is allowed)
- Direct construction of `KernelBuilder`, `OpenAIClient`, etc.

Allowed AI-related imports in Zone B:
- `Sprk.Bff.Api.Services.Ai.PublicContracts.*` (the facade interfaces)
- `Sprk.Bff.Api.Models.Ai.*` (POCO request/response shapes)

#### 3.5.5 Verification

A grep-based verification gate (FR-C6 from `sdap-bff-api-remediation-fix` Phase 6 task 082) will block PRs that violate the boundary. Until that lands, Phase 1 Track A acceptance includes a manual grep check (see §5.1.1 below).

Reference pattern: see `Services/Ai/Chat/` (existing SprkChat agent) for the canonical Zone A placement + tool organization that Insights should mirror.

#### 3.5.6 Why this matters

AI internals — model selection, prompt templates, tool wiring, embedding strategy — change frequently. Without the facade, every Insights Engine deliverable in Zone B becomes coupled to those internals, and every AI-team refactor breaks domain code. With the facade, the AI team can swap providers, rewire tools, and tune prompts without touching `InsightsResolverService` or any other Zone B code. This is the same architectural concern that drove `sdap-bff-api-remediation-fix` Outcome E to migrate 59 existing CRUD→AI couplings; the goal is to NOT recreate the problem with new Insights Engine code.

### 3.6 Explicitly NOT in scope — Phase 2+

- Additional Insight indexes (`insight-sessions` enrichment, etc.)
- Additional graph entities (Person, Firm, Judge, Issue) — Phase 1 only models Matter + Party + INVOLVED_PARTY edge + Precedent vertex
- Outlook/Teams surfaces
- Per-tenant deployment to customer environments (Phase 1 deploys to Spaarke Dev only)
- Closure-extraction playbook IMPLEMENTATION (Phase 1 designs it; Phase 2 builds it; sanitization primitive from D-A25 is wired in from day one)
- Identity resolution SAME_AS edges (Phase 2)
- AI Search S2 tier bump (Phase 2+)
- Migrate `PlaybookIndexingBackgroundService` to Function (Phase 3 cleanup)
- **EvaluatorGate** (LAVERN Pattern #2) — Phase 2+ quality upgrade; depends on provider tier abstraction (LAVERN Pattern #11) shipping first
- **Full GateResolver consumption** — Phase 2+ when write-back paths land; the primitive is built by Action Engine MVP per `coordination-assessment-with-insights-engine.md` §4.6 (LAVERN ADR 10.3)
- Cross-tenant Precedent publishing — Phase 2+ with major privacy/legal work first (DEF-10)

## 4. Architecture summary

Per [decisions.md](decisions.md), Phase 1 commits to:

- **Taxonomy**: **four-tier** Fact / Observation / Precedent / Inference per D-03 and D-46. Precedent layer architecture + scaffold lands in Phase 1 (D-A26, D-A27); lifecycle automation lands in Phase 1.5.
- **Substrate**: Azure AI Search (existing service; new indexes `insight-matters`, `insight-decisions`, `insight-risks`, `insight-sessions`, **`insight-precedents`**) + Cosmos NoSQL adjacency-list (new account; vertices include `Precedent`) + Live Dataverse Facts + `sprk_precedent` Dataverse entity
- **Embedding**: `text-embedding-3-large` (3072 dim)
- **Synthesis**: custom Insights Agent in `Sprk.Bff.Api/Services/Ai/Insights/` (Zone A, mirrors `Services/Ai/Chat/`) reusing existing `IChatClient` + UseFunctionInvocation + tool framework. Exposed to Zone B (the `InsightsResolverService`) via `Services/Ai/PublicContracts/IInsightsAi` facade per §3.5.
- **LAVERN-derived enforcement primitives** (turn the honesty contract from principle to mechanism):
  - `ISanitizer` (D-A25) — strips prompt-injection vectors before any LLM step
  - `GroundingVerifier` (D-A22) — mechanical post-Agent citation check
  - `EvidenceGuard.Validate` (D-A23) — runtime non-empty guard on evidence-bearing tool outputs
  - `IDeclineToFindTool` (D-A24) — deterministic decline path for insufficient evidence
- **Sync (Track B)**: Azure Functions on Flex Consumption + Service Bus topic + intake Function as auth trust boundary
- **Auth**: `Microsoft.Identity.Web` for inbound JWT (mirror `Sprk.Bff.Api/Program.cs`); `DefaultAzureCredential` for outbound; clientState → HMAC on Dataverse → Intake Function hop
- **Tenant isolation**: physical per-tenant; r1 uses tenant-list-as-configuration in Bicep params

Full diagrams and rationale: [design.md](design.md). LAVERN pattern adoption rationale: [lavern-pattern-assessment.md](lavern-pattern-assessment.md).

## 5. Acceptance criteria (Phase 1)

### Track A acceptance

- [ ] All Bicep modules deploy cleanly to Spaarke Dev environment (`spe-infrastructure-westus2`)
- [ ] All **5** AI Search indexes provisioned with correct schema (`insight-matters`, `insight-decisions`, `insight-risks`, `insight-sessions`, `insight-precedents`) — tenantId, 3072-dim vectors, vectorFilterMode-preFilter friendly
- [ ] Cosmos NoSQL account + database + containers provisioned; partition key strategy verified; Precedent vertex type + `OBSERVATION_SUPPORTS_PRECEDENT` / `PRECEDENT_RELATED_TO_PRECEDENT` edges representable
- [ ] `sprk_precedent` Dataverse entity + `sprk_precedent_observation` + `sprk_precedent_related` relationship tables provisioned and queryable
- [ ] `Sprk.Bff.Api` builds and existing tests pass after additions (no regressions)
- [ ] Unit + integration tests for new services pass
- [ ] **4-tier envelope** round-trips through `InsightArtifact` C# types (Fact / Observation / Precedent / Inference all serialize/deserialize correctly)
- [ ] `IPrecedentBoard` stub registered in DI and discoverable
- [ ] **End-to-end Precedent smoke test**: Precedent created via `POST /api/insights/admin/precedents`; `ISearchPrecedentsTool` retrieves it; Insights Agent cites it in an Inference response with provenance link `precedent://...`
- [ ] `POST /api/insights/ask` with `{question: "predict-matter-cost", subject: "matter:X"}` returns a structured `InsightResponse` (with mock data) demonstrating:
  - The 4-tier artifact envelope shape
  - Provenance pointers (`evidence[]`) including Precedent refs where applicable
  - Either an Inference with citations OR a structured `insufficient_evidence` response (via `IDeclineToFindTool`)
- [ ] **`GroundingVerifier` post-step** strips one known-bad citation from a synthetic Inference response in unit tests
- [ ] **`EvidenceGuard.Validate`** rejects programmatically-constructed empty-evidence artifact in unit tests
- [ ] **`IDeclineToFindTool`** produces structured `DeclineResponse` (not freely-composed prose) when invoked from low-evidence path
- [ ] **`ISanitizer` audit log** fires for one synthetic adversarial input (zero-width Unicode + ANSI escape) in unit tests
- [ ] All ADR compliance verified via `/adr-check` skill (no new violations)
- [ ] Zero new SAS keys, zero new `ClientSecretCredential` usages (per D-24, D-27)

### 5.1.1 §3.5 AI facade boundary acceptance (binding)

- [ ] D-A9 Insights Agent + tools land at `Services/Ai/Insights/` and `Services/Ai/Insights/Tools/` (NOT `Services/Insights/Agent/`)
- [ ] `Services/Ai/PublicContracts/IInsightsAi.cs` interface created and registered in DI
- [ ] D-A8 `InsightsResolverService` injects `IInsightsAi` only — verified by grep: zero hits for `IChatClient`, `IOpenAiClient`, `IPlaybookService`, `IChatAgent`, `Microsoft.Extensions.AI`, `Microsoft.SemanticKernel`, `OpenAI`, `Azure.AI.OpenAI`, or `Sprk.Bff.Api.Services.Ai.Chat`, `Sprk.Bff.Api.Services.Ai.Insights` in:
  - `Services/Insights/**/*.cs` (except D-A22/A25 which are explicitly Zone A primitives — those live at `Services/Ai/CitationVerification/` and `Services/Ai/IngestSanitization/`)
  - `Api/Insights/**/*.cs`
  - `Models/Insights/**/*.cs`
  Suggested verification command:
  ```bash
  grep -rE "IChatClient|IOpenAiClient|IPlaybookService|Microsoft\.Extensions\.AI|Microsoft\.SemanticKernel|using OpenAI|Azure\.AI\.OpenAI|Services\.Ai\.Chat|Services\.Ai\.Insights[^.P]" \
    src/server/api/Sprk.Bff.Api/Services/Insights/ \
    src/server/api/Sprk.Bff.Api/Api/Insights/ \
    src/server/api/Sprk.Bff.Api/Models/Insights/
  # Expect: zero matches (or only `Services.Ai.PublicContracts` references)
  ```
- [ ] Insights Engine Phase 1 PR(s) reference §3.5 in description as compliance check; future post-Phase-1 PRs touching `Services/Insights/` or `Api/Insights/` MUST pass the same grep before merge (interim manual check until `sdap-bff-api-remediation-fix` Phase 6 task 082 lands the FR-C6 CI gate)

### Track B acceptance (when unblocked)

- [ ] Dataverse webhook fires the Intake Function on `sprk_matter` create/update
- [ ] Service Bus message lands in the topic; consumer Function reads it
- [ ] InsightArtifact written to `insight-matters` index with all required fields
- [ ] Graph vertex + edges created for the matter and its involved parties
- [ ] End-to-end: a new `sprk_matter` in Dataverse triggers sync → `predict-matter-cost` query returns real (not mock) data within 60 seconds
- [ ] Zero SAS keys in the production pipeline (transitional `clientState` is the only shared secret; HMAC replaces it when Phase C #044 lands)

## 6. Dependencies and blockers

### 6.1 Internal (Spaarke team)

| # | Dependency | Owner | Status | Blocking |
|---|---|---|---|---|
| DEP-1 | Auth team responses to A1 (`clientState` validation code reference), A2 (Phase C #044 ETA + API shape), A3 (app reg model confirmation) — see decisions.md "Phase C coordination" | Auth team | **In flight** | Track B start |
| DEP-2 | Auth team responses to A4-A6 (informational: #047 template, #041-042 outbound, decisions.md feedback) | Auth team | **In flight** | Track B polish |
| DEP-3 | Resolution of O-02 (decisions.md): does `accessibleMatterSet` come from unified access control project or do we maintain our own source? | Architecture | Open | D-A8 (InsightsResolverService trimming logic) |
| DEP-4 | Resolution of O-01 (decisions.md): JPS or specialized format for closure-extraction playbook | Architecture | Open | D-A12 (playbook design doc), D-B5 (Phase 2 impl) |
| DEP-5 | LAVERN ADRs **10.1** (Precedent Board), **10.6** (Sanitization + Citation Verification Standard) ratified jointly with `ai-advanced-capabilities-development` project. ADRs proposed in `projects/ai-advanced-capabilities-development/LAVERN-ANALYSIS-AND-PLAN.md` §10. | Both projects (joint) | Proposed; not yet ratified | D-A22, D-A25, D-A26 design freeze |
| DEP-6 | Coordinate `IGateResolver` interface design (LAVERN ADR **10.3**) — built by Action Engine MVP; Insights consumes for Phase 2+ write-back paths. Tracked in `coordination-assessment-with-insights-engine.md` §4.6 (new). | Action Engine team | Pending Action Engine pipeline | No Phase 1 implementation; Phase 2+ consumer only |
| DEP-7 | **AI facade boundary compliance per §3.5** — Insights Engine MUST place D-A9 Insights Agent under `Services/Ai/Insights/` (NOT `Services/Insights/Agent/`) and consume it via `Services/Ai/PublicContracts/IInsightsAi` facade. Coordinates with `projects/sdap-bff-api-remediation-fix/` Outcome E which establishes the same boundary for the existing 59 CRUD→AI couplings, and with FR-C6 CI gate that lands in that project's Phase 6 task 082 to make the boundary mechanically enforced. | sdap-bff-api-remediation-fix project (Outcome E task 046 creates the facade scaffold; Phase 6 task 082 adds CI gate) | Outcome E task 046 ETA: post-Phase-3 baseline closure | D-A9 placement decision (resolved per §3.5); D-A8 Resolver imports (must use facade); Phase 1 Track A acceptance gate (§5.1.1) |

### 6.2 External (Azure / Microsoft)

| # | Dependency | Status | Notes |
|---|---|---|---|
| EXT-1 | Flex Consumption availability in westus2 | Verify before Bicep | Per knowledge research, available; Bicep should validate region support |
| EXT-2 | `text-embedding-3-large` model deployed in `spaarke-openai-dev` | Verify before D-A6 | Existing OpenAI account may have older deployments; may need explicit model deployment via Bicep |

## 7. Rigor and quality

- Per [CLAUDE.md §8](../../CLAUDE.md), this is a **FULL rigor** project (tags include `bff-api`, modifies `.cs` files, 20+ deliverables, dependencies on multiple tasks).
- All tasks run via `task-execute` skill with code-review + adr-check quality gates at Step 9.5.
- Phase C coordination is a continuous concern — every auth-touching task must reference decisions.md §D-22 to D-28.
- LAVERN coordination is a continuous concern — every LAVERN-derived task (D-A22 to D-A27) must reference decisions.md §D-46 to D-51 and the LAVERN ADR proposals in `projects/ai-advanced-capabilities-development/LAVERN-ANALYSIS-AND-PLAN.md`.

## 8. Phasing within Phase 1

A natural ordering for Track A:

| Wave | Tasks | Rationale |
|---|---|---|
| W1 | D-A1, D-A4, D-A5 | Foundation: scaffolding + types (now 4-tier per D-03 / D-A4) + interfaces (no runtime dependencies) |
| W1.5 | **D-A19, D-A20, D-A21** | r2 design docs — surfacing design, MCP contract, customer-onboarding workflow design (parallel with W2; no code, but blocks W6 endpoint shipping) |
| W2 | D-A2, D-A3, D-A13 | Bicep + index schemas (5 indexes including `insight-precedents`) + initial deployment |
| W2.5 | **D-A18, D-A25** | Platform primitives: `ISanitizer` + `Smacl1Sanitizer` (D-A25) + observability emitters (D-A18) — parallel with W3 |
| W3 | D-A6, D-A7 | Substrate implementations: Cosmos graph (with Precedent vertex type) + Live Facts. Independent, can run in parallel. |
| W3.5 | **D-A26, D-A15** | Precedent layer architecture + scaffold (D-A26); synthetic corpus generator (D-A15) — depends on W1 envelope + W3 Cosmos graph |
| W4 | D-A8 | Resolver orchestration (depends on W3 + W3.5 — must be Precedent-aware) |
| W4.5 | **D-A22, D-A23, D-A24** | Enforcement primitives: `GroundingVerifier` (post-Agent), `EvidenceGuard.Validate` (runtime guard), `IDeclineToFindTool` (decline path) — parallel with W5 |
| W5 | D-A9, D-A10 | Insights Agent + first question + Precedent tool stubs (`ISearchPrecedentsTool`, `ICitePrecedentTool`) (depends on W4 for resolver dispatch + W4.5 for honesty primitives) |
| W5.5 | **D-A16** | Golden dataset v1 — depends on W5 question catalog being defined; 10–15 tuples for `predict-matter-cost` |
| W6 | D-A11 | API endpoint (depends on W5 for agent) |
| W6.5 | **D-A27, D-A17** | Precedent admin endpoint (D-A27) + eval harness CI gate (D-A17) — parallel with W6; eval harness depends on D-A16 dataset and D-A18 observability |
| W7 | D-A12, D-A14 | Closure-extraction design + smoke tests (final integration, includes Precedent end-to-end smoke test + LAVERN primitive smoke tests + golden dataset eval harness baseline run) |

Tasks within a wave can be parallel; waves are sequential.

## 9. References

### 9.1 Project documents
- [decisions.md](decisions.md) — anchor doc; 51 numbered decisions (D-01 to D-38 baseline + architecture-doc r2 D-39 to D-45 + LAVERN-derived D-46 to D-51)
- [design.md](design.md) — comprehensive design (13 sections)
- [ai-inventory.md](ai-inventory.md) — DI-anchored existing AI service inventory
- [azure-inventory.md](azure-inventory.md) — Dev + Demo Azure inventories
- [README.md](README.md) — project overview
- [lavern-pattern-assessment.md](lavern-pattern-assessment.md) — LAVERN pattern-by-pattern analysis + decision basis for D-A22 through D-A27

### 9.1a Related Spaarke projects
- [`projects/sdap-bff-api-remediation-fix/`](../sdap-bff-api-remediation-fix/) — **AI facade source-of-truth (§3.5, DEP-7)**. Outcome E task 046 creates `Services/Ai/PublicContracts/`; tasks 047–050 migrate 59 existing CRUD→AI couplings through it; Phase 6 task 082 lands the FR-C6 CI gate that mechanically enforces the boundary. Insights Engine consumes the facade scaffold and must not reintroduce direct couplings.
- [`projects/ai-advanced-capabilities-development/LAVERN-ANALYSIS-AND-PLAN.md`](../ai-advanced-capabilities-development/LAVERN-ANALYSIS-AND-PLAN.md) — source of the 12 patterns + 6 proposed ADRs
- [`projects/ai-advanced-capabilities-development/ADVANCED-AI-USE-CASE-PATTERNS.md`](../ai-advanced-capabilities-development/ADVANCED-AI-USE-CASE-PATTERNS.md) — the six user-interaction modes that consume the Engine
- [`projects/ai-spaarke-action-engine-r1/action-engine-overview.md`](../ai-spaarke-action-engine-r1/action-engine-overview.md) — sister project; consumer of Insights' signals + Precedents
- [`projects/ai-spaarke-action-engine-r1/coordination-assessment-with-insights-engine.md`](../ai-spaarke-action-engine-r1/coordination-assessment-with-insights-engine.md) — joint decisions including §4.6 (GateResolver), §4.7 (shared platform primitives), §4.8 (Tool Registry metadata)

### 9.2 Knowledge base (researcher-authored)
- [knowledge/azure-ai-search/](../../knowledge/azure-ai-search/) — vector search, integrated vectorization, security trimming
- [knowledge/cosmos-gremlin/](../../knowledge/cosmos-gremlin/) — strategic direction signals; supports D-09 (NoSQL not Gremlin)
- [knowledge/azure-functions-isv/](../../knowledge/azure-functions-isv/) — Flex Consumption + per-tenant UAMI patterns
- [knowledge/dataverse-sync/](../../knowledge/dataverse-sync/) — Service Bus + Timer pattern; webhook payload caveats
- [knowledge/foundry-memory-patterns/](../../knowledge/foundry-memory-patterns/) — two-tier memory pattern reference

### 9.3 ADRs
- [ADR-001](../../docs/adr/ADR-001-minimal-api-and-workers.md) — BFF runtime + Functions permitted for narrow out-of-band integration
- [ADR-008](../../docs/adr/ADR-008-endpoint-filter-authorization.md) — endpoint filter authorization
- [ADR-009](../../docs/adr/ADR-009-redis-first-caching.md) — Redis-first caching
- [ADR-010](../../docs/adr/ADR-010-di-minimalism.md) — DI minimalism
- [ADR-013](../../docs/adr/ADR-013-ai-architecture.md) — AI architecture
- [ADR-016](../../docs/adr/ADR-016-ai-cost-rate-limit-and-backpressure.md) — Rate limiting
- [ADR-019](../../docs/adr/ADR-019-problemdetails.md) — ProblemDetails errors

### 9.4 Source code references
- [`Sprk.Bff.Api/Program.cs`](../../src/server/api/Sprk.Bff.Api/Program.cs) — reference inbound JWT pattern (to mirror in future Intake Function)
- [`Sprk.Bff.Api/Infrastructure/DI/AnalysisServicesModule.cs`](../../src/server/api/Sprk.Bff.Api/Infrastructure/DI/AnalysisServicesModule.cs) — feature module pattern (Insights module follows)
- [`Sprk.Bff.Api/Infrastructure/DI/AiModule.cs`](../../src/server/api/Sprk.Bff.Api/Infrastructure/DI/AiModule.cs) — `IChatClient` registration, tool framework, DI count audit (~290 lines, well-documented)
- [`Sprk.Bff.Api/Services/RecordMatching/DataverseIndexSyncService.cs`](../../src/server/api/Sprk.Bff.Api/Services/RecordMatching/DataverseIndexSyncService.cs) — existing Dataverse → AI Search sync pattern (template for Track B)
- [`Sprk.Bff.Api/Services/Ai/ReferenceIndexingService.cs`](../../src/server/api/Sprk.Bff.Api/Services/Ai/ReferenceIndexingService.cs) — existing idempotent indexer pattern (template for InsightArtifact indexing)

## 10. Pipeline next step

Run from this worktree:

```
/project-pipeline projects/ai-spaarke-insights-engine-r1
```

The pipeline will decompose this SPEC.md into POML tasks based on the wave structure in §8, prioritizing Track A. Track B tasks are gated on auth team responses (DEP-1, DEP-2).

When Phase C completes and DEP-1 resolves, author a follow-on `SPEC-track-b.md` for the sync wiring work.
