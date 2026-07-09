/**
 * AddTodoFollowOnStep.tsx
 * NET-NEW follow-on step: create a To Do (`sprk_todo`) linked to the
 * just-created record.
 *
 * There is no existing follow-on that wires To Do creation — this is the third
 * Next Step the new wizards offer (Send Email / Add To Do / Assign Work) per
 * design.md §5.9 + spec FR-13 / FR-15.
 *
 * Two parts:
 *   1. `AddTodoFollowOnStep` — a controlled FORM that collects To Do fields.
 *      Reuses the shared `CreateTodoStep` form (Title / Due Date / Assignee /
 *      Priority / Effort / Notes) rather than re-implementing it (§11
 *      reuse-first). Like every follow-on form step, it only collects; the
 *      record is created at finish time once the child record's GUID is known.
 *   2. `createTodoRegardingChild` — the create handler the wizard calls in its
 *      `onFinish`. It delegates to the ADR-024-compliant
 *      `TodoService.createTodo`, seeding the To Do's regarding to the
 *      JUST-CREATED CHILD record (NOT the original host) via
 *      `applyResolverFields`. This is the single, shared mechanism — no
 *      duplicated ad-hoc resolver logic (ADR-024 MUST NOT).
 *
 * The child entity type MUST be one of the `sprk_todo` regarding targets
 * (`TODO_REGARDING_CATALOG`, e.g. sprk_event, sprk_invoice, sprk_matter, …).
 * `createTodo` returns a failure result (never throws) for unsupported types.
 *
 * Constraints: Fluent v9 only (via CreateTodoStep), semantic tokens (ADR-021),
 * no PCF/solution imports (ADR-012).
 *
 * @see .claude/adr/ADR-024-polymorphic-resolver-pattern.md
 * @see CreateTodoWizard/todoService.ts — createTodo → applyResolverFields
 */
import * as React from 'react';
import { CreateTodoStep } from '../../CreateTodoWizard/CreateTodoStep';
import type { ICreateTodoFormState, AssociationResult } from '../../CreateTodoWizard/formTypes';
import { TodoService } from '../../CreateTodoWizard/todoService';
import type { ICreateTodoResult } from '../../CreateTodoWizard/todoService';
import type { IDataService } from '../../../types/serviceInterfaces';
import type { ILookupItem } from '../../../types/LookupTypes';

// ---------------------------------------------------------------------------
// Step props
// ---------------------------------------------------------------------------

export interface IAddTodoFollowOnStepProps {
  /** Data service (forwarded to CreateTodoStep — assignee search etc.). */
  dataService: IDataService;
  /** Current To Do form values (controlled by the parent wizard). */
  formValues: ICreateTodoFormState;
  /** Called whenever any field in the To Do form changes. */
  onFormValuesChange: (values: ICreateTodoFormState) => void;
  /**
   * Search callback for the Assignee lookup — returns OOB `contact` records
   * (per the CreateTodo UAT migration of `sprk_assignedto` to contact).
   */
  onSearchAssignees: (query: string) => Promise<ILookupItem[]>;
  /**
   * Optional validity callback. To Do is valid once Title is non-empty. The
   * follow-on step is optional/skippable regardless — this is surfaced only if
   * a wizard chooses to gate advancement on it.
   */
  onValidChange?: (isValid: boolean) => void;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const AddTodoFollowOnStep: React.FC<IAddTodoFollowOnStepProps> = ({
  dataService,
  formValues,
  onFormValuesChange,
  onSearchAssignees,
  onValidChange,
}) => {
  const handleValidChange = React.useCallback(
    (isValid: boolean) => {
      onValidChange?.(isValid);
    },
    [onValidChange]
  );

  return (
    <CreateTodoStep
      dataService={dataService}
      onSearchUsers={onSearchAssignees}
      onValidChange={handleValidChange}
      onFormValues={onFormValuesChange}
      initialFormValues={formValues}
    />
  );
};

AddTodoFollowOnStep.displayName = 'AddTodoFollowOnStep';

// ---------------------------------------------------------------------------
// Create handler (called by the wizard's onFinish)
// ---------------------------------------------------------------------------

/**
 * Reference to the just-created CHILD record the To Do should be regarding.
 * NOT the original host record — the To Do links to the record this wizard
 * just created (e.g. the new Event / Invoice), per design.md §5.9 + FR-15.
 */
export type CreatedChildRef = AssociationResult;

/**
 * Create a `sprk_todo` regarding the just-created child record.
 *
 * Delegates to the ADR-024-compliant `TodoService.createTodo`, which populates
 * the entity-specific `@odata.bind` lookup + all resolver fields via the shared
 * `applyResolverFields` primitive. The `child` becomes the To Do's regarding
 * triple — so the created To Do is regarding the new Event/Invoice/etc., not
 * the host record.
 *
 * Never throws — returns `ICreateTodoResult` with `success: false` +
 * `errorMessage` on failure (e.g. unsupported regarding entity type), matching
 * the follow-on "warnings, never fatal" convention of the other wizards.
 *
 * @param dataService  Injected data access (backs `TodoService`).
 * @param formValues   To Do fields collected by `AddTodoFollowOnStep`.
 * @param child        The just-created child record (regarding target). MUST be
 *                     one of the `sprk_todo` regarding targets.
 */
export async function createTodoRegardingChild(
  dataService: IDataService,
  formValues: ICreateTodoFormState,
  child: CreatedChildRef
): Promise<ICreateTodoResult> {
  const todoService = new TodoService(dataService);
  return todoService.createTodo(formValues, {
    entityType: child.entityType,
    recordId: child.recordId,
    recordName: child.recordName,
  });
}
