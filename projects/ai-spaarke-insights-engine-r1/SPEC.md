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
| D-A4 | `InsightArtifact` envelope C# types in `Sprk.Bff.Api/Models/Insights/` — POCOs for Fact / Observation / Inference per [design.md §2.2](design.md) | Domain types |
| D-A5 | `IInsightGraph` interface in `Sprk.Bff.Api/Services/Insights/Graph/` — typed named traversal patterns per [design.md §4.2.2](design.md) | Domain types |
| D-A6 | `CosmosNoSqlInsightGraph` implementation — adjacency-list document model; vertex + edge upsert/get/delete; `FindMattersInvolvingPartyAsync`, `FindConnectedEntitiesAsync` named traversals; per-tenant partition key | Substrate |
| D-A7 | `LiveFactResolverService` in `Sprk.Bff.Api/Services/Insights/Facts/` — direct Dataverse queries via existing `IDataverseService`. Initial Facts: `matterDuration`, `totalSpend`, `status`, `daysSinceLastActivity`, `documentCount`. 5-minute Redis cache | Domain logic |
| D-A8 | `InsightsResolverService` skeleton in `Sprk.Bff.Api/Services/Insights/` — orchestration: question router, signal fetcher composing `IInsightGraph` + `LiveFactResolverService` + AI Search, provenance assembler, per-question TTL cache, `accessibleMatterSet` enforcement at every query | Domain logic |
| D-A9 | `Insights Agent` shell in `Sprk.Bff.Api/Services/Insights/Agent/` — extends existing `IChatClient` + tool framework. Tool interfaces: `IFindComparableMattersTool`, `IGetMatterFactsTool`, `IAssessEvidenceSufficiencyTool`. Stub implementations that return mock data | Domain logic |
| D-A10 | `predict-matter-cost` Inference question definition — question catalog entry, evidence-sufficiency rule (`comparableMatters.min: 12`), insufficient-evidence response shape, registered with the Insights Agent | Domain logic |
| D-A11 | `POST /api/insights/ask` Minimal API endpoint in `Sprk.Bff.Api/Api/Insights/InsightEndpoints.cs` — accepts `InsightRequest`, returns `InsightResponse`. Endpoint filter for resource auth per ADR-008. Rate limiting per ADR-016. ProblemDetails errors per ADR-019 | API |
| D-A12 | Closure-extraction JPS playbook DESIGN document (not implementation) in `projects/ai-spaarke-insights-engine-r1/closure-extraction-playbook-design.md` — what it emits, version handling, target indexes | Design |
| D-A13 | Initial Bicep deployment to dev environment — provisions resources only (no functions deployed); verifies all Bicep modules work; documents the per-tenant parameter file pattern | DevOps |
| D-A14 | Smoke tests: unit tests for envelope serialization, `IInsightGraph` interface contracts, `LiveFactResolverService` against mocked Dataverse; integration tests for `InsightsResolverService` end-to-end with mock data; AI Search index provisioning verification (round-trip schema deploy + read) | Tests |

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

### 3.3 Explicitly NOT in scope (deferred to Phase 2+)

- Additional Insight indexes (`insight-sessions` enrichment, etc.)
- Additional graph entities (Person, Firm, Judge, Issue) — Phase 1 only models Matter + Party + INVOLVED_PARTY edge
- Outlook/Teams surfaces
- Per-tenant deployment to customer environments (Phase 1 deploys to Spaarke Dev only)
- Closure-extraction playbook IMPLEMENTATION (Phase 1 designs it; Phase 2 builds it)
- Identity resolution SAME_AS edges (Phase 2)
- AI Search S2 tier bump (Phase 2+)
- Migrate `PlaybookIndexingBackgroundService` to Function (Phase 3 cleanup)

## 4. Architecture summary

Per [decisions.md](decisions.md), Phase 1 commits to:

