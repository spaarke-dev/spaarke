# Project Plan: AI Platform Foundation — Phase 1

> **Last Updated**: 2026-02-22
> **Status**: Ready for Tasks
> **Spec**: [spec.md](spec.md)

---

## 1. Executive Summary

**Purpose**: Build the AI platform foundation for the June 2026 product launch — filling four critical gaps (seed data, chunking, scalable chat, evaluation) across four parallel workstreams.

**Scope**:
- Workstream A: Retrieval Foundation (LlamaParse dual-parser, SemanticDocumentChunker, RagIndexingPipeline, KnowledgeBaseEndpoints, two-index architecture)
- Workstream B: Scope Library + ScopeConfigEditorPCF (8 Actions, 10 Skills, 10 Knowledge Sources, 8 Tools, 10 Playbooks)
- Workstream C: SprkChat + Agent Framework (IChatClient, SprkChatAgent, chat tools, ChatEndpoints SSE, SprkChat UI component, AnalysisWorkspace integration)
- Workstream D: End-to-End Validation (test corpus, E2E tests, evaluation harness, quality baseline)

**Timeline**: ~8 weeks | **Estimated Effort**: ~200 hours across all workstreams

---

## 2. Architecture Context

### Design Constraints

**From ADRs** (must comply):
- **ADR-001**: No Azure Functions — use Minimal API + BackgroundService + Service Bus for async indexing
- **ADR-002**: No AI processing in Dataverse plugins — seed data is records only
- **ADR-004**: Idempotent job handlers with deterministic IdempotencyKey; emit JobOutcome
- **ADR-006**: New UI as PCF controls — no legacy JS webresources
- **ADR-007**: All document access via SpeFileStore — no Graph SDK leakage
- **ADR-008**: Endpoint filters for authorization on all new endpoints
- **ADR-009**: IDistributedCache (Redis) for cross-request caching; version cache keys; short TTL for security
- **ADR-010**: Concrete types unless genuine seam required; <= 15 non-framework DI registrations
- **ADR-012**: SprkChat shared component in @spaarke/ui-components; export types; 90%+ test coverage
- **ADR-013**: Extend BFF, not separate AI service; rate limit AI endpoints; no Azure Functions
- **ADR-014**: Tenant-scoped cache keys; version by model + content; centralized key builder
- **ADR-021**: Fluent UI v9 only; design tokens; dark mode required; WCAG 2.1 AA
- **ADR-022**: React 16 APIs only (ReactDOM.render, not createRoot); platform-library manifest; unmanaged solutions

**From Spec**:
- ScopeConfigEditorPCF bundle MUST be < 1MB — use CodeMirror (not Monaco)
- SprkChat shared component bundle contribution MUST be < 500KB
- RagIndexingPipeline MUST complete within 60 seconds of analysis completion
- Chat first-token latency MUST be < 2 seconds p95
- DI registrations count MUST remain <= 15 non-framework lines (ADR-010)

### Key Technical Decisions

| Decision | Rationale | Impact |
|----------|-----------|--------|
| Microsoft.Extensions.AI + Microsoft.Agents.AI for SprkChat | Agent Framework RC available Feb 19, 2026; stable API surface | C1–C3 implementation approach |
| LlamaParse as dual-parser alongside Azure Doc Intel | Complex legal docs (>30 pages, tables, scanned) need better parsing | A3 adds DocumentParserRouter with fallback |
| Two separate AI Search indexes | Knowledge index (512-token, curated) vs discovery (1024-token, auto) — different query patterns | A6 index configuration |
| Redis hot cache for chat + Dataverse for persistence | 24h idle expiry; audit trail in Dataverse; cross-server session support | C2 ChatSessionManager design |
| CodeMirror for scope JSON editing (not Monaco) | Monaco adds ~4MB; CodeMirror adds ~300KB — fits within PCF 1MB limit | B6 ScopeConfigEditorPCF |

### Discovered Resources

**Applicable Skills** (auto-discovered):
- `.claude/skills/dataverse-deploy/` — Deploy Dataverse solutions, PCF controls, seed data
- `.claude/skills/pcf-deploy/` — Build, pack, and deploy PCF controls via solution ZIP import
- `.claude/skills/azure-deploy/` — Deploy BFF API to Azure App Service
- `.claude/skills/adr-aware/` — Always-apply: load applicable ADRs before implementation
- `.claude/skills/script-aware/` — Always-apply: discover scripts before writing new automation

