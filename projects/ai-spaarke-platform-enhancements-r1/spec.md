# AI Platform Foundation (Phase 1) — AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-02-22
> **Source**: `design.md` (v1.2), consolidated from `sk-analysis-chat-design.md`, `AI-Platform-Strategy-and-Architecture.md`, `RAG-ARCHITECTURE-DESIGN.md`, `ai-playbook-scope-editor-PCF/design.md`, `enhancements.md`
> **Architecture Reference**: `docs/architecture/AI-ARCHITECTURE.md` (v3.0)
> **Strategy Reference**: `docs/guides/SPAARKE-AI-STRATEGY-AND-ROADMAP.md` (v1.0)

---

## Executive Summary

Phase 1 delivers the AI platform foundation required for June 2026 product launch. The platform has substantial working infrastructure (Azure OpenAI, AI Search, PlaybookExecutionEngine, 10+ tool handlers, RagService, AnalysisWorkspace PCF, completed scope resolution + semantic search) but critical gaps: zero seed data, no clause-aware chunking, hand-rolled chat that doesn't scale, no evaluation harness, and no scope management UX.

Four workstreams address these gaps: (A) build retrieval foundation with LlamaParse dual-parser, (B) seed the Scope Library with production-quality content and scope editor PCF, (C) replace the hand-rolled chat with Agent Framework-powered SprkChat, and (D) validate everything end-to-end with an evaluation harness.

---

## Scope

### In Scope

**Workstream A: Retrieval Foundation + LlamaParse**
- A1: Replace `NoOpQueryPreprocessor` with `RagQueryBuilder` (Summary + Entities query strategy)
- A2: `IDocumentChunker` + `SemanticDocumentChunker` (clause-aware, 1500 tokens, 200 overlap)
- A3: `DocumentParserRouter` + `LlamaParseClient` (dual-parser: Azure Doc Intel + LlamaParse)
- A4: `RagIndexingPipeline` + `RagIndexingJobHandler` (auto-index after analysis via Service Bus)
- A5: `KnowledgeBaseEndpoints` (CRUD + document management + test-search + health)
- A6: Two-index architecture (knowledge index 512 tokens + discovery index 1024 tokens)
- A7: Retrieval instrumentation (logging for evaluation harness)

**Workstream B: Scope Library & Seed Data**
- B1: 8 system Actions (ACT-001–008) with system prompts in Dataverse
- B2: 10 system Skills (SKL-001–010) with prompt fragments in Dataverse
- B3: 10 system Knowledge Sources (KNW-001–010) with reference content, embeddings, indexed
- B4: 8+ system Tools (TL-001–008) with handler class names and JSON Schema configs
- B5: 10 pre-built Playbooks (PB-001–010) with canvas JSON in Dataverse
- B6: `ScopeConfigEditorPCF` — adaptive control: handler dropdown + JSON editor + markdown editor
- B7: Verify/enhance handler discovery API (already built in scope-resolution project)

**Workstream C: SprkChat & Agent Framework**
- C1: Agent Framework integration — `Microsoft.Extensions.AI` + `Microsoft.Agents.AI` NuGet packages, `IChatClient` DI registration
- C2: `SprkChatAgent` + `SprkChatAgentFactory` + `IChatContextProvider` interface + `ChatSessionManager` + `ChatHistoryManager`
- C3: Chat tools — `DocumentSearchTools`, `AnalysisQueryTools`, `KnowledgeRetrievalTools`, `TextRefinementTools`
- C4: `ChatEndpoints` — unified `/api/ai/chat/sessions/*` endpoints (SSE streaming)
- C5: `SprkChat` React shared component in `@spaarke/ui-components`
- C6: Integration into AnalysisWorkspace PCF — replace current chat with SprkChat
- C7: Agent middleware — telemetry, cost control, content safety, audit

**Workstream D: End-to-End Validation**
- D1: Test document corpus (10+ documents: NDA, contract, lease, invoice, SLA, etc.)
- D2: End-to-end playbook tests (upload → analyze → verify output + citations)
- D3: Evaluation harness (gold dataset, Recall@K, nDCG@K, output validation, citation accuracy)
- D4: Quality baseline (all 10 playbooks against test corpus)
- D5: Negative testing (missing skills, empty knowledge, handler timeouts, malformed docs)
- D6: SprkChat evaluation (answer accuracy, citation rate, latency)

