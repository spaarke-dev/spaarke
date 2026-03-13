/**
 * Playbook Node Sync — Dataverse CRUD for sprk_playbooknode records.
 *
 * Replaces the R4 PCF version that used context.webAPI with
 * DataverseClient (fetch + Bearer token). All node CRUD, N:N
 * relationship management, and config JSON mapping happens here.
 *
 * Responsibilities:
 *   1. Query existing sprk_playbooknode records for a playbook
 *   2. Compute execution order via Kahn's topological sort of canvas edges
 *   3. Create new / update existing / delete orphaned node records
 *   4. Write sprk_dependsonjson with upstream node GUIDs
 *   5. Build sprk_configjson for all 7 node types via buildConfigJson()
 *   6. Manage N:N relationships (skills, knowledge) via associate/disassociate
 *
 * The BFF API only reads these records at execution time — it never
 * creates or updates them.
 *
 * @see design.md Section 7.8 — Updated playbookNodeSync Mapping
 * @see design.md Section 8 — Data Architecture
 */

import {
    createRecord,
    updateRecord,
    deleteRecord,
    retrieveMultipleRecords,
    associate,
    disassociate,
} from "./dataverseClient";
import type { DataverseRecord } from "./dataverseClient";
import {
    type PlaybookNodeData,
    type PlaybookNodeType,
    NodeTypeToDataverse,
    NodeTypeToActionType,
} from "../types/playbook";
import type { PromptSchema } from "../types/promptSchema";
import { validatePromptSchema } from "../types/promptSchema";
import {
    validatePromptSchemaNodes,
    hasValidationErrors,
    groupValidationsByNode,
    type PromptSchemaValidation,
} from "./canvasValidation";

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

/** Minimal canvas node shape from @xyflow/react / canvasStore. */
export interface CanvasNode {
    id: string;
    type: string;
    position: { x: number; y: number };
    data: PlaybookNodeData & Record<string, unknown>;
}

/** Minimal canvas edge shape from @xyflow/react. */
export interface CanvasEdge {
    id: string;
    source: string;
    target: string;
}

/** Existing Dataverse node record (from retrieveMultipleRecords). */
interface ExistingNode {
    id: string;
    configJson: string | null;
    skillIds: string[];
    knowledgeIds: string[];
    toolIds: string[];
}

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

const ENTITY_SET_NAME = "sprk_playbooknodes";
const LOG_PREFIX = "[PlaybookBuilder:PlaybookNodeSync]";

/** Validate that a string is a Dataverse GUID (with or without braces). */
const GUID_RE = /^[{(]?[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}[)}]?$/i;
function isGuid(value: string): boolean {
    return GUID_RE.test(value);
}

// ---------------------------------------------------------------------------
// Public API
// ---------------------------------------------------------------------------

/**
 * Load existing playbook node records from Dataverse.
 * @param playbookId - Playbook record GUID (no braces).
 * @returns Array of raw Dataverse record objects.
 */
export async function loadPlaybookNodes(playbookId: string): Promise<DataverseRecord[]> {
    const queryOptions =
        "$select=sprk_playbooknodeid,sprk_name,sprk_nodetype,sprk_executionorder," +
        "sprk_outputvariable,sprk_configjson,sprk_position_x,sprk_position_y," +
        "sprk_isactive,sprk_timeoutseconds,sprk_retrycount,sprk_conditionjson," +
        "sprk_dependsonjson,_sprk_playbookid_value,_sprk_actionid_value," +
        "_sprk_modeldeploymentid_value" +
        `&$filter=_sprk_playbookid_value eq ${playbookId}` +
        "&$orderby=sprk_executionorder asc";

    const result = await retrieveMultipleRecords(ENTITY_SET_NAME, queryOptions);
    console.info(`${LOG_PREFIX} Loaded ${result.entities.length} node records for playbook ${playbookId}`);
    return result.entities;
}

/**
 * Load N:N scope associations for all nodes in a playbook and return them
 * keyed by canvas node ID. Used by usePlaybookLoader to merge scope data
 * into the canvas store after loading the canvas JSON layout.
 *
 * @param playbookId - Playbook record GUID (no braces).
 * @returns Map of canvas node ID → { skillIds, knowledgeIds, toolIds }.
 */
