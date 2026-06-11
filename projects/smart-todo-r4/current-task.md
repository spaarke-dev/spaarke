# Current Task State — smart-todo-r4

> **Last Updated**: 2026-06-11 (post-Wave-A, all 6 tasks landed on `work/smart-todo-r4-wave2`)
> **Recovery**: Read "Quick Recovery" section first

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Project** | smart-todo-r4 — 7 workstreams (A-G), 31 tasks total |
| **Status** | **19 of 31 tasks ✅** (61% by count; foundation + Wave 2a + Wave 2a-followups + Wave A all complete) |
| **PR #377** | ✅ MERGED to master as squash `eed39e40a` (Phases 0 + 1 + Wave 2a + followups; 13 tasks) |
| **Worktree branch** | `work/smart-todo-r4-wave2` @ `54fb0d541` (Wave A: +6 tasks) — pushed to origin |
| **Active task** | none — between waves; ready to dispatch Wave B |
| **Working tree** | clean |
| **Next Action** | Dispatch Wave B (up to 8 parallel agents — see "Wave proposal B" table below) |
| **PR for Wave A** | Not yet opened — recommended NEXT: open PR for `work/smart-todo-r4-wave2` → master so Wave B can rebase off a clean post-merge master. OR continue Wave B on same branch and open one combined PR. **User decision required.** |

### Critical context for resume

- All R4 client surfaces from Phases 0 + 1 + Wave 2a + Wave 2a-followups are **live on master** via PR #377. Wave A surfaces are **on `work/smart-todo-r4-wave2` only** (commit `54fb0d541`) — NOT yet on master.
- **spaarkedev1 deploys are master-only** (durable memory saved at `~/.claude/projects/c--code-files-spaarke-wt-smart-todo-r4/memory/feedback_spaarkedev1-deploy-discipline.md`). Whoever lands LAST among in-flight PRs touching SpaarkeAi / SmartTodo / shared Code Page surfaces does ONE final master-side redeploy. Do NOT propose branch-based deploys.
- Master also includes PR #375 (R6 platform unification phases A+B+partial C) and PR #369 (multi-container-multi-index project scaffolding) — landed concurrently with R4 in same deploy window.
- **Known pre-existing condition from PR #369**: `@spaarke/sdap-client` rootDir cascade in `Spaarke.UI.Components/EntityCreationService.ts` breaks Vite builds of every code-page consuming `@spaarke/ui-components`. Workaround = vite alias to source. NOW APPLIED to: SmartTodo (Wave A task 040), LegalWorkspace (Wave A main-session reconciliation), CreateMatter/Project/Event/WorkAssignmentWizard (prior). Project-wide fix (tsconfig refs) deferred to task 092/098.

---

## Waves Complete (this session 2026-06-10 → 2026-06-11)

