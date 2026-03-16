# SprkChat Analysis Workspace Companion - AI Context

> **Purpose**: Provides context for Claude Code when working on ai-sprk-chat-workspace-companion.
> **Always load this file first** when working on any task in this project.

---

## Project Status

- **Phase**: Planning
- **Last Updated**: 2026-03-16
- **Current Task**: Not started
- **Next Action**: Execute tasks from TASK-INDEX.md starting with task 001

---

## Quick Reference

### Key Files
- [`spec.md`](spec.md) - Original design specification (permanent reference)
- [`README.md`](README.md) - Project overview and graduation criteria
- [`plan.md`](plan.md) - Implementation plan and WBS
- [`current-task.md`](current-task.md) - **Active task state** (for context recovery)
- [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) - Task tracker

### Project Metadata
- **Project Name**: ai-sprk-chat-workspace-companion
- **Type**: Code Page + Shared UI Library + BFF API
- **Complexity**: High (6 phases, 12 new files, 7 modified files, new BFF endpoint)

### Source Branch
- **Branch**: `work/ai-sprk-chat-workspace-companion`
- **Worktree**: `c:\code_files\spaarke-wt-ai-sprk-chat-workspace-companion`

---

## Context Loading Rules

When working on this project, Claude Code should:

1. **Always load this CLAUDE.md first** when starting work on any task
2. **Check current-task.md** for active work state (especially after compaction/new session)
3. **Reference spec.md** for design decisions, requirements, and acceptance criteria
4. **Load the relevant task file** from `tasks/` based on current work
5. **Apply ADRs** relevant to the technologies used (loaded automatically via adr-aware)

**Context Recovery**: If resuming work, say "where was I?" or "continue" to load state.

---

## 🚨 MANDATORY: Task Execution Protocol

**ABSOLUTE RULE**: All task work MUST use the `task-execute` skill. DO NOT read POML files directly and implement manually.

### Auto-Detection Rules (Trigger Phrases)

| User Says | Required Action |
|-----------|-----------------|
| "work on task X" | Execute task X via task-execute |
| "continue" | Execute next pending task (check TASK-INDEX.md for next 🔲) |
| "continue with task X" | Execute task X via task-execute |
| "next task" | Execute next pending task via task-execute |
| "keep going" | Execute next pending task via task-execute |
| "resume task X" | Execute task X via task-execute |
| "pick up where we left off" | Load current-task.md, invoke task-execute |

### Parallel Task Execution

Tasks 001–003 (Phase 2A), 010–013 (Phase 2B UI library), and 020–022 (Phase 2C BFF) are **designed for parallel execution**. When executing parallel groups:
- Send ONE message with MULTIPLE task-execute Skill tool calls
- Each call handles one task independently
- Tasks in different modules (BFF vs. UI library vs. Code Pages) are safe to parallelize

---

## Key Technical Constraints

### BFF API
- **MUST** use Minimal API pattern — no MVC controllers (ADR-001)
- **MUST** use endpoint filter for `GET /api/ai/chat/context-mappings/analysis/{analysisId}` (ADR-008)
- **MUST** use `IDistributedCache` (Redis) with 30-min TTL for context mapping (ADR-009)
- **MUST NOT** create a separate AI microservice — extend `Sprk.Bff.Api` (ADR-013)
- **MUST NOT** call Azure AI services from PCF or client-side

### Shared UI Library (`@spaarke/ui-components`)
- **MUST NOT** import `Xrm` or `ComponentFramework.*` — no Dataverse SDK dependency (ADR-012, NFR-03)
- **MUST** use Fluent UI v9 tokens only — no hard-coded colors (ADR-021)
- **MUST** support dark mode (ADR-021)
- **MUST** use callback-based props — no direct service calls from shared components
- `InlineAiToolbar` and `SlashCommandMenu` are Code Pages only (not PCF-safe — may use jsx-runtime)

### AnalysisWorkspace (Code Page — React 19)
- **MUST** use React 19 APIs (`createRoot`) — NOT React 16/17 (ADR-022)
- **MUST NOT** use `ReactDOM.render()` — that's PCF-only
- Read parameters from `URLSearchParams` (not PCF context)

### SprkChat Behavior
- **MUST** use `mousedown` (not `click`) on inline toolbar buttons to prevent selection loss
- **MUST** route all inline actions through existing SprkChat session (appears in chat history)
- **MUST** use `document_insert` BroadcastChannel event for insert-to-editor (reuse existing bridge)
- **MUST** reuse existing `DiffReviewPanel` for diff-type inline actions — no new component

### Plan Preview (Phase 2F)
- **MUST NOT** stub plan preview — BFF must emit real `plan_preview` SSE event type
- **MUST** gate all 2+ tool chains and Dataverse write-back behind plan preview
- Write-back targets `sprk_analysisoutput.sprk_workingdocument` ONLY — never SPE source files

---

## Existing Patterns to Follow

