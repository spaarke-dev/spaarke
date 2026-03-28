# Smart To Do Kanban R2 — AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-03-28
> **Source**: design.md
> **Prerequisite**: ai-analysis-workspace-sprkchat-integration-r1 (completed)

## Executive Summary

Merge the SmartToDo Kanban board and TodoDetailSidePane into a single unified Code Page with an inline resizable detail panel. This eliminates BroadcastChannel communication, duplicate MSAL auth, duplicate theme detection, the fixed 400px Dataverse side pane constraint, and the 6-layer navigation detection hack — replacing all of this with direct React state/callbacks in a single React 19 tree.

## Scope

### In Scope
- Inline TodoDetailPanel that replaces `Xrm.App.sidePanes` iframe for the Kanban context
- PanelSplitter extraction from AnalysisWorkspace to `@spaarke/ui-components` shared library
- Two-panel `usePanelLayout` variant (adapt existing three-panel hook for Kanban + Detail)
- TodoContext for shared state between Kanban board and detail panel (items, selectedEventId, optimistic updates)
- Optimistic single-item update on save (replace full `getActiveTodos()` refetch)
- Remove BroadcastChannel from SmartToDo component
- Remove 6-layer side pane lifecycle management code (~140 lines)
- Refactor `TodoDetailSidePane` into a reusable `TodoDetail` component in `@spaarke/ui-components` (for use in both Kanban inline panel and future standalone contexts)
- Panel width/state persistence via localStorage

### Out of Scope
- SmartToDo as a workspace section (handled by workspace-config R1)
- Changes to the Kanban board UX (columns, scoring algorithm, drag-and-drop behavior)
- Changes to TodoDetail fields, save logic, or dual-entity loading
- Mobile/responsive layout
- EventsPage list view integration with TodoDetail (future project)

### Affected Areas
- `src/solutions/SmartTodo/` — Unified Code Page (primary implementation target)
- `src/client/shared/Spaarke.UI.Components/` — Extract PanelSplitter, usePanelLayout, TodoDetail to shared library
- `src/client/code-pages/AnalysisWorkspace/` — Refactor to consume PanelSplitter/usePanelLayout from shared library (instead of local copies)
- `src/solutions/TodoDetailSidePane/` — Refactor: extract reusable TodoDetail component, keep as thin standalone Code Page wrapper
- `src/solutions/LegalWorkspace/` — Update SmartToDo integration if embedded mode changes

## Requirements

### Functional Requirements

1. **FR-01**: Card click in Kanban board opens TodoDetailPanel as an inline right panel within the same Code Page — Acceptance: Detail panel slides in from right, showing the clicked event's todo details
2. **FR-02**: TodoDetailPanel is resizable via PanelSplitter drag handle — Acceptance: User can drag the divider to resize; double-click resets to default 400px; keyboard arrow keys resize in 20px steps
3. **FR-03**: TodoDetailPanel is collapsible — Acceptance: Clicking the same card again (or a close button) collapses the panel; Kanban board expands to fill available width
4. **FR-04**: Save in TodoDetailPanel performs optimistic single-item update — Acceptance: Updated item reflects immediately in the Kanban board without full refetch; if save fails, revert to previous state
5. **FR-05**: TodoContext provides shared state for items, selectedEventId, and update callbacks — Acceptance: Both Kanban and Detail panel read from the same context; no BroadcastChannel or JSON serialization
6. **FR-06**: Panel open/closed state and width persist across page loads via localStorage — Acceptance: Reopening the SmartTodo Code Page restores previous panel width and collapsed/expanded state
7. **FR-07**: SmartToDo component modes continue to work — Acceptance: `embedded=true, disableSidePane=true` (workspace glance) shows no detail panel; `embedded=false` (standalone) shows inline detail panel on card click
8. **FR-08**: TodoDetailSidePane remains available as a standalone Code Page for non-Kanban contexts — Acceptance: `sprk_tododetailsidepane` web resource still works when opened independently (e.g., from EventsPage), consuming the extracted TodoDetail component from shared library

### Non-Functional Requirements

