/**
 * Barrel for @spaarke/smart-todo-components/types.
 *
 * Host-agnostic types — zero LegalWorkspace coupling.
 */

export type { ITodoRecord, IRegardingContext, IFeedSyncBridge, IWebApi } from './todo';
export type {
  TodoColumn,
  IKanbanTodoLike,
  IKanbanCardTodo,
  IKanbanColumn,
  IKanbanDataverseService,
  KanbanOrientation,
} from './kanban';

// R5 FR-01 / task 002 — host-agnostic domain types for the hoisted rich
// Smart To Do subtree (`components/SmartToDo/`).
export type {
  ITodo,
  PriorityLevel,
  EffortLevel,
  ITodoKanbanPreferences,
  ITodoMutationResult,
} from './entities';
export type {
  ITodoScoringPriorityFactor,
  ITodoScoringMultiplier,
  ITodoPriorityScore,
  ITodoEffortScore,
  ITodoScoringAction,
  ITodoScoringResult,
  ITodoScoringEventContext,
} from './todoScoringTypes';
