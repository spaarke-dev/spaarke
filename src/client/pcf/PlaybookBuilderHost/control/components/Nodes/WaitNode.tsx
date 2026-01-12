/**
 * Wait Node - pauses execution for human approval or time delay.
 */

import * as React from 'react';
import { Text, tokens } from '@fluentui/react-components';
import { Clock20Regular } from '@fluentui/react-icons';
import { BaseNode } from './BaseNode';
import type { PlaybookNodeData } from '../../stores';

interface WaitNodeProps {
  data: PlaybookNodeData;
  selected?: boolean;
}

/**
 * Wait node - pauses execution for human approval or time delay.
 * Use for: approval gates, human review steps, timed waits.
 */
export const WaitNode = React.memo(function WaitNode({
  data,
  selected,
}: WaitNodeProps) {
  return (
    <BaseNode
      data={data}
      selected={selected}
      icon={<Clock20Regular />}
      typeLabel="Wait"
    >
      {data.outputVariable && (
        <Text size={100} style={{ color: tokens.colorNeutralForeground3 }}>
          Output: {data.outputVariable}
        </Text>
      )}
    </BaseNode>
  );
});
