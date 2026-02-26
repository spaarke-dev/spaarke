# Smart To Do Kanban Board

> **Project**: events-smart-todo-kanban
> **Module**: LegalWorkspace (Code Page — React 18 SPA)
> **Created**: 2026-02-26

---

## Executive Summary

Replace the flat Smart To Do list in the Corporate Workspace with a three-column Kanban board (Today / Tomorrow / Future). Items are automatically assigned to columns by their existing **To Do Score** (0-100) thresholds. Users can drag-drop items between columns, pin items to prevent automatic reassignment, and expand cards into an editable side pane. Column thresholds are user-configurable and persisted in Dataverse via the new `sprk_userpreference` entity.

---

## Scope

### In Scope

1. **Three-column Kanban layout** — Today, Tomorrow, Future
2. **To Do Score-based column assignment** — items auto-assigned based on configurable thresholds
3. **Drag-and-drop** — reorder within columns and move between columns using `@hello-pangea/dnd`
4. **Pin/Lock** — users pin items to a column so the recalculate action doesn't move them
5. **Recalculate button** — re-runs To Do Score on all unpinned items and reassigns columns
6. **Kanban card** — displays: name, status (checkbox), due date, assigned to, To Do Score badge
7. **Expandable card** — click opens Xrm side pane with editable description and full event details
8. **User preferences** — column thresholds persisted in `sprk_userpreference` (JSON format)
9. **Reusable DnD wrapper** — generic `KanbanBoard` component usable across the application
10. **Dark mode / high-contrast** — all colours from Fluent v9 semantic tokens

### Out of Scope

- Server-side To Do Score computation (stays client-side)
- BFF API changes
- New Dataverse entities (already created by user)
- Email / Teams integration (stubs exist on Activity Feed cards)
- Mobile-specific responsive layout (desktop-first, naturally responsive via flex)

---

## Requirements

### Functional Requirements

#### FR-01: Three-Column Kanban Layout

The To Do section displays three columns side-by-side:

| Column | Header | Default Score Threshold | Colour Accent |
|--------|--------|------------------------|---------------|
| Today | "Today" | score ≥ 60 | `colorPaletteRedBorder2` (urgent red) |
| Tomorrow | "Tomorrow" | 30 ≤ score < 60 | `colorPaletteDarkOrangeBorder2` (amber) |
| Future | "Future" | score < 30 | `colorPaletteGreenBorder2` (relaxed green) |

Each column shows:
- Header with name + item count badge
- Top-border accent colour matching urgency
- Scrollable list of Kanban cards
- Drop target for drag-and-drop

#### FR-02: To Do Score Column Assignment

On initial load and after Recalculate:
1. Compute `computeTodoScore(event).todoScore` for each item
2. Compare against user's configured thresholds (or defaults: 60, 30)
3. Assign to Today / Tomorrow / Future based on score brackets
4. **Pinned items** (`sprk_todopinned = true`) keep their current column (`sprk_todocolumn`) regardless of score

#### FR-03: Drag-and-Drop

Using `@hello-pangea/dnd` (MIT-licensed, maintained fork of react-beautiful-dnd):
- Drag items within a column to reorder
- Drag items between columns to reassign
- On cross-column drop:
  - Set `sprk_todocolumn` to the target column (Today=0, Tomorrow=1, Future=2)
  - Auto-pin the item (`sprk_todopinned = true`) since user explicitly placed it
  - Optimistic UI update, write to Dataverse asynchronously
- On reorder within column: update local order (no Dataverse persistence for intra-column order in R1)
- Visual feedback: placeholder shown while dragging, drop indicator at target position

#### FR-04: Pin/Lock

- Each card shows a pin toggle icon (📌 `PinRegular` / `PinFilled` from `@fluentui/react-icons`)
- Pinned items display a filled pin icon and a subtle border indicator
- Pinned items are excluded from Recalculate column reassignment
- User can unpin at any time to allow auto-reassignment
- Pin state persisted in `sprk_todopinned` (boolean field on `sprk_event`)

#### FR-05: Recalculate Button