**Knowledge Guides**:
- `docs/guides/SPAARKE-AI-ARCHITECTURE.md` — Full AI architecture reference (all workstreams)
- `docs/guides/SPAARKE-AI-STRATEGY-AND-ROADMAP.md` — Product strategy and roadmap context
- `docs/guides/RAG-ARCHITECTURE.md` — RAG implementation patterns and design
- `docs/guides/RAG-CONFIGURATION.md` — RAG configuration and tuning guide
- `docs/guides/RAG-TROUBLESHOOTING.md` — Common RAG issues and solutions
- `docs/guides/HOW-TO-CREATE-AI-PLAYBOOK-SCOPES.md` — Scope entity creation guide
- `docs/guides/PCF-DEPLOYMENT-GUIDE.md` — PCF build, pack, and deploy procedures
- `docs/guides/AI-DEPLOYMENT-GUIDE.md` — Azure AI services deployment guide

**ADR References**:
- `.claude/adr/ADR-001-minimal-api.md` — BackgroundService pattern
- `.claude/adr/ADR-004-job-contract.md` — Idempotent job handlers
- `.claude/adr/ADR-009-redis-caching.md` — Redis-first caching
- `.claude/adr/ADR-013-ai-architecture.md` — AI in BFF, not separate service
- `.claude/adr/ADR-014-ai-caching.md` — Tenant-scoped cache keys
- `.claude/adr/ADR-021-fluent-design-system.md` — Fluent v9 + dark mode
- `.claude/adr/ADR-022-pcf-platform-libraries.md` — React 16 + platform-library manifest

**Reusable Code (Canonical Patterns)**:
- `src/server/api/Sprk.Bff.Api/Api/Ai/AnalysisEndpoints.cs` — SSE streaming endpoint pattern
- `src/server/api/Sprk.Bff.Api/Services/Ai/AnalysisOrchestrationService.cs` — AI orchestration (to modify)
- `src/server/api/Sprk.Bff.Api/Services/Ai/RagService.cs` — Existing RAG service (to extend)
- `src/server/api/Sprk.Bff.Api/Services/Ai/TextChunkingService.cs` — Existing chunking (replace with SemanticDocumentChunker)
- `src/client/pcf/AnalysisWorkspace/` — PCF to modify (integrate SprkChat)
- `src/client/shared/Spaarke.UI.Components/` — Shared component library (add SprkChat)

**Applicable Scripts**:
- `scripts/Deploy-BffApi.ps1` — BFF API deployment to Azure
- `scripts/Deploy-PCFWebResources.ps1` — PCF solution deployment
- `scripts/Create-BuilderScopes.ps1` — Scope record creation in Dataverse
- `scripts/Test-RagDedicatedModel.ps1` / `Test-RagSharedModel.ps1` — RAG testing
- `scripts/Test-SdapBffApi.ps1` — API endpoint testing
- `scripts/Setup-TestDocumentStorage.ps1` — Test document corpus setup

---

## 3. Implementation Approach

### Phase Structure

```
Phase 1: Foundation & Environment (Week 1)
└─ NuGet packages, Dataverse chat entities, DI wiring, two-index config

Phase 2: Workstream A — Retrieval Foundation (Week 2-3)
└─ RagQueryBuilder, SemanticDocumentChunker, DocumentParserRouter+LlamaParseClient
└─ RagIndexingPipeline + job handler, KnowledgeBaseEndpoints, retrieval logging
└─ Deploy: API to Azure

Phase 3: Workstream B — Scope Library & Seed Data (Week 2-4, parallel with A)
└─ 8 Actions, 10 Skills, 10 Knowledge Sources + indexing, 8 Tools, 10 Playbooks
└─ ScopeConfigEditorPCF (CodeMirror-based adaptive editor)
└─ Deploy: Dataverse records + PCF solution

Phase 4: Workstream C — SprkChat & Agent Framework (Week 3-5, parallel with B)
└─ IChatClient DI, SprkChatAgent + factory + session management
└─ Chat tools (4 tool groups), ChatEndpoints SSE, SprkChat React component
└─ AnalysisWorkspace PCF integration, agent middleware
└─ Deploy: API + PCF

Phase 5: Workstream D — End-to-End Validation (Week 6-8, requires A+B+C)
└─ Test corpus (10+ docs), E2E tests (10 playbooks), evaluation harness
└─ Quality baseline, negative tests, SprkChat evaluation
└─ Deploy: EvalRunner CLI tool
```

