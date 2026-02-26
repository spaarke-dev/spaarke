# Implementation Plan — Smart To Do Kanban Board

> **Project**: events-smart-todo-kanban
> **Module**: LegalWorkspace
> **Phases**: 4 (Foundation → Components → Integration → Polish)

---

## Architecture Context

### Discovered Resources

**ADRs**:
- ADR-006: PCF vs Code Pages — Kanban stays within existing LegalWorkspace Code Page
- ADR-012: Shared components — KanbanBoard/KanbanColumn go in `components/shared/`
- ADR-021: Fluent v9, semantic tokens, dark mode required
- ADR-022: Code Page bundles React 18 — free from PCF platform restrictions

**Patterns**:
- `.claude/patterns/pcf/dataverse-queries.md` — WebAPI CRUD patterns
- `.claude/patterns/dataverse/entity-operations.md` — Entity CRUD operations
- `.claude/patterns/webresource/full-page-custom-page.md` — Code Page template
- `.claude/patterns/pcf/theme-management.md` — Theme detection, token usage

**Constraints**:
- `.claude/constraints/pcf.md` — Fluent v9 tokens only, dark mode, accessibility
- `.claude/constraints/data.md` — Dataverse operations, optimistic UI patterns

**Existing Code**:
- `SmartToDo.tsx` — Current flat list container (will be modified)
- `TodoItem.tsx` — Card reference (badges, checkbox, InlineBadge)
- `todoScoreUtils.ts` — `computeTodoScore()` formula (drives column assignment)
- `useTodoItems.ts` — Data source hook (provides IEvent[])
- `DataverseService.ts` — CRUD operations for sprk_event
- `FeedTodoSyncContext.tsx` — Cross-block sync subscription
- `MicrosoftToDoIcon.tsx` — To Do brand icon

### Data Model

```
sprk_event (existing, 2 new fields)
├── sprk_todocolumn: Choice (0=Today, 1=Tomorrow, 2=Future)
└── sprk_todopinned: Boolean

sprk_userpreference (new entity, already created)
├── sprk_preferencetype: Choice
├── sprk_preferencevalue: Text (JSON)
└── _sprk_user_value: Lookup → systemuser
```

### Component Tree

```
SmartToDo (MODIFY — switch from flat list to Kanban)
├── KanbanHeader (NEW)
│   ├── Title + Badge
│   ├── AddTodoBar (existing, relocated)
│   ├── Recalculate Button
│   └── Settings Gear → ThresholdSettingsPopover
├── KanbanBoard (NEW — generic, reusable)
│   ├── KanbanColumn[Today] (NEW — generic droppable)
│   │   └── KanbanCard[] (NEW — draggable)
│   ├── KanbanColumn[Tomorrow]
│   │   └── KanbanCard[]
│   └── KanbanColumn[Future]
│       └── KanbanCard[]
├── DismissedSection (existing, kept at bottom)
└── TodoDetailPane (NEW — side pane content)
```

---

## Phase Breakdown

### Phase 1: Foundation (Tasks 001-005)

**Goal**: Install dependency, extend data model interfaces, create hooks for column assignment and user preferences.

| # | Deliverable | Files |
|---|------------|-------|
| 001 | Install `@hello-pangea/dnd` dependency | `package.json`, `package-lock.json` |
| 002 | Extend `IEvent` interface with `sprk_todocolumn` and `sprk_todopinned`; add `IUserPreference` interface; update `queryHelpers.ts` SELECT fields | `entities.ts`, `queryHelpers.ts`, `enums.ts` |
| 003 | Add DataverseService methods: `getUserPreferences()`, `saveUserPreferences()`, `updateEventColumn()`, `updateEventPinned()` | `DataverseService.ts` |
| 004 | Create `useUserPreferences` hook — reads/writes `sprk_userpreference` for threshold config | `hooks/useUserPreferences.ts` |
| 005 | Create `useKanbanColumns` hook — assigns items to columns by To Do Score thresholds, handles move/pin/recalculate | `hooks/useKanbanColumns.ts` |

### Phase 2: Reusable DnD Components (Tasks 010-012)

