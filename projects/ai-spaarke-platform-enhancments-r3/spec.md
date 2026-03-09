# AI Resource Activation & Integration - AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-03-04
> **Source**: design.md (v2 — Tiered Knowledge Architecture)
> **Predecessor Projects**: ai-spaarke-platform-enhancements-r1 (Phases 1-4 complete), ai-json-prompt-schema (complete)
> **Related**: post-deployment-work.md (R1 Phase 5 validation + OPS items)

## Executive Summary

The AI platform infrastructure is built (R1 Phases 1-4) but underutilized. Azure AI Search indexes are empty, golden reference documents aren't deployed, model selection is hardcoded, and playbook execution doesn't leverage RAG or advanced AI resources. This project activates and integrates the AI resources that already exist in code but aren't operational — creating a tiered knowledge architecture that gives playbooks domain-specific reference language to dramatically improve analysis quality.

## Scope

### In Scope

1. **Index Architecture & Cleanup** — Create dedicated golden reference index (`spaarke-rag-references`), populate empty indexes (records, discovery), remove 3 deprecated indexes
2. **Golden Reference Deployment** — Deploy KNW-001–010 knowledge sources to Dataverse, build reference indexing pipeline, vectorize and index into dedicated reference index
3. **Knowledge-Augmented Execution** — Wire L1 reference retrieval + optional L2 document context + optional L3 entity context into playbook action node execution
4. **Model Selection Integration** — Wire existing `ModelSelector` into execution pipeline, deploy Azure OpenAI models (gpt-4o, gpt-4o-mini, o1-mini), add model selection to Playbook Builder UI
5. **Embedding Model Governance** — Document embedding strategy, clean up legacy 1536-dim vector fields, establish change protocol

### Out of Scope

- New tool handler implementations (Document Intelligence forms/tables, web search)
- R1 Phase 5 validation work (separate: post-deployment-work.md)
- Production ingestion pipeline for customer documents (OPS-01 in post-deployment-work.md)
- Cost monitoring infrastructure (OPS-06 in post-deployment-work.md)
- Multi-provider LLM support (Anthropic, OpenAI direct) — future
- Graph RAG / knowledge graph construction — future enhancement
- Playbook Builder prompt authoring (completed in ai-json-prompt-schema project)

### Affected Areas

- `src/server/api/Sprk.Bff.Api/Services/Ai/` — Core AI services (RagService, ModelSelector, handlers, node executors)
- `src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/GenericAnalysisHandler.cs` — Hardcoded model fix
- `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/AiAnalysisNodeExecutor.cs` — Knowledge retrieval injection
- `src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookOrchestrationService.cs` — Knowledge context propagation
- `src/server/api/Sprk.Bff.Api/Services/RecordMatching/DataverseIndexSyncService.cs` — Records index population
- `src/server/api/Sprk.Bff.Api/Services/Ai/RagIndexingPipeline.cs` — Discovery index dual-write validation
- `infrastructure/ai-search/` — Index schema definitions
- `src/client/code-pages/PlaybookBuilder/` — Model selection dropdown in action node config
- `projects/ai-spaarke-platform-enhancements-r1/scripts/` — Knowledge source seed scripts

## Requirements

### Functional Requirements

