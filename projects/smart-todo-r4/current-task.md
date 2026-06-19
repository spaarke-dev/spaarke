# Current Task State — smart-todo-r4

> **Last Updated**: 2026-06-18 (Wave D ✅ deployed; Wave E queued — 3 new widget/app parity tasks 102/103/104 per UAT round 2)
> **Recovery**: Read "Quick Recovery" section first

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Project** | smart-todo-r4 — 7 workstreams (A-G), 34 tasks total (31 baseline + 3 Wave D widget-parity adds) |
| **Status** | **31 of 34 tasks ✅** (91% by count; all implementation waves + widget parity complete) |
| **PR #377** | ✅ MERGED to master as squash `eed39e40a` (Phases 0 + 1 + Wave 2a + followups; 13 tasks) |
| **PR #384** | ✅ MERGED to master 2026-06-11 (Waves A + B + C; 14 tasks) |
| **Wave D** | ✅ COMPLETE on branch (NOT yet on master): 099 widget chrome + Pattern D, 101 useKanbanColumns hoist, 100 openTodo launch + BroadcastChannel refetch. 4 commits on branch (`c50690be8` planning + `6074d42b9` 099 + `afb6ac6cc` 101 + `f593292c2` 100). Closes UAT issues 1-6 from 2026-06-18 screenshot. |
| **Worktree branch** | `work/smart-todo-r4-wave2` — synced to master tip `3d02a3d38` (post-PR-394 Wave E merge); R4-105 closeout local |
| **Active task** | **R4-092 deploy** (in-progress) — Wave D + Wave E both deployed to spaarkedev1; awaiting user UAT round 3 sign-off |
| **Working tree** | R4-105 POML + TASK-INDEX changes pending commit (this commit) |
| **Next Action** | **User UAT round 3 options**: (a) test on the deployed spaarkedev1 bits (Ctrl+F5 first); OR (b) use the NEW prototype harness at `c:/code_files/spaarke-prototype/projects/smart-todo-r4-uat/` (`npm run dev` → localhost:5173, sub-second HMR on widget source edits — no deploy needed for visual iteration). After UAT sign-off: flip R4-092 + R4-093 to ✅, proceed to R4-098 wrap-up + final PR-close. |
| **PR strategy** | Wave D (PR #391 `e4e91a3ec`); CI hardening (PR #392 + #393); Wave E (PR #394 `3d02a3d38` merged 2026-06-18T21:03:49Z). Prototype harness on `feature/uat-harness-framework` in spaarke-prototype repo — separate PR there when ready to land. R4-098 wrap-up + project-close still HELD until final UAT acceptance per durable user instruction. |

### 🆕 UAT-iteration harness (R4-105)

Built this session to address the ~30-min-per-Ctrl-F5 cost that drove Wave D + Wave E rework. Live now in `c:/code_files/spaarke-prototype/projects/smart-todo-r4-uat/`:

```bash
cd c:/code_files/spaarke-prototype/projects/smart-todo-r4-uat
npm run dev   # localhost:5173 with widget + 15 seeded todos + HMR on widget source
```

Framework (reusable for any future project): `c:/code_files/spaarke-prototype/_infra/` + `projects/_templates/prod-component-harness/`. See `projects/_framework-setup-2026-06/README.md` in the prototype repo for the design. AI convention: `claude.md` updated with "Production Component Harnesses" section.

---

## 🚨 RESTART POINT — read this first after compaction

> **Saved**: 2026-06-18 (session approaching context limit)
> **Status**: 35/38 tasks ✅; Wave E deployed; UAT-harness framework shipped to prototype repo
> **What's HELD**: R4-098 wrap-up + R4-093 UI tests + R4-092 closeout — ALL gated on user UAT acceptance per durable instruction

### Where every piece lives

| Artifact | Repo | Path | Branch |
|---|---|---|---|
| smart-todo-r4 production code | `c:/code_files/spaarke-wt-smart-todo-r4` | `src/` | `work/smart-todo-r4-wave2` (this worktree; HEAD = c1b02c961) |
| smart-todo-r4 production code (also on master) | `c:/code_files/spaarke` (main repo) | `src/` | `master` (HEAD = 3d02a3d38, PR #394 merged) |
| Deployed to spaarkedev1 | Dataverse | sprk_smarttodo + sprk_spaarkeai + sprk_createtodowizard web resources | Wave D + Wave E both live (timestamps 9:25-9:27 PM 2026-06-18) |
| UAT-harness framework | `c:/code_files/spaarke-prototype` | `_infra/` + `projects/_templates/prod-component-harness/` + `projects/smart-todo-r4-uat/` | `feature/uat-harness-framework` (HEAD = 73a9ddc; pushed to origin; PR NOT yet opened to main) |
| Framework docs | `c:/code_files/spaarke-prototype` | `docs/PROTOTYPE-UI-SYSTEM-GUIDE.md` + `docs/SKILLS-TO-BUILD.md` + `projects/_framework-setup-2026-06/README.md` + `claude.md` | Same `feature/uat-harness-framework` branch |
| R4-105 POML | `c:/code_files/spaarke-wt-smart-todo-r4` | `projects/smart-todo-r4/tasks/105-UAT-harness-consumer.poml` | This worktree, committed |

### PRs landed this session

| PR | Merged | Scope |
|---|---|---|
| #391 | 2026-06-18T16:03:49Z | Wave D widget parity (R4-099/100/101) |
| #392 | 2026-06-18T16:31:07Z | CI hardening: lockfile + `npm install` pattern |
| #393 | 2026-06-18T~17:00Z | CI hardening: explicit eslint dep in PCF package.json (root-cause fix) |
| #394 | 2026-06-18T21:03:49Z | Wave E widget/app parity (R4-102/103/104) — merge commit 3d02a3d38 |

### Outstanding work

| Task | State | Blocked on |
|---|---|---|
| **User UAT round 3** | Pending — user testing on live spaarkedev1 OR via new prototype harness | User availability |
| R4-092 deploy session POML closeout | 🔄 in-progress | UAT round 3 sign-off |
| R4-093 UI test suite | 🔲 not-started (superseded by iterative UAT) | UAT round 3 sign-off — likely auto-✅ when user signs off |
| R4-098 project wrap-up | 🔲 not-started — lessons-learned + README → Complete + final PR-close | UAT round 3 sign-off |
| Prototype harness framework PR → main | NOT yet opened; branch `feature/uat-harness-framework` pushed at commit 73a9ddc | User decision (merge now / wait for team review) |
| Skill build: `prototype-harness-setup` (HIGH priority per `docs/SKILLS-TO-BUILD.md`) | Not started | User decision (build it now? defer?) |

### Resume commands

```bash
# 1. Verify state
cd c:/code_files/spaarke-wt-smart-todo-r4
git status                                    # should be clean
git log -1 --oneline                          # should be c1b02c961
git fetch origin --prune
git rev-list --count HEAD..origin/master     # expect 0 (Wave E + R4-105 closeout both on master? no — closeout still local)

# Check prototype repo
cd c:/code_files/spaarke-prototype
git status
git branch --show-current                    # should be feature/uat-harness-framework
git log -1 --oneline                          # should be 73a9ddc

# To launch the harness for testing
cd c:/code_files/spaarke-prototype/projects/smart-todo-r4-uat
npm run dev                                   # localhost:5173

# To verify deployed bits on spaarkedev1
# Browser: open spaarkedev1 SpaarkeAi workspace; Ctrl+F5 first
# Or query Dataverse:
pwsh -NoProfile -File /c/tmp/verify-deploy.ps1
```

### Decision points awaiting user

1. **UAT round 3 verdict**: sign-off, partial sign-off (with new bugs filed), or new Wave F?
2. **Prototype harness PR to main**: merge `feature/uat-harness-framework` → main now, or wait?
3. **Build `prototype-harness-setup` skill**: build now (~4 hr investment, would benefit all future projects) or defer?

### Recovery instructions (for next session)

1. Read this Quick Recovery + RESTART POINT sections (above)
2. Read `c:/code_files/spaarke-prototype/docs/PROTOTYPE-UI-SYSTEM-GUIDE.md` for full framework context (700+ lines covering both modes)
3. Read `c:/code_files/spaarke-prototype/docs/SKILLS-TO-BUILD.md` for the skill decision context
4. Read this session's last commit messages: `git log --oneline -10` in BOTH repos
5. Re-confirm with user which decision point they want to address first

### Last commits this session (both repos)

```
spaarke-wt-smart-todo-r4 (work/smart-todo-r4-wave2):
  c1b02c961 feat(smart-todo-r4): R4-105 UAT-harness framework (cross-repo; sub-second iteration enabler)
  1e8872871 chore(smart-todo-r4): R4-092 Wave E deploy session 2026-06-18
  10e5b8a01 Merge remote-tracking branch 'origin/master' into work/smart-todo-r4-wave2
  87b3d3a85 chore(smart-todo-r4): prettier --write on 8 Wave E peer-package files
  1a906db84 feat(smart-todo-r4): R4-103 (E-2) — widget toolbar polish
  0a1443fbe feat(smart-todo-r4): R4-104 (E-3) — app chrome consolidation
  882c1836a feat(smart-todo-r4): R4-102 (E-1) — widget Kanban hoist

spaarke-prototype (feature/uat-harness-framework):
  73a9ddc feat(prototype): UAT harness framework + smart-todo-r4-uat first consumer
```

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
| **Wave B** | 042, 052, 060, 082, 083, 084 | 6 parallel agents. All returned clean. 042 retired `TodoDetailPanel` side-pane (FR-18; deleted `TodoDetailPanel.tsx` + `todoDetailService.ts`); 060 card affordances (Open icon + Checkbox + double-click on KanbanCard; reused Wave A `OPEN_TODOS_EVENT`); 052 RegardingResolver PCF read-only mode + pre-save handler formType 3/4 skip (PCF version bump 1.0.0 → 1.1.0, +3 tests = 23/23 pass, added PCF-side webpack alias for PR #369 cascade); 082/083/084 Visual Host instructions docs for Project/Invoice/WorkAssignment (clones of 081 template per §10 substitution table). 042 + 060 successfully merged on `SmartTodoApp.tsx` without write-race losses. Commit `0c2dd15da`. |
| **Wave C** | 041, 071 | 2 parallel agents. 071 returned clean (orientation persistence via `useUserPreferences` envelope extension mirroring 033's `viewMode` pattern; 13-assertion executable-spec test; back-compat with pre-071 records). 041 produced its on-disk artifacts (318-LOC `sprk_todo_dirty_check.js` web resource with `request-dirty-check`/`dirty-check-result` correlationId handshake + origin allow-list; 359-LOC test suite at `iframeDirtyCheckScript.test.ts`; 298-LOC bind instructions doc at `notes/c-dirty-check-bind-instructions.md`; shell README protocol section) but its final response was rate-limited before TASK-INDEX bump — main session fixed up row + counter. Commit `2ff4d9b75`. |
| **Wave D (post-PR-384 widget parity, 2026-06-18)** | 099, 101, 100 | 3 tasks closing 6 UAT issues from 2026-06-18 widget screenshot. Wave D-1 = 099 + 101 parallel; Wave D-2 = 100 serial after 099. **099** collapsed LW shim to Calendar canonical (kept section title, removed widget PaneHeader); widget gained `<Toolbar>` with `[SearchBox, +, Open, refresh]`; single-select state; in-memory search filter with 150ms debounce; title became "Smart To Do" (commit `6074d42b9`). **101** hoisted `useKanbanColumns` into `@spaarke/smart-todo-components` peer package (~340 LOC; generic on `T extends IKanbanTodoLike`); widget renders 3 grouped sections via `bucketTodoItems(items, 60, 30)`; Code Page consumer's import swapped + local hook deleted (commit `afb6ac6cc`). **100** extended `useLaunchContext` with `openTodo` discriminator (handles both raw query + envelope); SmartTodoApp auto-mounts `<SmartTodoModal>` on `openTodo`; shim's `handleOpenTodo` swapped from legacy `eventId` envelope to `action=openTodo&todoId=<guid>`; BroadcastChannel `sprk_todo:created` chosen for post-wizard-close refetch (producer in CreateTodoWizard `main.tsx`; consumer in shim); added `@spaarke/sdap-client` alias to CreateTodoWizard `vite.config.ts` (6th code-page receiving the PR #369 cascade workaround) (commit `f593292c2`). All builds green: SmartTodo 1.74 MB / 474 KB gzip, LegalWorkspace 2.25 MB / 624 KB gzip, CreateTodoWizard 1.66 MB / 451 KB gzip, RegardingResolver PCF 1.57 MiB. Wave D includes audit-findings note `notes/d-widget-parity-audit-2026-06-18.md` + 3 POMLs. |

---

## Remaining Work — 4 tasks (Phase 3/4; 092 deploy DEFERRED)

### Phase 3 + 4 (final tasks)

| Task | Description | Dependencies | Estimated | Recommended order |
|---|---|---|---|---|
| **094** | Final `grep -ri sprk_todoflag src/` → 0 functional hits (graduation criterion 12) | none on this branch (was `092` originally, but per user decision 092 is deferred) | ~15 min | **DO FIRST** — cheap, fully local, gating |
| **093** | UI test suite — NFR-05 modal nav latency, NFR-07 a11y, NFR-08 orientation switch | could run against `npm run dev` (no deploy needed) instead of deployed bits | ~1 day | DO SECOND |
| **098** | Project wrap-up — lessons-learned + README status → Complete + repo-cleanup + open ONE combined PR for `work/smart-todo-r4-wave2` (Waves A + B + C + Phase 3 = 16 tasks delta over master) | 093, 094 | ~0.5 day | DO LAST |
| **092** | Deploy all affected solutions — **DEFERRED per user instruction**. The master-only-deploy policy means this happens after the combined PR merges, NOT as a branch task. The task POML can be marked complete when 098 opens the PR (since merge → master triggers deploy responsibility for the last lander). Also fold in project-wide tsconfig refs fix for `@spaarke/sdap-client` cascade (5 code-pages + 1 PCF currently have local workaround aliases). | all 020-084 ✅ | n/a here | After PR merge |

**Rationale for the 094 → 093 → 098 sequence**: 094 is a mechanical local grep gate that catches any straggler `sprk_todoflag` references; better to know early than to discover it in the PR review. 093 can use `npm run dev` smoke testing instead of deployed bits. 098 closes the loop.

---

## CI hardening (2026-06-18 — systemic fix)

PR #391 surfaced the SECOND CI failure of the kind "lockfile/format drift blocks a Wave that was otherwise green locally" (PR #384 was Prettier; PR #391 was `npm ci` in `src/client/pcf` failing on `eslint@9.39.4` missing from lockfile). Per user direction "we need to fix these so that we do not continuously run into these issues":

- `.github/workflows/sdap-ci.yml` lines 172 + 194 (the 2 `npm ci --ignore-scripts` steps in the Client Quality job) **changed to** `npm install --legacy-peer-deps --no-audit --no-fund --ignore-scripts`. Rationale: aligns CI with the project's established convention per root CLAUDE.md §11 Node Installs section (Vite solutions + Build-AllClientComponents.ps1 already use this pattern; CI was the holdout). Strict `npm ci` blocks CI on transitive churn that's harmless at install time; `npm install` reads the lockfile as hints but doesn't enforce strict sync.
- `src/client/pcf/package-lock.json` regenerated against current `package.json` (587 → 643 packages; pulls in resolved `eslint@9.39.4` + 6 other `@humanfs/*` / `@humanwhocodes/*` transitives that were missing).
- Prettier auto-fix step in CI ("Prettier auto-fix" + "Push Prettier fixes") **already exists** as a self-healing layer for prettier drift on PRs — that's why PR #384's prettier issue was a one-time annoyance, not a recurring class. The `npm ci` issue had no equivalent self-heal; this change is the equivalent for install-step drift.

**What this fix prevents going forward**: any future task that bumps a package.json (dev-dep version, new transitive, package addition/removal) will not break CI just because the local lockfile regeneration was forgotten. CI will resolve dependencies on-the-fly via `npm install`. Reproducibility-critical paths (the master-deploy scripts) already use the same `--legacy-peer-deps --no-audit --no-fund` pattern.

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

### To resume (post-Wave-C)
1. Quick read: this file's Quick Recovery + "Phase 3 + 4" table
2. Verify branch state: `git status` clean, `git fetch origin --prune`, branch is ~6 commits ahead of master
3. Recommended next: **094** (mechanical local grep gate; ~15 min) — flips ✅ then proceed
4. Then **093** UI tests via `npm run dev` smoke (no deploy needed); then **098** wrap-up + open ONE combined PR for `work/smart-todo-r4-wave2`
5. **092 is DEFERRED per user instruction** — master-only-deploy means deploy happens AFTER PR merge, not as a branch task
6. **Do NOT deploy from the branch.** Master-only deploy discipline holds.

### Commands for resume
```bash
# 1. Verify worktree on wave2 branch
git branch --show-current   # expect: work/smart-todo-r4-wave2

# 2. Master sync check
git fetch origin --prune
git rev-list --count HEAD..origin/master  # expect 0 unless master moved; rebase if non-zero
git rev-list --count origin/master..HEAD  # expect ~6 (resume + Wave A + post-A doc + Wave B + post-B doc + Wave C)

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