### Critical Path

**Blocking Dependencies:**
- Phase 2 (A) and Phase 3 (B) can run in **parallel** — different files, different ownership
- Phase 4 (C) can begin parallel with Phase 3 (B) — C1/C2 don't block B
- Phase 5 (D) BLOCKED BY: A complete (retrieval data) + B complete (playbooks) + C complete (working chat)
- Task 001 (Dataverse chat entity schema) BLOCKS C2 (ChatSessionManager)
- Task 001 (DI foundation) BLOCKS all API tasks in A and C

**High-Risk Items:**
- `sprk_aichatmessage` / `sprk_aichatsummary` entity schema — unresolved per spec; tackle in task 001
- Agent Framework RC stability — pin version; monitor for GA (expected end Q1 2026)
- DI registration count — audit after Phase 2 and Phase 4 to stay <= 15
- LlamaParse API availability — build fallback first; LlamaParse enhancement second

**Parallel Execution Groups:**
- Group A+B: Tasks 010-019 (Workstream A) and 030-039 (Workstream B) — fully parallel
- Group A+C: Tasks 010-019 (A) can overlap with 050-054 (C1/C2 foundation)
- Group D: All D tasks (070-089) — require A+B+C complete; D1-D3 can be parallel with each other

---

## 4. Phase Breakdown

### Phase 1: Foundation & Environment (Week 1)

**Objectives:**
1. Add Agent Framework NuGet packages and verify resolution
2. Define and create Dataverse entities for chat persistence
3. Configure two-index Azure AI Search architecture
4. Wire foundational DI registrations

**Deliverables:**
- [ ] Agent Framework NuGet packages added (`Microsoft.Extensions.AI`, `Microsoft.Agents.AI`)
- [ ] `sprk_aichatmessage` and `sprk_aichatsummary` entities defined and created in Dataverse
- [ ] `sprk_aievaluationrun` and `sprk_aievaluationresult` entities defined and created
- [ ] Two-index configuration in `appsettings.json` (knowledge index + discovery index names)
- [ ] LlamaParse API key referenced via configuration/Key Vault

**Critical Tasks:**
- Task 001: Define Dataverse chat entity schema — MUST BE FIRST (blocks C2)
- Task 002: Add Agent Framework packages and verify build passes

**Inputs**: spec.md, existing `appsettings.json`, Key Vault config patterns

**Outputs**: Updated `Sprk.Bff.Api.csproj`, new Dataverse entities, updated `appsettings.json`

---

### Phase 2: Workstream A — Retrieval Foundation (Week 2-3)

**Objectives:**
1. Replace `NoOpQueryPreprocessor` with metadata-aware `RagQueryBuilder`
2. Implement clause-aware `SemanticDocumentChunker` using Document Intelligence Layout
3. Build `DocumentParserRouter` + `LlamaParseClient` with fallback
4. Implement `RagIndexingPipeline` + `RagIndexingJobHandler` via Service Bus
5. Build `KnowledgeBaseEndpoints` (CRUD + document management + test-search + health)
6. Configure two-index architecture with tenant isolation
7. Add retrieval instrumentation for evaluation harness

**Deliverables:**
- [ ] `RagQueryBuilder.cs` — builds RAG queries from DocumentAnalysisResult metadata
- [ ] `SemanticDocumentChunker.cs` — clause-aware chunking with configurable size/overlap
- [ ] `DocumentParserRouter.cs` + `LlamaParseClient.cs` — dual-parser with fallback
- [ ] `RagIndexingPipeline.cs` + `RagIndexingJobHandler.cs` — auto-index after analysis
- [ ] `KnowledgeBaseEndpoints.cs` — CRUD, document management, test-search, health
- [ ] Two-index AI Search configuration — knowledge (512-token) + discovery (1024-token)
- [ ] Retrieval logging for evaluation (Recall@K, nDCG@K instrumentation)
- [ ] API deployment: updated BFF deployed to Azure

