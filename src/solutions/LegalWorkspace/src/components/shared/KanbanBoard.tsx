/**
 * KanbanBoard — Generic, reusable drag-and-drop Kanban board component.
 *
 * Wraps @hello-pangea/dnd DragDropContext and renders typed columns.
 * This component has ZERO domain-specific logic — it can be used for
 * any Kanban-style UI (To Do, project tasks, document pipeline, etc.).
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

import * as React from "react";
import { makeStyles, tokens } from "@fluentui/react-components";
import {
  DragDropContext,
  Droppable,
  Draggable,
  type DropResult,
} from "@hello-pangea/dnd";

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

/** A single column in the Kanban board. */
export interface IKanbanColumn<T> {
  /** Unique column identifier (used as droppableId). */
  id: string;
  /** Display title for the column header. */
  title: string;
  /** Items assigned to this column. */
  items: T[];
  /** Optional CSS colour for the column's top accent border. */
  accentColor?: string;
}

/** Props for the generic KanbanBoard component. */
export interface IKanbanBoardProps<T> {
  /** Column definitions with their items. */
  columns: IKanbanColumn<T>[];
  /** Called when a drag operation completes (reorder or cross-column move). */
  onDragEnd: (result: DropResult) => void;
  /** Render function for each card. Receives the item and its index within the column. */
  renderCard: (item: T, index: number, columnId: string) => React.ReactNode;
  /** Extract a unique string ID from an item (used as draggableId). */
  getItemId: (item: T) => string;
  /** Optional aria-label for the board region. */
  ariaLabel?: string;
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  board: {
    display: "flex",
    flexDirection: "row",
    gap: tokens.spacingHorizontalM,
    flex: "1 1 0",
    minHeight: 0,
    overflow: "hidden",
  },
  column: {
    flex: "1 1 0",
    display: "flex",
    flexDirection: "column",
    minWidth: 0,
    backgroundColor: tokens.colorNeutralBackground2,
    borderRadius: tokens.borderRadiusMedium,
    overflow: "hidden",
  },
  columnHeader: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
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
  cardList: {
    flex: "1 1 0",
    overflowY: "auto",
    paddingTop: tokens.spacingVerticalXS,
    paddingBottom: tokens.spacingVerticalXS,
    paddingLeft: tokens.spacingHorizontalS,
    paddingRight: tokens.spacingHorizontalS,
    scrollbarWidth: "thin",
    scrollbarColor: `${tokens.colorNeutralStroke1} transparent`,
  },
  cardWrapper: {
    marginBottom: tokens.spacingVerticalXS,
  },
  emptyColumn: {
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    flex: "1 1 0",
    color: tokens.colorNeutralForeground4,
    paddingTop: tokens.spacingVerticalXL,
    paddingBottom: tokens.spacingVerticalXL,
  },
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

function KanbanBoardInner<T>(
  props: IKanbanBoardProps<T>,
  _ref: React.Ref<HTMLDivElement>
) {
  const { columns, onDragEnd, renderCard, getItemId, ariaLabel } = props;
  const styles = useStyles();

  return (
    <DragDropContext onDragEnd={onDragEnd}>
      <div
        className={styles.board}
        role="region"
        aria-label={ariaLabel ?? "Kanban board"}
      >
        {columns.map((column) => (
          <div
            key={column.id}
            className={styles.column}
            role="group"
            aria-label={column.title}
            style={
              column.accentColor
                ? { borderTopWidth: "3px", borderTopStyle: "solid", borderTopColor: column.accentColor }
                : undefined
            }
          >
            {/* Column header */}
            <div className={styles.columnHeader}>
              <span className={styles.columnTitle}>{column.title}</span>
              <span className={styles.columnCount} aria-label={`${column.items.length} items`}>
                {column.items.length}
              </span>
            </div>

            {/* Droppable card list */}
            <Droppable droppableId={column.id}>
              {(provided) => (
                <div
                  ref={provided.innerRef}
                  {...provided.droppableProps}
                  className={styles.cardList}
                  role="list"
                >
                  {column.items.length === 0 && (
                    <div className={styles.emptyColumn}>No items</div>
                  )}
                  {column.items.map((item, index) => {
                    const itemId = getItemId(item);
                    return (
                      <Draggable key={itemId} draggableId={itemId} index={index}>
                        {(dragProvided) => (
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
        ))}
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
