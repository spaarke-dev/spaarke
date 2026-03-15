# Implementation Plan: SprkChat Context Awareness R1

> **Project**: ai-sprk-chat-context-awareness-r1
> **Created**: 2026-03-15
> **Spec**: [spec.md](spec.md)

---

## Executive Summary

Implement a 4-phase data-driven context mapping system for SprkChat. Phase 1 delivers the core Dataverse entity, BFF service, and client integration. Phases 2-4 add multi-playbook UX, AI prompt enrichment, and admin tools. The plan is structured for **parallel execution** — independent work streams within and across phases can run concurrently via Claude Code task agents.

## Architecture Context

### Affected Components

| Layer | Component | Change Type |
|-------|-----------|-------------|
| Dataverse | `sprk_aichatcontextmap` entity | New |
| BFF API | `ChatContextMappingService` | New service |
| BFF API | `ChatEndpoints.cs` | New endpoint |
| BFF API | `ChatHostContext.cs` | Add `PageType` field |
| BFF API | `ChatSession` record | `PlaybookId` → `Guid?` |
| BFF API | `PlaybookChatContextProvider` | Phase 3 enrichment |
| Client | `contextService.ts` | Add `detectPageType()` |
| Client | `App.tsx` (SprkChatPane) | Dynamic startup flow |
| Client | `SprkChatContextSelector` | Populate from API |
| Client | `IHostContext` interface | Add `pageType` |
| Data | Seed data JSON | PAC CLI import file |
| Dataverse | Admin form | Model-driven CRUD |

### Discovered Resources

**ADRs (10)**:
- ADR-001 (Minimal API), ADR-006 (PCF vs Code Pages), ADR-007 (SpeFileStore)
- ADR-008 (Endpoint Filters), ADR-009 (Redis-First), ADR-010 (DI Minimalism)
- ADR-012 (Shared Library), ADR-013 (AI Architecture), ADR-014 (AI Caching)
- ADR-021 (Fluent v9), ADR-022 (PCF Platform Libraries)

**Constraints**: `api.md`, `ai.md`, `pcf.md`, `data.md`, `auth.md`

**Patterns**:
- `api/endpoint-definition.md`, `api/endpoint-filters.md`, `api/service-registration.md`
- `caching/distributed-cache.md`
- `dataverse/entity-operations.md`, `dataverse/web-api-client.md`
- `pcf/control-initialization.md`
- `ai/streaming-endpoints.md`

**Canonical Code References**:
- `ChatEndpoints.cs` — endpoint registration, tenant extraction, auth filters
- `ChatSessionManager.cs` — Redis caching pattern (tenant-scoped keys, 24h TTL)
- `PlaybookChatContextProvider.cs` — system prompt builder, scope resolution
- `ChatHostContext.cs` — current record (EntityType, EntityId, EntityName, WorkspaceType)
- `contextService.ts` — context detection, DEFAULT_PLAYBOOK_MAP, session persistence
- `App.tsx` — pane startup, auth init, context-change polling

**Scripts**: `Deploy-CustomPage.ps1`, `Test-SdapBffApi.ps1`, `Deploy-Playbook.ps1`

---

## Implementation Approach

### Parallel Execution Strategy

The plan is structured into **4 phases with 3 parallel work streams** in Phase 1, and **Phases 2, 3, 4 designed to run concurrently** after Phase 1 completes.

```
Phase 1 (MVP — Data-Driven Mapping)
├── Stream A: Backend (BFF models, service, endpoint, caching)
├── Stream B: Dataverse (entity creation, seed data)
└── Stream C: Client (page detection, dynamic startup, fallback)
    └── Integration: Wire all streams together + test

Phase 2 (Multi-Playbook UX)    ─┐
Phase 3 (Entity Enrichment)    ─┼── Run in PARALLEL (different files/layers)
Phase 4 (Admin Experience)     ─┘

Phase 5 (Integration & Wrap-up)
└── Final integration testing, deployment, wrap-up
```

