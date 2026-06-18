/**
 * KanbanBoard — Generic, reusable drag-and-drop Kanban board component.
 *
 * Wraps @hello-pangea/dnd DragDropContext and renders typed columns.
 * This component has ZERO domain-specific logic — it can be used for
 * any Kanban-style UI (To Do, project tasks, document pipeline, etc.).
 *
 * Hoisted from `src/solutions/SmartTodo/src/components/shared/KanbanBoard.tsx`
 * per smart-todo-decoupling-r3 task 010 (NFR-02 + FR-08).
 * Implementation preserved EXACTLY (zero behaviour or style changes) to
 * lock the R2 a11y baseline (NFR-10).
 *
 * Usage:
 *   <KanbanBoard<IEvent>
 *     columns={columns}
 *     onDragEnd={handleDragEnd}
 *     renderCard={(item, index) => <MyCard item={item} />}
 *     getItemId={(item) => item.id}
 *   />
 *
 * Design constraints:
 *   - All colours from Fluent UI v9 semantic tokens
 *   - makeStyles (Griffel) for all custom styles
 *   - Dark mode + high-contrast supported via token system
 *   - Keyboard accessible DnD (built into @hello-pangea/dnd)
 */

import * as React from 'react';
import { makeStyles, mergeClasses, tokens } from '@fluentui/react-components';
import { DragDropContext, Droppable, Draggable } from '@hello-pangea/dnd';
import type { IKanbanBoardProps } from './types';

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  board: {
    display: 'flex',
    flexDirection: 'row',
    gap: tokens.spacingHorizontalM,
    flex: '1 1 0',
    minHeight: 0,
    overflow: 'hidden',
    // FR-29 / NFR-08 — smooth row↔column flip (CSS-only). Honour
    // prefers-reduced-motion by snapping with zero transition.
    transitionProperty: 'gap',
    transitionDuration: tokens.durationNormal,
    transitionTimingFunction: tokens.curveEasyEase,
    '@media (prefers-reduced-motion: reduce)': {
      transitionDuration: '0ms',
    },
  },
  /**
   * Vertical orientation — columns stack top-to-bottom as collapsible
   * sections. `overflow-y: auto` lets the page scroll when the cumulative
   * section heights exceed the container height (FR-29: narrow workspace
   * widget container).
   */
  boardVertical: {
    flexDirection: 'column',
    overflowY: 'auto',
    overflowX: 'hidden',
    gap: tokens.spacingVerticalS,
  },
  column: {
    flex: '1 1 0',
    display: 'flex',
    flexDirection: 'column',
    minWidth: 0,
    backgroundColor: tokens.colorNeutralBackground2,
    borderRadius: tokens.borderRadiusMedium,
    overflow: 'hidden',
  },
  /**
   * Vertical-mode column — natural height. `flex: 0 0 auto` releases the
   * equal-flex distribution so each section sizes by its own card list;
   * the page-level scroll on `boardVertical` handles overflow.
   */
  columnVertical: {
    flex: '0 0 auto',
    width: '100%',
    minWidth: 0,
  },
  columnHeader: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    flexShrink: 0,
  },
  columnTitle: {
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
  },
  columnCount: {
    color: tokens.colorNeutralForeground3,
  },
  columnSubtitle: {
    color: tokens.colorNeutralForeground4,
    fontSize: tokens.fontSizeBase100,
  },
  cardList: {
    flex: '1 1 0',
    overflowY: 'auto',
    minHeight: 0,
    paddingTop: tokens.spacingVerticalXS,
    paddingBottom: tokens.spacingVerticalXS,
    paddingLeft: tokens.spacingHorizontalS,
    paddingRight: tokens.spacingHorizontalS,
  },
  /**
   * Vertical-mode card list — auto-height so the section grows with its
   * cards; page-level scroll on `boardVertical` takes over when content
   * exceeds the container.
   */
  cardListVertical: {
    flex: '0 0 auto',
    overflowY: 'visible',
  },
  cardWrapper: {
    marginBottom: tokens.spacingVerticalXS,
  },
  emptyColumn: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    flex: '1 1 0',
    color: tokens.colorNeutralForeground4,
    paddingTop: tokens.spacingVerticalXL,
    paddingBottom: tokens.spacingVerticalXL,
  },
  columnCollapsed: {
    flex: '0 0 40px',
    display: 'flex',
    flexDirection: 'column',
    minWidth: '40px',
    backgroundColor: tokens.colorNeutralBackground2,
    borderRadius: tokens.borderRadiusMedium,
    overflow: 'hidden',
    cursor: 'pointer',
  },
  columnCollapsedHeader: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
    gap: tokens.spacingVerticalXS,
  },
  columnCollapsedTitle: {
    writingMode: 'vertical-rl',
    transform: 'rotate(180deg)',
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
  },
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

