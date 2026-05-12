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
// Sub-components internal to the wizard (CreateRecordStep is matter-specific)
export { CreateRecordStep } from './CreateRecordStep';
// Task 024 -- matter-specific step components
export { AssignCounselStep } from './AssignCounselStep';
export { buildDefaultEmailSubject, buildDefaultEmailBody, } from './SendEmailStep';
// Service layer
export { MatterService, searchContacts, searchContactsAsLookup, searchMatterTypes, searchPracticeAreas, searchOrganizationsAsLookup, searchUsersAsLookup, fetchAiDraftSummary, streamAiDraftSummary, } from './matterService';
//# sourceMappingURL=index.js.map