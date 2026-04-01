# SprkChat Platform Enhancement R2 — Implementation Plan

> **Spec**: [spec.md](spec.md)
> **Created**: 2026-03-17
> **Optimized for**: Parallel agent execution with concurrent Claude Code task agents

---

## Executive Summary

R2 transforms SprkChat from a functional AI companion into a copilot-quality experience. The implementation is organized into **6 phases with 5 parallel execution groups**, enabling concurrent Claude Code agents to work simultaneously on independent workstreams. The critical path runs through Phase 1 (foundation) → Phase 2/3 (parallel BFF + frontend) → Phase 4 (integration) → Phase 5 (testing) → Phase 6 (wrap-up).

**Estimated effort**: 80-120 hours across 45-55 tasks
**Parallel speedup**: Phases 2 and 3 execute concurrently; within phases, multiple task groups run in parallel

---

## Architecture Context

### ADRs (must comply)

| ADR | Key Constraint | Phases Affected |
|-----|----------------|-----------------|
| ADR-001 | Minimal API only, no Azure Functions | 2 |
| ADR-006 | Code Pages for standalone dialogs | 3 |
| ADR-008 | Endpoint filters for auth | 2 |
| ADR-009 | Redis-first caching (`IDistributedCache`) | 1, 2 |
| ADR-010 | ≤15 non-framework DI registrations | 2 |
| ADR-012 | Shared `@spaarke/ui-components` library | 3 |
| ADR-013 | Extend BFF, flow ChatHostContext, SpeFileStore | 2 |
| ADR-014 | No streaming token cache, version cache keys, tenant scope | 2 |
| ADR-015 | No document content logging, data minimization | 2 |
| ADR-016 | Rate limiting on all AI endpoints | 2 |
| ADR-021 | Fluent v9 tokens, dark mode required | 3 |
| ADR-022 | Code Pages: React 18/19 bundled createRoot | 3 |

### Discovered Resources

**Skills applicable**:
- `task-execute` — primary task execution workflow
- `jps-action-create` — JPS definition creation for output node extensions
- `jps-validate` — JPS schema validation
- `jps-scope-refresh` — scope catalog refresh after schema changes
- `bff-deploy` — BFF API deployment
- `code-page-deploy` — SprkChatPane/AnalysisWorkspace web resource deployment
- `dataverse-create-schema` — Dataverse entity/field creation

**Knowledge docs**:
- `docs/architecture/AI-ARCHITECTURE.md` — AI framework architecture
- `docs/architecture/playbook-architecture.md` — AI implementation patterns
- `docs/architecture/playbook-architecture.md` — JPS schema and scope model
- `docs/ai-knowledge/catalogs/scope-model-index.json` — scope catalog

**Patterns**:
- `.claude/patterns/ai/streaming-endpoints.md` — SSE streaming canonical pattern
- `.claude/patterns/ai/analysis-scopes.md` — three-tier scope system
- `.claude/patterns/ai/text-extraction.md` — document extraction routing
- `.claude/patterns/api/endpoint-definition.md` — Minimal API endpoint pattern
- `.claude/patterns/api/endpoint-filters.md` — authorization filter pattern
- `.claude/patterns/api/service-registration.md` — feature module DI pattern
- `.claude/patterns/caching/distributed-cache.md` — Redis cache pattern
- `.claude/patterns/pcf/dialog-patterns.md` — Code Page dialog patterns

**Existing code to extend**:
- `ChatEndpoints.cs` — existing SSE chat endpoints
- `SprkChatAgent.cs` — core agent with tool registration
- `SprkChatAgentFactory.cs` — tool resolution and agent creation
- `AnalysisChatContextResolver.cs` — stub to replace with real implementation
- `CompoundIntentDetector.cs` — plan detection pattern
- `PlaybookChatContextProvider.cs` — playbook context loading
- `WebSearchTools.cs` — mock web search to replace
- `DocxExportService.cs` — existing OpenXML DOCX generation
- `SprkChat.tsx` — main chat component (54KB)
- `SprkChatBridge.ts` — BroadcastChannel bridge with stream event types
- `useSseStream.ts` — SSE consumption hook
- `useChatContextMapping.ts` — context mapping hook

**Scripts**:
- `scripts/Deploy-BffApi.ps1` — BFF API deployment
- `scripts/Deploy-ChatContextMappings.ps1` — context mapping deployment
- `scripts/Deploy-Playbook.ps1` — playbook deployment

---

## Implementation Approach

### Parallel Execution Strategy

