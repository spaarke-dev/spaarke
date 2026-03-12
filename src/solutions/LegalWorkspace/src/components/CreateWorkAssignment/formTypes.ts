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
// Step ID mappings for dynamic step injection
// ---------------------------------------------------------------------------

export const WA_FOLLOW_ON_STEP_ID_MAP: Record<WorkAssignmentFollowOnId, string> = {
  'assign-work': 'followon-wa-assign-work',
  'send-email': 'followon-wa-send-email',
  'create-event': 'followon-wa-create-event',
};

export const WA_FOLLOW_ON_STEP_LABEL_MAP: Record<WorkAssignmentFollowOnId, string> = {
  'assign-work': 'Assign Work',
  'send-email': 'Send Email',
  'create-event': 'Create Event',
};

export const WA_FOLLOW_ON_CANONICAL_ORDER = [
  'followon-wa-assign-work',
  'followon-wa-send-email',
  'followon-wa-create-event',
];
