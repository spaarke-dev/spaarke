# Current Task State

> **Auto-updated by task-execute and context-handoff skills**
> **Last Updated**: 2026-07-16
> **Protocol**: [Context Recovery](../../docs/procedures/context-recovery.md)

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | none — pipeline just completed |
| **Step** | — |
| **Status** | none (not-started) |
| **Next Action** | Run `task-execute` on **task 001** (`tasks/001-phase0-communication-schema-audit.poml`) to begin Wave 0 |

### Files Modified This Session
- Pipeline initialization only (README, plan, CLAUDE, current-task, tasks/)

### Critical Context
Project initialized via `/project-pipeline` 2026-07-16. 28 tasks across 9 waves + wrap-up. Wave 0 = Phase-0 spikes (ACS, schema, private-grant) + schema/enum/ADR foundation. This project edits shared `Services/Communication/` code (task 040) — run `/conflict-check` before every BFF wave.

---

## Active Task (Full Details)

| Field | Value |
|-------|-------|
| **Task ID** | none |
| **Task File** | — |
| **Title** | — |
| **Phase** | W0 — Phase 0 Verification + Foundation |
| **Status** | none |
| **Started** | — |

---

## Progress

### Completed Steps

*No steps completed yet — pipeline initialization only.*

### Current Step

*No active task.*

### Files Modified (All Task)

*No task files modified yet.*

### Decisions Made

- 2026-07-16: Grouping key LOCKED = (A) `sprk_communicationthread` entity + `sprk_thread` lookup — Reason: queryable grouping; home for thread privacy/participants/ACS thread id (design Q4).
- 2026-07-16: UI = OOB main form + PCFs (ADR-026 Path-A exception) — Reason: mirrors email-r4 W4 pivot, lowest-risk proven pattern.
- 2026-07-16: `CommunicationType.Message = 100000004` (Dataverse choice exists) — Reason: enum extension, not `TeamsMessage`.

---

## Next Action

**Next Step**: Begin task 001 via `task-execute`.

**Pre-conditions**:
- Dataverse MCP connected (for schema read).
- `/conflict-check` run at project start (shared `Services/Communication/`).

**Key Context**:
- Wave 0 spikes (001/002/003) de-risk the net-new ACS + private-grant areas before build.
- ADR-045 (channel seams), ADR-034 (membership), ADR-028 (auth) apply from the first BFF task.

**Expected Output**:
- Task 001: confirmed live `sprk_communication` schema delta + `Message` choice integer, recorded in `notes/spikes/`.

---

## Blockers

**Status**: None

---

## Session Notes

### Current Session
- Started: 2026-07-16
- Focus: Project pipeline initialization (complete)

### Key Learnings

*None yet.*

### Handoff Notes

*No handoff notes.*

---

## Quick Reference

### Project Context
- **Project**: messaging-communication-app-r1
- **Project CLAUDE.md**: [`CLAUDE.md`](./CLAUDE.md)
- **Task Index**: [`tasks/TASK-INDEX.md`](./tasks/TASK-INDEX.md)

### Applicable ADRs
- ADR-045: Communication channel seams — the spine this extends
- ADR-046: ACS messaging channel — authored in this project (task 007)
- ADR-034: Membership resolver — open-thread membership
- ADR-028: Auth v2 — server-side token minting; central credential

### Knowledge Files Loaded
- `.claude/constraints/bff-extensions.md` — BFF-addition checklist

---

*This file is the primary source of truth for active work state. Keep it updated.*
