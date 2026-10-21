// Ontology admin (spaarke-ontology-platform-r1 task 104; D-22, FR-56): the workspace tab shell (Rules and Classification, the R1
// areas; tasks 100 and 103 fill them) and the capability probe client the host uses to decide whether to register the tab.
export { OntologyAdminTab, ONTOLOGY_ADMIN_AREAS } from './OntologyAdminTab';
export type { OntologyAdminAreaId, OntologyAdminTabProps } from './OntologyAdminTab';
export { fetchOntologyAdminCapability, ONTOLOGY_ADMIN_CAPABILITIES_ROUTE } from './ontologyAdminCapability';
export type { OntologyAdminCapabilityOptions } from './ontologyAdminCapability';
// Task 103: the Classification area (triage categories) and the BFF service it reads and writes through.
export * from './Classification';
// Rules area (task 100; FR-57): the host builds the service from its authenticated fetch and passes `<RulesArea>` as the tab's `rules` slot.
export { RulesArea, createOntologyRulesService, ONTOLOGY_ADMIN_RULES_ROUTE } from './Rules';
export type { OntologyRulesService, OntologyRulesServiceOptions, RulesAreaProps } from './Rules';
// Rule wizard (task 102; D-22, D-149): the authoring service the host builds from its authenticated fetch and passes to `<RulesArea authoringService>`.
export * from './RuleWizard';
