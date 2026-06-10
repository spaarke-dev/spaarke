/**
 * SmartToDo barrel export.
 *
 * Public API for the Smart To Do Kanban board (standalone Code Page).
 * Import as: import { SmartToDo } from './components';
 *
 * Per R3 task 020 / OS-1, the legacy single-row `TodoItem` and the standalone
 * `TodoDetailPane` (R1 list-view detail) were removed when the kanban path
 * repointed at `sprk_todo`. The active detail surface is `TodoDetailPanel.tsx`
 * which wraps the shared `@spaarke/ui-components` `TodoDetail`.
 */

export { SmartToDo } from './SmartToDo';
export type { ISmartToDoProps } from './SmartToDo';

export { KanbanCard } from './KanbanCard';
export type { IKanbanCardProps } from './KanbanCard';

export { KanbanHeader } from './KanbanHeader';
export type { IKanbanHeaderProps } from './KanbanHeader';

export { MyTasksFilter, MY_TASKS_FILTER_MODES } from './MyTasksFilter';
export type { IMyTasksFilterProps } from './MyTasksFilter';

export { ThresholdSettingsPopover } from './ThresholdSettings';
export type { IThresholdSettingsProps } from './ThresholdSettings';

export { TodoAISummaryDialog } from './TodoAISummaryDialog';
export type { ITodoAISummaryDialogProps } from './TodoAISummaryDialog';

export { PriorityScoreCard } from './PriorityScoreCard';
export type { IPriorityScoreCardProps } from './PriorityScoreCard';

export { EffortScoreCard } from './EffortScoreCard';
export type { IEffortScoreCardProps } from './EffortScoreCard';