**Critical Tasks:**
- Task 010: RagQueryBuilder — BLOCKS all retrieval quality improvements
- Task 014: RagIndexingPipeline — BLOCKS task 015 (KnowledgeBase endpoints)

**Inputs**: `AnalysisOrchestrationService.cs`, `RagService.cs`, `DocumentIntelligenceService.cs`, ADR-001, ADR-004, ADR-007, ADR-009, ADR-013, ADR-014

**Outputs**: 8 new `.cs` files in `Services/Ai/`, modified `Program.cs`, `KnowledgeBaseEndpoints.cs`

---

### Phase 3: Workstream B — Scope Library & Seed Data (Week 2-4, parallel with A)

**Objectives:**
1. Create 8 system Actions (ACT-001–008) with production system prompts
2. Create 10 system Skills (SKL-001–010) with prompt fragments
3. Create 10 system Knowledge Sources (KNW-001–010) with content + embeddings + indexed
4. Create 8+ system Tools (TL-001–008) with handler class names + JSON Schema configs
5. Create 10 pre-built Playbooks (PB-001–010) with canvas JSON + scope references
6. Build and deploy `ScopeConfigEditorPCF` — adaptive editor for all 4 entity forms
7. Verify handler discovery API completeness

**Deliverables:**
- [ ] 8 `sprk_systemprompt` records (ACT-001–008) in Dataverse dev environment
- [ ] 10 `sprk_promptfragment` records (SKL-001–010) in Dataverse dev environment
- [ ] 10 `sprk_content` records (KNW-001–010) with content, embeddings indexed to knowledge index
- [ ] 8+ `sprk_analysistool` records (TL-001–008) with handler class names + schemas
- [ ] 10 `sprk_aiplaybook` records (PB-001–010) with canvas JSON
- [ ] `ScopeConfigEditorPCF` — adaptive control with handler dropdown + JSON editor + markdown editor
- [ ] PCF deployment to all 4 scope entity forms

**Critical Tasks:**
- Task 030-034 (seed data): Can run in parallel with each other (different Dataverse entities)
- Task 035 (ScopeConfigEditorPCF): New PCF control — 8-12 hour task

**Inputs**: `HOW-TO-CREATE-AI-PLAYBOOK-SCOPES.md`, `ScopeResolverService.cs`, existing scope entity schemas, ADR-006, ADR-021, ADR-022

**Outputs**: Dataverse records (all 5 entity types), new `src/client/pcf/ScopeConfigEditor/` directory

---

### Phase 4: Workstream C — SprkChat & Agent Framework (Week 3-5, parallel with B)

**Objectives:**
1. Integrate Agent Framework (`IChatClient` DI with Azure OpenAI provider)
2. Build `SprkChatAgent` + `SprkChatAgentFactory` + `IChatContextProvider` + `ChatSessionManager` + `ChatHistoryManager`
3. Implement chat tools: `DocumentSearchTools`, `AnalysisQueryTools`, `KnowledgeRetrievalTools`, `TextRefinementTools`
4. Build `ChatEndpoints` — `/api/ai/chat/sessions/*` with SSE streaming
5. Build `SprkChat` React shared component in `@spaarke/ui-components`
6. Integrate SprkChat into `AnalysisWorkspace` PCF (replace current chat)
7. Build agent middleware: telemetry, cost control, content safety, audit

**Deliverables:**
- [ ] `Services/Ai/Chat/` directory with SprkChatAgent, factory, context providers, session/history managers
- [ ] 4 chat tool classes registered as `AIFunction` via `AIFunctionFactory.Create()`
- [ ] `ChatEndpoints.cs` — create session, send message (SSE), refine text (SSE), context switch, history, delete
- [ ] `SprkChat/` directory in `@spaarke/ui-components` with full React component
- [ ] `AnalysisWorkspace` PCF updated — SprkChat replaces current chat panel
- [ ] Agent middleware pipeline (telemetry, cost control, content safety, audit)
- [ ] API + PCF deployment

