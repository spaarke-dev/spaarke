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
// NOTE: The local NextStepsStep.tsx + SendEmailStep.tsx were duplicate copies of
// the shared Next-Steps / email flow. CreateMatterWizard renders its follow-on
// cards through CreateRecordWizard (now backed by the shared WizardFollowOns
// module, design.md §5.9), so those local copies were dead code and were
// deleted in visual-host-create-button-r1 task 022. The unused
// buildDefaultEmailSubject / buildDefaultEmailBody helpers went with them —
// CreateMatterWizard.tsx builds its email subject/body inline via the
// CreateRecordWizard config (buildEmailSubject / buildEmailBody).

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
export type { IWizardDialogProps, IWizardStep, WizardAction, WizardStepId } from './wizardTypes';

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
