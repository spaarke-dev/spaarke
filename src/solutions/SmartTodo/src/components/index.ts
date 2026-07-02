/**
 * SmartToDo barrel export.
 *
 * Public API for the Smart To Do Kanban board (standalone Code Page).
 * Import as: import { SmartToDo } from './components';
 *
 * Per R3 task 020 / OS-1, the legacy single-row `TodoItem` and the standalone
 * `TodoDetailPane` (R1 list-view detail) were removed when the kanban path
 * repointed at `sprk_todo`. Per R4 task 042 / FR-18, the R3 `TodoDetailPanel`
 * side-pane is also retired. Per ai-spaarke-ai-workspace-UI-r2 FR-14
 * (2026-07-01), the R4 iframe-hosting `<SmartTodoModal>` (task 040) was also
 * retired. To-do detail editing now uses `Xrm.Navigation.navigateTo` at
 * Layout 1 (85% × 85%) — see `../SmartTodoApp.tsx` `openSprkTodoAsLayout1`.
 */

export { SmartToDo } from './SmartToDo';
export type { ISmartToDoProps } from './SmartToDo';

// R4 task 102 (E-1, 2026-06-18) — `KanbanCard` was hoisted into the
// `@spaarke/smart-todo-components` peer package so the workspace widget can
// reuse the same SmartTodo-shaped card. Re-export the hoisted symbols from
// the barrel so existing local consumers (e.g., `SmartToDo.tsx` neighbours,
// tests) continue to resolve `from './components'` without source-path churn.
export { KanbanCard } from '@spaarke/smart-todo-components';
export type { IKanbanCardProps } from '@spaarke/smart-todo-components';

export { KanbanHeader } from './KanbanHeader';
export type { IKanbanHeaderProps } from './KanbanHeader';

// R4 task 031 / FR-07 / OD-2 — the R3 `MyTasksFilter` three-mode radio group
// is removed. "Assigned to Me" is the SOLE filter mode for the SmartTodo Code
// Page; the scope is rendered as a single non-dismissible Tag in the Header
// Row 3 filter bar (see `components/Header/Header.tsx`).

export { ThresholdSettingsPopover } from './ThresholdSettings';
export type { IThresholdSettingsProps } from './ThresholdSettings';

export { TodoAISummaryDialog } from './TodoAISummaryDialog';
export type { ITodoAISummaryDialogProps } from './TodoAISummaryDialog';

export { PriorityScoreCard } from './PriorityScoreCard';
export type { IPriorityScoreCardProps } from './PriorityScoreCard';

export { EffortScoreCard } from './EffortScoreCard';
export type { IEffortScoreCardProps } from './EffortScoreCard';
