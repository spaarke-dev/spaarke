/**
 * CreateNotificationNode — Creates an in-app notification via appnotification.
 *
 * Migrated to @xyflow/react v12 NodeProps<Node<PlaybookNodeData>> generics.
 * Uses Fluent UI v9 design tokens for all colors (ADR-021).
 */

import React from 'react';
import type { Node, NodeProps } from '@xyflow/react';
import { tokens, Text } from '@fluentui/react-components';
import { Alert20Regular } from '@fluentui/react-icons';
import { BaseNode } from './BaseNode';
import type { PlaybookNodeData } from '../../types/canvas';

/**
 * Create Notification node — creates an in-app notification for a user.
 * Use for: daily updates, alerts, user notifications.
 */
export const CreateNotificationNode = React.memo(function CreateNotificationNode({
  data,
  selected,
}: NodeProps<Node<PlaybookNodeData>>) {
  return (
    <BaseNode data={data} selected={selected} icon={<Alert20Regular />} typeLabel="Create Notification">
      {data.outputVariable && (
        <Text size={100} style={{ color: tokens.colorNeutralForeground3 }}>
          Output: {data.outputVariable}
        </Text>
      )}
    </BaseNode>
  );
});
