/**
 * SmartTodoKanban — Domain composer that wraps the generic `<KanbanBoard>`
 * primitive with all SmartTodo-specific wiring (drag-drop column mutations,
 * card rendering, multi-select / open / pin callbacks, orientation prop).
 *
 * R4 task 102 (E-1, 2026-06-18) — hoisted from
 * `src/solutions/SmartTodo/src/components/SmartToDo.tsx` into the
 * `@spaarke/smart-todo-components` peer package. Closes UAT issues 3 + 4 + 6:
 *   3. Widget needs vertical/horizontal orientation toggle (parity with app)
 *   4. Widget needs drag-drop between columns + pin (parity with app)
 *   6. To Do cards need select checkboxes + tool icons (multi-select)
 *
 * Also closes the R4-020 deferred "13-file rich-feature subtree" follow-up.
 *
 * Architecture (per task POML / `.claude/adr/ADR-012`):
 *   - Generic visual primitive `<KanbanBoard>` (drag-drop infrastructure +
 *     orientation prop scaffolding) stays in `@spaarke/ui-components`.
 *   - SmartTodo-domain composer (this file) lives in
 *     `@spaarke/smart-todo-components` and binds:
 *       * The hoisted `useKanbanColumns` hook (bucketing + persistence)
 *       * The hoisted `<KanbanCard>` (sprk_todo-shaped card surface)
 *       * The drag-end handler that maps `droppableId` → `TodoColumn` and
 *         calls `moveItem` / `reorderInColumn`
 *       * Per-card pin handler that calls `togglePin`
 *
 * Both the Code Page (`SmartToDo.tsx`) and the workspace widget
 * (`SmartTodoWidget.tsx`) render this composer — ONE source of truth for
 * SmartTodo Kanban behavior.
 *
 * Mutation contract:
 *   - When `dataverseService` is supplied: drag-drop column changes and pin
 *     toggles persist via the injected service (Code Page passes its real
 *     `DataverseService`; widget can pass an adapter wrapping `Xrm.WebApi`).
 *   - When omitted: changes are local-only (optimistic state) and the hook
 *     emits a `[useKanbanColumns]` console warning so accidental misuse is
 *     visible. This matches the established hoisted-hook pattern (R4-101).
 *
 * Standards:
 *   - ADR-021: Fluent v9 + Griffel + semantic tokens (no v8, no inline
 *     styles for static rules — runtime accent colours stay inline because
 *     they're data-driven).
 *   - ADR-012: Shared component peer package; generic on `T extends
 *     IKanbanCardTodo` so both the Code Page's `ITodo` and the widget's
 *     `ITodoRecord` work without transforms.
 *
 * @see hooks/useKanbanColumns.ts — bucketing + mutation hook (R4-101)
 * @see components/KanbanCard/KanbanCard.tsx — hoisted card (R4-102)
 * @see src/client/shared/Spaarke.UI.Components/src/components/Kanban/KanbanBoard.tsx
 *   — generic visual primitive (R3 task 010 + R4-070 orientation prop)
 */

import * as React from 'react';
import type { DropResult } from '@hello-pangea/dnd';

import { KanbanBoard } from '../../../../Spaarke.UI.Components/src/components/Kanban/KanbanBoard';
import { useKanbanColumns } from '../../hooks/useKanbanColumns';
import { KanbanCard } from '../KanbanCard';
import { DEFAULT_TODAY_THRESHOLD, DEFAULT_TOMORROW_THRESHOLD } from '../../hooks/useKanbanColumns';
import type {
  IKanbanCardTodo,
  IKanbanDataverseService,
  KanbanOrientation,
  TodoColumn,
} from '../../types/kanban';

// ---------------------------------------------------------------------------
// Column id ↔ TodoColumn map — the generic `<KanbanBoard>` uses string ids
// (the column's `id` field). Our hook returns three columns identified by
// the literal TodoColumn strings, so the map is the identity function — kept
// as a typed map for safety against future column-id renames.
// ---------------------------------------------------------------------------

