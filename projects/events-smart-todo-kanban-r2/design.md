# Smart To Do Kanban R2 — Unified Code Page with Inline Detail Panel

> **Project**: events-smart-todo-kanban-r2
> **Status**: Design (Future)
> **Priority**: Medium
> **Prerequisite**: ai-analysis-workspace-sprkchat-integration-r1 (establishes the pattern)
> **Last Updated**: March 28, 2026

---

## Executive Summary

Merge the SmartToDo Kanban board and the TodoDetailSidePane into a single unified Code Page. The detail panel becomes an inline resizable panel within the same React tree, eliminating BroadcastChannel communication, duplicate auth, and the fixed 400px Dataverse side pane constraint. This mirrors the same architectural consolidation being done for Analysis Workspace + SprkChat.

---

## Problem Statement

The SmartToDo Kanban and TodoDetailSidePane are currently two separate surfaces communicating via BroadcastChannel:

- **SmartToDo** — renders in the LegalWorkspace Code Page (or standalone) as a three-column Kanban board
- **TodoDetailSidePane** (`sprk_tododetailsidepane`) — renders in a 400px `Xrm.App.sidePanes` iframe when a user clicks a Kanban card

They communicate via `BroadcastChannel("spaarke-todo-detail-channel")` with `TODO_SAVED` and `TODO_CLOSED` messages.

### Issues (same pattern as Analysis Workspace + SprkChat)

| Issue | Impact |
|-------|--------|
| **BroadcastChannel serialization** | All cross-pane data serialized as JSON; no shared state |
| **Duplicate auth** | Side pane independently initializes MSAL, acquires tokens, resolves Xrm context |
| **Duplicate theme detection** | Side pane runs its own 3-step theme fallback (localStorage → navbar → default) |
| **Full refetch on save** | `TODO_SAVED` triggers a complete `getActiveTodos()` refetch instead of updating a single item |
| **Fixed 400px layout** | `Xrm.App.sidePanes` locks width at 400px; not resizable by user |
| **Two deployment artifacts** | `sprk_tododetailsidepane` and workspace must be built/deployed separately |
| **Complex lifecycle** | Side pane open/close management, 6-layer navigation detection for auto-close (URL polling, beforeunload, hashchange, popstate, IntersectionObserver, mousedown) |

---

## Current Architecture

```
Dataverse Shell (UCI)
│
└── Main Content Area
    └── [LegalWorkspace or SmartTodo Code Page]  ← iframe 1
        ├── React 19 tree
        ├── MSAL auth
        ├── SmartToDo component
        │   ├── useTodoItems → Dataverse query
        │   ├── useKanbanColumns → score-based partitioning
        │   ├── KanbanBoard (Today / Tomorrow / Future)
        │   └── BroadcastChannel listener (TODO_SAVED → refetch)
        │
        └── Side Pane (Xrm.App.sidePanes, 400px)
            └── [TodoDetailSidePane]  ← iframe 2
                ├── Separate React 19 tree
                ├── Separate MSAL auth (duplicate)
                ├── Separate Xrm frame-walk
                ├── Separate theme detection
                ├── TodoDetail component
                │   ├── Description, Details, Notes, Score sliders
                │   ├── Dual-entity loading (sprk_event + sprk_eventtodo)
                │   └── Save → BroadcastChannel emit (TODO_SAVED)
                └── BroadcastChannel emitter

Communication: BroadcastChannel("spaarke-todo-detail-channel")
  Messages: TODO_SAVED { eventId }, TODO_CLOSED
```

## Target Architecture

```
Dataverse Shell (UCI)
│
└── Main Content Area (full page)
    └── [SmartTodo Code Page]  ← single iframe
        ├── React 19 tree (unified)
        ├── Single MSAL auth session
        ├── Single Xrm context
        ├── TodoContext (shared state)
        │   ├── items: IEvent[]
        │   ├── selectedEventId: string | null
        │   ├── updateItem(id, fields) → optimistic update
        │   └── refetch() → full reload
        │
        ├── KanbanBoard (left panel, resizable)
        │   ├── Today / Tomorrow / Future columns
        │   ├── Drag-and-drop
        │   └── Card click → setSelectedEventId
        │
        ├── PanelSplitter (draggable divider)
        │
        └── TodoDetailPanel (right panel, collapsible)
            ├── TodoDetail component (same as today)
            ├── Direct save callback → updateItem(id, fields)
            ├── No iframe, no BroadcastChannel
            └── Panel slides in/out on card click
```

---

## What Changes

