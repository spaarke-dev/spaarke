# Implementation Plan: AI Resource Activation & Integration (R3)

> **Last Updated**: 2026-03-04
> **Status**: Ready for Tasks
> **Spec**: [spec.md](spec.md)

---

## 1. Executive Summary

**Purpose**: Activate underutilized Azure AI resources (indexes, golden references, model selection) to create a tiered knowledge architecture that improves playbook analysis quality through domain-specific reference language retrieval.

**Scope**: 5 phases — Index Architecture, Golden Reference Deployment, Knowledge-Augmented Execution, Model Selection Integration, Embedding Governance.

**Estimated Effort**: ~47 hours across all phases.

---

## 2. Architecture Context

### Design Constraints

**From ADRs** (must comply):
- **ADR-001**: No Azure Functions — use Minimal API + BackgroundService for async indexing
- **ADR-004**: Idempotent job handlers with deterministic IdempotencyKey
- **ADR-008**: Endpoint filters for authorization on all admin endpoints
- **ADR-009**: IDistributedCache (Redis) for cross-request caching; version cache keys
- **ADR-010**: Concrete types; <= 15 non-framework DI registrations
- **ADR-013**: Extend BFF, not separate AI service; rate limit AI endpoints
- **ADR-014**: Tenant-scoped cache keys; version by model + content
- **ADR-017**: Job status tracking (Queued → Running → Completed/Failed)
- **ADR-019**: ProblemDetails with stable errorCode extension

**From Spec**:
- Knowledge retrieval must not add >500ms to action execution (p95)
- All index queries must be tenant-scoped
- No document content or model responses in logs
- Reference index uses text-embedding-3-large (3072-dim) only

### Key Technical Decisions

| Decision | Rationale | Impact |
|----------|-----------|--------|
| Dedicated `spaarke-rag-references` index | Golden references buried in 100K+ customer docs; need guaranteed retrieval | Phase 1A, 2B, 2D, 3A |
| Skills (JPS instructions) vs Knowledge (vectorized language) | Different purposes, different delivery mechanisms | Phase 3A prompt assembly |
| Model resolution chain: node → ModelSelector → default | Flexible per-action model selection without breaking existing | Phase 4B |
| Redis cache for RAG results per playbook session | Multiple nodes query same sources; avoid duplicate embedding/search | Phase 3E |

### Discovered Resources

**Applicable ADRs**:
- `.claude/adr/ADR-001-minimal-api.md` — Endpoint and worker patterns
- `.claude/adr/ADR-004-job-contract.md` — Background job contract
- `.claude/adr/ADR-008-endpoint-filters.md` — Authorization filters
- `.claude/adr/ADR-009-redis-caching.md` — Caching strategy
- `.claude/adr/ADR-010-di-minimalism.md` — DI registration limits
- `.claude/adr/ADR-013-ai-architecture.md` — AI services architecture
- `.claude/adr/ADR-014-ai-caching.md` — AI-specific caching
- `.claude/adr/ADR-019-error-handling.md` — Error response format

**Applicable Patterns**:
- `.claude/patterns/api/endpoint-definition.md` — Admin endpoint registration
- `.claude/patterns/api/service-registration.md` — DI module pattern
- `.claude/patterns/api/background-workers.md` — BackgroundService pattern
- `.claude/patterns/ai/analysis-scopes.md` — Scope resolution
- `.claude/patterns/caching/distributed-cache.md` — Redis cache pattern
- `.claude/patterns/testing/unit-test-structure.md` — Test patterns

**Knowledge Guides**:
- `docs/guides/SPAARKE-AI-ARCHITECTURE.md` — AI architecture reference
- `docs/guides/RAG-ARCHITECTURE.md` — RAG pipeline design
- `docs/guides/RAG-CONFIGURATION.md` — RAG configuration
- `docs/architecture/auth-AI-azure-resources.md` — Azure resource inventory

**Applicable Skills**:
- `azure-deploy` — Deploy infrastructure, verify model deployments
- `bff-deploy` — Deploy BFF API after server-side changes
- `adr-aware` — Load applicable ADRs (always-apply)
- `script-aware` — Discover existing scripts

**Existing Scripts**:
- `projects/ai-spaarke-platform-enhancements-r1/scripts/Create-KnowledgeSourceRecords.ps1`
- `projects/ai-spaarke-platform-enhancements-r1/scripts/Provision-AiSearchIndexes.ps1`

---

## 3. Phase Breakdown

### Phase 1: Index Architecture & Cleanup (10h)

**Objective**: Establish the target index architecture — create new reference index, remove deprecated indexes, populate empty ones.

**Deliverables**:
1. `spaarke-rag-references` Azure AI Search index created with 3072-dim vector schema
2. 3 deprecated indexes removed (knowledge-index, spaarke-knowledge-index, spaarke-knowledge-shared)
3. `spaarke-records-index` populated via bulk sync + tenantId field added
4. Discovery-index dual-write validated
5. Invoice index validated (deferred if no data)
6. `AnalysisOptions.SharedIndexName` updated

**Key Files**:
- `infrastructure/ai-search/spaarke-rag-references.json` (NEW)
- `Services/RecordMatching/DataverseIndexSyncService.cs`
- `Services/Ai/RagIndexingPipeline.cs`

### Phase 2: Golden Reference Deployment (12h)