export async function loadNodeScopes(
    playbookId: string,
): Promise<Map<string, { skillIds: string[]; knowledgeIds: string[]; toolIds: string[] }>> {
    const existing = await getExistingNodes(playbookId);
    const scopeMap = new Map<string, { skillIds: string[]; knowledgeIds: string[]; toolIds: string[] }>();

    for (const record of existing) {
        const canvasId = extractCanvasNodeId(record.configJson);
        if (canvasId) {
            scopeMap.set(canvasId, {
                skillIds: record.skillIds,
                knowledgeIds: record.knowledgeIds,
                toolIds: record.toolIds,
            });
        }
    }

    console.info(
        `${LOG_PREFIX} Loaded N:N scopes for ${scopeMap.size} nodes (of ${existing.length} total)`,
    );
    return scopeMap;
}

/**
 * Synchronise canvas nodes/edges to sprk_playbooknode Dataverse records.
 *
 * @param playbookId - Playbook record GUID (no braces).
 * @param nodes - Current canvas nodes.
 * @param edges - Current canvas edges.
 */
export async function syncNodesToDataverse(
    playbookId: string,
    nodes: CanvasNode[],
    edges: CanvasEdge[],
    initialNodeScopes?: Map<string, { skillIds: string[]; knowledgeIds: string[]; toolIds: string[] }>,
): Promise<void> {
    console.info(`${LOG_PREFIX} Syncing ${nodes.length} nodes, ${edges.length} edges for playbook ${playbookId}`);

    // Step 1: Load existing node records
    const existing = await getExistingNodes(playbookId);
    const existingByCanvasId = buildCanvasIdMap(existing);

    console.info(
        `${LOG_PREFIX} Found ${existing.length} existing records, ${existingByCanvasId.size} mapped by canvas ID`,
    );

    // Step 2: Compute execution order (Kahn's topological sort)
    const executionOrders = computeExecutionOrders(nodes, edges);

    // Step 3: Create or update each canvas node
    const canvasIdToNodeId = new Map<string, string>();
    const processedCanvasIds = new Set<string>();

    // Seed with existing mappings
    for (const [canvasId, nodeRecord] of existingByCanvasId) {
        canvasIdToNodeId.set(canvasId, nodeRecord.id);
    }

    for (const node of nodes) {
        processedCanvasIds.add(node.id);
        const order = executionOrders.get(node.id) ?? 0;

        try {
            const existingRecord = existingByCanvasId.get(node.id);
            const nodeInitialScopes = initialNodeScopes?.get(node.id);
            if (existingRecord) {
                await updateNodeRecord(existingRecord.id, node, order);
                // Sync N:N relationships (skills, knowledge, tools)
                await syncNodeRelationships(existingRecord.id, node.data, existingRecord, nodeInitialScopes);
            } else {
                const newId = await createNodeRecord(playbookId, node, order);
                canvasIdToNodeId.set(node.id, newId);
                // Create N:N relationships for new nodes (no initial scopes — all are new)
                await syncNodeRelationships(newId, node.data, { id: newId, configJson: null, skillIds: [], knowledgeIds: [], toolIds: [] });
            }
        } catch (err) {
            console.error(`${LOG_PREFIX} Failed to sync node ${node.id}:`, err);
        }
    }

    // Step 4: Update dependsOn for each node (needs all IDs resolved first)
    const incomingEdges = buildIncomingEdgeMap(edges);

    for (const node of nodes) {
        const nodeId = canvasIdToNodeId.get(node.id);
        if (!nodeId) continue;

        const dependsOnIds = (incomingEdges.get(node.id) ?? [])
            .filter((srcId) => canvasIdToNodeId.has(srcId))
            .map((srcId) => canvasIdToNodeId.get(srcId)!);

        try {
            const dependsOnJson = dependsOnIds.length > 0 ? JSON.stringify(dependsOnIds) : null;
            await updateRecord(ENTITY_SET_NAME, nodeId, {
                sprk_dependsonjson: dependsOnJson,
            });
        } catch (err) {
            console.error(`${LOG_PREFIX} Failed to update dependsOn for node ${nodeId}:`, err);
        }
    }

    // Step 5: Delete orphaned nodes (exist in Dataverse but not in canvas)
    let deletedCount = 0;
    for (const record of existing) {
        const canvasId = extractCanvasNodeId(record.configJson);
        if (canvasId && processedCanvasIds.has(canvasId)) continue;

        try {
            await deleteRecord(ENTITY_SET_NAME, record.id);
            deletedCount++;
        } catch (err) {
            console.error(`${LOG_PREFIX} Failed to delete orphaned node ${record.id}:`, err);
        }
    }

    console.info(
        `${LOG_PREFIX} Sync complete: ${nodes.length - existingByCanvasId.size} created, ` +
        `${Math.min(nodes.length, existingByCanvasId.size)} updated, ${deletedCount} deleted`,
    );
}

