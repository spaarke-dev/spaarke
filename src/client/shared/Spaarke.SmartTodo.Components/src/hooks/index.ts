/**
 * @spaarke/smart-todo-components/hooks — public hooks barrel.
 *
 * R4 task 101 (W-3 — 2026-06-18) — hoisted `useKanbanColumns` from the
 * Code Page into the peer package so both the Code Page (full mutations)
 * and the workspace widget (grouped render only) share one bucketing
 * implementation. Closes the 13-file rich-feature subtree follow-up
 * deferred from R4-020 at hook scope.
 */

export {
  useKanbanColumns,
  bucketTodoItems,
  DEFAULT_TODAY_THRESHOLD,
  DEFAULT_TOMORROW_THRESHOLD,
} from './useKanbanColumns';

export type { IUseKanbanColumnsOptions, IUseKanbanColumnsResult } from './useKanbanColumns';

// UAT 2026-06-19 — resolve current user's sprk_contact GUID for the
// migrated sprk_todo.sprk_assignedto Contact lookup.
export { useCurrentContactId } from './useCurrentContactId';
export type { IUseCurrentContactIdOptions, IUseCurrentContactIdResult } from './useCurrentContactId';
