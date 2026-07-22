# Current Task State

> **Auto-updated by task-execute and context-handoff skills**
> **Last Updated**: 2026-07-22 (pipeline init)
> **Protocol**: [Context Recovery](../../docs/procedures/context-recovery.md)

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | none — project initialized, no task started yet |
| **Step** | — |
| **Status** | not-started |
| **Next Action** | Say "continue" or "work on task 001" → invoke `task-execute` with `tasks/001-shadow-document-adr.poml` |

### Files Modified This Session
- Project artifacts created by `/project-pipeline` (README, plan, CLAUDE.md, current-task, tasks/, TASK-INDEX)

### Critical Context
R4 is a MISSION-CRITICAL hard-replace of the Compose save layer with a Shadow Document Architecture. **Phase 0 (tasks 001–006) is a proof gate** that MUST be green before any old-path deletion (023/032/060). Start at task 001.

---

## Active Task (Full Details)

| Field | Value |
|-------|-------|
| **Task ID** | none |
| **Task File** | — (first task: `tasks/001-shadow-document-adr.poml`) |
| **Title** | — |
| **Phase** | Ready to start Phase 0 (Gate) |
| **Status** | none |
| **Started** | — |

---

## Progress

### Completed Steps
*No steps completed yet*

### Current Step
*No task started — project just initialized.*

### Files Modified (All Task)
*No files modified yet*

### Decisions Made
- 2026-07-22: Cutover = hard replace (owner-confirmed); Phase 0 proof gate is the pre-commit safety net.
- 2026-07-22: D1–D5 locked; anchor = `(paraId, runIndex, run-local-offset)`.

---

## Next Action

**Next Step**: Start task 001 (Shadow-Document ADR) via `task-execute`.

**Pre-conditions**:
- On branch `work/spaarkeai-compose-r4` (already checked out).
- Read `spec.md`, `design.md`, and `notes/as-built-inventory.md` before implementation tasks.

**Key Context**:
- Phase 0 gate (task 006) blocks all cutover/deletion tasks.
- BFF=Y — every BFF task: Placement Justification + publish-size + seam slice + `/conflict-check`.

**Expected Output**:
- Task 001 produces the R4 Shadow-Document ADR (`.claude/adr/`) + the ADR-Tension Path-B amendment of the R3 paragraph-diff decision.

---

## Blockers

**Status**: None

---

## Session Notes

### Current Session
- Started: 2026-07-22 (pipeline init)
- Focus: Project initialized via `/project-pipeline`.

### Key Learnings
- `Services/Compose/` overlaps 4 sibling projects — `/conflict-check` before every BFF PR.
- Consume `Services/Ai/PublicContracts/` seams; NO fork of `Services/Ai/`.

### Handoff Notes
*No handoff notes*

---

## Quick Reference

### Project Context
- **Project**: spaarkeai-compose-r4
- **Project CLAUDE.md**: [`CLAUDE.md`](./CLAUDE.md)
- **Task Index**: [`tasks/TASK-INDEX.md`](./tasks/TASK-INDEX.md)

### Applicable ADRs
- ADR-013: AI facade — no AI internals in `Services/Compose/`
- ADR-007: no `Microsoft.Graph` above `SpeFileStore`
- ADR-038: seam DoD; banned mock/DI/ctor tests
- ADR-039/040: engine frozen; no new AI dispatch

### Knowledge Files Loaded
*Loaded per-task by task-execute from each POML's `<knowledge>` section.*

---

## Recovery Instructions

1. **Quick Recovery**: Read the "Quick Recovery" section above.
2. **Load task file**: `tasks/{task-id}-*.poml`.
3. **Load knowledge files**: From the task's `<knowledge>` section.
4. **Resume**: From "Next Action".

**Commands**: `/project-continue` · `/context-handoff` · "where was I?"

---

*This file is the primary source of truth for active work state. Keep it updated.*
