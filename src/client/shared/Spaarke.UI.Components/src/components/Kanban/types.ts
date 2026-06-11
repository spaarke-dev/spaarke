/**
 * Kanban primitives — type definitions.
 *
 * Hoisted from `src/solutions/SmartTodo/src/components/shared/KanbanBoard.tsx`
 * per smart-todo-decoupling-r3 task 010 (NFR-02 + FR-08).
 *
 * @see KanbanBoard.tsx — Generic drag-and-drop board component
 * @see KanbanCard.tsx — Generic card primitive (slot-based)
 * @see ADR-012 — Shared component library
 */

import type * as React from 'react';
import type { DropResult } from '@hello-pangea/dnd';

// ---------------------------------------------------------------------------
// IKanbanColumn — column definition consumed by KanbanBoard
// ---------------------------------------------------------------------------

/**
 * A single column in the Kanban board.
 *
 * Domain-agnostic: parameterised on the item type `T` so the same primitive
 * works for to-dos, project tasks, document pipelines, etc.
 */
export interface IKanbanColumn<T> {
  /** Unique column identifier (used as droppableId). */
  id: string;
  /** Display title for the column header. */
  title: string;
  /** Optional subtitle shown below the title (e.g., score criteria). */
  subtitle?: string;
  /** Items assigned to this column. */
  items: T[];
  /** Optional CSS colour for the column's top accent border. */
  accentColor?: string;
}

/**
 * Type alias — `KanbanColumn<T>` is the canonical shorthand for `IKanbanColumn<T>`.
 *
 * Exported per FR-08 acceptance criteria (Lib barrel exports `KanbanColumn`).
 * Use either name interchangeably.
 */
export type KanbanColumn<T> = IKanbanColumn<T>;

// ---------------------------------------------------------------------------
// IKanbanBoardProps — props for the generic KanbanBoard component
// ---------------------------------------------------------------------------

/**
 * Board layout orientation.
 *
 * - `horizontal` (default) — columns flow side-by-side (classic Kanban).
 * - `vertical`             — columns stack top-to-bottom as collapsible
 *                            sections; cards stack within each section.
 *
 * Implemented as a CSS-only swap (`flex-direction: row` ↔ `column`) — no
 * React tree re-creation, so cards keep their state across an orientation
 * change (smart-todo-r4 spec NFR-08: <300ms switch, no jank).
 */
export type KanbanOrientation = 'horizontal' | 'vertical';

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
  /** Set of column IDs that are currently collapsed. */
  collapsedColumns?: ReadonlySet<string>;
  /** Called when a column header is clicked to toggle collapse. */
  onToggleCollapse?: (columnId: string) => void;
  /**
   * Board flow direction. Defaults to `'horizontal'` (backwards-compatible).
   * Setting this to `'vertical'` swaps the CSS layout only — same React
   * tree, no card-state loss. See {@link KanbanOrientation}.
   *
   * smart-todo-r4 spec FR-28 / FR-29 / NFR-08.
   */
  orientation?: KanbanOrientation;
}

// ---------------------------------------------------------------------------
// IKanbanCardProps — props for the generic KanbanCard slot primitive
// ---------------------------------------------------------------------------

/**
 * Props for the generic KanbanCard primitive.
 *
 * Slot-based, domain-agnostic. Consumer wires its own data shape into the
 * slots — see the smart-todo-decoupling-r3 SmartTodo Code Page for a worked
 * example (constructs slots from an `IEvent`/`ITodo`).
 */
export interface IKanbanCardProps {
  /**
   * Left-anchor visual slot (e.g., score circle, status indicator, icon).
   * Sized to 40px square by convention. Aria-hidden by default.
   */
  scoreSlot?: React.ReactNode;
  /** Primary title slot (truncated single line). Required. */
  titleSlot: React.ReactNode;
  /** Optional secondary metadata rows (due date, assigned-to, badges, etc.). */
  metadataSlot?: React.ReactNode;
  /** Right-anchor actions slot (e.g., pin button, overflow menu). */
  actionsSlot?: React.ReactNode;
  /** Left border accent colour from parent column. */
  accentColor?: string;
  /** Whether this card is currently selected (detail panel open). */
  isSelected?: boolean;
  /** Whether the underlying item is completed (applies dimmed/strikethrough). */
  isCompleted?: boolean;
  /** Called when card body is clicked. */
  onClick?: () => void;
  /** Full aria-label for the card (read by screen readers in place of children). */
  ariaLabel?: string;
}
