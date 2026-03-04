# Event To Do — Solution Architecture

> **Version**: 1.0
> **Date**: March 1, 2026
> **Project**: Smart To Do Kanban & Todo Detail Side Pane
> **Status**: Implementation Complete

---

## Overview

The Event To Do system provides Kanban-style task management within the Corporate Legal Workspace. Users can flag events as to-do items, organize them across priority-based columns, and manage detailed task information through a side pane editor.

### Key Capabilities

| Feature | Description |
|---------|-------------|
| **Three-Column Kanban** | Drag-and-drop board with Today, Tomorrow, Future columns |
| **Score-Based Auto-Assignment** | To Do Score formula auto-assigns items to columns by priority, effort, and urgency |
| **Pinning** | Lock items in a specific column, overriding score-based assignment |
| **Detail Side Pane** | Full read/write editor for event fields and to-do notes |
| **Feed ↔ Kanban Sync** | Flag events from the Updates Feed; Kanban reflects changes instantly |
| **BroadcastChannel Sync** | Side pane edits propagate to Kanban board in real time |
| **Completion Workflow** | Yellow "Complete" → Green "Completed" with Dataverse state change |

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
         ┌────────────────────┼───────────────────┼─────────────────┐
         │                    │                   │                 │
    ┌────▼──────┐      ┌──────▼───────────────────▼──────┐         │
    │ Updates   │      │   TodoDetailSidePane (iframe)   │         │
    │ Feed      │      │                                  │         │
    │ Block 3   │      │  App.tsx → TodoDetail.tsx        │         │
    └────┬──────┘      │  todoService.ts                  │         │
         │             └──────────────┬───────────────────┘         │
    FeedTodoSync               BroadcastChannel                    │
    Context                    "TODO_SAVED"                        │
         │                            │                            │
    ┌────▼────────────────────────────▼──────┐                     │
    │       Smart To Do Kanban Board          │                     │
    │       Block 4                           │                     │
    │                                         │                     │
    │  ┌──────────┬────────────┬──────────┐  │                     │
    │  │  Today   │  Tomorrow  │  Future  │  │                     │
    │  │ score≥60 │  score≥30  │ score<30 │  │                     │
    │  └──────────┴────────────┴──────────┘  │                     │
    └─────────────────────────────────────────┘                     │
```

---

## Data Model

### Entity Split: Why Two Entities?

The system stores data across **two Dataverse entities** — `sprk_event` and `sprk_eventtodo` — by design:

1. **Separation of concerns** — `sprk_event` is a shared entity used across multiple features (calendar, updates feed, matters, projects). To-do-specific fields (notes, completion tracking, state lifecycle) are isolated to avoid bloating the core entity.

2. **State lifecycle isolation** — `sprk_eventtodo` has its own `statecode`/`statuscode` (Active → Inactive). The to-do extension can be deactivated when completed without affecting the parent event record's state.

3. **Optional participation** — Not every event is a to-do. The `sprk_todoflag` boolean on `sprk_event` opts an event into the to-do system. The `sprk_eventtodo` extension record is only created when to-do-specific data needs to be stored.

4. **Query efficiency** — The Kanban board queries only `sprk_event` fields (no notes, no completion dates). The side pane loads both entities in parallel. This keeps the Kanban query fast and the detail view complete.

### sprk_event — Core Event Record

The primary entity. Stores all fields used by the Kanban board and the side pane detail sections (Description, Details, To Do Score).

| Field | Type | Purpose |
|-------|------|---------|
| `sprk_eventid` | GUID | Primary key |
| `sprk_eventname` | Text | Display name shown on Kanban card and side pane header |
| `sprk_description` | Multiline text | Detailed description (editable in side pane) |
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

Companion entity linked via `_sprk_event_value` lookup. Optional — may not exist for every event. Stores the to-do-specific workflow data.

| Field | Type | Purpose |
|-------|------|---------|
| `sprk_eventtodoid` | GUID | Primary key |
| `_sprk_event_value` | Lookup → sprk_event | Parent event reference |
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

**Side pane** queries 14 fields from `sprk_event`:

```
sprk_eventid, sprk_eventname, sprk_description, sprk_duedate,
sprk_priorityscore, sprk_effortscore, sprk_todostatus, sprk_todocolumn,
sprk_todopinned, _sprk_assignedto_value, _sprk_eventtype_ref_value,
sprk_regardingrecordid, sprk_regardingrecordname, _sprk_regardingrecordtype_value
```

**Side pane** queries 6 fields from `sprk_eventtodo`:

```
sprk_eventtodoid, sprk_todonotes, sprk_completed,
sprk_completeddate, statecode, statuscode
```

---

## UI Surface 1: Smart To Do Kanban Board

Lives in the LegalWorkspace as **Block 4** of the Activity section. Three-column drag-and-drop board that auto-assigns items based on a computed score.

### To Do Score Formula

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

Both the Kanban board (`todoScoreUtils.ts`) and the side pane (`TodoDetail.tsx`) compute this formula identically, using `Math.ceil` for day differences and `Math.round` for the final score.

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

### Data Loading

```
Component Mount
  → useTodoItems hook
    → DataverseService.getActiveTodos(userId)
      → OData: GET sprk_events
          ?$filter=_ownerid_value eq {userId}
                   AND sprk_todoflag eq true
                   AND sprk_todostatus ne 100000002
          &$select={22 fields}
          &$orderby=sprk_priorityscore desc, sprk_duedate asc
    → Returns IEvent[] array

  → useUserPreferences hook
    → Fetches sprk_userpreference (preferenceType = 100000000)
    → Parses JSON thresholds

  → useKanbanColumns hook
    → Partitions items into Today/Tomorrow/Future arrays
    → Respects pinned items
