# Smart To Do Kanban R2 — Implementation Plan

> **Project**: events-smart-todo-kanban-r2
> **Created**: 2026-03-29
> **Source**: spec.md + design.md

## Executive Summary

Merge SmartToDo Kanban board and TodoDetailSidePane into a single unified Code Page with inline resizable detail panel. 19 tasks across 5 phases, targeting parallel execution via concurrent task agents.

**Scope**: Extract shared components, scaffold new Code Page, integrate two-panel layout, clean up legacy communication.

**Timeline**: 5 phases, ~25 estimated hours of work, reducible to ~14 hours wall-clock with parallelism.

## Architecture Context

### Applicable ADRs

| ADR | Constraint | Impact |
|-----|-----------|--------|
| ADR-006 | Code Page for standalone UI (React 19, Vite) | SmartTodo is a Code Page, not PCF |
| ADR-012 | Shared component library for reusable UI | PanelSplitter, useTwoPanelLayout, TodoDetail → `@spaarke/ui-components` |
| ADR-021 | Fluent v9 exclusively, dark mode required | All styling via design tokens |
| ADR-026 | Vite + vite-plugin-singlefile for Code Pages | Single HTML web resource output |

### Discovered Resources

| Type | Resource | Path |
|------|----------|------|
| ADR | ADR-006 (Code Page architecture) | `.claude/adr/ADR-006-pcf-over-webresources.md` |
| ADR | ADR-012 (Shared components) | `.claude/adr/ADR-012-shared-components.md` |
| ADR | ADR-021 (Fluent v9 design) | `.claude/adr/ADR-021-fluent-design-system.md` |
| ADR | ADR-026 (Code Page standard) | `.claude/adr/ADR-026-full-page-custom-page-standard.md` |
| Pattern | Code Page template | `.claude/patterns/webresource/full-page-custom-page.md` |
| Reference | Event-to-do architecture | `docs/architecture/event-to-do-architecture.md` |
| Code | PanelSplitter (source) | `src/client/code-pages/AnalysisWorkspace/src/components/PanelSplitter.tsx` |
| Code | usePanelLayout (reference) | `src/client/code-pages/AnalysisWorkspace/src/hooks/usePanelLayout.ts` |
| Code | TodoDetail (source) | `src/solutions/TodoDetailSidePane/src/components/TodoDetail.tsx` |
| Code | SmartToDo Kanban (source) | `src/solutions/LegalWorkspace/src/components/SmartToDo/` |
| Script | Deploy pattern | `scripts/Deploy-EventDetailSidePane.ps1` |

### Technical Decisions

1. **New `useTwoPanelLayout` hook** — two-panel variant with primary/detail naming, localStorage persistence. Not generalizing the three-panel hook (avoids AnalysisWorkspace regression).
2. **Copy Kanban components** from LegalWorkspace — not extracting to shared lib (out of R2 scope, keeps LegalWorkspace stable).
3. **TodoDetail accepts callback props** — `onSearchContacts`, `onSaveEventFields`, etc. Decouples from Xrm frame resolution.
4. **TodoContext** — React context providing items, selectedEventId, optimistic updateItem, and refetch.

## Implementation Approach

### Phase Structure

| Phase | Focus | Tasks | Estimated Hours |
|-------|-------|-------|----------------|
| 1 | Shared Library Extractions | 001, 002, 003, 010, 011 | 11h |
| 2 | SmartTodo Code Page Scaffold | 020, 021, 022 | 7h |
| 3 | Integration | 030, 031, 032, 033 | 9h |
| 4 | Polish & Testing | 040, 041, 042 | 3h |
| 5 | Deployment & Cleanup | 050, 051, 052, 055 | 4h |

### Critical Path

```
001 (PanelSplitter) → 002 (useTwoPanelLayout) ─┐
                                                 ├→ 030 (Layout) → 031 (DetailPanel) → 032 (Card Click) → 051 (Cleanup) → 055 (Wrap-up)
020 (Scaffold) → 021 (Context) + 022 (Domain) ─┘
```

### Parallel Execution Strategy

