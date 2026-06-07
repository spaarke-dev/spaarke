/**
 * Shared reusable components barrel — backwards-compat re-export shim.
 *
 * The Kanban primitives previously living in this directory were hoisted to
 * `@spaarke/ui-components/Kanban` per smart-todo-decoupling-r3 task 010
 * (NFR-02 + FR-08). This file re-exports them so any existing
 * `import { KanbanBoard } from '../shared'` callers continue to work.
 *
 * New code should import directly from `@spaarke/ui-components`.
 */

export { KanbanBoard } from '@spaarke/ui-components';
export type { IKanbanBoardProps, IKanbanColumn } from '@spaarke/ui-components';