- Button in the Kanban header: "Recalculate" with `ArrowClockwiseRegular` icon
- On click:
  1. For each **unpinned** item: recompute To Do Score → reassign column based on thresholds
  2. Pinned items stay in their current column
  3. Optimistic UI update with batch Dataverse writes
  4. Show brief toast/status: "Recalculated {N} items"

#### FR-06: Kanban Card Content

Each card displays:

```
┌────────────────────────────────────────────┐
│ [☐] Contact outside counsel           [📌] │
│ Due: Feb 4 · Jane Doe            [Score:72]│
└────────────────────────────────────────────┘
```

- **Row 1**: Checkbox (status toggle) + Event name (truncated) + Pin toggle
- **Row 2**: Due date label + Assigned to name + To Do Score badge
- Left border accent matches column colour
- Completed items: strikethrough + reduced opacity (matching existing TodoItem pattern)

#### FR-07: Expandable Card — Side Pane

- Clicking the card body (not checkbox/pin) opens an **Xrm side pane** via `Xrm.App.sidePanes.createPane()`
- Side pane contains:
  - Event name (read-only header)
  - Description (`sprk_description`) — editable text area
  - Priority badge, Effort badge, Due date
  - Assigned to
  - To Do Score breakdown (priority component, effort component, urgency component)
  - Action buttons: Email (stub), Teams (stub), Edit (navigateToEntity), AI Summary
  - Save button — writes `sprk_description` changes to Dataverse
- If `Xrm.App.sidePanes` is unavailable, fall back to a Fluent v9 `DrawerOverlay` panel

#### FR-08: User-Configurable Thresholds

- Settings gear icon in Kanban header opens a small popover/dialog
- Two threshold sliders (or number inputs):
  - "Today" threshold (default: 60) — items with score ≥ this go to Today
  - "Tomorrow" threshold (default: 30) — items with score ≥ this (and < Today threshold) go to Tomorrow
  - Items below Tomorrow threshold go to Future
- On save:
  - Persist to `sprk_userpreference` with `sprk_preferencetype = "TodoKanbanThresholds"`
  - `sprk_preferencevalue` JSON: `{ "todayThreshold": 60, "tomorrowThreshold": 30 }`
- On load: read user preferences; use defaults if none found

#### FR-09: Cross-Block Sync

- When an event is flagged in the Activity Feed (Block 3), it should appear in the Kanban board via the existing `FeedTodoSyncContext` subscription
- When unflagged, it should be removed from the Kanban board
- The existing `useTodoItems` hook already handles this — the Kanban board consumes its output

### Non-Functional Requirements

| ID | Requirement |
|----|-------------|
| NFR-01 | Initial render < 500ms for up to 100 To Do items |
| NFR-02 | Drag-drop interaction feels instant (< 100ms visual feedback) |
| NFR-03 | All colours from Fluent v9 semantic tokens — zero hardcoded hex |
| NFR-04 | Dark mode and high-contrast supported automatically via token system |
| NFR-05 | `@hello-pangea/dnd` bundle impact < 30KB gzipped |
| NFR-06 | Reusable KanbanBoard component: no SmartToDo-specific logic in the DnD wrapper |
| NFR-07 | Optimistic UI for all Dataverse writes (pin, column change, status toggle) |
| NFR-08 | Keyboard accessible: Tab navigation, Enter/Space for actions, arrow keys for DnD |

---

## Technical Approach

### Architecture

```
SmartToDo (existing container)
├── KanbanHeader  (title, recalculate button, settings gear, count badge)
├── KanbanBoard   (generic DnD wrapper — @hello-pangea/dnd)
│   ├── KanbanColumn[Today]
│   │   ├── KanbanCard (draggable)
│   │   ├── KanbanCard
│   │   └── ...
│   ├── KanbanColumn[Tomorrow]
│   │   └── KanbanCard ...
│   └── KanbanColumn[Future]
│       └── KanbanCard ...
├── ThresholdSettingsPopover (user preference editor)
└── TodoDetailPane (side pane content for expanded card)
```

### Component Responsibilities