**Critical Tasks:**
- Task 050 (C1: Agent Framework integration) — BLOCKS all other C tasks
- Task 051 (C2: SprkChatAgent + session management) — BLOCKS C3, C4, C6
- Task 054 (C5: SprkChat React component) — BLOCKS C6 (AnalysisWorkspace integration)

**Inputs**: `AnalysisEndpoints.cs` (SSE pattern), `AnalysisOrchestrationService.cs`, `RagService.cs`, `AnalysisWorkspace` PCF source, `@spaarke/ui-components` structure, ADR-009, ADR-012, ADR-013, ADR-014, ADR-021, ADR-022

**Outputs**: `Services/Ai/Chat/` (6+ files), `ChatEndpoints.cs`, `SprkChat/` component, modified `AnalysisWorkspace` PCF, modified `Program.cs`

---

### Phase 5: Workstream D — End-to-End Validation (Week 6-8)

**Objectives:**
1. Create test document corpus (10+ documents covering all playbook types)
2. Build automated E2E tests for all 10 playbooks
3. Build evaluation harness with gold dataset, Recall@K, nDCG@K scoring
4. Record quality baseline for all 10 playbooks against test corpus
5. Run negative test suite (missing skills, empty knowledge, handler timeouts, malformed docs)
6. Run SprkChat evaluation (answer accuracy, citation rate, latency)

**Deliverables:**
- [ ] Test document corpus in SPE test container (NDA, contract, lease, invoice, SLA, etc.)
- [ ] 10 E2E playbook tests — upload → analyze → verify output + citations
- [ ] Evaluation harness CLI (`tools/EvalRunner/`) + `EvaluationEndpoints.cs`
- [ ] Gold dataset (manually curated, not from production)
- [ ] Quality baseline report — all 10 playbooks, reproducible scores
- [ ] Negative test suite results
- [ ] SprkChat evaluation report (accuracy, citation rate, latency)

**Prerequisites**: Phase 2 complete (retrieval data) + Phase 3 complete (playbooks) + Phase 4 complete (SprkChat)

**Inputs**: All deployed services from A+B+C, `Setup-TestDocumentStorage.ps1`, ADR-013

**Outputs**: `tools/EvalRunner/`, `EvaluationEndpoints.cs`, `tests/integration/` E2E tests, evaluation reports

---

### Phase 6: Project Wrap-up (Final)

**Deliverables:**
- [ ] README.md status updated to Complete
- [ ] Lessons learned documented
- [ ] Project artifacts archived
- [ ] `/repo-cleanup` run

---

## 5. Dependencies

### External Dependencies

| Dependency | Status | Risk | Mitigation |
|------------|--------|------|------------|
| Microsoft Agent Framework RC NuGet | ✅ Available (Feb 19, 2026) | Low — API stable | Pin version; plan GA upgrade |
| LlamaParse API account + key | 🔲 Need provisioning | Medium | Build fallback first; add LlamaParse second |
| Azure OpenAI (gpt-4o-mini + text-embedding-3-small) | ✅ Production | Low | Already configured |
| Azure AI Search S1 (two indexes) | ✅ Ready | Low | S1 tier has capacity |
| Azure Document Intelligence (Layout model) | 🔲 Needs upgrade from Read | Medium | Upgrade in task 011 (A2) |

### Internal Dependencies

| Dependency | Location | Status |
|------------|----------|--------|
| ScopeResolverService | `Services/Ai/ScopeResolverService.cs` | ✅ Production |
| PlaybookExecutionEngine | `Services/Ai/PlaybookExecutionEngine.cs` | ✅ Production |
| RagService (existing) | `Services/Ai/RagService.cs` | ✅ Production (to extend) |
| AnalysisOrchestrationService | `Services/Ai/AnalysisOrchestrationService.cs` | ✅ Production (to modify) |
| @spaarke/ui-components | `src/client/shared/Spaarke.UI.Components/` | ✅ Production |
| Dataverse scope entities | sprk_analysistool, sprk_promptfragment, sprk_systemprompt, sprk_content, sprk_aiplaybook | ✅ Exist |

---

## 6. Testing Strategy

**Unit Tests** (80%+ coverage target for new services):
- `RagQueryBuilder` — query construction from analysis metadata
- `SemanticDocumentChunker` — boundary detection, chunk size, overlap
- `DocumentParserRouter` — routing logic, fallback behavior
- `RagIndexingJobHandler` — idempotency, outcome emission
- `SprkChatAgent` — tool invocation, context management, session handling
- `ChatSessionManager` — session lifecycle, summarization trigger
- SprkChat React component — render, context switching, predefined prompts

