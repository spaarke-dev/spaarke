# Event To Do — Solution Architecture

> **Version**: 2.0
> **Date**: March 30, 2026
> **Project**: Smart To Do Kanban R2 — Unified Code Page with Inline Detail Panel
> **Status**: Implementation Complete

---

## Overview

The Event To Do system provides Kanban-style task management within the Corporate Legal Workspace and as a standalone Code Page. Users can flag events as to-do items, organize them across priority-based columns, and manage detailed task information through an inline resizable detail panel.

### Key Capabilities

| Feature | Description |
|---------|-------------|
| **Three-Column Kanban** | Drag-and-drop board with Today, Tomorrow, Future columns |
| **Score-Based Auto-Assignment** | To Do Score formula auto-assigns items to columns by priority, effort, and urgency |
| **Pinning** | Lock items in a specific column, overriding score-based assignment |
| **Inline Detail Panel** | Resizable right panel for editing event fields and to-do notes (R2) |
| **Optimistic Updates** | Save in detail panel updates Kanban card immediately — no full refetch |
| **Feed ↔ Kanban Sync** | Flag events from the Updates Feed; Kanban reflects changes instantly |
| **Completion Workflow** | Yellow "Complete" → Green "Completed" with Dataverse state change |

### Architecture Evolution (R1 → R2)

| Concern | R1 (Two Iframes) | R2 (Unified Code Page) |
|---------|-------------------|------------------------|
| Detail rendering | Separate iframe via `Xrm.App.sidePanes` (400px fixed) | Inline React panel, same tree (resizable) |
| Communication | BroadcastChannel JSON messages | Direct React state/callbacks via TodoContext |
| Auth | Two MSAL sessions | One shared auth |
| Theme | Two independent detections | One FluentProvider |
| Save propagation | `TODO_SAVED` → full refetch of all items | `updateItem(id, fields)` → update single item in state |
| Panel lifecycle | 6-layer navigation detection for auto-close | React unmount (component lifecycle) |
| Deployment | Two web resources | One web resource (`sprk_smarttodo`) |

---

## Architecture Diagram

```
                    ┌─────────────────────────────────────────┐
                    │           Dataverse CRM                  │
                    │                                          │
                    │  ┌──────────────┐    ┌───────────────┐  │
                    │  │  sprk_event  │◄───│ sprk_eventtodo│  │
                    │  │  (core)      │    │ (extension)   │  │
                    │  └──────┬───────┘    └──────┬────────┘  │
                    └─────────┼───────────────────┼───────────┘
                              │                   │
                       Xrm.WebApi          Xrm.WebApi +
                              │            direct REST API
                              │                   │
    ┌─────────────────────────┼───────────────────┼──────────────────────┐
    │  SmartTodo Code Page    │                   │  (sprk_smarttodo)    │
    │  React 19 unified tree  │                   │                     │
    │                         │                   │                     │
    │  ┌──────────────────────▼──────┐  ┌────────▼─────────────────┐  │
    │  │   Kanban Board (left)       │  │  TodoDetailPanel (right) │  │
    │  │                             │  │                          │  │
    │  │  ┌────────┬────────┬─────┐  │  │  TodoDetail component    │  │
    │  │  │ Today  │Tomorrow│Futur│  │  │  (from @spaarke/         │  │
    │  │  │ ≥60   │ ≥30   │ <30 │  │  │   ui-components)         │  │
    │  │  └────────┴────────┴─────┘  │  │                          │  │
    │  └─────────────────────────────┘  └──────────────────────────┘  │
    │                    │        PanelSplitter        │               │
    │                    └───────(draggable)───────────┘               │
    │                                                                  │
    │  TodoContext (shared state: items, selectedEventId, updateItem)  │
    └──────────────────────────────────────────────────────────────────┘

    ┌──────────────────────────────────────────────────┐
    │  LegalWorkspace (Corporate Workspace)             │
    │                                                   │
    │  ┌────────────┐     ┌──────────────────────────┐ │
    │  │ Updates    │     │ SmartToDo (glance mode)  │ │
    │  │ Feed       │     │ embedded=true             │ │
    │  │ Block 3   │     │ "Open full view" button   │ │
    │  └─────┬──────┘     └───────────┬──────────────┘ │
    │        │                        │                 │
    │   FeedTodoSync           Xrm.Navigation          │
    │   Context                .navigateTo(             │
    │        │                  sprk_smarttodo)          │
    └────────┼────────────────────────┼─────────────────┘
             │                        │
             └──────── sync ──────────┘
```

