# Current Task State

> **Auto-updated by task-execute and context-handoff skills**
> **Last Updated**: 2026-06-07 (wave 2 complete; awaiting human decisions on escalation + environment coordination)
> **Protocol**: [Context Recovery](../../docs/procedures/context-recovery.md)

---

## Quick Recovery (READ THIS FIRST)

<!-- This section is for FAST context restoration after compaction -->
<!-- Must be readable in < 30 seconds -->

| Field | Value |
|-------|-------|
| **Task** | 013 — LegalWorkspace legacy to-do refactor |
| **Step** | Context loaded; starting Step 1 (inventory) |
| **Status** | FULL rigor. 13 files surveyed; planning complete. About to execute. |
| **Next Action** | Step 1: Inventory legacy refs in LegalWorkspace (done — 13 files). Then begin refactor: types → queryHelpers → DataverseService → hooks → SmartToDo widget → CreateTodo → CreateWorkAssignment → ActivityFeed → QuickSummary → xrmProvider. |

### Files Modified This Session
<!-- Only files touched in CURRENT session, not all time -->
- `CLAUDE.md` — Modified (task 084: fixed §10 line 183 stale ADR-030 link → ADR-032)
- `src/server/api/Sprk.Bff.Api/Program.cs` — Modified (task 018: registered TodoSync module)
- `src/server/api/Sprk.Bff.Api/appsettings.template.json` — Modified (task 018: added `Spaarke:Graph:TodoSync:Enabled: false`)
- `src/server/api/Sprk.Bff.Api/Services/Todo/*` (13 files) — Created (task 018: 4 interfaces + 1 enum + 4 NullObject impls + 4 Placeholder impls)
- `src/server/api/Sprk.Bff.Api/Infrastructure/DI/TodoSyncModule.cs` — Created (task 018: feature-gated DI with unconditional binding per ADR-032)
- `tests/unit/Sprk.Bff.Api.Tests/Services/Todo/TodoSyncModuleTests.cs` — Created (task 018: 14 tests, all pass)
- `projects/smart-todo-decoupling-r3/notes/eventtodo-reference-audit.md` — Created (task 001: legacy-reference audit, 4 escalation items flagged)
- `projects/smart-todo-decoupling-r3/tasks/TASK-INDEX.md` — Modified (statuses 001/018/084 → ✅)

### Critical Context

**Waves 1+2 complete (4 tasks)**: 001 audit + 010 Kanban hoist (18 tests pass; 3 packages build clean) + 018 Null-Object scaffolding (6103 tests pass; publish-size -0.15 MB) + 084 CLAUDE.md drift fix.

**Audit escalation resolved (user decisions, 2026-06-07)**:
1. **`TodoGenerationService.cs`**: option (a) refactor to write `sprk_todo`. → **New task 006** added.
2. **`ExternalAccess` BFF + `external-spa`**: included in scope, refactor/migrate. → **New tasks 007 + 008** added.
3. **Environment**: target = `https://spaarkedev1.crm.dynamics.com/` (dev). **Portability mandate**: nothing hardcoded — this is a product; schema + code must work in any tenant via solution export/import + config-driven endpoints.
4. **AAD `Tasks.ReadWrite` scope (task 015)**: user will add the scope — instructions provided.
5. **Worktree setup**: `node_modules/` missing at root → both wave 1 + wave 2 commits used `--no-verify` per user authorization. Tooling fix pending (or accept `--no-verify` for this branch).

**TASK-INDEX updated**: Phase 1 expanded from 5 → 8 tasks. Total project tasks: 38 → 41. Tasks 004 + 005 now depend on 006/007/008 completion.

---

## Active Task (Full Details)

| Field | Value |
|-------|-------|
| **Task ID** | none |
| **Task File** | — |
| **Title** | — |
| **Phase** | — |
| **Status** | not-started |
| **Started** | — |

---

## Progress

### Completed Steps

*No steps completed yet*

### Current Step

*No active step*

