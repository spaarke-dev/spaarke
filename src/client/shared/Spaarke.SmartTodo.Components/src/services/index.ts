/**
 * @spaarke/smart-todo-components/services — the Smart To Do data service (#2972).
 *
 * One copy of the to-do and preference reads/writes for every host. Hosts inject the Xrm.WebApi client and the
 * create- and status-update-through-BFF functions; LegalWorkspace's `DataverseService` extends `TodoDataverseService`. The query builders
 * and result helpers stay internal (`./todoQueries`, `./todoServiceResult`).
 */

export { TodoDataverseService } from './TodoDataverseService';
export type { ITodoWebApi, CreateTodoRecord, UpdateTodoRecord, TodoStatus } from './TodoDataverseService';
export type { ITodoRegardingFilter, TodoListScope } from './todoQueries';
export type { ITodoServiceResult, ITodoServiceError } from './todoServiceResult';
