/**
 * EntityNameValidatorNode — Post-LLM Tool that scrubs hallucinated entity names.
 *
 * Visual parity peer with WaitNode / AiAnalysisNode (R4 hotfix #2 2026-06-26):
 * task 004 NODE_TYPE_INFO entry was structurally incomplete — there was no
 * dedicated node component, so React Flow fell back to a default plain box on
 * the canvas. This component closes the gap and brings the Tool to visual
 * parity with peer nodes (icon + type label "Tool" + Output preview).
 *
 * Uses @xyflow/react v12 NodeProps<Node<PlaybookNodeData>> generics + Fluent
 * UI v9 design tokens for all colors (ADR-021). Color scheme is owned by
 * BaseNode.tsx:141 (magenta — text-transform tool family alongside Wait).
 */

import React from 'react';
import type { Node, NodeProps } from '@xyflow/react';
import { tokens, Text } from '@fluentui/react-components';
import { ShieldCheckmark20Regular } from '@fluentui/react-icons';
import { BaseNode } from './BaseNode';
import type { PlaybookNodeData } from '../../types/canvas';

/**
 * Entity Name Validator node — scrubs LLM-emitted entity names not present in
 * an upstream-supplied allow-list. Backed by EntityNameValidatorNodeExecutor
 * (server, ActionType=141). The "Tool" type label distinguishes it from AI
 * Analysis / Workflow / Output node families per task 003/004 design.
 */
export const EntityNameValidatorNode = React.memo(function EntityNameValidatorNode({
  data,
  selected,
}: NodeProps<Node<PlaybookNodeData>>) {
  return (
    <BaseNode data={data} selected={selected} icon={<ShieldCheckmark20Regular />} typeLabel="Tool">
      {data.outputVariable && (
        <Text size={100} style={{ color: tokens.colorNeutralForeground3 }}>
          Output: {data.outputVariable}
        </Text>
      )}
    </BaseNode>
  );
});