### Out of Scope

- SprkChat on Workspace, Document Studio, Word Add-in, Matter Page surfaces (Phase 2)
- Deviation detection / ENH-005 (Phase 2 — needs knowledge pack maturity)
- Ambiguity detection / ENH-006 (Phase 2 — new tool handler)
- Multi-user annotations / ENH-007 (Phase 4 — UX feature)
- Document version comparison / ENH-008 (Phase 2 — needs similarity infra)
- Prompt library management / ENH-003 (Phase 2 — use predefined prompts initially)
- Document relationship visuals / `ai-document-relationship-visuals` (Phase 2 — needs similarity graph)
- Find Similar Documents UI (Phase 2 — needs discovery index populated)
- Client-specific overlays (Phase 4 — needs base system proven)
- Multi-agent orchestration (Phase 3 — Phase 1 uses single-agent SprkChat)
- Word Add-in AI panel and redlining (Phase 2)
- AI Foundry Prompt Flow activation (Phase 5)

### Affected Areas

**BFF API** (`src/server/api/Sprk.Bff.Api/`):
- `Api/Ai/ChatEndpoints.cs` — New unified chat API (C4)
- `Api/Ai/KnowledgeBaseEndpoints.cs` — New KB management API (A5)
- `Api/Ai/EvaluationEndpoints.cs` — New evaluation API (D3)
- `Services/Ai/Chat/` — New directory: SprkChatAgent, tools, contexts, middleware, memory (C2–C3, C7)
- `Services/Ai/RagQueryBuilder.cs` — New smart query strategy (A1)
- `Services/Ai/SemanticDocumentChunker.cs` — New chunking service (A2)
- `Services/Ai/DocumentParserRouter.cs` — New dual-parser (A3)
- `Services/Ai/LlamaParseClient.cs` — New LlamaParse client (A3)
- `Services/Ai/RagIndexingPipeline.cs` — New indexing orchestrator (A4)
- `Services/Jobs/Handlers/RagIndexingJobHandler.cs` — New background indexing (A4)
- `Services/Ai/AnalysisOrchestrationService.cs` — Modified: replace first-500-chars query (A1), replace old chat path (C4)
- `Services/Ai/DocumentIntelligenceService.cs` — Modified: wire dual-parser + indexing pipeline (A3, A4)
- `Program.cs` — Modified: register new services and endpoints

**PCF Controls** (`src/client/pcf/`):
- `ScopeConfigEditor/` — New PCF control (B6)
- `AnalysisWorkspace/` — Modified: integrate SprkChat replacing current chat (C6)

**Shared Component Library** (`src/client/shared/Spaarke.UI.Components/`):
- `src/components/SprkChat/` — New shared component directory (C5)

**Dataverse**:
- `sprk_analysistool` records — 8+ system tools (B4)
- `sprk_promptfragment` records — 10 system skills (B2)
- `sprk_systemprompt` records — 8 system actions (B1)
- `sprk_content` records — 10 system knowledge sources (B3)
- `sprk_aiplaybook` records — 10 system playbooks (B5)
- `sprk_aichatmessage` entity — New: chat message persistence (C2)
- `sprk_aichatsummary` entity — New: conversation summaries (C2)
- `sprk_aievaluationrun` entity — New: evaluation run records (D3)
- `sprk_aievaluationresult` entity — New: evaluation result records (D3)

**CLI Tools**:
- `tools/EvalRunner/` — New evaluation CLI for CI/CD (D3)

---

## Requirements

### Functional Requirements

**Retrieval Foundation (Workstream A)**

1. **FR-A01**: `RagQueryBuilder` builds RAG search queries from `DocumentAnalysisResult` metadata (Summary, DocumentType, Entities, Keywords) instead of first 500 characters.
   - Acceptance: RAG queries use analysis metadata; Recall@10 improves vs. baseline.

2. **FR-A02**: `SemanticDocumentChunker` splits documents at section/paragraph boundaries detected by Document Intelligence Layout model.
   - Acceptance: Chunks respect section boundaries; no mid-sentence splits; configurable size (default 1500 tokens) and overlap (default 200 tokens).