// ---------------------------------------------------------------------------
// Validation-Aware Sync — Runs canvas validation before persisting
// ---------------------------------------------------------------------------

/**
 * Result of a validated sync operation.
 */
export interface ValidatedSyncResult {
    /** Whether the sync was executed (false if validation errors blocked it). */
    synced: boolean;
    /** All validation results (errors and warnings). */
    validations: PromptSchemaValidation[];
    /** Validation results grouped by node ID for UI consumption. */
    validationsByNode: Map<string, { errors: string[]; warnings: string[] }>;
}

/**
 * Validate canvas nodes and, if no errors, sync to Dataverse.
 *
 * This is the recommended entry point for save operations. It:
 *   1. Runs all prompt schema validation rules
 *   2. If errors exist, returns results WITHOUT syncing (errors block save)
 *   3. If only warnings (or none), syncs to Dataverse and returns results
 *
 * @param playbookId - Playbook record GUID (no braces).
 * @param nodes - Current canvas nodes (typed for @xyflow/react compatibility).
 * @param edges - Current canvas edges.
 * @returns Validation results and whether the sync was executed.
 */
export async function validateAndSyncNodes(
    playbookId: string,
    nodes: CanvasNode[],
    edges: CanvasEdge[],
    initialNodeScopes?: Map<string, { skillIds: string[]; knowledgeIds: string[]; toolIds: string[] }>,
): Promise<ValidatedSyncResult> {
    // Cast CanvasNode[] to the @xyflow/react Node shape for validation.
    // CanvasNode is structurally compatible (has id, type, data with PlaybookNodeData).
    const validationNodes = nodes.map((n) => ({
        ...n,
        data: n.data,
    }));

    const validations = validatePromptSchemaNodes(
        validationNodes as unknown as import("@xyflow/react").Node<import("../types/canvas").PlaybookNodeData>[],
        edges,
    );

    const validationsByNode = groupValidationsByNode(validations);

    if (hasValidationErrors(validations)) {
        const errorCount = validations.filter((v) => v.severity === "error").length;
        console.warn(
            `${LOG_PREFIX} Sync blocked: ${errorCount} validation error(s) found. Fix errors before saving.`,
        );
        return { synced: false, validations, validationsByNode };
    }

    // No errors — proceed with sync (warnings are informational)
    await syncNodesToDataverse(playbookId, nodes, edges, initialNodeScopes);
    return { synced: true, validations, validationsByNode };
}

// Re-export validation utilities for consumers that import from playbookNodeSync
export {
    validatePromptSchemaNodes,
    hasValidationErrors,
    groupValidationsByNode,
    type PromptSchemaValidation,
} from "./canvasValidation";

// ---------------------------------------------------------------------------
// buildConfigJson — Maps ALL 7 node types' fields into sprk_configjson
// ---------------------------------------------------------------------------

/**
 * Build the sprk_configjson JSON string from typed node data.
 * Maps type-specific form fields into the config blob that the
 * execution engine reads at runtime.
 *
 * @param canvasNodeId - The canvas node ID (stored as __canvasNodeId for sync tracking).
 * @param data - The PlaybookNodeData from the canvas store.
 * @returns JSON string for sprk_configjson.
 */
