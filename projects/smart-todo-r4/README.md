# Smart To Do — UX Enhancement (R4)

> **Last Updated**: 2026-06-10
>
> **Status**: In Progress

## Overview

R4 closes the UX gaps surfaced during R3 UAT. It rebuilds the SmartTodo Code Page header/toolbar to align with the validated `SemanticSearchControl` PCF pattern, replaces the broken side-pane with a **hybrid modal pattern** (a reusable `<RecordNavigationModalShell>` Code Page wrapper that iframe-embeds the OOB MDA "To Do main form"), surfaces a multi-environment-stable **regarding resolver** on the main form, adds **Visual Host "Upcoming To Dos"** cards to four parent forms, and rebuilds the stale SpaarkeAi workspace widget that still queries the retired `sprk_event.sprk_todoflag` shape.

The hybrid modal pattern is the architectural keystone: it standardizes record navigation (`<` `>` + "N of M") across all list-launched modals (To Do, Document preview, future entities) while keeping native Dataverse form behavior (save, BPF, business rules, statuscode controls) intact inside the iframe.

## Quick Links

| Document | Description |
|----------|-------------|
| [Project Plan](./plan.md) | Implementation plan with phase breakdown and WBS |
| [Design Spec](./spec.md) | AI-optimized specification — source of truth for FRs, NFRs, MUST rules |
| [Design Doc](./design.md) | Original human-authored design document (v3) |
| [Tasks](./tasks/TASK-INDEX.md) | Task registry, dependencies, parallel groups |
| [Project CLAUDE.md](./CLAUDE.md) | AI context file for this project |

## Current Status