**Goal**: Build the generic, reusable KanbanBoard and KanbanColumn components with `@hello-pangea/dnd`.

| # | Deliverable | Files |
|---|------------|-------|
| 010 | Create `KanbanBoard` — generic DragDropContext wrapper with typed column/card rendering | `components/shared/KanbanBoard.tsx` |
| 011 | Create `KanbanColumn` — Droppable column with header, count badge, scrollable area, accent colour | `components/shared/KanbanColumn.tsx` |
| 012 | Create `shared/index.ts` barrel export for KanbanBoard and KanbanColumn | `components/shared/index.ts` |

### Phase 3: Smart To Do Kanban Components (Tasks 020-026)

**Goal**: Build the domain-specific Kanban card, header, settings, detail pane, and rewire SmartToDo container.

| # | Deliverable | Files |
|---|------------|-------|
| 020 | Create `KanbanCard` — draggable card with checkbox, name, due date, assigned to, score badge, pin toggle | `components/SmartToDo/KanbanCard.tsx` |
| 021 | Create `KanbanHeader` — title, count badge, AddTodoBar, recalculate button, settings gear | `components/SmartToDo/KanbanHeader.tsx` |
| 022 | Create `ThresholdSettingsPopover` — Popover with threshold inputs, save/reset, reads from useUserPreferences | `components/SmartToDo/ThresholdSettings.tsx` |
| 023 | Create `TodoDetailPane` — side pane content with editable description, score breakdown, action buttons | `components/SmartToDo/TodoDetailPane.tsx` |
| 024 | Rewire `SmartToDo.tsx` — replace flat list rendering with KanbanBoard, wire hooks, preserve all existing features (add, dismiss, restore, cross-block sync) | `components/SmartToDo/SmartToDo.tsx` |
| 025 | Update `SmartToDo/index.ts` barrel exports — add new component exports | `components/SmartToDo/index.ts` |
| 026 | Build verification — `npm run build` with zero errors, verify bundle size impact of `@hello-pangea/dnd` | N/A (verification) |

### Phase 4: Polish & Wrap-up (Tasks 030-032)

**Goal**: Dark mode testing, accessibility review, project completion.

| # | Deliverable | Files |
|---|------------|-------|
| 030 | Dark mode + high-contrast verification — all Kanban components render correctly with semantic tokens | Visual verification |
| 031 | Accessibility review — keyboard DnD, screen reader labels, focus management | Component updates |
| 032 | Project wrap-up — update README status, final build check | `README.md` |

---

## Dependencies

```
Phase 1 (001-005) — all sequential, each builds on prior
  001 → 002 → 003 → 004,005 (004 and 005 can be parallel after 003)

Phase 2 (010-012) — depends on 001 (DnD installed)
  010 → 011 → 012
  Can start in parallel with Phase 1 tasks 002-005

Phase 3 (020-026) — depends on Phase 1 + Phase 2
  020,021,022,023 can be parallel (independent components)
  024 depends on 020,021,022,023 (all composed in SmartToDo)
  025 depends on 024
  026 depends on 025

Phase 4 (030-032) — depends on Phase 3
  030,031 can be parallel
  032 depends on 030,031
```

## Parallel Execution Groups

| Group | Tasks | Prerequisite | Notes |
|-------|-------|--------------|-------|
| A | 004, 005 | 003 complete | Independent hooks |
| B | 010-012, 002-005 | 001 complete | DnD components and data model can develop in parallel |
| C | 020, 021, 022, 023 | Phase 1+2 complete | Four independent leaf components |
| D | 030, 031 | 026 complete | Visual and accessibility checks |

## Risk Register

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| `@hello-pangea/dnd` bundle too large | Low | Medium | Lazy-load KanbanBoard; measure with bundle analyzer |
| Xrm.App.sidePanes unavailable | Medium | Low | Fallback to Fluent DrawerOverlay |
| Batch Dataverse writes on recalculate | Low | Medium | Optimistic UI first, background writes |
| Cross-block sync race condition | Low | Medium | Existing FeedTodoSync pattern handles this |
