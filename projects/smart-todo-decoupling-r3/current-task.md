# Current Task State

> **Auto-updated by task-execute and context-handoff skills**
> **Last Updated**: 2026-06-07
> **Protocol**: [Context Recovery](../../docs/procedures/context-recovery.md)

---

## Quick Recovery (READ THIS FIRST)

<!-- This section is for FAST context restoration after compaction -->
<!-- Must be readable in < 30 seconds -->

| Field | Value |
|-------|-------|
| **Task** | none |
| **Step** | — |
| **Status** | not-started |
| **Next Action** | Run `/task-execute projects/smart-todo-decoupling-r3/tasks/001-*.poml` to begin Phase 1 (schema work). Phase 1 is sequential and blocks all downstream phases. |

### Files Modified This Session
<!-- Only files touched in CURRENT session, not all time -->

*No files modified yet*

### Critical Context

Project just initialized by `/project-pipeline` on 2026-06-07. Phase 1 begins with Dataverse schema work: create `sprk_todo` custom entity, delete `sprk_eventtodo`, remove four to-do fields from `sprk_event`. Pre-release — no compat shims. Spec's stale ADR-030 references were corrected to ADR-032 during pipeline. CLAUDE.md §10 line 183 has the same stale link — tracked as a low-priority cleanup task in this project.

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
