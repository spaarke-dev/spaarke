/**
 * CreateRecordWizard barrel export.
 *
 * Provides the reusable multi-step record creation wizard and its
 * supporting types. Entity-specific wizards import from here to
 * get the orchestration layer.
 */

// Main component
export { CreateRecordWizard } from './CreateRecordWizard';

// Follow-on steps UI
export {
  NextStepsStep,
  FOLLOW_ON_STEP_ID_MAP,
  FOLLOW_ON_STEP_LABEL_MAP,
  FOLLOW_ON_CANONICAL_ORDER,
} from './FollowOnSteps';

// Step components (for direct use if needed)
export { AssignWorkFollowOnStep, WORK_ASSIGNMENT_PRIORITY } from './steps/AssignWorkFollowOnStep';
export { AssignResourcesStep } from './steps/AssignResourcesStep';
export { DraftSummaryStep } from './steps/DraftSummaryStep';
export { SendEmailStep } from './steps/SendEmailStep';
export { RecipientField } from './steps/RecipientField';

// Types
export type {
  ICreateRecordWizardConfig,
  ICreateRecordWizardProps,
  IFinishContext,
  IFollowOnState,
  IAssignWorkFollowOnState,
  IEntityInfoStep,
  IAssociateToStepConfig,
  FollowOnActionId,
  IRecipientItem,
  SearchCallback,
  AssociationResult,
  EntityTypeOption,
} from './types';

// Step prop types
export type { IAssignWorkFollowOnStepProps, WorkAssignmentPriorityValue } from './steps/AssignWorkFollowOnStep';
export type { IAssignResourcesStepProps } from './steps/AssignResourcesStep';
export type { IDraftSummaryStepProps } from './steps/DraftSummaryStep';
export type { ISendEmailStepProps } from './steps/SendEmailStep';
export type { IRecipientFieldProps } from './steps/RecipientField';
export type { INextStepsStepProps } from './FollowOnSteps';
