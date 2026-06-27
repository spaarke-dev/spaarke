# Smart To Do — UX Enhancement (R4) — AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-06-10
> **Source**: `projects/smart-todo-r4/design.md` (v3)
> **Predecessor**: smart-todo-decoupling-r3 (PR #373 merged `e328beaf`; wrap-up PR #374)

---

## Executive Summary

R3 decoupled `sprk_todo` from `sprk_event` and stood it up as a first-class entity. R4 closes UX gaps surfaced during R3 UAT: it rebuilds the SmartTodo Code Page header/toolbar to align with the validated Semantic Search PCF pattern, replaces the broken side-pane with a **hybrid modal pattern** (a reusable `<RecordNavigationModalShell>` Code Page wrapper that iframe-embeds the OOB MDA "To Do main form"), surfaces a multi-environment-stable **regarding resolver** on the main form, adds **Visual Host "Upcoming To Dos"** cards to four parent forms, and rebuilds the stale SpaarkeAi workspace widget that still queries the retired `sprk_event.sprk_todoflag` shape.

The hybrid modal pattern is the architectural keystone: it standardizes record navigation (`<` `>` + "N of M") across all list-launched modals (To Do, Document preview, future entities) while keeping native Dataverse form behavior (save, BPF, business rules, statuscode controls) intact inside the iframe.

---

## Scope

### In Scope

- **A** — Workspace widget rebuild: identify the deployed surface(s) still emitting the OData `sprk_event.sprk_todoflag` query, rebuild against `sprk_todo`, apply Pattern D dual-use widget architecture, redeploy.
- **B** — SmartTodo Code Page header/toolbar overhaul: 4-row layout (title / search+actions / filter bar / selection-aware toolbar) aligned with `SemanticSearchControl` pattern; Fluent v9 + shared component library mandatory.
- **C** — HYBRID modal pattern: extract `<RecordNavigationModalShell>` (single Code Page wrapper) from existing `RichFilePreview.tsx` navigation logic; iframe-embed the OOB "To Do main form" (`eca59df4-1364-f111-ab0c-7ced8ddc4cc6`) as content; cross-frame messaging for dirty-check + nav. Used for both MDA-context AND Code Page-context launches.
- **D** — Reusable regarding resolver: audit PCF vs Web Resource vs embedded Code Page on multi-env stability + resilience; implement the winner; wrap `PolymorphicResolverService.applyResolverFields`; reusable for `sprk_todo` AND `sprk_communication` (same regarding shape).
- **E** — Card affordances: "Open" icon upper-right; double-click body opens modal; selection checkbox upper-left (drives toolbar in B).
- **F** — Vertical Kanban orientation toggle on the Code Page toolbar; persisted as user preference.
- **G** — Visual Host "Upcoming To Dos" cards on **Matter / Project / Invoice / WorkAssignment** main forms; clone of chart definition `154bd4a4-f359-f111-a825-3833c5d9bcab` pattern but target `sprk_todo`; drill-through opens SmartTodo Code Page modal (NOT entity list view).
- Retire `TodoDetailPanel` side-pane in `src/solutions/SmartTodo/`.
- Refactor `RichFilePreviewDialog` to consume the new `<RecordNavigationModalShell>` (proves the abstraction without regression).

### Out of Scope

- **Microsoft To Do bidirectional sync** (R3 Phase 7; blocked on AAD `Tasks.ReadWrite` scope add by tenant admin; will be R5 or follow-on project).
- **`sprk_eventtodo` entity delete cleanup** (R3 task 005; deferred per user; 26 appmodulecomponent refs).
- **SPE / Graph permission issue** (BFF MI missing from container-type registration; platform / Microsoft-support issue; independent of R4).
- **10 deferred parent-form ribbons** (R3 task 040 follow-up; defer per OD-6).
- **Outlook add-in further enhancements** beyond what R3 shipped (CreateTodoButton + LinkedTodosBanner).
- **Event and Communication entities** for Visual Host "Upcoming To Dos" cards (OQ-3 — only 4 entities in scope).
- **`/api/spe/...` admin endpoints** and any BFF-side work (R4 is purely client-side + Dataverse config).

### Affected Areas

- `src/solutions/SmartTodo/` — Code Page UI overhaul (B), card affordances (E), vertical orientation toggle (F), modal wiring (C)
- `src/solutions/LegalWorkspace/src/components/SmartToDo/` — widget rebuild or retirement (A); likely target for the legacy-query failure
- `src/client/shared/Spaarke.UI.Components/src/components/FilePreview/RichFilePreview*.tsx` — source for navigation pattern extraction (C); `RichFilePreviewDialog` refactor target
- `src/client/shared/Spaarke.UI.Components/src/components/` — new `RecordNavigationModalShell/` hoist (C); new shared primitives for Code Page toolbar (B)
- `src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/` — new `SmartTodoWidget` registration if Pattern D dual-use is chosen (A)
- `src/client/pcf/RegardingResolver/` *(new, pending audit outcome)* — PCF for resolver (D)
- Form designer: **To Do main form** (`eca59df4-1364-f111-ab0c-7ced8ddc4cc6`) — host regarding resolver (D)
- Form designer: **Matter / Project / Invoice / WorkAssignment main forms** — add Visual Host "Upcoming To Dos" section (G)
- Dataverse: **4 new chart definitions** for G (one per parent entity)
- `infrastructure/dataverse/charts/` — JSON chart definitions if following existing scripts pattern
- `scripts/` — `Create-UpcomingTodosChartDefinitions.ps1` if scripted deployment used

---

## Requirements

### Functional Requirements

#### A. Workspace widget

1. **FR-01**: Identify the failing deployed widget surface(s) emitting the `Could not find a property named 'sprk_todoflag' on type 'sprk_event'` error. Acceptance: audit report names the file(s); covers both "system widget" load AND user-added workspace widget load.
2. **FR-02**: Rebuild the widget to query `sprk_todo` (not `sprk_event`). Acceptance: 0 OData $select hits for `sprk_todoflag`; query targets `sprk_todo` filtered by current workspace's regarding context AND statuscode in {Open, In Progress}.
3. **FR-03**: Apply the appropriate widget archetype per `docs/guides/BUILD-A-NEW-WORKSPACE-WIDGET.md` decision tree (likely **Pattern D dual-use**; Calendar canonical reference). Acceptance: PR description documents the archetype chosen + why.
4. **FR-04**: Comply with widget layout structure pattern. Acceptance: widget mounts cleanly under all 6 workspace layouts (including narrow-pane); no a11y regressions vs. neighboring widgets.
5. **FR-05**: Rebuild and redeploy affected solutions. Acceptance: workspace + dashboard surfaces both render the widget; UAT-step error from R3 retro is gone.

#### B. SmartTodo Code Page header/toolbar overhaul

6. **FR-06**: Layout = 4 stacked rows: (1) page title "Smart To Do" full-width; (2) search box + Refresh + "+ New" icons; (3) filter bar (facets + Clear); (4) selection-aware toolbar (when ≥1 card selected). Acceptance: pixel-comparable to `SemanticSearchControl` PCF.
7. **FR-07**: Filter modes — only "Assigned to Me" (drop "My Tasks" parallel mode per OD-2). Acceptance: filter UI shows only one mode-equivalent toggle.
8. **FR-08**: Selection-aware toolbar exposes: **Open** (opens modal — see C), **Delete**, **Email**, **Pin** (toggles `sprk_todopinned`). Acceptance: each action functional; toolbar hides when 0 cards selected.
9. **FR-09**: List / Card view toggle present; matches `SemanticSearchControl` icon set. Acceptance: view persists per-user (likely `sprk_userpreference`).
10. **FR-10**: All new UI MUST use `@spaarke/ui-components`; hoist any new primitives that don't yet exist there. Acceptance: no inline component definitions in `src/solutions/SmartTodo/`; any new shared primitives ship from `@spaarke/ui-components`.
11. **FR-11**: All new styling MUST follow `/fluent-v9-component` skill (Griffel `makeStyles` + semantic tokens; no inline styles; no v8 components). Acceptance: `eslint --no-restricted-imports` check + visual review.

#### C. HYBRID modal pattern + universal `<` `>` record navigation

12. **FR-12**: Extract navigation logic from `RichFilePreview.tsx` (`currentIndex`, `navigationTotal`, `onNavigate`, `ChevronLeft20Regular` / `ChevronRight20Regular`, `prevDisabled` / `nextDisabled`) into a new shared `<RecordNavigationModalShell>` component in `@spaarke/ui-components`. Acceptance: shell has props `currentIndex`, `navigationTotal`, `onNavigate`, `title`, `actionBar`, `children`; renders chrome.
13. **FR-13**: The shell renders an iframe pointing at `Xrm.Page.context.getClientUrl()/main.aspx?pagetype=entityrecord&etn=<entity>&id=<recordId>&formid=<formId>&navbar=off`. Acceptance: iframe renders the OOB MDA form; save + BPF + business rules + statuscode all function natively inside.
14. **FR-14**: Cross-frame messaging protocol for dirty-check on `<` `>` navigation: outer shell posts `request-dirty-check`, inner form responds `dirty-check-result` ({dirty: bool}); if dirty, outer shell prompts user before navigating iframe src. Acceptance: dirty user is prompted; clean user navigates without prompt; the message protocol is documented in `@spaarke/ui-components` API docs.
15. **FR-15**: Refactor `RichFilePreviewDialog` to consume `<RecordNavigationModalShell>`. Acceptance: existing file preview flows (Document viewer, attachment preview) function with no regressions; visual diff ≤5px.
16. **FR-16**: SmartTodo card-open path uses `<RecordNavigationModalShell>` with To Do main form (`eca59df4-1364-f111-ab0c-7ced8ddc4cc6`) as iframe target. Acceptance: clicking "Open" on a card from the Kanban opens the modal; `<` `>` browses other cards in the current filter set.
17. **FR-17**: Modal works from both **MDA context** (launched via subgrid command, ribbon, or visual host drill-through) AND **Code Page context** (launched from inside SmartTodo Code Page). Acceptance: tested + verified in both launch contexts.
18. **FR-18**: Retire `TodoDetailPanel` side-pane in `src/solutions/SmartTodo/`. Acceptance: file deleted; references removed; OD-4 issues (no save, "Completed" doesn't work) gone because they were side-pane bugs.

#### D. Reusable regarding resolver

19. **FR-19**: Audit step evaluates PCF, Web Resource, and embedded Code Page approaches against criteria: (a) solution export/import portability for multi-env deploys; (b) form designer ergonomics; (c) coupling to BFF (zero preferred); (d) maintenance burden (one shared artifact vs. per-form duplication). Acceptance: audit document in `projects/smart-todo-r4/notes/regarding-resolver-audit.md`; recommends one approach with rationale.
20. **FR-20**: Implement the audited winner. Acceptance: component supports all 11 entity targets (Matter / Project / Event / Communication / WorkAssignment / Invoice / Budget / Analysis / Organization / Contact / Document); single-row picker UX; mutual-exclusivity enforced.
21. **FR-21**: MUST wrap `PolymorphicResolverService.applyResolverFields` for the regarding write (no reimplementation of FR-13 mutual-exclusivity logic). Acceptance: code review confirms the wrap.
22. **FR-22**: Reusable for `sprk_todo` AND `sprk_communication` (same shape). Acceptance: zero entity-specific branching in the component; entity is a prop / configuration.
23. **FR-23**: Added to the To Do main form (`eca59df4-1364-f111-ab0c-7ced8ddc4cc6`). Acceptance: regarding picker visible on main form; saving persists all 5 fields (`sprk_regarding<X>`, `sprk_regardingrecordtype`, `sprk_regardingrecordid`, `sprk_regardingrecordname`, `sprk_regardingrecordurl`).
24. **FR-24**: When form is non-edit (read-only or view-only role), the resolver renders the current value(s) read-only — no edit UI. Acceptance: tested in user with read-only role.

#### E. Card affordances

25. **FR-25**: "Open" icon (Fluent v9 `Open20Regular` or equivalent) in upper-right of each Kanban card. Acceptance: single-click on icon opens the modal.
26. **FR-26**: Double-click on card body opens the modal. Acceptance: double-click anywhere on the card (except the selection checkbox) triggers the same open path.
27. **FR-27**: Selection checkbox in upper-left of each card; drives the selection-aware toolbar (FR-08). Acceptance: single-select + multi-select both work; ≥1 selected → toolbar appears.

#### F. Vertical Kanban orientation

28. **FR-28**: Toolbar exposes an orientation toggle (icon: `LayoutColumnTwo20Regular` ↔ `LayoutRowTwoSplit20Regular` or equivalent). Acceptance: toggle present; clicking re-orients Kanban from horizontal columns to vertical sections.
29. **FR-29**: Vertical orientation = each column ("Today / Tomorrow / Future") becomes a collapsible section stacked top-to-bottom; cards stack within each section. Acceptance: works on standalone Code Page + when mounted as workspace widget in narrow container.
30. **FR-30**: Persist orientation preference via `sprk_userpreference.preferencetype = "SmartTodoOrientation"`. Acceptance: user's last selection restored on next visit.

#### G. Visual Host "Upcoming To Dos" cards

31. **FR-31**: Create 4 new chart definitions in `sprk_chartdefinition`, one per parent entity, modeled on `154bd4a4-f359-f111-a825-3833c5d9bcab` ("UPCOMING TASKS"). Acceptance: 4 new chart def records present in spaarkedev1; each has correct `sprk_entitylogicalname = sprk_todo`, correct fetchxml, correct `sprk_contextfieldname`, correct `sprk_drillthroughtarget`.
32. **FR-32**: Each chart def fetchxml queries `sprk_todo` where (statecode = 0) AND (statuscode in {1, 659490001}) AND (`sprk_duedate` next-5-days OR `sprk_todopinned` = true). Acceptance: SQL/Web API verification.
33. **FR-33**: Each chart def's `sprk_contextfieldname` is the corresponding `sprk_regarding<X>` lookup (e.g., `sprk_regardingmatter` for Matter, `sprk_regardingproject` for Project, etc.). Acceptance: at Visual Host runtime, the parent record's ID is filtered through this field.
34. **FR-34**: `sprk_drillthroughtarget` opens the SmartTodo Code Page (`sprk_smarttodo`) modal with launch-context URL params pre-filtering to the parent record. Acceptance: drill-through invokes `Xrm.Navigation.navigateTo({pageType: "webresource", webresourceName: "sprk_smarttodo"}, {target: 2})` with `?regardingType=<entity>&regardingId=<id>` query string — reuses R3 task 070b URL-param parser. Drill-through does NOT navigate to an entity list view.
35. **FR-35**: `sprk_visualtype` = "Due Date Card List" (id `100000009`, matching UPCOMING TASKS chart def). Acceptance: visual matches the UPCOMING TASKS card shape on Matter form.
36. **FR-36**: Add the Visual Host control to Matter / Project / Invoice / WorkAssignment main forms. Acceptance: each form has the new "Upcoming To Dos" section; renders the chart def via VisualHost PCF.

### Non-Functional Requirements

- **NFR-01**: All UI follows Fluent UI v9 + semantic tokens + Griffel `makeStyles`. No Fluent v8. No inline styles. No CSS modules. (ADR-021)
- **NFR-02**: All shared UI primitives ship from `@spaarke/ui-components`. No new shared primitives may live in solution-specific source. (R3 NFR-02 carry-forward)
- **NFR-03**: Code Pages are multi-environment portable — no hardcoded environment URLs, app IDs, container IDs, or chart definition IDs in source. Use config + URL params. (R3 portability mandate carry-forward)
- **NFR-04**: Form-designer changes to the To Do main form propagate without R4 code change (validates the hybrid modal architecture).
- **NFR-05**: Modal navigation (`<` `>`) feels instant — <500ms perceived latency between click and iframe content swap, measured on a representative network.
- **NFR-06**: Test coverage — unit tests for `<RecordNavigationModalShell>` (dirty-check protocol, nav state transitions) + integration test for at least one entity (To Do or RichFilePreview).
- **NFR-07**: Accessibility — keyboard navigation across `<` `>` controls; dirty-check prompt accessible; selection checkbox + toolbar follow WCAG 2.1 AA. No regression vs. R3.
- **NFR-08**: Performance — vertical Kanban orientation switch <300ms; no layout jank on the workspace widget mount.
- **NFR-09**: PR description for every R4 task touching the deployed bundle MUST identify the deployed surface(s) affected and confirm rebuild + redeploy steps.

---

## Technical Constraints

### Applicable ADRs

- **ADR-024** — Polymorphic resolver pattern. Binding for all regarding writes (FR-21).
- **ADR-021** — Fluent UI v9 design system. Applies to B, C, E, F (FR-11).
- **ADR-022** — PCF platform libraries. Applies if D audit chooses PCF.
- **ADR-006** — PCF over Web Resources. Informs D decision but may be overridden if stability wins.
- **ADR-028** — Spaarke Auth v2 (OBO + MI). Applies anywhere R4 talks to BFF (likely no new BFF surface; verify during implementation).
- **ADR-032** — Null-Object Kill-Switch Pattern. Applies if R4 adds any feature-gated BFF service (likely none; verify).

### MUST Rules

- ✅ **MUST** use `@spaarke/ui-components` for all shared UI primitives (NFR-02).
- ✅ **MUST** follow `/fluent-v9-component` skill for all styling work (FR-11, NFR-01).
- ✅ **MUST** wrap `PolymorphicResolverService.applyResolverFields` for any regarding write (FR-21; ADR-024).
- ✅ **MUST** use the **HYBRID modal pattern** (`<RecordNavigationModalShell>` + iframe-embedded OOB MDA form) for the new modal — no pure-React form re-implementation (FR-12 through FR-18).
- ✅ **MUST** be multi-environment portable — no hardcoded environment-specific values (NFR-03).
- ✅ **MUST** rebuild + redeploy affected deployed solutions after source changes (NFR-09).
- ✅ **MUST** comply with `BUILD-A-NEW-WORKSPACE-WIDGET.md` decision tree for A (FR-03).
- ❌ **MUST NOT** query `sprk_event` for `sprk_todoflag` (the field was removed in R3).
- ❌ **MUST NOT** reimplement save / BPF / business rules / statuscode flows in a custom React form (the OOB MDA form inside the iframe handles these).
- ❌ **MUST NOT** introduce v8 Fluent components or inline styles (NFR-01).
- ❌ **MUST NOT** retain the broken `TodoDetailPanel` side-pane (FR-18).
- ❌ **MUST NOT** drill-through the Visual Host card to an entity list view — must open the SmartTodo Code Page modal (FR-34).

### Existing Patterns to Follow

- **Navigation pattern source**: `src/client/shared/Spaarke.UI.Components/src/components/FilePreview/RichFilePreview.tsx` — extract for `<RecordNavigationModalShell>` (FR-12).
- **Regarding picker UX**: `src/client/shared/Spaarke.UI.Components/src/components/AssociateToStep/` — existing 11-entity picker; informs D component UX (FR-20).
- **Resolver write logic**: `src/client/shared/Spaarke.UI.Components/src/services/PolymorphicResolverService.ts` — wrap, don't reimplement (FR-21).
- **Visual Host PCF**: `src/client/pcf/VisualHost/control/` — used to render the new chart definitions (FR-31 through FR-36).
- **Chart def pattern**: chart definition `154bd4a4-f359-f111-a825-3833c5d9bcab` ("UPCOMING TASKS") — clone for sprk_todo (FR-31).
- **Layout reference**: `src/client/pcf/SemanticSearchControl/SemanticSearchControl/SemanticSearchControl.tsx` — visual + structural reference for B (FR-06).
- **Widget pattern reference**: `BUILD-A-NEW-WORKSPACE-WIDGET.md`; Calendar widget Pattern D worked example.
- **URL-param launch context**: R3 task 070b — `useLaunchContext` hook in `src/solutions/SmartTodo/src/hooks/useLaunchContext.ts` — reused for Visual Host drill-through (FR-34).
- **User-preference persistence**: existing `sprk_userpreference` entity pattern used in R3 — reuse for F orientation toggle (FR-30).

---

## Success Criteria

1. [ ] SmartTodo workspace widget mounts cleanly in both "system" and user-added contexts in spaarkedev1 — **verify**: open SpaarkeAi workspace + add widget; no console errors; no 400 OData errors.
2. [ ] SmartTodo Code Page renders 4-row layout per design — **verify**: visual review against `SemanticSearchControl` PCF; a11y audit clean.
3. [ ] "Assigned to Me" is the only filter mode — **verify**: UI inspection; no "My Tasks" toggle present.
4. [ ] Selection-aware toolbar appears with Open/Delete/Email/Pin — **verify**: select 1+ cards; all 4 actions functional.
5. [ ] `<RecordNavigationModalShell>` renders chrome + iframe-embedded form — **verify**: open from Kanban; iframe contains OOB To Do main form; native save works; `<` `>` browse works; "N of M" counter accurate.
6. [ ] OD-4 regressions fixed — **verify**: save persists; "Completed" status update works; no need for separate cleanup.
7. [ ] `RichFilePreviewDialog` works after refactor — **verify**: open file preview from Document grid; visual + functional regression test.
8. [ ] Regarding resolver visible + functional on To Do main form — **verify**: all 11 entity targets selectable; saving persists 5 fields atomically; FR-13 mutual-exclusivity enforced.
9. [ ] Visual Host "Upcoming To Dos" card visible + functional on Matter / Project / Invoice / WorkAssignment forms — **verify**: open one record of each parent type; card renders; drill-through opens SmartTodo Code Page modal pre-filtered to parent.
10. [ ] Vertical Kanban orientation toggle works + persists — **verify**: toggle in both standalone Code Page + workspace widget context; preference restored on next visit.
11. [ ] Card "Open" icon + double-click + selection checkbox all functional — **verify**: each affordance tested.
12. [ ] `grep -i sprk_todoflag src/**/*.{ts,tsx,cs}` returns zero functional hits (per FR-29 carry-forward) — **verify**: documented in wrap-up note.
13. [ ] All deployed solutions affected by R4 source changes rebuilt + redeployed — **verify**: PR descriptions identify each surface; verified by smoke test in spaarkedev1.

---

## Dependencies

### Prerequisites

- R3 PR #373 merged to master ✅ (squash `e328beaf`)
- R3 wrap-up PR #374 ready (R4 design + spec live here) ✅
- Dataverse "To Do main form" (`eca59df4-1364-f111-ab0c-7ced8ddc4cc6`) exists ✅
- Chart definition `154bd4a4-f359-f111-a825-3833c5d9bcab` ("UPCOMING TASKS") present in spaarkedev1 ✅ (pattern reference)
- VisualHost PCF deployed ✅
- `BUILD-A-NEW-WORKSPACE-WIDGET.md` widget pattern doc current ✅ (R4 task 011 / W-2 rewrite)
- `@spaarke/ui-components` builds clean from master ✅

### External

- None. R4 is purely client-side + Dataverse config. **No new BFF endpoints, no new Azure resources, no new external service integrations.**

---

## Owner Clarifications

*Captured from the design conversation 2026-06-10:*

| Topic | Question | Answer | Impact |
|---|---|---|---|
| Filter modes | "My Tasks" vs "Assigned to Me" | Only "Assigned to Me" — owner field not important (BU-owned) | FR-07: drop one mode |
| Modal hosting | `Xrm.Navigation.navigateTo` vs Code Page iframe | HYBRID — Code Page wrapper + iframe-embedded OOB form | C section + FR-12 to FR-18 |
| `<` `>` nav scope | MDA-context modal too, or only Code-Page-context? | Yes — adopted hybrid means single pattern everywhere | FR-17 (both contexts) |
| Resolver pattern | PCF vs Web Resource vs Code Page embed | Whichever is most resilient/stable for multi-env deploys | D audit step (FR-19) |
| Side-pane bugs | What's broken? | No save; "Completed" doesn't work | FR-18 (retire); MDA form fixes both |
| Ribbons | 10 deferred ribbons in R4 scope? | Defer | OD-6 → Out of Scope |
| Visual Host targets | Include Event + Communication? | No, only Matter/Project/Invoice/WorkAssignment | OQ-3 → 4 entities only |
| Visual Host drill | Entity list view or modal? | Modal (Kanban app) | FR-34 |
| Widget failure | Which surface? | Both "system" AND user-added; stale deployed bundle | A audit step (FR-01); LegalWorkspace `SmartToDo` is likely culprit |
| `< >` in MDA modal | Trade-off analysis advantages/disadvantages | Hybrid wins on maintenance burden; lowest-maint over time | C section + design.md trade-off table |

## Assumptions

*Proceeding with these (owner did not need to specify):*

- **PCF audit outcome** — assuming PCF is the likely winner for D (modern Spaarke standard) but the audit may surface a tie-breaker. If audit chooses Web Resource or embedded Code Page, treat the choice as binding without further user approval.
- **Hidden field for PCF binding** — if D winner is PCF, assuming the PCF binds to a single hidden field (e.g., `sprk_regardingrecordtype`) and writes the other 4 fields via side effects. This is the modern PCF pattern for multi-field writes.
- **Chart definitions deployed via PowerShell** — assuming the 4 new chart definitions for G are deployed via a new script `scripts/Create-UpcomingTodosChartDefinitions.ps1` following the existing `scripts/create-test-chartdefinitions.ps1` pattern. Alternative: manual creation in spaarkedev1 maker portal. Pick during planning.
- **Cross-frame messaging via `postMessage`** — assuming standard `window.postMessage` with origin checks (Spaarke domain only) for FR-14 dirty-check protocol. Alternative `BroadcastChannel` if needed for multi-tab scenarios.
- **Vertical Kanban orientation default = horizontal** — assuming first-visit users see horizontal columns (matches R3 default); preference flips to vertical only via toggle.
- **Workspace widget Pattern D** — assuming Pattern D dual-use (shared-lib widget + thin LW shim) is the right archetype per `BUILD-A-NEW-WORKSPACE-WIDGET.md`. If the audit identifies a simpler Pattern A composable section path, that's an acceptable substitution.

## Unresolved Questions

*Implementation-time questions that the audit / design-spike steps will resolve. None are blocking; all have stated assumptions that will hold unless audit findings overturn them:*

- [ ] **D audit outcome** — PCF vs Web Resource vs embedded Code Page. Blocks: FR-19 implementation choice. Resolved by: `projects/smart-todo-r4/notes/regarding-resolver-audit.md` during early R4 tasks.
- [ ] **Workspace widget surface identification** — which specific deployed solution(s) emit the legacy query. Blocks: FR-01 fix scope. Resolved by: A audit step.
- [ ] **PCF binding strategy** if PCF wins D audit — single hidden field + side effects, or other pattern. Resolved by: PCF implementer during the build.
- [ ] **Drill-through URL signature** — confirm `Xrm.Navigation.navigateTo({pageType: "webresource", webresourceName: "sprk_smarttodo"})` accepts query-string launch params and renders modal-style (not full-window). Blocks: FR-34. Resolved by: small implementation spike (~30 min).

---

*AI-optimized specification. Original design: [`projects/smart-todo-r4/design.md`](design.md). Status: Ready for `/project-pipeline`.*
