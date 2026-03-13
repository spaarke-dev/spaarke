/**
 * todoService.ts
 * To Do creation service for the Create New To Do wizard.
 *
 * Creates a sprk_event record in Dataverse with:
 *   - sprk_todoflag = true
 *   - sprk_todostatus = 0 (Open)
 *   - sprk_todosource = 0 (User)
 */

import type { ICreateTodoFormState } from './formTypes';
import type { IWebApi, WebApiEntity } from '../../types/xrm';

// ---------------------------------------------------------------------------
// Result types
// ---------------------------------------------------------------------------

export interface ICreateTodoResult {
  todoId?: string;
  todoName?: string;
  success: boolean;
  errorMessage?: string;
}

// ---------------------------------------------------------------------------
// TodoService class
// ---------------------------------------------------------------------------

export class TodoService {
  constructor(private readonly _webApi: IWebApi) {}

  /**
   * Create a sprk_event record marked as a To Do.
   */
  async createTodo(formValues: ICreateTodoFormState): Promise<ICreateTodoResult> {
    const entity: WebApiEntity = {
      sprk_eventname: formValues.title.trim(),
      sprk_priority: formValues.priority,
      sprk_todoflag: true,
      sprk_todostatus: 0,  // Open
      sprk_todosource: 0,  // User
    };

    if (formValues.description?.trim()) {
      entity['sprk_description'] = formValues.description.trim();
    }
    if (formValues.dueDate) {
      entity['sprk_duedate'] = formValues.dueDate;
    }

    try {
      const result = await this._webApi.createRecord('sprk_event', entity);
      return {
        todoId: result.id,
        todoName: formValues.title.trim(),
        success: true,
      };
    } catch (err) {
      console.error('[TodoService] createRecord error:', err);
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      const errObj = err as any;
      const message = errObj?.message || (err instanceof Error ? err.message : 'Unknown error');
      return {
        success: false,
        errorMessage: `Failed to create to do: ${message}`,
      };
    }
  }
}