export function buildConfigJson(canvasNodeId: string, data: PlaybookNodeData): string {
    const actionType = NodeTypeToActionType[data.type as PlaybookNodeType];
    const config: Record<string, unknown> = {
        __canvasNodeId: canvasNodeId,
        __actionType: actionType ?? 0,
    };

    switch (data.type) {
        case "aiAnalysis":
            // AI Analysis: scope IDs, model, tool, action
            // SkillIds and KnowledgeIds are handled via N:N tables, not in configjson.
            // But we include model/tool/action references for executor convenience.
            if (data.modelDeploymentId) config.modelDeploymentId = data.modelDeploymentId;
            // JPS serialization: if promptSchema exists, serialize it as the systemPrompt value.
            // Otherwise preserve flat text systemPrompt for backward compatibility.
            if (data.promptSchema) {
                config.systemPrompt = JSON.stringify(data.promptSchema);
            } else if (data.systemPrompt) {
                config.systemPrompt = data.systemPrompt;
            }
            break;

        case "aiCompletion":
            // AI Completion: system prompt, user prompt template, temperature, max tokens
            // JPS serialization: if promptSchema exists, serialize it as the systemPrompt value.
            // Otherwise preserve flat text systemPrompt for backward compatibility.
            if (data.promptSchema) {
                config.systemPrompt = JSON.stringify(data.promptSchema);
            } else if (data.systemPrompt) {
                config.systemPrompt = data.systemPrompt;
            }
            if (data.userPromptTemplate) config.userPromptTemplate = data.userPromptTemplate;
            if (data.temperature != null) config.temperature = data.temperature;
            if (data.maxTokens != null) config.maxTokens = data.maxTokens;
            if (data.modelDeploymentId) config.modelDeploymentId = data.modelDeploymentId;
            break;

        case "condition":
            // Condition: conditionJson (already a JSON string, stored separately)
            // conditionJson goes into sprk_conditionjson field, not configjson.
            // Any additional condition config can go here.
            break;

        case "deliverOutput":
            // Deliver Output: delivery type, Handlebars template, output format
            if (data.deliveryType) config.deliveryType = data.deliveryType;
            if (data.template) config.template = data.template;
            config.outputFormat = {
                includeMetadata: data.includeMetadata ?? false,
                includeSourceCitations: data.includeSourceCitations ?? false,
                maxLength: data.maxOutputLength ?? undefined,
            };
            break;

        case "deliverToIndex":
            // Deliver to Index: index name, source, parent entity, metadata
            if (data.indexName) config.indexName = data.indexName;
            config.source = data.indexSource ?? "document";
            if (data.indexContentVariable) config.contentVariable = data.indexContentVariable;
            if (data.indexParentEntityType || data.indexParentEntityId) {
                config.parentEntity = {
                    entityType: data.indexParentEntityType ?? "",
                    entityId: data.indexParentEntityId ?? "",
                    entityName: data.indexParentEntityName ?? "",
                };
            }
            if (data.indexMetadata) {
                try {
                    config.metadata = JSON.parse(data.indexMetadata as string);
                } catch { /* invalid JSON, skip */ }
            }
            break;

        case "sendEmail":
            // Send Email: to, cc, subject, body, isHtml
            if (data.emailTo && data.emailTo.length > 0) config.to = data.emailTo;
            if (data.emailCc && data.emailCc.length > 0) config.cc = data.emailCc;
            if (data.emailSubject) config.subject = data.emailSubject;
            if (data.emailBody) config.body = data.emailBody;
            if (data.emailIsHtml != null) config.isHtml = data.emailIsHtml;
            break;

        case "updateRecord":
            // Update Record: entityLogicalName, recordId, fieldMappings (new) or fields (legacy), lookups
            // The UpdateRecordForm stores full config in data.configJson
            if (data.configJson) {
                try {
                    const parsed = JSON.parse(data.configJson as string);
                    if (parsed.entityLogicalName) config.entityLogicalName = parsed.entityLogicalName;
                    if (parsed.recordId) config.recordId = parsed.recordId;
                    if (parsed.fieldMappings) config.fieldMappings = parsed.fieldMappings;
                    else if (parsed.fields) config.fields = parsed.fields;
                    if (parsed.lookups) config.lookups = parsed.lookups;
                } catch { /* invalid JSON, skip */ }
            }
            break;

        case "createTask":
            // Create Task: subject, description, regarding, owner, dueDate
            if (data.taskSubject) config.subject = data.taskSubject;
            if (data.taskDescription) config.description = data.taskDescription;
            if (data.taskRegardingId) config.regardingObjectId = data.taskRegardingId;
            if (data.taskRegardingType) config.regardingObjectType = data.taskRegardingType;
            if (data.taskOwnerId) config.ownerId = data.taskOwnerId;
            if (data.taskDueDate) config.dueDate = data.taskDueDate;
            break;

        case "wait":
            // Wait: wait type, duration, datetime
            if (data.waitType) config.waitType = data.waitType;
            if (data.waitDurationHours != null) config.duration = { hours: data.waitDurationHours };
            if (data.waitUntilDateTime) config.untilDateTime = data.waitUntilDateTime;
            break;
    }

    return JSON.stringify(config);
}

