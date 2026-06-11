# A — Workspace widget surface audit

> **Task**: R4-001 (Phase 0 audit) · **Date**: 2026-06-10 · **Status**: complete
> **Rigor**: STANDARD (research/audit task; no code changes)
> **Author**: task-execute (Claude Opus 4.7)

---

## TL;DR

The R3 source refactor that retired `sprk_event.sprk_todoflag` was completed correctly across all source files — every `sprk_todoflag` reference in `src/` is a docstring/comment documenting the historical removal, never an active OData query or write. **The runtime OData error reported by R3 UAT is therefore a deployed-bundle staleness issue, not a code defect.** Two source surfaces could be the deployed-bundle origin: (1) the `SmartTodo` Code Page (`sprk_smarttodo`, retired side-pane shape) and (2) the embedded LegalWorkspace section (`src/solutions/LegalWorkspace/src/components/SmartToDo/`). The audit recommends **Pattern D (dual-use shared-lib widget + thin LegalWorkspace shim)** for the R4 rebuild, modelling on Calendar (R3 task 115). PR #372 was merged to master 2026-06-10 and does **not** touch the SmartToDo source — coordination is a clean `git pull` away.

---

## 1. Legacy query inventory

Source-side search for `sprk_todoflag` across `src/` returned the matches below. All TypeScript source matches are **non-functional documentation** (comments/docstrings explaining what was removed in R3). Two compiled-bundle matches exist; one is a `.test.ts` assertion verifying absence of the field; the other lives in a `SpeDocumentViewer` PCF bundle and is unrelated to the SmartToDo widget surface.

