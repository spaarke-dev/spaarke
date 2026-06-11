/**
 * Kanban primitives — barrel export.
 *
 * Hoisted from SmartTodo-local per smart-todo-decoupling-r3 task 010
 * (NFR-02 + FR-08). Domain-agnostic primitives reusable across surfaces.
 *
 * Exports (per task 010 acceptance criteria):
 *   - KanbanBoard         — generic DnD board (drag-drop + columns)
 *   - KanbanColumn (type) — alias of IKanbanColumn<T>
 *   - KanbanCard          — generic slot-based card primitive
 *
 * Plus the supporting type interfaces:
 *   - IKanbanColumn<T>, IKanbanBoardProps<T>, IKanbanCardProps
 *
 * @see ADR-012 Shared Component Library
 * @see ADR-021 Fluent UI v9 design tokens
 */

export { KanbanBoard } from './KanbanBoard';
export { KanbanCard } from './KanbanCard';
export type {
  IKanbanBoardProps,
  IKanbanCardProps,
  IKanbanColumn,
  KanbanColumn,
  KanbanOrientation,
} from './types';