### Concurrency Rules

1. **Within Phase 1**: Streams A, B, C own different files — safe for parallel agents
2. **After Phase 1**: Phases 2, 3, 4 touch different files — safe for parallel agents
3. **Phase 5**: Sequential — integration testing after all phases merge

---

## Phase Breakdown

### Phase 1: Data-Driven Mapping (MVP)

**Goal**: Replace hardcoded playbook map with Dataverse-backed, Redis-cached context resolution.

#### Stream A — Backend (BFF API)

| Task | Description | Files | Estimate |
|------|-------------|-------|----------|
| 1A.1 | Add `PageType` to `ChatHostContext` record (nullable, backward-compatible) | `ChatHostContext.cs` | 1h |
| 1A.2 | Make `ChatSession.PlaybookId` nullable (`Guid?`); update `CreateSessionAsync` signature | `ChatSession.cs`, `ChatSessionManager.cs`, `ChatDataverseRepository.cs` | 2h |
| 1A.3 | Create `ChatContextMappingService` with Redis caching (30-min TTL, resolution order: exact → entity+any → wildcard+pageType → global fallback) | New: `Services/Ai/Chat/ChatContextMappingService.cs` | 3h |
| 1A.4 | Create `GET /api/ai/chat/context-mappings` endpoint with auth filter + tenant extraction | `ChatEndpoints.cs`, new response records | 2h |
| 1A.5 | Handle null PlaybookId in `SprkChatAgent` (raw conversational AI, no tools) | `SprkChatAgent.cs` | 2h |
| 1A.6 | Unit tests for ChatContextMappingService (resolution order, caching, fallback) | New test files | 3h |

#### Stream B — Dataverse

| Task | Description | Files | Estimate |
|------|-------------|-------|----------|
| 1B.1 | Create `sprk_aichatcontextmap` entity with all fields (name, entitytype, pagetype choice, playbookid lookup, sortorder, isdefault, description, isactive) | Dataverse Web API | 2h |
| 1B.2 | Create seed data JSON for PAC CLI import (matter/form → Document Assistant) | New: `data/chat-context-mappings.json` | 1h |

#### Stream C — Client

| Task | Description | Files | Estimate |
|------|-------------|-------|----------|
| 1C.1 | Add `pageType` to `IHostContext` interface; implement `detectPageType()` in `contextService.ts` (form/list/dashboard/workspace/unknown) | `types.ts`, `contextService.ts` | 2h |
| 1C.2 | Replace `DEFAULT_PLAYBOOK_MAP` with dynamic API call; add sessionStorage caching (5-min TTL) | `contextService.ts`, `App.tsx` | 3h |
| 1C.3 | Implement no-mapping fallback UI (hide selector, show generic chat) | `App.tsx`, `SprkChat.tsx` | 2h |

#### Integration

| Task | Description | Files | Estimate |
|------|-------------|-------|----------|
| 1INT.1 | End-to-end integration: BFF ↔ Dataverse ↔ Client wiring + smoke test | Cross-layer | 2h |
| 1INT.2 | Deploy Phase 1 to dev environment | Deploy scripts | 1h |

**Parallel Groups (Phase 1)**:
- **Group A**: Tasks 1A.1, 1A.2, 1B.1, 1B.2 — no file overlap, run concurrently
- **Group B**: Tasks 1A.3, 1A.4, 1C.1 — after Group A models are defined, run concurrently
- **Group C**: Tasks 1A.5, 1A.6, 1C.2, 1C.3 — after endpoint exists, run concurrently
- **Group D**: Tasks 1INT.1, 1INT.2 — sequential after all above

---

### Phase 2: Multi-Playbook UX

**Goal**: Populate playbook selector from context-mapping response; support runtime switching.
**Prerequisite**: Phase 1 complete (endpoint returns `availablePlaybooks[]`)

