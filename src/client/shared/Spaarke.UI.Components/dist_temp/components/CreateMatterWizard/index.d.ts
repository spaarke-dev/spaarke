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
export { CreateMatterWizard, default } from './CreateMatterWizard';
export type { ICreateMatterWizardProps } from './CreateMatterWizard';
export { CreateRecordStep } from './CreateRecordStep';
export { AssignCounselStep } from './AssignCounselStep';
export { buildDefaultEmailSubject, buildDefaultEmailBody, } from './SendEmailStep';
export { MatterService, searchContacts, searchContactsAsLookup, searchMatterTypes, searchPracticeAreas, searchOrganizationsAsLookup, searchUsersAsLookup, fetchAiDraftSummary, streamAiDraftSummary, } from './matterService';
export type { IWizardDialogProps, IWizardStep, WizardAction, WizardStepId, } from './wizardTypes';
export type { ICreateRecordStepProps, ICreateMatterFormState, ICreateMatterFormErrors, IAiPrefillFields, IAiPrefillState, IAiPrefillRequest, IAiPrefillResponse, FormAction, } from './formTypes';
export type { IAssignCounselStepProps } from './AssignCounselStep';
export type { ICreateMatterResult, CreateMatterResultStatus, IFollowOnActions, IAssignCounselInput, IDraftSummaryInput, IAiDraftSummaryResponse, IContact, } from './matterService';
//# sourceMappingURL=index.d.ts.map