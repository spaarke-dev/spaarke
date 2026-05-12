/**
 * playbookService.ts
 *
 * Loads playbook-related entities from Dataverse WebAPI.
 * Ported from AnalysisBuilder PCF (AnalysisBuilderApp.tsx lines 182-377).
 *
 * This service is used by the Analysis Launcher code page (not a PCF control),
 * so the webApi parameter is typed as `any` rather than
 * ComponentFramework.WebApi.
 */
import { ENTITY_NAMES, ID_FIELDS, RELATIONSHIP_NAMES, } from './types';
// ---------------------------------------------------------------------------
// Internal helpers
// ---------------------------------------------------------------------------
/** Shared OData filter + sort applied to every entity list query. */
const BASE_FILTER = '&$filter=statecode eq 0&$orderby=sprk_name';
// ---------------------------------------------------------------------------
// Individual entity loaders
// ---------------------------------------------------------------------------
/**
 * Load all active playbooks from sprk_analysisplaybook.
 *
 * @param webApi - Dataverse WebAPI accessor (Xrm.WebApi or equivalent).
 * @returns Resolved array of IPlaybook records.
 */
export async function loadPlaybooks(webApi) {
    const result = await webApi.retrieveMultipleRecords(ENTITY_NAMES.playbook, `?$select=${ID_FIELDS.playbook},sprk_name,sprk_description${BASE_FILTER}`);
    return result.entities.map((entity) => ({
        id: entity[ID_FIELDS.playbook],
        name: entity.sprk_name,
        description: entity.sprk_description || '',
        icon: 'Lightbulb', // No dedicated icon field in Dataverse
        isDefault: false,
    }));
}
/**
 * Load all active actions from sprk_analysisaction.
 *
 * @param webApi - Dataverse WebAPI accessor.
 * @returns Resolved array of IAction records.
 */
export async function loadActions(webApi) {
    const result = await webApi.retrieveMultipleRecords(ENTITY_NAMES.action, `?$select=${ID_FIELDS.action},sprk_name,sprk_description${BASE_FILTER}`);
    return result.entities.map((entity) => ({
        id: entity[ID_FIELDS.action],
        name: entity.sprk_name,
        description: entity.sprk_description || '',
        icon: 'Play', // No dedicated icon field in Dataverse
    }));
}
/**
 * Load all active skills from sprk_analysisskill.
 *
 * @param webApi - Dataverse WebAPI accessor.
 * @returns Resolved array of ISkill records.
 */
export async function loadSkills(webApi) {
    const result = await webApi.retrieveMultipleRecords(ENTITY_NAMES.skill, `?$select=${ID_FIELDS.skill},sprk_name,sprk_description${BASE_FILTER}`);
    return result.entities.map((entity) => ({
        id: entity[ID_FIELDS.skill],
        name: entity.sprk_name,
        description: entity.sprk_description || '',
        icon: 'Brain', // No dedicated icon field in Dataverse
        type: 'analysis', // No dedicated type field in Dataverse
    }));
}
/**
 * Load all active knowledge items from sprk_analysisknowledge.
 *
 * @param webApi - Dataverse WebAPI accessor.
 * @returns Resolved array of IKnowledge records.
 */
export async function loadKnowledge(webApi) {
    const result = await webApi.retrieveMultipleRecords(ENTITY_NAMES.knowledge, `?$select=${ID_FIELDS.knowledge},sprk_name,sprk_description${BASE_FILTER}`);
    return result.entities.map((entity) => ({
        id: entity[ID_FIELDS.knowledge],
        name: entity.sprk_name,
        description: entity.sprk_description || '',
        icon: 'Library', // No dedicated icon field in Dataverse
        source: 'dataverse', // No dedicated source field in Dataverse
    }));
}
/**
 * Load all active tools from sprk_analysistool.
 *
 * @param webApi - Dataverse WebAPI accessor.
 * @returns Resolved array of ITool records.
 */
