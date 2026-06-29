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

import {
  IPlaybook,
  IPlaybookConsumerMapping,
  IAction,
  ISkill,
  IKnowledge,
  ITool,
  IPlaybookScopes,
  ENTITY_NAMES,
  ID_FIELDS,
  RELATIONSHIP_NAMES,
} from './types';

// ---------------------------------------------------------------------------
// Internal helpers
// ---------------------------------------------------------------------------

/** Shared OData filter + sort applied to every entity list query. */
const BASE_FILTER = '&$filter=statecode eq 0&$orderby=sprk_name';

/**
 * Minimal subset of `Xrm.WebApi` (or PCF `ComponentFramework.WebApi`) used by this service.
 * Avoids depending on Xrm typings for a library that targets code pages.
 */
interface IPlaybookWebApi {
  retrieveMultipleRecords(entityName: string, options?: string): Promise<{ entities: Record<string, unknown>[] }>;
  retrieveRecord(entityName: string, id: string, options?: string): Promise<Record<string, unknown>>;
}

// ---------------------------------------------------------------------------
// Individual entity loaders
// ---------------------------------------------------------------------------

/**
 * Load all active playbooks from sprk_analysisplaybook.
 *
 * R7 task 094 / FR-18: when `options.includeConsumers === true`, ALSO loads
 * `sprk_playbookconsumer` rows in a second parallel query and joins them
 * in-memory on `_sprk_playbook_value`. Each returned `IPlaybook` has a
 * `consumers` array (possibly empty) listing the consumer surfaces that
 * invoke it. Two-query approach (vs. `$expand`) intentionally avoids
 * depending on the auto-generated 1:N relationship navigation name, which
 * varies by Dataverse maker conventions and is brittle to schema renames.
 * The cost is one extra round-trip; at the ~10-100 playbook scale this is
 * negligible and parallelizable via Promise.all.
 *
 * @param webApi - Dataverse WebAPI accessor (Xrm.WebApi or equivalent).
 * @param options - Optional flags. `includeConsumers` (default false)
 *                  triggers the join with `sprk_playbookconsumer`.
 * @returns Resolved array of IPlaybook records.
 */
export async function loadPlaybooks(
  webApi: IPlaybookWebApi,
  options?: { includeConsumers?: boolean }
): Promise<IPlaybook[]> {
  const includeConsumers = options?.includeConsumers === true;

  // Fire playbook query + optional consumer query in parallel — single
  // round-trip cost even when consumers are joined in.
  const [playbookResult, consumerMap] = await Promise.all([
    webApi.retrieveMultipleRecords(
      ENTITY_NAMES.playbook,
      `?$select=${ID_FIELDS.playbook},sprk_name,sprk_description${BASE_FILTER}`
    ),
    includeConsumers
      ? loadConsumerMappingsByPlaybookId(webApi)
      : Promise.resolve(new Map<string, IPlaybookConsumerMapping[]>()),
  ]);

  return playbookResult.entities.map(
    (entity: Record<string, unknown>): IPlaybook => {
      const playbookId = entity[ID_FIELDS.playbook] as string;
      const playbook: IPlaybook = {
        id: playbookId,
        name: entity.sprk_name as string,
        description: (entity.sprk_description as string) || '',
        icon: 'Lightbulb', // No dedicated icon field in Dataverse
        isDefault: false,
      };
      if (includeConsumers) {
        // Always assign — empty array signals "expand was requested, no rows"
        // (a dead-code candidate per design.md §3 consumer-driven model).
        playbook.consumers = consumerMap.get(playbookId) ?? [];
      }
      return playbook;
    }
  );
}

/**
 * Internal helper — fetches every enabled `sprk_playbookconsumer` row and
 * groups them by playbook id (from `_sprk_playbook_value` OData accessor).
 *
 * R7 task 094 / FR-18 support. The query is unfiltered on consumer-type
 * because the Library modal needs to display the FULL consumer landscape
 * per playbook (not just one consumer). Disabled rows are EXCLUDED here
 * since the Library is a maker-facing discovery surface and showing soft-
 * disabled wiring would confuse the picker. If a future caller needs
 * disabled rows, add a flag.
 *
 * Scale note: this returns the full table (typically 6-20 rows in production).
 * If the consumer table grows to thousands, replace with a per-playbook
 * `$filter=_sprk_playbook_value eq <guid>` loop or move to a BFF endpoint
 * that pre-joins server-side.
 *
 * Per DATA-ACCESS-DECISION-CRITERIA: this uses the SAME webApi accessor as
 * loadPlaybooks (host-context Xrm.WebApi in MDA, BFF proxy in external-SPA).
 * The choice is delegated to the caller — no NEW data-access path introduced.
 */
