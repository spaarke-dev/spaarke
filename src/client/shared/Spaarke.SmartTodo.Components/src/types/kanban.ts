/**
 * Kanban grouping types — host-agnostic shape for the hoisted
 * `useKanbanColumns` hook (R4 task 101 / W-3, closes R4-020 13-file deferred
 * follow-up at hook-scope).
 *
 * The hook works on any item that carries the three sprk_todo fields it
 * needs for column resolution + score-based bucketing. The Code Page passes
 * its richer `ITodo` (which is a structural superset of `IKanbanTodoLike`).
 * The widget passes its `ITodoRecord` (also structurally compatible). The
 * generic `T extends IKanbanTodoLike` keeps both call sites strongly typed
 * without forcing a single concrete shape into the peer package.
 *
 * Why a separate file (not in `./todo.ts`):
 *   - `./todo.ts` covers widget data shapes (records, regarding context,
 *     feed-sync). This file covers Kanban-specific column/service contracts.
 *   - Lets `import type { IKanbanTodoLike } from '@spaarke/smart-todo-components/types'`
 *     stay narrow when a consumer only needs the bucketing.
 *
 * @see ./todo.ts — widget data types
 * @see ../hooks/useKanbanColumns.ts — hook implementation
 * @see ADR-012 (shared component library)
 */

/**
 * Three-bucket Kanban column identifiers. Mirrors `TodoColumn` from the
 * Code Page's `types/enums.ts` (re-exported here so the peer package has
 * its own canonical type without coupling back to the host source).
 */
export type TodoColumn = 'Today' | 'Tomorrow' | 'Future';

/**
 * Minimum item shape the hoisted `useKanbanColumns` hook requires.
 *
 * Both the Code Page's `ITodo` and the widget's `ITodoRecord` are
 * structural supertypes of this — no transform needed at the call site.
 *
 * Fields used by the bucketing logic:
 *   - sprk_todoid: stable React key + override-map index
 *   - sprk_priorityscore / sprk_effortscore / sprk_duedate: score components
 *   - sprk_todocolumn / sprk_todopinned: respect pinned-column override
 *
 * Fields are optional/nullable to match Dataverse's actual nullability —
 * the hook applies sensible defaults (priority 50, effort 50, no due date).
 */
export interface IKanbanTodoLike {
  sprk_todoid: string;
  sprk_priorityscore?: number;
  sprk_effortscore?: number;
  sprk_duedate?: string;
  /** Choice value (Dataverse): 100000000=Today, 100000001=Tomorrow, 100000002=Future. */
  sprk_todocolumn?: number | string | null;
  sprk_todopinned?: boolean;
}

/**
 * Card-side superset of `IKanbanTodoLike` — adds the fields the hoisted
 * `<KanbanCard>` renders (title, status/state, assigned-to display name).
 *
 * Both the Code Page's `ITodo` (`src/solutions/SmartTodo/src/types/entities.ts`)
 * and the widget's `ITodoRecord` (`./todo.ts`) are structural supertypes of
 * this contract — the card consumes them without any transform.
 *
 * R4 task 102 (E-1, 2026-06-18) — closes UAT issues 3 + 4 + 6.
 */
export interface IKanbanCardTodo extends IKanbanTodoLike {
  /** Required: display title (sprk_name on sprk_todo). */
  sprk_name: string;
  /** Optional: Dataverse statecode (0=Active, 1=Inactive). */
  statecode?: number;
  /** Optional: Dataverse statuscode (1=Open, 659490001=InProgress, 2=Completed, 659490002=Dismissed). */
  statuscode?: number;
  /** Optional: display name from the `_sprk_assignedto_value` systemuser lookup. */
  assignedToName?: string;
}

/**
 * Re-exports the `IKanbanColumn<T>` shape from `@spaarke/ui-components` so
 * peer package consumers see one canonical column type without needing to
 * import the UI primitive library directly. Source-path import keeps the
 * dependency narrow (same pattern as `PaneHeader` in `SmartTodoWidget.tsx`).
 */
export type { IKanbanColumn, KanbanOrientation } from '../../../Spaarke.UI.Components/src/components/Kanban/types';

/**
 * Minimal Dataverse service surface the hoisted hook requires to persist
 * column moves, pin toggles, and recalculate batches.
 *
 * The Code Page's existing `DataverseService` (in
 * `src/solutions/SmartTodo/src/services/DataverseService.ts`) is structurally
 * compatible — its three methods return `IResult<...>` which carries a
 * `success: boolean` field, matching this contract.
 *
 * The widget call site does NOT pass a service (the widget renders grouped
 * lists only — no drag-drop, no mutations per task 101 scope). When omitted,
 * the hook's mutation callbacks are no-ops that log a console warning if
 * called — defensive guard against accidental misuse.
 */
export interface IKanbanDataverseService {
  /** Update sprk_todocolumn on a single todo. */
  updateEventColumn(todoId: string, column: number): Promise<{ success: boolean }>;
  /** Update sprk_todopinned on a single todo. */
  updateEventPinned(todoId: string, pinned: boolean): Promise<{ success: boolean }>;
  /** Batch-update sprk_todocolumn on many todos (sequential under the hood). */
  batchUpdateEventColumns(updates: Array<{ eventId: string; column: number }>): Promise<{ success: boolean }>;
}
