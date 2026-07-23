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
export { AssignResourcesStep } from './AssignResourcesStep';
export { RecipientField } from './RecipientField';
export { DraftSummaryStep } from './DraftSummaryStep';
// SendEmailStep + buildDefaultEmailSubject/buildDefaultEmailBody removed (email-r4
// task 061): the LegalWorkspace CreateMatter email-step fork was retired. The
// canonical wizard email step is `SendEmailFollowOnStep` from @spaarke/ui-components
// (over the generic `EmailStep`); the Matter wizard composes email via its
// config-driven followOn model in WizardDialog.tsx (buildEmailSubject/buildEmailBody).
// SuccessConfirmation removed — shell handles success screen via IWizardSuccessConfig

// Task 024 — Service layer
export {
  MatterService,
  searchContacts,
  searchContactsAsLookup,
  searchMatterTypes,
  searchPracticeAreas,
  searchUsersAsLookup,
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
export type { IAssignResourcesStepProps } from './AssignResourcesStep';
export type { IRecipientItem, IRecipientFieldProps } from './RecipientField';
export type { IDraftSummaryStepProps } from './DraftSummaryStep';
// ISendEmailStepProps removed — SendEmailStep fork retired (email-r4 task 061)
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