| Pattern | File | Used For |
|---------|------|----------|
| Context mapping service | `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/ChatContextMappingService.cs` | Extend for analysis-specific resolution |
| SSE streaming pattern | `src/server/api/Sprk.Bff.Api/Api/Ai/AiToolEndpoints.cs` | `plan_preview` SSE event type |
| BroadcastChannel bridge | `src/client/code-pages/AnalysisWorkspace/src/hooks/useDocumentStreaming.ts` | Extend for `inline_action` event |
| Diff review hook | `src/client/code-pages/AnalysisWorkspace/src/hooks/useDiffReview.ts` | Reuse for diff-type inline actions |
| SprkChat entry point | `src/client/shared/Spaarke.UI.Components/src/components/SprkChat/SprkChat.tsx` | Mount SlashCommandMenu + QuickActionChips |
| Launcher script | `src/client/code-pages/SprkChatPane/launcher/openSprkChatPane.ts` | Extend with `SprkChatLaunchContext` |

---

## Parallel Execution Design

This project is decomposed for maximum parallel execution across Claude Code task agents:

```
┌─────────────────────────────────────────────────────────────┐
│  GROUP A (independent — run in parallel)                    │
│  001-003: Phase 2A Contextual Launch (openSprkChatPane,     │
│           AnalysisWorkspace, EventsPage/SpeAdminApp)        │
│  010-013: Phase 2B UI Library (InlineAiToolbar components)  │
│  020-022: Phase 2C BFF endpoint + resolver + seed data      │
├─────────────────────────────────────────────────────────────┤
│  GROUP B (after Group A — run in parallel)                  │
│  030-031: Phase 2B wiring (useInlineAiToolbar + EditorPanel)│
│  040-043: Phase 2E UI components (SlashCommandMenu,         │
│           QuickActionChips, SprkChatMessageRenderer,        │
│           PlanPreviewCard)                                  │
├─────────────────────────────────────────────────────────────┤
│  GROUP C (after Group B — run in parallel)                  │
│  050-051: Phase 2D Insert-to-Editor (event + editor hook)   │
│  060-062: Phase 2E wiring (SprkChat.tsx, SprkChatInput.tsx) │
├─────────────────────────────────────────────────────────────┤
│  GROUP D (sequential — depends on Group C)                  │
│  070-073: Phase 2F BFF Plan Preview + write-back            │
├─────────────────────────────────────────────────────────────┤
│  GROUP E (final — all phases done)                          │
│  080: Integration tests                                     │
│  081-082: Deploy (code-page-deploy + bff-deploy)            │
│  090: Project wrap-up                                       │
└─────────────────────────────────────────────────────────────┘
```

---

## Applicable ADRs

| ADR | Title | Constraint |
|-----|-------|-----------|
| [ADR-001](.claude/adr/ADR-001-minimal-api.md) | Minimal API | New BFF endpoints use Minimal API, no Azure Functions |
| [ADR-006](.claude/adr/ADR-006-pcf-over-webresources.md) | PCF over Webresources | AnalysisWorkspace is a Code Page (React 19), not PCF |
| [ADR-008](.claude/adr/ADR-008-endpoint-filters.md) | Endpoint Filters | New BFF endpoint MUST use endpoint filter for auth |
| [ADR-009](.claude/adr/ADR-009-redis-caching.md) | Redis Caching | Context mapping uses Redis, 30-min TTL |
| [ADR-012](.claude/adr/ADR-012-shared-components.md) | Shared Components | All new UI components go in @spaarke/ui-components |
| [ADR-013](.claude/adr/ADR-013-ai-architecture.md) | AI Architecture | AI extends BFF only, no separate microservice |
| [ADR-021](.claude/adr/ADR-021-fluent-design-system.md) | Fluent Design | Fluent v9 tokens only, dark mode required |
| [ADR-022](.claude/adr/ADR-022-pcf-platform-libraries.md) | PCF Platform Libraries | Code Pages: React 19 + createRoot (not PCF pattern) |

---

## Unresolved Questions

- [ ] **Plan preview session state** — How does BFF maintain "pending plan" state between `plan_preview` SSE emission and user approval? Investigate `ChatSessionManager` and `AiToolService` session model before starting Phase 2F tasks (task 070+). Blocks: plan approval endpoint design.

---

## Decisions Made

*2026-03-16*: Use static dictionary in BFF to map `sprk_playbookcapabilities` integer values to `InlineAiAction` definitions — confirmed field format is multi-select option set with 7 known values (100000000–100000006). No Dataverse schema change needed.

---

## Related Projects

- **ai-sprk-chat-context-awareness-r1** — Phase 1 (prerequisite, merged ✅): Created `ChatContextMappingService`, `sprk_aichatcontextmap` entity, base context mapping endpoint.
- **ai-sprk-chat-workspace-analysis-r1** — Adjacent project on `work/ai-sprk-chat-workspace-analysis-r1` (1 unmerged commit, low overlap risk).

---

*This file should be kept updated throughout project lifecycle.*
