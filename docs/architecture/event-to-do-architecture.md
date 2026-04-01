# Event To Do — Solution Architecture

> **Version**: 2.0
> **Date**: March 30, 2026
> **Project**: Smart To Do Kanban R2 — Unified Code Page with Inline Detail Panel
> **Status**: Implementation Complete

---

## Overview

The Event To Do system provides Kanban-style task management within the Corporate Legal Workspace and as a standalone Code Page. Users can flag events as to-do items, organize them across priority-based columns, and manage task detail through an inline resizable panel.

---

## Architecture Evolution (R1 → R2)

The central architectural decision in R2 was to replace the dual-iframe approach (Kanban + side pane via `Xrm.App.sidePanes`) with a single unified React Code Page containing an inline resizable detail panel.

| Concern | R1 (Two Iframes) | R2 (Unified Code Page) |
|---------|-------------------|------------------------|
| Detail rendering | Separate iframe via `Xrm.App.sidePanes` (400px fixed) | Inline React panel, same tree (resizable) |
| Communication | BroadcastChannel JSON messages | Direct React state/callbacks via TodoContext |
| Auth | Two MSAL sessions | One shared auth |
| Theme | Two independent detections | One FluentProvider |
| Save propagation | `TODO_SAVED` → full refetch of all items | `updateItem(id, fields)` → update single item in state |
| Panel lifecycle | 6-layer navigation detection hack | React unmount (component lifecycle) |
| Deployment | Two web resources | One web resource (`sprk_smarttodo`) |

---

## Key Design Decisions

### Why an Inline Panel (Not a Side Pane)? (R2)

The `Xrm.App.sidePanes` approach (R1) required two separate iframe applications communicating via BroadcastChannel, with duplicate auth, duplicate theme detection, and a complex 6-layer navigation detection hack to handle auto-close (URL polling, hashchange, popstate, IntersectionObserver, mousedown, beforeunload). The inline panel (R2) shares the same React tree, enabling direct state/callbacks, one auth session, one theme provider, and standard React unmount lifecycle.

### Why a Separate Code Page (Not Embedded in LegalWorkspace)?

The SmartTodo Code Page is independently deployable and reusable from multiple contexts. The LegalWorkspace shows a compact glance view and opens the full Code Page as a dialog when the user needs the detail panel.

### Why Direct REST API for State Changes?

`Xrm.WebApi.updateRecord` silently ignores `statecode`/`statuscode` fields in some Dataverse environments — the promise resolves successfully but the record's state does not change. Using `fetch` against the Web API endpoint (`/api/data/v9.2/sprk_eventtodos({id})`) with explicit OData headers reliably persists state changes.

### Why Compute Score on Both Sides?

The Kanban board computes the score for column assignment; the detail panel computes it for the live score preview. Both use the identical formula to avoid drift. The formula is: Priority (50%) + Inverted Effort (20%) + Due Date Urgency (30%).

### Why Optimistic Updates Everywhere?

Every mutation (save, drag, flag toggle, pin toggle) updates local state immediately and persists asynchronously. Failed writes trigger rollback. This keeps the UI responsive — the user never waits for a network round-trip.

### Why Extract TodoDetail to Shared Library?

The TodoDetail component is consumed by both the SmartTodo Code Page (inline panel) and the standalone TodoDetailSidePane. Extracting to `@spaarke/ui-components` eliminates duplication. The component accepts callback props (`onSearchContacts`, `onOpenRegardingRecord`, `onSaveEventFields`, etc.) to stay context-agnostic per ADR-012.

### Why Two Entities (sprk_event + sprk_eventtodo)?

1. **Separation of concerns** — `sprk_event` is a shared entity used across calendar, updates feed, matters, and projects. To-do-specific fields are isolated to avoid bloating the core entity.
2. **State lifecycle isolation** — `sprk_eventtodo` has its own `statecode`/`statuscode`. The to-do extension can be deactivated when completed without affecting the parent event.
3. **Optional participation** — Not every event is a to-do. The extension record is only created when to-do-specific data needs to be stored.
4. **Query efficiency** — The Kanban board queries only `sprk_event` fields (no notes, no completion dates). The detail panel loads both entities in parallel.

---

## Architecture Diagram

