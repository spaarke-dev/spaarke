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
// ---------------------------------------------------------------------------
// TodoService class
// ---------------------------------------------------------------------------
export class TodoService {
    constructor(_dataService) {
        this._dataService = _dataService;
    }
    /**
     * Create a sprk_event record marked as a To Do.
     *
     * CRITICAL: sprk_todoflag MUST be set to true. This is what distinguishes
     * a To Do from a regular Event in Dataverse.
     */
    async createTodo(formValues) {
        const entity = {
            sprk_eventname: formValues.title.trim(),
            sprk_priority: formValues.priority,
            sprk_todoflag: true,
            sprk_todostatus: 0, // Open
            sprk_todosource: 0, // User
        };
        if (formValues.description?.trim()) {
            entity['sprk_description'] = formValues.description.trim();
        }
        if (formValues.dueDate) {
            entity['sprk_duedate'] = formValues.dueDate;
        }
        try {
            // IDataService.createRecord returns Promise<string> (just the id)
            const todoId = await this._dataService.createRecord('sprk_event', entity);
            return {
                todoId,
                todoName: formValues.title.trim(),
                success: true,
            };
        }
        catch (err) {
            console.error('[TodoService] createRecord error:', err);
            // eslint-disable-next-line @typescript-eslint/no-explicit-any
            const errObj = err;
            const message = errObj?.message || (err instanceof Error ? err.message : 'Unknown error');
            return {
                success: false,
                errorMessage: `Failed to create to do: ${message}`,
            };
        }
    }
}
//# sourceMappingURL=todoService.js.map