function KanbanBoardInner<T>(props: IKanbanBoardProps<T>, _ref: React.Ref<HTMLDivElement>) {
  const {
    columns,
    onDragEnd,
    renderCard,
    getItemId,
    ariaLabel,
    collapsedColumns,
    onToggleCollapse,
    orientation = 'horizontal',
  } = props;
  const styles = useStyles();

  const isVertical = orientation === 'vertical';
  const boardClassName = mergeClasses(styles.board, isVertical && styles.boardVertical);
  const columnClassName = mergeClasses(styles.column, isVertical && styles.columnVertical);
  const cardListClassName = mergeClasses(styles.cardList, isVertical && styles.cardListVertical);

  return (
    <DragDropContext onDragEnd={onDragEnd}>
      <div
        className={boardClassName}
        role="region"
        aria-label={ariaLabel ?? 'Kanban board'}
        aria-orientation={isVertical ? 'vertical' : 'horizontal'}
        data-orientation={orientation}
      >
        {columns.map(column => {
          const isCollapsed = collapsedColumns?.has(column.id) ?? false;

          // R4 task 103 (E-2, 2026-06-18) — column tint + top-border accent
          // are composed into a single inline style. `tintColor` (UAT 5) sits
          // behind cards as a gentle wash; `accentColor` remains the sharper
          // top-border accent rail. Both default to `undefined` for backwards
          // compatibility with existing consumers (Calendar, future Kanban
          // surfaces) that don't set either.
          const columnInlineStyle: React.CSSProperties | undefined = (() => {
            const hasAccent = !!column.accentColor;
            const hasTint = !!column.tintColor;
            if (!hasAccent && !hasTint) return undefined;
            return {
              ...(hasAccent
                ? {
                    borderTopWidth: '3px',
                    borderTopStyle: 'solid',
                    borderTopColor: column.accentColor,
                  }
                : {}),
              ...(hasTint ? { backgroundColor: column.tintColor } : {}),
            };
          })();

          if (isCollapsed) {
            return (
              <div
                key={column.id}
                className={styles.columnCollapsed}
                role="group"
                aria-label={`${column.title} (collapsed)`}
                onClick={() => onToggleCollapse?.(column.id)}
                style={columnInlineStyle}
              >
                <div className={styles.columnCollapsedHeader}>
                  <span className={styles.columnCount}>{column.items.length}</span>
                  <span className={styles.columnCollapsedTitle}>{column.title}</span>
                </div>
              </div>
            );
          }

          return (
            <div
              key={column.id}
              className={columnClassName}
              role="group"
              aria-label={column.title}
              style={columnInlineStyle}
            >
              {/* Column header */}
              <div
                className={styles.columnHeader}
                style={onToggleCollapse ? { cursor: 'pointer' } : undefined}
                onClick={onToggleCollapse ? () => onToggleCollapse(column.id) : undefined}
              >
                <div>
                  <span className={styles.columnTitle}>{column.title}</span>
                  {column.subtitle && <div className={styles.columnSubtitle}>{column.subtitle}</div>}
                </div>
                <span className={styles.columnCount} aria-label={`${column.items.length} items`}>
                  {column.items.length}
                </span>
              </div>

              {/* Droppable card list */}
              <Droppable droppableId={column.id}>
                {provided => (
                  <div ref={provided.innerRef} {...provided.droppableProps} className={cardListClassName} role="list">
                    {column.items.length === 0 && <div className={styles.emptyColumn}>No items</div>}
                    {column.items.map((item, index) => {
                      const itemId = getItemId(item);
                      return (
                        <Draggable key={itemId} draggableId={itemId} index={index}>
                          {dragProvided => (
                            <div
                              ref={dragProvided.innerRef}
                              {...dragProvided.draggableProps}
                              {...dragProvided.dragHandleProps}
                              className={styles.cardWrapper}
                            >
                              {renderCard(item, index, column.id)}
                            </div>
                          )}
                        </Draggable>
                      );
                    })}
                    {provided.placeholder}
                  </div>
                )}
              </Droppable>
            </div>
          );
        })}
      </div>
    </DragDropContext>
  );
}

/**
 * Generic Kanban board with drag-and-drop support.
 * Use with typed columns and a renderCard function.
 */
export const KanbanBoard = React.forwardRef(KanbanBoardInner) as <T>(
  props: IKanbanBoardProps<T> & { ref?: React.Ref<HTMLDivElement> }
) => React.ReactElement | null;
