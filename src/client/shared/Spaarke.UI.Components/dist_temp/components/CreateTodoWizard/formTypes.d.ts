/**
 * formTypes.ts
 * Form state types for Create New To Do wizard.
 *
 * A To Do is a sprk_event record with sprk_todoflag=true.
 */
export interface ICreateTodoFormState {
    /** Title -- free text (required). Maps to sprk_eventname. */
    title: string;
    /** Due date -- ISO date string (optional). Maps to sprk_duedate. */
    dueDate: string;
    /** Priority -- Dataverse option set value (100000000=Low, 100000001=Normal, 100000002=High, 100000003=Urgent). */
    priority: number;
    /** Description -- free text, multi-line (optional). Maps to sprk_description. */
    description: string;
}
export declare const EMPTY_TODO_FORM: ICreateTodoFormState;
//# sourceMappingURL=formTypes.d.ts.map