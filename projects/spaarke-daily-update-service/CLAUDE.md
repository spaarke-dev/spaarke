# Spaarke Daily Update Service - AI Context

> **Purpose**: This file provides context for Claude Code when working on spaarke-daily-update-service.
> **Always load this file first** when working on any task in this project.

---

## Project Status

- **Phase**: Planning
- **Last Updated**: 2026-03-30
- **Current Task**: Not started
- **Next Action**: Execute task 001

---

## Quick Reference

### Key Files
- [`spec.md`](spec.md) - Original design specification (permanent reference)
- [`README.md`](README.md) - Project overview and graduation criteria
- [`plan.md`](plan.md) - Implementation plan and WBS
- [`current-task.md`](current-task.md) - **Active task state** (for context recovery)
- [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) - Task tracker

### Project Metadata
- **Project Name**: spaarke-daily-update-service
- **Type**: BFF API + Code Page + Playbook Extension
- **Complexity**: High (backend services + frontend Code Page + playbook engine extension + cleanup)

---

## Context Loading Rules

When working on this project, Claude Code should:

1. **Always load this file first** when starting work on any task
2. **Check current-task.md** for active work state (especially after compaction/new session)
3. **Reference spec.md** for design decisions, requirements, and acceptance criteria
4. **Load the relevant task file** from `tasks/` based on current work
5. **Apply ADRs** relevant to the technologies used (loaded automatically via adr-aware)

**Context Recovery**: If resuming work, see [Context Recovery Protocol](../../docs/procedures/context-recovery.md)

---

## 🚨 MANDATORY: Task Execution Protocol

**ABSOLUTE RULE**: All task work MUST use the `task-execute` skill. DO NOT read POML files directly and implement manually.

### Auto-Detection Rules (Trigger Phrases)

When you detect these phrases from the user, invoke task-execute skill:

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

When tasks can run in parallel (no dependencies), each task MUST still use task-execute:
- Send one message with multiple Skill tool invocations
- Each invocation calls task-execute with a different task file
- Example: Tasks 020, 021, 022 in parallel → Three separate task-execute calls in one message

### 🚨 MUST: Multi-File Work Decomposition

**For tasks modifying 4+ files, Claude Code MUST:**

1. **Decompose into dependency graph** — group files by module/component
2. **Delegate to subagents in parallel where safe** — one subagent per module
3. **Parallelize when** files are in different modules with no shared interfaces
4. **Serialize when** files have tight coupling

---

## Key Technical Constraints

- MUST use native `appnotification` entity — no custom notification table
- MUST use existing `PlaybookOrchestrationService` for playbook execution
- MUST register `CreateNotificationNodeExecutor` via `NodeExecutorRegistry[ActionType]`
- MUST use `sprk_playbooktype` field (Notification = 2) — no new schema fields
- MUST store schedule config in `sprk_configjson` — no new schema fields
- MUST implement opt-out subscription model (all playbooks active by default)
- MUST implement idempotency check before creating notifications
- MUST use BackgroundService for scheduler — no Azure Functions (ADR-001)
- MUST use Code Page (React 19, Vite) for Daily Digest — not PCF (ADR-006)
- MUST use Fluent UI v9 exclusively with semantic tokens; dark mode required (ADR-021)
- MUST extend BFF for AI briefing endpoint — no separate service (ADR-013)
- MUST register services via feature module; DI ≤15 lines (ADR-010)
- MUST use `@spaarke/ui-components` shared library for digest UI (ADR-012)

## Existing Code Patterns to Follow

| Pattern | Reference File |
|---------|---------------|
| Node executor implementation | `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/CreateTaskNodeExecutor.cs` |
| Executor registration | `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/NodeExecutorRegistry.cs` |
| Playbook orchestration | `src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookOrchestrationService.cs` |
| Run context | `src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookRunContext.cs` |
| Canvas types | `src/client/code-pages/PlaybookBuilder/src/types/playbook.ts` |
| Code Page pattern | `src/solutions/CreateMatterWizard/` (thin wrapper, Vite build) |
| LegalWorkspace (remove panel) | `src/solutions/LegalWorkspace/src/components/NotificationPanel/` |

---

## Decisions Made

*No decisions recorded yet*

---

## Implementation Notes

*No notes yet*

---

## Resources

### Applicable ADRs
- **ADR-001**: Minimal API + BackgroundService (scheduler must be BackgroundService)
- **ADR-006**: Code Page for Daily Digest (React 19, not PCF)
- **ADR-010**: DI minimalism (feature module, ≤15 lines)
- **ADR-012**: Shared component library for digest UI
- **ADR-013**: AI briefing extends BFF (no separate service)
- **ADR-021**: Fluent UI v9, semantic tokens, dark mode

### Related Projects
- None currently

### External Documentation
- `docs/architecture/playbook-architecture.md` — Playbook engine architecture
- `docs/guides/PLAYBOOK-DESIGN-GUIDE.md` — Playbook design guide

---

*This file should be kept updated throughout project lifecycle*
