/**
 * Playbook Node Sync — Dataverse CRUD for sprk_playbooknode records.
 *
 * When the PlaybookBuilder canvas is saved, this service synchronises the
 * visual canvas nodes to executable Dataverse records via the PCF WebAPI.
 *
 * Responsibilities (all Dataverse-direct, no BFF API involvement):
 *   1. Query existing sprk_playbooknode records for the playbook
 *   2. Compute execution order via topological sort of canvas edges
 *   3. Create new / update existing / delete orphaned node records
 *   4. Store dependsOn GUIDs for the execution graph
 *
 * The BFF API only reads these records at execution time — it never
 * creates or updates them.
 */

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

/** Minimal canvas node shape from React Flow / canvasStore */
export interface CanvasNode {
    id: string;
    type: string;
    position: { x: number; y: number };
    data: Record<string, unknown>;
}

/** Minimal canvas edge shape from React Flow */
export interface CanvasEdge {
    id: string;
    source: string;
    target: string;
}

/** PCF WebAPI interface (subset of ComponentFramework.WebApi) */
interface PcfWebApi {
    createRecord(entityName: string, data: Record<string, unknown>): Promise<{ id: string }>;
    updateRecord(entityName: string, id: string, data: Record<string, unknown>): Promise<unknown>;
    deleteRecord(entityName: string, id: string): Promise<unknown>;
    retrieveMultipleRecords(
        entityName: string,
        options?: string,
        maxPageSize?: number,
    ): Promise<{ entities: Record<string, unknown>[] }>;
}

/** Existing Dataverse node record (from retrieveMultipleRecords) */
interface ExistingNode {
    id: string; // sprk_playbooknodeid
    configJson: string | null;
}

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

const ENTITY_NAME = 'sprk_playbooknode';
const LOG_PREFIX = '[PlaybookNodeSync]';

// ---------------------------------------------------------------------------
// Public API
// ---------------------------------------------------------------------------

/**
 * Synchronise canvas nodes/edges to sprk_playbooknode Dataverse records.
 *
 * @param webApi   PCF WebAPI from context.webAPI
 * @param playbookId  Playbook record GUID (no braces)
 * @param nodes    Current canvas nodes
 * @param edges    Current canvas edges
 */