- **Substrate**: Azure AI Search (existing service) + Cosmos NoSQL adjacency-list (new account) + Live Dataverse Facts
- **Embedding**: `text-embedding-3-large` (3072 dim)
- **Synthesis**: custom Insights Agent in `Sprk.Bff.Api`, reusing existing `IChatClient` + UseFunctionInvocation + tool framework
- **Sync (Track B)**: Azure Functions on Flex Consumption + Service Bus topic + intake Function as auth trust boundary
- **Auth**: `Microsoft.Identity.Web` for inbound JWT (mirror `Sprk.Bff.Api/Program.cs`); `DefaultAzureCredential` for outbound; clientState → HMAC on Dataverse → Intake Function hop
- **Tenant isolation**: physical per-tenant; r1 uses tenant-list-as-configuration in Bicep params

Full diagrams and rationale: [design.md](design.md).

## 5. Acceptance criteria (Phase 1)

### Track A acceptance

- [ ] All Bicep modules deploy cleanly to Spaarke Dev environment (`spe-infrastructure-westus2`)
- [ ] All 4 AI Search indexes provisioned with correct schema (tenantId, 3072-dim vectors, vectorFilterMode-preFilter friendly)
- [ ] Cosmos NoSQL account + database + containers provisioned; partition key strategy verified
- [ ] `Sprk.Bff.Api` builds and existing tests pass after additions (no regressions)
- [ ] Unit + integration tests for new services pass
- [ ] `POST /api/insights/ask` with `{question: "predict-matter-cost", subject: "matter:X"}` returns a structured `InsightResponse` (with mock data) demonstrating:
  - The artifact envelope shape
  - Provenance pointers (`evidence[]`)
  - Either an Inference with citations OR a structured `insufficient_evidence` response
- [ ] All ADR compliance verified via `/adr-check` skill (no new violations)
- [ ] Zero new SAS keys, zero new `ClientSecretCredential` usages (per D-24, D-27)

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

### 6.2 External (Azure / Microsoft)

| # | Dependency | Status | Notes |
|---|---|---|---|
| EXT-1 | Flex Consumption availability in westus2 | Verify before Bicep | Per knowledge research, available; Bicep should validate region support |
| EXT-2 | `text-embedding-3-large` model deployed in `spaarke-openai-dev` | Verify before D-A6 | Existing OpenAI account may have older deployments; may need explicit model deployment via Bicep |

## 7. Rigor and quality

- Per [CLAUDE.md §8](../../CLAUDE.md), this is a **FULL rigor** project (tags include `bff-api`, modifies `.cs` files, 14+ deliverables, dependencies on multiple tasks).
- All tasks run via `task-execute` skill with code-review + adr-check quality gates at Step 9.5.
- Phase C coordination is a continuous concern — every auth-touching task must reference decisions.md §D-22 to D-28.

## 8. Phasing within Phase 1

A natural ordering for Track A:

| Wave | Tasks | Rationale |
|---|---|---|
| W1 | D-A1, D-A4, D-A5 | Foundation: scaffolding + types + interfaces (no runtime dependencies) |
| W2 | D-A2, D-A3, D-A13 | Bicep + index schemas + initial deployment (validate infra deploys cleanly) |
| W3 | D-A6, D-A7 | Substrate implementations: Cosmos graph + Live Facts. Independent, can run in parallel. |
| W4 | D-A8 | Resolver orchestration (depends on W3) |
| W5 | D-A9, D-A10 | Insights Agent + first question (depends on W4 for resolver dispatch) |
| W6 | D-A11 | API endpoint (depends on W5 for agent) |
| W7 | D-A12, D-A14 | Closure-extraction design + smoke tests (final integration) |

Tasks within a wave can be parallel; waves are sequential.

## 9. References

### 9.1 Project documents
- [decisions.md](decisions.md) — anchor doc; 38 numbered decisions
- [design.md](design.md) — comprehensive design (13 sections)
- [ai-inventory.md](ai-inventory.md) — DI-anchored existing AI service inventory
- [azure-inventory.md](azure-inventory.md) — Dev + Demo Azure inventories
- [README.md](README.md) — project overview

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
