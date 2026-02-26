# CLAUDE.md — Smart To Do Kanban Board

> **Project**: smart-todo-kanban
> **Module**: LegalWorkspace (React 18 Code Page)

---

## Project Context

This project replaces the flat Smart To Do list with a three-column Kanban board (Today/Tomorrow/Future) using `@hello-pangea/dnd` for drag-and-drop. Items are auto-assigned to columns by To Do Score thresholds, with pin/lock override and user-configurable thresholds persisted in Dataverse.

## Applicable ADRs

| ADR | Key Constraint |
|-----|---------------|
| ADR-006 | Code Pages for standalone dialogs; this is within the existing LegalWorkspace Code Page |
| ADR-012 | Shared components in `components/shared/`; KanbanBoard/KanbanColumn are generic |
| ADR-021 | Fluent v9 only, `makeStyles` (Griffel), semantic tokens, dark mode required |
| ADR-022 | Code Page bundles React 18 — no PCF platform library restrictions |

## Key Design Decisions

1. **Reusable DnD**: `KanbanBoard` and `KanbanColumn` are generic (typed via generics), placed in `components/shared/`. No SmartToDo-specific logic in the DnD wrapper.
2. **Column assignment**: Client-side only via `computeTodoScore()`. Not server-computed.
3. **Pin on drag**: Cross-column drag auto-pins the item to prevent recalculate from moving it back.
4. **Side pane**: Primary: `Xrm.App.sidePanes.createPane()`. Fallback: Fluent v9 `DrawerOverlay`.
5. **User preferences**: `sprk_userpreference` entity with JSON in `sprk_preferencevalue`.

## File Map

### New Files
- `src/solutions/LegalWorkspace/src/components/shared/KanbanBoard.tsx`
- `src/solutions/LegalWorkspace/src/components/shared/KanbanColumn.tsx`
- `src/solutions/LegalWorkspace/src/components/shared/index.ts`
- `src/solutions/LegalWorkspace/src/components/SmartToDo/KanbanCard.tsx`
- `src/solutions/LegalWorkspace/src/components/SmartToDo/KanbanHeader.tsx`
- `src/solutions/LegalWorkspace/src/components/SmartToDo/ThresholdSettings.tsx`
- `src/solutions/LegalWorkspace/src/components/SmartToDo/TodoDetailPane.tsx`
- `src/solutions/LegalWorkspace/src/hooks/useKanbanColumns.ts`
- `src/solutions/LegalWorkspace/src/hooks/useUserPreferences.ts`

### Modified Files
- `src/solutions/LegalWorkspace/src/types/entities.ts` — add `sprk_todocolumn`, `sprk_todopinned`, `IUserPreference`
- `src/solutions/LegalWorkspace/src/types/enums.ts` — add `TodoColumn` type
- `src/solutions/LegalWorkspace/src/services/queryHelpers.ts` — add fields to SELECT
- `src/solutions/LegalWorkspace/src/services/DataverseService.ts` — add preference + column CRUD
- `src/solutions/LegalWorkspace/src/components/SmartToDo/SmartToDo.tsx` — replace flat list with Kanban
- `src/solutions/LegalWorkspace/src/components/SmartToDo/index.ts` — add barrel exports
- `src/solutions/LegalWorkspace/package.json` — add `@hello-pangea/dnd`

## Dataverse Fields

| Entity | Field | Type | Values |
|--------|-------|------|--------|
| `sprk_event` | `sprk_todocolumn` | Choice | 0=Today, 1=Tomorrow, 2=Future |
| `sprk_event` | `sprk_todopinned` | Boolean | true/false |
| `sprk_userpreference` | `sprk_preferencetype` | Choice | "TodoKanbanThresholds" |
| `sprk_userpreference` | `sprk_preferencevalue` | Text | `{"todayThreshold":60,"tomorrowThreshold":30}` |

## Patterns to Follow

- **Optimistic UI**: Match existing pattern in `SmartToDo.tsx` — apply state change immediately, rollback on error
- **InlineBadge**: Reuse from `TodoItem.tsx` for score badge
- **Cross-block sync**: Consume `useFeedTodoSync()` — Kanban inherits this from `useTodoItems`
- **Griffel styles**: `makeStyles` with semantic tokens — zero hardcoded colours
- **Side pane**: Use `Xrm.App.sidePanes` pattern from `navigation.ts`

## Task Execution Protocol

When executing tasks in this project, Claude Code MUST invoke the `task-execute` skill. DO NOT read POML files directly and implement manually.
