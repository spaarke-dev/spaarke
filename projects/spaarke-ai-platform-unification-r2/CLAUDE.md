# CLAUDE.md - Spaarke AI Platform Unification R2

> **Project**: spaarke-ai-platform-unification-r2
> **Type**: Platform Rebuild (frontend) + Extension (backend)
> **Branch**: work/spaarke-ai-platform-unification-r2

## Project Context

Rebuild SpaarkeAi frontend shell and extend BFF backend for AI-directed three-pane experience with dynamic capability orchestration, safety perimeter, and Cosmos DB persistence.

**This is a REBUILD, not an enhancement.** The frontend shell, event bus, and widget registries are replaced. SprkChat and existing widgets are preserved and migrated.

## Applicable ADRs

| ADR | Relevance |
|-----|-----------|
| ADR-001 | Minimal API + BackgroundService (Functions acceptable with justification) |
| ADR-004 | Job contract for RecordSyncJob |
| ADR-006 | Code Pages for standalone UI (SpaarkeAi) |
| ADR-007 | SpeFileStore facade (CompareDocumentsTool) |
| ADR-008 | Endpoint filters for all new AI endpoints |
| ADR-009 | Redis-first caching; CapabilityManifest uses IMemoryCache with documented exception |
| ADR-010 | DI minimalism via feature modules |
| ADR-012 | Shared component library (@spaarke/ai-widgets) |
| ADR-013 | AI architecture: extend BFF, not separate service |
| ADR-014 | AI caching for expensive results |
| ADR-015 | AI data governance (AMENDED: governed stores are exceptions) |
| ADR-016 | AI rate limits for new endpoints |
| ADR-021 | Fluent UI v9, dark mode, tokens only |
| ADR-022 | React 19 for Code Pages (bundled) |

## Key Technical Decisions

- **D-01**: Single LLM call per turn always (router pre-selects tools[])
- **D-03**: Stream + retroactive annotation for groundedness
- **D-06**: Write-through to Cosmos (not idle-flush)
- **D-08**: Data-refreshed widget restore (not stale snapshot)
- **D-17**: Frontend is REBUILD (shell + events); SprkChat preserved
- **D-18**: New `@spaarke/ai-widgets` package (separate from ai-outputs)
- **D-20**: ADR-015 amended with governed data store exceptions

## Resource Quick Reference

### Patterns
- `.claude/patterns/ai/` — SSE streaming, text extraction, analysis scopes
- `.claude/patterns/api/` — endpoints, filters, DI registration, background workers

### Constraints
- `.claude/constraints/ai.md` — AI/ML MUST/MUST NOT
- `.claude/constraints/api.md` — BFF API constraints
- `.claude/constraints/pcf.md` — Code Page constraints (React 19)

### Architecture Docs
- `docs/architecture/AI-ARCHITECTURE.md`
- `docs/architecture/chat-architecture.md`
- `docs/architecture/playbook-architecture.md`
- `docs/architecture/code-pages-architecture.md`

### Key Source Files (R1 — preserve/extend)
- `Services/Ai/Chat/SprkChatAgentFactory.cs` (866 lines — extend)
- `Services/Ai/Chat/ChatSessionManager.cs` (202 lines — extend)
- `Api/Ai/ChatEndpoints.cs` (700+ lines — extend)
- `Services/Ai/Chat/Middleware/AgentServiceRoutingMiddleware.cs` (407 lines — inform Layer 1)
- `Services/Ai/Chat/PlaybookChatContextProvider.cs` (563 lines — extend)
- `SprkChat.tsx` (2,091 lines — preserve as Conversation pane child)

### Infrastructure
- `infrastructure/bicep/modules/` — existing modules (no Cosmos yet)
- `infrastructure/ai-search/` — index schemas

## Task Execution Protocol

When executing tasks in this project, ALWAYS use the `task-execute` skill. See root CLAUDE.md for the mandatory protocol.

## Parallel Execution Safety

Tasks are grouped for parallel agent execution. Rules:
- Tasks in the same parallel group MUST NOT modify the same files
- `.claude/` paths are main-session-only (no parallel agent writes)
- Build verification runs between parallel waves
- Failed tasks are retried sequentially, not re-parallelized
