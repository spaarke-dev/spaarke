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
export { LookupField } from './LookupField';
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
// SuccessConfirmation removed — shell handles success screen via IWizardSuccessConfig

// Task 024 — Service layer
export {
  MatterService,
  searchContacts,
  searchContactsAsLookup,
  searchMatterTypes,
  searchPracticeAreas,
  fetchAiDraftSummary,
} from './matterService';

// Types — wizard
// Note: IWizardState removed (navigation state is now in WizardShell/wizardShellTypes.ts).
// WizardAction retains only file-upload domain actions; navigation actions are WizardShellAction.
export type {
  IWizardDialogProps,
  IWizardStepperProps,
  IFileUploadZoneProps,
  IUploadedFileListProps,
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
// ISuccessConfirmationProps removed — SuccessConfirmation component deleted (T012)
export type {
  ICreateMatterResult,
  CreateMatterResultStatus,
  IFollowOnActions,
  IAssignCounselInput,
  IDraftSummaryInput,
  ISendEmailInput,
  IAiDraftSummaryResponse,
} from './matterService';
