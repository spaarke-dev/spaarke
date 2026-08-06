# Current Task State

> **Auto-updated by task-execute and context-handoff skills**
> **Last Updated**: 2026-08-06 14:20
> **Protocol**: [Context Recovery](../../docs/procedures/context-recovery.md)

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | none — project initialized, execution operator-gated |
| **Step** | — |
| **Status** | none |
| **Next Action** | Register portfolio under Epic #427 + add `projects/INDEX.md` row + file NG1 Idea; then Phase 0 task 001 (rubric). Assessments (Phase 1) require operator "use a workflow" opt-in. |

### Files Modified This Session
- Project artifacts generated (README, plan, CLAUDE.md, current-task.md, tasks/, TASK-INDEX.md)

### Critical Context
Standing quality program, single worktree, surfaces = workstreams. Assessment-first (Fable-verified, gating). BFF workstream already assessed. Initialize-only: no task auto-executed.

---

## Active Task (Full Details)

| Field | Value |
|-------|-------|
| **Task ID** | none |
| **Task File** | — |
| **Title** | — |
| **Phase** | — |
| **Status** | none |
| **Started** | — |

---

## Progress

### Completed Steps
*No steps completed yet*

### Current Step
*No active task*

### Files Modified (All Task)
*No files modified yet*

### Decisions Made
- 2026-08-06: Finance auth via `@spaarke/auth` (not HMAC) — Reason: owner directive, canonical ADR-028
- 2026-08-06: Assessment-first (Fable-verified) is the gating deliverable — Reason: can't task remediation against un-verified findings
- 2026-08-06: Initialize-only (no auto-execute) — Reason: operator opt-in required for Workflow assessments

---

## Next Action

**Next Step**: Portfolio registration (Epic #427) + INDEX row + NG1 Idea, then Phase 0 task 001 (rubric authoring).

**Pre-conditions**: Epic #427 exists; no orphan R3 Issue.

**Key Context**:
- Refer to `spec.md` FR-01..FR-04 for Phase 0 deliverables
- Refer to `design.md` §5 (rubric D1–D11) + §6 (assessment method)

**Expected Output**: `docs/standards/CODE-QUALITY-RUBRIC.md`, `notes/SCORECARD.md`, `quality-assessment` Workflow, portfolio Issue.

---

## Blockers

**Status**: None

---

## Session Notes

### Current Session
- Started: 2026-08-06
- Focus: Project initialization via /design-to-spec → /project-pipeline (initialize-only)

### Key Learnings
*None yet*

### Handoff Notes
See [`notes/SESSION-HANDOFF.md`](notes/SESSION-HANDOFF.md) for the read-first program handoff.

---

## Quick Reference

### Project Context
- **Project**: code-quality-and-assurance-r3
- **Project CLAUDE.md**: [`CLAUDE.md`](./CLAUDE.md)
- **Task Index**: [`tasks/TASK-INDEX.md`](./tasks/TASK-INDEX.md)

### Applicable ADRs
- ADR-028: Spaarke Auth v2 — all client→BFF auth via `@spaarke/auth`
- ADR-013: AI facade — CRUD uses `PublicContracts/`
- ADR-032: Null-object kill-switch — preserve verified seams
- ADR-038: Testing — KEEP categories, coverage = observation
- ADR-010 / ADR-022 / ADR-002

### Knowledge Files Loaded
- `.claude/constraints/bff-extensions.md`, `docs/assessments/bff-ai-extraction-assessment-2026-05-20.md`

---

## Recovery Instructions

1. **Quick Recovery**: Read the "Quick Recovery" section above (< 30 seconds)
2. **If more context needed**: Read `notes/SESSION-HANDOFF.md` + `spec.md`
3. **Load task file**: `tasks/{task-id}-*.poml`
4. **Resume**: From the "Next Action" section

**Commands**: `/project-continue`, `/context-handoff`, "where was I?"

---

*This file is the primary source of truth for active work state. Keep it updated.*
