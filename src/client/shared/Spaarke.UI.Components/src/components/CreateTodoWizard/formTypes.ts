/**
 * formTypes.ts
 * Form state types for Create New To Do wizard (R3 — targets `sprk_todo`).
 *
 * Per smart-todo-decoupling-r3 spec FR-15 / OS-1: A To Do is a first-class
 * `sprk_todo` record. The legacy `sprk_event` + `sprk_todoflag=true` model
 * has been retired (no compat shim per NFR-12 / OS-1).
 *
 * Field mapping (vs. R1/R2 legacy):
 *   - Title   → `sprk_name`            (was `sprk_eventname`)
 *   - Notes   → `sprk_notes`           (was `sprk_eventtodo.sprk_todonotes`)
 *   - Description → `sprk_description` (unchanged)
 *   - Due Date → `sprk_duedate`        (unchanged)
 *   - Priority → `sprk_priorityscore`  (Integer 0-100; derived from a Priority
 *                                       choice selection via the shared
 *                                       `todoScoreMappings.ts` table — see
 *                                       smart-todo-r5 task 011 / FR-02)
 *   - Effort   → `sprk_effortscore`    (Integer 0-100; derived from an Effort
 *                                       choice selection via the shared
 *                                       `todoScoreMappings.ts` table, Option B
 *                                       quick-wins-first — task 011 / FR-03)
 *   - Assignee → `sprk_assignedto`     (Lookup → systemuser; new field)
 *
 * `priorityScore`/`effortScore` remain plain numbers on this form-state shape
 * (the write path `todoService.ts` uses is unchanged) — `CreateTodoStep.tsx`
 * now derives them from a Priority/Effort Dropdown rather than a raw 0-100
 * slider; the resolved CHOICE itself is local UI state in that component, not
 * part of this form-state contract.
 *
 * @see src/solutions/SpaarkeCore/entities/sprk_todo/entity-schema.md
 * @see projects/smart-todo-decoupling-r3/spec.md FR-15
 * @see ../../utils/todoScoreMappings.ts for the shared choice→score mapping (task 011)
 */

import type { AssociationResult } from '../AssociateToStep/types';

// ---------------------------------------------------------------------------
// Form state
// ---------------------------------------------------------------------------

/**
 * Wizard form state captured across the steps of the CreateTodo wizard.
 *
 * The regarding association is NOT in this state — it's captured by the
 * `AssociateToStep` wizard step into a separate `AssociationResult` object
 * that the wizard plumbs through to `TodoService.createTodo(...)`.
 */
export interface ICreateTodoFormState {
  /** Title — free text (required). Maps to `sprk_name`. */
  title: string;
  /** Notes — free text, multi-line (optional). Maps to `sprk_notes`. */
  notes: string;
  /** Due date — ISO date string (optional). Maps to `sprk_duedate`. */
  dueDate: string;
  /** Priority score — integer 0-100. Maps to `sprk_priorityscore`. */
  priorityScore: number;
  /** Effort score — integer 0-100. Maps to `sprk_effortscore`. */
  effortScore: number;
  /** Assignee (systemuser GUID) — optional. Maps to `sprk_assignedto`. */
  assignedToId: string;
  /** Assignee display name (for UI; not persisted directly). */
  assignedToName: string;
}

export const EMPTY_TODO_FORM: ICreateTodoFormState = {
  title: '',
  notes: '',
  dueDate: '',
  priorityScore: 50,
  effortScore: 50,
  assignedToId: '',
  assignedToName: '',
};

// ---------------------------------------------------------------------------
// Regarding shape used by todoService.createTodo
// ---------------------------------------------------------------------------

/**
 * Regarding triple captured by the AssociateToStep, in the canonical shape
 * already exported by that component. Re-exported here for ergonomic import
 * by todoService consumers.
 *
 * @see src/client/shared/Spaarke.UI.Components/src/components/AssociateToStep/types.ts
 */
export type { AssociationResult } from '../AssociateToStep/types';

/**
 * Optional "initial regarding" passed to the wizard via the
 * `ICreateTodoWizardProps.initialRegarding` prop.
 *
 * Used by launch contexts that already know the parent record (e.g., a Matter
 * detail-page ribbon button) to pre-fill the AssociateToStep selection and
 * advance past it. Task 032 fully wires this through; task 031 only adds the
 * prop surface so 032 can build on it.
 */
export type IInitialRegarding = AssociationResult;
