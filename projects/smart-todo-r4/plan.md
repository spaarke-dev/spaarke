# Project Plan: Smart To Do — UX Enhancement (R4)

> **Last Updated**: 2026-06-10
> **Status**: Ready for Tasks
> **Spec**: [spec.md](spec.md)
> **Design**: [design.md](design.md)

---

## 1. Executive Summary

**Purpose**: Close the UX gaps surfaced during R3 UAT for the new first-class `sprk_todo` entity. Rebuild the stale workspace widget, replace the broken side-pane with a hybrid modal pattern, modernize the SmartTodo Code Page toolbar, introduce a reusable polymorphic regarding resolver, and surface "Upcoming To Dos" cards on four parent forms.

**Scope**:
- A workspace widget rebuild (`SmartToDo`, query `sprk_todo` not `sprk_event`)
- B SmartTodo Code Page header/toolbar overhaul aligned with `SemanticSearchControl` pattern
- C hybrid modal — new `<RecordNavigationModalShell>` shared component + iframe-embedded OOB To Do main form
- D reusable regarding resolver (winner picked by audit — PCF / Web Resource / Code Page embed)
- E card affordances (Open icon, double-click, selection checkbox)
- F vertical Kanban orientation toggle (persisted via `sprk_userpreference`)
- G 4 new `sprk_chartdefinition` records + Visual Host on Matter / Project / Invoice / WorkAssignment forms

**Timeline**: ~3-4 weeks elapsed | **Estimated Effort**: 36-45 tasks (most parallelizable in waves after foundation phase)

---

## 2. Architecture Context

### Design Constraints

**From ADRs** (must comply):

- **ADR-024** — Polymorphic Resolver Pattern. R4 D MUST wrap `PolymorphicResolverService.applyResolverFields` (FR-21); no re-implementation of mutual-exclusivity logic.
- **ADR-021** — Fluent UI v9 Design System. R4 B/C/E/F MUST use Fluent v9 + Griffel `makeStyles` + semantic tokens; no v8 components; no inline styles (FR-11, NFR-01).
- **ADR-022** — PCF Platform Libraries. If D audit chooses PCF, binding applies: React 16 boundary, platform-provided Fluent.
- **ADR-006** — PCF over Web Resources / UI Surface Architecture. Decision tree authoritative for D audit; informs all R4 client-surface placement.
- **ADR-012** — Shared Component Library. R4 B/C/F MUST hoist new primitives to `@spaarke/ui-components` (NFR-02); no solution-specific inline definitions.
- **ADR-026** — Code Page Build Standard. SmartTodo Code Page + any new Code Page wrappers use Vite + `vite-plugin-singlefile` + React 19.
- **ADR-028** — Spaarke Auth Architecture (v2). Verify at implementation; spec says no new BFF surface but flag if a BFF call sneaks in (must use `useAuth()` / `authenticatedFetch`, not token snapshots).
- **ADR-030** — PaneEventBus Pattern. Applies to R4 A widget if it dispatches `widget_load`-style events.
- **ADR-032** — BFF Null-Object Kill-Switch Pattern. Verify-only; should be a NO-OP for R4 (purely client-side).

**From Spec**:

- ✅ MUST use `@spaarke/ui-components` for all shared UI primitives
- ✅ MUST follow `/fluent-v9-component` skill for all styling
- ✅ MUST use **HYBRID modal pattern** (`<RecordNavigationModalShell>` + iframe-embedded OOB MDA form) — no pure-React form re-implementation
- ✅ MUST be multi-environment portable — no hardcoded environment URLs, app IDs, container IDs, or chart definition IDs in source
- ✅ MUST rebuild + redeploy affected solutions after source changes (NFR-09)
- ✅ MUST comply with `BUILD-A-NEW-WORKSPACE-WIDGET.md` decision tree for A (FR-03)
- ❌ MUST NOT query `sprk_event` for `sprk_todoflag` (field removed in R3)
- ❌ MUST NOT reimplement save / BPF / business rules / statuscode in a custom React form
- ❌ MUST NOT introduce v8 Fluent components or inline styles
- ❌ MUST NOT retain `TodoDetailPanel` side-pane
- ❌ MUST NOT drill-through Visual Host to entity list view (must open SmartTodo Code Page modal)

### Key Technical Decisions