- **NFR-01**: Panel open animation completes in <200ms (CSS transition, no layout thrash)
- **NFR-02**: Optimistic update reflects in Kanban board within one React render cycle (no loading spinner for save)
- **NFR-03**: Single deployment artifact — one HTML web resource (`sprk_smarttodo`) replaces the current two-resource deployment
- **NFR-04**: Accessibility: PanelSplitter has `role="separator"`, keyboard resize (ArrowLeft/ArrowRight), focus-visible indicator (WCAG 2.1 AA)

## Technical Constraints

### Applicable ADRs

- **ADR-006** (UI Surface Architecture): SmartTodo is a Code Page (standalone HTML web resource, React 19 bundled). Not a PCF control.
- **ADR-012** (Shared Component Library): PanelSplitter, usePanelLayout, and TodoDetail must be extracted to `@spaarke/ui-components`. Import via `@spaarke/ui-components`.
- **ADR-021** (Fluent UI v9): All UI uses Fluent v9 components and design tokens. Dark mode and high-contrast required. `makeStyles` for custom styling.
- **ADR-026** (Code Page Standard): Vite + `vite-plugin-singlefile` build. React 19 `createRoot`. Single HTML output. `sprk_smarttodo` web resource name.

### MUST Rules

- MUST use React 19 `createRoot()` entry point (Code Page, not PCF)
- MUST use Fluent v9 `FluentProvider` with theme detection (4-level cascade)
- MUST use `@spaarke/ui-components` for PanelSplitter, TodoDetail, and usePanelLayout
- MUST use Vite + `vite-plugin-singlefile` for single HTML output
- MUST use Fluent design tokens for all colors/spacing (no hard-coded values)
- MUST support light, dark, and high-contrast themes
- MUST use `makeStyles` (Griffel) for custom styling
- MUST ensure PanelSplitter has ARIA `role="separator"` with `aria-valuenow`

### MUST NOT Rules

- MUST NOT use BroadcastChannel for Kanban-to-Detail communication
- MUST NOT use `Xrm.App.sidePanes` for the detail panel in Kanban context
- MUST NOT use Fluent v8 components
- MUST NOT hard-code colors (use Fluent tokens)
- MUST NOT produce multiple output files (single HTML only)
- MUST NOT use React Router (use Xrm.Navigation for external navigation)

### Existing Patterns to Follow

- PanelSplitter implementation: `src/client/code-pages/AnalysisWorkspace/src/components/PanelSplitter.tsx`
- usePanelLayout (three-panel): `src/client/code-pages/AnalysisWorkspace/src/hooks/usePanelLayout.ts` — adapt to two-panel
- Code Page entry pattern: See ADR-006 and ADR-026 for `createRoot` + `FluentProvider` + `parseDataParams` bootstrap
- Theme detection: `resolveCodePageTheme()` from `@spaarke/ui-components/utils`
- Existing SmartToDo component: `src/solutions/SmartTodo/` (current Kanban implementation)
- Existing TodoDetailSidePane: `src/solutions/TodoDetailSidePane/` (TodoDetail component to extract)

## Success Criteria

1. [ ] Card click opens inline detail panel (no iframe, no side pane) — Verify: Click card, confirm same-origin React panel appears
2. [ ] PanelSplitter drag resizes Kanban/Detail proportions — Verify: Drag divider, confirm both panels resize smoothly
3. [ ] Save in detail panel updates single Kanban card optimistically — Verify: Edit and save, confirm card updates without full page refresh
4. [ ] BroadcastChannel completely removed from SmartToDo — Verify: Grep for `BroadcastChannel` in SmartTodo folder returns zero results
5. [ ] Side pane lifecycle management removed — Verify: No `Xrm.App.sidePanes` calls, no navigation detection code in SmartTodo
6. [ ] Panel state persists across page loads — Verify: Set panel width, navigate away and back, confirm width restored
7. [ ] Dark mode works end-to-end — Verify: Toggle theme, confirm Kanban + Detail + Splitter all respond
8. [ ] TodoDetailSidePane still works as standalone Code Page — Verify: Open `sprk_tododetailsidepane` from a non-Kanban context, confirm it loads and saves
9. [ ] Single `sprk_smarttodo` web resource deployed — Verify: `npm run build` produces one HTML file
10. [ ] Keyboard accessibility: splitter responds to ArrowLeft/ArrowRight — Verify: Tab to splitter, press arrows, confirm resize

