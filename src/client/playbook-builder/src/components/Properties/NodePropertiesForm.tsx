import { useCallback, useMemo } from 'react';
import {
  makeStyles,
  tokens,
  Text,
  Input,
  Label,
  Textarea,
  SpinButton,
  Badge,
  Divider,
  shorthands,
} from '@fluentui/react-components';
import type { SpinButtonChangeEvent, SpinButtonOnChangeData } from '@fluentui/react-components';
import { useCanvasStore, PlaybookNode, PlaybookNodeData, PlaybookNodeType } from '../../stores';
import { ScopeSelector } from './ScopeSelector';

const useStyles = makeStyles({
  form: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
  },
  field: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
  },
  nodeType: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    marginBottom: tokens.spacingVerticalS,
  },
  sectionHeader: {
    marginTop: tokens.spacingVerticalM,
    marginBottom: tokens.spacingVerticalS,
  },
  readOnlyField: {
    backgroundColor: tokens.colorNeutralBackground3,
    ...shorthands.padding(tokens.spacingVerticalS, tokens.spacingHorizontalM),
    ...shorthands.borderRadius(tokens.borderRadiusMedium),
  },
  conditionEditor: {
    fontFamily: 'monospace',
    fontSize: '12px',
  },
});

// Human-readable labels for node types
const nodeTypeLabels: Record<PlaybookNodeType, string> = {
  aiAnalysis: 'AI Analysis',
  aiCompletion: 'AI Completion',
  condition: 'Condition',
  deliverOutput: 'Deliver Output',
  createTask: 'Create Task',
  sendEmail: 'Send Email',
  wait: 'Wait',
};

// Badge colors for node types
const nodeTypeBadgeColors: Record<PlaybookNodeType, 'brand' | 'warning' | 'success' | 'important'> = {
  aiAnalysis: 'brand',
  aiCompletion: 'brand',
  condition: 'warning',
  deliverOutput: 'success',
  createTask: 'important',
  sendEmail: 'important',
  wait: 'important',
};

interface NodePropertiesFormProps {
  node: PlaybookNode;
}

/**
 * Form component for editing node properties.
 * Auto-saves changes to the Zustand store.
 */
export function NodePropertiesForm({ node }: NodePropertiesFormProps) {
  const styles = useStyles();
  const updateNode = useCanvasStore((state) => state.updateNode);

  const isConditionNode = node.data.type === 'condition';

  // Create a memoized update handler
  const handleUpdate = useCallback(
    (field: keyof PlaybookNodeData, value: unknown) => {
      updateNode(node.id, { [field]: value });
    },
    [node.id, updateNode]
  );

  // Handler for text inputs
  const handleTextChange = useCallback(
    (field: keyof PlaybookNodeData) =>
      (e: React.ChangeEvent<HTMLInputElement | HTMLTextAreaElement>) => {
        handleUpdate(field, e.target.value);
      },
    [handleUpdate]
  );

  // Handler for spin button (number inputs)
  const handleNumberChange = useCallback(
    (field: keyof PlaybookNodeData) =>
      (_e: SpinButtonChangeEvent, data: SpinButtonOnChangeData) => {
        if (data.value !== undefined && data.value !== null) {
          handleUpdate(field, data.value);
        }
      },
    [handleUpdate]
  );

  // Get current values with defaults
  const values = useMemo(
    () => ({
      label: node.data.label || '',
      outputVariable: node.data.outputVariable || '',
      timeoutSeconds: node.data.timeoutSeconds ?? 300,
      retryCount: node.data.retryCount ?? 0,
      conditionJson: node.data.conditionJson || '{\n  "field": "",\n  "operator": "equals",\n  "value": ""\n}',
      skillIds: node.data.skillIds || [],
      knowledgeIds: node.data.knowledgeIds || [],
      toolId: node.data.toolId,
    }),
    [node.data]
  );

  return (
    <div className={styles.form}>
      {/* Node Type Badge (read-only) */}
      <div className={styles.nodeType}>
        <Text size={200} weight="semibold">
          Type:
        </Text>
        <Badge appearance="filled" color={nodeTypeBadgeColors[node.data.type]}>
          {nodeTypeLabels[node.data.type]}
        </Badge>
      </div>

      {/* Basic Properties */}
      <div className={styles.field}>
        <Label htmlFor="node-label" required>
          Name
        </Label>
        <Input
          id="node-label"
          value={values.label}
          onChange={handleTextChange('label')}
          placeholder="Enter node name"
        />
      </div>

      <div className={styles.field}>
        <Label htmlFor="output-variable">Output Variable</Label>
        <Input
          id="output-variable"
          value={values.outputVariable}
          onChange={handleTextChange('outputVariable')}
          placeholder="e.g., extractedEntities"
        />
        <Text size={100} style={{ color: tokens.colorNeutralForeground3 }}>
          Reference in other nodes as {'{{'}
          {values.outputVariable || 'variableName'}
          {'}}'}
        </Text>
      </div>

      {/* Execution Settings */}
      <Divider className={styles.sectionHeader}>Execution Settings</Divider>

      <div className={styles.field}>
        <Label htmlFor="timeout-seconds">Timeout (seconds)</Label>
        <SpinButton
          id="timeout-seconds"
          value={values.timeoutSeconds}
          onChange={handleNumberChange('timeoutSeconds')}
          min={30}
          max={3600}
          step={30}
        />
        <Text size={100} style={{ color: tokens.colorNeutralForeground3 }}>
          Maximum time before timeout (30-3600s)
        </Text>
      </div>

      <div className={styles.field}>
        <Label htmlFor="retry-count">Retry Count</Label>
        <SpinButton
          id="retry-count"
          value={values.retryCount}
          onChange={handleNumberChange('retryCount')}
          min={0}
          max={5}
          step={1}
        />
        <Text size={100} style={{ color: tokens.colorNeutralForeground3 }}>
          Number of retry attempts on failure (0-5)
        </Text>
      </div>

      {/* Scope Selector (skills, knowledge, tools) */}
      <ScopeSelector
        nodeId={node.id}
        nodeType={node.data.type}
        skillIds={values.skillIds}
        knowledgeIds={values.knowledgeIds}
        toolId={values.toolId}
      />

      {/* Condition Editor (only for condition nodes) */}
      {isConditionNode && (
        <>
          <Divider className={styles.sectionHeader}>Condition Expression</Divider>

          <div className={styles.field}>
            <Label htmlFor="condition-json">Condition JSON</Label>
            <Textarea
              id="condition-json"
              className={styles.conditionEditor}
              value={values.conditionJson}
              onChange={handleTextChange('conditionJson')}
              rows={6}
              resize="vertical"
              placeholder='{"field": "score", "operator": ">=", "value": 0.8}'
            />
            <Text size={100} style={{ color: tokens.colorNeutralForeground3 }}>
              JSON expression to evaluate. True branch executes if condition passes.
            </Text>
          </div>
        </>
      )}
    </div>
  );
}