---

## Data Model

### Entity Split: Why Two Entities?

The system stores data across **two Dataverse entities** — `sprk_event` and `sprk_eventtodo` — by design:

1. **Separation of concerns** — `sprk_event` is a shared entity used across multiple features (calendar, updates feed, matters, projects). To-do-specific fields (notes, completion tracking, state lifecycle) are isolated to avoid bloating the core entity.

2. **State lifecycle isolation** — `sprk_eventtodo` has its own `statecode`/`statuscode` (Active → Inactive). The to-do extension can be deactivated when completed without affecting the parent event record's state.

3. **Optional participation** — Not every event is a to-do. The `sprk_todoflag` boolean on `sprk_event` opts an event into the to-do system. The `sprk_eventtodo` extension record is only created when to-do-specific data needs to be stored.

4. **Query efficiency** — The Kanban board queries only `sprk_event` fields (no notes, no completion dates). The detail panel loads both entities in parallel. This keeps the Kanban query fast and the detail view complete.

### sprk_event — Core Event Record

The primary entity. Stores all fields used by the Kanban board and the detail panel sections (Description, Details, To Do Score).

| Field | Type | Purpose |
|-------|------|---------|
| `sprk_eventid` | GUID | Primary key |
| `sprk_eventname` | Text | Display name shown on Kanban card and detail panel header |
| `sprk_description` | Multiline text | Detailed description (editable in detail panel) |
| `sprk_duedate` | DateTime | Due date (drives urgency component of To Do Score) |
| `sprk_priorityscore` | Int (0–100) | User-set priority weight (50% of score) |
| `sprk_effortscore` | Int (0–100) | User-set effort weight (20% of score, inverted) |
| `sprk_todoflag` | Boolean | Marks event as a to-do item (opt-in) |
| `sprk_todostatus` | Choice | Open (100000000), Completed (100000001), Dismissed (100000002) |
| `sprk_todosource` | Choice | System (100000000), User (100000001), AI (100000002) |
| `sprk_todocolumn` | Choice | Today (0), Tomorrow (1), Future (2) — Kanban column |
| `sprk_todopinned` | Boolean | Locks item in its assigned column |
| `_sprk_assignedto_value` | Lookup → contact | Assigned person |
| `_sprk_eventtype_ref_value` | Lookup | Event type classification |
| `sprk_regardingrecordid` | Text (GUID) | Associated matter/project ID |
| `sprk_regardingrecordname` | Text | Associated matter/project display name |
| `_sprk_regardingrecordtype_value` | Lookup | Record type (Matter vs Project) |
| `createdon` | DateTime | Record creation timestamp |
| `modifiedon` | DateTime | Last modification timestamp |

### sprk_eventtodo — To Do Extension Record

Companion entity linked via `_sprk_regardingevent_value` lookup (relationship: `sprk_eventtodo_RegardingEvent_n1`). Optional — may not exist for every event. Stores the to-do-specific workflow data.

| Field | Type | Purpose |
|-------|------|---------|
| `sprk_eventtodoid` | GUID | Primary key |
| `_sprk_regardingevent_value` | Lookup → sprk_event | Parent event reference |
| `sprk_todonotes` | Multiline text | User notes specific to the to-do workflow |
| `sprk_completed` | Boolean | Completion flag |
| `sprk_completeddate` | DateTime | When marked complete |
| `statecode` | Int | 0 = Active, 1 = Inactive (conditional choice) |
| `statuscode` | Int | 1 = Open (under Active), 2 = Completed (under Inactive) |

### OData Field Selections

**Kanban board** queries 22 fields from `sprk_event` only:

```
sprk_eventid, sprk_eventname, _sprk_eventtype_ref_value,
sprk_description, sprk_priority, sprk_priorityscore, sprk_effortscore,
sprk_estimatedminutes, sprk_priorityreason, sprk_effortreason,
sprk_todoflag, sprk_todostatus, sprk_todosource,
sprk_regardingrecordid, sprk_regardingrecordname, _sprk_regardingrecordtype_value,
_sprk_assignedto_value, sprk_duedate, sprk_todocolumn, sprk_todopinned,
createdon, modifiedon
```

