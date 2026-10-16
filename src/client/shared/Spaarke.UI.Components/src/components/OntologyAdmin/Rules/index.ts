// Ontology admin Rules area (spaarke-ontology-platform-r1 task 100; FR-57): list, read-only rule page, On/Off, Retire.
export { RulesArea } from './RulesArea';
export type { RulesAreaProps } from './RulesArea';
export { RuleList } from './RuleList';
export { RulePage } from './RulePage';
export { RetireRuleDialog, MAX_RETIRE_REASON_LENGTH, describeClosure } from './RetireRuleDialog';
export { createOntologyRulesService, ONTOLOGY_ADMIN_RULES_ROUTE } from './rulesService';
export type {
  OntologyRulesService,
  OntologyRulesServiceOptions,
  RuleDetail,
  RuleListResponse,
  RuleRetiredResult,
  RuleSummary,
  RulesFailure,
  RulesResult,
} from './rulesService';