```
Phase 1: Foundation (sequential — establishes schemas and infrastructure)
  └── Tasks 001-009

Phase 2: BFF API Services          Phase 3: Frontend UI Components
  ├── Group A: SSE + Streaming       ├── Group C: Markdown + SSE UI
  ├── Group B: Playbook Dispatcher   ├── Group D: Slash Commands + UI
  └── (sequential: Context Resolver) └── Group E: Upload + Word UI
  Tasks 010-029                      Tasks 030-049

         ↓ Both complete ↓

Phase 4: Integration & Write-back (requires Phase 2 + 3)
  └── Tasks 050-059

Phase 5: Testing & Deployment
  └── Tasks 060-069

Phase 6: Wrap-up
  └── Task 090
```

### Critical Path

```
Phase 1 (foundation)
  → Phase 2 Group A (SSE streaming BFF) + Phase 3 Group C (markdown + SSE UI) [PARALLEL]
  → Phase 4 (integration: wire BFF ↔ UI, write-back, playbook handoff)
  → Phase 5 (E2E testing, deployment)
  → Phase 6 (wrap-up)
```

### Parallel Execution Groups

| Group | Phase | Description | Files Owned | Prerequisite |
|-------|-------|-------------|-------------|-------------|
| **A** | 2 | SSE streaming + document context (BFF) | `Services/Ai/Chat/`, `Api/Ai/ChatEndpoints.cs` (streaming additions) | Phase 1 complete |
| **B** | 2 | Playbook Dispatcher + web search (BFF) | `Services/Ai/Chat/PlaybookDispatcher.cs`, `Tools/WebSearchTools.cs`, `Api/Ai/PlaybookDispatchEndpoints.cs` | Phase 1 complete |
| **C** | 3 | Markdown rendering + SSE UI + citations | `SprkChat/SprkChatMessage*.tsx`, `services/renderMarkdown.ts` | Phase 1 complete |
| **D** | 3 | Dynamic slash commands + scope capabilities UI | `SprkChat/SprkChatInput.tsx`, `SlashCommandMenu/`, `SprkChatContextSelector.tsx` | Phase 1 complete |
| **E** | 3 | Document upload zone + Open in Word UI | `SprkChat/SprkChatUploadZone.tsx`, new Code Page components | Phase 1 complete |

**Agents can run Groups A+C, B+D, or A+B+C+D+E concurrently after Phase 1.**

---

## Phase Breakdown

### Phase 1: Foundation & Infrastructure (Tasks 001-009)

**Objective**: Establish Dataverse schema, JPS extensions, AI Search index, and shared utilities that all subsequent phases depend on.

**Deliverables**:
1. Dataverse schema changes — scope capabilities field, playbook trigger metadata fields, scope searchGuidance field
2. JPS schema extensions — output node type, autonomous action flag, trigger metadata
3. Playbook embedding AI Search index — dedicated `playbook-embeddings` index with vector field
4. Shared markdown rendering utility — `renderMarkdown()` in `@spaarke/ui-components`
5. Playbook trigger metadata seeding — populate existing playbooks with trigger phrases and record types

**Critical Tasks**:
- 001: Dataverse schema — add `sprk_capabilities` to scope, `sprk_triggerPhrases`/`sprk_recordType`/`sprk_entityType`/`sprk_tags` to playbook, `sprk_searchGuidance` to scope
- 002: JPS schema extensions — add `output` node type and `requiresConfirmation` flag to JPS JSON definition
- 003: Create playbook-embeddings AI Search index — schema with `contentVector3072` field, configure text-embedding-3-large
- 004: Shared `renderMarkdown()` utility — single pipeline using `marked` + `dompurify`, Fluent v9 CSS tokens, dark mode support
- 005: Seed playbook trigger metadata — populate existing playbooks with trigger phrases and matching metadata
- 006: Deploy Phase 1 (Dataverse schema + AI Search index)

**Inputs**: spec.md, existing Dataverse schema, existing JPS definitions
**Outputs**: Updated Dataverse schema, JPS extensions, AI Search index, shared markdown utility

---

### Phase 2: BFF API Services (Tasks 010-029) — PARALLEL with Phase 3

**Objective**: Implement all backend services — SSE streaming, document context injection, PlaybookDispatcher, AnalysisChatContextResolver, web search, document upload, Word export.

**Parallel Group A — SSE Streaming + Document Context** (Tasks 010-014):
- 010: SSE streaming endpoint — enhance `ChatEndpoints.cs` to stream `token`/`done`/`error` events via `text/event-stream`
- 011: Document context injection service — semantic chunking with 30K token budget, conversation-aware re-selection
- 012: Multi-document context aggregation — extend document injection for multiple documents within shared budget
- 013: Document upload endpoint — `POST /api/ai/chat/sessions/{id}/documents`, Document Intelligence processing, session-scoped storage
- 014: Document SPE persistence endpoint — `POST /api/ai/chat/sessions/{id}/documents/{docId}/persist`, SpeFileStore upload

