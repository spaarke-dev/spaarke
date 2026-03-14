/**
 * UpdateRecordNode — Updates a Dataverse entity record with AI analysis results.
 *
 * Migrated to @xyflow/react v12 NodeProps<Node<PlaybookNodeData>> generics.
 * Uses Fluent UI v9 design tokens for all colors (ADR-021).
 */

import React from 'react';
import type { Node, NodeProps } from '@xyflow/react';
import { tokens, Text } from '@fluentui/react-components';
import { DatabaseArrowUp20Regular } from '@fluentui/react-icons';
import { BaseNode } from './BaseNode';
import type { PlaybookNodeData } from '../../types/canvas';

/**
 * Update Record node — writes fields to a Dataverse entity record.
 * Use for: writing AI analysis results back to document fields,
 * updating status fields, setting lookup references.
 */
export const UpdateRecordNode = React.memo(function UpdateRecordNode({
  data,
  selected,
}: NodeProps<Node<PlaybookNodeData>>) {
  // Extract entity name from configJson if available
  let entityHint = '';
  if (data.configJson) {
    try {
      const config = JSON.parse(data.configJson as string);
      if (config.entityLogicalName) {
        entityHint = config.entityLogicalName;
      }
    } catch {
      /* ignore parse errors */
    }
  }

  return (
    <BaseNode data={data} selected={selected} icon={<DatabaseArrowUp20Regular />} typeLabel="Update Record">
      {entityHint && (
        <Text size={100} style={{ color: tokens.colorNeutralForeground3 }}>
          {entityHint}
        </Text>
      )}
      {data.outputVariable && (
        <Text size={100} style={{ color: tokens.colorNeutralForeground3 }}>
          Output: {data.outputVariable}
        </Text>
      )}
    </BaseNode>
  );
});
