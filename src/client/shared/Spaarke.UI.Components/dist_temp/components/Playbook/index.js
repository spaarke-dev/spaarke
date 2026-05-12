/**
 * Playbook component library — shared public API.
 *
 * Reusable components, services, and types for interacting with the
 * AI Playbook data model in Dataverse. Used by Analysis Builder,
 * Document Upload Wizard, LegalWorkspace, and any future consumer.
 *
 * Solution-specific components (DocumentUploadStep, FollowUpActionsStep,
 * PlaybookSelector) remain in their respective solution folders.
 */
export { ENTITY_NAMES, RELATIONSHIP_NAMES, ID_FIELDS } from './types';
// Services
export { loadPlaybooks, loadActions, loadSkills, loadKnowledge, loadTools, loadPlaybookScopes, loadAllData, } from './playbookService';
export { createAnalysis, associateScopes, createAndAssociate } from './analysisService';
// Components
export { PlaybookCardGrid } from './PlaybookCardGrid';
export { ScopeList } from './ScopeList';
export { ScopeConfigurator } from './ScopeConfigurator';
//# sourceMappingURL=index.js.map