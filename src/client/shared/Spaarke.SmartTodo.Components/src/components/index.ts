/**
 * @spaarke/smart-todo-components/components — public components barrel.
 *
 * R4 task 102 (E-1, 2026-06-18) — hoisted KanbanCard + SmartTodoKanban
 * from the SmartTodo Code Page so the workspace widget renders the full
 * Kanban (drag-drop + pin + multi-select + orientation) with parity.
 *
 * R5 FR-01 / task 002 (2026-08-15) — hoisted the 13-file rich Kanban subtree
 * from `src/solutions/LegalWorkspace/src/components/SmartToDo/` (see
 * `./SmartToDo/`). The rich subtree's card (`RecordCardShell`-based, ITodo-typed)
 * COLLIDES by name with the widget card below, and the widget `KanbanCard`
 * export is consumed externally by the SmartTodo Code Page — so the rich card
 * is re-exported under the ALIAS `SmartToDoKanbanCard` / `ISmartToDoKanbanCardProps`
 * rather than clobbering the bare `KanbanCard` name. See task-002-hoist notes.
 */

// ── Pre-existing widget card + composer (SmartTodo Code Page hoist, R4-102) ──
export { KanbanCard } from './KanbanCard';
export type { IKanbanCardProps } from './KanbanCard';

export { SmartTodoKanban } from './SmartTodoKanban';
export type { ISmartTodoKanbanProps } from './SmartTodoKanban';

// ── Shared top-bar (hoisted from the SmartTodo Code Page, R5 follow-up
//    2026-08-17) so the Code Page AND SmartTodoWidget render the IDENTICAL
//    Header (Filter · + New Task · overflow) + inline SearchFilter — a code page
//    and its widget must compose the same shared-lib components. ──────────────
export { Header, QUICK_ADD_TODO_EVENT } from './Header';
export type { HeaderProps, QuickAddTodoEventDetail } from './Header';
export { SearchFilter } from './SearchFilter';
export type { SearchFilterProps } from './SearchFilter';

// ── Rich Smart To Do subtree (LegalWorkspace hoist, R5 FR-01 / task 002) ─────
export {
  SmartToDo,
  LazyTodoAISummaryDialog,
  TodoAISummaryFallback,
  SmartToDoDialog,
  KanbanHeader,
  AddTodoBar,
  DismissedSection,
  ThresholdSettingsPopover,
  ThresholdSettings,
  TodoDetailPane,
  TodoAISummaryDialog,
  PriorityScoreCard,
  EffortScoreCard,
} from './SmartToDo';
export type {
  ISmartToDoProps,
  ISmartToDoDialogProps,
  IKanbanHeaderProps,
  IAddTodoBarProps,
  IDismissedSectionProps,
  IThresholdSettingsProps,
  ITodoDetailPaneProps,
  ITodoAISummaryDialogProps,
  IPriorityScoreCardProps,
  IEffortScoreCardProps,
} from './SmartToDo';

// Rich subtree card — ALIASED to avoid clobbering the externally-consumed
// widget `KanbanCard` export above (task 002 deviation; see notes).
export { KanbanCard as SmartToDoKanbanCard } from './SmartToDo';
export type { IKanbanCardProps as ISmartToDoKanbanCardProps } from './SmartToDo';