| Decision | Rationale | Impact |
|----------|-----------|--------|
| Hybrid modal (Code Page wrapper + iframe OOB form) over pure-React form | Save / BPF / business rules / statuscode kept native; lowest maintenance | Unblocks C; sets pattern for all future list-launched modals |
| Wrap `PolymorphicResolverService.applyResolverFields` | One source of truth for FR-13 mutual-exclusivity | D reusable across `sprk_todo` + `sprk_communication` with zero entity-specific branching |
| Refactor `RichFilePreviewDialog` to consume new `<RecordNavigationModalShell>` BEFORE wiring SmartTodo modal | Regression-safety check on the abstraction; if file preview breaks, the shell isn't right yet | Adds 1 task but prevents discovering shell bugs in SmartTodo context |
| Filter modes reduced to "Assigned to Me" only | UAT confusion; BU-owned `ownerid` field | Drops "My Tasks" toggle from B FR-07 |
| D audit deferred to implementation-time task | Genuine trade-offs PCF vs Web Resource vs Code Page on multi-env stability | Implementation tasks for D depend on audit outcome; spec accepts decision as binding |
| Vertical Kanban orientation = CSS transform-only re-layout | NFR-08 <300ms switch; no re-mount | F implementation should avoid React tree re-creation |
| Drill-through opens Code Page modal (NOT entity list view) | Preserves curated Kanban UX | G FR-34 requires existing or new `useLaunchContext`-style URL-param parser |

### Discovered Resources

> **Source**: comprehensive discovery via Explore agent 2026-06-10 (after master sync `a2ac6a849`)

**Applicable Skills** (auto-discovered, in invocation order):

- `.claude/skills/fluent-v9-component/` — Authors Fluent v9 React components across Spaarke surfaces. **Mandatory for B/E/F**: all styling work routes through this skill (Griffel + semantic tokens; no v8).
- `.claude/skills/pcf-deploy/` — Build / pack / deploy PCF controls. Used by **D** if audit chooses PCF; potentially by **G** for VisualHost tweaks.
- `.claude/skills/code-page-deploy/` — Build + deploy React Code Page web resources to Dataverse. **Definite for B/C/F** (SmartTodo Code Page redeploy).
- `.claude/skills/dataverse-deploy/` — Deploy solutions + PCF + Web Resources via PAC CLI. Used for **G** (chart definition records + updated parent forms) and **A** (widget redeploy).
- `.claude/skills/dataverse-create-schema/` — Create / update Dataverse schema (entities, attributes, option sets) via Web API. Verify at **D** time if any new attributes; verify **F** at `sprk_userpreference` integration.
- `.claude/skills/widget-design/` — Design an MCP App widget. Used by **A** if widget surface needs design rework beyond the data-source switch.
- `.claude/skills/adr-check/` — Validate code changes against ADRs. Quality gate at task completion (every PR-bearing task).
- `.claude/skills/code-review/` — Comprehensive code review. Quality gate at task completion.
- `.claude/skills/ui-test/` — Browser-based UI testing for PCF + Code Pages. Verifies **NFR-05** (modal nav <500ms), **NFR-07** (a11y), **NFR-08** (orientation switch <300ms).
- `.claude/skills/task-execute/` — Load-bearing task execution protocol; every R4 task is invoked through this.

**Knowledge Articles**:

- `docs/guides/BUILD-A-NEW-WORKSPACE-WIDGET.md` — Decision tree for A archetype selection. **Current as of R4 W-2 rewrite (2026-05-26)**; binding per FR-03.
- `docs/architecture/SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md` — Authoritative two-wrapper model (Dashboard + Direct). Required reading for A.
- `docs/architecture/SPAARKEAI-WORKSPACE-ARCHITECTURE.md` — Cold-load → widget render pipeline. Prerequisite for A widget placement decisions.
- `docs/architecture/SPAARKEAI-COMPONENT-MODEL.md` — Inventory of `@spaarke/*` packages (where to hoist).
- `docs/architecture/SPAARKEAI-COMPONENTIZATION-AUDIT.md` — §2A canonical Pattern D (Calendar widget worked example). A reference if dual-use chosen.
- `docs/architecture/spaarke-todo-architecture.md` — Smart To Do architecture (merged from R3 wrap-up PR #374, 2026-06-10). Required context for all R4 work.
- `docs/architecture/LEGALWORKSPACE-RETIREMENT.md` — Standalone LegalWorkspace Code Page retired (OC-R4-05); component-library boundary clarification for A.
- `docs/architecture/event-to-do-architecture.md` — R3-updated entity model; reference for B/C/D.
- `docs/standards/CODING-STANDARDS.md` — Fluent v9 rules (§29-36), PCF React 16 boundaries (§38-42), Code Page React 19 (§44+).
- `.claude/patterns/ui/fluent-v9-component-authoring.md` — Mandatory pattern for all styling (B, E, F).
- `.claude/patterns/ui/fluent-v9-theming.md`, `ui/fluent-v9-react-version-boundaries.md` — React 19 Code Page boundaries for B + C.
- `.claude/patterns/dataverse/polymorphic-resolver.md` — Full pattern guide for D resolver logic; dual-field + service wrappers.
- `.claude/patterns/pcf/fluent-v9-modern-theming.md` — If D chooses PCF.
- `.claude/patterns/webresource/code-page-wizard-wrapper.md` — Pattern for C `<RecordNavigationModalShell>` iframe wrapper.

**Reusable Code** (canonical references; verified present on disk):

- `src/client/pcf/SemanticSearchControl/SemanticSearchControl/SemanticSearchControl.tsx` — Layout reference for B (4-row hierarchy + selection toolbar).
- `src/client/shared/Spaarke.UI.Components/src/components/FilePreview/RichFilePreview.tsx` — **C extraction source**: `currentIndex`, `navigationTotal`, `onNavigate`, chevron logic.
- `src/client/shared/Spaarke.UI.Components/src/components/FilePreview/RichFilePreviewDialog.tsx` — **C refactor target**: consume new shell post-extraction.
- `src/client/shared/Spaarke.UI.Components/src/components/AssociateToStep/AssociateToStep.tsx` — Existing 11-entity picker UX; D regarding picker can reuse or adapt.
- `src/client/shared/Spaarke.UI.Components/src/services/PolymorphicResolverService.ts` — **D MUST wrap, MUST NOT reimplement** (FR-21).
- `src/client/pcf/VisualHost/control/` — Chart rendering host for G.
- `src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/` — Calendar widget Pattern D canonical (if A chooses dual-use).
- `src/solutions/SmartTodo/src/App.tsx` — R4 B/C/F host surface.
- `src/solutions/SmartTodo/src/components/TodoDetailPanel.tsx` — **R4 FR-18 retires this** (side-pane bugs).
- `src/solutions/LegalWorkspace/src/components/SmartToDo/` — **A audit target** (likely culprit for `sprk_todoflag` legacy query).

**Scripts Available** (`scripts/`):

- `Deploy-ChartDefinitionEntity.ps1` — Create/update chart def records (G).
- `Check-ChartDefinitionEntity.ps1` — Verify chart def Web API contract pre-deploy (G).
- `Add-ChartDefinitionAttributes.ps1` — Idempotent attribute setup (if G needs new `sprk_chartdefinition` fields).
- `Build-ViteSolutionsDirect.ps1` — Build SmartTodo Code Page + any new Vite-bundled web resources (B, C).
- `Build-AllClientComponents.ps1` — Full rebuild including PCF (D if PCF, G VisualHost).
- `Deploy-PCFWebResources.ps1` — PCF deployment (D if PCF).
- `Deploy-CustomPage.ps1` — Code Page deployment (B, C, F).

**Discovery Gaps** (the discovery agent flagged; tasks must resolve):

1. **`useLaunchContext` hook** — spec cites `src/solutions/SmartTodo/src/hooks/useLaunchContext.ts` but file does not exist. Existing hooks: `useFeedTodoSync`, `useKanbanColumns`, `useTodoItems`, `useTodoScoring`, `useUserPreferences`. **G task must clarify**: implement new, or repurpose an existing hook?
2. **`@spaarke/events-components` source location** — present only in `node_modules`; consumed as published package, not local lib. If A chooses Pattern D, A creates new `@spaarke/smart-todo-components` peer package.
3. **D PCF placement** — `src/client/pcf/RegardingResolver/` doesn't exist (expected, since audit pending). D first task = audit + decision; D second task = implement.

---

## 3. Implementation Approach

### Phase Structure

```
Phase 0: Foundation — Audit + Spike (Week 1, days 1-2)
└─ A audit: identify failing widget surface(s)
└─ D audit: PCF vs Web Resource vs Code Page embed
└─ G spike: confirm sprk_smarttodo accepts query-string + modal-style render

Phase 1: Shared-lib hoist (Week 1, days 3-5)
└─ <RecordNavigationModalShell> extraction from RichFilePreview.tsx
└─ RichFilePreviewDialog refactor (regression-safety on the shell)
└─ B toolbar primitives hoisted to @spaarke/ui-components

Phase 2: Implementation waves (Week 2-3; mostly parallelizable)
├─ Wave 2a (parallel): A widget rebuild, B Code Page overhaul, D resolver implementation, G chart def creation
├─ Wave 2b (parallel): C SmartTodo modal wiring, E card affordances, F vertical orientation toggle
└─ Wave 2c (serial after 2a/2b): G form additions on 4 parent forms (depends on D resolver on To Do form)

Phase 3: Deployment + Testing (Week 3, days 4-5)
└─ Build + deploy all affected solutions
└─ UI test suite (NFR-05, NFR-07, NFR-08)
└─ Regression sweep on file preview, To Do save, statuscode flows

Phase 4: Wrap-up (Week 4, day 1)
└─ lessons-learned + retro
└─ README status → Complete
```

### Critical Path

**Blocking Dependencies**:

- **Phase 1 BLOCKS Phase 2** for C (need `<RecordNavigationModalShell>` before SmartTodo modal wiring) and for B (need toolbar primitives before Code Page overhaul).
- **D audit (Phase 0) BLOCKS D implementation (Phase 2)** — winner determines PCF vs Web Resource vs Code Page tasks.
- **G spike (Phase 0) BLOCKS G form additions (Phase 2c)** — drill-through URL signature uncertainty.
- **D resolver on To Do form BLOCKS G** — drill-through pre-filters by regarding fields; resolver must exist before parent-form Visual Hosts can drill into pre-filtered modal.
- **A audit (Phase 0) BLOCKS A widget rebuild (Phase 2a)** — must know which deployed surface is stale before rebuilding.

**High-Risk Items**:

- **C cross-frame messaging** — postMessage between Code Page parent and OOB MDA iframe under MDA security headers. Mitigation: spike at start of C; documented dirty-check protocol; fallback to query-string signal.
- **D audit outcome** — if Web Resource wins, ADR-006 PCF-preference is overridden. Mitigation: document rationale in `notes/regarding-resolver-audit.md`; reviewer judgment.
- **G drill-through URL signature** — `Xrm.Navigation.navigateTo({pageType: "webresource"...})` may not accept query params or modal-style render. Mitigation: 30-min spike at start of G; fallback to direct `window.open` with modal CSS.
- **Parallel branch overlap** — PR #372 (workspace UI polish) overlaps with R4 A; `work/spaarke-datagrid-framework-r1` (55 unmerged) overlaps with R4 C shell hoist. Mitigation: coordinate file ownership; sequence shell-hoist task last in Phase 1 if datagrid merges first.

---

## 4. Phase Breakdown

### Phase 0: Foundation — Audit + Spike ✅ COMPLETE (2026-06-10)

**Objectives** (all met):
1. ✅ Identify which deployed surface(s) emit `sprk_todoflag` legacy query (A).
2. ✅ Pick the regarding resolver architecture: PCF vs Web Resource vs embedded Code Page (D).
3. ✅ Confirm drill-through URL signature for SmartTodo Code Page modal render (G).
4. ✅ Confirm `useLaunchContext` resolution: implement new or repurpose existing.

**Deliverables** (all written 2026-06-10):
- [x] [`notes/widget-surface-audit.md`](notes/widget-surface-audit.md) (256 lines) — source is CLEAN; runtime error is stale-bundle issue. Both system-widget + user-added paths use SAME bundle. Archetype: **Pattern D dual-use** with new `@spaarke/smart-todo-components` peer package modeled on Calendar (R3 task 115).
- [x] [`notes/regarding-resolver-audit.md`](notes/regarding-resolver-audit.md) (162 lines) — **virtual PCF** chosen (40/40 vs 22/40 Web Resource vs 26/40 Code Page); `src/client/pcf/RegardingResolver/` mirroring deployed `AssociationResolver` v1.1.0.
- [x] [`notes/drill-through-spike.md`](notes/drill-through-spike.md) (311 lines) — payload confirmed by MS Learn + 20+ in-repo callers. Modal-style render risk: very-low. Surfaced **contract gap**: VisualHost wraps params in `?data=<envelope>` but existing `useLaunchContext` reads raw keys only.
- [x] [`notes/launch-context-decision.md`](notes/launch-context-decision.md) (205 lines) — **CORRECTION**: hook EXISTS (R3 task 070b shipped it; 235 LOC + 217 LOC tests). Initial project discovery missed it. Decision: **REPURPOSE + EXTEND** with `openTodos` action discriminator.

**Binding decisions for downstream tasks**:

| For task | Decision | Source |
|---|---|---|
| 020 (A widget) | Pattern D dual-use; new `@spaarke/smart-todo-components` peer package; rebuild `LegalWorkspace/SmartToDo` shim AND standalone Code Page (both share bundle, but standalone freshness uncertain — redeploy both for safety per NFR-09) | R4-001 |
| 020 — design risk | `FeedTodoSyncContext` coupling needs design decision — recommended: lift subscription into LW section shim (Calendar pattern) so shared-lib widget stays host-agnostic | R4-001 |
| 050 (D resolver) | Virtual PCF at `src/client/pcf/RegardingResolver/`; bound to hidden `sprk_regardingrecordtype` lookup; 4-side-effect-field writes via `Xrm.WebApi`; manifest `entity` input prop enables reuse for `sprk_communication` | R4-002 |
| 051 (D form add) | Verify hidden `sprk_regardingrecordtype` field present on To Do main form BEFORE binding | R4-002 |
| 080 (G chart defs) | `sprk_drillthroughtarget = "sprk_smarttodo.html"`; `sprk_contextfieldname = "sprk_regarding<X>"` per entity; `sprk_visualtype = 100000009` Due Date Card List | R4-003 |
| 081-084 (G forms) | Drill-through payload: `Xrm.Navigation.navigateTo({pageType:"webresource", webresourceName:"sprk_smarttodo.html", data:"entityName=sprk_todo&filterField=sprk_regarding<X>&filterValue=<guid>&mode=dialog"}, {target:2, position:1, width:90%, height:85%})`. Auto-emitted by VisualHost when chart def's drill target is set | R4-003 |
| 030 (B layout) + 081-084 (G drill) | Consume the EXISTING `useLaunchContext` hook — extend it via new task 034 before tasks 081-084 attempt drill-through | R4-004 |

**New task added**: [034](tasks/034-B-extend-useLaunchContext.poml) — extends `useLaunchContext` with `openTodos` discriminator AND switches parser to `parseDataParams()` shared utility for envelope handling. Closes the contract gap surfaced by R4-003 + R4-004. Blocks tasks 081-084.

**Stale-claim corrections applied**:
- R4 spec.md cited `useLaunchContext.ts` as missing — was wrong; hook exists.
- Tasks 020 + 060 had `004` in deps; corrected — they don't consume the hook directly. Only tasks 030 + 081-084 do (and 030 already does in R3 — no R4 change required for the consumer side; 034 extends the producer).

---

### Phase 1: Shared-lib hoist (Week 1, days 3-5)

**Objectives**:
1. Extract `<RecordNavigationModalShell>` from `RichFilePreview.tsx` into `@spaarke/ui-components`.
2. Refactor `RichFilePreviewDialog` to consume the shell (regression check).
3. Hoist B toolbar primitives (selection-aware toolbar, view toggle, orientation toggle).

**Deliverables**:
- [ ] `<RecordNavigationModalShell>` in `@spaarke/ui-components` with props: `currentIndex`, `navigationTotal`, `onNavigate`, `title`, `actionBar`, `children`, dirty-check protocol callbacks.
- [ ] `RichFilePreviewDialog` refactored to consume shell; existing file preview flows pass regression.
- [ ] `<SelectionAwareToolbar>` in `@spaarke/ui-components` with slot-based action API (Open / Delete / Email / Pin).
- [ ] Cross-frame messaging protocol documented in `@spaarke/ui-components` API docs (`request-dirty-check` / `dirty-check-result`).

**Critical Tasks**: `<RecordNavigationModalShell>` MUST come first (BLOCKS C wave); RichFilePreviewDialog refactor SECOND (regression-safety gate).

**Inputs**: `RichFilePreview.tsx` (extraction source), `SemanticSearchControl.tsx` (toolbar reference).

**Outputs**: New shared-lib components published to `@spaarke/ui-components`; published version bump; consumers updated.

---

### Phase 2: Implementation waves (Week 2-3)

#### Wave 2a — parallel (independent file scopes)

**Objectives**:
- A: Rebuild workspace widget to query `sprk_todo` (Pattern D if dual-use chosen).
- B: SmartTodo Code Page header/toolbar overhaul (4-row layout, "Assigned to Me" filter, view toggle).
- D: Implement audited regarding resolver (winner from Phase 0); add to To Do main form.
- G: Create 4 new `sprk_chartdefinition` records (Matter, Project, Invoice, WorkAssignment); deploy via PowerShell script.

**Deliverables**:
- [ ] Workspace widget mounts cleanly; 0 OData errors; sprk_todoflag eliminated.
- [ ] SmartTodo Code Page renders 4-row layout matching `SemanticSearchControl` pixel-comparable.
- [ ] Regarding resolver visible + functional on To Do main form; 11 entity targets selectable; FR-13 mutual-exclusivity enforced.
- [ ] 4 chart def records present in spaarkedev1 with correct `sprk_entitylogicalname`, `sprk_contextfieldname`, `sprk_drillthroughtarget`, `sprk_visualtype`.

**Parallelization Safety**:
- A touches `src/solutions/LegalWorkspace/src/components/SmartToDo/` or new `@spaarke/smart-todo-components/` — disjoint.
- B touches `src/solutions/SmartTodo/` Code Page UI — disjoint.
- D touches new `src/client/pcf/RegardingResolver/` OR `src/client/webresources/` OR `src/solutions/RegardingResolver/` per audit — disjoint.
- G touches Dataverse records via script (no source file conflict).

**Wave Build Verification** (per project-pipeline Step 5 mandatory rule):
- After wave: `npm run build` in each affected package; if `.cs` modified (not expected), `dotnet build src/server/api/Sprk.Bff.Api/`.

#### Wave 2b — parallel (after 2a checkpoint)

**Objectives**:
- C: Wire SmartTodo card-open path to `<RecordNavigationModalShell>` with To Do main form iframe.
- E: Card affordances (Open icon upper-right, double-click body, selection checkbox upper-left).
- F: Vertical Kanban orientation toggle + `sprk_userpreference` persistence.

**Deliverables**:
- [ ] SmartTodo card "Open" → modal with iframe-embedded To Do main form; `<` `>` nav + "N of M" + dirty-check all functional in BOTH MDA and Code Page contexts.
- [ ] `TodoDetailPanel` side-pane retired (file deleted, references removed).
- [ ] Card affordances all functional (Open icon, double-click, checkbox drives selection-aware toolbar from B).
- [ ] Vertical orientation toggle works; preference persists per-user via `sprk_userpreference.preferencetype = "SmartTodoOrientation"`.

**Parallelization Safety**:
- C touches `src/solutions/SmartTodo/src/components/` + retires `TodoDetailPanel.tsx`.
- E touches `src/solutions/SmartTodo/src/components/TodoCard.tsx` (or equivalent).
- F touches `src/solutions/SmartTodo/src/components/KanbanBoard.tsx` (or equivalent) + new hook.
- E + F + C all in same Code Page; coordinate via clear file ownership at task level. **If file ownership conflicts, serialize C → E → F.**

#### Wave 2c — serial (after 2a + 2b complete)

**Objectives**:
- G: Add Visual Host control to Matter / Project / Invoice / WorkAssignment main forms; verify drill-through opens SmartTodo Code Page modal pre-filtered.

**Deliverables**:
- [ ] Each parent form has new "Upcoming To Dos" section.
- [ ] Drill-through opens SmartTodo Code Page modal with `?regardingType=<entity>&regardingId=<id>` pre-filter.

**Serial Reason**: Drill-through depends on D resolver writes (must exist on To Do form) AND on C modal (Code Page modal-render path must work).

---

### Phase 3: Deployment + Testing (Week 3, days 4-5)

**Objectives**:
1. Rebuild + redeploy all affected solutions (SmartTodo, LegalWorkspace if touched, `@spaarke/ui-components` consumers, RegardingResolver per audit, VisualHost solution carrying 4 new chart defs + 4 parent form changes).
2. Run UI test suite: NFR-05 (modal nav <500ms), NFR-07 (a11y), NFR-08 (orientation switch <300ms).
3. Regression sweep: file preview unchanged, To Do save persists, "Completed" statuscode works.

**Deliverables**:
- [ ] All affected solutions deployed to spaarkedev1; smoke test green.
- [ ] UI test report attached to wrap-up PR.
- [ ] `grep -i sprk_todoflag src/**/*.{ts,tsx,cs}` returns zero functional hits.

**Inputs**: All Phase 2 task outputs.

**Outputs**: Deployed solutions + test artifacts.

---

### Phase 4: Wrap-up (Week 4, day 1)

**Objectives**:
1. Write `notes/lessons-learned.md` capturing what worked, what didn't, what to do differently next time.
2. Update README status → Complete.
3. Run `/repo-cleanup` to remove ephemeral artifacts.
4. Open R4 PR; merge to master via `/merge-to-master`.

**Deliverables**:
- [ ] `notes/lessons-learned.md` written.
- [ ] README "Status" header → Complete; "Completed Date" filled in.
- [ ] R4 PR opened, reviewed, merged.
- [ ] Branch `work/smart-todo-r4` merged + cleaned up via `/worktree-sync` Full Sync.

---

## 5. Dependencies

### External Dependencies

| Dependency | Status | Risk | Mitigation |
|------------|--------|------|------------|
| Dataverse To Do main form `eca59df4-1364-f111-ab0c-7ced8ddc4cc6` | GA | Low | Confirmed; no upstream changes expected |
| Chart def `154bd4a4-f359-f111-a825-3833c5d9bcab` (UPCOMING TASKS pattern reference) | GA | Low | Confirmed in spaarkedev1 |
| `sprk_userpreference` entity | GA | Low | R3 created; F builds on existing pattern |
| MDA iframe security headers (frame-ancestors, X-Frame-Options) | Externally controlled | Medium | C spike validates postMessage flow under current headers; fallback strategies documented |
| `Xrm.Navigation.navigateTo({pageType: "webresource"})` modal render | GA | Medium | G spike validates; fallback to direct window.open if blocked |

### Internal Dependencies

| Dependency | Location | Status |
|------------|----------|--------|
| `PolymorphicResolverService.applyResolverFields` (R3) | `src/client/shared/Spaarke.UI.Components/src/services/PolymorphicResolverService.ts` | Production |
| `RichFilePreview.tsx` navigation logic | `src/client/shared/Spaarke.UI.Components/src/components/FilePreview/RichFilePreview.tsx` | Production |
| `AssociateToStep` 11-entity picker UX (R3) | `src/client/shared/Spaarke.UI.Components/src/components/AssociateToStep/` | Production |
| `SemanticSearchControl` PCF (layout reference) | `src/client/pcf/SemanticSearchControl/` | Production |
| `VisualHost` PCF | `src/client/pcf/VisualHost/` | Production |
| Calendar widget Pattern D canonical | `@spaarke/events-components` (node_modules) | Published |
| `@spaarke/ui-components` | `src/client/shared/Spaarke.UI.Components/` | Active dev |
| `BUILD-A-NEW-WORKSPACE-WIDGET.md` widget pattern doc | `docs/guides/BUILD-A-NEW-WORKSPACE-WIDGET.md` | Current (R4 W-2 rewrite 2026-05-26) |

### Parallel Project Coordination

| Branch / PR | Overlap Area | Coordination Strategy |
|---|---|---|
| **PR #372** `feature/ai-spaarke-ai-workspace-UI-r1` | `Spaarke.AI.Widgets` workspace area (overlaps R4-A) | Coordinate file ownership at task time; rebase R4-A on top of #372 if merged first |
| **work/spaarke-datagrid-framework-r1** (55 unmerged, no PR) | `Spaarke.UI.Components` heavy (overlaps R4-C shell hoist + R4-B toolbar primitives) | Sequence R4-C shell hoist AFTER datagrid-framework merge if possible; otherwise resolve at PR time |
| **work/matter-ui-r1-v1.1.72-vh-polish** (18 unmerged) | Visual Host (adjacent to R4-G but not same files) | Monitor; coordinate if Visual Host source changes |
| **work/spaarke-multi-container-multi-index-r1** (48 unmerged) | Probably docs/server-side only (low risk) | No coordination needed unless surface widens |
| Dependabot PRs (~10 open) | NuGet / npm version bumps | Independent; merge as they pass CI |

---

## 6. Testing Strategy

**Unit Tests** (target: all new shared-lib components covered):
- `<RecordNavigationModalShell>` — dirty-check protocol, nav state transitions, "N of M" rendering, action bar slot.
- `<SelectionAwareToolbar>` — show/hide on selection count, action callbacks.
- Regarding resolver service wrapper — write logic delegates correctly to `PolymorphicResolverService`.
- Vertical orientation hook — preference read/write round-trip via `sprk_userpreference`.

**Integration Tests** (per NFR-06):
- At least one entity (To Do OR RichFilePreview) end-to-end through `<RecordNavigationModalShell>`: open → navigate `<` `>` → dirty-check prompt → close.
- D resolver writes round-trip: pick entity, save, reload, verify all 5 fields persisted atomically.

**E2E Tests** (UI-test skill, browser-driven):
- SmartTodo Code Page: load → filter → select card → open modal → save in iframe → close → verify update.
- Visual Host on Matter form: load record → see "Upcoming To Dos" → drill through → SmartTodo modal opens pre-filtered.
- Workspace widget: load SpaarkeAi workspace → widget mounts → no OData errors → cards render.
- Vertical orientation toggle: load Code Page → toggle → re-orient → reload → preference restored.

**Regression Tests** (must pass post-deploy):
- File preview (RichFilePreviewDialog refactored) — visual diff ≤5px per FR-15.
- To Do save in OOB form — both inside iframe AND outside (no regression).
- Existing R3 statuscode flows — "Completed" persists in all open paths.

**a11y** (NFR-07, WCAG 2.1 AA):
- Keyboard nav across `<` `>` controls.
- Dirty-check prompt accessible.
- Selection checkbox + toolbar.
- Card "Open" icon focusable + activates with Enter.

**Performance** (NFR-05, NFR-08):
- Modal nav `<` → iframe content swap <500ms perceived latency (representative network).
- Vertical orientation switch <300ms; no layout jank.

---

## 7. Acceptance Criteria

### Technical Acceptance (per phase)

**Phase 0 (Audit + Spike)**:
- [ ] All 4 audit/spike notes written; each contains a decision + rationale
- [ ] D audit outcome unblocks D implementation task scope
- [ ] G spike outcome unblocks G form-addition task scope

**Phase 1 (Shared-lib hoist)**:
- [ ] `<RecordNavigationModalShell>` exported from `@spaarke/ui-components`; published
- [ ] `RichFilePreviewDialog` consumes shell; file preview regression suite passes; visual diff ≤5px
- [ ] Toolbar primitives (`<SelectionAwareToolbar>`, view toggle, orientation toggle) hoisted

**Phase 2 (Implementation)**:
- [ ] Wave 2a: A widget loads cleanly, B Code Page 4-row layout pixel-comparable, D resolver functional with 11 entities, G 4 chart defs deployed
- [ ] Wave 2b: C modal works in MDA + Code Page contexts, E affordances all functional, F orientation persists
- [ ] Wave 2c: G Visual Host cards on 4 parent forms with working drill-through

**Phase 3 (Deployment + Testing)**:
- [ ] All affected solutions deployed; smoke test green
- [ ] UI test report attached; NFR-05/07/08 all green
- [ ] `grep -i sprk_todoflag` returns zero functional hits

**Phase 4 (Wrap-up)**:
- [ ] `lessons-learned.md` written
- [ ] README status → Complete
- [ ] R4 PR merged

### Business Acceptance

- [ ] All 13 [Success Criteria from spec.md](spec.md#success-criteria) verify green in spaarkedev1
- [ ] UAT users confirm no `sprk_todoflag` errors
- [ ] OD-4 regressions (no save, "Completed" broken) verified fixed by smoke test

---

## 8. Risk Register

| ID | Risk | Probability | Impact | Mitigation |
|----|------|------------|---------|------------|
| R1 | Cross-frame messaging blocked under MDA security headers | Medium | High | Phase 0 spike validates; postMessage origin handshake; query-string fallback |
| R2 | D audit picks Web Resource, diverging from ADR-006 PCF preference | Low | Medium | Spec accepts audit as binding; document rationale; ADR amendment if pattern shifts |
| R3 | `sprk_smarttodo` Code Page doesn't accept query-string or modal-render | Medium | High | Phase 0 G spike; fallback to direct `window.open` with modal CSS |
| R4 | Parallel branch overlap with PR #372 or datagrid-framework branch | High | Medium | Coordinate file ownership; sequence R4-C shell hoist if datagrid merges first |
| R5 | `useLaunchContext` hook implementation/repurpose decision unclear | Confirmed | Low | Phase 0 audit decides; ~1 task either way |
| R6 | Vertical orientation causes layout jank in narrow widget container | Low | Medium | CSS transform-only re-layout; F NFR-08 test gates |
| R7 | LegalWorkspace standalone Code Page retirement (OC-R4-05) conflicts with A widget rebuild | Low | Medium | A targets shared lib + LegalWorkspace `components/SmartToDo/`; retirement is separate work — clarify boundary at A audit |
| R8 | `@spaarke/events-components` is published-only, no source in repo; A Pattern D requires new peer package | Confirmed | Low | A creates `@spaarke/smart-todo-components` if Pattern D chosen (per OC-R4 component model) |
| R9 | OOB To Do main form changes (added by other work) break iframe contract | Low | Medium | NFR-04 — form-designer changes propagate without R4 code change; verify after any form publish |
| R10 | Chart def deployment via PowerShell script fails on production env (auth issues) | Low | Medium | Use existing `Deploy-ChartDefinitionEntity.ps1`; smoke test in spaarkedev1 first |

---

## 9. Next Steps

1. **Review this plan** with project owner
2. **Run** `/task-create projects/smart-todo-r4` (or rely on `/project-pipeline` Step 3 in the same invocation) to generate task POML files
3. **Begin Phase 0** with audit tasks (parallel — 4 independent audits)
4. **Coordinate** with PR #372 owner + datagrid-framework branch owner on file ownership before Phase 1 starts

---

**Status**: Ready for Tasks
**Next Action**: Generate POML task files via `task-create` (Step 3 of `/project-pipeline`)

---

*For Claude Code: This plan provides implementation context. Load relevant sections when executing tasks. Phase 0 outputs (audit notes) feed directly into Phase 2 task scopes — do not skip them.*