3. **FR-A03**: `DocumentParserRouter` routes complex documents (contracts, leases, scanned, >30 pages, tables) to LlamaParse API; simple documents use Azure Doc Intelligence.
   - Acceptance: Router correctly selects parser based on metadata; system works when LlamaParse is unavailable (fallback to Doc Intelligence).

4. **FR-A04**: `RagIndexingPipeline` automatically chunks, embeds, and indexes documents after analysis completion via Service Bus job.
   - Acceptance: New analysis triggers indexing job; document appears in search within 60 seconds of analysis completion.

5. **FR-A05**: Knowledge Base CRUD API allows admin to create, read, update, delete knowledge bases; add/remove documents; test search quality; monitor health.
   - Acceptance: All CRUD operations work; documents added to KB are immediately searchable; health endpoint returns document count and last-updated timestamp.

6. **FR-A06**: Two-index architecture maintains separate knowledge index (512-token chunks, curated) and discovery index (1024-token chunks, auto-populated).
   - Acceptance: Knowledge queries hit knowledge index; discovery queries hit discovery index; tenant isolation enforced on both.

**Scope Library & Seed Data (Workstream B)**

7. **FR-B01**: 8 system Actions created in Dataverse with production-quality system prompts linked to appropriate tool handlers.
   - Acceptance: All 8 ACT-* records exist; ScopeResolverService resolves them; analysis uses correct system prompt.

8. **FR-B02**: 10 system Skills created in Dataverse with specialized prompt fragments.
   - Acceptance: All 10 SKL-* records exist; playbooks reference correct skills; prompt fragments inject into analysis pipeline.

9. **FR-B03**: 10 system Knowledge Sources created with reference content, embeddings generated, and content indexed to knowledge index.
   - Acceptance: All 10 KNW-* records exist with content; RAG retrieval returns relevant chunks during analysis.

10. **FR-B04**: 8+ system Tools created with handler class names and JSON Schema `ConfigurationSchema`.
    - Acceptance: All TL-* records exist; handler class names resolve to registered handlers; ConfigurationSchema validates tool configurations.

11. **FR-B05**: 10 pre-built Playbooks created with canvas JSON, node definitions, and scope references.
    - Acceptance: All 10 PB-* playbooks selectable in AnalysisWorkspace; each executes successfully against appropriate test document.

12. **FR-B06**: `ScopeConfigEditorPCF` control deployed to all 4 scope entity forms with adaptive editor behavior.
    - Acceptance: Control auto-detects entity type; shows handler dropdown for tools; shows markdown editor for skills/actions; validates JSON config against handler schema; dark mode works.

**SprkChat & Agent Framework (Workstream C)**

13. **FR-C01**: Agent Framework integrated into BFF API with `IChatClient` (Azure OpenAI provider) registered in DI.
    - Acceptance: `IChatClient` resolves from DI; agent creates via `chatClient.AsAIAgent()`; multi-provider configuration available (Azure OpenAI default).

14. **FR-C02**: SprkChat BFF service manages chat sessions with persistent storage (Dataverse + Redis) and conversation summarization.
    - Acceptance: Sessions persist across server restarts; history resumption works; conversation summarizes after 15 messages; 10 active messages kept in full.

15. **FR-C03**: Chat tools (`DocumentSearchTools`, `AnalysisQueryTools`, `KnowledgeRetrievalTools`, `TextRefinementTools`) registered as `AIFunction` via `AIFunctionFactory.Create()`.
    - Acceptance: Agent autonomously calls tools when relevant; document search returns chunks from RAG; knowledge retrieval uses knowledge index; text refinement produces alternative wording.

16. **FR-C04**: Unified chat API at `/api/ai/chat/sessions/*` with SSE streaming for messages and refinements.
    - Acceptance: Create session, send message (SSE stream), refine text (SSE stream), switch context, get suggestions, get history, delete session all work.

17. **FR-C05**: `SprkChat` React shared component in `@spaarke/ui-components` with context switching, predefined prompts, and highlight-and-refine.
    - Acceptance: Component renders in AnalysisWorkspace; context toggle (Document/Analysis/Hybrid) works; predefined prompt chips display and insert; highlight-and-refine toolbar appears on text selection.