1. **FR-01**: Create `spaarke-rag-references` Azure AI Search index with schema supporting 3072-dim vectors, knowledge source ID, domain tags, version tracking — Acceptance: Index exists, accepts document uploads, returns search results
2. **FR-02**: Remove 3 deprecated indexes (`knowledge-index`, `spaarke-knowledge-index`, `spaarke-knowledge-shared`) — Acceptance: Indexes deleted from Azure AI Search, `AnalysisOptions.SharedIndexName` updated
3. **FR-03**: Deploy 10 golden reference knowledge sources (KNW-001–010) to Dataverse `sprk_analysisknowledge` records — Acceptance: All 10 records visible in Dataverse with content and playbook N:N relationships
4. **FR-04**: Build `ReferenceIndexingService` to chunk, embed, and index knowledge source content into `spaarke-rag-references` — Acceptance: Admin endpoint triggers indexing, ~100 chunks created from 10 sources
5. **FR-05**: Wire L1 knowledge retrieval into `AiAnalysisNodeExecutor` so playbook actions query the reference index before calling OpenAI — Acceptance: Action execution includes reference chunks in prompt context when knowledge sources are linked
6. **FR-06**: Support configurable knowledge retrieval per action node (`auto`/`always`/`never`, topK, includeDocumentContext, includeEntityContext) — Acceptance: Node config controls retrieval behavior
7. **FR-07**: Fix `GenericAnalysisHandler` to use model resolution chain: node override → `ModelSelector` routing → global default (not hardcoded `gpt-4o`) — Acceptance: Different operation types use different models
8. **FR-08**: Populate `spaarke-records-index` via `DataverseIndexSyncService.BulkSyncAsync()` and add `tenantId` field to schema — Acceptance: Matters/Projects/Invoices indexed and searchable with tenant scoping
9. **FR-09**: Validate discovery-index dual-write in `RagIndexingPipeline` — Acceptance: Re-indexing a document writes chunks to both knowledge-v2 and discovery-index
10. **FR-10**: Add model selection dropdown to Playbook Builder action node configuration — Acceptance: Users can select "Auto" or specific model deployment per action node
11. **FR-11**: Wire optional L2 customer document context retrieval from `spaarke-knowledge-index-v2` during playbook execution — Acceptance: When enabled, similar document chunks appear in prompt context
12. **FR-12**: Wire optional L3 entity context retrieval from `spaarke-records-index` during playbook execution — Acceptance: When enabled, matter/project metadata appears in prompt context

### Non-Functional Requirements

- **NFR-01**: Knowledge retrieval must not add >500ms to playbook action execution (p95) — reference index is small (~100 chunks), queries should be fast
- **NFR-02**: Reference index query results must be cached in Redis per (query-hash, knowledgeSourceIds, topK) for duration of playbook execution session to prevent duplicate calls across nodes
- **NFR-03**: All new admin endpoints must follow ADR-001 Minimal API patterns with ADR-008 endpoint filters for authorization
- **NFR-04**: Embedding generation for reference indexing must use `text-embedding-3-large` (3072 dimensions) — no legacy 1536-dim fields
- **NFR-05**: All index operations must be tenant-scoped — `tenantId` filter required on all queries
- **NFR-06**: No document content, model responses, or full prompts logged — only identifiers, sizes, timings, outcome codes (ADR-014)

## Technical Constraints

### Applicable ADRs

- **ADR-001**: Minimal API pattern — all new endpoints must be Minimal API with endpoint filters
- **ADR-004**: Job Contract — background indexing jobs must follow job contract schema with idempotency
- **ADR-008**: Endpoint filters — resource authorization via filters, no global middleware
- **ADR-009**: Redis-first caching — knowledge retrieval cache uses `IDistributedCache` (Redis), no L1 without profiling proof
- **ADR-010**: DI minimalism — ≤15 non-framework DI registrations, use feature modules
- **ADR-013**: AI Architecture — extend BFF, no separate AI microservice; AI endpoints follow ADR-001
- **ADR-014**: Data minimization — no document content or model output in logs
- **ADR-017**: Background job status — playbook execution handler tracks job status transitions
- **ADR-019**: ProblemDetails with stable errorCodes for all error responses

### MUST Rules

- ✅ MUST use Minimal API for all new endpoints (ADR-001)
- ✅ MUST use endpoint filters for authorization on admin endpoints (ADR-008)
- ✅ MUST use Redis (`IDistributedCache`) for cross-request caching (ADR-009)
- ✅ MUST scope all index queries by `tenantId` (ADR-013)
- ✅ MUST use BackgroundService + Service Bus for async indexing jobs (ADR-001, ADR-004)
- ✅ MUST implement idempotent job handlers with `IdempotencyKey` (ADR-004)
- ✅ MUST return ProblemDetails with `errorCode` extension for all failures (ADR-019)
- ✅ MUST log only identifiers, sizes, timings — never content or prompts (ADR-014)
- ❌ MUST NOT create separate microservice for knowledge retrieval (ADR-013)
- ❌ MUST NOT use Azure Functions or Durable Functions for indexing jobs (ADR-001)
- ❌ MUST NOT cache authorization decisions (ADR-009)
- ❌ MUST NOT inject GraphServiceClient directly into AI services (ADR-010)

