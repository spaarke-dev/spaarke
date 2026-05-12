/**
 * todoService.ts
 * To Do creation service for the Create New To Do wizard.
 *
 * Creates a sprk_event record in Dataverse with:
 *   - sprk_todoflag = true
 *   - sprk_todostatus = 0 (Open)
 *   - sprk_todosource = 0 (User)
 *
 * Dependencies are injected via constructor -- no solution-specific imports.
 */
import type { ICreateTodoFormState } from './formTypes';
import type { IDataService } from '../../types/serviceInterfaces';
export interface ICreateTodoResult {
    todoId?: string;
    todoName?: string;
    success: boolean;
    errorMessage?: string;
}
export declare class TodoService {
    private readonly _dataService;
    constructor(_dataService: IDataService);
    /**
     * Create a sprk_event record marked as a To Do.
     *
     * CRITICAL: sprk_todoflag MUST be set to true. This is what distinguishes
     * a To Do from a regular Event in Dataverse.
     */
    createTodo(formValues: ICreateTodoFormState): Promise<ICreateTodoResult>;
}
//# sourceMappingURL=todoService.d.ts.map