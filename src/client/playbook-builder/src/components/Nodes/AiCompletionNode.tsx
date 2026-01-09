import { memo } from 'react';
import { Text, tokens } from '@fluentui/react-components';
import { BrainCircuit20Regular } from '@fluentui/react-icons';
import { BaseNode } from './BaseNode';
import type { PlaybookNodeData } from '../../stores';

interface AiCompletionNodeProps {
  data: PlaybookNodeData;
  selected?: boolean;
}

/**
 * AI Completion node - generates text using AI completion.
 * Use for: drafting emails, creating summaries, generating content.
 */
export const AiCompletionNode = memo(function AiCompletionNode({ data, selected }: AiCompletionNodeProps) {
  return (
    <BaseNode data={data} selected={selected} icon={<BrainCircuit20Regular />} typeLabel="AI Completion">
      {data.outputVariable && (
        <Text size={100} style={{ color: tokens.colorNeutralForeground3 }}>
          Output: {data.outputVariable}
        </Text>
      )}
    </BaseNode>
  );
});