**Parallel Group B — Playbook Dispatcher + Web Search** (Tasks 015-019):
- 015: PlaybookDispatcher service — two-stage matching (vector similarity → LLM refinement), parameter extraction
- 016: Playbook embedding pipeline — index playbooks into `playbook-embeddings` on create/update, embedding generation
- 017: Enhanced WebSearchTools — replace mock with Bing API, scope-guided search via `sprk_searchGuidance`, citation generation
- 018: Playbook output handler — typed outputs (text/dialog/navigation/download/insert), HITL vs autonomous routing
- 019: Dynamic command resolution service — metadata-driven command catalog from playbooks + scopes, Redis cache (5-min TTL)

**Sequential — Context Resolver + Integration Endpoints** (Tasks 020-025):
- 020: AnalysisChatContextResolver real implementation — replace stub with Dataverse queries for capabilities, commands, documents, search guidance
- 021: Scope capabilities service — `sprk_scope.sprk_capabilities` field reading, command contribution independent of playbook
- 022: Open in Word endpoint — `POST /api/ai/chat/export/word`, extend DocxExportService → SpeFileStore upload → Word Online URL
- 023: SSE write-back enhancement — stream write-back content as `document_stream_start/token/end` SSE events
- 024: Rate limiting + backpressure — apply ADR-016 to all new endpoints
- 025: Deploy Phase 2 (BFF API)

**Critical Tasks**: 010 (SSE streaming), 015 (PlaybookDispatcher), 020 (ContextResolver)

---

### Phase 3: Frontend UI Components (Tasks 030-049) — PARALLEL with Phase 2

**Objective**: Implement all frontend changes — markdown rendering integration, SSE streaming UI, dynamic slash commands, citation cards, document upload zone, Open in Word button.

**Parallel Group C — Markdown + SSE + Citations** (Tasks 030-034):
- 030: Integrate `renderMarkdown()` into SprkChatMessageRenderer — replace raw text rendering with formatted HTML
- 031: SSE streaming UI — token-by-token rendering with typing indicator in SprkChat, `useSseStream` enhancement
- 032: Citation cards — inline numbered citations `[N]`, `SprkChatCitationPopover` enhancement for web search sources
- 033: Plan preview streaming — update `PlanPreviewCard` for SSE-based plan step progress
- 034: Dark mode audit — verify all new UI elements use Fluent v9 semantic tokens (ADR-021)

**Parallel Group D — Slash Commands + Scope Capabilities** (Tasks 035-039):
- 035: Dynamic slash command resolution — fetch commands at session init from context mapping endpoint, populate `dynamicSlashCommands` prop
- 036: `SlashCommandMenu` enhancement — category-based grouping (system/playbook/scope), visual distinction for dynamic commands
- 037: Scope capability commands — surface scope-contributed commands in slash menu independent of playbook
- 038: Command catalog caching — client-side cache with session-scoped invalidation
- 039: Action confirmation UX — `requiresConfirmation` HITL dialog vs autonomous execution feedback

**Parallel Group E — Upload + Word + Multi-doc** (Tasks 040-045):
- 040: Document upload zone — drag-and-drop zone in SprkChat, file type validation, upload progress indicator
- 041: Upload processing feedback — show extraction progress, inject into context notification
- 042: "Save to matter files" action — optional SPE persistence UX for uploaded documents
- 043: Multi-document selector — extend SprkChatContextSelector for multi-document selection from AnalysisWorkspace file list
- 044: Open in Word button — export action in chat, loading state, Word Online redirect
- 045: Deploy Phase 3 (SprkChatPane + AnalysisWorkspace web resources)

---

### Phase 4: Integration & Write-back (Tasks 050-059) — Requires Phase 2 + 3

**Objective**: Wire BFF services to frontend, implement write-back streaming via BroadcastChannel, end-to-end playbook dispatch → dialog handoff.

