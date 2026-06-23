# Current Task State — smart-todo-r4

> **Last Updated**: 2026-06-23 (R4-110 in progress — 4-step chain audit fix applied to source + docs; build/deploy next)
> **Recovery**: Read "Quick Recovery" section first

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Project** | smart-todo-r4 — closeout wave (rounds 110-117) post UAT rounds 4-13 |
| **Phase** | **Closeout Wave 14** — 8 tasks tasked + ready to execute. R4 main scope ALREADY MERGED (PR #406 squash commit `80f70a1d4` on master, 2026-06-23T13:21:19Z). |
| **Worktree branch** | `work/smart-todo-r4-closeout` (NEW — created from master after PR #406 merge). Pushed to origin. |
| **Active task** | **R4-110 — Structural Workspace height-chain audit + fix** — ✅ **COMPLETE**. UAT-validated (8-section checklist + multi-widget overlap re-test). Ready for PR + merge to master. |
| **Next Action** | Commit follow-up → /push-to-github → /merge-to-master. After merge, redeploy from master per "spaarkedev1 deploys are master-only" durable rule. Next closeout task: R4-111 (remove widget Expand-to-Code-Page modal). |
| **Pre-execution context** | The per-section `style: { height: 'calc(100vh - 200px)', minHeight: '560px' }` in `LegalWorkspace/workspaceConfig.tsx` is a workaround for an unresolved structural break in the workspace height chain above `WorkspaceShell.shell`. Round 7 of UAT attempted the fix and collapsed the workspace to 40px — that attempt and the working tactical-fix (round 11/12) are documented in `notes/handoffs/` and the git log on the merged work/smart-todo-r4-uat4-fixes branch (now deleted locally but findable via `git log master`). |

### What just shipped (PR #406 on master)

13 UAT rounds + structural workspace fixes + documentation:
- Contact entity migration (sprk_contact → OOB contact) — entity name + nav-prop PascalCase bind-key (`sprk_AssignedTo@odata.bind`)
- Modal save/close interceptor (parent-side `Xrm.Page.ui.close` patch in SmartTodoModal)
- Widget responsive layout (`display: flex` on body wrapper) + WorkspaceLayoutWidget.root `height: 100%` + WorkspaceShell.row `flex: 1 1 0 + alignItems: stretch`
- Date-based bucketing (Today/Tomorrow/Future by due-date, not score)
- Widget default orientation = vertical (rows); Code Page = horizontal (columns)
- Refresh button wiring (TodoContext.refetch was a no-op placeholder)
- Assigned To typeahead picker (widget + Code Page Header)
- Quick-add 400 Bad Request fix (PascalCase nav-prop name)
- Documentation: `docs/guides/BUILD-A-NEW-WORKSPACE-WIDGET.md` §7.2 (HEIGHT chain contract) + `.claude/patterns/ui/embedded-widget-sizing.md` extended

### Closeout Wave (110-117) — execution order

1. **R4-110** — Structural Workspace height-chain audit + fix (FOUNDATIONAL, 1-2 days). Removes per-section calc(100vh-200px) workaround. Updates pattern docs as canonical reference. **← START HERE**
2. **R4-111** — Remove widget "Expand to Code Page modal" path (1-2 hrs). User decision: ONLY modal from widget is the record modal.
3. **R4-112** — PCF CREATE-mode bridge (FU-1, 2-3 hrs). Originally R4-scoped; deferred. Without it, NEW sprk_todo creates have empty regarding fields.
4. **R4-113** — Form-script audit (NEW-1, 1-2 hrs). `sprk_/js/sprk_todo_dirty_check.js` was deployed but never registered on form OnLoad. Per user's "no shims" rule: register or delete.
5. **R4-114** — Wire vitest for SmartTodo Code Page (FU-4, 2-3 hrs). 22 test spec files exist but no runner. Per "no shims": wire vitest, make tests real.
6. **R4-115** — SpeDocumentViewer stale bundle cleanup (FU-6, 1-2 hrs). Legacy `sprk_todoflag` write path may still be in deployed bundle.
7. **R4-116** — R4-092 final deploy notes + flip to ✅ (30 min, serial after 110-115).
8. **R4-117** — R4-098 wrap-up + lessons-learned + repo-cleanup (2-3 hrs, serial after 116).

### R4-110 audit findings + 4-step plan (executing now)

**Static chain audit finding**: Post UAT rounds 11/12, the height chain from `App.appRoot` (`100vh` anchor) down through every layer to `SectionPanel.card` (grid-stretched via row `alignItems: stretch`) is structurally intact. The per-section `calc(100vh - 200px)` workaround on SmartTodo was not the root issue — it was forcing dominance over peers in the legacy LegalWorkspace 5-section dashboard. **One latent fragility remained**: `WorkspaceTabManagerComponent.content` was `display:block` with `flex:1`, which silently ignores child flex — meaning future widget authors who use `flex:1` instead of `height:100%` on their root would trip the same trap as smart-todo-r4 rounds 4-10.

**4-step plan (user approved)**:
1. ✅ `WorkspaceTabManagerComponent.content` — add `display:flex; flexDirection:column; minHeight:0` (chain robustness — future widgets can now use either `flex:1` or `height:100%`)
2. ✅ `workspaceConfig.tsx` (LegalWorkspace default dashboard, Path B) — remove `style: { height: "calc(100vh - 200px)", minHeight: "560px" }`; keep `style: { minHeight: "560px" }` floor parity with todoRegistration.defaultHeight (Path A)
3. ✅ `SmartTodoWidget.styles.ts` kanbanContainer — remove vestigial `minHeight: 400px` pixel floor (chain now delivers determinate height)
4. ✅ Pattern docs (`BUILD-A-NEW-WORKSPACE-WIDGET.md §7.2` + `embedded-widget-sizing.md` HEIGHT contract + `SectionPanel.tsx` card comment) — updated to reflect post-R4-110 forgiving chain

**Files modified (this task)**:
- `src/solutions/SpaarkeAi/src/components/workspace/WorkspaceTabManagerComponent.tsx`
- `src/solutions/LegalWorkspace/src/workspaceConfig.tsx`
- `src/client/shared/Spaarke.SmartTodo.Components/src/widgets/SmartTodoWidget/SmartTodoWidget.styles.ts`
- `src/client/shared/Spaarke.UI.Components/src/components/WorkspaceShell/SectionPanel.tsx` (comment refresh only)
- `docs/guides/BUILD-A-NEW-WORKSPACE-WIDGET.md` (§7.2 chain map + §7.2.3 + §7.2.5 anti-patterns)
- `.claude/patterns/ui/embedded-widget-sizing.md` (HEIGHT contract + failure modes table)

**Next**: build affected packages → deploy `sprk_spaarkeai` + `sprk_smarttodo` → user runs console diagnostic to verify chain holds.

### Files Modified This Session (closeout planning)

**Committed to `work/smart-todo-r4-closeout` (commit `26e0befe3`):**
- `projects/smart-todo-r5/design.md` — NEW: R5 backlog + user's R5 design items
- `projects/smart-todo-r4/tasks/110-new2-structural-workspace-layout-audit.poml` — NEW
- `projects/smart-todo-r4/tasks/111-remove-widget-expand-codepage-modal.poml` — NEW
- `projects/smart-todo-r4/tasks/112-fu1-pcf-create-mode-bridge.poml` — NEW
- `projects/smart-todo-r4/tasks/113-new1-form-script-audit.poml` — NEW
- `projects/smart-todo-r4/tasks/114-fu4-wire-vitest-smart-todo.poml` — NEW
- `projects/smart-todo-r4/tasks/115-fu6-spedocumentviewer-cleanup.poml` — NEW
- `projects/smart-todo-r4/tasks/116-r4-092-final-deploy-notes.poml` — NEW
- `projects/smart-todo-r4/tasks/117-r4-098-wrap-up.poml` — NEW
- `projects/smart-todo-r4/tasks/TASK-INDEX.md` — header updated, closeout wave section added

**Uncommitted (NOT smart-todo-r4 related — DO NOT include in any commit):**
- `.claude/agent-memory/researcher/MEMORY.md` + new researcher file (unrelated subagent work)
- `.husky/_/*` (auto-regenerated husky shims, ignore)
- `projects/smart-todo-r4/current-task.md` (this file — context-handoff updates it)

**Memory updates this session (persistent, NOT in git):**
- `~/.claude/projects/c--code-files-spaarke-wt-smart-todo-r4/memory/feedback_no-shims-clean-up-dead-code.md` — NEW
- `~/.claude/projects/c--code-files-spaarke-wt-smart-todo-r4/memory/feedback_elevate-to-shared-component-library.md` — NEW
- `~/.claude/projects/c--code-files-spaarke-wt-smart-todo-r4/memory/MEMORY.md` — index updated

### Critical Context for resumption

1. **R4-110 is foundational** — it informs how every future widget mounts in the Workspace. The per-section `calc(100vh - 200px)` is a workaround for an unresolved structural break above `WorkspaceShell.shell`. Round 7 tried the structural fix and collapsed the workspace to 40px; round 11/12 fixed it tactically via `WorkspaceLayoutWidget.root { height: 100% }` + `WorkspaceShell.row { flex: 1 1 0, alignItems: stretch }`. The proper fix needs an audit of the chain from `SpaarkeAi App` down through every layer, identifying the topmost break. **Use the console diagnostic scripts** documented in BUILD-A-NEW-WORKSPACE-WIDGET.md §7.2.4 to test changes BEFORE deploying.
2. **R4-111 simplifies R4-110** — once the widget no longer launches the Code Page as a modal, the nested-modal complexity disappears. May want to do 111 BEFORE 110 to reduce surface area, but 110 is the higher-impact foundation work.
3. **R4-112 is independent** — PCF CREATE-mode bridge can be done in parallel with anything else. Different surface (PCF + OnSave form script).
4. **R4-113 form-script audit** — `sprk_/js/sprk_todo_dirty_check.js` v1.1.0 lives at Dataverse `webresourceid: 4c6e8319-c069-f111-ab0d-7ced8ddc4cc6`. Confirm via MCP that it's still there. Decision needed from user before executing: register or delete.
5. **R5 worktree** — DEFERRED until R4 closes. R5 design.md captured all R5 items so context doesn't leak.

---

## Full State (Detailed)

### Project repository state

- **PR #406**: MERGED at squash commit `80f70a1d4` on master (2026-06-23T13:21:19Z). Source branch `work/smart-todo-r4-uat4-fixes` deleted (per gh merge default).
- **Closeout branch**: `work/smart-todo-r4-closeout` created from master post-merge. Pushed to origin. NO PR open yet — will be created when closeout work is committable.
- **Master state**: 11 commits behind branch was reconciled via merge before PR #406 closed; master is now at 80f70a1d4.

### Deployed surfaces (spaarkedev1)

| Web Resource | Modified | Carries |
|---|---|---|
| `sprk_smarttodo` | 2026-06-22 23:36Z | All Code Page-side R4 work (Header chrome, useCurrentContactId, columnOrders, orientation prop, contact filter, refresh wiring, modal listener) |
| `sprk_spaarkeai` | 2026-06-22 23:38Z | All widget-side R4 work (3-field quick-add, Filter slide, contact resolution, kanban responsive fill, modal close intercept, structural layout fixes) |
| `sprk_createtodowizard` | 2026-06-20 22:35Z | Wizard `/contacts(...)` bind + searchContactsAsLookup picker + PascalCase nav-prop fallbacks |
| `sprk_/js/sprk_todo_dirty_check.js` v1.1.0 | 2026-06-22 12:41Z | NEVER REGISTERED on form OnLoad — see R4-113 |

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
- FU-2 RecordNavigationModalShell chromeMode
- FU-5 LW Kanban rich-feature hoist (IMPORTANT per shared-lib elevation philosophy)
- TEST-1/TEST-2 test infrastructure
- PROC-1 real-Dataverse smoke before merge (cross-cutting; affects all UI projects)
- R5 worktree creation deferred until R4 closes

### Durable feedback memories (apply going forward)

- **no-shims-clean-up-dead-code**: complete or delete; don't leave dead web resources, unregistered scripts, or spec files without runners
- **elevate-to-shared-component-library**: if potentially reusable (PCF / Outlook / mobile / other workspace), hoist to `@spaarke/*` shared lib NOW; don't wait for second consumer
- **spaarkedev1-deploy-discipline**: deploys are master-only; last merger redeploys after PR lands

---

*Updated by `/context-handoff` at 2026-06-23 14:15Z. To resume: say "work on task 110" or "continue".*