| Task | Description | Files | Estimate |
|------|-------------|-------|----------|
| 2.1 | Wire `SprkChatContextSelector` to populate from `availablePlaybooks[]` | `SprkChatContextSelector` component | 2h |
| 2.2 | Implement playbook switching via PATCH `/sessions/{id}/context` + `switchContext()` | `App.tsx`, `contextService.ts` | 2h |
| 2.3 | Client sessionStorage caching: key `"sprkchat-context-{entityType}-{pageType}"`, 5-min TTL | `contextService.ts` | 1h |
| 2.4 | Unit/component tests for multi-playbook UX | Test files | 2h |

**Parallel Groups (Phase 2)**:
- **Group E**: Tasks 2.1, 2.2, 2.3 — different functions, run concurrently
- Task 2.4 — after Group E

---

### Phase 3: Entity Metadata Enrichment

**Goal**: Inject entity name + page type into AI system prompt for context-aware responses.
**Prerequisite**: Phase 1 complete (ChatHostContext has PageType)

| Task | Description | Files | Estimate |
|------|-------------|-------|----------|
| 3.1 | Write unit tests for enrichment logic (null guard, budget check, append-not-replace) | New test files | 2h |
| 3.2 | Implement enrichment block in `PlaybookChatContextProvider` (append after playbook prompt, ≤100 token cap) | `PlaybookChatContextProvider.cs` | 2h |
| 3.3 | Ensure `EntityName` excluded from structured telemetry (AgentTelemetryMiddleware audit) | `AgentTelemetryMiddleware.cs` | 1h |
| 3.4 | Integration test: AI response references entity name on matter form | Test files | 2h |

**Parallel Groups (Phase 3)**:
- **Group F**: Tasks 3.1, 3.3 — independent checks, run concurrently
- Task 3.2 — after 3.1 (tests first)
- Task 3.4 — after 3.2

---

### Phase 4: Admin Experience

**Goal**: Model-driven form for CRUD + cache refresh button.
**Prerequisite**: Phase 1 complete (entity exists)

| Task | Description | Files | Estimate |
|------|-------------|-------|----------|
| 4.1 | Create model-driven form for `sprk_aichatcontextmap` (view, create, edit, deactivate) | Dataverse solution | 2h |
| 4.2 | Implement `DELETE /api/ai/chat/context-mappings/cache` endpoint (evict `chat:ctx-mapping:*` Redis keys) | `ChatEndpoints.cs` | 1h |
| 4.3 | Wire "Refresh Mappings" button on admin form to cache eviction endpoint | Ribbon/command bar | 2h |
| 4.4 | Admin workflow tests (create mapping → refresh → verify SprkChat picks up) | Test files | 2h |

**Parallel Groups (Phase 4)**:
- **Group G**: Tasks 4.1, 4.2 — Dataverse form + BFF endpoint, run concurrently
- Task 4.3 — after Group G
- Task 4.4 — after 4.3

---

### Phase 5: Integration, Testing & Wrap-up

**Goal**: Final integration testing, deployment verification, project closure.
**Prerequisite**: Phases 2, 3, 4 all complete

| Task | Description | Files | Estimate |
|------|-------------|-------|----------|
| 5.1 | Full end-to-end integration test (all 4 phases working together) | Cross-layer | 3h |
| 5.2 | Performance validation: context-mappings ≤50ms, pane-open ≤300ms | k6/perf tooling | 2h |
| 5.3 | Deploy all phases to dev environment | Deploy scripts | 2h |
| 5.4 | Project wrap-up (README status, lessons learned, archive) | Project files | 1h |

---

## Cross-Phase Parallel Execution Map

```
Timeline    Phase 1 (MVP)                     Phases 2/3/4              Phase 5
──────────────────────────────────────────────────────────────────────────────────
            ┌─ Stream A (BFF) ──────┐
  Start ────┤  Stream B (DV)  ──────┼── Integration ──┐
            └─ Stream C (Client) ───┘                 │
                                                      ▼
                                         ┌─ Phase 2 (UX) ────────┐
                                         ├─ Phase 3 (Enrichment) ┼── Phase 5
                                         └─ Phase 4 (Admin) ─────┘   (Final)
```

