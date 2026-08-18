/**
 * @spaarke/smart-todo-components — public surface root barrel.
 *
 * R4 task 020 (Pattern D dual-use rebuild — 2026-06-10):
 *   Hosts the canonical SmartTodoWidget consumed by the LegalWorkspace embedded
 *   section shim and (future) SpaarkeAi Direct widget registration.
 *
 * See README.md and `projects/smart-todo-r4/notes/widget-surface-audit.md`.
 */

export * from './widgets';
export * from './types';
export * from './hooks';
export * from './components';
// Shared client-side To Do search predicate (§11 — single predicate used by
// BOTH the Code Page and SmartTodoWidget; smart-todo-r5 follow-up 2026-08-17).
export { matchesTodoSearchQuery, buildTodoDueDateSearchBlob } from './utils/todoSearchUtils';
export type { TodoSearchableFields } from './utils/todoSearchUtils';