**Objective**: Deploy knowledge source content to Dataverse, build indexing pipeline, vectorize and index into dedicated reference index.

**Deliverables**:
1. 10 `sprk_analysisknowledge` records deployed to Dataverse (KNW-001–010)
2. `ReferenceIndexingService` built (chunk → embed → index)
3. Admin endpoints for reference indexing
4. All 10 sources indexed (~100 chunks in `spaarke-rag-references`)
5. Reference retrieval method added to `RagService`

**Key Files**:
- `Services/Ai/ReferenceIndexingService.cs` (NEW)
- `Api/Ai/AdminKnowledgeEndpoints.cs` (NEW)
- `Services/Ai/RagService.cs` (extend)

### Phase 3: Knowledge-Augmented Execution (14h)

**Objective**: Wire knowledge retrieval into playbook action node execution with configurable L1/L2/L3 layers.

**Deliverables**:
1. L1 reference knowledge retrieval in `AiAnalysisNodeExecutor`
2. Configurable retrieval settings per action (auto/always/never, topK)
3. Optional L2 customer document context
4. Optional L3 entity context via records index
5. Redis caching for RAG results per playbook session
6. Prompt assembly: skill + knowledge tokens + document

**Key Files**:
- `Services/Ai/Nodes/AiAnalysisNodeExecutor.cs` (major changes)
- `Services/Ai/Handlers/GenericAnalysisHandler.cs` (prompt assembly)
- `Services/Ai/PlaybookOrchestrationService.cs` (knowledge context propagation)

### Phase 4: Model Selection Integration (8h)

**Objective**: Wire ModelSelector into execution pipeline, deploy models, add UI controls.

**Deliverables**:
1. Azure OpenAI model deployments verified/created (gpt-4o, gpt-4o-mini, o1-mini)
2. `GenericAnalysisHandler` model resolution chain (node → ModelSelector → default)
3. Model selection guidelines documented per operation type
4. Model selection dropdown in Playbook Builder action node config

**Key Files**:
- `Services/Ai/Handlers/GenericAnalysisHandler.cs` (fix hardcoded model)
- `Services/Ai/ModelSelector.cs` (verify routing)
- PlaybookBuilder code page (model dropdown)

### Phase 5: Embedding Model Governance (3h)

**Objective**: Document strategy, clean up legacy fields, establish change protocol.

**Deliverables**:
1. Embedding strategy documented
2. Legacy 1536-dim `contentVector` write stopped in `RagIndexingPipeline`
3. Embedding model change protocol documented

**Key Files**:
- `Services/Ai/RagIndexingPipeline.cs` (remove legacy writes)
- Documentation files

---

## 4. Parallel Execution Strategy

Tasks are designed for maximum parallelism using Claude Code task agents:

| Group | Tasks | Prerequisite | Notes |
|-------|-------|--------------|-------|
| **A** (Phase 1) | 001, 002, 003 | None | Index creation, deprecated removal, records sync — independent Azure operations |
| **B** (Phase 1) | 004, 005 | None | Discovery validation, invoice validation — independent of Group A |
| **C** (Phase 2) | 010, 011 | None | Dataverse deployment + service scaffolding — can start while Phase 1 runs |
| **D** (Phase 2) | 012, 013 | 010 + 011 | Index sources + wire retrieval — needs Dataverse records and service |
| **E** (Phase 3) | 020, 021 | 013 | L1 retrieval + config — depends on reference retrieval service |
| **F** (Phase 3) | 022, 023, 024 | 020 | L2, L3, caching — independent extensions after L1 is wired |
| **G** (Phase 4) | 030, 031 | None | Model deployment verification + handler fix — independent of Phase 3 |
| **H** (Phase 4) | 032, 033 | 031 | Guidelines doc + UI dropdown — after handler fix |
| **I** (Phase 5) | 040, 041, 042 | None | Documentation + cleanup — independent, can run anytime |

**Critical Path**: 001 → 010/011 → 012/013 → 020 → 022/023/024

---

## 5. Risk Assessment

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| Azure AI Search SKU doesn't support additional index | Low | High | Verify SKU limits before creating index |
| Azure OpenAI models not deployed / insufficient quota | Medium | High | Verify deployments early in Phase 4 |
| Reference index retrieval adds too much latency | Low | Medium | Index is tiny (~100 chunks); benchmark early |
| Existing playbooks break with knowledge injection | Medium | Medium | Phase 3 uses `auto` mode — only injects when knowledge sources linked |

---

## 6. Dependencies

| Item | Required By | Current Status |
|------|-------------|---------------|
| Azure OpenAI model deployments | Phase 4 | Needs verification |
| Dataverse test records (Matters, Projects) | Phase 1C | Should exist |
| KNW-001–010 content | Phase 2A | Authored, seed script exists |
| Azure AI Search capacity | Phase 1A | Needs SKU verification |

---

## 7. Success Metrics

| Metric | Target | Verification |
|--------|--------|-------------|
| Reference index created | ~100 chunks indexed | Azure AI Search document count |
| Deprecated indexes removed | 3 removed | Index list query |
| Knowledge retrieval latency | <500ms p95 | Benchmark during Phase 3 |
| Model selection working | Different models per operation type | Test classification vs reasoning tasks |
| Analysis quality | Measurable improvement with knowledge | Side-by-side comparison |

---

*Plan generated from spec.md. Execute tasks via `task-execute` skill.*
