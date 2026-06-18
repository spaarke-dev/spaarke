# R4 Task Index

> **Project**: smart-todo-r4
> **Last Updated**: 2026-06-18 (Wave E ✅ COMPLETE — 102 + 103 + 104 all landed; awaiting PR + redeploy)
> **Branch**: `work/smart-todo-r4-wave2`
> **Total Tasks**: 37 (was 34; +102/103/104 Wave E widget/app parity from 2026-06-18 UAT round 2 — see [e-widget-app-parity-audit-2026-06-18.md](../notes/e-widget-app-parity-audit-2026-06-18.md))
> **Status**: 🔲 2 not-started · 🔄 1 in-progress (092 awaiting UAT sign-off) · ✅ 34 complete · ❌ 0 blocked
> **Active wave**: **Wave E ✅ COMPLETE** (102 widget Kanban hoist + 103 widget toolbar polish + 104 app chrome consolidation all landed). Remaining: 092 (Wave D deploy session, awaiting UAT sign-off — will need re-deploy after Wave E PR merges to master), 093 (UI test suite — superseded by iterative UAT), 098 (project wrap-up, HELD until UAT acceptance).

---

## Phase 0 Outcomes — Binding Decisions (2026-06-10)

| Audit | Decision | Binds tasks | Notes file |
|---|---|---|---|
| **R4-001** A widget surface | **Pattern D dual-use**; new `@spaarke/smart-todo-components` peer package; source is clean (runtime error = stale bundle, not code defect); BOTH widget paths share bundle | 020 | [widget-surface-audit.md](../notes/widget-surface-audit.md) |
| **R4-002** D resolver architecture | **Virtual PCF** `Spaarke.Controls.RegardingResolver` at `src/client/pcf/RegardingResolver/`; bound to hidden `sprk_regardingrecordtype`; mirrors `AssociationResolver` precedent (40/40 vs 22/40 vs 26/40 scoring) | 050, 051, 052 | [regarding-resolver-audit.md](../notes/regarding-resolver-audit.md) |
| **R4-003** G drill-through | Payload + modal-style render confirmed by MS Learn + 20+ in-repo precedents; **surfaced contract gap** → new task 034 | 080, 081-084 | [drill-through-spike.md](../notes/drill-through-spike.md) |
| **R4-004** useLaunchContext | Hook **EXISTS** (initial discovery wrong); **REPURPOSE + EXTEND** with `openTodos` discriminator; only 030 + 081-084 consume it (020/060 don't) | 030, 081-084 | [launch-context-decision.md](../notes/launch-context-decision.md) |

**New task added**: [034](034-B-extend-useLaunchContext.poml) — combines R4-003 + R4-004 follow-up (extend `useLaunchContext` with `openTodos` discriminator + `parseDataParams()` envelope consumption). Parallel-safe, blocks 081-084.

---

## Wave G1+2a Outcomes — 5 tasks complete (2026-06-10)

| Task | Deliverable | Tests | Build | Carry-forward findings |
|---|---|---|---|---|
| **010** RecordNavigationModalShell | 6 files (`tsx/styles/types/index/README/test`) in new shared-lib dir; postMessage dirty-check + origin allow-list; Fluent v9 + Griffel | 15 ✅ | clean | Task 011 must adapt: legacy `onNavigate(index)` vs new `onNavigate("prev"\|"next")`; arrow-key nav stays in `RichFilePreview.tsx`, NOT extracted; shell renders chrome only — dialog envelope + iframe stay with consumer |
| **012** Toolbar primitives | 3 components (SelectionAwareToolbar / ViewToggle / OrientationToggle); icon corrections from spec | 20 ✅ | clean | `LayoutRowTwoSplit20Regular` doesn't exist in `@fluentui/react-icons` v2.0.320 — used `LayoutRowTwo20Regular`. `<ToolbarButton>` rejects `appearance="outline"` — used `<Button size="small">` inside `<Toolbar>` landmark |
| **034** useLaunchContext extension | Hook 235→471 LOC, tests 217→410 LOC, parseDataParams extended with `search?` test-injection param, SmartTodoApp.tsx narrowing fix | 22 ✅ (8 narrowed + 14 new) | clean (9.53s build) | Test runner gap (no vitest/jest in SmartTodo) is **pre-existing**; tests are executable-spec shims. `entityType` derivation: `sprk_regarding<X>` prefix stripped → prepend `sprk_` → e.g., `sprk_regardingmatter` → `sprk_matter`. NEW behavior: hook clears `data=` envelope after first read |
| **050** RegardingResolver PCF | `src/client/pcf/RegardingResolver/` mirrors SemanticSearchControl nested layout; `ResolverWriteHandler::applyRegardingSelection` = SOLE write path; wraps `applyResolverFields`; nulls 10 other lookups before SET | 20 ✅ | `build:prod` clean, bundle 1.56 MiB | **Task 051 hard-blockers**: verify `sprk_regardingrecordtype` exists on To Do main form; pre-save handler for new-record CREATE transaction (Xrm.Page setValue staging — AssociationResolver pattern). Pinned `@fluentui/react-icons@^2.0.226` + `ajv@^8.20.0` workarounds — DO NOT bump |
| **080** Chart def records | 4 JSON files + `Create-UpcomingTodosChartDefinitions.ps1` + deploy-notes doc | n/a | JSON valid + PS syntax-checks | **Schema correction**: live column = `sprk_fetchxmlquery` (not `sprk_fetchxml` as spec used). Live deploy DEFERRED to user command. Task 081-084 must remove old `154bd4a4-...` UPCOMING TASKS chart def from Matter form; SmartTodo Code Page MUST deploy with `.html` suffix; R4-034 is hard prerequisite for drill-through pre-filter |

---

## How to Use This Index

- **🔲** Not started · **🔄** In progress · **✅** Complete · **❌** Blocked · **🔁** Needs retry
- **`task-execute`** is the canonical execution skill. Trigger it via "work on task NNN", "next task", "continue".
- Tasks in the same parallel group can run concurrently — invoke `task-execute` with multiple Skill calls in a single message.
- Status updates happen in this file AND in each task's POML `<metadata><status>`.
- Critical path = longest dependency chain that gates the wrap-up task.

---

## Phase / Wave Overview

| Phase / Wave | Tasks | Parallel-Safe | Estimated Effort | Gates |
|---|---|---|---|---|
| **Phase 0** Foundation (audits + spike) | 001, 002, 003, 004 | ✅ COMPLETE 2026-06-10 | ~3 hours wall-clock | Phase 2 task scopes gated |
| **Phase 1** Shared-lib hoist | 010 + 011 (serial) · 012 (parallel) | ⚠️ Mixed | 2-3 days | Gates Phase 2 Waves 2a/2b |
| **Phase 2 Wave 2a** Independent surfaces | 020, 030, 050, 080, 034 | ✅ All parallel | 3-4 days | Gates Wave 2b + 2c |
| **Phase 2 Wave 2a (B sub-tasks)** | 031, 032, 033 | ✅ Parallel after 030 | 2 days | — |
| **Phase 2 Wave 2a (D sub-tasks)** | 051, 052 | ⚠️ Serial after 050 | 1.5 days | Gates Wave 2c |
| **Phase 2 Wave 2b** SmartTodo Code Page work | 040, 060, 070, 071 | ⚠️ File-ownership care | 4-5 days | Gates 041, 042 |
| **Phase 2 Wave 2b (C sub-tasks)** | 041, 042 | ⚠️ Serial after 040 | 2 days | — |
| **Phase 2 Wave 2c** Parent-form Visual Host | 081, 082, 083, 084 | ✅ All parallel | 1-2 days | After 051 + 080 |
| **Phase 3** Deployment + Test | 092, 093, 094 | ⚠️ Serial | 2 days | After everything |
| **Phase 4** Wrap-up | 098 | — | 0.5 day | After 092/093/094 |

**Critical path** (post-Phase 0): 050 → 051 → 081 → 092 → 098 (~2 weeks if heavily parallelized). Phase 0 is now ✅ — the new gating chain is 050 (D resolver) → 051 (form add) → 081 (Visual Host on form) → 092 (deploy) → 098 (wrap-up).

---

## Tasks by Phase

### Phase 0: Foundation (Audits + Spike)

| Status | ID | Title | Tags | Parallel-Safe | Depends on | Blocks |
|:---:|:---|---|---|:---:|---|---|
| ✅ | [001](001-A-audit-widget-surface.poml) | A — Audit failing workspace widget surface(s) → Pattern D dual-use | audit, widget, smart-todo | ✅ | — | 020 |
| ✅ | [002](002-D-audit-regarding-resolver-architecture.poml) | D — Audit regarding resolver architecture → **virtual PCF** | audit, pcf, webresource, code-page | ✅ | — | 050 |
| ✅ | [003](003-G-spike-drill-through-url.poml) | G — Spike: drill-through URL contract → payload + 034 follow-up | spike, code-page, navigation | ✅ | — | 080, 034 |
| ✅ | [004](004-useLaunchContext-decision.poml) | useLaunchContext hook decision → **REPURPOSE + EXTEND** (hook exists; 034 implements) | audit, hook | ✅ | — | 034 (not 020/060 — corrected) |

---

### Phase 1: Shared-lib hoist

| Status | ID | Title | Tags | Parallel-Safe | Depends on | Blocks |
|:---:|:---|---|---|:---:|---|---|
| ✅ | [010](010-extract-RecordNavigationModalShell.poml) | Extract `<RecordNavigationModalShell>` from RichFilePreview.tsx → 6 files + 15 tests | shared-lib, hoist, fluent-v9 | ✅ | — | 011, 040, 041 |
| ✅ | [011](011-refactor-RichFilePreviewDialog.poml) | RichFilePreviewDialog refactored → adapter (`onNavigate(dir)` ↔ `onNavigate(idx)`); smart bypass when no nav props (zero visual change for LegalWorkspace consumer) | shared-lib, regression-safety | ❌ (after 010 ✅) | 010 ✅ | — |
| ✅ | [012](012-hoist-toolbar-primitives.poml) | Hoist toolbar primitives → 3 components + 20 tests; icon corrections noted | shared-lib, hoist, fluent-v9 | ✅ | — | 030, 032, 033, 070 |

---

### Phase 2 Wave 2a: Independent surfaces (parallel)

| Status | ID | Title | Tags | Parallel-Safe | Depends on | Blocks |
|:---:|:---|---|---|:---:|---|---|
| ✅ | [020](020-A-rebuild-workspace-widget.poml) | A — Pattern D widget shipped → new `@spaarke/smart-todo-components` peer package + LW shim with FeedTodoSyncContext lift. Builds clean (peer 0 err, LW 3,303 modules). Rich-feature LW Kanban hoist deferred by design | widget, smart-todo, deploy | ✅ | 001 ✅ | 092 |
| ✅ | [030](030-B-smarttodo-4row-layout.poml) | B — 4-row layout shipped (Header 257 LOC + styles + barrel). 3,259 modules clean. ViewToggle deferred → 033; OrientationToggle deferred → 070+ | code-page, smart-todo, ui | ✅ | 012 ✅ | 031, 032, 033, 040, 060 |
| ✅ | [050](050-D-implement-regarding-resolver.poml) | D — Virtual PCF resolver (mirrors AssociationResolver) → 20/20 tests, bundle 1.56 MiB | resolver, fluent-v9 | ✅ | 002 ✅ | 051, 052 |
| ✅ | [080](080-G-create-chart-definitions.poml) | G — 4 chart def JSONs + PS deploy script (live-deploy DEFERRED to user command) | dataverse-schema, deploy | ✅ | 003 ✅ | 081, 082, 083, 084 |

#### B sub-tasks (after 030)

| Status | ID | Title | Tags | Parallel-Safe | Depends on | Blocks |
|:---:|:---|---|---|:---:|---|---|
| ✅ | [031](031-B-assigned-to-me-filter.poml) | B — "Assigned to Me" is now the sole filter mode. Header.tsx Row 3 renders a single non-dismissible `<Tag>Assigned to Me</Tag>`; `MyTasksFilter.tsx` deleted; `TodoFilterMode` + `MyTasksFilterMode` types removed; `buildTodoItemsQuery(userId)` bakes `_sprk_assignedto_value eq ${userId}` into the filter unconditionally; useUserPreferences drops the `myTasksFilterMode` field (backwards-compatible on read). TypeScript clean across all 9 touched files. | smart-todo, filter | ✅ (with 032, 033) | 030 | — |
| ✅ | [032](032-B-selection-aware-toolbar-actions.poml) | B — Selection-aware toolbar actions (Open / Delete / Email / Pin) — wired via `createToolbarActions` from new `components/Toolbar/`; Open dispatches `sprk-smarttodo:open-todos` window event (task 040 listener); Delete confirms + parallel `Xrm.WebApi.deleteRecord`; Email composes mailto:; Pin any-unpinned ⇒ pin-all, all-pinned ⇒ unpin-all per spec FR-08; 20+ executable-spec test cases (no runner yet) | smart-todo, toolbar | ✅ (with 031, 033) | 012, 030 | 040 (Open action) |
| ✅ | [033](033-B-list-card-view-toggle.poml) | B — List/Card view toggle wired. New ListView + Header `viewMode` props consuming hoisted `<ViewToggle>`; persisted via `viewMode` field on `useUserPreferences` JSON envelope (preferencetype 100000000, no new optionset). Default = card. `npm run build` 3,274 modules 8.29s clean. | smart-todo, user-preference | ✅ (with 031, 032) | 012, 030 | — |
| ✅ | [034](034-B-extend-useLaunchContext.poml) | B — Extended useLaunchContext (235→471 LOC, 22 tests) **(NEW from Phase 0)** + parseDataParams extended | smart-todo, hook, url-params | ✅ | — | 081, 082, 083, 084 |

#### D sub-tasks (serial after 050)

| Status | ID | Title | Tags | Parallel-Safe | Depends on | Blocks |
|:---:|:---|---|---|:---:|---|---|
| ✅ | [051](051-D-add-to-todo-main-form.poml) | D — JS Web Resource `sprk_todo_regarding_presave.js` (347 LOC) + 9-section form-bind instructions doc. Live form-designer steps deferred to user. **PCF enhancement follow-up flagged** (see Wave outcomes below) | dataverse, form-designer, deploy | ❌ (after 050 ✅) | 050 ✅ | 081-084 |
| ✅ | [052](052-D-read-only-mode.poml) | D — Read-only mode verified + hardened. RegardingResolverHost already detects `context.mode.isControlDisabled` (lines 38-44); added defensive write-gates in `handleSelectRecord` + `handleClear` (early-return if `readOnly`); pre-save handler skips `formType === 3/4`; +3 unit tests (23 passing); PCF v1.0.0 → v1.1.0; webpack alias stubs `@spaarke/sdap-client` (PR #369 cascade workaround). Prod bundle 1.57 MiB (+10 KiB vs 050) | regarding, security | ❌ (after 051) | 050, 051 | — |

---

### Phase 2 Wave 2b: SmartTodo Code Page work (file-ownership care)

> ⚠️ Tasks 040, 060, 070 all touch `src/solutions/SmartTodo/`. If file-ownership conflicts arise, serialize 040 → 060 → 070. Default plan: parallel with care.

| Status | ID | Title | Tags | Parallel-Safe | Depends on | Blocks |
|:---:|:---|---|---|:---:|---|---|
| ✅ | [040](040-C-wire-smarttodo-modal.poml) | C — Wire SmartTodo card-open to `<RecordNavigationModalShell>` with iframe | modal, iframe, smart-todo | ⚠️ | 010, 030 | 041, 042 |
| ✅ | [060](060-E-card-affordances.poml) | E — Card affordances (Open icon, double-click, selection checkbox) | smart-todo, ui | ⚠️ | 012, 030, 040 | — |
| ✅ | [070](070-F-vertical-kanban-orientation.poml) | F — Vertical Kanban orientation toggle (CSS transform-only, no DOM reflow) | smart-todo, ui, layout | ⚠️ | 012, 030 | 071 |

#### Sub-tasks (serial after parents)

| Status | ID | Title | Tags | Parallel-Safe | Depends on | Blocks |
|:---:|:---|---|---|:---:|---|---|
| ✅ | [041](041-C-dirty-check-cross-frame-messaging.poml) | C — Cross-frame dirty-check postMessage protocol (Wave C — JS web resource + shell + bind instructions doc) | cross-frame-messaging, dataverse-form | ❌ | 010, 040 | — |
| ✅ | [042](042-C-retire-TodoDetailPanel.poml) | C — Retire TodoDetailPanel side-pane | cleanup | ❌ | 040 | — |
| ✅ | [071](071-F-orientation-persistence.poml) | F — Persist orientation via sprk_userpreference | user-preference | ❌ | 070 | — |

---

### Phase 2 Wave 2c: Parent-form Visual Host (parallel — 4 forms)

| Status | ID | Title | Tags | Parallel-Safe | Depends on | Blocks |
|:---:|:---|---|---|:---:|---|---|
| ✅ | [081](081-G-visualhost-on-matter-form.poml) | G — Visual Host on Matter main form (10-section maker doc — Wave A) | dataverse, form-designer | ✅ | 051, 080, **034** | 092 |
| ✅ | [082](082-G-visualhost-on-project-form.poml) | G — Visual Host on Project main form (instructions doc — Wave B parallel) | dataverse, form-designer | ✅ | 051, 080, **034** | 092 |
| ✅ | [083](083-G-visualhost-on-invoice-form.poml) | G — Visual Host on Invoice main form → 10-section maker doc cloned from 081 template with Invoice substitutions | dataverse, form-designer | ✅ | 051, 080, **034** | 092 |
| ✅ | [084](084-G-visualhost-on-workassignment-form.poml) | G — Visual Host on WorkAssignment main form (instructions doc — Wave B parallel) | dataverse, form-designer | ✅ | 051, 080, **034** | 092 |

---

### Phase 3: Deployment + Testing (serial)

| Status | ID | Title | Tags | Parallel-Safe | Depends on | Blocks |
|:---:|:---|---|---|:---:|---|---|
| 🔄 | [092](092-deploy-all-affected-solutions.poml) | Deploy all affected solutions to spaarkedev1 — **IN PROGRESS** (2026-06-16: SmartTodo + SpaarkeAi + RegardingResolver v1.1.0 + dirty-check JS deployed; golden-path smoke test pending) | deploy, smoke-test | ❌ | all 020-084 | 093, 094 |
| 🔲 | [093](093-ui-test-suite-nfr-validation.poml) | UI test suite for NFR-05 / NFR-07 / NFR-08 | ui-test, a11y, performance | ❌ | 092 | 098 |
| ✅ | [094](094-grep-sweep-sprk-todoflag.poml) | Final grep sweep: 0 functional `sprk_todoflag` hits (2026-06-12; 45 comment/guard lines classified; stale SpeDocumentViewer bundle.js flagged → follow-up #6) | regression, grep | ❌ | ~~092~~ (re-sequenced first per user decision) | 098 |

---

### Phase 4: Wrap-up

| Status | ID | Title | Tags | Parallel-Safe | Depends on | Blocks |
|:---:|:---|---|---|:---:|---|---|
| 🔲 | [098](098-project-wrap-up.poml) | Project wrap-up — lessons-learned, README, repo-cleanup, PR | wrap-up, docs, pr | ❌ | 092, 093, 094, **099, 100, 101, 102, 103, 104** | — |

---

### Wave D — Widget parity follow-up (post-PR-384 UAT, 2026-06-18)

UAT screenshot 2026-06-18 surfaced 6 widget issues; diagnostic + plan in [d-widget-parity-audit-2026-06-18.md](../notes/d-widget-parity-audit-2026-06-18.md). All three land in ONE PR; widget feature parity is the user-facing acceptance criterion. **098 wrap-up depends on Wave D complete.**

| Status | ID | Title | Tags | Parallel-Safe | Depends on | Blocks |
|:---:|:---|---|---|:---:|---|---|
| ✅ | [099](099-W1-widget-chrome-pattern-d.poml) | W-1 — Widget chrome consolidation + Pattern D alignment shipped. LW shim collapsed to Calendar shape (structural-only: no section toolbar; `title: "Smart To Do"` only). Widget `<PaneHeader>` removed; single `<Toolbar>` with `[SearchBox, +, Open, refresh]` in that order. Local debounced (150 ms) in-memory search across `sprk_name + sprk_description`; clears restore full list. Single-select `selectedId` powers selection-aware Open (disabled until 1 card selected). Co-exists cleanly with R4-101's grouped rendering (shared `filteredItems`). Builds: peer tsc 0 err; LegalWorkspace Vite 3,312 modules 13.51s 2,252 kB. Closes UAT 2 + 3 + 5. | widget, pattern-d, fluent-v9 | ✅ (parallel with 101) | 020, 030 | 100 |
| ✅ | [100](100-W2-open-to-form-refetch.poml) | W-2 — Open-to-form launch protocol + post-wizard-close refetch (closes UAT 1/4: `useLaunchContext.openTodo` discriminator auto-mounts `<SmartTodoModal>`; BroadcastChannel `sprk_todo:created` wires refetch on wizard close) | widget, code-page, launch-context, modal | ❌ (serial after 099 on `todo.registration.ts`) | 099, 034, 040 | 098 |
| ✅ | [101](101-W3-grouping-hoist.poml) | W-3 — Today/Tomorrow/Future grouping shipped. Hoisted `useKanbanColumns` into `@spaarke/smart-todo-components` (~340 LOC hook + `bucketTodoItems` pure helper + `IKanbanTodoLike`/`IKanbanDataverseService` types). Code Page consumer swapped to `@spaarke/smart-todo-components` import + passes `dataverseService: serviceRef.current` (structurally compatible). Widget renders 3 grouped sections via `bucketTodoItems(filteredItems, 60, 30)` with R4-099's single-select `selectedId` carried across groups. Builds: peer tsc 0 err; SmartTodo 3,279 modules 1,745 kB; LegalWorkspace 3,312 modules 2,252 kB. R4-020 deferred 13-file follow-up closed at hook-scope (full Kanban hoist still future work if surfaced). | widget, peer-package, hoist, fluent-v9 | ✅ (parallel with 099) | 020, 030 | 098 |

---

### Wave E — Widget/App parity round 2 (post-Wave-D UAT, 2026-06-18)

UAT round 2 (post-Wave-D deploy) surfaced 11 functional gaps: 7 on the widget (drag-drop + multi-select + orientation + column colors + capital-case + search-as-icon + quick-add + always-on Open) and 4 on the app (consolidated chrome + smaller quick-create + 3-col default + single toolbar). Diagnostic + plan in [e-widget-app-parity-audit-2026-06-18.md](../notes/e-widget-app-parity-audit-2026-06-18.md). All three land in ONE PR; full widget/app parity is the user-facing acceptance criterion. **098 wrap-up depends on Wave E complete.**

| Status | ID | Title | Tags | Parallel-Safe | Depends on | Blocks |
|:---:|:---|---|---|:---:|---|---|
| ✅ | [102](102-E1-widget-kanban-hoist.poml) | E-1 — Widget Kanban hoist shipped. Created `components/KanbanCard/` (368 LOC hoisted card, bit-for-bit visual + interaction parity with app) + `components/SmartTodoKanban/` (197 LOC domain composer wrapping `<KanbanBoard>` + `useKanbanColumns` + `<KanbanCard>` with drag-end / pin handlers) + `utils/todoScoring.ts` (shared score + due-label helpers) in `@spaarke/smart-todo-components`. Widget swapped grouped lists → `<SmartTodoKanban>`; selection model upgraded from `string\|null` → `Set<string>`; `<OrientationToggle>` added to toolbar with local state; built `dataverseService` adapter wrapping `webApi.updateRecord` for drag-drop + pin persistence. Code Page: import-source swap only (`./KanbanCard` → peer package); local `KanbanCard.tsx` (565 LOC) deleted. DnD lib clarified — `@hello-pangea/dnd` (not react-dnd) added as peerDep. SpaarkeAi vite.config added `@spaarke/sdap-client` workaround alias (7th surface; pre-existing PR #369 cascade — broke on master before this task too). Builds: peer pkg tsc clean; SmartTodo 1.75 MB / 475 KB gzip; LegalWorkspace 2.26 MB / 627 KB gzip; SpaarkeAi 3.85 MB / 1.05 MB gzip — all 4 green. Closes R4-020 deferred 13-file rich-feature subtree follow-up. | widget, peer-package, hoist, hello-pangea-dnd, kanban | ✅ (parallel with 104) | 020, 099, 101 | 103, 098 |
| ✅ | [103](103-E2-widget-toolbar-polish.poml) | E-2 — Widget toolbar polish shipped. Toolbar reorganised to `[+ wizard][QuickAdd Input][Add btn] \| (spacer) \| [Open][Refresh][Orient][Search icon]`. Search becomes `<ToggleButton>` (Search20Regular icon, right-aligned) — toggling expands a SearchBox row BELOW the toolbar with autofocus; toggling OFF clears the query so filter and search-visibility stay coupled. Open is ALWAYS-ENABLED: 0 selected → `onOpenTodo()` no-arg → LW shim opens SmartTodo Code Page with NO launch data → app renders default Kanban view; 1+ → `onOpenTodo(first)` → existing R4-100 `openTodo` envelope path → auto-mounts modal. Open tooltip switches text by selection count. Column tints (UAT 5): `IKanbanColumn<T>` extended with optional `tintColor`; `bucketTodoItems` sets `colorPaletteRedBackground1`/`YellowBackground1`/`GreenBackground1`; `KanbanBoard.tsx` composes inline `style` from `accentColor` (top-border, existing) + `tintColor` (bg, new) — both backwards-compatible. Capital Case labels: already correct at `bucketTodoItems` source; `KanbanBoard.columnTitle` has no text-transform; widget's legacy `groupTitle` (with uppercase) is now dead code preserved for back-compat. Quick-add (UAT 7): inline Input + Add btn in left toolbar slot; Enter/click calls `webApi.createRecord('sprk_todo', {sprk_name: title})`; success clears + refetches; failure surfaces `<MessageBar intent="warning">` with `<Link>` "Open full wizard" recovery delegating to existing `onAddTodo`. `IWebApi` extended with optional `createRecord` — legacy hosts that omit it auto-suppress the QuickAdd group. LW shim's `handleOpenTodo` widened to `(todoId?: string) => void` (passes undefined data when no todoId). All 4 builds green: peer pkg tsc clean; SmartTodo 1.74 MB / 475 KB gzip; LegalWorkspace 2.27 MB / 628 KB gzip; SpaarkeAi 3.85 MB / 1.05 MB gzip. ADR-021 binding: semantic tokens only (no hex), Griffel for all static rules, runtime-data-driven inline styles only for tint/accent (established pattern). | widget, fluent-v9, toolbar, ux-polish | ❌ (serial after 102 — uses hoisted Kanban container's column-style hook points) | 099, 102 | 098 |
| ✅ | [104](104-E3-app-chrome-consolidation.poml) | E-3 — App chrome consolidation shipped. Header.tsx rewritten as single Fluent v9 `<Toolbar>` landmark (was R4-030 4-row): `[ToDoIcon + "Smart To Do"] [QuickAdd Input + Add] | (spacer) | [Refresh + ViewToggle + OrientationToggle? + Settings + SearchBox]` — OR — selection-mode `[SelectionAwareToolbar + SearchBox]` when count > 0. QuickAdd dispatches new `QUICK_ADD_TODO_EVENT` window event consumed by SmartToDo (single-source handleAdd). SmartToDo gained `hideHeader` + `onSettingsOpenerReady` props so the consolidated Header owns chrome and the inner KanbanHeader is suppressed; threshold popover stays mounted (hidden anchor) so the gear button can open it via callback. Orientation hoisted to App-level via existing useUserPreferences envelope. viewMode default unchanged (`'card'` already maps to 3-col Kanban — audit's "rename to kanban" turned out functionally no-op). All Wave 2a/2b features preserved (R4-031/032/033/070/040). Build: SmartTodo Vite 3,286 modules / 1,753 kB / 476 kB gzip clean; LegalWorkspace Vite 3,319 modules / 2,259 kB clean. | code-page, fluent-v9, header, toolbar, ux-polish | ✅ (parallel with 102) | 030, 033, 099 | 098 |

---

## Parallel Execution Groups

| Group | Tasks | Prerequisite | Max Concurrent | Notes |
|---|---|---|:---:|---|
| **G0 — Foundation audits** | 001, 002, 003, 004 | (none) | 4 | All independent; each writes its own `notes/*.md` |
| **G1a — Hoist parallel** | 010, 012 | (none) | 2 | Independent shared-lib components |
| **G1b — Hoist regression** | 011 | 010 | 1 | Serial after 010 |
| **G2a — Independent surfaces** | 020, 030, 050, 080, **034** | G0 ✅ | 5 | Disjoint file scopes; A/B/D/G surfaces + new useLaunchContext extension |
| **G2a-B** | 031, 032, 033 | 030 + 012 (for 032/033) | 3 | Same Code Page but disjoint refactor scopes |
| **G2a-D-serial** | 051 → 052 | 050 | 1 each | Form-designer change then read-only mode |
| **G2b — Code Page wave** | 040, 060, 070 | 010/012/030/040 (per task) | up to 3 (with file-ownership care) | All touch `src/solutions/SmartTodo/`. If conflict, serialize 040 → 060 → 070 |
| **G2b-after** | 041, 042, 071 | 010 + 040 (for 041, 042); 070 (for 071) | 3 | Serial after their parent tasks |
| **G2c — Parent forms** | 081, 082, 083, 084 | 051 + 080 | 4 | Each touches a different form |
| **G3 — Deploy + Test** | 092 → 093 + 094 | All G2 complete | 1 then 2 | 093 + 094 parallel after 092 |
| **GD-1 — Widget parity parallel** | 099, 101 | 020 + 030 (both ✅) | 2 | Disjoint files: 099 = shim + widget chrome; 101 = peer package hook hoist + widget rendering |
| **GD-2 — Widget parity serial** | 100 | 099 + 034 + 040 (all ✅ after 099 lands) | 1 | Touches `todo.registration.ts` after 099's cleanup; serial dependency |
| **GE-1 — Widget/app parity parallel** | 102, 104 | 099 + 101 (both ✅) | 2 | Disjoint files: 102 = peer package full Kanban hoist + widget consumer; 104 = app Header collapse + viewMode default |
| **GE-2 — Widget toolbar polish serial** | 103 | 102 ✅ (uses hoisted Kanban container's column-style hook points) | 1 | Serial after 102; same widget files |
| **G4 — Wrap-up** | 098 | G3 + GD + GE complete | 1 | Final task — also depends on Wave E |

**Max-concurrency rule** (from project-pipeline skill): **6 agents per wave hard limit**. G2a + G2a-B (4 + 3 = 7) requires staging or single-agent fallback for the last task.

**Sub-Agent Write Boundary** (from root CLAUDE.md §3): None of these tasks touch `.claude/` paths, so all are sub-agent-dispatchable. If any future task adds `.claude/` writes, mark `parallel-safe: false` and run in main session only.

---

## Dependency Graph (text rendering)

```
G0 Foundation ✅ COMPLETE (2026-06-10)
  001 ✅  002 ✅  003 ✅  004 ✅

G1 Shared-lib hoist
  010 → 011
  012 (parallel with 010, 011)

G2a Independent surfaces (parallel after G0 ✅ / G1)
  020 ← 001 ✅
  030 ← 012 → 031, 032 (← 012, 030), 033 (← 012, 030)
  050 ← 002 ✅ → 051 → 052
  080 ← 003 ✅ → 081, 082, 083, 084 (← 051, 080, 034)
  034 ← (no deps; from Phase 0 aggregation) — blocks 081-084

G2b SmartTodo Code Page (after G1, G2a-B)
  040 ← 010, 030 → 041 (← 010), 042
  060 ← 012, 030, 040
  070 ← 012, 030 → 071

G3 Deployment (after everything)
  092 ← all 020-084 → 093, 094 (parallel)

GD Widget parity (post-PR-384 UAT 2026-06-18)
  099 ← 020, 030 (chrome + Pattern D alignment)
  101 ← 020, 030 (grouping hoist) — parallel with 099
  100 ← 099, 034, 040 (open + refetch) — serial after 099

GE Widget/App parity round 2 (post-Wave-D UAT 2026-06-18)
  102 ← 020, 099, 101 (full Kanban hoist + drag-drop + multi-select + orientation + pin)
  104 ← 030, 033, 099 (app chrome consolidation + 3-col default) — parallel with 102
  103 ← 099, 102 (toolbar polish + search-icon + always-Open + column colors + Capital case + quick-add) — serial after 102

G4 Wrap-up
  098 ← 092, 093, 094, 099, 100, 101, 102, 103, 104
```

---

## High-Risk Tasks

| Task | Risk | Mitigation |
|---|---|---|
| **010** | Shell abstraction wrong (regression in 011) | Task 011 is the regression check; if shell is wrong, fix in 010 before progressing |
| **041** | postMessage blocked under MDA security headers | Spike inside 041 first; fallback to query-string signal documented |
| **050** | Audit picks wrong architecture | Audit doc is the binding decision; reviewer judgment on rationale |
| **081-084** | Form-designer changes need solution import; one mistake replicates | Smoke-test each form independently before next; deploy via dataverse-deploy skill |
| **092** | Deployment order matters (shared lib first, then consumers) | Document explicit deploy order; verify each bundle before proceeding |

---

## Coordination with Parallel Projects

| Branch / PR | Overlap | Action |
|---|---|---|
| **PR #372** `feature/ai-spaarke-ai-workspace-UI-r1` | `Spaarke.AI.Widgets` (R4-020) | Coordinate at task 001 audit time; rebase if #372 merges first |
| **work/spaarke-datagrid-framework-r1** (55 unmerged, no PR) | `Spaarke.UI.Components` (R4-010, R4-012, R4-040) | Sequence R4-010 + R4-012 AFTER datagrid-framework merge if possible; resolve at PR time |
| **work/matter-ui-r1-v1.1.72-vh-polish** (18 unmerged) | Visual Host (R4-081) | Monitor; coordinate if Visual Host source changes |

---

## Wave G1+2a-followups Outcomes — 4 tasks complete (2026-06-10)

| Task | Deliverable | Build | Carry-forward findings |
|---|---|---|---|
| **011** RichFilePreviewDialog refactor | Adapter (`onNavigate(dir)` → `onNavigate(idx)`); 10 tests; 25/25 across dialog+shell | `tsc` clean | **Smart bypass**: non-nav path bypasses shell entirely → zero visual change for LegalWorkspace (only real consumer). **Shell-API feedback (deferred)**: duplicate-title-bar when nav props supplied AND content has its own title bar (RichFilePreview case). Two API options proposed: `chromeMode?: 'full'\|'content-only'` on shell OR `suppressTitleBar?` on RichFilePreview. Not blocking task 040 (iframe-embedded MDA form is self-chromed). |
| **020** SmartTodo widget rebuild (Pattern D) | New `@spaarke/smart-todo-components` peer package (widget 448 LOC + types + tests) + LW shim rewired in `todo.registration.ts` + LW `package.json` adds workspace dep | Peer pkg `tsc --noEmit` 0 err; LW `npm run build` 3,303 modules 14.98s clean | **Agent hit stream timeout at 52min / 81 tool uses** — operational not work-quality. Main session did POML closeout. **Deliberate scope decision** (documented in `todo.registration.ts` comments): rich-feature LW Kanban 13-file subtree (score cards, dismissed section, threshold settings, AI summary dialog) NOT hoisted in initial 0.1.0 — deferred. Satisfies FR-02 (sprk_todoflag eliminated) + FR-04 + FR-05. `FeedTodoSyncContext` lives in shim per user decision. |
| **030** SmartTodo 4-row layout | New `Header/` (257 LOC + styles + barrel) + SmartTodoApp.tsx | `npm run build` 3,259 modules 11.62s clean | App-level state: `searchQuery`, `selectedIds: Set<string>` at `SmartTodoLayout:~109`. Stub toolbar actions at `SmartTodoApp.tsx:115-139` → 032 replaces. ViewToggle deferred → 033; OrientationToggle → 070+. Outlook ribbon createTodo flow preserved verbatim. |
| **051** D form-binding + pre-save handler | `src/client/webresources/js/sprk_todo_regarding_presave.js` (347 LOC) + 9-section instructions doc | JS syntax clean | Solution wrappers deferred to R4-092. **🚨 PCF enhancement follow-up needed**: `RegardingResolverApp.tsx handleSelectRecord` must populate `window.__sprk_regarding_pending__` on CREATE so OnSave handler can stage fields. Without it, 5 companion fields stay empty after first CREATE save. Sketch in instructions doc. **Must land before R4-092 deploy.** |

---

## Follow-ups Surfaced (Not Yet Filed As Tasks)

| # | Follow-up | Source | Priority | Notes |
|---|---|---|---|---|
| 1 | **PCF CREATE-mode bridge**: `RegardingResolverApp.tsx handleSelectRecord` populates `window.__sprk_regarding_pending__` on CREATE | R4-051 finding | **High** — blocks D path on new records; must land before R4-092 deploy | Sketch in `notes/d-form-bind-instructions.md` |
| 2 | **Shell API tweak**: `<RecordNavigationModalShell>` `chromeMode?: 'full'\|'content-only'` OR `<RichFilePreview>` `suppressTitleBar?` | R4-011 finding | Low — not blocking current R4; SmartTodo iframe self-chromed | Two options in R4-011 notes |
| 3 | **PR #376 overlap**: `WidgetErrorBoundary.tsx` rewrite (17 add / 35 del) on `feature/ai-workspace-ui-r1-followups` — R4-020 LW shim consumes WidgetErrorBoundary | external PR | **Monitor** — open, not merged today | Re-verify usage when #376 merges |
| 4 | **SmartTodo test runner**: pre-existing — no vitest/jest. 22 useLaunchContext tests are executable-spec shims | R4-034 / pre-existing | Low | ~2hr to wire vitest |
| 5 | **LW Kanban rich-feature hoist**: 13-file subtree NOT hoisted into `@spaarke/smart-todo-components` 0.1.0 | R4-020 deliberate | Medium — future widget consumers need it; LW shim ships full features today | In R4-020 deliverable comments |
| 6 | **Stale SpeDocumentViewer solution bundle**: checked-in `solution/.../bundle.js` (v1.0.16, built 2026-05-11) embeds pre-R3 legacy code incl. live `entity['sprk_todoflag'] = true` write path; deployed v1.0.16 carries same latent code | R4-094 sweep finding | **Medium-High** — rebuild + version-bump + repack against current shared libs in R4-092 master-side deploy (or verify legacy To Do wizard path unreachable) | `notes/grep-sweep-result.md` §3 |

---

*To execute a task, say "work on task NNN" (or "continue" for the next 🔲). The `task-execute` skill handles knowledge loading, checkpointing, quality gates, and TASK-INDEX status updates.*