### Parallel Execution Groups Summary

| Group | Tasks | Prerequisites | Agent Scope |
|-------|-------|---------------|-------------|
| **A** | 1A.1, 1A.2, 1B.1, 1B.2 | None | 4 concurrent agents: BFF models + DV entity |
| **B** | 1A.3, 1A.4, 1C.1 | Group A | 3 concurrent agents: service + endpoint + client detection |
| **C** | 1A.5, 1A.6, 1C.2, 1C.3 | Group B | 4 concurrent agents: null handling + tests + client wiring |
| **D** | 1INT.1, 1INT.2 | Group C | Sequential: integration then deploy |
| **E** | 2.1, 2.2, 2.3 | Phase 1 | 3 concurrent agents: selector + switching + caching |
| **F+G** | 3.1, 3.3, 4.1, 4.2 | Phase 1 | 4 concurrent agents: Phase 3 tests + telemetry + Phase 4 form + cache endpoint |
| **H** | 2.4, 3.2, 4.3 | E, F, G | 3 concurrent agents: Phase 2 tests + enrichment impl + admin wiring |
| **I** | 3.4, 4.4 | H | 2 concurrent agents: integration tests |
| **J** | 5.1-5.4 | All | Sequential: final integration + deploy + wrap-up |

---

## Dependencies

### Internal Dependencies

```
1A.1 (ChatHostContext) ──→ 1A.3 (MappingService) ──→ 1A.4 (Endpoint)
1A.2 (Nullable PlaybookId) ──→ 1A.5 (Null handling)
1B.1 (DV Entity) ──→ 1A.3 (Service queries entity)
1B.1 (DV Entity) ──→ 4.1 (Admin form)
Phase 1 ──→ Phase 2 (endpoint available)
Phase 1 ──→ Phase 3 (ChatHostContext has PageType)
Phase 1 ──→ Phase 4 (entity + service exist)
3.1 (Tests) ──→ 3.2 (Implementation)
4.1 + 4.2 ──→ 4.3 (Wiring)
Phases 2,3,4 ──→ Phase 5 (Integration)
```

### External Dependencies

- PAC CLI for seed data import
- Dataverse dev environment access
- Active `sprk_aiplaybook` record for seed data reference

---

## Testing Strategy

| Level | Scope | Approach |
|-------|-------|----------|
| Unit | ChatContextMappingService | Resolution order, caching, fallback |
| Unit | PlaybookChatContextProvider | Enrichment append, null guard, token cap |
| Unit | detectPageType() | Form/list/dashboard/workspace/unknown |
| Integration | BFF endpoint | Auth filter, tenant extraction, Redis cache |
| Integration | Full pipeline | Context detection → mapping → playbook selection |
| Performance | Endpoint latency | ≤50ms p95 Redis hit |
| Performance | Pane-open time | ≤300ms p95 |

---

## Risk Register

| Risk | Probability | Impact | Mitigation |
|------|-------------|--------|-----------|
| Enrichment disrupts playbook prompts | Low | High | Append-only, never mid-prompt |
| Entity name PII in logs | Low | High | Never in structured properties |
| Token budget exceeded | Medium | Medium | Check before append, skip + warn |
| PlaybookChatContextProvider untested | Medium | Medium | Tests-first approach (3.1 before 3.2) |
| Concurrent agent file conflicts | Low | Medium | Clear file ownership per stream |

---

## Acceptance Criteria

1. No hardcoded playbook GUIDs remain in client code
2. Admin can change mappings without code deployment
3. `GET /api/ai/chat/context-mappings` ≤50ms p95 (Redis hit)
4. SprkChatPane opens ≤300ms p95
5. Generic chat mode works with zero mapping records
6. AI references entity name in Phase 3 responses
7. Entity name absent from Application Insights structured logs
8. SSE streaming not regressed
9. Backward-compatible ChatHostContext deserialization

---

*Generated by Claude Code project-pipeline*