const COLUMN_ID_MAP: Record<string, TodoColumn> = {
  Today: 'Today',
  Tomorrow: 'Tomorrow',
  Future: 'Future',
};

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface ISmartTodoKanbanProps<T extends IKanbanCardTodo = IKanbanCardTodo> {
  /** Pre-sorted todo items (callers sort by score desc + duedate asc). */
  items: ReadonlyArray<T>;
  /**
   * Score threshold for the "Today" bucket. Defaults to
   * `DEFAULT_TODAY_THRESHOLD` (60). Code Page reads from user preferences;
   * widget uses defaults unless the host injects values.
   */
  todayThreshold?: number;
  /**
   * Score threshold for the "Tomorrow" bucket. Defaults to
   * `DEFAULT_TOMORROW_THRESHOLD` (30).
   */
  tomorrowThreshold?: number;
  /**
   * Optional Dataverse service for column / pin persistence. When omitted,
   * drag-drop + pin toggles are local-only (the hook logs a console warn).
   * Both the Code Page's `DataverseService` and a widget-side `Xrm.WebApi`
   * adapter satisfy this interface.
   */
  dataverseService?: IKanbanDataverseService;
  /**
   * Multi-select set lifted to the host (FR-27). When provided, each card
   * renders a selection checkbox bound to this Set + the `onToggleSelect`
   * callback. Omit (both `selectedIds` and `onToggleSelect`) to hide the
   * checkboxes — back-compat for surfaces without selection wiring.
   */
  selectedIds?: ReadonlySet<string>;
  /** Called when the user toggles a card's selection checkbox. */
  onToggleSelect?: (todoId: string) => void;
  /**
   * Called when the user requests to OPEN a card (per-card Open icon or
   * double-click — FR-25 / FR-26). The host wires this to the canonical
   * `OPEN_TODOS_EVENT` dispatch (or its own modal routing).
   *
   * When omitted, the per-card Open icon is not rendered.
   */
  onOpenTodo?: (todoId: string) => void;
  /**
   * Called when the host wants to react to a card click (typically opens
   * the detail pane in the Code Page; widget can omit). When omitted, card
   * body clicks are inert.
   *
   * Note: this is the SINGLE-click handler. Modal-open (double-click) flows
   * through `onOpenTodo` per the FR-25/FR-26 contract.
   */
  onCardClick?: (todoId: string) => void;
  /**
   * ID of the currently-selected card for single-select detail-pane styling
   * (Code Page detail pane). Independent of `selectedIds` (multi-select).
   */
  selectedCardId?: string | null;
  /**
   * Board layout orientation — `'horizontal'` (default) or `'vertical'`.
   * CSS-only swap (no React tree re-creation), so cards keep their
   * drag-drop + selection state across an orientation flip (NFR-08).
   *
   * @see FR-28 / FR-29 / NFR-08 (R4 spec)
   */
  orientation?: KanbanOrientation;
  /** Optional set of column IDs that are currently collapsed. */
  collapsedColumns?: ReadonlySet<string>;
  /** Called when a column header is clicked to toggle collapse. */
  onToggleCollapse?: (columnId: string) => void;
  /** ARIA label for the Kanban region. Defaults to "Smart To Do Kanban board". */
  ariaLabel?: string;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

/**
 * Domain-specific Kanban composer for SmartTodo. Renders three columns
 * (Today / Tomorrow / Future) populated by `useKanbanColumns`, each card
 * via the hoisted `<KanbanCard>`. Drag-drop and pin persistence flow
 * through the injected `dataverseService` (when supplied).
 */
export function SmartTodoKanban<T extends IKanbanCardTodo>({
  items,
  todayThreshold = DEFAULT_TODAY_THRESHOLD,
  tomorrowThreshold = DEFAULT_TOMORROW_THRESHOLD,
  dataverseService,
  selectedIds,
  onToggleSelect,
  onOpenTodo,
  onCardClick,
  selectedCardId = null,
  orientation = 'horizontal',
  collapsedColumns,
  onToggleCollapse,
  ariaLabel = 'Smart To Do Kanban board',
}: ISmartTodoKanbanProps<T>): React.ReactElement {
  const { columns, moveItem, reorderInColumn, togglePin } = useKanbanColumns<T>({
    items,
    todayThreshold,
    tomorrowThreshold,
    dataverseService,
  });

  // -------------------------------------------------------------------------
  // Drag-end handler — mirrors `SmartToDo.tsx`'s `handleDragEnd` bit-for-bit:
  //   - Dropped outside any column → no-op
  //   - Dropped at same position → no-op
  //   - Same-column reorder → `reorderInColumn`
  //   - Cross-column → `moveItem` (auto-pins to the new column)
  // -------------------------------------------------------------------------

  const handleDragEnd = React.useCallback(
    (result: DropResult) => {
      const { destination, source } = result;

      if (!destination) return;
      if (destination.droppableId === source.droppableId && destination.index === source.index) {
        return;
      }

      if (destination.droppableId === source.droppableId) {
        reorderInColumn(source.droppableId, source.index, destination.index);
      } else {
        const targetColumn = COLUMN_ID_MAP[destination.droppableId];
        if (targetColumn) {
          moveItem(result.draggableId, targetColumn);
        }
      }
    },
    [moveItem, reorderInColumn],
  );

  // -------------------------------------------------------------------------
  // Pin toggle handler — thin wrapper over the hook's `togglePin`
  // -------------------------------------------------------------------------

  const handlePinToggle = React.useCallback(
    (todoId: string) => {
      togglePin(todoId);
    },
    [togglePin],
  );

  // -------------------------------------------------------------------------
  // Render card — delegates to the hoisted `<KanbanCard>` with column-tinted
  // accent border (same as the Code Page composition).
  // -------------------------------------------------------------------------

  const renderCard = React.useCallback(
    (item: T, _index: number, columnId: string) => {
      const col = columns.find((c) => c.id === columnId);
      return (
        <KanbanCard<T>
          todo={item}
          onPinToggle={handlePinToggle}
          onClick={onCardClick}
          accentColor={col?.accentColor}
          isSelected={item.sprk_todoid === selectedCardId}
          onOpen={onOpenTodo}
          isMultiSelected={selectedIds?.has(item.sprk_todoid) ?? false}
          onToggleSelect={onToggleSelect}
        />
      );
    },
    [columns, handlePinToggle, onCardClick, selectedCardId, onOpenTodo, selectedIds, onToggleSelect],
  );

  const getItemId = React.useCallback((item: T) => item.sprk_todoid, []);

  // -------------------------------------------------------------------------
  // Render the generic primitive — all SmartTodo specifics flow as props.
  // -------------------------------------------------------------------------

  return (
    <KanbanBoard<T>
      columns={columns}
      onDragEnd={handleDragEnd}
      renderCard={renderCard}
      getItemId={getItemId}
      ariaLabel={ariaLabel}
      collapsedColumns={collapsedColumns}
      onToggleCollapse={onToggleCollapse}
      orientation={orientation}
    />
  );
}

SmartTodoKanban.displayName = 'SmartTodoKanban';
