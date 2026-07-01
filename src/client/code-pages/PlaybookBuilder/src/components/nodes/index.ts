/**
 * Node Components — Barrel Export + nodeTypes Registry
 *
 * Exports all custom node components and the nodeTypes map for @xyflow/react v12.
 * The nodeTypes map must be defined outside the component to avoid re-renders.
 */

import type { NodeTypes } from '@xyflow/react';

// Base component
export { BaseNode, nodeColorSchemes } from './BaseNode';

// Node implementations
export { StartNode } from './StartNode';
export { AiAnalysisNode } from './AiAnalysisNode';
export { AiCompletionNode } from './AiCompletionNode';
export { ConditionNode } from './ConditionNode';
export { DeliverOutputNode } from './DeliverOutputNode';
export { DeliverToIndexNode } from './DeliverToIndexNode';
export { UpdateRecordNode } from './UpdateRecordNode';
export { CreateTaskNode } from './CreateTaskNode';
export { SendEmailNode } from './SendEmailNode';
export { CreateNotificationNode } from './CreateNotificationNode';
export { WaitNode } from './WaitNode';
export { EntityNameValidatorNode } from './EntityNameValidatorNode';
// R7 Wave 8 task 089 (FR-27): warning-state shell for nodes whose executorType
// is not present in the local EXECUTOR_METADATA catalog. See UnknownNode.tsx
// for the rationale + coerceUnknownNodeTypes in canvasStore.ts for canvas-side
// detection.
export { UnknownNode } from './UnknownNode';

// Import components for registry
import { StartNode } from './StartNode';
import { AiAnalysisNode } from './AiAnalysisNode';
import { AiCompletionNode } from './AiCompletionNode';
import { ConditionNode } from './ConditionNode';
import { DeliverOutputNode } from './DeliverOutputNode';
import { DeliverToIndexNode } from './DeliverToIndexNode';
import { UpdateRecordNode } from './UpdateRecordNode';
import { CreateTaskNode } from './CreateTaskNode';
import { SendEmailNode } from './SendEmailNode';
import { CreateNotificationNode } from './CreateNotificationNode';
import { WaitNode } from './WaitNode';
import { EntityNameValidatorNode } from './EntityNameValidatorNode';
import { UnknownNode } from './UnknownNode';

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
  deliverToIndex: DeliverToIndexNode,
  updateRecord: UpdateRecordNode,
  createTask: CreateTaskNode,
  sendEmail: SendEmailNode,
  createNotification: CreateNotificationNode,
  wait: WaitNode,
  // R4 hotfix #2 (2026-06-26): task 004 left this missing — canvas fell back to
  // the default plain box. EntityNameValidator is a post-LLM Tool node; the
  // component renders icon + "Tool" type label + output preview to match peers.
  entityNameValidator: EntityNameValidatorNode,
  // R7 Wave 8 task 089 (FR-27): rendered when `node.data.executorType` is
  // undefined OR not present in EXECUTOR_METADATA. canvasStore's
  // coerceUnknownNodeTypes() rewrites `node.type = 'unknown'` during canvas
  // hydration so this entry actually gets dispatched by React Flow.
  unknown: UnknownNode,
};
