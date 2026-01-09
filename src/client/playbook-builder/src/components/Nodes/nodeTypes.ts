import type { ComponentType } from 'react';
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
 * React Flow passes props like { data, selected, id, ... } to custom nodes.
 * Our components extract what they need (data, selected).
 */
// eslint-disable-next-line @typescript-eslint/no-explicit-any
export const nodeTypes: Record<string, ComponentType<any>> = {
  aiAnalysis: AiAnalysisNode,
  aiCompletion: AiCompletionNode,
  condition: ConditionNode,
  deliverOutput: DeliverOutputNode,
  createTask: CreateTaskNode,
  sendEmail: SendEmailNode,
  wait: WaitNode,
};
