/**
 * index.ts
 * Public barrel export for the CreateWorkAssignmentWizard shared library component.
 *
 * Consumer usage:
 *   import { WorkAssignmentWizardDialog } from './components/CreateWorkAssignmentWizard';
 */

// Primary entry point -- the wizard component
export { default as WorkAssignmentWizardDialog } from './WorkAssignmentWizardDialog';
export type { IWorkAssignmentWizardDialogProps } from './WorkAssignmentWizardDialog';

// Step components (available for testing or extension)
export { SelectWorkStep } from './SelectWorkStep';
export type { ISelectWorkStepProps } from './SelectWorkStep';

export { AddFilesStep } from './AddFilesStep';
export type { IAddFilesStepProps } from './AddFilesStep';

export { EnterInfoStep } from './EnterInfoStep';
export type { IEnterInfoStepProps } from './EnterInfoStep';

export { NextStepsSelectionStep } from './NextStepsSelectionStep';
export type { INextStepsSelectionStepProps } from './NextStepsSelectionStep';

export { AssignWorkStep } from './AssignWorkStep';
export type { IAssignWorkStepProps } from './AssignWorkStep';

export { CreateFollowOnEventStep } from './CreateFollowOnEventStep';
export type { ICreateFollowOnEventStepProps } from './CreateFollowOnEventStep';

// Service layer
export {
  WorkAssignmentService,
  searchMatterTypes,
  searchPracticeAreas,
  searchContactsAsLookup,
  searchOrganizationsAsLookup,
  searchUsersAsLookup,
} from './workAssignmentService';

// Types -- form state
export type {
  ICreateWorkAssignmentFormState,
  IAssignWorkState,
  ICreateFollowOnEventState,
  ICreateWorkAssignmentResult,
  WorkAssignmentFollowOnId,
} from './formTypes';
export {
  EMPTY_WORK_ASSIGNMENT_FORM,
  EMPTY_ASSIGN_WORK_STATE,
  EMPTY_FOLLOW_ON_EVENT_STATE,
  WA_FOLLOW_ON_STEP_ID_MAP,
  WA_FOLLOW_ON_STEP_LABEL_MAP,
  WA_FOLLOW_ON_CANONICAL_ORDER,
} from './formTypes';
