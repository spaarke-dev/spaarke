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
    // 2026-06-19 fix: explicit `width: 100%` + `alignSelf: stretch` to
    // guarantee the board fills its container's cross-axis. Without these,
    // some host layouts (Griffel-nested flex chains, SectionPanel) computed
    // the board at half the container width and clipped Tomorrow + Future
    // columns via `overflow: hidden`. The user's decision (2026-06-19):
    // horizontal mode = ALWAYS fit all 3 columns to container, no
    // horizontal scroll. Columns themselves shrink via `flex: 1 1 0 /
    // minWidth: 0 / overflow: hidden` so cards ellipsis-truncate cleanly
    // in narrow widgets.
    width: '100%',
    alignSelf: 'stretch',
    minHeight: 0,
    minWidth: 0,
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
  /**
   * Column-header count badge — UAT 2026-06-19 redesign:
   * pill-shaped with a column-coordinated background color (red/yellow/green)
   * matching the column accent. The background color is applied INLINE
   * because it's data-driven per column; the rest of the pill shape +
   * typography is Griffel-managed.
   */
  columnCount: {
    display: 'inline-flex',
    alignItems: 'center',
    justifyContent: 'center',
    minWidth: '24px',
    height: '20px',
    paddingLeft: tokens.spacingHorizontalS,
    paddingRight: tokens.spacingHorizontalS,
    borderRadius: '999px',
    fontWeight: tokens.fontWeightSemibold,
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForegroundOnBrand,
    backgroundColor: tokens.colorNeutralBackground3,
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
  /**
   * UAT 2026-06-19 — collapsed column matches expanded shape:
   * SAME flex sizing + width as expanded so the layout doesn't jump.
   * Title left-aligned, count pill right-aligned. Only the card list area
   * is hidden. The user's request: "title should show and be left aligned;
   * pill right aligned (same location as when not collapsed)".
   */
  columnCollapsed: {
    flex: '1 1 0',
    display: 'flex',
    flexDirection: 'column',
    minWidth: 0,
    backgroundColor: tokens.colorNeutralBackground2,
    borderRadius: tokens.borderRadiusMedium,
    overflow: 'hidden',
    cursor: 'pointer',
  },
  /** Collapsed header — same layout as columnHeader. */
  columnCollapsedHeader: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
  },
  /** Collapsed title — horizontal, semibold (matches expanded columnTitle). */
  columnCollapsedTitle: {
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
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

          // UAT 2026-06-20 — Single container for both expanded AND collapsed.
          // Same column classname/layout in both states; only the droppable
          // card list area is conditional. This fixes two issues:
          //   1. Vertical mode collapsed: column inherits columnVertical's
          //      `flex: 0 0 auto` so it sizes to its content (just the header
          //      = ~44px), instead of growing via the prior `columnCollapsed`
          //      `flex: 1 1 0` which fell through unstyled in vertical parents.
          //   2. Horizontal mode collapsed: `alignSelf: flex-start` opts out
          //      of the cross-axis stretch default so the column height
          //      collapses to header height. Other expanded siblings still
          //      stretch to board height normally.
          const collapsedHorizontalInline =
            isCollapsed && !isVertical ? { alignSelf: 'flex-start' as const } : {};

          return (
            <div
              key={column.id}
              className={columnClassName}
              role="group"
              aria-label={isCollapsed ? `${column.title} (collapsed)` : column.title}
              style={{ ...columnInlineStyle, ...collapsedHorizontalInline }}
            >
              {/* Column header — always rendered; click toggles collapse. */}
              <div
                className={styles.columnHeader}
                style={onToggleCollapse ? { cursor: 'pointer' } : undefined}
                onClick={onToggleCollapse ? () => onToggleCollapse(column.id) : undefined}
              >
                <div>
                  <span className={styles.columnTitle}>{column.title}</span>
                  {column.subtitle && <div className={styles.columnSubtitle}>{column.subtitle}</div>}
                </div>
                <span
                  className={styles.columnCount}
                  style={
                    column.accentColor
                      ? {
                          backgroundColor: column.accentColor,
                          color: column.countTextColor ?? tokens.colorNeutralForegroundOnBrand,
                        }
                      : undefined
                  }
                  aria-label={`${column.items.length} items`}
                >
                  {column.items.length}
                </span>
              </div>

              {/* Droppable card list — hidden when column collapsed. */}
              {!isCollapsed && (
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
              )}
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
