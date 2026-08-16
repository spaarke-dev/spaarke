# Current Task State — Smart To Do R5

> **Last Updated**: 2026-08-15 (by context-handoff)
> **Recovery**: Read "Quick Recovery" first. New session will begin task execution.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | **7 of 28 DONE**: 001 002 003 010 (Phase 1) + 020 024 042 (Wave A). All committed/pushed. |
| **Step** | Wave A (020/024/042) landed clean via 3 disjoint subagents. **Wave B dispatched: {011, 012, 022}**. |
| **Status** | in-progress — Wave B running (or ready to review) |
| **Next Action** | Review Wave B {011, 012, 022} on return (verify each, run combined build/jest, commit as separate units, mark TASK-INDEX). Then Wave C candidates: **023** (coloring — touches hot `UI.Components/Kanban/KanbanBoard.tsx`, do carefully), **021** (filter pane — needs 020✓; touches queryHelpers ⚠ conflicts w/ 022), **034** (BrowseModal), **013** (RegardingResolver form — LIVE Dataverse, opus, careful/separate). Then Phase-4 modal spine 030→031→032→033 (serial, needs 013), Phase-6 ribbon 050→051→052, deploys 014/025/035, wrap-up 090. |

### Disjoint-partition ledger (subagents share ONE worktree — never co-edit a file)
- **useKanbanColumns.ts** touched by 022 AND 023 → never same wave.
- **queryHelpers.ts** touched by 021 AND 022 → never same wave.
- **RegardingResolverApp.tsx** touched by 042 (done) AND 013-adjacent → 013 is live-form, separate.
- **solutions/SmartTodo/SmartToDo.tsx** touched by 011 (quick-add handleAdd) — distinct from SmartTodoApp.tsx (020, done) and Header (020, done).
- Wave B {011, 012, 022} verified disjoint: 011=UI.Components/CreateTodoWizard+SmartToDo.tsx+webresource · 012=SmartTodo.Components/components/KanbanCard+scorecards · 022=SmartTodo.Components/hooks/useKanbanColumns.ts+queryHelpers.ts.

### Critical Context
Execution underway on branch `work/smart-todo-r5`. **001 complete** (PR#508 boundary fix — barrel imports, package/tsconfig wired, `tsc` green, gates clean). **010 complete by pre-existence** (`sprk_priority`/`sprk_effort` already on live `sprk_todo` with exact spec values — NO schema write). Most tasks are `parallel-safe:false` (shared-lib contention) → critical path runs serially in main session; subagent fan-out reserved for group Q (020/022/023/024) + group R (040/041/042). UI.Components `dist` + both packages' `node_modules` are now built locally (needed for `tsc`).

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

### Steps Completed / Decisions This Task — none (task 011 not started)

Task 003 is complete; see `projects/smart-todo-r5/notes/task-003-shim.md` for full detail (LW `components/SmartToDo/`
is now a 3-file thin shim + `hooks/useSmartToDoBridge.ts`; 10 duplicated files git-rm'd; a 3rd consumer
(`WorkspaceGrid.tsx`'s `LazySmartToDoDialog`) was discovered and required zero edits; a pre-existing `App.tsx`
tsc error — dead `useDialogForDetail` prop — was fixed as part of the conversion).

**Next**: load task 011's POML (`tasks/011-...poml`) + knowledge files (ADR-012, spec.md FR-02/03 sections,
CLAUDE.md "Implementation Notes" re: `sprk_priorityscore`/`sprk_effortscore` already existing) before
implementing the auto-score handler.

### Parallel Execution
_(none active)_