18. **FR-C06**: SprkChat replaces current chat in AnalysisWorkspace PCF.
    - Acceptance: Old `/api/ai/analysis/{id}/continue` endpoint deprecated; AnalysisWorkspace uses SprkChat component; all current chat functionality preserved plus new capabilities.

**Validation (Workstream D)**

19. **FR-D01**: Test document corpus of 10+ documents covering all playbook types.
    - Acceptance: Documents exist in test SPE container; each document type maps to at least one playbook.

20. **FR-D02**: Automated end-to-end test for each playbook (upload → analyze → verify output structure → verify citations).
    - Acceptance: All 10 playbooks pass E2E test; structured output validated; citations reference actual document sections.

21. **FR-D03**: Evaluation harness with gold dataset, retrieval scoring (Recall@K, nDCG@K), and output validation scoring.
    - Acceptance: Harness runs via API and CLI; produces JSON + markdown report; scores are reproducible.

22. **FR-D04**: Quality baseline recorded for all 10 playbooks against test corpus.
    - Acceptance: Baseline report generated; scores stored in Dataverse for trend analysis.

### Non-Functional Requirements

- **NFR-01**: RAG query latency < 600ms p95 (embedding + search + reranking).
- **NFR-02**: Chat first-token latency < 2 seconds p95 (including tool calls).
- **NFR-03**: Indexing pipeline completes within 60 seconds of analysis completion.
- **NFR-04**: LlamaParse parsing completes within 30 seconds for 20-page document.
- **NFR-05**: ScopeConfigEditorPCF bundle size < 1MB (CodeMirror, not Monaco).
- **NFR-06**: SprkChat shared component bundle contribution < 500KB.
- **NFR-07**: Chat session Redis hot cache expires after 24 hours idle.
- **NFR-08**: All new API endpoints rate-limited (AI stream endpoints share rate limit pool).
- **NFR-09**: Tenant isolation enforced on all indexes, cache keys, and chat sessions.
- **NFR-10**: DI registration count remains <= 15 non-framework lines after all new services added.
- **NFR-11**: All new PCF controls support light, dark, and high-contrast themes.
- **NFR-12**: Chat history supports 50 messages per session before archiving.

---

## Technical Constraints

### Applicable ADRs

| ADR | Title | Workstreams | Key Constraint |
|-----|-------|-------------|---------------|
| ADR-001 | Minimal API + BackgroundService | A, C, D | No Azure Functions; use BackgroundService + Service Bus for async |
| ADR-002 | Thin Dataverse Plugins | B | No AI processing in plugins; seed data is records only |
| ADR-004 | Async Job Contract | A | Idempotent handlers; deterministic IdempotencyKey; emit JobOutcome |
| ADR-006 | PCF Over WebResources | B, C | New UI as PCF controls; no legacy JS webresources |
| ADR-007 | SpeFileStore Facade | A | All document access via SpeFileStore; no Graph SDK leakage |
| ADR-008 | Endpoint Filters | A, C, D | Endpoint filters for authorization on all new endpoints |
| ADR-009 | Redis-First Caching | A, C | IDistributedCache for cross-request; version cache keys; short TTL for security |
| ADR-010 | DI Minimalism | All | Concrete types; feature modules; <= 15 non-framework registrations |
| ADR-012 | Shared Component Library | B, C | SprkChat in @spaarke/ui-components; export types; 90%+ test coverage |
| ADR-013 | AI Architecture | All | Extend BFF, not separate service; rate limit AI endpoints; no Functions |
| ADR-014 | AI Caching | A, C | Tenant-scoped keys; version by model + content; centralized key builder |
| ADR-021 | Fluent UI v9 | B, C | Fluent v9 only; design tokens; dark mode required; WCAG 2.1 AA |
| ADR-022 | PCF Platform Libraries | B, C | React 16 APIs only; platform-library manifest; unmanaged solutions |

### MUST Rules