### Existing Patterns to Follow

- See `src/server/api/Sprk.Bff.Api/Services/Ai/RagIndexingPipeline.cs` for document chunking + embedding + indexing pattern
- See `src/server/api/Sprk.Bff.Api/Services/Ai/RagService.cs` for Azure AI Search query pattern
- See `src/server/api/Sprk.Bff.Api/Services/Ai/ModelSelector.cs` for operation-type model routing
- See `src/server/api/Sprk.Bff.Api/Services/RecordMatching/DataverseIndexSyncService.cs` for bulk sync pattern
- See `projects/ai-spaarke-platform-enhancements-r1/scripts/Create-KnowledgeSourceRecords.ps1` for seed script pattern
- See `.claude/patterns/api/` for endpoint definition patterns

## Architecture: Tiered Knowledge System

### The Three Knowledge Layers

| Layer | Purpose | Index | Content | Delivery |
|-------|---------|-------|---------|----------|
| **Skills** (Instructions) | Tell the LLM WHAT to do | N/A (JPS in `sprk_systemprompt`) | Rules, checklists, output schemas | Direct prompt injection |
| **L1: Golden References** | Improve LLM understanding with expert language | `spaarke-rag-references` (NEW, dedicated) | Domain terminology, clause libraries, red flag catalogs | RAG from dedicated small index |
| **L2: Customer Documents** | Provide context from similar prior work | `spaarke-knowledge-index-v2` (existing) | Previously analyzed documents | RAG from main index (optional) |
| **L3: Entity Context** | Business entity awareness | `spaarke-records-index` (existing, needs population) | Matters, projects, invoices | Agentic retrieval (optional) |

### Skills vs Knowledge Distinction

- **Skills** = JPS instructions telling the LLM what to do (rules, checklists, output format). Stored in `sprk_analysisaction.sprk_systemprompt`. Always loaded for their action node.
- **Knowledge** = Vectorized domain reference language improving the LLM's understanding. Stored in `spaarke-rag-references` index. Retrieved via RAG (top-K most relevant chunks).
- The skill says "identify force majeure clauses." The knowledge gives the LLM expert language about what force majeure clauses look like — making analysis more accurate.

### Why Golden References Need a Dedicated Index

1. **Guaranteed retrieval** — In a small index (~100 chunks), relevant references reliably surface. In 100K+ docs, they compete and may not rank high enough.
2. **Different content nature** — References are authoritative, curated, versioned. Customer docs are varied, dynamic.
3. **Different query intent** — "What does expert knowledge say?" vs "What similar docs exist?"
4. **Deterministic behavior** — Playbook authors linking KNW-002 expect those references to reliably influence analysis.
5. **Independent lifecycle** — References updated by SMEs on curation schedule; customer docs flow through ingestion pipeline.

### Knowledge Retrieval Flow

```
Playbook executes Action Node
  → Load Skill (JPS prompt: role, task, constraints, output schema)
  → Resolve linked knowledge sources from playbook N:N relationships
  → Build semantic query from document content + action context
  → Query spaarke-rag-references (L1): top-K reference chunks
  → [Optional] Query spaarke-knowledge-index-v2 (L2): similar customer docs
  → [Optional] Query spaarke-records-index (L3): entity metadata
  → Assemble prompt: skill + reference tokens + document + context
  → Call OpenAI (model selected per node config / ModelSelector)
  → Return structured output
```

## Implementation Phases

### Phase 1: Index Architecture & Cleanup (10h)

