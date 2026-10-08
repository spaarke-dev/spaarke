/**
 * quickAddTodoPayload — the `sprk_todo` create payload of the three-field quick-add (title, due date, assigned to).
 *
 * ONE builder for both quick-add surfaces: `SmartTodoWidget` and the SmartTodo Code Page's `QUICK_ADD_TODO_EVENT`
 * listener (`src/solutions/SmartTodo/src/components/SmartToDo.tsx`), which each built the same payload inline
 * (spaarke-ontology-platform-r1 task 106).
 *
 * Why it moved here: `sprk_todo.sprk_duedate` is a Dataverse **Date Only** column (task 106). The Web API accepts only
 * `"YYYY-MM-DD"` for it and refuses a timestamp with HTTP 400. Both copies sent `toISOString()` of 23:59 LOCAL on the
 * picked day — refused now, and before the conversion the NEXT day in UTC from 20:00 Eastern. The date input's own
 * `"YYYY-MM-DD"` is the calendar day the user picked, so it is sent as it is.
 */

/** Input of {@link buildQuickAddTodoPayload}. */
export interface QuickAddTodoInput {
  /** Trimmed, non-empty title. */
  title: string;
  /** The picked calendar day, `"YYYY-MM-DD"` (a date input's value), or empty/undefined for no due date. */
  dueDate?: string;
  /** Contact GUID for Assigned To, or empty/undefined to leave it unset. */
  assignedToContactId?: string;
}

/** The Web API create payload for a quick-added To Do. */
export function buildQuickAddTodoPayload(input: QuickAddTodoInput): Record<string, unknown> {
  const payload: Record<string, unknown> = { sprk_name: input.title };
  if (input.assignedToContactId) {
    // sprk_assignedto targets the OOB `contact` table (set `contacts`). The bind key MUST be the navigation property
    // name `sprk_AssignedTo` (PascalCase, from EntityDefinitions metadata): the lowercase column name fails with
    // "An undeclared property 'sprk_assignedto' which only has property annotations…" (UAT 2026-06-22 round 13).
    payload['sprk_AssignedTo@odata.bind'] = `/contacts(${input.assignedToContactId})`;
  }
  if (input.dueDate) {
    payload['sprk_duedate'] = input.dueDate;
  }
  return payload;
}