**Detail panel** queries 14 fields from `sprk_event`:

```
sprk_eventid, sprk_eventname, sprk_description, sprk_duedate,
sprk_priorityscore, sprk_effortscore, sprk_todostatus, sprk_todocolumn,
sprk_todopinned, _sprk_assignedto_value, _sprk_eventtype_ref_value,
sprk_regardingrecordid, sprk_regardingrecordname, _sprk_regardingrecordtype_value
```

**Detail panel** queries 6 fields from `sprk_eventtodo`:

```
sprk_eventtodoid, sprk_todonotes, sprk_completed,
sprk_completeddate, statecode, statuscode
```

---

## UI Surface 1: Smart To Do Code Page (`sprk_smarttodo`)

A standalone Vite single-file HTML Code Page (React 19) that provides the full Kanban board with inline detail panel. Deployed as `sprk_smarttodo` web resource.

### Two-Panel Layout

| Panel | Content | Width | Behavior |
|-------|---------|-------|----------|
| **Left (primary)** | Kanban board (Today / Tomorrow / Future columns) | Fills remaining space | Always visible |
| **PanelSplitter** | Draggable divider | 4px | ARIA role="separator", keyboard accessible |
| **Right (detail)** | TodoDetailPanel → TodoDetail component | Default 400px, resizable | Collapses when no item selected |

Panel state (width + visibility) persists to `localStorage` under key `smarttodo-panel-layout`.

### Component Architecture

```
SmartTodoApp.tsx
  └── TodoProvider (shared state context)
       ├── SmartToDo.tsx (Kanban container)
       │   ├── KanbanHeader (title, AddTodoBar, recalculate, settings)
       │   ├── KanbanBoard (three-column drag-and-drop via @hello-pangea/dnd)
       │   │   └── KanbanCard × N (score circle, name, due date, pin icon)
       │   └── DismissedSection (collapsible)
       │
       ├── PanelSplitter (from @spaarke/ui-components)
       │
       └── TodoDetailPanel.tsx (detail wrapper)
            ├── Header (event name + close button)
            ├── Loading/error states
            └── TodoDetail (from @spaarke/ui-components)
                 ├── Description textarea
                 ├── Details (Record Type, Record link, Due Date, Assigned To)
                 ├── To Do Notes textarea
                 ├── To Do Score (Priority slider, Effort slider, live score)
                 └── Sticky footer (Remove, Save, Complete)
```

### Shared State: TodoContext

TodoContext provides shared state between the Kanban board and detail panel, replacing the old BroadcastChannel communication:

```typescript
interface TodoContextValue {
  items: IEvent[];                    // Active to-do items
  dismissedItems: IEvent[];           // Dismissed items
  selectedEventId: string | null;     // Currently selected card (null = panel closed)
  selectItem(id: string | null): void; // Toggle selection (clicking same ID deselects)
  updateItem(id: string, partial): void; // Optimistic single-item merge
  handleStatusToggle(id: string): void;  // Open ↔ Completed
  handleDismiss(id: string): void;
  handleRestore(id: string): void;
  handleRemove(id: string): void;
  refetch(): void;
}
```

### Component Modes

The SmartToDo component supports multiple rendering contexts:

| Mode | Props | Where Used | Card Click | Detail Panel |
|------|-------|------------|------------|--------------|
| **Standalone (R2)** | `embedded=false` | SmartTodo Code Page | Opens inline detail panel | Inline (same tree) |
| **Workspace glance** | `embedded=true, disableSidePane=true` | LegalWorkspace section | "Open full view" button | None (compact) |

### Entry Points

| Context | How Opened | Detail |
|---------|-----------|--------|
| **From LegalWorkspace** | Card click or "Open full view" → `Xrm.Navigation.navigateTo({ pageType: "webresource", webresourceName: "sprk_smarttodo" })` | Opens as 85% dialog |
| **Direct URL** | `https://{org}.crm.dynamics.com/WebResources/sprk_smarttodo` | Full page |

---

## UI Surface 2: Todo Detail Side Pane (Standalone)

The `TodoDetailSidePane` (`sprk_tododetailsidepane`) remains available as a standalone Code Page for non-Kanban contexts (e.g., EventsPage list view). In R2, it was refactored to consume the shared `TodoDetail` component from `@spaarke/ui-components` instead of maintaining a local copy.

