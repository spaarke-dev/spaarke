/**
 * AI Analysis Node - processes data with AI and produces structured output.
 */

import * as React from 'react';
import { Text, tokens } from '@fluentui/react-components';
import { BrainCircuit20Regular } from '@fluentui/react-icons';
import { BaseNode } from './BaseNode';
import type { PlaybookNodeData } from '../../stores';

interface AiAnalysisNodeProps {
  data: PlaybookNodeData;
  selected?: boolean;
}

/**
 * AI Analysis node - processes data with AI and produces structured output.
 * Use for: document analysis, entity extraction, classification.
 */
export const AiAnalysisNode = React.memo(function AiAnalysisNode({
  data,
  selected,
}: AiAnalysisNodeProps) {
  return (
    <BaseNode
      data={data}
      selected={selected}
      icon={<BrainCircuit20Regular />}
      typeLabel="AI Analysis"
    >
      {data.outputVariable && (
        <Text size={100} style={{ color: tokens.colorNeutralForeground3 }}>
          Output: {data.outputVariable}
        </Text>
      )}
    </BaseNode>
  );
});