| Task | Description | Key Files |
|------|-------------|-----------|
| 1A | Create `spaarke-rag-references` index with 3072-dim vector schema | `infrastructure/ai-search/spaarke-rag-references.json` |
| 1B | Remove 3 deprecated indexes | Azure portal / CLI |
| 1C | Populate records-index via `BulkSyncAsync()`, add `tenantId` field | `DataverseIndexSyncService.cs` |
| 1D | Validate discovery-index dual-write | `RagIndexingPipeline.cs` |
| 1E | Validate invoice index (defer if no data) | `InvoiceIndexingJobHandler.cs` |

### Phase 2: Golden Reference Deployment (12h)

| Task | Description | Key Files |
|------|-------------|-----------|
| 2A | Deploy KNW-001–010 to Dataverse | `Create-KnowledgeSourceRecords.ps1` |
| 2B | Build `ReferenceIndexingService` (chunk → embed → index to reference index) | New: `Services/Ai/ReferenceIndexingService.cs` |
| 2C | Index all 10 knowledge sources, verify retrieval | Admin endpoint |
| 2D | Wire reference retrieval into `RagService` / new `ReferenceRetrievalService` | `RagService.cs` or new service |

### Phase 3: Knowledge-Augmented Execution (14h)

| Task | Description | Key Files |
|------|-------------|-----------|
| 3A | Add L1 knowledge retrieval to `AiAnalysisNodeExecutor` | `AiAnalysisNodeExecutor.cs`, `GenericAnalysisHandler.cs` |
| 3B | Configurable retrieval per action (auto/always/never, topK) | `PlaybookOrchestrationService.cs` |
| 3C | Optional L2 customer document context | `RagService.cs` |
| 3D | Optional L3 entity context via records index | `RecordMatchingService.cs` |
| 3E | Knowledge retrieval caching in Redis | `IDistributedCache` |

### Phase 4: Model Selection Integration (8h)

| Task | Description | Key Files |
|------|-------------|-----------|
| 4A | Deploy/verify models on Azure OpenAI (gpt-4o, gpt-4o-mini, o1-mini) | Azure CLI |
| 4B | Wire `ModelSelector` into `GenericAnalysisHandler` (fix hardcoded gpt-4o) | `GenericAnalysisHandler.cs` |
| 4C | Document model selection guidelines per operation type | Documentation |
| 4D | Add model selection dropdown to Playbook Builder UI | `PlaybookBuilder` code page |

### Phase 5: Embedding Model Governance (3h)

| Task | Description | Key Files |
|------|-------------|-----------|
| 5A | Document current embedding strategy | Documentation |
| 5B | Stop writing to legacy 1536-dim `contentVector` fields | `RagIndexingPipeline.cs` |
| 5C | Document embedding model change protocol | Documentation |

## Success Criteria

1. [ ] `spaarke-rag-references` index exists with ~100 chunks from 10 knowledge sources — Verify: `az search index show` + document count query
2. [ ] 3 deprecated indexes removed — Verify: `az search index list` no longer includes them
3. [ ] All 10 pre-built playbook actions retrieve L1 knowledge during execution — Verify: Run playbook with logging, confirm reference chunks appear in prompt
4. [ ] Analysis quality measurably improves with reference context vs without — Verify: Side-by-side comparison of same document analysis
5. [ ] `GenericAnalysisHandler` uses `ModelSelector` chain (no hardcoded model) — Verify: Classification task uses gpt-4o-mini, reasoning task uses gpt-4o
6. [ ] gpt-4o, gpt-4o-mini, and o1-mini confirmed deployed on Azure OpenAI — Verify: `az cognitiveservices account deployment list`
7. [ ] `spaarke-records-index` populated with Matters + Projects + Invoices — Verify: Search query returns results
8. [ ] 4 of 5 active indexes have data (references, knowledge-v2, records, discovery) — Verify: Document count > 0 for each

## Dependencies

### Prerequisites

- Azure OpenAI model deployments (gpt-4o, o1-mini) — need verification/provisioning (Phase 4)
- Dataverse test records (Matters, Projects, Invoices) — should exist from testing (Phase 1C)
- Golden reference content (KNW-001–010) — content authored in R1, seed script exists (Phase 2A)
- Azure AI Search capacity for additional index — verify SKU supports it (Phase 1A)

