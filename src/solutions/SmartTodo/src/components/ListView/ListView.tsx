/**
 * ListView — Dense list rendering of SmartTodo items (R4 FR-09 / task 033).
 *
 * Alternative to the Kanban card grid (`SmartToDo`) when the user prefers a
 * dense, row-oriented list view. Renders the same `ITodo[]` data using a
 * Fluent v9 `<Table>` with columns:
 *
 *   • Score      — composite To Do Score (0-100) from `computeTodoScore`
 *   • Title      — `sprk_name` (truncated)
 *   • Due        — short due-date label + urgency tier
 *   • Assigned   — `assignedToName` (formatted-value display)
 *   • Pin        — toggle (delegated to parent via `onPinToggle`)
 *
 * Row interactions:
 *   • Click row → `onItemClick(todoId)` (parent opens detail panel; matches
 *     KanbanCard semantics)
 *   • Click pin → `onPinToggle(todoId)` — does NOT bubble to row click
 *   • Completed items (statuscode 2) show 0.6 opacity + strikethrough title
 *
 * Per ADR-021: Fluent v9 + Griffel + semantic tokens. No v8, no inline styles.
 * Per ADR-012: Fluent v9 `<Table>` consumed directly from `@fluentui/react-components`
 * (no need to hoist a new shared primitive — this is solution-specific layout).
 *
 * @see ADR-021 Fluent UI v9 design system
 * @see ADR-012 Shared component library
 * @see smart-todo-r4 spec FR-09 (List/Card view toggle)
 * @see ./KanbanCard.tsx — parallel visual reference (score + due + pin)
 */
import * as React from 'react';
import {
  Table,
  TableBody,
  TableCell,
  TableHeader,
  TableHeaderCell,
  TableRow,
  Button,
  Tooltip,
  Text,
  mergeClasses,
} from '@fluentui/react-components';
import { PinRegular, PinFilled } from '@fluentui/react-icons';
import type { ITodo } from '../../types/entities';
import { computeTodoScore } from '../../utils/todoScoreUtils';
import { computeDueLabel, parseDueDate } from '../../utils/dueLabelUtils';
import { useListViewStyles } from './ListView.styles';

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface IListViewProps {
  /** Items to render (already sorted by parent — typically by To Do Score DESC). */
  items: ReadonlyArray<ITodo>;
  /** Called when the user clicks a row body. */
  onItemClick?: (todoId: string) => void;
  /** Called when the user clicks the pin icon. */
  onPinToggle?: (todoId: string) => void;
  /** GUID of the row currently shown in the detail panel (for highlight). */
  selectedTodoId?: string | null;
  /** Optional aria-label override. */
  ariaLabel?: string;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const ListView: React.FC<IListViewProps> = ({
  items,
  onItemClick,
  onPinToggle,
  selectedTodoId = null,
  ariaLabel = 'Smart To Do list',
}) => {
  const styles = useListViewStyles();

  const handleRowClick = React.useCallback(
    (todoId: string) => () => {
      onItemClick?.(todoId);
    },
    [onItemClick],
  );

  const handlePinClick = React.useCallback(
    (todoId: string) =>
      (e: React.MouseEvent<HTMLButtonElement>) => {
        // Prevent the row click from firing (matches KanbanCard semantics).
        e.stopPropagation();
        onPinToggle?.(todoId);
      },
    [onPinToggle],
  );

  if (items.length === 0) {
    return (
      <div className={styles.root} role="region" aria-label={ariaLabel}>
        <div className={styles.emptyRow}>
          <Text size={200}>No to-do items.</Text>
        </div>
      </div>
    );
  }

  return (
    <div className={styles.root} role="region" aria-label={ariaLabel}>
      <div className={styles.scrollArea}>
        <Table
          className={styles.tableWrap}
          size="small"
          aria-label={ariaLabel}
        >
          <TableHeader>
            <TableRow className={styles.headerRow}>
              <TableHeaderCell style={{ width: '56px' }}>Score</TableHeaderCell>
              <TableHeaderCell>Title</TableHeaderCell>
              <TableHeaderCell style={{ width: '140px' }}>Due</TableHeaderCell>
              <TableHeaderCell style={{ width: '180px' }}>Assigned</TableHeaderCell>
              <TableHeaderCell className={styles.actionCell}>
                <span aria-label="Pin" />
              </TableHeaderCell>
            </TableRow>
          </TableHeader>
          <TableBody>
            {items.map((item) => {
              const isCompleted = item.statuscode === 2;
              const isSelected = selectedTodoId === item.sprk_todoid;
              const { todoScore } = computeTodoScore(item);
              const due = computeDueLabel(parseDueDate(item.sprk_duedate));
              const pinned = item.sprk_todopinned === true;

              return (
                <TableRow
                  key={item.sprk_todoid}
                  className={mergeClasses(
                    styles.row,
                    isSelected && styles.rowSelected,
                    isCompleted && styles.rowCompleted,
                  )}
                  onClick={handleRowClick(item.sprk_todoid)}
                  aria-selected={isSelected}
                >
                  <TableCell className={styles.scoreCell}>
                    {Math.round(todoScore)}
                  </TableCell>
                  <TableCell
                    className={mergeClasses(
                      styles.titleCell,
                      isCompleted && styles.titleCompleted,
                    )}
                  >
                    {item.sprk_name}
                  </TableCell>
                  <TableCell className={styles.secondaryCell}>
                    {due.label || '—'}
                  </TableCell>
                  <TableCell className={styles.secondaryCell}>
                    {item.assignedToName ?? '—'}
                  </TableCell>
                  <TableCell className={styles.actionCell}>
                    <Tooltip
                      content={pinned ? 'Unpin' : 'Pin'}
                      relationship="label"
                    >
                      <Button
                        appearance="subtle"
                        size="small"
                        icon={pinned ? <PinFilled /> : <PinRegular />}
                        aria-label={pinned ? 'Unpin to-do' : 'Pin to-do'}
                        aria-pressed={pinned}
                        onClick={handlePinClick(item.sprk_todoid)}
                      />
                    </Tooltip>
                  </TableCell>
                </TableRow>
              );
            })}
          </TableBody>
        </Table>
      </div>
    </div>
  );
};

ListView.displayName = 'SmartTodoListView';