### R2 Changes to TodoDetailSidePane

| Aspect | Before (R1) | After (R2) |
|--------|-------------|------------|
| TodoDetail component | Local copy (1032 lines) | Imported from `@spaarke/ui-components` |
| Type definitions | Local `TodoRecord.ts` | Imported from `@spaarke/ui-components` |
| BroadcastChannel | Still present | Still present (needed for standalone operation) |
| Xrm access | Direct internal calls | Wrapped as callback props (`onSearchContacts`, `onOpenRegardingRecord`) |

---

## UI Surface 3: LegalWorkspace Integration

The LegalWorkspace renders SmartToDo in **glance mode** as Block 4 of the Activity section. In R2, the BroadcastChannel and side pane lifecycle management were removed (~150 lines).

| Feature | Before (R1) | After (R2) |
|---------|-------------|------------|
| Card click | Opens `Xrm.App.sidePanes` iframe | Opens SmartTodo Code Page via `navigateTo` |
| Detail editing | TodoDetailSidePane in 400px fixed iframe | Full SmartTodo Code Page with inline panel |
| BroadcastChannel | Listener for `TODO_SAVED` → full refetch | Removed |
| Navigation detection | 6-layer auto-close hack (URL polling, hashchange, popstate, IntersectionObserver, mousedown, beforeunload) | Removed (React unmount handles cleanup) |

---

## To Do Score Formula

```
Score = Priority (50%) + Inverted Effort (20%) + Due Date Urgency (30%)
```

| Component | Weight | Source | Notes |
|-----------|--------|--------|-------|
| Priority | 50% | `sprk_priorityscore × 0.50` | User-set 0–100 |
| Effort | 20% | `(100 − sprk_effortscore) × 0.20` | Inverted — low effort scores higher (quick wins) |
| Urgency | 30% | Computed from `sprk_duedate` | Auto-calculated, not user-editable |

**Urgency mapping:**

| Due Date Condition | Urgency Value |
|-------------------|---------------|
| Overdue (past due) | 100 |
| ≤ 3 days | 80 |
| ≤ 7 days | 50 |
| ≤ 10 days | 25 |
| > 10 days or no due date | 0 |

Final score: `Math.max(0, Math.min(100, Math.round(raw)))`

Both the Kanban board (`todoScoreUtils.ts`) and the detail panel (`TodoDetail.tsx`) compute this formula identically, using `Math.ceil` for day differences and `Math.round` for the final score.

### Column Assignment

Items are auto-assigned to columns based on user-configurable thresholds stored in `sprk_userpreference`:

| Column | Default Rule | Description |
|--------|-------------|-------------|
| **Today** | Score ≥ 60 | Highest-priority items |
| **Tomorrow** | Score ≥ 30 and < 60 | Medium-priority items |
| **Future** | Score < 30 | Low-priority or distant items |

Thresholds are user-configurable via the Kanban settings gear icon. Stored in `sprk_userpreference` as JSON: `{ todayThreshold: 60, tomorrowThreshold: 30 }`.

### Pinning

Pinned items (`sprk_todopinned = true`) stay in their assigned column (`sprk_todocolumn`) regardless of score changes. When a user drags a card to a different column, the item is auto-pinned. Users can toggle the pin to allow score-based reassignment.

### Drag-and-Drop

| Action | Dataverse Update | Behavior |
|--------|-----------------|----------|
| Drag card to different column | `sprk_todocolumn` + `sprk_todopinned = true` | Optimistic UI update; rollback on failure |
| Reorder within column | None (UI-only) | Local ordering, no persistence |
| Toggle pin | `sprk_todopinned` | Unpinned → recalculates column from score |

---

## Cross-Component Communication

### TodoContext: Kanban ↔ Detail Panel (R2)

In the unified SmartTodo Code Page, the Kanban board and detail panel share state via `TodoContext` (React Context):

```
Card click
  → selectItem(eventId)
  → TodoContext updates selectedEventId
  → SmartTodoApp useEffect triggers showDetail()
  → TodoDetailPanel loads entity data
  → User edits and saves
  → TodoDetailPanel calls updateItem(eventId, fields) — optimistic merge
  → Kanban card re-renders immediately (single React render cycle)
  → Dataverse write in background
  → If failure: rollback to previous state
```

No BroadcastChannel, no JSON serialization, no iframe boundary. Direct React state/callbacks.

