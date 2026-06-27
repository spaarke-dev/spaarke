# Current Task State — smart-todo-r4

> **Last Updated**: 2026-06-24 (by /context-handoff — R4-110 COMPLETE + merged + worktree current with master)
> **Recovery**: Read "Quick Recovery" section first

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Project** | smart-todo-r4 — closeout wave (110-117) post UAT rounds 4-13 |
| **Phase** | **Closeout Wave**. R4-110 ✅ COMPLETE. 6 tasks remaining: R4-111 → R4-117. |
| **Worktree branch** | `work/smart-todo-r4-closeout` at `cc1391c9a` (= origin/master) — fully current; pushed to origin |
| **Active task** | **NONE** (between tasks). R4-110 just shipped via PR #419 → master at `b26ac56b7` (2026-06-23T20:10:31Z). |
| **Next Action** | Say "work on task 111" (or 112/113/114/115 in any order — see "Execution order" below). Task POMLs are at `tasks/111-117.poml`. Each task uses `task-execute` skill per root CLAUDE.md §4. |
| **Working tree** | Clean except 1 untracked researcher MD (intentionally ignored per project handoff). 0 commits ahead of origin/master; 0 behind. |

### What just shipped (PR #419 on master)

**R4-110 — Structural Workspace height-chain audit + fix** (merge commit `b26ac56b7`):

- `WorkspaceTabManagerComponent.content` — added `display:flex; flexDirection:column; minHeight:0` so future widget roots can use either `flex:1` OR `height:100%` (R4-110 chain robustness)
- `workspaceConfig.tsx` — removed per-section `style:{height:'calc(100vh-200px)',minHeight:'560px'}` on SmartTodo; kept `minHeight:560` floor parity with `todoRegistration.defaultHeight`
- `SmartTodoWidget.kanbanContainer` — removed vestigial `minHeight:400px` pixel floor
- `WorkspaceShell.row` — removed `minHeight:0` (R4-110 follow-up after UAT revealed multi-widget dashboard overlap)
- `SectionPanel.tsx` — refreshed round-7 comment
- `BUILD-A-NEW-WORKSPACE-WIDGET.md §7.2` — rewritten chain map + per-section sizing guidance + anti-patterns
- `.claude/patterns/ui/embedded-widget-sizing.md` — HEIGHT contract simplified (3 author-rules → 2)
- 8 closeout-wave POMLs (R4-111–R4-117) + R5 design backlog (incl. F-6 filter/search label, F-7 inner-modal sizing, F-8 inner-modal Save&Close)

**UAT validation**: 8-section checklist + multi-widget overlap re-test all passed. Console height-chain diagnostic showed 943px widget root, chain consistent across 14 layers.

### Deploy state (spaarkedev1)

| Web Resource | Source | State |
|---|---|---|
| `sprk_smarttodo` | ✅ Built + deployed from main repo **master** (2026-06-24 16:32 local), bundle 1721 KB | Fully current with master |
| `sprk_spaarkeai` | ⚠️ Last deploy was from worktree iteration (R4-110 follow-up code, 3854 KB) | Has R4-110 changes (UAT-validated) but missing master-only work since. Master build was broken at deploy time (`@spaarke/daily-briefing-components/widgets` missing). **PR #417 (`fix(daily-briefing): unbreak master CI`) is now on master and likely fixes it.** Owner of the daily-briefing project (or whoever ships next) should redeploy from master to bring spaarkedev1 fully current. |
| `sprk_createtodowizard` | 2026-06-20 22:35Z | Unchanged from PR #406 |
| `sprk_/js/sprk_todo_dirty_check.js` v1.1.0 | 2026-06-22 12:41Z | NEVER REGISTERED on form OnLoad — R4-113 will audit |

### Closeout Wave execution order (remaining: 111-117)

| Task | Title | Est | Parallel-safe? |
|---|---|---|---|
| ~~R4-110~~ | Structural height-chain audit + fix | DONE | — |
| **R4-111** | Remove widget "Expand to Code Page modal" path | 1-2 hrs | Yes |
| **R4-112** | PCF CREATE-mode bridge (FU-1) | 2-3 hrs | Yes (different surface) |
| **R4-113** | Form-script audit (NEW-1) — register or delete `sprk_todo_dirty_check.js` | 1-2 hrs | Yes |
| **R4-114** | Wire vitest for SmartTodo Code Page (FU-4) | 2-3 hrs | Yes |
| **R4-115** | SpeDocumentViewer stale bundle cleanup (FU-6) | 1-2 hrs | Yes |
| **R4-116** | R4-092 final deploy notes + flip to ✅ | 30 min | No (serial after 111-115) |
| **R4-117** | R4-098 wrap-up + lessons-learned + repo-cleanup | 2-3 hrs | No (serial after 116) |

### Files Modified This Session (R4-110)

**Committed to `work/smart-todo-r4-closeout` and MERGED to master via PR #419 (`b26ac56b7`):**

- `40ff12224` fix(workspace-layout): R4-110 — chain robustness + remove SmartTodo calc workaround (7 files)
- `222ddae3f` fix(workspace-shell): R4-110 follow-up — remove row minHeight:0 (4 files)
- `a222bf2e3` Merge origin/master into work/smart-todo-r4-closeout (181 files — master sync; 7 conflicts resolved "ours")