**API / BFF**:
- MUST use Minimal API for all new endpoints (ADR-001)
- MUST use endpoint filters for authorization on chat, KB, evaluation endpoints (ADR-008)
- MUST use BackgroundService + Service Bus for indexing pipeline (ADR-001)
- MUST implement `RagIndexingJobHandler` as idempotent with deterministic IdempotencyKey (ADR-004)
- MUST route all document access through SpeFileStore (ADR-007)
- MUST use `IDistributedCache` (Redis) for chat sessions, embeddings, handler metadata (ADR-009)
- MUST scope all cache keys by tenant (ADR-014)
- MUST apply rate limiting to all AI endpoints (ADR-013)
- MUST register concrete types unless genuine seam required (ADR-010)
- MUST keep DI registrations <= 15 non-framework lines (ADR-010)
- MUST return ProblemDetails for all API errors (ADR-001)

**PCF / Frontend**:
- MUST use Fluent UI v9 (`@fluentui/react-components`) exclusively (ADR-021)
- MUST use React 16 APIs (`ReactDOM.render()`, not `createRoot`) (ADR-022)
- MUST declare `platform-library` for React 16.14.0 and Fluent 9.46.2 (ADR-022)
- MUST place SprkChat in `@spaarke/ui-components` (ADR-012)
- MUST support light, dark, and high-contrast themes (ADR-021)
- MUST use `makeStyles` (Griffel) for custom styling (ADR-021)
- MUST keep ScopeConfigEditorPCF bundle < 1MB (ADR-022, use CodeMirror not Monaco)
- MUST deploy unmanaged solutions (ADR-022)

**AI / Agent Framework**:
- MUST NOT create separate AI microservice (ADR-013)
- MUST NOT call Azure AI services directly from PCF (ADR-013)
- MUST NOT use Azure Functions for AI processing (ADR-013)
- MUST NOT expose API keys to clients (ADR-013)
- MUST NOT cache streaming tokens (ADR-014)
- MUST NOT process AI in Dataverse plugins (ADR-002)

### Existing Patterns to Follow

- See `src/server/api/Sprk.Bff.Api/Api/Ai/AnalysisEndpoints.cs` for SSE streaming endpoint pattern
- See `src/server/api/Sprk.Bff.Api/Services/Ai/AnalysisOrchestrationService.cs` for existing AI orchestration
- See `src/server/api/Sprk.Bff.Api/Services/Ai/RagService.cs` for RAG search implementation
- See `src/client/pcf/DocumentRelationshipViewer/` for PCF with MSAL auth, theming, Fluent v9
- See `src/client/shared/Spaarke.UI.Components/` for shared component library patterns
- See `.claude/patterns/api/` for BFF endpoint patterns
- See `.claude/patterns/pcf/` for PCF control patterns

---

## Success Criteria

1. [ ] 10 pre-built playbooks ship with product — Verify: playbooks selectable in AnalysisWorkspace, each executes against test document
2. [ ] 8 Actions, 10 Skills, 10 Knowledge sources, 8 Tools seeded — Verify: Dataverse records exist, ScopeResolverService resolves all
3. [ ] Clause-aware chunking deployed with LlamaParse option — Verify: chunks respect boundaries, parser router selects correctly
4. [ ] Knowledge base management operational — Verify: admin CRUD works, documents indexed, search returns results
5. [ ] Hybrid retrieval measurably improves over baseline — Verify: evaluation harness shows Recall@10 >= 0.7
6. [ ] End-to-end playbook workflow passes for all 10 playbooks — Verify: automated test suite green
7. [ ] Scope editor PCF deployed — Verify: admins configure scopes with validation on all 4 entity forms
8. [ ] SprkChat deployed in AnalysisWorkspace — Verify: context switching, tool use, predefined prompts, highlight-and-refine work
9. [ ] LlamaParse dual-parser evaluated — Verify: complex legal documents parse with >90% structural accuracy
10. [ ] Quality baseline established — Verify: evaluation report generated for all 10 playbooks with reproducible scores

---

## Dependencies

### Prerequisites

- `ai-scope-resolution-enhancements` — Complete (ScopeResolverService, handler discovery API, GenericAnalysisHandler all shipped)
- `ai-semantic-search-ui-r2` — Complete (SemanticSearchControl PCF with full UI, filters, infinite scroll, dark mode all shipped)
- Azure AI Search S1 tier has capacity for two indexes (knowledge + discovery)
- LlamaParse API account provisioned with API key in Key Vault
- Microsoft Agent Framework RC NuGet packages available (shipped Feb 19, 2026)
- Dataverse entities exist for all scope types (sprk_analysistool, sprk_promptfragment, sprk_systemprompt, sprk_content, sprk_aiplaybook)

