/**
 * index.ts
 * Public barrel export for the CreateMatterWizard shared library component.
 *
 * Consumer usage:
 *   import { CreateMatterWizard } from './components/CreateMatterWizard';
 */

// Primary entry point -- the wizard component
export { CreateMatterWizard, default } from './CreateMatterWizard';
export type { ICreateMatterWizardProps } from './CreateMatterWizard';

// Sub-components (available for testing or extension)
export { WizardStepper } from './WizardStepper';
export { FileUploadZone } from './FileUploadZone';
export { UploadedFileList } from './UploadedFileList';
export { CreateRecordStep } from './CreateRecordStep';
export { LookupField } from './LookupField';
export { AiFieldTag } from './AiFieldTag';

// Task 024 -- Step 3 + follow-on step components
export { NextStepsStep, FOLLOW_ON_STEP_ID_MAP, FOLLOW_ON_STEP_LABEL_MAP } from './NextStepsStep';
export { AssignCounselStep } from './AssignCounselStep';
export { AssignResourcesStep } from './AssignResourcesStep';
export { RecipientField } from './RecipientField';
export { DraftSummaryStep } from './DraftSummaryStep';
export {
  SendEmailStep,
  buildDefaultEmailSubject,
  buildDefaultEmailBody,
} from './SendEmailStep';

// Service layer
export {
  MatterService,
  searchContacts,
  searchContactsAsLookup,
  searchMatterTypes,
  searchPracticeAreas,
  searchOrganizationsAsLookup,
  searchUsersAsLookup,
  fetchAiDraftSummary,
  streamAiDraftSummary,
} from './matterService';

// Types -- wizard
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

// Types -- Step 2 form
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

// Types -- Step 3 follow-on
export type {
  FollowOnActionId,
  IFollowOnCardDef,
  INextStepsStepProps,
} from './NextStepsStep';
export type { IAssignCounselStepProps } from './AssignCounselStep';
export type { IAssignResourcesStepProps } from './AssignResourcesStep';
export type { IRecipientItem, IRecipientFieldProps } from './RecipientField';
export type { IDraftSummaryStepProps } from './DraftSummaryStep';
export type { ISendEmailStepProps } from './SendEmailStep';

// Types -- service result
export type {
  ICreateMatterResult,
  CreateMatterResultStatus,
  IFollowOnActions,
  IAssignCounselInput,
  IDraftSummaryInput,
  IAiDraftSummaryResponse,
  IContact,
} from './matterService';
