/**
 * Playbook component library — public API.
 *
 * Shared components, services, and types for interacting with the
 * AI Playbook data model in Dataverse. Used by both the Analysis Builder
 * code page and the Quick Start Playbook Wizards.
 */

// Types
export type {
  IPlaybook,
  IScopeItem,
  IAction,
  ISkill,
  IKnowledge,
  ITool,
  IPlaybookScopes,
  IAnalysisConfig,
  IFollowUpAction,
  ScopeTabId,
} from "./types";

export {
  ENTITY_NAMES,
  RELATIONSHIP_NAMES,
  ID_FIELDS,
} from "./types";

// Services
export {
  loadPlaybooks,
  loadActions,
  loadSkills,
  loadKnowledge,
  loadTools,
  loadPlaybookScopes,
  loadAllData,
} from "./playbookService";

export type { IPlaybookData } from "./playbookService";

export {
  createAnalysis,
  associateScopes,
  createAndAssociate,
} from "./analysisService";

// Components
export { PlaybookCardGrid } from "./PlaybookCardGrid";
export { ScopeList } from "./ScopeList";
export { ScopeConfigurator } from "./ScopeConfigurator";
export { DocumentUploadStep } from "./DocumentUploadStep";
export { FollowUpActionsStep } from "./FollowUpActionsStep";
