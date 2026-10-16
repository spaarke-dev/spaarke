// Ontology admin (spaarke-ontology-platform-r1 task 104; D-22, FR-56): the workspace tab shell (Rules and Classification, the R1
// areas; tasks 100 and 103 fill them) and the capability probe client the host uses to decide whether to register the tab.
export { OntologyAdminTab, ONTOLOGY_ADMIN_AREAS } from './OntologyAdminTab';
export type { OntologyAdminAreaId, OntologyAdminTabProps } from './OntologyAdminTab';
export { fetchOntologyAdminCapability, ONTOLOGY_ADMIN_CAPABILITIES_ROUTE } from './ontologyAdminCapability';
export type { OntologyAdminCapabilityOptions } from './ontologyAdminCapability';