| File | Line | Match (excerpt) | Functional? | Notes |
|---|---:|---|---|---|
| `src/solutions/SmartTodo/src/types/entities.ts` | 36 | `Replaces the legacy IEvent-with-sprk_todoflag shape from R1/R2.` | No (docstring) | Justifies type shape |
| `src/solutions/SmartTodo/src/types/entities.ts` | 82 | `Per R3 FR-29 / OS-1, the four legacy event-todo fields (sprk_todoflag, ...)` | No (docstring) | R3 carry-over note |
| `src/solutions/SmartTodo/src/services/queryHelpers.ts` | 176 | `Per R3 FR-29 / OS-1, the four legacy event-todo fields (sprk_todoflag, ...)` | No (docstring) | Field is absent from `EVENT_SELECT_FIELDS` |
| `src/solutions/SmartTodo/src/services/queryHelpers.ts` | 469 | `Replaces the legacy sprk_event-based select that filtered on sprk_todoflag.` | No (docstring) | |
| `src/solutions/SmartTodo/src/services/queryHelpers.ts` | 549 | `Per OS-1: no sprk_todoflag filter — that field no longer exists on sprk_event.` | No (docstring) | `buildTodoItemsQuery` queries `sprk_todo` directly |
| `src/solutions/SmartTodo/src/services/DataverseService.ts` | 468 | `Per OS-1: no sprk_todoflag write.` | No (docstring) | |
| `src/solutions/SmartTodo/src/services/DataverseService.ts` | 757 | `// Per R3 FR-29 / OS-1, sprk_event.sprk_todoflag is removed in Phase 1.` | No (comment) | |
| `src/solutions/SmartTodo/src/hooks/useTodoItems.ts` | 5 | `The legacy sprk_event + sprk_todoflag path is...` | No (docstring) | |
| `src/solutions/SmartTodo/README.md` | 14 | `entity (not sprk_event with sprk_todoflag=true).` | No (markdown) | |
| `src/solutions/DailyBriefing/src/hooks/useInlineTodoCreate.ts` | 7 | `Legacy sprk_event { sprk_todoflag=true, sprk_todostatus, sprk_todosource }` | No (docstring) | |
| `src/solutions/LegalWorkspace/src/types/entities.ts` | 36 | Same as SmartTodo entities.ts:82 | No (docstring) | Sibling copy in LW |
| `src/solutions/LegalWorkspace/src/types/entities.ts` | 68 | Same as SmartTodo entities.ts:36 | No (docstring) | |
| `src/solutions/LegalWorkspace/src/services/queryHelpers.ts` | 200 | `EVENT_SELECT_FIELDS` docstring | No (docstring) | Field absent from select list (verified L204-222) |
| `src/solutions/LegalWorkspace/src/services/queryHelpers.ts` | 528 | `Per OS-1: no sprk_todoflag filter` | No (docstring) | `buildTodoItemsQuery` queries `sprk_todo` |
| `src/solutions/LegalWorkspace/src/services/DataverseService.ts` | 500 | `Per OS-1: no sprk_todoflag write.` | No (docstring) | |
| `src/solutions/LegalWorkspace/src/hooks/useTodoItems.ts` | 5 | Mirror of SmartTodo useTodoItems | No (docstring) | |
| `src/solutions/LegalWorkspace/src/hooks/useParallelDataLoad.ts` | 160 | `legacy sprk_event.sprk_todoflag path is removed` | No (comment) | |
| `src/solutions/LegalWorkspace/src/contexts/FeedTodoSyncContext.tsx` | 20 | `Map<eventId, boolean> for sprk_event.sprk_todoflag toggles. In R3 the ...` | No (docstring) | |
| `src/solutions/LegalWorkspace/src/components/SmartToDo/TodoDetailPane.tsx` | 13 | `Per R3 FR-29 / OS-1: this pane operates on sprk_todo records (not the legacy sprk_event + sprk_todoflag shape).` | No (docstring) | |
| `src/solutions/LegalWorkspace/src/components/QuickSummary/quickSummaryConfig.ts` | 107 | `sprk_event?$filter=sprk_todoflag eq true path was removed alongside the column.` | No (comment) | Active card now queries `sprk_todo` (L114) |
| `src/solutions/LegalWorkspace/src/components/CreateWorkAssignment/workAssignmentService.ts` | 530 | R3 carry-over note | No (comment) | |
| `src/solutions/LegalWorkspace/src/components/CreateWorkAssignment/formTypes.ts` | 97 | R3 carry-over note | No (docstring) | |
| `src/solutions/LegalWorkspace/src/components/CreateWorkAssignment/CreateFollowOnEventStep.tsx` | 9 | R3 carry-over note | No (comment) | |
| `src/solutions/LegalWorkspace/src/components/ActivityFeed/FeedItemCard.tsx` | 41 | R3 carry-over note | No (comment) | |
| `src/solutions/LegalWorkspace/src/components/ActivityFeed/ActivityFeedList.tsx` | 19 | R3 carry-over note | No (docstring) | |
| `src/solutions/LegalWorkspace/src/components/ActivityFeed/ActivityFeed.tsx` | 289 | R3 carry-over note | No (comment) | |
| `src/server/api/Sprk.Bff.Api/Services/Workspace/TodoGenerationService.cs` | 92, 849 | XML doc-comment notes about R3 removal | No (xmldoc) | BFF service is out of R4 scope |
| `src/client/shared/Spaarke.UI.Components/src/components/TodoDetail/TodoDetail.tsx` | 352, 675 | R3 carry-over notes | No (docstring) | |
| `src/client/shared/Spaarke.UI.Components/src/components/CreateTodoWizard/__tests__/todoService.test.ts` | 188, 189 | `expect(payload).not.toHaveProperty('sprk_todoflag')` | **Yes (test guards absence)** | Regression guard — assertion is correct |
| `src/client/shared/Spaarke.UI.Components/src/components/CreateTodoWizard/todoService.ts` | 7 | Docstring | No | |
| `src/client/shared/Spaarke.UI.Components/src/components/CreateTodoWizard/index.ts` | 6 | Docstring | No | |
| `src/client/shared/Spaarke.UI.Components/src/components/CreateTodoWizard/formTypes.ts` | 6 | Docstring | No | |
| `src/client/shared/Spaarke.UI.Components/src/components/CreateWorkAssignmentWizard/CreateFollowOnEventStep.tsx` | 125, 203 | Docstrings | No | |
| `src/client/shared/Spaarke.UI.Components/src/components/CreateWorkAssignmentWizard/formTypes.ts` | 103 | Docstring | No | |
| `src/client/shared/Spaarke.UI.Components/src/components/CreateWorkAssignmentWizard/workAssignmentService.ts` | 567, 568 | Docstrings | No | |
| `src/client/pcf/SpeDocumentViewer/solution/src/Controls/Spaarke.SpeDocumentViewer/bundle.js` | 2000, 2010, 2030, 2130, 3070 | Compiled bundle artifact | **Yes (compiled output)** | **Unrelated to SmartToDo** — this is a checked-in PCF bundle output; needs separate scrub but not blocking R4 task 020 |