| Component | File | Purpose |
|-----------|------|---------|
| `SmartToDo` | `SmartToDo.tsx` (MODIFY) | Container: switches from flat list to Kanban layout; orchestrates data flow |
| `KanbanBoard` | `shared/KanbanBoard.tsx` (NEW) | Generic reusable DnD board: DragDropContext, columns, drag handlers |
| `KanbanColumn` | `shared/KanbanColumn.tsx` (NEW) | Single droppable column: header, scrollable card list, drop zone |
| `KanbanCard` | `SmartToDo/KanbanCard.tsx` (NEW) | Individual card: checkbox, name, due date, assigned to, score, pin |
| `KanbanHeader` | `SmartToDo/KanbanHeader.tsx` (NEW) | Header bar: title, count, recalculate, settings |
| `ThresholdSettingsPopover` | `SmartToDo/ThresholdSettings.tsx` (NEW) | Popover with threshold inputs + save/reset |
| `TodoDetailPane` | `SmartToDo/TodoDetailPane.tsx` (NEW) | Side pane content: editable description, score breakdown, actions |

### Reusable DnD Pattern

The `KanbanBoard` and `KanbanColumn` components are generic and placed in `src/solutions/LegalWorkspace/src/components/shared/`:

```typescript
// KanbanBoard.tsx — Generic DnD board
export interface IKanbanColumn<T> {
  id: string;
  title: string;
  items: T[];
  accentColor?: string;
}

export interface IKanbanBoardProps<T> {
  columns: IKanbanColumn<T>[];
  onDragEnd: (result: DropResult) => void;
  renderCard: (item: T, index: number) => React.ReactNode;
  getItemId: (item: T) => string;
}
```

This allows the same DnD board to be used for any future Kanban-style UI (e.g., project tasks, document review pipeline).

### Data Flow

```
useTodoItems (existing hook)
  ↓ items: IEvent[]
useKanbanColumns (new hook)
  ↓ reads user preferences (thresholds)
  ↓ assigns items to columns by To Do Score
  ↓ respects pinned items (sprk_todopinned + sprk_todocolumn)
  ↓ columns: IKanbanColumn<IEvent>[]
SmartToDo
  ↓ passes columns to KanbanBoard
  ↓ handles drag-end: update column assignment + pin
  ↓ handles recalculate: reassign unpinned items
KanbanBoard (generic DnD)
  ↓ renders KanbanColumn for each column
  ↓ renders KanbanCard for each item (via renderCard prop)
```

### New Hook: `useKanbanColumns`

```typescript
// hooks/useKanbanColumns.ts
export interface IUseKanbanColumnsOptions {
  items: IEvent[];
  todayThreshold: number;    // default 60
  tomorrowThreshold: number; // default 30
}

export interface IUseKanbanColumnsResult {
  columns: IKanbanColumn<IEvent>[];
  moveItem: (eventId: string, targetColumn: string) => void;
  togglePin: (eventId: string) => void;
  recalculate: () => void;
}
```

### New Hook: `useUserPreferences`

```typescript
// hooks/useUserPreferences.ts
export interface IUseUserPreferencesResult {
  preferences: ITodoKanbanPreferences;
  updatePreferences: (prefs: Partial<ITodoKanbanPreferences>) => Promise<void>;
  isLoading: boolean;
}

export interface ITodoKanbanPreferences {
  todayThreshold: number;    // default 60
  tomorrowThreshold: number; // default 30
}
```

Reads/writes `sprk_userpreference` records filtered by current user + `sprk_preferencetype = "TodoKanbanThresholds"`.

### Dataverse Fields Used

| Entity | Field | Type | Purpose |
|--------|-------|------|---------|
| `sprk_event` | `sprk_todocolumn` | Choice (0=Today, 1=Tomorrow, 2=Future) | Persisted column assignment |
| `sprk_event` | `sprk_todopinned` | Boolean | Lock item in its column |
| `sprk_userpreference` | `sprk_preferencetype` | Choice | Discriminator: "TodoKanbanThresholds" |
| `sprk_userpreference` | `sprk_preferencevalue` | Text (JSON) | Threshold values as JSON |