**Tasks**:
- 050: SSE streaming integration — wire BFF SSE endpoint to `useSseStream` hook, verify token-by-token rendering
- 051: Write-back via SSE + BroadcastChannel — stream write-back content from BFF → SprkChat → SprkChatBridge → Lexical editor
- 052: Playbook dispatch → dialog handoff — natural language → PlaybookDispatcher → typed output → Code Page dialog open
- 053: Dynamic slash command integration — wire context mapping → slash menu population → command execution
- 054: Document context integration — verify document injection, conversation-aware chunking, multi-doc analysis
- 055: Web search integration — wire BFF web search → citation cards → scope guidance
- 056: Upload integration — wire drag-and-drop → BFF upload → Document Intelligence → context injection
- 057: Open in Word integration — wire export button → BFF endpoint → Word Online open
- 058: Deploy Phase 4 (full stack)

---

### Phase 5: Testing & Hardening (Tasks 060-069)

**Objective**: End-to-end testing, performance validation, error handling, NFR verification.

**Tasks**:
- 060: SSE streaming E2E tests — latency (NFR-01: first token < 500ms), reconnection, cancellation
- 061: Playbook Dispatcher E2E tests — semantic matching accuracy, parameter extraction, HITL/autonomous paths
- 062: Document context E2E tests — chunking accuracy, budget enforcement (NFR-05: 128K limit), multi-doc
- 063: Upload E2E tests — processing time (NFR-02: < 15 seconds), file type validation, SPE persistence
- 064: Context resolver E2E tests — initialization time (NFR-03: < 3 seconds with Redis), capability accuracy
- 065: Rate limiting verification — ADR-016 compliance, 429/503 responses
- 066: Security audit — DOMPurify sanitization, token budget enforcement, session-scoped upload cleanup (NFR-06), no document content logging (ADR-015)
- 067: Deploy Phase 5 (final deployment)

---

### Phase 6: Wrap-up (Task 090)

**Objective**: Project completion, documentation, quality gates.

**Tasks**:
- 090: Project wrap-up — code-review, adr-check, repo-cleanup, README update, lessons learned

---

## Dependencies

### External Dependencies

| Dependency | Required By | Status |
|------------|-------------|--------|
| Azure OpenAI text-embedding-3-large | Task 003, 016 | Ready |
| Azure AI Search | Task 003, 015 | Ready |
| Document Intelligence | Task 013, 041 | Ready |
| Bing Web Search API | Task 017 | Pending (GitHub #232) |
| Open XML SDK | Task 022 | Ready (in use) |
| `marked` npm package | Task 004 | To evaluate |
| `dompurify` npm package | Task 004 | To add |

### Internal Dependencies (Task-Level)

```
Phase 1 (001-006) → Phase 2 (010-025) + Phase 3 (030-045) [PARALLEL]
  Phase 2 + Phase 3 → Phase 4 (050-058)
    Phase 4 → Phase 5 (060-067)
      Phase 5 → Phase 6 (090)

Within Phase 2:
  Group A (010-014): Sequential internally
  Group B (015-019): 015 → 016, 017 independent, 018 depends on 015, 019 depends on 020
  020-025: Sequential, depends on Groups A + B

Within Phase 3:
  Group C (030-034): 030 first (markdown), then 031-034 parallel
  Group D (035-039): Mostly parallel after 035
  Group E (040-045): Mostly parallel
```

---

## Testing Strategy

| Test Type | Scope | Tools |
|-----------|-------|-------|
| Unit tests | Services, utilities | xUnit, Moq |
| Integration tests | API endpoints, Redis, AI Search | xUnit, TestServer |
| E2E/UI tests | Full stack flows | Manual + ui-test skill |
| Performance | NFR validation | Custom scripts, Application Insights |

---

## Acceptance Criteria

### Technical
- All ADR constraints satisfied (verified by adr-check)
- All 18 functional requirements met
- All 8 non-functional requirements met
- ≤15 non-framework DI registrations (ADR-010)
- No raw markdown visible in chat UI
- SSE first token < 500ms

### Business
- All 16 graduation criteria checked off
- User can complete full workflow: ask question → AI streams response with citations → request action → playbook dispatches → dialog opens pre-populated

---

## Risk Register

| Risk | Impact | Likelihood | Mitigation | Owner |
|------|--------|------------|------------|-------|
| ADR-010 DI ceiling | Med | High | Use factory instantiation for new tools | Dev |
| Token budget exceeded | High | Med | Strict partitioning with user notification | Dev |
| Bing API not provisioned | Med | Med | Mock-first implementation, wire later | Dev |
| SSE reconnection complexity | Med | Med | Redis session persistence, graceful degradation | Dev |
| Large SprkChat.tsx complexity | Med | Med | Component decomposition, careful merge strategy | Dev |

---

## Next Steps

Run task-create to generate individual task files from this plan.

---

*For Claude Code: This plan provides implementation context. Load relevant sections when executing tasks. Phases 2 and 3 are designed for concurrent agent execution.*
