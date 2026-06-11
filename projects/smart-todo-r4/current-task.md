# Current Task State — smart-todo-r4

> **Last Updated**: 2026-06-11 00:35 (by context-handoff before /compact)
> **Recovery**: Read "Quick Recovery" section first

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Project** | smart-todo-r4 — 7 workstreams (A-G), 31 tasks total |
| **Status** | **13 of 31 tasks ✅** (42% by count; foundation + first implementation wave complete) |
| **PR #377** | ✅ MERGED to master as squash `eed39e40a` (2026-06-11T00:18:47Z) |
| **Worktree branch** | `work/smart-todo-r4-wave2` @ `254302259` (= origin/master tip); old `work/smart-todo-r4` branch + remote deleted post-merge; main repo free to checkout master |
| **Active task** | none — between waves; ready to dispatch next |
| **Working tree** | only this current-task.md modified (resume point); ready for /compact |
| **Next Action** | Dispatch Wave A (6 parallel agents): 031 (B-filter) + 032 (B-actions) + 033 (B-view) + 040 (C-modal) + 070 (F-orient) + 081 (G-Matter form) |
| **Suggested next wave** | 6 parallel agents: 031 (B-filter) + 032 (B-actions) + 033 (B-view) + 040 (C-modal) + 070 (F-orient) + 081 (G-Matter form) |

### Critical context for resume

- All R4 client surfaces from Phases 0 + 1 + Wave 2a + Wave 2a-followups are **live on master**: `<RecordNavigationModalShell>`, 3 toolbar primitives, RichFilePreviewDialog refactor, useLaunchContext extension, RegardingResolver virtual PCF + CREATE-mode bridge, SmartTodo 4-row Header, `@spaarke/smart-todo-components` peer package + LW shim, pre-save JS web resource, 4 chart def JSONs + deploy script.
- **spaarkedev1 deploys are master-only** (durable memory saved at `~/.claude/projects/c--code-files-spaarke-wt-smart-todo-r4/memory/feedback_spaarkedev1-deploy-discipline.md`). Whoever lands LAST among in-flight PRs touching SpaarkeAi / SmartTodo / shared Code Page surfaces does ONE final master-side redeploy. Do NOT propose branch-based deploys.
- Master also includes PR #375 (R6 platform unification phases A+B+partial C) and PR #369 (multi-container-multi-index project scaffolding) — landed concurrently with R4 in same deploy window.

---

## Waves Complete (this session 2026-06-10 → 2026-06-11)

| Wave | Tasks | Outcome |
|---|---|---|
| **G0 Phase 0 audits** | 001, 002, 003, 004 | All binding decisions captured. Decisions: A=Pattern D dual-use; D=virtual PCF (mirrors AssociationResolver precedent); G drill-through payload confirmed via MS Learn + 20+ repo callers; `useLaunchContext` EXISTS (R3 task 070b shipped it; not new build — REPURPOSE + EXTEND). **NEW TASK 034 added** to close R4-003/R4-004 contract gap (envelope handling + openTodos discriminator). |
| **G1+2a hoists/PCF/hook/charts** | 010, 012, 034, 050, 080 | Shared lib hoists (RecordNavigationModalShell + 3 toolbar primitives), RegardingResolver virtual PCF (20/20 tests, 1.56 MiB bundle), useLaunchContext extension (235→471 LOC, 22 tests), 4 chart def JSONs + idempotent PS deploy script. |
| **G1+2a-followups** | 011, 020, 030, 051 | RichFilePreviewDialog refactor (regression-safety pass), SmartToDo widget rebuild via new `@spaarke/smart-todo-components` peer package (Pattern D; FeedTodoSyncContext lifted to LW shim per user decision; agent hit timeout but produced clean buildable work; main session did closeout), SmartTodo 4-row Header layout, form-binding JS Web Resource + 9-section instructions doc. |
| **R4-051 follow-up fix** | (no new task) | PCF CREATE-mode bridge: `RegardingResolverApp.tsx` populates `window.__sprk_regarding_pending__` on CREATE so OnSave handler stages all 5 fields in INSERT transaction. Closes the HIGH-priority follow-up before any deploy. |

---

## Remaining Work — 18 tasks (suggested grouping for next waves)

### Wave proposal A (6 parallel, dispatch FIRST after compact)