// ---------------------------------------------------------------------------
// JPS Format Detection & Deserialization (Dataverse → canvas)
// ---------------------------------------------------------------------------

/**
 * Detect whether a string contains JPS (JSON Prompt Schema) format.
 *
 * Mirrors the server-side detection logic: the string must start with '{'
 * (after trimming whitespace) and contain the '"$schema"' marker.
 *
 * @param value - The raw string value (e.g. from sprk_systemprompt or configJson.systemPrompt).
 * @returns True if the value appears to be JPS JSON, false if it's flat text or empty.
 */
export function isJpsFormat(value: string | null | undefined): boolean {
    if (!value) return false;
    const trimmed = value.trimStart();
    return trimmed.startsWith("{") && trimmed.includes('"$schema"');
}

/**
 * Attempt to deserialize a JPS JSON string into a PromptSchema object.
 *
 * Performs format detection, JSON parsing, and structural validation.
 * Returns undefined for flat text, empty strings, or invalid JPS.
 *
 * @param value - The raw string value to attempt JPS deserialization on.
 * @returns A validated PromptSchema if the value is valid JPS, or undefined otherwise.
 */
export function deserializePromptSchema(value: string | null | undefined): PromptSchema | undefined {
    if (!isJpsFormat(value)) return undefined;

    try {
        const parsed = JSON.parse(value!);
        const result = validatePromptSchema(parsed);
        if (result.valid) {
            return parsed as PromptSchema;
        }
        console.warn(`${LOG_PREFIX} JPS validation failed:`, result.errors);
        return undefined;
    } catch (err) {
        console.warn(`${LOG_PREFIX} JPS parse failed:`, err);
        return undefined;
    }
}

/**
 * Extract a PromptSchema from a node's configJson string.
 *
 * Parses the configJson blob, checks the `systemPrompt` property for JPS format,
 * and returns the deserialized PromptSchema if valid.
 *
 * Also checks for a `promptSchemaOverride` property in the configJson
 * (reserved for node-level overrides in Phase 5).
 *
 * @param configJson - The raw sprk_configjson string from a Dataverse node record.
 * @returns An object with `promptSchema` (from systemPrompt) and `promptSchemaOverride`
 *          (from configJson.promptSchemaOverride), either or both may be undefined.
 */
export function parseNodeConfigForPromptSchema(configJson: string | null | undefined): {
    promptSchema: PromptSchema | undefined;
    promptSchemaOverride: PromptSchema | undefined;
} {
    if (!configJson) return { promptSchema: undefined, promptSchemaOverride: undefined };

    try {
        const parsed = JSON.parse(configJson);
        const systemPromptValue = typeof parsed.systemPrompt === "string" ? parsed.systemPrompt : undefined;
        const overrideValue = typeof parsed.promptSchemaOverride === "string"
            ? parsed.promptSchemaOverride
            : undefined;

        return {
            promptSchema: deserializePromptSchema(systemPromptValue),
            promptSchemaOverride: deserializePromptSchema(overrideValue),
        };
    } catch {
        return { promptSchema: undefined, promptSchemaOverride: undefined };
    }
}

// ---------------------------------------------------------------------------
// Dataverse CRUD (internal)
// ---------------------------------------------------------------------------

async function getExistingNodes(playbookId: string): Promise<ExistingNode[]> {
    const queryOptions =
        "$select=sprk_playbooknodeid,sprk_configjson" +
        `&$filter=_sprk_playbookid_value eq ${playbookId}` +
        "&$expand=sprk_playbooknode_skill($select=sprk_analysisskillid)" +
        ",sprk_playbooknode_knowledge($select=sprk_analysisknowledgeid)" +
        ",sprk_playbooknode_tool($select=sprk_analysistoolid)";

    const result = await retrieveMultipleRecords(ENTITY_SET_NAME, queryOptions);
    return (result.entities ?? []).map((e) => ({
        id: (e["sprk_playbooknodeid"] as string) ?? "",
        configJson: (e["sprk_configjson"] as string) ?? null,
        skillIds: extractRelatedIds(e["sprk_playbooknode_skill"], "sprk_analysisskillid"),
        knowledgeIds: extractRelatedIds(e["sprk_playbooknode_knowledge"], "sprk_analysisknowledgeid"),
        toolIds: extractRelatedIds(e["sprk_playbooknode_tool"], "sprk_analysistoolid"),
    }));
}

