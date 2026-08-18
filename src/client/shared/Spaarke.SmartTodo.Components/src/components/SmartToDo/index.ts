/**
 * SmartToDo rich-subtree barrel (folder-scoped).
 *
 * R5 FR-01 / task 002 — the 13-file rich Kanban subtree hoisted host-agnostic
 * from `src/solutions/LegalWorkspace/src/components/SmartToDo/` into
 * `@spaarke/smart-todo-components`.
 *
 * NOTE: `KanbanCard` / `IKanbanCardProps` here are the LegalWorkspace *rich*
 * card (RecordCardShell-based, ITodo-typed) — DISTINCT from the package's
 * pre-existing widget card at `../KanbanCard/`. The package root barrel
 * (`../index.ts`) re-exports this one under the alias `SmartToDoKanbanCard`
 * to avoid clobbering the externally-consumed root `KanbanCard` export.
 */

export { SmartToDo, LazyTodoAISummaryDialog, TodoAISummaryFallback } from './SmartToDo';
export type { ISmartToDoProps } from './SmartToDo';

export { SmartToDoDialog } from './SmartToDoDialog';
export type { ISmartToDoDialogProps } from './SmartToDoDialog';

export { KanbanCard } from './KanbanCard';
export type { IKanbanCardProps } from './KanbanCard';

export { KanbanHeader } from './KanbanHeader';
export type { IKanbanHeaderProps } from './KanbanHeader';

export { AddTodoBar } from './AddTodoBar';
export type { IAddTodoBarProps } from './AddTodoBar';

export { DismissedSection } from './DismissedSection';
export type { IDismissedSectionProps } from './DismissedSection';

export { ThresholdSettingsPopover } from './ThresholdSettings';
export { default as ThresholdSettings } from './ThresholdSettings';
export type { IThresholdSettingsProps } from './ThresholdSettings';

export { TodoDetailPane } from './TodoDetailPane';
export type { ITodoDetailPaneProps } from './TodoDetailPane';

export { TodoAISummaryDialog } from './TodoAISummaryDialog';
export type { ITodoAISummaryDialogProps } from './TodoAISummaryDialog';

export { PriorityScoreCard } from './PriorityScoreCard';
export type { IPriorityScoreCardProps } from './PriorityScoreCard';

export { EffortScoreCard } from './EffortScoreCard';
export type { IEffortScoreCardProps } from './EffortScoreCard';
