/**
 * formTypes.ts
 * Form state types for the Work Assignment wizard.
 *
 * Entity: sprk_workassignment
 */

// ---------------------------------------------------------------------------
// Main form state
// ---------------------------------------------------------------------------

export interface ICreateWorkAssignmentFormState {
  /** Step 1: Record Type — which entity type the work relates to. */
  recordType: '' | 'matter' | 'project' | 'invoice' | 'event';
  /** Step 1: Selected record GUID. */
  recordId: string;
  /** Step 1: Selected record display name. */
  recordName: string;
  /** Step 1: If true, no specific record is linked. */
  assignWithoutRecord: boolean;

  /** Step 3: Work assignment name (required). Maps to sprk_name. */
  name: string;
  /** Step 3: Description. Maps to sprk_description. */
  description: string;
  /** Step 3: Matter Type lookup GUID. */
  matterTypeId: string;
  /** Step 3: Matter Type display name. */
  matterTypeName: string;
  /** Step 3: Practice Area lookup GUID. */
  practiceAreaId: string;
  /** Step 3: Practice Area display name. */
  practiceAreaName: string;
  /** Step 3: Priority option set (100000000-100000003). */
  priority: number;
  /** Step 3: Response Due Date — ISO date string. Maps to sprk_responseduedate. */
  responseDueDate: string;
}

export const EMPTY_WORK_ASSIGNMENT_FORM: ICreateWorkAssignmentFormState = {
  recordType: '',
  recordId: '',
  recordName: '',
  assignWithoutRecord: false,
  name: '',
  description: '',
  matterTypeId: '',
  matterTypeName: '',
  practiceAreaId: '',
  practiceAreaName: '',
  priority: 100000001, // Normal
  responseDueDate: '',
};

// ---------------------------------------------------------------------------
// Follow-on action identifiers
// ---------------------------------------------------------------------------

export type WorkAssignmentFollowOnId = 'assign-work' | 'send-email' | 'create-event';

// ---------------------------------------------------------------------------
// Follow-on: Assign Work state
// ---------------------------------------------------------------------------

export interface IAssignWorkState {
  assignedAttorneyId: string;
  assignedAttorneyName: string;
  assignedParalegalId: string;
  assignedParalegalName: string;
  assignedLawFirmId: string;
  assignedLawFirmName: string;
  assignedLawFirmAttorneyId: string;
  assignedLawFirmAttorneyName: string;
  notifyResources: boolean;
}

export const EMPTY_ASSIGN_WORK_STATE: IAssignWorkState = {
  assignedAttorneyId: '',
  assignedAttorneyName: '',
  assignedParalegalId: '',
  assignedParalegalName: '',
  assignedLawFirmId: '',
  assignedLawFirmName: '',
  assignedLawFirmAttorneyId: '',
  assignedLawFirmAttorneyName: '',
  notifyResources: false,
};

// ---------------------------------------------------------------------------
// Follow-on: Create Event state
// ---------------------------------------------------------------------------

export interface ICreateFollowOnEventState {
  eventName: string;
  eventDescription: string;
  eventPriority: number;
  eventDueDate: string;
  eventFinalDueDate: string;
  assignedToId: string;
  assignedToName: string;
  // R3 (smart-todo-decoupling-r3, task 031): The `addTodo` flag was removed
  // here per FR-15 / OS-1. The legacy "Add a To Do" checkbox wrote
  // `sprk_event.sprk_todoflag=true`; that column is being dropped from the
  // schema. To Dos are now first-class `sprk_todo` records created via
  // CreateTodoWizard.
}

export const EMPTY_FOLLOW_ON_EVENT_STATE: ICreateFollowOnEventState = {
  eventName: 'Assign Work',
  eventDescription: '',
  eventPriority: 100000001,
  eventDueDate: '',
  eventFinalDueDate: '',
  assignedToId: '',
  assignedToName: '',
};

// ---------------------------------------------------------------------------
// Result type
// ---------------------------------------------------------------------------

export interface ICreateWorkAssignmentResult {
  status: 'success' | 'partial' | 'error';
  workAssignmentId?: string;
  workAssignmentName?: string;
  errorMessage?: string;
  warnings: string[];
}

// ---------------------------------------------------------------------------
// Follow-on step injection
// ---------------------------------------------------------------------------
// The per-wizard `WA_FOLLOW_ON_STEP_ID_MAP` / `_STEP_LABEL_MAP` /
// `_CANONICAL_ORDER` constants were removed when this wizard migrated onto the
// shared WizardFollowOns module (task 023). Dynamic step ids are now derived by
// `followOnStepId(cardId)` from the card config; canonical ordering follows the
// `FollowOnCardConfig[]` array order. See WorkAssignmentWizardDialog.tsx.
