/**
 * index.ts
 * Public barrel export for the CreateMatter wizard components.
 *
 * Consumer usage:
 *   import { WizardDialog } from './components/CreateMatter';
 */

// Primary entry point — the wizard dialog shell
export { WizardDialog } from './WizardDialog';
export type { IWizardDialogPropsInternal } from './WizardDialog';

// Sub-components (available for testing or extension)
export { WizardStepper } from './WizardStepper';
export { FileUploadZone } from './FileUploadZone';
export { UploadedFileList } from './UploadedFileList';
export { CreateRecordStep } from './CreateRecordStep';
export { AiFieldTag } from './AiFieldTag';

// Task 024 — Step 3 + follow-on step components
export { NextStepsStep, FOLLOW_ON_STEP_ID_MAP, FOLLOW_ON_STEP_LABEL_MAP } from './NextStepsStep';
export { AssignCounselStep } from './AssignCounselStep';
export { DraftSummaryStep } from './DraftSummaryStep';
export {
  SendEmailStep,
  buildDefaultEmailSubject,
  buildDefaultEmailBody,
} from './SendEmailStep';
export { SuccessConfirmation } from './SuccessConfirmation';

// Task 024 — Service layer
export { MatterService, searchContacts, fetchAiDraftSummary } from './matterService';

// Types — wizard
export type {
  IWizardDialogProps,
  IWizardStepperProps,
  IFileUploadZoneProps,
  IUploadedFileListProps,
  IWizardState,
  IWizardStep,
  IUploadedFile,
  IFileValidationError,
  WizardAction,
  WizardStepId,
  WizardStepStatus,
  UploadedFileType,
  AcceptedMimeType,
} from './wizardTypes';

// Types — Step 2 form (task 023)
export type {
  ICreateRecordStepProps,
  ICreateMatterFormState,
  ICreateMatterFormErrors,
  IAiPrefillFields,
  IAiPrefillState,
  IAiPrefillRequest,
  IAiPrefillResponse,
  AiPrefillStatus,
  MatterType,
  PracticeArea,
  FormAction,
} from './formTypes';

// Types — Step 3 follow-on (task 024)
export type {
  FollowOnActionId,
  IFollowOnCardDef,
  INextStepsStepProps,
} from './NextStepsStep';
export type { IAssignCounselStepProps } from './AssignCounselStep';
export type { IDraftSummaryStepProps } from './DraftSummaryStep';
export type { ISendEmailStepProps } from './SendEmailStep';
export type { ISuccessConfirmationProps } from './SuccessConfirmation';
export type {
  ICreateMatterResult,
  CreateMatterResultStatus,
  IFollowOnActions,
  IAssignCounselInput,
  IDraftSummaryInput,
  ISendEmailInput,
  IAiDraftSummaryResponse,
} from './matterService';