| Concern | Current (two iframes) | Target (single tree) |
|---------|----------------------|---------------------|
| Detail rendering | Separate iframe via `Xrm.App.sidePanes` | Inline React panel, same tree |
| Communication | BroadcastChannel JSON messages | Direct React state/callbacks |
| Auth | Two MSAL sessions | One shared auth |
| Theme | Two independent detections | One FluentProvider |
| Save propagation | `TODO_SAVED` → full refetch of all items | `updateItem(id, fields)` → update single item in state |
| Panel width | Fixed 400px (Dataverse controls) | User-resizable via PanelSplitter |
| Panel lifecycle | 6-layer navigation detection for auto-close | React unmount (component lifecycle) |
| Deployment | Two web resources | One web resource |

---

## Reusable Components from Analysis Workspace Project

The `ai-analysis-workspace-sprkchat-integration-r1` project establishes:

| Component | Reuse in SmartToDo R2 |
|-----------|----------------------|
| `PanelSplitter` | Draggable divider between Kanban and Detail panel |
| `usePanelLayout` | Resizable, collapsible panel state management |
| Three-panel pattern | Adapt to two-panel: Kanban (main) + Detail (right) |
| Shared context pattern | `TodoContext` replaces `AnalysisAiContext` |

**This project should wait until the Analysis Workspace integration ships** — the patterns and components will be proven and ready to reuse.

---

## SmartToDo Component Modes (Updated)

| Mode | Props | Where Used | Card Click | Detail Panel |
|------|-------|------------|------------|--------------|
| **Workspace glance** | `embedded=true, disableSidePane=true` | Workspace section | Disabled | None |
| **Full app (R2)** | `embedded=false` | Standalone Code Page | Opens inline detail panel | Inline (same tree) |
| **Workspace full** | `embedded=true, disableSidePane=false` | Future: workspace section with detail | Opens inline detail panel | Inline (same tree) |

---

## TodoDetailSidePane Retirement

After R2 ships:

1. `sprk_tododetailsidepane` web resource is deprecated
2. All card clicks route to the inline detail panel
3. BroadcastChannel communication removed from SmartToDo
4. Side pane lifecycle management code removed (~140 lines)
5. Web resource can be removed from Dataverse solution

**Exception**: If other surfaces (e.g., EventsPage list view) need a standalone detail editor, keep `TodoDetailSidePane` as a separate Code Page for those contexts only. Evaluate at R2 implementation time.

---

## Data Model (No Changes)

The data model from R1 is unchanged:

- `sprk_event` — core event record with to-do fields (flag, status, score, column, pinned)
- `sprk_eventtodo` — optional extension record (notes, completion tracking, state lifecycle)
- `sprk_userpreference` — Kanban threshold settings (todayThreshold, tomorrowThreshold)

See `docs/architecture/event-to-do-architecture.md` for full schema and OData field selections.

---

## FeedTodoSyncContext Independence (Prerequisite from workspace-config R1)

The `spaarke-workspace-user-configuration-r1` project includes a prerequisite fix: `useFeedTodoSync()` returns no-op stubs instead of throwing when no `FeedTodoSyncProvider` exists. This fix is required before R2 because:

- SmartToDo as a standalone Code Page has no `FeedTodoSyncProvider`
- The fix is a ~10-line change in `useFeedTodoSync.ts`
- After the fix, SmartToDo works in any context: workspace, standalone, dialog

---

## Scope

### In Scope
- Inline detail panel (replaces `Xrm.App.sidePanes` for Kanban context)
- PanelSplitter reuse from Analysis Workspace project
- TodoContext for shared state between Kanban and Detail
- Optimistic single-item updates (replace full refetch)
- Remove BroadcastChannel from SmartToDo
- Remove 6-layer side pane lifecycle management

### Out of Scope
- SmartToDo as a workspace section (handled by workspace-config R1)
- Changes to the Kanban board UX (columns, scoring, drag-drop)
- Changes to TodoDetail fields or save logic
- Mobile layout

---

## Dependencies

### Prerequisites
- `ai-analysis-workspace-sprkchat-integration-r1` — PanelSplitter, usePanelLayout components
- `spaarke-workspace-user-configuration-r1` — `useFeedTodoSync` no-op fix

### Reused Components
- `PanelSplitter` from `@spaarke/ui-components`
- `usePanelLayout` from `@spaarke/ui-components`
- Existing `TodoDetail` component (move from TodoDetailSidePane into shared or inline)

---

*Last updated: March 28, 2026*