| Task | Touches | Dependencies (all ✅) | Estimated |
|---|---|---|---|
| **031** B "Assigned to Me" filter (drop "My Tasks") | `SmartTodo/src/hooks/useTodoItems.ts` + Header | 030 ✅ | 0.5d |
| **032** B selection-aware toolbar actions (Open/Delete/Email/Pin — replaces 030's 4 stubs) | `SmartTodo/src/SmartTodoApp.tsx:115-139` + `Toolbar/ToolbarActions.ts` | 012 ✅, 030 ✅ | 1-1.5d |
| **033** B List/Card view toggle + persistence | `useUserPreferences.ts` (extend) + `ListView/` (new) + Header | 012 ✅, 030 ✅ | 1d |
| **040** C wire SmartTodo card-open path to `<RecordNavigationModalShell>` with To Do main form iframe | `SmartTodo/src/components/Modal/SmartTodoModal.tsx` (new) | 010 ✅, 030 ✅ | 2d |
| **070** F vertical Kanban orientation toggle (CSS transform-only) | `SmartTodo/src/components/KanbanBoard.tsx` | 012 ✅, 030 ✅ | 1-1.5d |
| **081** G Visual Host "Upcoming To Dos" on Matter form (instructions doc + JS + form designer steps for user) | Matter main form (form-designer change) + solution XML | 051 ✅, 080 ✅, 034 ✅ | 0.5d |

**File-ownership care**: 031, 032, 033, 040, 070 all live in `src/solutions/SmartTodo/src/`. R4-030's Header exposes `selectedIds` Set at `SmartTodoLayout:~109` (with `_setSelectedIds` setter ready) and `facets` prop API. 031 wires facets; 032 replaces the 4 stub `onClick` handlers at `SmartTodoApp.tsx:115-139`; 033 mounts `<ViewToggle>` in Header Row 3 trailing edge; 040 wires the Open action's modal launch; 070 modifies `KanbanBoard.tsx` (separate file, lowest conflict risk). If sub-agents conflict on `SmartTodoApp.tsx`, serialize the latter three.

### Wave proposal B (after Wave A lands — up to 6 parallel)

- **041** C cross-frame dirty-check messaging (depends on 040)
- **042** C retire `TodoDetailPanel` side-pane (depends on 040)
- **060** E card affordances: Open icon + double-click + selection checkbox (depends on 012 ✅, 030 ✅, 040)
- **052** D read-only mode for view-only roles (depends on 050 ✅, 051 ✅)
- **071** F orientation persistence via `sprk_userpreference` (depends on 070)
- **082** G Visual Host on Project form (parallel-safe; depends on 051 + 080 + 034 — all ✅)
- **083** G Visual Host on Invoice form (parallel-safe)
- **084** G Visual Host on WorkAssignment form (parallel-safe)

Can dispatch up to 6 — pick: 041, 042, 052, 060, 082, 083 (or any 6 from the list).

### Phase 3 + 4 (final 4 tasks — after Wave A + B)

- **092** Deploy all affected solutions (per project-pipeline deploy convention now: **deploy from master**, not from feature branch)
- **093** UI test suite (NFR-05 modal nav latency, NFR-07 a11y, NFR-08 orientation switch)
- **094** Final `grep -ri sprk_todoflag src/` → 0 functional hits (graduation criterion 12)
- **098** Wrap-up: lessons-learned + README status → Complete + repo-cleanup + final PR

---

## Follow-ups Surfaced (Not Yet Filed As Tasks)

See [`tasks/TASK-INDEX.md` Follow-ups Surfaced section](tasks/TASK-INDEX.md#follow-ups-surfaced-not-yet-filed-as-tasks). Summary:

1. **MEDIUM** — LW Kanban rich-feature hoist into `@spaarke/smart-todo-components` (13-file subtree deferred from R4-020)
2. **LOW** — Shell `chromeMode?` or `RichFilePreview` `suppressTitleBar?` API tweak (R4-011 finding)
3. **LOW** — SmartTodo vitest/jest test runner wiring (pre-existing; 22 useLaunchContext tests are executable-spec shims)
4. **MONITOR** — PR #376 `WidgetErrorBoundary` rewrite landed clean (no follow-up needed; verified via rebase)

---

## Files Modified This Session (post-merge state)

**All committed + merged to master via PR #377 squash commit `eed39e40a`. No uncommitted changes.**

Major new files:
- `src/client/pcf/RegardingResolver/` (entire PCF, ~20 files)
- `src/client/shared/Spaarke.SmartTodo.Components/` (new peer package, ~12 files including node_modules)
- `src/client/shared/Spaarke.UI.Components/src/components/{RecordNavigationModalShell,SelectionAwareToolbar,ViewToggle,OrientationToggle}/` (4 new shared components)
- `src/client/webresources/js/sprk_todo_regarding_presave.js` (form pre-save handler)
- `src/solutions/SmartTodo/src/components/Header/` (4-row layout)
- `infrastructure/dataverse/charts/upcoming-todos-*.json` (4 chart def JSONs)
- `scripts/Create-UpcomingTodosChartDefinitions.ps1`
- `projects/smart-todo-r4/notes/{widget-surface-audit,regarding-resolver-audit,drill-through-spike,launch-context-decision,g-chart-def-deploy-notes,d-form-bind-instructions}.md`

Modified existing files:
- `src/client/pcf/RegardingResolver/RegardingResolver/RegardingResolverApp.tsx` (CREATE-mode bridge)
- `src/client/shared/Spaarke.UI.Components/src/components/{FilePreview/RichFilePreviewDialog.tsx,utils/parseDataParams.ts,index.ts}`
- `src/solutions/SmartTodo/src/{SmartTodoApp.tsx,hooks/useLaunchContext.ts,hooks/__tests__/useLaunchContext.test.ts}`
- `src/solutions/LegalWorkspace/src/sections/todo.registration.ts` (Pattern D shim)
- `src/solutions/LegalWorkspace/package.json` (workspace dep on smart-todo-components)
- All R4 task POMLs marked complete; TASK-INDEX + plan.md + CLAUDE.md updated through 3 wave outcomes

---

## Key Decisions Made This Session

| Date | Decision | Why |
|---|---|---|
| 2026-06-10 | Pattern D dual-use for A widget | R4-001 audit: source CLEAN; rebuild via shared-lib widget + thin LW shim mirrors Calendar canonical R3 task 115 |
| 2026-06-10 | Virtual PCF (40/40 vs 22/40 vs 26/40) for D | R4-002 audit: AssociationResolver v1.1.0 precedent already deployed; same shape |
| 2026-06-10 | REPURPOSE + EXTEND useLaunchContext (don't build new) | R4-004 audit: hook EXISTS (235 LOC + 217 tests from R3 task 070b) — initial discovery missed it |
| 2026-06-10 | New task 034 added | R4-003 + R4-004 surfaced contract gap: VisualHost wraps params in `?data=<envelope>`; raw-key parser needed extension |
| 2026-06-10 | FeedTodoSyncContext lifted to LW section shim | User decision per R4-001 audit recommendation — shared-lib widget stays host-agnostic |
| 2026-06-10 | LW Kanban rich-feature hoist deferred (R4-020 scope decision) | Initial 0.1.0 of `@spaarke/smart-todo-components` ships minimal Pattern D widget satisfying FR-02 + FR-04 + FR-05; 13-file rich-feature subtree deferred to future task |
| 2026-06-10 | PCF CREATE-mode bridge via `window.__sprk_regarding_pending__` global | R4-051 surfaced gap; ~30-line addition to RegardingResolverApp.tsx closes it; landed before PR open |
| 2026-06-10 | spaarkedev1 deploys are master-only (durable convention) | User instruction; saved as feedback memory; last merger does single master-side redeploy |

---

## Quick Reference

### Project files
- Project root: `c:\code_files\spaarke-wt-smart-todo-r4\projects\smart-todo-r4\`
- Spec: `spec.md` (3491 words; binding requirements)
- Plan: `plan.md` (Phase 0 outcomes + Wave G1+2a outcomes + Wave G1+2a-followups outcomes captured)
- CLAUDE.md: project AI context (binding decisions + parallel branch coord)
- TASK-INDEX: 31 tasks with statuses + 5 follow-ups documented

### To resume
1. Quick read: this file's Quick Recovery + Suggested Wave A
2. Verify branch + master sync: `git status` clean (only this file may be ahead), `git fetch origin --prune`, `git rev-list --count HEAD..origin/master` should be 0
3. **If master moved**: `git rebase origin/master` first (no need to delete branch — it was created clean from master tip)
4. Dispatch Wave A (6 parallel agents): 031 + 032 + 033 + 040 + 070 + 081 — use `task-execute` skill per task POML
5. After Wave A returns: build verify (Spaarke.UI.Components + SmartTodo Code Page + LegalWorkspace + RegardingResolver PCF), batch-merge any shared-lib index.ts exports, commit + push, open PR
6. **Do NOT deploy from the branch.** Open PR → coordinate with Ralph → merge → master deploy happens last.

### Commands for resume
```bash
# 1. Verify worktree on wave2 branch
git branch --show-current   # expect: work/smart-todo-r4-wave2

# 2. Master sync check
git fetch origin --prune
git rev-list --count HEAD..origin/master  # expect 0; rebase if non-zero

# 3. Open project task index
# View projects/smart-todo-r4/tasks/TASK-INDEX.md for task list (look for 🔲 markers)
```

---

## Recovery Instructions

**To recover context after compaction or new session:**

1. **Quick Recovery**: Read the table at the top (< 30 seconds)
2. **If more context needed**: Read "Waves Complete this session" + "Remaining Work" sections
3. **Load task POML**: `projects/smart-todo-r4/tasks/{NNN}-*.poml` for the specific task being executed
4. **Resume**: Follow the "To resume" steps in Quick Reference; dispatch Wave A

**Commands:**
- `/project-continue` — full project context reload + master sync
- `/context-handoff` — save state again (next time)
- "where was I?" — quick context recovery

**For full protocol**: See [docs/procedures/context-recovery.md](../../docs/procedures/context-recovery.md)

---

*This file is the primary source of truth for active work state. Keep it updated.*
