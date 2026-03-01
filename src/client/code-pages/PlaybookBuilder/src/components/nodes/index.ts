/**
 * Node Components — Barrel Export + nodeTypes Registry
 *
 * Exports all custom node components and the nodeTypes map for @xyflow/react v12.
 * The nodeTypes map must be defined outside the component to avoid re-renders.
 */

import type { NodeTypes } from "@xyflow/react";

// Base component
export { BaseNode, nodeColorSchemes } from "./BaseNode";

// Node implementations
export { StartNode } from "./StartNode";
export { AiAnalysisNode } from "./AiAnalysisNode";
export { AiCompletionNode } from "./AiCompletionNode";
export { ConditionNode } from "./ConditionNode";
export { DeliverOutputNode } from "./DeliverOutputNode";
export { CreateTaskNode } from "./CreateTaskNode";
export { SendEmailNode } from "./SendEmailNode";
export { WaitNode } from "./WaitNode";

// Import components for registry
import { StartNode } from "./StartNode";
import { AiAnalysisNode } from "./AiAnalysisNode";
import { AiCompletionNode } from "./AiCompletionNode";
import { ConditionNode } from "./ConditionNode";
import { DeliverOutputNode } from "./DeliverOutputNode";
import { CreateTaskNode } from "./CreateTaskNode";
import { SendEmailNode } from "./SendEmailNode";
import { WaitNode } from "./WaitNode";

/**
 * Node type registry for @xyflow/react v12.
 * Maps PlaybookNodeType strings to their React components.
 *
 * IMPORTANT: Defined at module scope (outside components) to prevent
 * unnecessary re-renders per React Flow documentation.
 */
export const nodeTypes: NodeTypes = {
    start: StartNode,
    aiAnalysis: AiAnalysisNode,
    aiCompletion: AiCompletionNode,
    condition: ConditionNode,
    deliverOutput: DeliverOutputNode,
    createTask: CreateTaskNode,
    sendEmail: SendEmailNode,
    wait: WaitNode,
};