**Integration Tests**:
- RagIndexingPipeline: analysis → Service Bus job → indexed document (< 60s)
- KnowledgeBaseEndpoints: CRUD operations, search returns results
- ChatEndpoints: create session, SSE streaming message, tool use
- AnalysisWorkspace PCF: SprkChat renders, context toggle, highlight-and-refine

**E2E Tests** (Phase 5):
- All 10 playbooks: upload → analyze → verify output + citations
- Evaluation harness: Recall@10 >= 0.7

---

## 7. Acceptance Criteria

### Phase 2 (Workstream A) Acceptance:
- [ ] `RagQueryBuilder` uses analysis metadata (not first-500-chars) — Recall@10 improves vs baseline
- [ ] Chunks respect section boundaries; configurable 1500 tokens, 200 overlap
- [ ] Parser router selects correctly; system works when LlamaParse unavailable
- [ ] New analysis triggers indexing job; document searchable within 60 seconds
- [ ] All KB CRUD operations work; health endpoint returns doc count + last-updated

### Phase 3 (Workstream B) Acceptance:
- [ ] All 8 ACT-* records exist; ScopeResolverService resolves them
- [ ] All 10 SKL-* records exist; prompt fragments inject into analysis pipeline
- [ ] All 10 KNW-* records exist with content; RAG retrieval returns relevant chunks
- [ ] All TL-* records exist; handler class names resolve to registered handlers
- [ ] All 10 PB-* playbooks selectable in AnalysisWorkspace; each executes against test document
- [ ] ScopeConfigEditorPCF auto-detects entity type; validates JSON; dark mode works

### Phase 4 (Workstream C) Acceptance:
- [ ] `IChatClient` resolves from DI; agent creates via `chatClient.AsAIAgent()`
- [ ] Sessions persist across server restarts; history resumption works; summarizes after 15 messages
- [ ] Agent autonomously calls tools when relevant; retrieval returns chunks from RAG
- [ ] SSE streaming works for message + refinement endpoints
- [ ] SprkChat component renders; context toggle, predefined prompts, highlight-and-refine all work
- [ ] AnalysisWorkspace uses SprkChat; old `/api/ai/analysis/{id}/continue` deprecated

### Phase 5 (Workstream D) Acceptance:
- [ ] All 10 playbooks pass E2E test; citations reference actual document sections
- [ ] Evaluation harness Recall@10 >= 0.7 for knowledge index
- [ ] Quality baseline report generated with reproducible scores for all 10 playbooks

---

## 8. Risk Register

| ID | Risk | Probability | Impact | Mitigation |
|----|------|------------|---------|------------|
| R1 | Dataverse chat entity schema causes C2 rework | Medium | High | Define schema completely in task 001 before any C2 work |
| R2 | Agent Framework RC has breaking changes | Low | High | Pin exact RC version; create upgrade task in backlog |
| R3 | DI count exceeds 15 after all workstreams | Medium | Medium | Feature module pattern; audit after Phase 2 and Phase 4 |
| R4 | LlamaParse API not provisioned on time | Medium | Medium | Build fallback path first; LlamaParse is an enhancement |
| R5 | SprkChat bundle > 500KB | Low | Low | Code splitting; lazy load non-critical paths |
| R6 | Azure Doc Intelligence Layout model upgrade breaks existing parsing | Medium | Medium | Test existing flows before deploying; rollback plan |
| R7 | Knowledge source content quality requires multiple iterations | High | Medium | Start content authoring early in Phase 3; plan review cycle |

---

## 9. Next Steps

1. **Run** `/task-create ai-spaarke-platform-enhancements-r1` to generate task files
2. **Begin** Phase 1 — Task 001: Define Dataverse chat entity schema
3. **Parallel start** Phase 2 (Workstream A) and Phase 3 (Workstream B) after Phase 1 completes

---

**Status**: Ready for Tasks
**Next Action**: Generate task files → start task 001

---

*For Claude Code: This plan provides implementation context. Load relevant sections when executing tasks.*