/** Extract an array of GUIDs from an expanded N:N collection. */
function extractRelatedIds(collection: unknown, idField: string): string[] {
    if (!Array.isArray(collection)) return [];
    return collection
        .map((item: Record<string, unknown>) => (item[idField] as string) ?? "")
        .filter((id: string) => id.length > 0);
}

async function createNodeRecord(
    playbookId: string,
    node: CanvasNode,
    executionOrder: number,
): Promise<string> {
    const data = node.data;
    const name = asString(data.label) ?? asString(data.name as unknown) ?? node.type;
    const outputVariable = asString(data.outputVariable) ?? `output_${node.id}`;
    const configJson = buildConfigJson(node.id, data);
    const nodeType = NodeTypeToDataverse[node.type as PlaybookNodeType];

    const payload: Record<string, unknown> = {
        sprk_name: name,
        sprk_nodetype: nodeType,
        "sprk_playbookid@odata.bind": `/sprk_analysisplaybooks(${playbookId})`,
        sprk_executionorder: executionOrder,
        sprk_outputvariable: outputVariable,
        sprk_configjson: configJson,
        sprk_position_x: Math.round(node.position.x),
        sprk_position_y: Math.round(node.position.y),
        sprk_isactive: data.isActive ?? true,
    };

    // Optional lookup bindings (only if real GUIDs, skip mock IDs)
    const actionId = asString(data.actionId);
    if (actionId && isGuid(actionId)) payload["sprk_actionid@odata.bind"] = `/sprk_analysisactions(${actionId})`;

    // Tool is now N:N — handled via syncNodeRelationships, not lookup binding

    const modelDeploymentId = asString(data.modelDeploymentId);
    if (modelDeploymentId && isGuid(modelDeploymentId))
        payload["sprk_modeldeploymentid@odata.bind"] = `/sprk_aimodeldeployments(${modelDeploymentId})`;

    const timeoutSeconds = asNumber(data.timeoutSeconds);
    if (timeoutSeconds != null) payload["sprk_timeoutseconds"] = timeoutSeconds;

    const retryCount = asNumber(data.retryCount);
    if (retryCount != null) payload["sprk_retrycount"] = retryCount;

    const conditionJson = asString(data.conditionJson);
    if (conditionJson) payload["sprk_conditionjson"] = conditionJson;

    const newId = await createRecord(ENTITY_SET_NAME, payload);
    console.info(`${LOG_PREFIX} Created node ${newId} from canvas ${node.id}: ${name}`);
    return newId;
}

async function updateNodeRecord(
    nodeId: string,
    node: CanvasNode,
    executionOrder: number,
): Promise<void> {
    const data = node.data;
    const configJson = buildConfigJson(node.id, data);
    const nodeType = NodeTypeToDataverse[node.type as PlaybookNodeType];

    const payload: Record<string, unknown> = {
        sprk_nodetype: nodeType,
        sprk_executionorder: executionOrder,
        sprk_configjson: configJson,
        sprk_position_x: Math.round(node.position.x),
        sprk_position_y: Math.round(node.position.y),
    };

    const name = asString(data.label) ?? asString(data.name as unknown);
    if (name) payload["sprk_name"] = name;

    const outputVariable = asString(data.outputVariable);
    if (outputVariable) payload["sprk_outputvariable"] = outputVariable;

    const actionId = asString(data.actionId);
    if (actionId && isGuid(actionId)) payload["sprk_actionid@odata.bind"] = `/sprk_analysisactions(${actionId})`;

    // Tool is now N:N — handled via syncNodeRelationships, not lookup binding

    const modelDeploymentId = asString(data.modelDeploymentId);
    if (modelDeploymentId && isGuid(modelDeploymentId))
        payload["sprk_modeldeploymentid@odata.bind"] = `/sprk_aimodeldeployments(${modelDeploymentId})`;

    const timeoutSeconds = asNumber(data.timeoutSeconds);
    if (timeoutSeconds != null) payload["sprk_timeoutseconds"] = timeoutSeconds;

    const retryCount = asNumber(data.retryCount);
    if (retryCount != null) payload["sprk_retrycount"] = retryCount;

    const conditionJson = asString(data.conditionJson);
    if (conditionJson) payload["sprk_conditionjson"] = conditionJson;

    const isActive = asBool(data.isActive);
    if (isActive != null) payload["sprk_isactive"] = isActive;

    await updateRecord(ENTITY_SET_NAME, nodeId, payload);
}

