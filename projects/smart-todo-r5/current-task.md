# Current Task State — Smart To Do R5

> **Last Updated**: 2026-08-15 (by context-handoff)
> **Recovery**: Read "Quick Recovery" first. New session will begin task execution.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | none active — project fully initialized, ready for execution |
| **Step** | Pipeline complete (artifacts + 28 POML tasks + branch pushed) |
| **Status** | ready-to-execute (operator-gated) |
| **Next Action** | In the new session, say **"work on task 001"** (invokes `task-execute` on `tasks/001-absorb-pr508-boundary-fix.poml`). To parallelize, say **"start group P"** (runs 001 + 010 concurrently). |

### Critical Context
`/design-to-spec` + `/project-pipeline` are DONE. Branch `work/smart-todo-r5` is committed (`f98353bc0`) and pushed. 28 tasks across 7 phases; all decisions locked (see `CLAUDE.md` → "Decisions Made"). Start at task 001 (the critical-path serial spine 001→002→003). Nothing is in-progress; no uncommitted work.

### Files/State this session (all committed)
- `spec.md`, `design.md` — refined; #508 absorbed into FR-01
- `plan.md`, `README.md`, `CLAUDE.md`, `TASK-INDEX.md` — generated
- `tasks/*.poml` — 28 tasks (all XML-valid, full field set)
- `projects/INDEX.md` — R5 row added (BFF=N, SpaarkeAi=Y)

---

## Full State (Detailed)

### Where execution begins
- **Entry task**: `001` — Absorb PR #508 boundary fix on `Spaarke.SmartTodo.Components` (`opus/xhigh`, gate startable).
- **Parallel group P**: 001 + 010 (schema columns) may run concurrently.
- **Serial spines**: 001→002→003 (Phase 1 hoist) · 010→011 (schema→handler) · 030→031→032→033 (modal) · 050→051→052 (ribbon).
- **High-power tasks** (`opus/xhigh`): 001 (package boundary), 002 (13-file hoist), 033 (Save&Close/OOB-dialog coordination). All else `sonnet/high`.
- Full registry + parallel groups + critical path: [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md).

### Locked decisions (also in CLAUDE.md)
- F-5 effort = **Option B** (Low→25…Very High→100, None→50); formula unchanged.
- R-9 ribbon (5 entities) = **in R5** (Phase 6). U-3 overflow = Settings→ThresholdSettings / Layout→orientation toggle / Refresh→reload.
- RegardingResolver = sole canonical resolver (AssociationResolver retired — do NOT resurrect).
- F-7/F-8 modal = **Option 1** (OOB main form, full-cover + hidden header) — ADR-050 **Path A** exception.
- PR #508 absorbed into FR-01/task 001; **close #508 as superseded at wrap-up (task 090)**.

### ⚠️ Codebase-drift reconciliations (verify at task time — code wins per §2)
- `SmartTodoModal.tsx` was DELETED — task 033 targets `openSprkTodoAsLayout1()` / `FeedSyncBridgeHost.handleOpenTodo()`, not the stale interceptor.
- `+ New Task` currently opens `CreateTodoWizard` (FR-10 = real behavior swap; reuse `navigateToEntityRecordSurfaceAsync()`).
- FR-15 (task 034) may already be resolved — no live `RecordNavigationModalShell` consumer; written verify-first.
- Test runner is **Jest** (not vitest); Playwright + axe-core already present (`tests/e2e/`).
- `sprk_todo` form XML is NOT in the repo (Dataverse-hosted) — tasks 013/030/031/032 treat it as an MCP/maker target.

### Coordination
- **Shared-lib contention**: 19 worktrees touch shared libs; `Spaarke.SmartTodo.Components` is hot. Run `/conflict-check` before each PR; small sequential PRs. Overlaps `code-quality-and-assurance-r3` (SpaarkeAi=Y).
- Task 060 edits `.claude/skills/push-to-github` → main-session-only (§3 sub-agent write boundary).

### Steps Completed / Decisions This Task
_(none — no task started yet)_

### Parallel Execution
_(none active)_