### FeedTodoSyncContext: Updates Feed ↔ Kanban

The Updates Feed (Block 3) and Smart To Do Kanban (Block 4) share state via `FeedTodoSyncContext` (React Context + useReducer) when both are rendered in the LegalWorkspace:

```
State shape:
  flags: Map<eventId, boolean>     — current to-do flag state (optimistic)
  pendingWrites: Set<eventId>       — writes in flight
  errors: Map<eventId, string>      — failed toggles
```

**Data flow when user flags an event in the Updates Feed:**

```
1. User clicks flag button on event card
   → toggleFlag(eventId) dispatches TOGGLE_FLAG (optimistic update)
   → Subscribers notified immediately

2. Kanban board (subscriber) reacts:
   → flagged = true:  fetch full event, insert into list, re-sort by score
   → flagged = false: remove event from list

3. Debounce 300ms → Dataverse write:
   → updateRecord('sprk_event', id, { sprk_todoflag: true/false })
   → On success: WRITE_SUCCESS (clear pending)
   → On failure: WRITE_FAILURE (rollback flag, surface error)
```

---

## Complete Data Flows

### Flow 1: App Startup → Kanban Populated

```
1. SmartToDo mounts → useTodoItems hook fires
2. DataverseService.getActiveTodos(userId) → OData query
3. Returns IEvent[] (22 fields per record)
4. useUserPreferences → fetches thresholds from sprk_userpreference
5. useKanbanColumns → partitions items into Today/Tomorrow/Future
6. KanbanBoard renders three columns with KanbanCard components
```

### Flow 2: Click Kanban Card → Detail Panel Opens (R2)

```
1. KanbanCard onClick → selectItem(eventId) via TodoContext
2. selectedEventId changes → SmartTodoApp useEffect → showDetail()
3. PanelSplitter + TodoDetailPanel become visible (150ms CSS transition)
4. TodoDetailPanel loads both entities in parallel:
   - loadTodoRecord(eventId) → GET sprk_event
   - loadTodoExtension(eventId) → GET sprk_eventtodos?$filter=_sprk_regardingevent_value eq {id}
5. TodoDetail renders with both records
6. Score computed live from current slider values
7. KanbanCard shows selected highlight (tokens.colorNeutralBackground1Selected)
```

### Flow 3: Edit and Save in Detail Panel (R2)

```
1. User modifies fields (description, due date, notes, etc.)
2. Dirty detection tracks sprk_event fields and sprk_eventtodo fields separately
3. User clicks Save:
   a. Capture previous item state for rollback
   b. updateItem(eventId, fields) → optimistic Kanban card update (immediate)
   c. If event fields dirty → saveTodoFields(eventId, updates) to Dataverse
   d. If notes dirty → saveTodoExtensionFields(todoId, { sprk_todonotes }) to Dataverse
   e. If Dataverse save fails → rollback to previous state
4. Kanban card reflects changes within one React render cycle (no full refetch)
```

### Flow 4: Mark as Complete

```
1. User clicks yellow "Complete" button
2. handleCompleted() executes:
   a. Save any dirty event fields (sprk_event)
   b. Save completion data (sprk_eventtodo):
      - sprk_completed = true
      - sprk_completeddate = now
      - sprk_todonotes (if dirty)
   c. Deactivate via direct REST API:
      - PATCH sprk_eventtodos({id})
      - { statecode: 1, statuscode: 2 }
3. On success:
   - Optimistic update → Kanban card shows Completed status
   - Button switches to green "Completed" (disabled)
4. Detail panel stays open showing completed state
```

### Flow 5: Flag Event in Updates Feed

```
1. User clicks flag button on event card in Updates Feed
2. FeedTodoSyncContext.toggleFlag(eventId):
   a. Optimistic update (Map updated immediately)
   b. Kanban subscriber reacts instantly:
      - flagged=true → fetch event, insert into board, sort by score
      - flagged=false → remove from board
   c. Debounce 300ms → Dataverse write:
      - sprk_todoflag = true/false
      - sprk_todosource = User
3. On failure → rollback optimistic update, surface error
```

### Flow 6: Drag Card Between Columns