| Wave | Tasks | Outcome |
|---|---|---|
| **G0 Phase 0 audits** | 001, 002, 003, 004 | All binding decisions captured. Decisions: A=Pattern D dual-use; D=virtual PCF (mirrors AssociationResolver precedent); G drill-through payload confirmed via MS Learn + 20+ repo callers; `useLaunchContext` EXISTS (R3 task 070b shipped it; not new build — REPURPOSE + EXTEND). **NEW TASK 034 added** to close R4-003/R4-004 contract gap (envelope handling + openTodos discriminator). |
| **G1+2a hoists/PCF/hook/charts** | 010, 012, 034, 050, 080 | Shared lib hoists (RecordNavigationModalShell + 3 toolbar primitives), RegardingResolver virtual PCF (20/20 tests, 1.56 MiB bundle), useLaunchContext extension (235→471 LOC, 22 tests), 4 chart def JSONs + idempotent PS deploy script. |
| **G1+2a-followups** | 011, 020, 030, 051 | RichFilePreviewDialog refactor (regression-safety pass), SmartToDo widget rebuild via new `@spaarke/smart-todo-components` peer package (Pattern D; FeedTodoSyncContext lifted to LW shim per user decision; agent hit timeout but produced clean buildable work; main session did closeout), SmartTodo 4-row Header layout, form-binding JS Web Resource + 9-section instructions doc. |
| **R4-051 follow-up fix** | (no new task) | PCF CREATE-mode bridge: `RegardingResolverApp.tsx` populates `window.__sprk_regarding_pending__` on CREATE so OnSave handler stages all 5 fields in INSERT transaction. Closes the HIGH-priority follow-up before any deploy. |
| **Wave A (post-/compact)** | 031, 032, 033, 040, 070, 081 | 6 parallel agents on `work/smart-todo-r4-wave2`. All returned clean with builds green. 031 "Assigned to Me" filter + MyTasksFilter deletion; 032 selection-aware toolbar (Open/Delete/Email/Pin); 033 List/Card view toggle + persistence; 040 SmartTodo modal wire-up (`<RecordNavigationModalShell>` + iframe OOB form); 070 vertical Kanban orientation (CSS flex-direction); 081 Matter form Visual Host instructions doc. Main-session reconciliation: deduped `OPEN_TODOS_EVENT` between 032 + 040 (032 canonical); added `@spaarke/sdap-client` vite alias to LegalWorkspace (PR #369 cascade unblock). Commit `54fb0d541`. |

---

## Remaining Work — 12 tasks (suggested grouping for next waves)

### Wave proposal B (dispatch NEXT — up to 8 parallel)

| Task | Touches | Dependencies (all ✅) | Estimated | Parallel risk |
|---|---|---|---|---|
| **041** C cross-frame dirty-check messaging | iframe `postMessage` protocol + `<RecordNavigationModalShell>` wiring | 040 ✅ | 1d | Low — separate concern |
| **042** C retire `TodoDetailPanel` side-pane (FR-18) | `SmartTodoApp.tsx` cleanup + `TodoDetailPanel/` removal | 040 ✅ | 0.5d | **HIGH on `SmartTodoApp.tsx`** — should land BEFORE 041 to simplify diff, or serialize against it |
| **060** E card affordances: Open icon + double-click + selection checkbox | `SmartTodo/src/components/SmartToDoCard.tsx` + `KanbanBoard.tsx` | 012 ✅, 030 ✅, 040 ✅ | 1d | Medium — touches card subtree |
| **052** D read-only mode for view-only roles | `RegardingResolver` PCF + pre-save handler gate | 050 ✅, 051 ✅ | 0.5-1d | Low — PCF is separate |
| **071** F orientation persistence via `sprk_userpreference` | `useUserPreferences.ts` (extend further) + `SmartToDo.tsx` | 070 ✅, 033 ✅ | 0.5d | **HIGH overlap with 033's useUserPreferences viewMode field — read 033's final hook shape before extending** |
| **082** G Visual Host on Project form (clone 081 doc) | new instructions doc | 051 ✅, 080 ✅, 034 ✅ | 0.25d | None |
| **083** G Visual Host on Invoice form (clone 081 doc) | new instructions doc | 051 ✅, 080 ✅, 034 ✅ | 0.25d | None |
| **084** G Visual Host on WorkAssignment form (clone 081 doc) | new instructions doc | 051 ✅, 080 ✅, 034 ✅ | 0.25d | None |

**Dispatch recommendation**: 042 + 041 should be serialized (042 first, then 041 — both touch the modal subtree). 071 should be serialized against any other useUserPreferences-touching work. The four G-tasks (082/083/084 plus optionally 081 redo if needed) are zero-conflict and fully parallel-safe. Suggested wave: dispatch 6 parallel = 042 + 060 + 052 + 082 + 083 + 084, then 041 + 071 in a small follow-up wave.

### Phase 3 + 4 (final 4 tasks — after Wave B)

- **092** Deploy all affected solutions (per project-pipeline deploy convention now: **deploy from master**, not from feature branch) — also fold in project-wide tsconfig refs fix for `@spaarke/sdap-client` cascade
- **093** UI test suite (NFR-05 modal nav latency, NFR-07 a11y, NFR-08 orientation switch)
- **094** Final `grep -ri sprk_todoflag src/` → 0 functional hits (graduation criterion 12)
- **098** Wrap-up: lessons-learned + README status → Complete + repo-cleanup + final PR

---

## Follow-ups Surfaced (Not Yet Filed As Tasks)

See [`tasks/TASK-INDEX.md` Follow-ups Surfaced section](tasks/TASK-INDEX.md#follow-ups-surfaced-not-yet-filed-as-tasks). Summary:

1. **MEDIUM** — LW Kanban rich-feature hoist into `@spaarke/smart-todo-components` (13-file subtree deferred from R4-020)
2. **LOW** — Shell `chromeMode?` or `RichFilePreview` `suppressTitleBar?` API tweak (R4-011 finding)
3. **LOW** — SmartTodo vitest/jest test runner wiring (pre-existing; 22 useLaunchContext tests + 12+ Toolbar tests + 12+ Modal tests are executable-spec shims)
4. **MEDIUM (NEW from Wave A)** — Project-wide `@spaarke/sdap-client` rootDir fix: tsconfig `composite` + `references` so consumers don't need per-config vite alias workarounds. Currently aliased in 5 code-pages (4 wizards + SmartTodo + LegalWorkspace). Belongs in task 092 or 098.
5. **MONITOR** — PR #376 `WidgetErrorBoundary` rewrite landed clean (no follow-up needed; verified via rebase)

---

## Files Modified This Session (post-Wave-A state)

**Phases 0 + 1 + Wave 2a + Wave 2a-followups all merged to master via PR #377 squash `eed39e40a`. Wave A is on `work/smart-todo-r4-wave2` (commit `54fb0d541`) and pushed to origin but NOT yet merged.**

### Wave A new files (commit `54fb0d541`)

- `src/solutions/SmartTodo/src/components/Toolbar/{ToolbarActions.ts,index.ts,__tests__/ToolbarActions.test.ts}` — Open/Delete/Email/Pin action factory + tests (task 032)
- `src/solutions/SmartTodo/src/components/Modal/{SmartTodoModal.tsx,buildTodoIframeUrl.ts,index.ts,__tests__/buildTodoIframeUrl.test.ts}` — `<RecordNavigationModalShell>` + iframe wrapper + URL builder + tests (task 040)
- `src/solutions/SmartTodo/src/components/ListView/{ListView.tsx,ListView.styles.ts,index.ts}` — dense Fluent v9 table view (task 033)
- `projects/smart-todo-r4/notes/g-matter-form-visualhost-instructions.md` — 10-section maker doc (task 081)

### Wave A modified files (commit `54fb0d541`)

- `src/client/shared/Spaarke.UI.Components/src/components/Kanban/{KanbanBoard.tsx,types.ts,index.ts,__tests__/KanbanBoard.test.tsx}` — orientation prop + Griffel vertical rules + 3 tests (task 070)
- `src/solutions/SmartTodo/src/SmartTodoApp.tsx` — App-level wiring for all 4 SmartTodoApp-touching tasks (031 filter, 032 toolbar, 033 view mode + ListView mount, 040 modal subscriber); OPEN_TODOS_EVENT reconciled to import from `./components/Toolbar`
- `src/solutions/SmartTodo/src/hooks/useUserPreferences.ts` — viewMode field added (033); MyTasksFilterMode removed (031)
- `src/solutions/SmartTodo/src/hooks/useTodoItems.ts` + `services/queryHelpers.ts` + `services/DataverseService.ts` — single-arg `getActiveTodos(userId)`; filter baked in unconditionally (031)
- `src/solutions/SmartTodo/src/components/{Header/Header.tsx,SmartToDo.tsx,KanbanHeader.tsx,index.ts,ThresholdSettings.tsx}` — Header view-mode toggle (033) + orientation toggle slot (070); MyTasksFilter prop/render purge (031)
- `src/solutions/SmartTodo/src/components/MyTasksFilter.tsx` — DELETED (031)
- `src/solutions/SmartTodo/vite.config.ts` — `@spaarke/sdap-client` alias (040)
- `src/solutions/LegalWorkspace/vite.config.ts` — `@spaarke/sdap-client` alias (main-session reconciliation; PR #369 cascade unblock)
- All 6 Wave A POMLs flipped to `complete`; TASK-INDEX updated to 19/31

### Cumulative project state (master + branch)

Refer to `git log master..work/smart-todo-r4-wave2` for the precise Wave A delta. Master tip carries everything from Phases 0 + 1 + Wave 2a + followups.

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

### To resume (post-Wave-A)
1. Quick read: this file's Quick Recovery + "Wave proposal B" table
2. Verify branch + master sync: `git status` clean, `git fetch origin --prune`, `git rev-list --count HEAD..origin/master` (≥0 is OK — Wave A is ahead of master by 1 commit; if behind, rebase)
3. **Decision point**: open PR for Wave A now (decouples Wave B from any rebase work), OR continue Wave B on same branch and open one combined PR. Ask user before assuming.
4. Dispatch Wave B (recommended 6 parallel): 042 + 060 + 052 + 082 + 083 + 084. Then a follow-up wave for 041 + 071 (serialized against 042/033 respectively). Use the **Agent tool** with `general-purpose` subagent — Skill tool just loads the protocol into main context; Agent tool spawns concurrent sub-agents.
5. After Wave B returns: same reconciliation pattern as Wave A — main session does merges, dedupes, build-verify across all 4 packages, commit + push.
6. **Do NOT deploy from the branch.** Master-only deploy discipline holds.

### Commands for resume
```bash
# 1. Verify worktree on wave2 branch
git branch --show-current   # expect: work/smart-todo-r4-wave2

# 2. Master sync check
git fetch origin --prune
git rev-list --count HEAD..origin/master  # expect 0 unless master moved; rebase if non-zero
git rev-list --count origin/master..HEAD  # expect 1 (Wave A commit 54fb0d541)

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
