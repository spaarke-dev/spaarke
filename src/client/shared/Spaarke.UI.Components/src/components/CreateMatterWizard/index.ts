/**
 * index.ts
 * Public barrel export for the CreateMatterWizard shared library component.
 *
 * Consumer usage:
 *   import { CreateMatterWizard } from './components/CreateMatterWizard';
 *
 * NOTE: This barrel intentionally does NOT re-export symbols that are already
 * exported from sibling barrels (FileUpload, LookupField, Wizard,
 * CreateRecordWizard, AiFieldTag). Consumers should import those from their
 * canonical source.
 */

// Primary entry point -- the wizard component
export { CreateMatterWizard, default } from './CreateMatterWizard';
export type { ICreateMatterWizardProps } from './CreateMatterWizard';

// Sub-components internal to the wizard (CreateRecordStep is matter-specific)
export { CreateRecordStep } from './CreateRecordStep';

// Task 024 -- matter-specific step components
export { AssignCounselStep } from './AssignCounselStep';
export {
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

// Types -- wizard (matter-specific only; shared types live in Wizard/index.ts
// and FileUpload/index.ts)
export type {
  IWizardDialogProps,
  IWizardStep,
  WizardAction,
  WizardStepId,
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
  FormAction,
} from './formTypes';

// Types -- Step 3 follow-on (matter-specific only; shared follow-on types
// live in CreateRecordWizard/index.ts)
export type { IAssignCounselStepProps } from './AssignCounselStep';

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
