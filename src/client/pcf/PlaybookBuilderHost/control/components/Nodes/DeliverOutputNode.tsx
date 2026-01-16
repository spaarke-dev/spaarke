/**
 * Deliver Output Node - outputs data to Power Apps or other consumers.
 */

import * as React from 'react';
import { Text, tokens } from '@fluentui/react-components';
import { DocumentArrowRight20Regular } from '@fluentui/react-icons';
import { BaseNode } from './BaseNode';
import type { PlaybookNodeData } from '../../stores';

interface DeliverOutputNodeProps {
  data: PlaybookNodeData;
  selected?: boolean;
}

/**
 * Deliver Output node - outputs data to Power Apps or other consumers.
 * Terminal node (no output handle).
 */
export const DeliverOutputNode = React.memo(function DeliverOutputNode({
  data,
  selected,
}: DeliverOutputNodeProps) {
  return (
    <BaseNode
      data={data}
      selected={selected}
      icon={<DocumentArrowRight20Regular />}
      typeLabel="Deliver Output"
      sourceHandleCount={0}
    >
      {data.outputVariable && (
        <Text size={100} style={{ color: tokens.colorNeutralForeground3 }}>
          Variable: {data.outputVariable}
        </Text>
      )}
    </BaseNode>
  );
});