```

---

## UI Surface 2: Todo Detail Side Pane

A standalone Vite single-file HTML application (`tododetailsidepane.html`) loaded inside an `Xrm.App.sidePanes` iframe. Opens when a user clicks a Kanban card.

### Component Tree

```
main.tsx
  └── App.tsx (root)
       ├── Resolves theme (localStorage → navbar detection → light default)
       ├── Parses eventId from URL params
       ├── Loads sprk_event and sprk_eventtodo in parallel
       ├── Manages save/deactivate/remove handlers
       └── Renders TodoDetail.tsx
            ├── Description textarea (auto-expanding, 8-line default)
            ├── Details section
            │   ├── Record Type badge
            │   ├── Record link (opens in new tab via Xrm.Navigation)
            │   ├── Due Date picker
            │   └── Assigned To combobox (searches contact table)
            ├── To Do Notes textarea (auto-expanding, 8-line default)
            ├── To Do Score section
            │   ├── "To Do Score" title + info popover + score circle
            │   ├── Priority slider (0–100)
            │   └── Effort slider (0–100)
            └── Sticky footer
                ├── Remove button (red, left-aligned)
                ├── Save button (primary blue)
                └── Complete / Completed button (yellow → green)
```

### Dual-Entity Loading

The side pane loads both entities in parallel on mount:

```typescript
Promise.all([
  loadTodoRecord(eventId),        // GET sprk_event(id)?$select=14 fields
  loadTodoExtension(eventId),     // GET sprk_eventtodos?$filter=_sprk_event_value eq {id}&$top=1
])
```

The extension is optional. If no `sprk_eventtodo` record exists, the side pane still displays all `sprk_event` fields — the To Do Notes section and Complete button are just unavailable.

### Save Routing

The side pane tracks dirty state separately for each entity and routes saves to the correct target:

| Changed Field(s) | Saved To | API Method |
|-------------------|----------|------------|
| Description, Due Date, Priority, Effort, Assigned To | `sprk_event` | `Xrm.WebApi.updateRecord` |
| To Do Notes | `sprk_eventtodo` | `Xrm.WebApi.updateRecord` |
| Complete action (data fields) | `sprk_eventtodo` | `Xrm.WebApi.updateRecord` |
| Complete action (state change) | `sprk_eventtodo` | Direct REST API `fetch` |

### Assigned To Lookup

The Assigned To field uses a `Combobox` with debounced search (300ms) against the standard Dataverse `contact` table:

```
OData: GET contacts
  ?$select=contactid,fullname
  &$filter=contains(fullname,'{query}')
  &$top=10
  &$orderby=fullname asc
```

On save, the selected contact is written as an OData entity bind:

```json
{ "sprk_AssignedTo@odata.bind": "/contacts({contactId})" }
```

### Completion Workflow

The Complete button has two visual states:

| `sprk_eventtodo` State | Button |
|------------------------|--------|
| Active / Open (`statecode = 0`) | **Yellow** "Complete" button (clickable) |
| Inactive / Completed (`statecode = 1`) | **Green** "Completed" button (disabled) |

When clicked, the completion is a **two-step process**:

```
Step 1: Save data fields (Xrm.WebApi.updateRecord)
  → sprk_completed = true
  → sprk_completeddate = {current ISO timestamp}
  → sprk_todonotes = {if dirty}