// ---------------------------------------------------------------------------
// N:N Relationship Sync (Skills + Knowledge)
// ---------------------------------------------------------------------------

/**
 * Sync N:N relationships for a node:
 *   - sprk_playbooknode_skill (PlaybookNode ↔ AnalysisSkill)
 *   - sprk_playbooknode_knowledge (PlaybookNode ↔ AnalysisKnowledge)
 *   - sprk_playbooknode_tool (PlaybookNode ↔ AnalysisTool)
 *
 * Computes the diff between current canvas selections and existing
 * Dataverse associations, then adds/removes as needed.
 *
 * @param initialScopes - The N:N scopes that were loaded when the canvas first opened.
 *   Used to distinguish "user explicitly removed a scope in the canvas" from
 *   "scope was added externally after the canvas loaded". Without this, externally-
 *   added scopes would be disassociated on save because the canvas doesn't know
 *   about them.
 */
async function syncNodeRelationships(
    nodeId: string,
    data: PlaybookNodeData,
    existingRecord: ExistingNode,
    initialScopes?: { skillIds: string[]; knowledgeIds: string[]; toolIds: string[] },
): Promise<void> {
    // Filter out non-GUID IDs (e.g. mock/placeholder IDs like "skill-5")
    const desiredSkillIds = (data.skillIds ?? []).filter(isGuid);
    const desiredKnowledgeIds = (data.knowledgeIds ?? []).filter(isGuid);
    const desiredToolIds = (data.toolIds ?? []).filter(isGuid);

    // Protect externally-added scopes: only remove scopes that were present
    // at load time AND were explicitly removed by the user in the canvas.
    // Scopes added externally (in Dataverse but not in initialScopes) are preserved.
    const safeToRemoveSkills = initialScopes?.skillIds ?? [];
    const safeToRemoveKnowledge = initialScopes?.knowledgeIds ?? [];
    const safeToRemoveTools = initialScopes?.toolIds ?? [];

    // Sync skills
    const skillsToAdd = desiredSkillIds.filter((id) => !existingRecord.skillIds.includes(id));
    const skillsToRemove = existingRecord.skillIds.filter(
        (id) => !desiredSkillIds.includes(id) && safeToRemoveSkills.includes(id),
    );

    for (const skillId of skillsToAdd) {
        try {
            await associate(
                ENTITY_SET_NAME,
                nodeId,
                "sprk_playbooknode_skill",
                "sprk_analysisskills",
                skillId,
            );
        } catch (err) {
            console.error(`${LOG_PREFIX} Failed to associate skill ${skillId} with node ${nodeId}:`, err);
        }
    }

    for (const skillId of skillsToRemove) {
        try {
            await disassociate(
                ENTITY_SET_NAME,
                nodeId,
                "sprk_playbooknode_skill",
                skillId,
            );
        } catch (err) {
            console.error(`${LOG_PREFIX} Failed to disassociate skill ${skillId} from node ${nodeId}:`, err);
        }
    }

    // Sync knowledge
    const knowledgeToAdd = desiredKnowledgeIds.filter((id) => !existingRecord.knowledgeIds.includes(id));
    const knowledgeToRemove = existingRecord.knowledgeIds.filter(
        (id) => !desiredKnowledgeIds.includes(id) && safeToRemoveKnowledge.includes(id),
    );

    for (const knowledgeId of knowledgeToAdd) {
        try {
            await associate(
                ENTITY_SET_NAME,
                nodeId,
                "sprk_playbooknode_knowledge",
                "sprk_analysisknowledges",
                knowledgeId,
            );
        } catch (err) {
            console.error(`${LOG_PREFIX} Failed to associate knowledge ${knowledgeId} with node ${nodeId}:`, err);
        }
    }

    for (const knowledgeId of knowledgeToRemove) {
        try {
            await disassociate(
                ENTITY_SET_NAME,
                nodeId,
                "sprk_playbooknode_knowledge",
                knowledgeId,
            );
        } catch (err) {
            console.error(`${LOG_PREFIX} Failed to disassociate knowledge ${knowledgeId} from node ${nodeId}:`, err);
        }
    }

    // Sync tools
    const toolsToAdd = desiredToolIds.filter((id) => !existingRecord.toolIds.includes(id));
    const toolsToRemove = existingRecord.toolIds.filter(
        (id) => !desiredToolIds.includes(id) && safeToRemoveTools.includes(id),
    );

    for (const toolId of toolsToAdd) {
        try {
            await associate(
                ENTITY_SET_NAME,
                nodeId,
                "sprk_playbooknode_tool",
                "sprk_analysistools",
                toolId,
            );
        } catch (err) {
            console.error(`${LOG_PREFIX} Failed to associate tool ${toolId} with node ${nodeId}:`, err);
        }
    }

    for (const toolId of toolsToRemove) {
        try {
            await disassociate(
                ENTITY_SET_NAME,
                nodeId,
                "sprk_playbooknode_tool",
                toolId,
            );
        } catch (err) {
            console.error(`${LOG_PREFIX} Failed to disassociate tool ${toolId} from node ${nodeId}:`, err);
        }
    }

    const totalChanges = skillsToAdd.length + skillsToRemove.length +
        knowledgeToAdd.length + knowledgeToRemove.length +
        toolsToAdd.length + toolsToRemove.length;

    if (totalChanges > 0) {
        console.info(
            `${LOG_PREFIX} Node ${nodeId} N:N sync: ` +
            `skills +${skillsToAdd.length}/-${skillsToRemove.length}, ` +
            `knowledge +${knowledgeToAdd.length}/-${knowledgeToRemove.length}, ` +
            `tools +${toolsToAdd.length}/-${toolsToRemove.length}`,
        );
    }
}