| Metric | Value |
|--------|-------|
| **Phase** | Initialization → Foundation |
| **Progress** | 0% (0 of ~36 tasks complete) |
| **Target Date** | TBD |
| **Completed Date** | — |
| **Owner** | spaarke-dev |
| **Branch** | `work/smart-todo-r4` (worktree) |
| **Predecessor** | smart-todo-decoupling-r3 (PR #373 merged `e328beaf`; wrap-up PR #374 merged `a2ac6a849`) |

## Problem Statement

R3 decoupled `sprk_todo` from `sprk_event` and stood it up as a first-class entity. UAT then surfaced five UX gaps:

1. **Workspace widget broken** — A stale deployed bundle still emits `Could not find a property named 'sprk_todoflag' on type 'sprk_event'` because the widget hasn't been rebuilt to query `sprk_todo`.
2. **Side-pane bugs** — `TodoDetailPanel` doesn't save and the "Completed" statuscode control doesn't work; the side-pane pattern is the wrong tool for editing the full form.
3. **Toolbar drift** — The SmartTodo Code Page header/toolbar doesn't match the validated `SemanticSearchControl` PCF pattern users now expect.
4. **Regarding picker fragility** — No reusable, multi-environment-stable component exists for the 11-target regarding lookup that R3 introduced.
5. **No parent-form visibility** — Matter / Project / Invoice / WorkAssignment forms have no "Upcoming To Dos" surface; users have to navigate to the SmartTodo Code Page to see what's due.

Until these are fixed, users on UAT environments cannot rely on the new `sprk_todo` entity, which blocks adoption of the R3 architecture.

## Solution Summary

Seven workstreams (A–G) deliver the fixes. Two architectural patterns are load-bearing:

- **Hybrid modal pattern (C)** — A new `<RecordNavigationModalShell>` shared-lib component extracted from `RichFilePreview.tsx` provides chrome (`<` `>` nav, "N of M" counter, dirty-check prompt) and iframe-embeds the OOB MDA form. This keeps save / BPF / business rules / statuscode native (no React form re-implementation) while standardizing record navigation across all list-launched modals.
- **Polymorphic regarding resolver (D)** — A reusable component (PCF / Web Resource / Code Page embed — winner picked by audit) wraps `PolymorphicResolverService.applyResolverFields` so `sprk_todo` AND `sprk_communication` can share one regarding picker without per-form duplication.

Workstreams A (widget rebuild), B (toolbar overhaul), E (card affordances), F (vertical Kanban orientation toggle), and G (Visual Host cards on 4 parent forms) finish the UX closure.

## Graduation Criteria

The project is considered **complete** when all 13 success criteria from [spec.md §Success Criteria](spec.md#success-criteria) verify green in spaarkedev1:

- [ ] SmartTodo workspace widget mounts cleanly in both system + user-added contexts; no `sprk_todoflag` OData errors
- [ ] SmartTodo Code Page renders 4-row layout matching `SemanticSearchControl`; a11y audit clean
- [ ] "Assigned to Me" is the only filter mode (no "My Tasks" toggle)
- [ ] Selection-aware toolbar appears with Open / Delete / Email / Pin actions
- [ ] `<RecordNavigationModalShell>` renders chrome + iframe-embedded form; save + `<` `>` nav + "N of M" all work
- [ ] OD-4 regressions fixed — save persists, Completed status update works
- [ ] `RichFilePreviewDialog` works after refactor — no file preview regressions
- [ ] Regarding resolver visible + functional on To Do main form; 11 entity targets selectable; FR-13 mutual-exclusivity enforced
- [ ] Visual Host "Upcoming To Dos" cards visible on Matter / Project / Invoice / WorkAssignment forms; drill-through opens SmartTodo Code Page modal
- [ ] Vertical Kanban orientation toggle works + persists per user via `sprk_userpreference`
- [ ] Card Open icon + double-click + selection checkbox all functional
- [ ] `grep -i sprk_todoflag src/**/*.{ts,tsx,cs}` returns zero functional hits
- [ ] All affected deployed solutions rebuilt + redeployed; PR descriptions identify each surface

## Scope

### In Scope

- **A** Workspace widget rebuild (`SmartToDo` in LegalWorkspace; possible Pattern D dual-use)
- **B** SmartTodo Code Page header/toolbar overhaul (4-row layout + selection-aware toolbar)
- **C** Hybrid modal pattern — `<RecordNavigationModalShell>` shared component + iframe-embedded OOB To Do main form
- **D** Reusable regarding resolver (PCF or alternative per audit; wraps `PolymorphicResolverService`)
- **E** Card affordances — Open icon, double-click, selection checkbox
- **F** Vertical Kanban orientation toggle (persisted via `sprk_userpreference`)
- **G** Visual Host "Upcoming To Dos" cards on Matter / Project / Invoice / WorkAssignment forms (4 new `sprk_chartdefinition` records)
- Retire `TodoDetailPanel` side-pane
- Refactor `RichFilePreviewDialog` to consume the new shell (regression-safety check)

### Out of Scope

- Microsoft To Do bidirectional sync (R3 Phase 7; blocked on AAD scope; R5 or later)
- `sprk_eventtodo` entity delete cleanup (R3 task 005; deferred)
- SPE / Graph permission issue (BFF MI; Microsoft-support track)
- 10 deferred parent-form ribbons (R3 task 040 follow-up)
- Outlook add-in further enhancements beyond R3
- Event + Communication parent forms for G (only 4 entities in scope)
- Any BFF-side work — R4 is purely client-side + Dataverse configuration

## Key Decisions

| Decision | Rationale | ADR |
|----------|-----------|-----|
| Hybrid modal (Code Page wrapper + iframe-embedded OOB form) over `Xrm.Navigation.navigateTo` or pure-React form | Native save / BPF / business rules / statuscode kept in OOB form; lowest maintenance over time; one navigation pattern for all list-launched modals | [ADR-006](../../.claude/adr/ADR-006-pcf-over-webresources.md) (surface choice) |
| Wrap `PolymorphicResolverService.applyResolverFields` for D regarding writes (no reimplementation) | Avoid duplicating FR-13 mutual-exclusivity logic from R3; single source of truth for resolver writes across `sprk_todo` + `sprk_communication` | [ADR-024](../../.claude/adr/ADR-024-polymorphic-resolver.md) |
| All R4 shared primitives ship from `@spaarke/ui-components` (no inline component definitions in solution-specific source) | R3 NFR-02 carry-forward; one library, one place to maintain | [ADR-012](../../.claude/adr/ADR-012-shared-component-library.md) |
| Fluent UI v9 + Griffel `makeStyles` + semantic tokens mandatory; no Fluent v8 / no inline styles / no CSS modules | NFR-01; consistency with rest of Spaarke client surface | [ADR-021](../../.claude/adr/ADR-021-fluent-v9-design-system.md) |
| Filter modes reduced to "Assigned to Me" only (drop "My Tasks") | Owner field BU-owned; "My Tasks" surfaced confusion in UAT; one mode = one mental model | — |
| Drill-through opens SmartTodo Code Page modal, NOT entity list view | Modal preserves the curated Kanban UX; entity list view would dump users into raw Dataverse | — |
| Only 4 parent forms get Visual Host card (Matter / Project / Invoice / WorkAssignment) | Event + Communication explicitly excluded per OQ-3 | — |
| **D winner deferred to audit** (`projects/smart-todo-r4/notes/regarding-resolver-audit.md`) | Trade-offs between PCF, Web Resource, and embedded Code Page on multi-env stability genuine; assumption is PCF wins but audit may surface tie-breaker | [ADR-006](../../.claude/adr/ADR-006-pcf-over-webresources.md) (decision tree) |

## Risks & Mitigations

| Risk | Impact | Likelihood | Mitigation |
|------|--------|------------|------------|
| Cross-frame messaging (parent ↔ iframe) breaks under MDA security headers (frame-ancestors, X-Frame-Options) | High | Medium | Early spike at C: post-message origin handshake; fallback to query-string dirty signal if `postMessage` blocked |
| D audit picks Web Resource → diverges from PCF-preferred Spaarke standard | Medium | Low | Spec accepts audit outcome as binding; document rationale in `notes/regarding-resolver-audit.md`; ADR amendment if needed |
| `sprk_smarttodo` Code Page doesn't accept query-string launch params or doesn't render modal-style for drill-through | High (blocks G) | Medium | 30-min spike at start of G workstream; fallback: implement modal-style render in SmartTodo App.tsx itself |
| Parallel work overlap with PR #372 (`ai-spaarke-ai-workspace-UI-r1`) on `Spaarke.AI.Widgets` | Medium | High | Coordinate file ownership at task time; rebase R4-A on top of #372 if it merges first |
| Parallel work overlap with `work/spaarke-datagrid-framework-r1` (55 unmerged commits) on `Spaarke.UI.Components` | Medium | High | Schedule R4-C shell hoist after datagrid-framework merge if possible; otherwise resolve conflicts at PR time |
| `useLaunchContext` hook referenced in spec doesn't exist at the cited path (`src/solutions/SmartTodo/src/hooks/`) | Low | Confirmed (discovery flagged) | Implement new in R4 if needed for G drill-through context; ~1 task |
| Stale deployed widget surface (LegalWorkspace `SmartToDo`) actually requires source change in retired LegalWorkspace standalone Code Page | Medium | Low | Component-library boundary per OC-R4-05 means we rebuild widget in shared lib; LegalWorkspace standalone Code Page retirement (R4 DR-03) is separate work |
| Vertical Kanban orientation causes layout jank under narrow workspace-widget container | Medium | Low | NFR-08 sets <300ms switch target; CSS transform-only re-layout; test in narrow pane during F implementation |

## Dependencies

| Dependency | Type | Status | Notes |
|------------|------|--------|-------|
| R3 PR #373 merged to master | Internal | ✅ Done | `e328beafb` |
| R3 wrap-up PR #374 merged to master | Internal | ✅ Done | `a2ac6a849` (brought in R4 design.md/spec.md, spaarke-todo-architecture.md) |
| Dataverse "To Do main form" `eca59df4-1364-f111-ab0c-7ced8ddc4cc6` exists | External | ✅ Verified | Confirmed in spaarkedev1 |
| Chart definition `154bd4a4-f359-f111-a825-3833c5d9bcab` ("UPCOMING TASKS") | External | ✅ Verified | Pattern reference for G; confirmed in spaarkedev1 |
| VisualHost PCF deployed | Internal | ✅ Production | Used by G to render new chart defs |
| `BUILD-A-NEW-WORKSPACE-WIDGET.md` widget pattern doc current | Internal | ✅ Current | R4 task 011 / W-2 rewrite (2026-05-26) |
| `@spaarke/ui-components` builds clean from master | Internal | ✅ Verified | Used for all C shell + B toolbar hoists |
| No new BFF endpoints / Azure resources / external integrations | External | ✅ Confirmed | Spec line 205 explicit |

## Changelog

| Date | Version | Change | Author |
|------|---------|--------|--------|
| 2026-06-10 | 1.0 | Initial spec seeded from design.md v3 (`0a643bcbc`) | spaarke-dev |
| 2026-06-10 | 1.1 | Project initialized via `/project-pipeline`: README, plan.md, CLAUDE.md, tasks/ | spaarke-dev |

---

*Project initialized 2026-06-10 via `/project-pipeline projects/smart-todo-r4` on Opus 4.7. See [plan.md](plan.md) for phase breakdown and [tasks/TASK-INDEX.md](tasks/TASK-INDEX.md) for the task list.*
