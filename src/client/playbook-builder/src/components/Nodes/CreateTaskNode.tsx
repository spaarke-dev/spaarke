import { memo } from 'react';
import { Text, tokens } from '@fluentui/react-components';
import { TaskListSquareLtr20Regular } from '@fluentui/react-icons';
import { BaseNode } from './BaseNode';
import type { PlaybookNodeData } from '../../stores';

interface CreateTaskNodeProps {
  data: PlaybookNodeData;
  selected?: boolean;
}

/**
 * Create Task node - creates a Dataverse task record.
 * Use for: follow-up actions, workflow assignments.
 */
export const CreateTaskNode = memo(function CreateTaskNode({ data, selected }: CreateTaskNodeProps) {
  return (
    <BaseNode data={data} selected={selected} icon={<TaskListSquareLtr20Regular />} typeLabel="Create Task">
      {data.outputVariable && (
        <Text size={100} style={{ color: tokens.colorNeutralForeground3 }}>
          Output: {data.outputVariable}
        </Text>
      )}
    </BaseNode>
  );
});