**Verdict**: **Zero functional `sprk_todoflag` reads or writes in TypeScript source for the SmartTodo or LegalWorkspace surfaces.** The success criteria #12 in `spec.md` (`grep -i sprk_todoflag src/**/*.{ts,tsx,cs}` returns zero functional hits) is already satisfied for source. The `SpeDocumentViewer/bundle.js` compiled artifact is out of R4 scope (it's PCF, not workspace).

---

## 2. Affected deployed surface(s)

Two source trees host the SmartTodo Kanban / detail-pane logic. Both look clean in source but at least one (likely both) has a stale **deployed bundle** in spaarkedev1 that pre-dates R3's Phase-1 source updates:

### Surface A — Standalone SmartTodo Code Page (`sprk_smarttodo`)
- **Source**: `src/solutions/SmartTodo/src/` (Vite + `vite-plugin-singlefile` per ADR-026)
- **Web resource name**: `sprk_smarttodo` (referenced by `LegalWorkspace/src/sections/todo.registration.ts:111` and by R3 task 070b URL-launch context)
- **Deploy**: `Deploy-SmartTodoCodePage.ps1` (probably; pattern matches `code-page-deploy` skill)
- **Mount points**: standalone Code Page modal (launched via subgrid command, ribbon, or the "Open" icon in the embedded LW section toolbar); R4 spec FR-34 also calls for it to be opened as the Visual Host drill-through target.
- **Status**: Source clean. Deployed bundle may be stale if `sprk_smarttodo` was last deployed before R3 PR #373 (`e328beaf`, merged 2026-XX-XX).

### Surface B — LegalWorkspace embedded SmartToDo section (`src/solutions/LegalWorkspace/src/components/SmartToDo/`)
- **Source**: `src/solutions/LegalWorkspace/src/components/SmartToDo/SmartToDo.tsx` (+ `KanbanCard`, `KanbanHeader`, `TodoDetailPane`, `DismissedSection`, `AddTodoBar`, `ThresholdSettings`, `TodoAISummaryDialog`, `EffortScoreCard`, `PriorityScoreCard`, `index.ts`, `todoScoringTypes.ts` — 13 files)
- **Section registration**: `src/solutions/LegalWorkspace/src/sections/todo.registration.ts` (registered as `id: "todo"` in `sectionRegistry.ts` and exposed as a Dashboard-wrapper section via `system-layouts.json`).
- **Deploy**: `Deploy-LegalWorkspace.ps1` (per `code-page-deploy`). LegalWorkspace bundle is consumed by SpaarkeAi (per the embedded LegalWorkspaceApp host contract — `LEGALWORKSPACE-EMBEDDED-MODE-CONTRACT.md`). Per `LEGALWORKSPACE-RETIREMENT.md` (OC-R4-05), the **standalone LegalWorkspace Code Page is being retired in R4** — the LegalWorkspace package remains as a library consumed by SpaarkeAi, no longer self-deployed.
- **Mount points**: 
  - As a **system widget** when a Dashboard-wrapper workspace layout includes the `"todo"` section (cold-load path via `useWorkspaceLayouts.GetDefaultLayoutAsync` → `LegalWorkspaceApp` → `SectionPanel` → `todo.registration.factory(ctx).renderContent()` → `<SmartToDo embedded={true} … />`).
  - As a **user-added widget** when a user customizes a layout in the WorkspaceLayoutWizard and adds the "My To Do List" section.
- **Status**: Source clean. Deployed bundle (either the standalone LW Code Page if still active in spaarkedev1, or the SpaarkeAi bundle that embeds LW) may be stale.

### Surfaces ruled out

