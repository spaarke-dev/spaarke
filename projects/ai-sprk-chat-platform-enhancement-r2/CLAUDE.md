# CLAUDE.md — SprkChat Platform Enhancement R2

> **Project**: ai-sprk-chat-platform-enhancement-r2
> **Type**: Feature Enhancement (AI + Full Stack)
> **Complexity**: High (13 in-scope items, 18 FRs, 12 ADRs)

## Project Status

| Field | Value |
|-------|-------|
| **Phase** | Development |
| **Last Updated** | 2026-03-17 |
| **Current Task** | none |
| **Next Action** | Execute task 001 |

## Quick Reference

### Key Files

| File | Purpose |
|------|---------|
| [spec.md](spec.md) | AI implementation specification |
| [README.md](README.md) | Project overview and graduation criteria |
| [plan.md](plan.md) | Implementation plan with parallel groups |
| [current-task.md](current-task.md) | Active task state (for recovery) |
| [TASK-INDEX.md](tasks/TASK-INDEX.md) | Task status registry |

### Project Metadata

- **Name**: SprkChat Platform Enhancement R2
- **Predecessor**: ai-sprk-chat-workspace-companion (R1, 29 tasks, complete)
- **Branch**: work/ai-sprk-chat-workspace-companion
- **Parallel Groups**: A (SSE+Docs BFF), B (Playbook BFF), C (Markdown+SSE UI), D (Slash Commands UI), E (Upload+Word UI)

## Context Loading Rules

1. **Always load first**: This file (CLAUDE.md) + current-task.md
2. **Load for task context**: The active task's `.poml` file + its `<knowledge>` files
3. **Load for architecture decisions**: `.claude/adr/ADR-XXX.md` (concise versions)
4. **Load for patterns**: `.claude/patterns/` matching the task's resource types
5. **Load for deep dive only**: `docs/` full documentation (avoid unless needed)

## 🚨 MANDATORY: Task Execution Protocol

**ABSOLUTE RULE**: When executing project tasks, invoke the `task-execute` skill. DO NOT read POML files directly and implement manually.

### Trigger Phrases → Required Action

| User Says | Action |
|-----------|--------|
| "work on task X" | Invoke task-execute with task X POML file |
| "continue" | Check TASK-INDEX.md for next 🔲, invoke task-execute |
| "next task" | Check TASK-INDEX.md for next 🔲, invoke task-execute |
| "keep going" | Check TASK-INDEX.md for next 🔲, invoke task-execute |
| "resume task X" | Invoke task-execute with task X POML file |

### Parallel Task Execution

Tasks within the same parallel group (A-E) can run concurrently:
- Send ONE message with MULTIPLE Skill tool invocations
- Each invocation calls task-execute for a different task
- Tasks MUST NOT modify the same files
- Track parallel tasks in current-task.md

### Multi-File Decomposition Rules

If a single task requires changes across multiple directories:
- BFF changes (`src/server/`) and UI changes (`src/client/`) → Split into separate tasks
- Shared library changes (`Spaarke.UI.Components/`) → Separate task
- Each task should own specific files to enable safe parallel execution

## Key Technical Constraints

### MUST Rules (from spec.md)
- MUST use single markdown parser across all surfaces (chat, editor, Word export)
- MUST sanitize rendered HTML via DOMPurify before `dangerouslySetInnerHTML`
- MUST stream SSE via `text/event-stream` content type
- MUST NOT cache streaming tokens (ADR-014) — cache final content only
- MUST NOT create static playbook relationship tables — use metadata-driven matching
- MUST NOT send full document content in Service Bus payloads (ADR-015)
- MUST NOT log document text or email body content (ADR-015)
- MUST NOT exceed 128K total token context window
- MUST NOT allow unbounded `Task.WhenAll` on AI calls (ADR-016)
- MUST scope all cache keys by tenant (ADR-014)
- MUST use endpoint filters for auth (ADR-008)
- MUST use `IDistributedCache` Redis for caching (ADR-009)
- MUST flow `ChatHostContext` through full pipeline (ADR-013)

### DI Registration Budget (ADR-010)
Current AiModule: **16 non-framework registrations** (slightly over ≤15 limit)
- New services MUST use factory instantiation (not DI registration)
- Pattern: `SprkChatAgentFactory.ResolveTools()` instantiates tools directly

### Existing Architecture to Extend
- `SprkChatAgent` — dual IChatClient pattern (execution + raw detection)
- `CompoundIntentDetector` — plan preview gate for write-back/external actions
- `PlaybookChatContextProvider` — playbook context loading with 8K token budget
- `SprkChatBridge` — BroadcastChannel with typed stream events
- `DocxExportService` — OpenXML SDK DOCX generation (extend for Word Online)

## Decisions Made

| Date | Decision | Rationale |
|------|----------|-----------|
| 2026-03-17 | Conversation-aware chunking (not static) | Accuracy over latency for long docs |
| 2026-03-17 | Optional SPE persist for uploads | User control over document lifecycle |
| 2026-03-17 | Dedicated playbook-embeddings index | Clean separation from document RAG |
| 2026-03-17 | Dataverse fields for matching + JPS for execution | Two-layer: queryable + runtime |

## Implementation Notes

*(Updated during task execution)*

## Resources

### Applicable ADRs
- [ADR-001](.claude/adr/ADR-001-minimal-api.md) — Minimal API, no Functions
- [ADR-006](.claude/adr/ADR-006-pcf-over-webresources.md) — PCF vs Code Pages
- [ADR-008](.claude/adr/ADR-008-endpoint-filters.md) — Endpoint filters for auth
- [ADR-009](.claude/adr/ADR-009-redis-caching.md) — Redis-first caching
- [ADR-010](.claude/adr/ADR-010-di-minimalism.md) — DI ≤15 registrations
- [ADR-012](.claude/adr/ADR-012-shared-components.md) — Shared component library
- [ADR-013](.claude/adr/ADR-013-ai-architecture.md) — AI architecture, extend BFF
- [ADR-014](.claude/adr/ADR-014-ai-caching.md) — No streaming token cache
- [ADR-015](.claude/adr/ADR-015-ai-data-governance.md) — Data minimization
- [ADR-016](.claude/adr/ADR-016-ai-rate-limits.md) — Rate limiting
- [ADR-021](.claude/adr/ADR-021-fluent-design-system.md) — Fluent v9, dark mode
- [ADR-022](.claude/adr/ADR-022-pcf-platform-libraries.md) — Code Pages: React 18/19

### Key Patterns
- `.claude/patterns/ai/streaming-endpoints.md` — SSE canonical pattern
- `.claude/patterns/ai/analysis-scopes.md` — Three-tier scope system
- `.claude/patterns/api/endpoint-definition.md` — Minimal API endpoints
- `.claude/patterns/caching/distributed-cache.md` — Redis cache pattern

### Related Projects
- `ai-sprk-chat-workspace-companion` (R1) — predecessor, 29 tasks complete

---

*Project context for Claude Code. Updated automatically during task execution.*