export async function syncCanvasToNodes(
    webApi: PcfWebApi,
    playbookId: string,
    nodes: CanvasNode[],
    edges: CanvasEdge[],
): Promise<void> {
    console.info(`${LOG_PREFIX} Syncing ${nodes.length} nodes, ${edges.length} edges for playbook ${playbookId}`);

    // Step 1: Load existing node records
    const existing = await getExistingNodes(webApi, playbookId);
    const existingByCanvasId = buildCanvasIdMap(existing);

    console.info(`${LOG_PREFIX} Found ${existing.length} existing records, ${existingByCanvasId.size} mapped by canvas ID`);

    // Step 2: Compute execution order (Kahn's topological sort)
    const executionOrders = computeExecutionOrders(nodes, edges);

    // Step 3: Create or update each canvas node
    const canvasIdToNodeId = new Map<string, string>();
    const processedCanvasIds = new Set<string>();

    // Seed with existing mappings
    for (const [canvasId, nodeId] of existingByCanvasId) {
        canvasIdToNodeId.set(canvasId, nodeId);
    }

    for (const node of nodes) {
        processedCanvasIds.add(node.id);
        const order = executionOrders.get(node.id) ?? 0;

        try {
            const existingNodeId = existingByCanvasId.get(node.id);
            if (existingNodeId) {
                await updateNode(webApi, existingNodeId, node, order);
            } else {
                const newId = await createNode(webApi, playbookId, node, order);
                canvasIdToNodeId.set(node.id, newId);
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
            await webApi.updateRecord(ENTITY_NAME, nodeId, {
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
            await webApi.deleteRecord(ENTITY_NAME, record.id);
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
// Dataverse CRUD
// ---------------------------------------------------------------------------

async function getExistingNodes(webApi: PcfWebApi, playbookId: string): Promise<ExistingNode[]> {
    const select = 'sprk_playbooknodeid,sprk_configjson';
    const filter = `_sprk_playbookid_value eq ${playbookId}`;
    const options = `?$select=${select}&$filter=${filter}`;

    const result = await webApi.retrieveMultipleRecords(ENTITY_NAME, options, 500);
    return (result.entities ?? []).map((e) => ({
        id: (e['sprk_playbooknodeid'] as string) ?? '',
        configJson: (e['sprk_configjson'] as string) ?? null,
    }));
}

async function createNode(
    webApi: PcfWebApi,
    playbookId: string,
    node: CanvasNode,
    executionOrder: number,
): Promise<string> {
    const data = node.data;
    const name = asString(data['label']) ?? asString(data['name']) ?? node.type;
    const outputVariable = asString(data['outputVariable']) ?? `output_${node.id}`;
    const configJson = JSON.stringify({ __canvasNodeId: node.id, ...stripKnownFields(data) });

    const payload: Record<string, unknown> = {
        sprk_name: name,
        'sprk_playbookid@odata.bind': `/sprk_analysisplaybooks(${playbookId})`,
        sprk_executionorder: executionOrder,
        sprk_outputvariable: outputVariable,
        sprk_configjson: configJson,
        sprk_position_x: Math.round(node.position.x),
        sprk_position_y: Math.round(node.position.y),
        sprk_isactive: asBool(data['isActive']) ?? true,
    };

    // Optional lookup bindings
    const actionId = asString(data['actionId']);
    if (actionId) payload['sprk_actionid@odata.bind'] = `/sprk_analysisactions(${actionId})`;

    const toolId = asString(data['toolId']);
    if (toolId) payload['sprk_toolid@odata.bind'] = `/sprk_analysistools(${toolId})`;

    const modelDeploymentId = asString(data['modelDeploymentId']);
    if (modelDeploymentId)
        payload['sprk_modeldeploymentid@odata.bind'] = `/sprk_aimodeldeployments(${modelDeploymentId})`;

    const timeoutSeconds = asNumber(data['timeoutSeconds']);
    if (timeoutSeconds != null) payload['sprk_timeoutseconds'] = timeoutSeconds;

    const retryCount = asNumber(data['retryCount']);
    if (retryCount != null) payload['sprk_retrycount'] = retryCount;

    const conditionJson = asString(data['conditionJson']);
    if (conditionJson) payload['sprk_conditionjson'] = conditionJson;

    const result = await webApi.createRecord(ENTITY_NAME, payload);
    console.info(`${LOG_PREFIX} Created node ${result.id} from canvas ${node.id}: ${name}`);
    return result.id;
}

async function updateNode(
    webApi: PcfWebApi,
    nodeId: string,
    node: CanvasNode,
    executionOrder: number,
): Promise<void> {
    const data = node.data;
    const configJson = JSON.stringify({ __canvasNodeId: node.id, ...stripKnownFields(data) });

    const payload: Record<string, unknown> = {
        sprk_executionorder: executionOrder,
        sprk_configjson: configJson,
        sprk_position_x: Math.round(node.position.x),
        sprk_position_y: Math.round(node.position.y),
    };

    const name = asString(data['label']) ?? asString(data['name']);
    if (name) payload['sprk_name'] = name;

    const outputVariable = asString(data['outputVariable']);
    if (outputVariable) payload['sprk_outputvariable'] = outputVariable;

    const actionId = asString(data['actionId']);
    if (actionId) payload['sprk_actionid@odata.bind'] = `/sprk_analysisactions(${actionId})`;

    const toolId = asString(data['toolId']);
    if (toolId) payload['sprk_toolid@odata.bind'] = `/sprk_analysistools(${toolId})`;

    const modelDeploymentId = asString(data['modelDeploymentId']);
    if (modelDeploymentId)
        payload['sprk_modeldeploymentid@odata.bind'] = `/sprk_aimodeldeployments(${modelDeploymentId})`;

    const timeoutSeconds = asNumber(data['timeoutSeconds']);
    if (timeoutSeconds != null) payload['sprk_timeoutseconds'] = timeoutSeconds;

    const retryCount = asNumber(data['retryCount']);
    if (retryCount != null) payload['sprk_retrycount'] = retryCount;

    const conditionJson = asString(data['conditionJson']);
    if (conditionJson) payload['sprk_conditionjson'] = conditionJson;

    const isActive = asBool(data['isActive']);
    if (isActive != null) payload['sprk_isactive'] = isActive;

    await webApi.updateRecord(ENTITY_NAME, nodeId, payload);
}

// ---------------------------------------------------------------------------
// Graph algorithms
// ---------------------------------------------------------------------------

/** Kahn's algorithm — topological sort of canvas edges → execution order per node. */
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

/** Build map: targetCanvasId → [sourceCanvasIds] from edges. */
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

/** Build lookup: canvasNodeId → Dataverse node ID from existing records. */
function buildCanvasIdMap(records: ExistingNode[]): Map<string, string> {
    const map = new Map<string, string>();
    for (const r of records) {
        const canvasId = extractCanvasNodeId(r.configJson);
        if (canvasId) map.set(canvasId, r.id);
    }
    return map;
}

/** Extract __canvasNodeId from the configJson blob. */
function extractCanvasNodeId(configJson: string | null): string | null {
    if (!configJson) return null;
    try {
        const obj = JSON.parse(configJson);
        return typeof obj.__canvasNodeId === 'string' ? obj.__canvasNodeId : null;
    } catch {
        return null;
    }
}

/** Strip known Dataverse-bound fields from data to produce clean config. */
function stripKnownFields(data: Record<string, unknown>): Record<string, unknown> {
    const known = new Set([
        'label', 'name', 'actionId', 'toolId', 'modelDeploymentId',
        'outputVariable', 'timeoutSeconds', 'retryCount', 'conditionJson',
        'isActive', 'skillIds', 'knowledgeIds',
    ]);
    const result: Record<string, unknown> = {};
    for (const [k, v] of Object.entries(data)) {
        if (!known.has(k)) result[k] = v;
    }
    return result;
}

function asString(v: unknown): string | null {
    return typeof v === 'string' && v.length > 0 ? v : null;
}

function asNumber(v: unknown): number | null {
    if (typeof v === 'number') return v;
    if (typeof v === 'string') {
        const n = Number(v);
        return isNaN(n) ? null : n;
    }
    return null;
}

function asBool(v: unknown): boolean | null {
    if (typeof v === 'boolean') return v;
    return null;
}