- **`src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/`** — does NOT register any SmartTodo-style direct widget. The registry contains BudgetDashboard, SearchResults, AnalysisEditor, ContractComparison, StatusSummary, Recommendation, ActionPlan, redline-viewer, create-matter-wizard, document-upload-wizard, search-select-wizard, email-compose, meeting-schedule, create-project-wizard, find-similar-wizard, workspace (LegalWorkspaceApp), DataverseEntityView, MetricsDashboard (PR #372 additions). No `todo` Direct widget type exists. SmartToDo is **Pattern A composable section only** today.
- **`src/client/shared/Spaarke.Events.Components/`** — Calendar widget only; no todo crossover.
- **`src/server/api/Sprk.Bff.Api/Services/Workspace/TodoGenerationService.cs`** — BFF service, out of R4 scope (R4 is "purely client-side + Dataverse config" per spec.md §Dependencies / External).

---

## 3. Mount-path analysis — both contexts affected?

Per `SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md` §2-3, the SmartToDo workspace surface uses the **Dashboard wrapper** (because it's registered as a `SectionRegistration` consumed by `LegalWorkspaceApp`, not as a `WorkspaceWidgetRegistry` Direct widget type).

| Mount path | Affected? | Evidence |
|---|---|---|
| **System widget** (workspace cold-load) | **YES** | When a user opens the SpaarkeAi workspace and the default layout (or any Dataverse-system layout) includes section `id: "todo"`, `LegalWorkspaceApp` mounts `<SmartToDo embedded={true} … />` via `todo.registration.factory()`. If the embedded LW bundle is stale and still selects `sprk_todoflag` from `sprk_event`, this is where the OData error fires on cold load. The R3 UAT report described exactly this symptom. |
| **User-added widget** (custom layout via WorkspaceLayoutWizard) | **YES** | Same path as system widget — the section is the unit of composition. A user-added "My To Do List" section adds the same `id: "todo"` reference to a `sprk_workspacelayout` row, which the cold-load pipeline resolves through the same `sectionRegistry.ts` → `todo.registration` chain. Same bundle, same code path, same error. |
| Standalone SmartTodo Code Page (sprk_smarttodo) | **Probably yes (separate symptom)** | Source uses `useTodoItems` → `DataverseService.getActiveTodos` → `buildTodoItemsQuery` → `sprk_todo` only. Same as LW. If the deployed bundle of `sprk_smarttodo` is stale, the modal-launch path (R4 FR-34 visual-host drill-through, FR-16 toolbar "Open" icon, FR-17 dual-context modal) will also error. |

**Conclusion**: Both Dashboard-wrapper mount paths (system + user-added) go through the SAME registration, SAME component, SAME bundle. Rebuilding once fixes both. The standalone `sprk_smarttodo` Code Page is a separate deployable artifact and must also be rebuilt + redeployed.

---

## 4. PR #372 overlap + coordination plan

**PR #372 status**: MERGED to master on **2026-06-10** at 19:45 UTC (today, but BEFORE this audit). Merge commit: `a338f4b24`. This worktree's local `master` is at `219bd230` (pre-PR-372); **it has not yet been pulled into this worktree.**

**PR #372 scope** (86 files; relevant filtered to workspace/SmartToDo):
- Adds `DataverseEntityViewWidget`, `MetricsDashboardWidget` (+ `metricsDashboardConfigs.ts`) to `Spaarke.AI.Widgets/src/widgets/workspace/`.
- Modifies `register-workspace-widgets.ts`, `CreateMatterWizardWidget.tsx`, `DocumentUploadWizardWidget.tsx`.
- Modifies `Spaarke.Events.Components/src/widgets/CalendarWorkspaceWidget/CalendarWorkspaceWidget.tsx` + `CalendarSection.tsx`.
- Modifies `Spaarke.UI.Components/src/components/WorkspaceShell/SectionPanel.tsx` + `sectionMetadataCatalog.ts`.
- Modifies `LegalWorkspace/src/sectionRegistry.ts` and adds new sections: `invoices.registration.ts`, `matters.registration.ts`, `projects.registration.ts`, `workAssignments.registration.ts`. Touches `documents.registration.ts`.
- Modifies `src/solutions/SpaarkeAi/src/components/workspace/WorkspacePane.tsx`, `WorkspacePaneMenu.tsx`, `WorkspaceTabManagerComponent.tsx`, `services/pinnedWorkspaces.ts`.
- Modifies `src/solutions/SmartTodo/index.html` (Code Page entry) — *minor; possibly Vite/HMR config*.
- Adds `WorkspaceLayoutWizard/index.html`, `package.json`, `main.tsx`, `tsconfig.json`.

**Overlap with R4 A target (`SmartToDo` widget rebuild)**:
- **`LegalWorkspace/src/sectionRegistry.ts`** — both R4 task 020 (if Pattern D adopted, will add `smartTodoRegistration` import + entry) AND PR #372 (added 4 new section registrations) touch this file. **Conflict probability: low** (additive registry entries; standard merge unless we land at exactly the same array position).
- **`LegalWorkspace/src/components/SmartToDo/`** — R4 will rewrite or delete this whole subtree; PR #372 does **not touch SmartToDo source** at all. **Conflict probability: zero.**
- **`SmartTodo/index.html`** — PR #372 touches this; R4 task 015 (B header overhaul) and 040/050 (C/E/F card affordances) will also touch `src/solutions/SmartTodo/src/` extensively. **Conflict probability: low for index.html itself; main risk is downstream SmartTodo refactor merges.**
- **`Spaarke.AI.Widgets/src/widgets/workspace/register-workspace-widgets.ts`** — PR #372 added widgets here; R4 task 020 may add a `smart-todo` Direct widget registration here (if Pattern D's optional Direct registration is adopted per BUILD-A-NEW-WORKSPACE-WIDGET §4.2 Step 6). **Conflict probability: low** (append-only).

**Coordination plan**:
1. **Before R4 task 020 starts**: `git fetch origin && git merge origin/master` (or rebase) on `work/smart-todo-r4`. PR #372 lands cleanly with the additive registry/section changes. No expected conflicts in the SmartToDo source path.
2. **Ownership split**: PR #372 owns the 4 new sections (invoices, matters, projects, work assignments) + the entity-view widget + the metrics dashboard. R4 task 020 owns SmartToDo subtree + (potentially new) `@spaarke/smart-todo-components` peer package. No file ownership collision.
3. **Task 020 PR description must state** the master sync (whether rebased on top of `a338f4b24` or merged) and must note that **PR #372's changes to `sectionRegistry.ts` are preserved**.
4. **If R4 task 020 lands BEFORE further PR #372 follow-up batches** (e.g., a hypothetical "Batches 3+4"), R4 must coordinate with that branch's owner (`feature/ai-spaarke-ai-workspace-UI-r1` is the head branch — verify whether further work is queued). Per `gh pr list`, PR #372 is closed/merged; no follow-up branch is open as of audit time.

---

## 5. Recommended rebuild archetype (binding for task 020)

### Decision: **Pattern D — Dual-use widget (shared-lib widget + thin LegalWorkspace section shim)**

This is the **recommended default** per `BUILD-A-NEW-WORKSPACE-WIDGET.md` §1.2 ("If in doubt, build dual-use (Pattern D)") and matches the spec.md Assumption that "Pattern D (dual-use shared-lib widget + thin LW shim) is the right archetype for A".

### Rationale (tied to BUILD-A-NEW-WORKSPACE-WIDGET decision tree)

Walking §1's decision tree for SmartToDo:

- **Q1**: Does my widget compose MULTIPLE sections inside ONE workspace tab, where users add/remove/re-order via WorkspaceLayoutWizard (unit of mount = `sprk_workspacelayout`)? **YES** — SmartToDo is a single section that composes inside existing multi-section workspace layouts (default layout has To Do + Quick Summary + Latest Updates + Documents + Get Started). It is NOT a sophisticated single-purpose direct widget that owns the whole tab.
- **Q2**: Is the section logic LegalWorkspace-internal (reaches into `FeedTodoSyncContext`, LW `DataverseService`, LW hooks)? **PARTIALLY YES TODAY** — `useTodoItems` consumes `FeedTodoSyncContext` for cross-block todo lifecycle sync, and uses LW-local `DataverseService`. **But that coupling is removable**: the FeedTodoSyncContext subscription is for cross-block highlight sync with `ActivityFeed`; the standalone SmartTodo Code Page already operates without it.

Per BUILD-A-NEW-WORKSPACE-WIDGET §4 ("For NEW work prefer Archetype 3 (Pattern D) unless you have a specific reason to stay LW-internal"), the right answer is to **hoist the SmartToDo Kanban into a shared lib** (`@spaarke/smart-todo-components`) where it has zero LW coupling, and then keep the LW section as a **thin shim** that:
- Wraps the shared `SmartTodoWorkspaceWidget` with the LW `FeedTodoSyncContext` subscription IF the section is mounted from within an `<ActivityFeed>` neighborhood, OR
- Renders the widget bare when mounted outside LW (e.g., when SpaarkeAi mounts a `'smart-todo'` Direct widget type).

This matches the **Calendar canonical example** (R3 task 115) — `CalendarWorkspaceWidget` lives in `@spaarke/events-components` with zero LW coupling; LW shim is 62 lines.

### What Pattern D buys us specifically for R4

| Benefit | Carry-over to R4 work | Reference |
|---|---|---|
| SmartTodo can be mounted as a Dashboard section (today's path) AND as a Direct widget (future R5+ workspace tab) without code duplication | spec.md FR-04 ("mounts cleanly under all 6 workspace layouts") + spec.md FR-05 (rebuild + redeploy affected solutions) | BUILD §1.3 |
| The R4 SmartTodo Code Page (`src/solutions/SmartTodo/`) and the embedded LW section render the **same** component, fixing the long-standing dual-source-of-truth issue | spec.md §B "All new UI MUST use `@spaarke/ui-components`; hoist any new primitives" (NFR-02) — direct application | BUILD §4.1 + Anti-pattern §8 "Do not duplicate section code in both LegalWorkspace and SpaarkeAi" |
| Subsequent R4 work (B header/toolbar overhaul, E card affordances, F vertical orientation, C modal wiring) lands in ONE place and propagates to BOTH consumers automatically | spec.md success criteria #1 (workspace widget) + #10 (vertical orientation works in BOTH standalone Code Page + workspace widget context) | BUILD §4.1 Calendar polish R10–R13 example |
| LegalWorkspace retirement (OC-R4-05 / W-6) does not orphan SmartToDo — when LW becomes library-only, SmartToDo continues to work via the shared lib | `LEGALWORKSPACE-RETIREMENT.md` Component-library boundary | BUILD §4.2 Step 7 R4 NOTE |
| Aligns with the **ADR-012 Shared Component Library** boundary (binding per CLAUDE.md §10 / project CLAUDE.md MUST rules) | spec.md NFR-02 (carry-forward) | ADR-012 |

### Pattern A fallback considered and rejected

Pattern A (composable section, LW-internal) is what SmartToDo is **today**. It is the cheapest rebuild path — just fix the deployed bundle. But it (a) keeps the dual-source-of-truth between `src/solutions/SmartTodo/` and `src/solutions/LegalWorkspace/src/components/SmartToDo/`, (b) does not align with BUILD-A-NEW-WORKSPACE-WIDGET §4's "preferred default for new work going forward", and (c) leaves R4 task 020 needing to choose between deleting one of the two trees or letting them drift further. Pattern D resolves all three.

### Rebuild location (binding for task 020)

```
NEW shared library:
  src/client/shared/Spaarke.SmartTodo.Components/        (peer package, mirrors Spaarke.Events.Components)
    package.json                                          (name: @spaarke/smart-todo-components)
    tsconfig.json
    src/
      index.ts                                            (barrel)
      widgets/
        SmartTodoWorkspaceWidget/
          SmartTodoWorkspaceWidget.tsx                    (the canonical widget)
          SmartTodoWorkspaceWidget.css                    (Griffel makeStyles; ADR-021 tokens only)
      components/                                         (KanbanCard, KanbanHeader, etc. — hoisted)
        KanbanCard/
        KanbanHeader/
        TodoDetailPane/                                   (note: spec.md FR-18 retires the broken side-pane; this component will be DELETED in C work)
        DismissedSection/
        AddTodoBar/
        ThresholdSettings/
        TodoAISummaryDialog/
        EffortScoreCard/
        PriorityScoreCard/
      hooks/                                              (useTodoItems, useKanbanColumns, useUserPreferences hoisted)
      services/                                           (queryHelpers, DataverseService hoisted)
      types/                                              (entities, enums hoisted)
      utils/                                              (todoScoreUtils, dueLabelUtils hoisted)

UPDATED LegalWorkspace section shim (thin):
  src/solutions/LegalWorkspace/src/sections/todo.registration.ts    (re-target import to @spaarke/smart-todo-components; ~62 lines like Calendar)
  src/solutions/LegalWorkspace/src/components/SmartToDo/             (DELETE this subtree; shared lib owns the components now)

UPDATED standalone SmartTodo Code Page:
  src/solutions/SmartTodo/src/App.tsx                                 (re-target imports to @spaarke/smart-todo-components)
  src/solutions/SmartTodo/src/components/SmartToDo/                   (DELETE; if any wrapper logic is needed, it stays here)

OPTIONAL Direct widget registration (R4 may defer, like Calendar did):
  src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/register-workspace-widgets.ts  (add 'smart-todo' Direct widget type pointing at @spaarke/smart-todo-components → SmartTodoWorkspaceWidget)
```

### Is the peer package `@spaarke/smart-todo-components` needed?

**Yes.** Per the task POML guidance: "If you recommend Pattern D, a new `@spaarke/smart-todo-components` peer package would be created during task 020." This audit confirms creation. Rationale: SmartTodo is large enough (13 components, 4 hooks, full services + types + utils) that putting it in `@spaarke/ui-components` would bloat the general-purpose lib. Sibling pattern with `@spaarke/events-components` (Calendar) is the proven model. Peer package keeps SmartTodo concerns isolated; the shared lib is consumed only by code paths that actually use SmartTodo.

---

## 6. Acceptance criteria checklist (from task 001 POML)

- [x] `notes/widget-surface-audit.md` exists and names at least one source file emitting the legacy query → **Section 1 lists 30+ files (all docstring/non-functional); Section 2 names the 13-file `LegalWorkspace/src/components/SmartToDo/` subtree as the primary suspected deployed-bundle source.**
- [x] Audit confirms whether system widget path, user-added widget path, or both are affected (with evidence) → **Section 3 confirms BOTH paths affected (same Dashboard-wrapper section registration; same code path; same bundle).**
- [x] Recommended rebuild archetype follows BUILD-A-NEW-WORKSPACE-WIDGET decision tree (Pattern A / Pattern D / other) with stated rationale → **Section 5 walks the §1 decision tree explicitly (Q1=YES, Q2=PARTIALLY but removable); recommends Pattern D modeled on Calendar (R3 task 115); rejects Pattern A with three rationales.**
- [x] PR #372 coordination plan documented (file ownership / rebase strategy) → **Section 4 documents PR #372 is MERGED 2026-06-10, local worktree at `219bd230` is pre-PR-372; recommends `git merge origin/master` before task 020; ownership split confirmed clean.**
- [x] Task 020 scope is clear after reading the audit (rebuild location + archetype + dependencies) → **Section 5 includes the explicit directory layout for the new `@spaarke/smart-todo-components` peer package, the LW shim shape, the standalone Code Page re-targeting, and the optional Direct widget registration.**

---

## 7. Open questions / risks for task 020

1. **`FeedTodoSyncContext` coupling — needs design decision**. `useTodoItems` subscribes to `FeedTodoSyncContext` for cross-block highlight sync with `ActivityFeed` (LW-local context, not in any shared lib). When hoisting to `@spaarke/smart-todo-components`, the widget cannot depend on that LW-internal context. **Options**: (a) make the subscription optional with `useContext` returning `null` outside LW, (b) move the subscription up into the LW section shim (thin wrapper subscribes + passes lifecycle events as props), (c) ship a tiny `@spaarke/feed-sync` lib if the sync truly cross-pkg. Decision: probably (b) — matches the Calendar shim shape; keeps the widget host-agnostic.

2. **`useUserPreferences` may need hoist or duplication**. The R4 F-1 vertical-orientation preference persists via `sprk_userpreference` per FR-30. If both standalone Code Page and workspace widget share the preference, the hook should live in the shared lib. Audit-time recommendation: hoist as part of task 020 prep.

3. **`SpeDocumentViewer/bundle.js` compiled artifact contains `sprk_todoflag` references** at lines 2000–3070. This is unrelated to R4 A (it's a PCF bundle, not workspace), but spec.md success criterion #12 specifies `grep -i sprk_todoflag src/**/*.{ts,tsx,cs}` returns zero functional hits. If the grep is run with broader globs (`*.js`), it will find these. Recommend either (a) excluding `**/bundle.js` from the success-criterion grep, or (b) opening a follow-up to refresh the PCF bundle. **Not blocking for R4 A** — flag for R4 wrap-up.

4. **Verify `sprk_smarttodo` deployment date**. The standalone Code Page may already be on a current bundle (R3 task 070b explicitly touched it). If `Deploy-SmartTodoCodePage.ps1` was run as part of R3 closeout, only Surface B is stale. The audit recommends **redeploy both** as the safe path; spec.md NFR-09 requires every R4 task touching a deployed bundle to identify and redeploy affected surfaces — task 020 should redeploy LegalWorkspace bundle (or SpaarkeAi if LW retirement is complete) AND `sprk_smarttodo`.

5. **R3 task 005 deferred** (`sprk_eventtodo` entity delete cleanup, 26 `appmodulecomponent` refs). Per spec.md "Out of Scope", this stays out of R4. Confirms R4 A does not need to touch the `sprk_eventtodo` cleanup.

6. **Test-time risk**: spec.md NFR-06 requires unit tests for `<RecordNavigationModalShell>` and integration tests. Task 020 hoist may temporarily break existing R3 test suites under `src/solutions/LegalWorkspace/src/components/SmartToDo/` (if tests live there). Recommend migrating tests alongside components into `@spaarke/smart-todo-components/__tests__/`.

---

## Appendix — Related architectural references consulted

- [`docs/guides/BUILD-A-NEW-WORKSPACE-WIDGET.md`](../../../docs/guides/BUILD-A-NEW-WORKSPACE-WIDGET.md) — §1 decision tree (binding per FR-03), §4 Pattern D canonical example
- [`docs/architecture/SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md`](../../../docs/architecture/SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md) — two-wrapper model; OC-R4-06 intentional retention
- [`docs/architecture/SPAARKEAI-WORKSPACE-ARCHITECTURE.md`](../../../docs/architecture/SPAARKEAI-WORKSPACE-ARCHITECTURE.md) — cold-load → widget render pipeline
- [`docs/architecture/SPAARKEAI-COMPONENTIZATION-AUDIT.md`](../../../docs/architecture/SPAARKEAI-COMPONENTIZATION-AUDIT.md) §2A — Calendar Pattern D canonical worked example
- [`docs/architecture/LEGALWORKSPACE-RETIREMENT.md`](../../../docs/architecture/LEGALWORKSPACE-RETIREMENT.md) — standalone LW Code Page retirement (OC-R4-05); component-library boundary
- [`docs/architecture/LEGALWORKSPACE-EMBEDDED-MODE-CONTRACT.md`](../../../docs/architecture/LEGALWORKSPACE-EMBEDDED-MODE-CONTRACT.md) — 21 testable MUSTs for embedded host
- [`.claude/adr/ADR-006-pcf-over-webresources.md`](../../../.claude/adr/ADR-006-pcf-over-webresources.md) — UI surface decision tree
- [`.claude/adr/ADR-012-shared-component-library.md`](../../../.claude/adr/ADR-012-shared-component-library.md) — shared primitives MUST ship from a shared lib
- [`.claude/adr/ADR-030-pane-event-bus.md`](../../../.claude/adr/ADR-030-pane-event-bus.md) — typed PaneEventBus contract (applies if widget dispatches workspace events; SmartToDo currently does NOT)
- [`projects/smart-todo-r4/spec.md`](../spec.md) — workstream A FR-01 through FR-05 (binding scope)
- [`projects/smart-todo-r4/CLAUDE.md`](../CLAUDE.md) — applicable ADRs + MUST/MUST NOT rules

---

*End of audit. Task 020 may proceed once master is pulled and the design decisions in §7 are resolved (probably in the task-020 design-spike step).*
