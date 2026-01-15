/**
 * Node Type Registry for React Flow
 *
 * Maps node type strings to their React components.
 * React Flow passes props like { data, selected, id, ... } to custom nodes.
 */

import type { ComponentType } from 'react';
import type { NodeTypes } from 'react-flow-renderer';
import { AiAnalysisNode } from './AiAnalysisNode';
import { AiCompletionNode } from './AiCompletionNode';
import { ConditionNode } from './ConditionNode';
import { DeliverOutputNode } from './DeliverOutputNode';
import { CreateTaskNode } from './CreateTaskNode';
import { SendEmailNode } from './SendEmailNode';
import { WaitNode } from './WaitNode';

/**
 * Node type registry for React Flow.
 * Maps node type strings to their React components.
 *
 * Note: Type assertion needed because our custom node components use a subset
 * of NodeProps (data, selected) rather than the full interface.
 */
export const nodeTypes = {
  aiAnalysis: AiAnalysisNode,
  aiCompletion: AiCompletionNode,
  condition: ConditionNode,
  deliverOutput: DeliverOutputNode,
  createTask: CreateTaskNode,
  sendEmail: SendEmailNode,
  wait: WaitNode,
} as NodeTypes;