Step 2: Deactivate record (direct REST API fetch)
  → PATCH /api/data/v9.2/sprk_eventtodos({id})
  → Body: { "statecode": 1, "statuscode": 2 }
```

**Why two steps?** `Xrm.WebApi.updateRecord` silently ignores `statecode`/`statuscode` fields in some Dataverse environments — the promise resolves successfully but the record state does not change. The direct REST API `fetch` call bypasses this limitation and reliably persists the state change.

**Why not one call?** Data fields and state changes must be in separate PATCH requests because Dataverse may reject a PATCH that mixes data field updates with `statecode`/`statuscode` changes, depending on the entity configuration.

---

## Cross-Component Communication

### BroadcastChannel: Side Pane ↔ Kanban

The side pane runs in an iframe, separate from the Kanban board. They communicate via `BroadcastChannel("spaarke-todo-detail-channel")`:

| Event | Sender | Receiver | Trigger |
|-------|--------|----------|---------|
| `TODO_SAVED` | Side pane | Kanban board | After any successful save |
| `TODO_CLOSED` | Side pane | Kanban board | When side pane closes |

```
Side pane saves fields
  → sendTodoSaved(eventId)
  → BroadcastChannel emits TODO_SAVED { eventId }
  → Kanban board receives message
  → Refetches the specific event record
  → Updates card display (score recalculated, column may change)
```

This provides real-time sync — adjusting Priority in the side pane and clicking Save immediately updates the Kanban card's score and may move it to a different column.

### FeedTodoSyncContext: Updates Feed ↔ Kanban

The Updates Feed (Block 3) and Smart To Do Kanban (Block 4) share state via `FeedTodoSyncContext` (React Context + useReducer):

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

The 300ms debounce prevents write spam on rapid toggles while keeping perceived latency under 1 second.

---

## Complete Data Flows

### Flow 1: App Startup → Kanban Populated

```
1. SmartToDo mounts → useTodoItems hook fires
2. DataverseService.getActiveTodos(userId) → OData query
3. Returns IEvent[] (22 fields per record)
4. initFlags(events) → FeedTodoSyncContext bulk-initializes flag Map
5. useUserPreferences → fetches thresholds from sprk_userpreference
6. useKanbanColumns → partitions items into Today/Tomorrow/Future
7. KanbanBoard renders three columns with KanbanCard components
```

### Flow 2: Click Kanban Card → Side Pane Opens

```
1. KanbanCard onClick → opens Xrm.App.sidePanes web resource
   URL: sprk_tododetailsidepane?data=eventId={sprk_eventid}
2. parseParams() extracts eventId
3. App.tsx loads both entities in parallel:
   - loadTodoRecord(eventId) → GET sprk_event
   - loadTodoExtension(eventId) → GET sprk_eventtodo (filter by event lookup)
4. TodoDetail renders with both records
5. Score computed live from current slider values
```

### Flow 3: Edit and Save in Side Pane

```
1. User modifies fields (description, due date, notes, etc.)
2. Dirty detection tracks sprk_event fields and sprk_eventtodo fields separately
3. User clicks Save:
   a. If event fields dirty → saveTodoFields(eventId, updates)
   b. If notes dirty → saveTodoExtensionFields(todoId, { sprk_todonotes })
4. On success → sendTodoSaved(eventId) via BroadcastChannel
5. Kanban receives TODO_SAVED → refetches event → updates card
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
   - Local state updates → button switches to green "Completed" (disabled)
   - sendTodoSaved(eventId) → Kanban updates
4. Side pane stays open showing completed state
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

### TodoDetailSidePane Solution

```
src/solutions/TodoDetailSidePane/src/
├── main.tsx                           Entry point (createRoot)
├── App.tsx                            Root component — loads both entities, manages handlers
├── components/
│   └── TodoDetail.tsx                 Full edit UI — description, details, notes, score, footer
├── services/
│   └── todoService.ts                Dataverse I/O — load, save, deactivate, contact search
├── types/
│   └── TodoRecord.ts                 ITodoRecord, ITodoExtension, OData select constants
└── utils/
    ├── broadcastChannel.ts           BroadcastChannel messaging (TODO_SAVED, TODO_CLOSED)
    ├── parseParams.ts                URL parameter extraction (eventId)
    └── xrmAccess.ts                  Xrm context access, getClientUrl, setRecordState
```

