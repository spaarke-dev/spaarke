/**
 * SmartTodo Toolbar — barrel export.
 *
 * @see ./ToolbarActions.ts for the action factory + types.
 */
export {
  createToolbarActions,
  resolveSelectedTodos,
  OPEN_TODOS_EVENT,
} from './ToolbarActions';
export type {
  ITodoActionWebApi,
  ActionResult,
  ToolbarActionContext,
  ToolbarActionHandlers,
  OpenTodosEventDetail,
} from './ToolbarActions';