After PR #419 merged, the worktree fast-forwarded to `cc1391c9a` (current master HEAD, which includes 17 more commits from other projects: PCF cleanup, CI fixes, daily-briefing test repair).

**Uncommitted (NOT smart-todo-r4 — DO NOT include in any commit):**

- `.claude/agent-memory/researcher/MEMORY.md` (now matches master — resolved during pull)
- `.claude/agent-memory/researcher/spaarke-pcf-client-quality-eslint-2026-06.md` (untracked, researcher subagent's PCF ESLint research)

### Critical Context for resumption

1. **R4-110 is DONE and on master.** The shell-side height chain is now forgiving — widget authors can use either `flex:1` OR `height:100%` on their roots, and intermediate wrappers must be `display:flex` (or `grid`). Per-section `style.height` is no longer needed; minHeight floors only (matches `defaultHeight` in registrations).

2. **R4-111 simplifies R4-110.** Once the widget no longer launches the Code Page as a modal, the nested-modal complexity (F-7, F-8 in R5 backlog) is reduced.

3. **R4-112 is independent** — PCF CREATE-mode bridge can run in parallel with 111/113-115.

4. **R4-113 form-script audit** — `sprk_/js/sprk_todo_dirty_check.js` v1.1.0 lives at Dataverse `webresourceid: 4c6e8319-c069-f111-ab0d-7ced8ddc4cc6`. Decision needed from user before executing: register on form OnLoad, or delete per "no shims" rule.

5. **R5 worktree** — DEFERRED until R4 closes (after R4-117). R5 design.md captured all R5 items so context doesn't leak.

6. **sprk_spaarkeai deploy is owed to spaarkedev1** — once the daily-briefing project (or whoever ships next) deploys from master, spaarkedev1 will be fully current. NOT this worktree's responsibility per the durable "spaarkedev1 deploys are master-only" rule.

7. **MUST invoke `task-execute` skill** to start any task (per root CLAUDE.md §4). DO NOT read POML files directly + implement manually.

---

## Full State (Detailed)

### Project repository state

- **PR #406**: MERGED at squash commit `80f70a1d4` on master (2026-06-23T13:21:19Z) — R4 main scope.
- **PR #419**: MERGED at merge commit `b26ac56b7` on master (2026-06-23T20:10:31Z) — R4-110.
- **Master HEAD now**: `cc1391c9a` (R4-110 + 17 commits from PCF cleanup, CI fixes, daily-briefing test repair PR #417).
- **Worktree HEAD**: `cc1391c9a` (= master via fast-forward + push 2026-06-24).
- **Working tree**: Clean except 1 untracked researcher MD (intentional).

### Data state on spaarkedev1

- `sprk_todo.sprk_assignedto` schema = OOB `contact` lookup ✓ (verified via MCP)
- Nav-prop name = `sprk_AssignedTo` (PascalCase) ✓ (verified via EntityDefinitions metadata)
- Ralph Schroeder contact: `contactid=2e419a4f-010d-f111-8342-7ced8d1dc988`, linked via `sprk_systemuser` to systemuser `1d02f31c-1872-f011-b4cb-7c1e52671ad0` ✓
- 15 active sprk_todo rows bulk-assigned to Ralph's contact for UAT seed ✓

### Open items going to R5 (captured in `projects/smart-todo-r5/design.md`)

- F-1 yellow contrast (visual)
- F-2 Completed status + filter toggle
- F-3 filter pane redesign (Priority/Status/Due/Assigned-To categories)
- F-4 NEW `sprk_priority` choice + priority icon + auto-set score
- F-5 NEW `sprk_effort` choice + auto-set score
- **F-6 NEW** — SmartTodo widget toolbar 'Search' label (should be Filter, also broken) — surfaced during R4-110 UAT, pre-existing
- **F-7 NEW** — Open-To-Do inner-modal sizing (nested modals at 80% vs outer 85%) — surfaced during R4-110 UAT, pre-existing
- **F-8 NEW** — Open-To-Do inner-modal Save&Close routing (inner dialog Save lands user on SpaarkeAi instead of refreshing parent) — surfaced during R4-110 UAT, pre-existing
- FU-2 RecordNavigationModalShell chromeMode
- FU-5 LW Kanban rich-feature hoist (IMPORTANT per shared-lib elevation philosophy)
- NEW-2 Structural Workspace height-chain fix → ✅ Closed in R4-110 (delete this entry once R4 PR fully closes)
- TEST-1/TEST-2 test infrastructure
- PROC-1 real-Dataverse smoke before merge (cross-cutting; affects all UI projects)
- R5 worktree creation deferred until R4 closes

### Durable feedback memories (apply going forward)

- **no-shims-clean-up-dead-code**: complete or delete; don't leave dead web resources, unregistered scripts, or spec files without runners
- **elevate-to-shared-component-library**: if potentially reusable (PCF / Outlook / mobile / other workspace), hoist to `@spaarke/*` shared lib NOW; don't wait for second consumer
- **spaarkedev1-deploy-discipline**: deploys are master-only; last merger redeploys after PR lands

---

*Updated by `/context-handoff` 2026-06-24. To resume: say "work on task 111" (or 112/113/114/115 in any order — they're parallel-safe). Each task uses `task-execute` skill per root CLAUDE.md §4.*