export async function loadTools(webApi) {
    const result = await webApi.retrieveMultipleRecords(ENTITY_NAMES.tool, `?$select=${ID_FIELDS.tool},sprk_name,sprk_description${BASE_FILTER}`);
    return result.entities.map((entity) => ({
        id: entity[ID_FIELDS.tool],
        name: entity.sprk_name,
        description: entity.sprk_description || '',
        icon: 'Wrench', // No dedicated icon field in Dataverse
        toolType: 'api', // No dedicated toolType field in Dataverse
    }));
}
// ---------------------------------------------------------------------------
// Playbook scope loader (N:N expand)
// ---------------------------------------------------------------------------
/**
 * Load the scope IDs (skills, knowledge, tools, actions) linked to a specific
 * playbook via its N:N relationships.
 *
 * Uses a single retrieveRecord call with $expand so all four relationship sets
 * are fetched in one round-trip — matching the pattern in AnalysisBuilderApp.
 *
 * @param webApi     - Dataverse WebAPI accessor.
 * @param playbookId - GUID of the sprk_analysisplaybook record.
 * @returns Resolved IPlaybookScopes containing arrays of related entity IDs.
 */
export async function loadPlaybookScopes(webApi, playbookId) {
    const scopes = {
        actionIds: [],
        skillIds: [],
        knowledgeIds: [],
        toolIds: [],
    };
    const expandQuery = `?$expand=` +
        `${RELATIONSHIP_NAMES.playbookSkill}($select=${ID_FIELDS.skill}),` +
        `${RELATIONSHIP_NAMES.playbookKnowledge}($select=${ID_FIELDS.knowledge}),` +
        `${RELATIONSHIP_NAMES.playbookTool}($select=${ID_FIELDS.tool}),` +
        `${RELATIONSHIP_NAMES.playbookAction}($select=${ID_FIELDS.action})`;
    const playbook = await webApi.retrieveRecord(ENTITY_NAMES.playbook, playbookId, expandQuery);
    // Extract skill IDs
    if (playbook[RELATIONSHIP_NAMES.playbookSkill] && Array.isArray(playbook[RELATIONSHIP_NAMES.playbookSkill])) {
        scopes.skillIds = playbook[RELATIONSHIP_NAMES.playbookSkill].map(s => s[ID_FIELDS.skill]);
    }
    // Extract knowledge IDs
    if (playbook[RELATIONSHIP_NAMES.playbookKnowledge] && Array.isArray(playbook[RELATIONSHIP_NAMES.playbookKnowledge])) {
        scopes.knowledgeIds = playbook[RELATIONSHIP_NAMES.playbookKnowledge].map(k => k[ID_FIELDS.knowledge]);
    }
    // Extract tool IDs
    if (playbook[RELATIONSHIP_NAMES.playbookTool] && Array.isArray(playbook[RELATIONSHIP_NAMES.playbookTool])) {
        scopes.toolIds = playbook[RELATIONSHIP_NAMES.playbookTool].map(t => t[ID_FIELDS.tool]);
    }
    // Extract action IDs (note: relationship name follows a different pattern)
    if (playbook[RELATIONSHIP_NAMES.playbookAction] && Array.isArray(playbook[RELATIONSHIP_NAMES.playbookAction])) {
        scopes.actionIds = playbook[RELATIONSHIP_NAMES.playbookAction].map(a => a[ID_FIELDS.action]);
    }
    return scopes;
}
/**
 * Load all five entity types in parallel.
 *
 * All five requests are dispatched concurrently via Promise.all so total load
 * time is bounded by the slowest individual query rather than the sum.
 *
 * @param webApi - Dataverse WebAPI accessor.
 * @returns Resolved IPlaybookData containing all entity arrays.
 */
export async function loadAllData(webApi) {
    const [playbooks, actions, skills, knowledge, tools] = await Promise.all([
        loadPlaybooks(webApi),
        loadActions(webApi),
        loadSkills(webApi),
        loadKnowledge(webApi),
        loadTools(webApi),
    ]);
    return { playbooks, actions, skills, knowledge, tools };
}
//# sourceMappingURL=playbookService.js.map