```
1. User drags card from Tomorrow → Today
2. moveItem(eventId, "Today"):
   a. Optimistic UI update (card moves immediately)
   b. Auto-pin: sprk_todopinned = true, sprk_todocolumn = 0 (Today)
   c. Async Dataverse writes:
      - updateEventColumn(eventId, 0)
      - updateEventPinned(eventId, true)
   d. On failure → rollback to original column
3. Pinned item stays in Today regardless of future score changes
```

---

## File Map

### SmartTodo Code Page (R2 — Primary Surface)

```
src/solutions/SmartTodo/
├── package.json                      React 19, Vite + singlefile, @spaarke/ui-components
├── vite.config.ts                    Aliases for shared lib deep imports
├── tsconfig.json                     Strict, bundler resolution
├── index.html                        CSS reset for Dataverse iframe
├── src/
│   ├── main.tsx                      Entry point (createRoot, React.StrictMode)
│   ├── App.tsx                       FluentProvider + resolveCodePageTheme
│   ├── SmartTodoApp.tsx              Two-panel layout (Kanban + PanelSplitter + Detail)
│   ├── context/
│   │   └── TodoContext.tsx           Shared state (items, selection, optimistic updates)
│   ├── components/
│   │   ├── SmartToDo.tsx             Kanban container (header, board, dismissed)
│   │   ├── KanbanCard.tsx            Card with score circle, selected highlight
│   │   ├── KanbanHeader.tsx          Title, AddTodoBar, settings
│   │   ├── DismissedSection.tsx      Collapsible dismissed items
│   │   ├── TodoDetailPanel.tsx       Detail wrapper (entity loading, save callbacks)
│   │   ├── ThresholdSettings.tsx     Column threshold config
│   │   └── shared/KanbanBoard.tsx    Drag-and-drop board
│   ├── hooks/
│   │   ├── useTodoItems.ts           Data loading
│   │   ├── useKanbanColumns.ts       Column assignment, drag-drop, pin toggle
│   │   └── useUserPreferences.ts     Threshold settings
│   ├── services/
│   │   ├── DataverseService.ts       Kanban CRUD operations
│   │   ├── todoDetailService.ts      Detail panel entity load/save
│   │   └── xrmProvider.ts            Xrm frame-walk for Code Page context
│   ├── types/
│   │   ├── entities.ts               IEvent interface
│   │   └── enums.ts                  TodoColumn, status constants
│   └── utils/
│       ├── todoScoreUtils.ts         Score formula
│       └── xrmAccess.ts              getClientUrl, setRecordState
└── dist/
    └── smarttodo.html                Deployable single-file HTML (sprk_smarttodo)
```

### Shared Library Components (extracted in R2)

```
src/client/shared/Spaarke.UI.Components/src/
├── components/
│   ├── PanelSplitter/                Draggable, keyboard-accessible panel divider
│   │   ├── PanelSplitter.tsx         ARIA role="separator", Fluent v9 tokens
│   │   └── index.ts
│   └── TodoDetail/                   Context-agnostic to-do detail editor
│       ├── TodoDetail.tsx            Description, details, notes, score, footer
│       ├── types.ts                  ITodoRecord, ITodoExtension, field update types
│       └── index.ts
└── hooks/
    └── useTwoPanelLayout.ts          Two-panel resize hook (localStorage persistence)
```

### TodoDetailSidePane (Standalone — Refactored in R2)

```
src/solutions/TodoDetailSidePane/src/
├── main.tsx                           Entry point (createRoot)
├── App.tsx                            Root — imports TodoDetail from @spaarke/ui-components
├── services/
│   └── todoService.ts                Dataverse I/O (types from @spaarke/ui-components)
└── utils/
    ├── broadcastChannel.ts           BroadcastChannel messaging (standalone contexts)
    ├── parseParams.ts                URL parameter extraction (eventId)
    └── xrmAccess.ts                  Xrm context access, setRecordState
```

### LegalWorkspace Kanban (Glance Mode)

```
src/solutions/LegalWorkspace/src/components/SmartToDo/
├── SmartToDo.tsx                     Kanban container (glance mode, no BroadcastChannel)
├── KanbanBoard.tsx                   Three-column layout
├── KanbanCard.tsx                    Card component
├── KanbanHeader.tsx                  Title, "Open full view" button
└── DismissedSection.tsx              Dismissed items
```

---

## Technical Decisions

### Why an Inline Panel (Not a Side Pane)? (R2)