### LegalWorkspace Kanban Components

```
src/solutions/LegalWorkspace/src/
├── components/SmartToDo/
│   ├── SmartToDo.tsx                  Kanban container (header, board, dismissed section)
│   ├── KanbanBoard.tsx                Three-column drag-and-drop layout
│   ├── KanbanCard.tsx                 Individual to-do card (score circle, name, badges)
│   ├── KanbanHeader.tsx               Title bar, add button, settings gear
│   └── DismissedSection.tsx           Collapsible section for dismissed items
├── hooks/
│   ├── useTodoItems.ts                Data loading + feed sync subscription
│   ├── useKanbanColumns.ts            Column assignment, drag-drop, pin toggle
│   └── useUserPreferences.ts          Threshold settings (sprk_userpreference)
├── utils/
│   └── todoScoreUtils.ts             Score formula (matches TodoDetail.tsx exactly)
├── services/
│   ├── DataverseService.ts            getActiveTodos, updateEventColumn, updateEventPinned
│   └── queryHelpers.ts                TODO_SELECT_FIELDS, buildTodoFilter, buildTodoQuery
└── types/
    └── entities.ts                    IEvent interface with all 22+ fields
```

---

## Technical Decisions

### Why a Side Pane (Not a Dialog or Form)?

Side panes (`Xrm.App.sidePanes`) stay open alongside the Kanban board, allowing users to edit a to-do while still seeing their board. Dialogs would obscure the board. Standard entity forms would navigate away from the workspace entirely.

### Why BroadcastChannel (Not React Context)?

The side pane runs inside an iframe (separate JavaScript context) from the Kanban board. React Context cannot cross iframe boundaries. `BroadcastChannel` is the standard web API for cross-context communication within the same origin.

### Why Direct REST API for State Changes?

`Xrm.WebApi.updateRecord` silently ignores `statecode`/`statuscode` fields in some Dataverse environments. The promise resolves successfully, but the record's state does not change. Using `fetch` against the Web API endpoint (`/api/data/v9.2/sprk_eventtodos({id})`) with explicit OData headers reliably persists state changes.

### Why Compute Score on Both Sides?

The Kanban board computes the score for column assignment; the side pane computes it for the live score preview. Both implementations use the identical formula to avoid drift. The side pane score updates in real time as the user adjusts the Priority and Effort sliders.

### Why Optimistic Updates Everywhere?

Every mutation (save, drag, flag toggle, pin toggle) updates local state immediately and persists asynchronously. Failed writes trigger rollback. This keeps the UI responsive — the user never waits for a network round-trip to see their action reflected.

---

## Dataverse Entity Relationships

```
┌──────────────────┐          ┌──────────────────────┐
│   sprk_event     │ 1 ←── 0..1│   sprk_eventtodo   │
│                  │          │                      │
│  sprk_eventid    │          │  sprk_eventtodoid    │
│  sprk_eventname  │          │  _sprk_event_value ──┤──→ sprk_event
│  sprk_description│          │  sprk_todonotes      │
│  sprk_duedate    │          │  sprk_completed      │
│  sprk_priority   │          │  sprk_completeddate  │
│  sprk_effortscore│          │  statecode           │
│  sprk_todoflag   │          │  statuscode          │
│  sprk_todostatus │          └──────────────────────┘
│  sprk_todocolumn │
│  sprk_todopinned │
│  _sprk_assigned  │──→ contact
│  _sprk_eventtype │──→ sprk_eventtype
│  _sprk_regarding │──→ sprk_matter / sprk_project
│  sprk_todosource │
└──────────────────┘

┌──────────────────────┐
│ sprk_userpreference  │
│                      │
│ sprk_preferencetype  │  = 100000000 (TodoKanbanThresholds)
│ sprk_preferencevalue │  = JSON: { todayThreshold, tomorrowThreshold }
└──────────────────────┘
```

---

## Theme Resolution

The side pane iframe cannot inherit the parent page's Fluent theme directly. Theme is resolved via a three-step fallback:

1. **localStorage** — Check `spaarke-theme` key (shared across all Spaarke web resources)
2. **Navbar detection** — Inspect Power Apps navbar background color (works when same-origin)
3. **Default** — Light theme (safe default; OS dark mode is intentionally not used as a fallback because it could conflict with Power Apps light mode)

All UI uses Fluent UI v9 semantic tokens per ADR-021, ensuring correct appearance in both themes.
