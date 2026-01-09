import { memo } from 'react';
import { Text, tokens } from '@fluentui/react-components';
import { Mail20Regular } from '@fluentui/react-icons';
import { BaseNode } from './BaseNode';
import type { PlaybookNodeData } from '../../stores';

interface SendEmailNodeProps {
  data: PlaybookNodeData;
  selected?: boolean;
}

/**
 * Send Email node - sends an email via Dataverse.
 * Use for: notifications, reports, automated communications.
 */
export const SendEmailNode = memo(function SendEmailNode({ data, selected }: SendEmailNodeProps) {
  return (
    <BaseNode data={data} selected={selected} icon={<Mail20Regular />} typeLabel="Send Email">
      {data.outputVariable && (
        <Text size={100} style={{ color: tokens.colorNeutralForeground3 }}>
          Output: {data.outputVariable}
        </Text>
      )}
    </BaseNode>
  );
});