async function loadConsumerMappingsByPlaybookId(
  webApi: IPlaybookWebApi
): Promise<Map<string, IPlaybookConsumerMapping[]>> {
  const result = await webApi.retrieveMultipleRecords(
    ENTITY_NAMES.playbookConsumer,
    // Note: NO $orderby because consumer table doesn't have sprk_name as the
    // canonical sort field; rely on insertion order. NO statecode filter on
    // the consumer table because that column controls archival, NOT the
    // routing-active toggle (which is sprk_enabled). We DO filter enabled=true
    // because Library should only surface wiring that would actually route.
    '?$select=sprk_consumertype,sprk_consumercode,sprk_environment,sprk_priority,sprk_enabled,_sprk_playbook_value&$filter=sprk_enabled eq true'
  );

  const byPlaybook = new Map<string, IPlaybookConsumerMapping[]>();
  for (const entity of result.entities) {
    // OData lookup accessor convention: `_<lookupcolumn>_value` returns
    // the target GUID. Per chat-routing-redesign-r1 task 028 evidence the
    // lookup column on sprk_playbookconsumer is `sprk_playbook` (NOT
    // `sprk_playbookid`).
    const playbookId = entity['_sprk_playbook_value'] as string | null | undefined;
    if (!playbookId) {
      // Defensive — a consumer row without a lookup target is misconfigured
      // and not actionable for the Library. Skip silently; the FR-19 maker-
      // review tool surfaces these gaps separately.
      continue;
    }
    const mapping: IPlaybookConsumerMapping = {
      consumerType: (entity.sprk_consumertype as string) ?? '',
      consumerCode: (entity.sprk_consumercode as string) ?? null,
      environment: (entity.sprk_environment as string) ?? null,
      enabled: (entity.sprk_enabled as boolean) ?? false,
      priority: (entity.sprk_priority as number) ?? undefined,
    };
    const existing = byPlaybook.get(playbookId);
    if (existing) {
      existing.push(mapping);
    } else {
      byPlaybook.set(playbookId, [mapping]);
    }
  }
  return byPlaybook;
}

/**
 * Load all active actions from sprk_analysisaction.
 *
 * @param webApi - Dataverse WebAPI accessor.
 * @returns Resolved array of IAction records.
 */
export async function loadActions(webApi: IPlaybookWebApi): Promise<IAction[]> {
  const result = await webApi.retrieveMultipleRecords(
    ENTITY_NAMES.action,
    `?$select=${ID_FIELDS.action},sprk_name,sprk_description${BASE_FILTER}`
  );

  return result.entities.map(
    (entity: Record<string, unknown>): IAction => ({
      id: entity[ID_FIELDS.action] as string,
      name: entity.sprk_name as string,
      description: (entity.sprk_description as string) || '',
      icon: 'Play', // No dedicated icon field in Dataverse
    })
  );
}

/**
 * Load all active skills from sprk_analysisskill.
 *
 * @param webApi - Dataverse WebAPI accessor.
 * @returns Resolved array of ISkill records.
 */
export async function loadSkills(webApi: IPlaybookWebApi): Promise<ISkill[]> {
  const result = await webApi.retrieveMultipleRecords(
    ENTITY_NAMES.skill,
    `?$select=${ID_FIELDS.skill},sprk_name,sprk_description${BASE_FILTER}`
  );

  return result.entities.map(
    (entity: Record<string, unknown>): ISkill => ({
      id: entity[ID_FIELDS.skill] as string,
      name: entity.sprk_name as string,
      description: (entity.sprk_description as string) || '',
      icon: 'Brain', // No dedicated icon field in Dataverse
      type: 'analysis', // No dedicated type field in Dataverse
    })
  );
}

/**
 * Load all active knowledge items from sprk_analysisknowledge.
 *
 * @param webApi - Dataverse WebAPI accessor.
 * @returns Resolved array of IKnowledge records.
 */
export async function loadKnowledge(webApi: IPlaybookWebApi): Promise<IKnowledge[]> {
  const result = await webApi.retrieveMultipleRecords(
    ENTITY_NAMES.knowledge,
    `?$select=${ID_FIELDS.knowledge},sprk_name,sprk_description${BASE_FILTER}`
  );

  return result.entities.map(
    (entity: Record<string, unknown>): IKnowledge => ({
      id: entity[ID_FIELDS.knowledge] as string,
      name: entity.sprk_name as string,
      description: (entity.sprk_description as string) || '',
      icon: 'Library', // No dedicated icon field in Dataverse
      source: 'dataverse', // No dedicated source field in Dataverse
    })
  );
}

/**
 * Load all active tools from sprk_analysistool.
 *
 * @param webApi - Dataverse WebAPI accessor.
 * @returns Resolved array of ITool records.
 */