The `Xrm.App.sidePanes` approach (R1) required two separate iframe applications communicating via BroadcastChannel, with duplicate auth, duplicate theme detection, and a complex 6-layer navigation detection hack. The inline panel (R2) shares the same React tree, enabling direct state/callbacks, one auth session, one theme provider, and standard React unmount lifecycle.

### Why a Separate Code Page (Not Embedded in LegalWorkspace)?

The SmartTodo Code Page is independently deployable and reusable from multiple contexts. The LegalWorkspace shows a compact glance view and opens the full Code Page as a dialog when the user needs the detail panel.

### Why Direct REST API for State Changes?

`Xrm.WebApi.updateRecord` silently ignores `statecode`/`statuscode` fields in some Dataverse environments. The promise resolves successfully, but the record's state does not change. Using `fetch` against the Web API endpoint (`/api/data/v9.2/sprk_eventtodos({id})`) with explicit OData headers reliably persists state changes.

### Why Compute Score on Both Sides?

The Kanban board computes the score for column assignment; the detail panel computes it for the live score preview. Both implementations use the identical formula to avoid drift. The detail panel score updates in real time as the user adjusts the Priority and Effort sliders.

### Why Optimistic Updates Everywhere?

Every mutation (save, drag, flag toggle, pin toggle) updates local state immediately and persists asynchronously. Failed writes trigger rollback. This keeps the UI responsive — the user never waits for a network round-trip to see their action reflected.

### Why Extract TodoDetail to Shared Library?

The TodoDetail component is consumed by both the SmartTodo Code Page (inline panel) and the standalone TodoDetailSidePane. Extracting to `@spaarke/ui-components` eliminates duplication. The component accepts callback props (`onSearchContacts`, `onOpenRegardingRecord`, `onSaveEventFields`, etc.) to stay context-agnostic per ADR-012.

---

## Dataverse Entity Relationships

```
┌──────────────────┐              ┌──────────────────────┐
│   sprk_event     │ 1 ←── 0..1  │   sprk_eventtodo     │
│                  │              │                      │
│  sprk_eventid    │              │  sprk_eventtodoid    │
│  sprk_eventname  │              │  _sprk_regarding     │
│  sprk_description│              │   event_value ───────┤──→ sprk_event
│  sprk_duedate    │              │  sprk_todonotes      │
│  sprk_priority   │              │  sprk_completed      │
│  sprk_effortscore│              │  sprk_completeddate  │
│  sprk_todoflag   │              │  statecode           │
│  sprk_todostatus │              │  statuscode          │
│  sprk_todocolumn │              └──────────────────────┘
│  sprk_todopinned │
│  _sprk_assigned  │──→ contact
│  _sprk_eventtype │──→ sprk_eventtype
│  _sprk_regarding │──→ sprk_matter / sprk_project
│  sprk_todosource │
└──────────────────┘

Relationship: sprk_eventtodo_RegardingEvent_n1
  sprk_eventtodo._sprk_regardingevent_value → sprk_event.sprk_eventid

┌──────────────────────┐
│ sprk_userpreference  │
│                      │
│ sprk_preferencetype  │  = 100000000 (TodoKanbanThresholds)
│ sprk_preferencevalue │  = JSON: { todayThreshold, tomorrowThreshold }
└──────────────────────┘
```

---

## Deployment

| Artifact | Web Resource | Build | Deploy Script |
|----------|-------------|-------|---------------|
| SmartTodo Code Page | `sprk_smarttodo` | `cd src/solutions/SmartTodo && npm run build` → `dist/smarttodo.html` | `scripts/Deploy-SmartTodo.ps1` |
| TodoDetailSidePane | `sprk_tododetailsidepane` | `cd src/solutions/TodoDetailSidePane && npm run build` → `dist/tododetailsidepane.html` | Manual upload or deploy script |

---

## Theme Resolution

All SmartTodo surfaces use the shared theme detection from `@spaarke/ui-components`:

1. **localStorage** — Check `spaarke-theme` key (shared across all Spaarke web resources)
2. **URL parameter** — `?theme=dark` override
3. **Navbar detection** — Inspect Power Apps navbar background color (works when same-origin)
4. **System preference** — `prefers-color-scheme` media query

All UI uses Fluent UI v9 semantic tokens per ADR-021, ensuring correct appearance in light, dark, and high-contrast modes. A reactive listener (`setupCodePageThemeListener`) updates the theme dynamically on cross-tab changes.