## Dependencies

### Prerequisites (Completed)

- `ai-analysis-workspace-sprkchat-integration-r1` — PanelSplitter and usePanelLayout implementations exist in AnalysisWorkspace (need extraction to shared library)
- `spaarke-workspace-user-configuration-r1` — `useFeedTodoSync()` returns no-op stubs when no `FeedTodoSyncProvider` exists

### Reused Components

- `PanelSplitter` — Extract from AnalysisWorkspace to `@spaarke/ui-components` (generic, reusable as-is)
- `usePanelLayout` — Extract and adapt from three-panel to support configurable two-panel layout
- `TodoDetail` — Extract core detail component from TodoDetailSidePane into `@spaarke/ui-components`
- Existing `SmartToDo` component — Current Kanban board (Today/Tomorrow/Future columns, drag-drop, scoring)
- `useTodoItems` — Existing hook for Dataverse todo query
- `useKanbanColumns` — Existing hook for score-based partitioning

### External Dependencies

- None (all changes are within the Spaarke codebase)

## Data Model (No Changes)

The data model from R1 is unchanged:

- `sprk_event` — Core event record with to-do fields (flag, status, score, column, pinned)
- `sprk_eventtodo` — Optional extension record (notes, completion tracking, state lifecycle)
- `sprk_userpreference` — Kanban threshold settings (todayThreshold, tomorrowThreshold)

Reference: `docs/architecture/event-to-do-architecture.md`

## Owner Clarifications

| Topic | Question | Answer | Impact |
|-------|----------|--------|--------|
| TodoDetailSidePane retirement | Fully retire or keep as standalone? | Refactor: extract reusable TodoDetail to shared lib, keep `sprk_tododetailsidepane` as thin wrapper for non-Kanban contexts | R2 extracts component rather than deleting; side pane becomes thin Code Page wrapper |
| Prerequisite availability | Are PanelSplitter/usePanelLayout shipped to shared lib? | Completed in AnalysisWorkspace but local to that Code Page — not yet in shared library | R2 must include extraction tasks to move PanelSplitter and usePanelLayout to `@spaarke/ui-components` |
| Default panel width | What default width for detail panel? | 400px (matches current side pane, familiar to users) | Initial layout: Kanban fills remaining width, detail panel 400px, resizable from there |
| Panel persistence | Persist panel state across sessions? | Yes, via localStorage | Use localStorage (not sessionStorage) for cross-session persistence |
| Inline panel navigation behavior | Detail panel integrated into Kanban tree — no orphan on navigate away? | Correct — inline panel unmounts with the Code Page, eliminating the 6-layer navigation detection hack | No lifecycle management code needed; React unmount handles cleanup |

## Assumptions

- **usePanelLayout adaptation**: Assuming the three-panel hook can be generalized to support configurable panel counts (two-panel for SmartTodo, three-panel for AnalysisWorkspace) without breaking the existing AnalysisWorkspace integration
- **TodoDetail extraction**: Assuming the TodoDetail component in TodoDetailSidePane has minimal coupling to the side pane bootstrap code and can be cleanly extracted with props-based dependency injection
- **SmartTodo Code Page exists**: Assuming `src/solutions/SmartTodo/` already has a Vite-based Code Page structure from R1

## Unresolved Questions

- [ ] Should the two-panel usePanelLayout be a separate hook (e.g., `useTwoPanelLayout`) or a generalized version of the existing hook with configurable panel definitions? — Blocks: shared library API design task
- [ ] Does the existing SmartToDo component need a new prop to control detail panel behavior, or should TodoContext handle this entirely? — Blocks: component interface design task

---

*AI-optimized specification. Original design: design.md*