export async function loadTools(webApi: IPlaybookWebApi): Promise<ITool[]> {
  const result = await webApi.retrieveMultipleRecords(
    ENTITY_NAMES.tool,
    `?$select=${ID_FIELDS.tool},sprk_name,sprk_description${BASE_FILTER}`
  );

  return result.entities.map(
    (entity: Record<string, unknown>): ITool => ({
      id: entity[ID_FIELDS.tool] as string,
      name: entity.sprk_name as string,
      description: (entity.sprk_description as string) || '',
      icon: 'Wrench', // No dedicated icon field in Dataverse
      toolType: 'api', // No dedicated toolType field in Dataverse
    })
  );
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
export async function loadPlaybookScopes(webApi: IPlaybookWebApi, playbookId: string): Promise<IPlaybookScopes> {
  const scopes: IPlaybookScopes = {
    actionIds: [],
    skillIds: [],
    knowledgeIds: [],
    toolIds: [],
  };

  const expandQuery =
    `?$expand=` +
    `${RELATIONSHIP_NAMES.playbookSkill}($select=${ID_FIELDS.skill}),` +
    `${RELATIONSHIP_NAMES.playbookKnowledge}($select=${ID_FIELDS.knowledge}),` +
    `${RELATIONSHIP_NAMES.playbookTool}($select=${ID_FIELDS.tool}),` +
    `${RELATIONSHIP_NAMES.playbookAction}($select=${ID_FIELDS.action})`;

  const playbook = await webApi.retrieveRecord(ENTITY_NAMES.playbook, playbookId, expandQuery);

  // Extract skill IDs
  if (playbook[RELATIONSHIP_NAMES.playbookSkill] && Array.isArray(playbook[RELATIONSHIP_NAMES.playbookSkill])) {
    scopes.skillIds = (playbook[RELATIONSHIP_NAMES.playbookSkill] as Array<Record<string, string>>).map(
      s => s[ID_FIELDS.skill]
    );
  }

  // Extract knowledge IDs
  if (playbook[RELATIONSHIP_NAMES.playbookKnowledge] && Array.isArray(playbook[RELATIONSHIP_NAMES.playbookKnowledge])) {
    scopes.knowledgeIds = (playbook[RELATIONSHIP_NAMES.playbookKnowledge] as Array<Record<string, string>>).map(
      k => k[ID_FIELDS.knowledge]
    );
  }

  // Extract tool IDs
  if (playbook[RELATIONSHIP_NAMES.playbookTool] && Array.isArray(playbook[RELATIONSHIP_NAMES.playbookTool])) {
    scopes.toolIds = (playbook[RELATIONSHIP_NAMES.playbookTool] as Array<Record<string, string>>).map(
      t => t[ID_FIELDS.tool]
    );
  }

  // Extract action IDs (note: relationship name follows a different pattern)
  if (playbook[RELATIONSHIP_NAMES.playbookAction] && Array.isArray(playbook[RELATIONSHIP_NAMES.playbookAction])) {
    scopes.actionIds = (playbook[RELATIONSHIP_NAMES.playbookAction] as Array<Record<string, string>>).map(
      a => a[ID_FIELDS.action]
    );
  }

  return scopes;
}

// ---------------------------------------------------------------------------
// Aggregate loader
// ---------------------------------------------------------------------------

/** Result shape returned by loadAllData. */
export interface IPlaybookData {
  playbooks: IPlaybook[];
  actions: IAction[];
  skills: ISkill[];
  knowledge: IKnowledge[];
  tools: ITool[];
}

/**
 * Load all five entity types in parallel.
 *
 * All five requests are dispatched concurrently via Promise.all so total load
 * time is bounded by the slowest individual query rather than the sum.
 *
 * R7 task 094 / FR-18: when `options.includeConsumers === true`, playbooks
 * are joined with their `sprk_playbookconsumer` mappings. Default behavior
 * (no options arg) is UNCHANGED for back-compat — existing callers
 * (PlaybookLibraryShell intent-mode, IntentWizardFlow, etc.) keep their
 * lighter payload.
 *
 * @param webApi - Dataverse WebAPI accessor.
 * @param options - Optional flags. `includeConsumers` (default false) opts
 *                  into the consumer-mapping join.
 * @returns Resolved IPlaybookData containing all entity arrays.
 */
export async function loadAllData(
  webApi: IPlaybookWebApi,
  options?: { includeConsumers?: boolean }
): Promise<IPlaybookData> {
  const [playbooks, actions, skills, knowledge, tools] = await Promise.all([
    loadPlaybooks(webApi, options),
    loadActions(webApi),
    loadSkills(webApi),
    loadKnowledge(webApi),
    loadTools(webApi),
  ]);

  return { playbooks, actions, skills, knowledge, tools };
}
