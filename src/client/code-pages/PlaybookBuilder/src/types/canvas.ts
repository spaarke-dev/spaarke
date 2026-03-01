/**
 * Canvas Types — Shared type definitions for @xyflow/react v12 canvas
 *
 * Defines PlaybookNodeType, PlaybookNodeData, ConditionEdgeData,
 * and typed node/edge aliases used throughout the Playbook Builder.
 *
 * @xyflow/react v12: Uses Node<T> and Edge<T> typed generics.
 */

import type { Node, Edge } from "@xyflow/react";

/**
 * All supported playbook node types.
 * Maps 1:1 to the nodeTypes registry keys and the canvas store.
 */
export type PlaybookNodeType =
    | "start"
    | "aiAnalysis"
    | "aiCompletion"
    | "condition"
    | "deliverOutput"
    | "createTask"
    | "sendEmail"
    | "wait";

/**
 * Data payload for all playbook nodes.
 * Stored in Node<PlaybookNodeData>.data.
 *
 * Fields map to Dataverse sprk_playbooknode columns:
 *   label        -> sprk_name
 *   type         -> sprk_nodetype (option set)
 *   configJson   -> sprk_configjson
 *   actionId     -> sprk_actionid (lookup)
 */
export interface PlaybookNodeData {
    /** Display label shown in the node header */
    label: string;
    /** Node type discriminator */
    type: PlaybookNodeType;
    /** Optional Dataverse action record ID linked to this node */
    actionId?: string;
    /** Output variable name for downstream node references */
    outputVariable?: string;
    /** Serialized node-specific configuration (maps to sprk_configjson) */
    configJson?: string;
    /** Whether the node has been fully configured */
    isConfigured?: boolean;
    /** Validation error messages (empty = valid) */
    validationErrors?: string[];
    /** Arbitrary config bag for node-specific settings */
    config?: Record<string, unknown>;
    /** Timeout in seconds (for wait nodes, AI nodes) */
    timeoutSeconds?: number;
    /** Retry count for recoverable failures */
    retryCount?: number;
    /** Condition expression JSON (for condition nodes) */
    conditionJson?: string;
    /** Selected AI skill IDs (N:N relationship) */
    skillIds?: string[];
    /** Selected knowledge source IDs (N:N relationship) */
    knowledgeIds?: string[];
    /** Selected tool ID (lookup) */
    toolId?: string;
    /** AI model deployment ID (for aiAnalysis, aiCompletion nodes) */
    modelDeploymentId?: string;
    /** Index signature for React Flow v12 compatibility */
    [key: string]: unknown;
}

/**
 * Data payload for condition branch edges.
 * Stored in Edge<ConditionEdgeData>.data.
 */
export interface ConditionEdgeData {
    /** Which branch this edge represents */
    branch: "true" | "false";
    /** Optional human-readable condition label */
    conditionLabel?: string;
    /** Index signature for React Flow v12 compatibility */
    [key: string]: unknown;
}

/**
 * Typed node alias for the Playbook Builder canvas.
 */
export type PlaybookNode = Node<PlaybookNodeData>;

/**
 * Typed edge alias — either a condition branch edge or a standard edge.
 */
export type PlaybookEdge = Edge<ConditionEdgeData>;

/**
 * Canvas JSON structure persisted to sprk_canvaslayoutjson.
 */
export interface CanvasJson {
    nodes: PlaybookNode[];
    edges: PlaybookEdge[];
    version: number;
}

/** Current canvas JSON schema version */
export const CANVAS_JSON_VERSION = 1;