### External Dependencies

- **LlamaParse API** — External service; requires API key; system works without it (fallback)
- **Microsoft Agent Framework** — RC available now; GA expected end Q1 2026; API surface is stable
- **Azure OpenAI** — Production; gpt-4o-mini for analysis, text-embedding-3-small for embeddings
- **Azure AI Search** — Production; standard + semantic ranking tier
- **Azure Document Intelligence** — Production; upgrade from Read → Layout model required
- **Dataverse** — Production; dev environment at spaarkedev1.crm.dynamics.com

### Workstream Dependencies

```
A (retrieval)  ──▶ Enables D3, D4 (evaluation needs retrieval data)
B (seed data)  ──▶ Enables D1, D2 (testing needs playbooks)
C (SprkChat)   ──▶ Enables D6 (chat evaluation needs working chat)
A + B + C      ──▶ D (validation is the launch gate)
```

---

## Owner Clarifications

| Topic | Question | Answer | Impact |
|-------|----------|--------|--------|
| Agent Framework timing | Must we wait for GA? | No — RC shipped Feb 19, 2026; available for development now. | SprkChat moves from Phase 3 to Phase 1 (Workstream C). |
| LlamaParse | Should we evaluate for document intelligence? | Yes — include as dual-parser option alongside Azure Doc Intelligence. | Added A3 (DocumentParserRouter + LlamaParseClient). |
| Existing development | How do we account for completed work? | Validate as part of project — don't rebuild. | Scope resolution and semantic search both completed independently. Current state documented. |

---

## Assumptions

*Proceeding with these assumptions (not explicitly specified in design):*

- **LlamaParse API key**: Stored in Azure Key Vault alongside other API keys. Accessed via `IConfiguration` / Options pattern.
- **Chat session cleanup**: 24-hour idle session expiry in Redis; Dataverse records persist indefinitely for audit.
- **Seed data content authoring**: Spaarke team writes production-quality prompt content for all system scopes. Content iterated with domain expert review.
- **Evaluation gold dataset**: Manually curated test documents and expected outputs. Not derived from production data.
- **Agent Framework version pinning**: Pin to RC version at project start; upgrade to GA when released (expected minimal breaking changes).
- **SprkChat initial surface**: AnalysisWorkspace only in Phase 1. Other surfaces (Workspace, Document Studio, Word Add-in) in Phase 2.
- **CodeMirror over Monaco**: For scope editor PCF JSON editing — lighter bundle fits within PCF 1MB limit.
- **Dataverse entities for chat**: New entities `sprk_aichatmessage` and `sprk_aichatsummary` need to be created (schema not yet defined).
- **Discovery index population**: Auto-indexing to discovery index is opt-in per container via settings; not enabled by default.

---

## Unresolved Questions

*These may need answers during implementation:*

- [ ] **Dataverse entity schema for chat messages**: Exact fields for `sprk_aichatmessage` and `sprk_aichatsummary` — Blocks: C2 (ChatSessionManager implementation)
- [ ] **LlamaParse tier selection logic**: Beyond document type, should users be able to force LlamaParse tier (Cost Effective vs Agentic vs Agentic Plus)? — Blocks: A3 configuration design
- [ ] **Knowledge source content format**: Should KNW-* seed data content be markdown, plain text, or structured JSON? — Blocks: B3 content authoring
- [ ] **Evaluation scoring thresholds**: What Recall@10 / nDCG@10 scores constitute "pass" vs "fail"? — Blocks: D3 harness configuration
- [ ] **Old chat endpoint deprecation**: Should `/api/ai/analysis/{id}/continue` be removed immediately or kept alongside new chat endpoints during transition? — Blocks: C4/C6 integration

---

*AI-optimized specification. Source documents: design.md (v1.2), sk-analysis-chat-design.md, AI-Platform-Strategy-and-Architecture.md, RAG-ARCHITECTURE-DESIGN.md, ai-playbook-scope-editor-PCF/design.md, enhancements.md*