| Group | Tasks | Starts After | Duration |
|-------|-------|-------------|----------|
| A | 001, 003 | Immediately | 3h |
| B | 002 | 001 ✅ | 3h |
| C | 010, 011 | 003 ✅, 001 ✅ | 2h |
| D | 020 | Immediately | 2h |
| E | 021, 022 | 020 ✅ | 3h |
| F | 030, 031 | 002 ✅, 021 ✅, 022 ✅ | 5h (sequential) |
| G | 032, 033 | 030 ✅, 031 ✅ | 2h |
| H | 040, 041, 042 | 030 ✅ | 1h |
| I | 050 | 030 ✅ | 1h |
| J | 051, 052 | 033 ✅ | 2h (sequential) |
| K | 055 | All ✅ | 1h |

## Phase Breakdown

### Phase 1: Shared Library Extractions (Tasks 001-011)

**Objective**: Extract PanelSplitter, useTwoPanelLayout, and TodoDetail to `@spaarke/ui-components` so both SmartTodo Code Page and TodoDetailSidePane can consume them.

**Deliverables**:
- PanelSplitter component in shared library
- useTwoPanelLayout hook in shared library
- TodoDetail component in shared library (with callback-based API)
- TodoDetailSidePane refactored to use shared TodoDetail
- AnalysisWorkspace refactored to use shared PanelSplitter

### Phase 2: SmartTodo Code Page Scaffold (Tasks 020-022)

**Objective**: Create the new SmartTodo Code Page project and establish the core state management pattern.

**Deliverables**:
- `src/solutions/SmartTodo/` Vite project (React 19, singlefile)
- TodoContext with items, selectedEventId, optimistic updates
- All Kanban domain files copied and adapted from LegalWorkspace

### Phase 3: Integration (Tasks 030-033)

**Objective**: Wire the two-panel layout, TodoDetailPanel, card selection, and optimistic updates into a working unified Code Page.

**Deliverables**:
- SmartTodoApp with KanbanBoard + PanelSplitter + TodoDetailPanel
- Card click → inline detail panel open/close
- Save → optimistic single-item update in Kanban board

### Phase 4: Polish & Testing (Tasks 040-042)

**Objective**: Verify persistence, accessibility, and theme compliance.

**Deliverables**:
- localStorage panel state persistence verified
- ARIA/keyboard accessibility verified
- Dark mode + high-contrast verified

### Phase 5: Deployment & Cleanup (Tasks 050-055)

**Objective**: Create deployment script, remove legacy communication from LegalWorkspace, wrap up project.

**Deliverables**:
- Deploy-SmartTodo.ps1 script
- BroadcastChannel + sidePanes removed from LegalWorkspace SmartToDo
- LegalWorkspace updated to open SmartTodo Code Page
- All 10 success criteria verified

## Testing Strategy

| Level | Approach |
|-------|----------|
| Build verification | `npm run build` produces single HTML for SmartTodo, TodoDetailSidePane, AnalysisWorkspace |
| Functional | Card click → detail panel, save → optimistic update, panel resize |
| Regression | TodoDetailSidePane standalone still works, LegalWorkspace embedded still works |
| Accessibility | PanelSplitter ARIA, keyboard resize, focus indicators |
| Theme | Light, dark, high-contrast across all components |

## Risk Register

| ID | Risk | Probability | Impact | Mitigation |
|----|------|-------------|--------|------------|
| R1 | TodoDetail extraction breaks existing side pane | Medium | High | Task 010 verifies build immediately after extraction |
| R2 | useTwoPanelLayout doesn't handle edge cases | Low | Medium | Based on proven three-panel pattern; simpler = fewer edge cases |
| R3 | Kanban component copy diverges from LegalWorkspace | Low | Low | R2 scope only; future extraction project if needed |
| R4 | workspace-config-r1 useFeedTodoSync fix not merged | Low | Medium | SmartTodo Code Page includes no-op stub regardless |

## Acceptance Criteria

### Technical
- Single HTML web resource output from `npm run build`
- Zero BroadcastChannel references in SmartTodo
- Zero Xrm.App.sidePanes references in SmartTodo
- PanelSplitter has ARIA role="separator" with keyboard support
- All Fluent v9 tokens (no hard-coded colors)

### Business
- Card click opens inline detail panel instantly
- Save reflects in Kanban card without full page refresh
- Panel width persists across sessions
- Dark mode works throughout

---

*Generated by project-pipeline on 2026-03-29*