```
                    ┌─────────────────────────────────────────┐
                    │           Dataverse CRM                  │
                    │  ┌──────────────┐    ┌───────────────┐  │
                    │  │  sprk_event  │◄───│ sprk_eventtodo│  │
                    │  │  (core)      │    │ (extension)   │  │
                    │  └──────┬───────┘    └──────┬────────┘  │
                    └─────────┼───────────────────┼───────────┘
    ┌─────────────────────────┼───────────────────┼──────────────────────┐
    │  SmartTodo Code Page    │                   │  (sprk_smarttodo)    │
    │  React 19 unified tree                                             │
    │  ┌──────────────────────────────┐  ┌────────────────────────────┐  │
    │  │   Kanban Board (left)        │  │  TodoDetailPanel (right)   │  │
    │  │  Today / Tomorrow / Future   │  │  TodoDetail (@spaarke/     │  │
    │  │  Score-based auto-assignment │  │   ui-components)           │  │
    │  └──────────────────────────────┘  └────────────────────────────┘  │
    │                    │        PanelSplitter (draggable)    │          │
    │  TodoContext (shared state: items, selectedEventId, updateItem)     │
    └─────────────────────────────────────────────────────────────────────┘

    ┌──────────────────────────────────────────────────┐
    │  LegalWorkspace (Corporate Workspace)             │
    │  ┌────────────┐     ┌──────────────────────────┐ │
    │  │ Updates    │     │ SmartToDo (glance mode)  │ │
    │  │ Feed       │     │ embedded=true             │ │
    │  └─────┬──────┘     └───────────┬──────────────┘ │
    │   FeedTodoSyncContext    Xrm.Navigation.navigateTo │
    └────────────────────────────────────────────────────┘
```

---

## To Do Score Formula

```
Score = Priority (50%) + Inverted Effort (20%) + Due Date Urgency (30%)
```

- **Priority**: `sprk_priorityscore × 0.50` (user-set 0–100)
- **Effort**: `(100 − sprk_effortscore) × 0.20` (inverted — low effort scores higher)
- **Urgency**: computed from `sprk_duedate` — overdue=100, ≤3d=80, ≤7d=50, ≤10d=25, >10d or none=0

**Column thresholds** (user-configurable, stored in `sprk_userpreference`):
- Today: score ≥ 60
- Tomorrow: score ≥ 30 and < 60
- Future: score < 30

**Pinning**: Items with `sprk_todopinned = true` stay in their assigned column regardless of score. Dragging a card auto-pins it.

---

## Cross-Component Communication

### TodoContext (Kanban ↔ Detail Panel)

In the unified SmartTodo Code Page, the Kanban board and detail panel share state via React Context — no BroadcastChannel, no JSON serialization, no iframe boundary. Card click → `selectItem(eventId)` → detail panel loads. Save → `updateItem(eventId, fields)` → Kanban card re-renders immediately.

### FeedTodoSyncContext (Updates Feed ↔ Kanban)

When both rendered in LegalWorkspace, flagging an event in the Updates Feed uses optimistic updates (Map updated immediately). Kanban subscribes and reacts instantly. Debounced Dataverse write with rollback on failure.

---

## UI Surfaces

| Surface | Web Resource | Notes |
|---------|-------------|-------|
| SmartTodo Code Page (primary) | `sprk_smarttodo` | Full Kanban + inline detail panel. React 19, Vite single-file |
| TodoDetailSidePane (standalone) | `sprk_tododetailsidepane` | For non-Kanban contexts (e.g., EventsPage). Still uses BroadcastChannel for standalone operation |
| LegalWorkspace glance | N/A (embedded) | `embedded=true, disableSidePane=true`. "Open full view" → `navigateTo sprk_smarttodo` |

---

## Theme Resolution

All SmartTodo surfaces use the shared `resolveCodePageTheme()` cascade from `@spaarke/ui-components`:
1. localStorage (`spaarke-theme`)
2. URL parameter (`?theme=dark`)
3. Navbar DOM detection
4. System preference

All UI uses Fluent UI v9 semantic tokens per ADR-021.

---

## Deployment

| Artifact | Build | Deploy Script |
|----------|-------|---------------|
| SmartTodo Code Page | `cd src/solutions/SmartTodo && npm run build` → `dist/smarttodo.html` | `scripts/Deploy-SmartTodo.ps1` |
| TodoDetailSidePane | `cd src/solutions/TodoDetailSidePane && npm run build` | Manual upload or deploy script |