### External Dependencies

- Azure AI Search service availability and SKU limits
- Azure OpenAI quota allocation for additional model deployments
- Dataverse environment availability for knowledge source deployment

### Relationship to Post-Deployment Work

**Recommended sequence**: Complete R3 first (activate infrastructure) → then Phase 5 evaluation (post-deployment-work.md AIPL-070–075). This gives the evaluation harness something meaningful to measure — playbooks with vs without knowledge augmentation.

| Activity | Owner |
|----------|-------|
| R3: Create reference index, deploy golden refs, wire knowledge retrieval, fix model selection | This project |
| AIPL-070–075: Test corpus, eval harness, quality baseline | post-deployment-work.md |
| OPS-03: Prompt refinement (needs R3 + baseline first) | post-deployment-work.md |

## Owner Clarifications

*Answers captured during design review:*

| Topic | Question | Answer | Impact |
|-------|----------|--------|--------|
| Semantic Search vs RAG | Are these the same resource? | No — Semantic Search is user-facing document retrieval (returns metadata); RAG is internal LLM context augmentation (returns chunk text tokens) | Separate services, sometimes shared index |
| Golden reference index | Should references be in the same index as customer docs? | No — dedicated index needed. References would be buried in 100K+ customer documents | New `spaarke-rag-references` index created |
| Golden reference value | Are references instructions (CAG) or vectorized knowledge (RAG)? | Knowledge — vectorized domain language indexed for semantic retrieval. Skills provide instructions; knowledge provides reference language that improves LLM pattern recognition | References go through full RAG pipeline (chunk → embed → index → retrieve) |
| Knowledge delivery mechanism | Plain text injection or vector retrieval? | Vector retrieval — the value comes from vector search finding the most semantically relevant reference chunks to inject as tokens | RAG against dedicated curated corpus |

## Assumptions

- Azure AI Search Basic/Standard SKU supports the additional `spaarke-rag-references` index (Basic supports 5 indexes, Standard supports 50)
- Existing `text-embedding-3-large` model deployment has sufficient quota for ~100 additional embedding requests during reference indexing
- Knowledge retrieval latency from a ~100-chunk index will be <100ms (well within 500ms NFR budget)
- Invoice index (Phase 1E) may be deferred if no invoice test data exists in Dataverse

## Unresolved Questions

- [ ] Which Azure OpenAI models are actually deployed vs just configured? Verification needed in Phase 4A — Blocks: Model selection guidelines
- [ ] Is discovery-index dual-write broken or just never triggered? Investigation in Phase 1D — Blocks: Discovery index population approach

## Reference Materials

### Industry Research
- [Standard RAG Is Dead: Why AI Architecture Split in 2026](https://ucstrategies.com/news/standard-rag-is-dead-why-ai-architecture-split-in-2026/)
- [RAG vs CAG: The Architect's Guide to LLM Memory](https://medium.com/@coyle_41098/rag-vs-cag-the-architects-guide-to-llm-memory-47b4b77eaaed)
- [The Next Frontier of RAG: Enterprise Knowledge Systems 2026-2030](https://nstarxinc.com/blog/the-next-frontier-of-rag-how-enterprise-knowledge-systems-will-evolve-2026-2030/)
- [Agentic RAG: How Intelligent Retrieval and Reasoning Are Reshaping Enterprise AI](https://www.kore.ai/blog/what-is-agentic-rag)
- [Azure AI Search RAG Best Practice](https://techcommunity.microsoft.com/blog/azure-ai-foundry-blog/rag-best-practice-with-ai-search/4357711)

### Spaarke Internal
- `docs/guides/SPAARKE-AI-ARCHITECTURE.md` — Current AI tool framework
- `docs/guides/RAG-ARCHITECTURE.md` — RAG pipeline design
- `docs/architecture/auth-AI-azure-resources.md` — Azure resource inventory
- `projects/ai-spaarke-platform-enhancements-r1/spec.md` — R1 specification

---

*AI-optimized specification. Original design: design.md (v2 — Tiered Knowledge Architecture)*