### Files Modified (All Task)

*No files modified yet*

### Decisions Made

*No decisions recorded yet*

---

## Next Action

**Next Step**: Begin Phase 1 — Dataverse schema work

**Pre-conditions**:
- Confirm target Dataverse environment (dev) with environment owner
- Confirm `Tasks.ReadWrite` delegated scope can be added to AAD app (D-1) — does not gate Phase 1 but should be initiated early
- Confirm Modern UCI app id (`Spaarke:ModelDrivenApps:DefaultAppId`) (UQ-2 / D-4)

**Key Context**:
- ADR-024 governs the multi-entity resolution pattern — `sprk_todo` MUST follow `sprk_communication` shape exactly (11 specific lookups + 4 resolver fields)
- Pre-release rule: no compat shims, no data migration (per FR-29 + OS-1 / OS-2)
- Spec [FR-01](spec.md#schema--data-model) defines the full attribute set

**Expected Output**:
- `sprk_todo` entity present in target environment
- `sprk_eventtodo` deleted
- Four `sprk_todo*` fields removed from `sprk_event` (keep `sprk_priorityscore`, `sprk_effortscore`, `sprk_duedate`)
- `sprk_recordtype_ref` row for `sprk_todo` added

---

## Blockers

**Status**: None

---

## Session Notes

### Current Session
- Started: 2026-06-07
- Focus: Project initialization via `/project-pipeline`

### Key Learnings

- 2026-06-07: Spec.md cited `ADR-030-bff-nullobject-kill-switch.md`, but the file is actually `ADR-032-bff-nullobject-kill-switch.md` (`ADR-030` is `pane-event-bus`). All four spec references were corrected to ADR-032. `CLAUDE.md` §10 line 183 has the same stale link (tracked separately as a cleanup task in this project).
- 2026-06-07: `ADR-009` is `redis-caching.md`, not `graph-obo-token-caching.md`. The OBO + token-caching content for Graph lives in `ADR-028-spaarke-auth-architecture.md`. Use ADR-028 for OBO compliance, not ADR-009.

### Handoff Notes

*No handoff notes*

---

## Quick Reference

### Project Context
- **Project**: smart-todo-decoupling-r3
- **Project CLAUDE.md**: [`CLAUDE.md`](./CLAUDE.md)
- **Task Index**: [`tasks/TASK-INDEX.md`](./tasks/TASK-INDEX.md)

### Applicable ADRs
- ADR-001 (`.claude/adr/ADR-001-minimal-api.md`) — minimal-API pattern for new BFF endpoints (Graph webhook, sync trigger)
- ADR-008 (`.claude/adr/ADR-008-endpoint-filters.md`) — endpoint filters for authorization on webhook
- ADR-024 (`.claude/adr/ADR-024-polymorphic-resolver-pattern.md`) — multi-entity resolution rules; binding for `sprk_todo` regarding shape
- ADR-028 (`.claude/adr/ADR-028-spaarke-auth-architecture.md`) — OBO + token caching for `Tasks.ReadWrite`
- ADR-032 (`.claude/adr/ADR-032-bff-nullobject-kill-switch.md`) — Null-Object pattern; Graph sync feature-gate (FR-26)

### Knowledge Files Loaded

*Loaded per task by task-execute*

---

## Recovery Instructions

**To recover context after compaction or new session:**

1. **Quick Recovery**: Read the "Quick Recovery" section above (< 30 seconds)
2. **If more context needed**: Read Active Task and Progress sections
3. **Load task file**: `tasks/{task-id}-*.poml`
4. **Load knowledge files**: From task's `<knowledge>` section
5. **Resume**: From the "Next Action" section

**Commands**:
- `/project-continue` — Full project context reload + master sync
- `/context-handoff` — Save current state before compaction
- "where was I?" — Quick context recovery

**For full protocol**: See [docs/procedures/context-recovery.md](../../docs/procedures/context-recovery.md)

---

*This file is the primary source of truth for active work state. Keep it updated.*