// ---------------------------------------------------------------------------
// Graph Algorithms
// ---------------------------------------------------------------------------

/**
 * Kahn's algorithm — topological sort of canvas edges to produce
 * execution order per node. Nodes in cycles receive order 0.
 */
function computeExecutionOrders(nodes: CanvasNode[], edges: CanvasEdge[]): Map<string, number> {
    const nodeIds = new Set(nodes.map((n) => n.id));
    const inDegree = new Map<string, number>();
    const adjacency = new Map<string, string[]>();

    for (const n of nodes) {
        inDegree.set(n.id, 0);
        adjacency.set(n.id, []);
    }

    for (const e of edges) {
        if (nodeIds.has(e.source) && nodeIds.has(e.target)) {
            adjacency.get(e.source)!.push(e.target);
            inDegree.set(e.target, (inDegree.get(e.target) ?? 0) + 1);
        }
    }

    const queue: string[] = [];
    for (const [id, deg] of inDegree) {
        if (deg === 0) queue.push(id);
    }

    const order = new Map<string, number>();
    let currentOrder = 1;

    while (queue.length > 0) {
        const id = queue.shift()!;
        order.set(id, currentOrder++);
        for (const target of adjacency.get(id) ?? []) {
            const newDeg = (inDegree.get(target) ?? 1) - 1;
            inDegree.set(target, newDeg);
            if (newDeg === 0) queue.push(target);
        }
    }

    // Nodes in cycles get order 0
    for (const n of nodes) {
        if (!order.has(n.id)) order.set(n.id, 0);
    }

    return order;
}

/** Build map: targetCanvasId -> [sourceCanvasIds] from edges. */
function buildIncomingEdgeMap(edges: CanvasEdge[]): Map<string, string[]> {
    const map = new Map<string, string[]>();
    for (const e of edges) {
        if (!map.has(e.target)) map.set(e.target, []);
        map.get(e.target)!.push(e.source);
    }
    return map;
}

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

/** Build lookup: canvasNodeId -> ExistingNode from records. */
function buildCanvasIdMap(records: ExistingNode[]): Map<string, ExistingNode> {
    const map = new Map<string, ExistingNode>();
    for (const r of records) {
        const canvasId = extractCanvasNodeId(r.configJson);
        if (canvasId) map.set(canvasId, r);
    }
    return map;
}

/** Extract __canvasNodeId from the configJson blob. */
function extractCanvasNodeId(configJson: string | null): string | null {
    if (!configJson) return null;
    try {
        const obj = JSON.parse(configJson);
        return typeof obj.__canvasNodeId === "string" ? obj.__canvasNodeId : null;
    } catch {
        return null;
    }
}

function asString(v: unknown): string | null {
    return typeof v === "string" && v.length > 0 ? v : null;
}

function asNumber(v: unknown): number | null {
    if (typeof v === "number") return v;
    if (typeof v === "string") {
        const n = Number(v);
        return isNaN(n) ? null : n;
    }
    return null;
}

function asBool(v: unknown): boolean | null {
    if (typeof v === "boolean") return v;
    return null;
}