### IEvent Interface Extensions

Add to `entities.ts`:
```typescript
sprk_todocolumn?: number;  // Choice: 0=Today, 1=Tomorrow, 2=Future
sprk_todopinned?: boolean; // Lock item in assigned column
```

Add to `queryHelpers.ts` TODO_SELECT_FIELDS:
```typescript
'sprk_todocolumn',
'sprk_todopinned',
```

### New Entity Interface

Add to `entities.ts`:
```typescript
export interface IUserPreference {
  sprk_userpreferenceid: string;
  sprk_preferencetype: number;   // Choice field
  sprk_preferencevalue: string;  // JSON text
  _sprk_user_value?: string;     // Lookup to systemuser
}
```

### Dependency: @hello-pangea/dnd

```bash
cd src/solutions/LegalWorkspace && npm install @hello-pangea/dnd
```

- MIT license
- React 18 compatible
- Active maintenance (fork of react-beautiful-dnd)
- ~25KB gzipped
- TypeScript types included

### Side Pane Strategy

Primary: Use `Xrm.App.sidePanes.createPane()` for native Dataverse integration:
```typescript
const pane = Xrm.App.sidePanes.createPane({
  title: event.sprk_eventname,
  canClose: true,
  width: 400,
});
// Render TodoDetailPane content into the pane
```

Fallback: If Xrm.App.sidePanes is unavailable (e.g., during development), render a Fluent v9 `DrawerOverlay` as an in-app panel.

---

## Success Criteria

1. Three-column Kanban renders with items sorted into Today/Tomorrow/Future by To Do Score
2. Drag-drop moves items between columns with optimistic UI and Dataverse persistence
3. Pinned items stay in their column after Recalculate
4. Recalculate reassigns all unpinned items based on current scores and thresholds
5. Card click opens side pane with editable description
6. Threshold settings persist across sessions via `sprk_userpreference`
7. KanbanBoard component is generic (no domain-specific logic)
8. Dark mode and high-contrast render correctly (all tokens, zero hardcoded colours)
9. Cross-block sync: flag toggle in Activity Feed reflects in Kanban board
10. `npm run build` succeeds with zero errors

---

## Existing Code References

| File | Relevance |
|------|-----------|
| `src/solutions/LegalWorkspace/src/components/SmartToDo/SmartToDo.tsx` | Container to modify — replace flat list with Kanban |
| `src/solutions/LegalWorkspace/src/components/SmartToDo/TodoItem.tsx` | Reference for card layout, badges, InlineBadge pattern |
| `src/solutions/LegalWorkspace/src/utils/todoScoreUtils.ts` | `computeTodoScore()` — drives column assignment |
| `src/solutions/LegalWorkspace/src/hooks/useTodoItems.ts` | Data source hook — provides sorted IEvent[] items |
| `src/solutions/LegalWorkspace/src/hooks/useFeedTodoSync.ts` | Cross-block sync subscription |
| `src/solutions/LegalWorkspace/src/services/DataverseService.ts` | Existing CRUD methods for sprk_event |
| `src/solutions/LegalWorkspace/src/utils/navigation.ts` | `navigateToEntity()` for Edit action in detail pane |
| `src/solutions/LegalWorkspace/src/icons/MicrosoftToDoIcon.tsx` | To Do brand icon for header |
| `src/solutions/LegalWorkspace/src/types/entities.ts` | IEvent interface to extend |
| `src/solutions/LegalWorkspace/src/types/enums.ts` | Enum types for badges |

---

## Risks and Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| `@hello-pangea/dnd` bundle size | Increases page load | Lazy-load the KanbanBoard component; measure with `vite-bundle-visualizer` |
| Xrm.App.sidePanes unavailable in some hosts | Detail pane doesn't open | Fallback to Fluent v9 DrawerOverlay |
| Batch Dataverse writes on recalculate may be slow | UI feels sluggish | Optimistic updates first, batch writes in background |
| sprk_todocolumn choice values mismatch | Wrong column assignment | Validate choice values on first load, log warnings |
