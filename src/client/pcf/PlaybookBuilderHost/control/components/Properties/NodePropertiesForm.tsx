/**
 * NodePropertiesForm - Form for editing node properties
 *
 * Uses Accordion for collapsible sections to reduce clutter.
 * Auto-saves changes to the Zustand store.
 */

import * as React from 'react';
import { useCallback, useMemo } from 'react';
import {
  makeStyles,
  tokens,
  Text,
  Input,
  Label,
  SpinButton,
  Badge,
  Button,
  Accordion,
  AccordionHeader,
  AccordionItem,
  AccordionPanel,
  shorthands,
} from '@fluentui/react-components';
import { Delete20Regular } from '@fluentui/react-icons';
import type {
  SpinButtonChangeEvent,
  SpinButtonOnChangeData,
} from '@fluentui/react-components';
import { useCanvasStore, PlaybookNode, PlaybookNodeData, PlaybookNodeType } from '../../stores';
import { ScopeSelector } from './ScopeSelector';
import { ConditionEditor } from './ConditionEditor';
import { ModelSelector } from './ModelSelector';

const useStyles = makeStyles({
  form: {
    display: 'flex',
    flexDirection: 'column',
  },
  headerRow: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    marginBottom: tokens.spacingVerticalS,
    ...shorthands.padding(tokens.spacingVerticalS, 0),
  },
  nodeTypeHeader: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
  },
  field: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
    marginBottom: tokens.spacingVerticalS,
  },
  fieldHint: {
    color: tokens.colorNeutralForeground3,
  },
  accordionPanel: {
    ...shorthands.padding(tokens.spacingVerticalS, tokens.spacingHorizontalXS),
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
const nodeTypeBadgeColors: Record<
  PlaybookNodeType,
  'brand' | 'warning' | 'success' | 'important'
> = {
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
 * Uses collapsible Accordion sections for cleaner UI.
 * Auto-saves changes to the Zustand store.
 */
export const NodePropertiesForm = React.memo(function NodePropertiesForm({
  node,
}: NodePropertiesFormProps) {
  const styles = useStyles();
  const updateNode = useCanvasStore((state) => state.updateNode);
  const removeNode = useCanvasStore((state) => state.removeNode);

  const isConditionNode = node.data.type === 'condition';
  const isAiNode = node.data.type === 'aiAnalysis' || node.data.type === 'aiCompletion';

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

  // Handler for deleting the node
  const handleDelete = useCallback(() => {
    removeNode(node.id);
  }, [node.id, removeNode]);

  // Get current values with defaults
  const values = useMemo(
    () => ({
      label: node.data.label || '',
      outputVariable: node.data.outputVariable || '',
      timeoutSeconds: node.data.timeoutSeconds ?? 300,
      retryCount: node.data.retryCount ?? 0,
      conditionJson:
        node.data.conditionJson ||
        '{\n  "field": "",\n  "operator": "equals",\n  "value": ""\n}',
      skillIds: node.data.skillIds || [],
      knowledgeIds: node.data.knowledgeIds || [],
      toolId: node.data.toolId,
      modelDeploymentId: node.data.modelDeploymentId,
    }),
    [node.data]
  );

  // Default open sections - Basic is always open
  const defaultOpenItems = ['basic'];

  return (
    <div className={styles.form}>
      {/* Header row: Node type badge + delete button */}
      <div className={styles.headerRow}>
        <div className={styles.nodeTypeHeader}>
          <Text size={200} weight="semibold">
            Type:
          </Text>
          <Badge appearance="filled" color={nodeTypeBadgeColors[node.data.type]}>
            {nodeTypeLabels[node.data.type]}
          </Badge>
        </div>
        <Button
          appearance="subtle"
          icon={<Delete20Regular />}
          onClick={handleDelete}
          aria-label="Delete node"
          title="Delete node"
        />
      </div>

      <Accordion
        multiple
        collapsible
        defaultOpenItems={defaultOpenItems}
      >
        {/* Basic Properties Section */}
        <AccordionItem value="basic">
          <AccordionHeader size="small">Basic</AccordionHeader>
          <AccordionPanel className={styles.accordionPanel}>
            <div className={styles.field}>
              <Label htmlFor="node-label" required size="small">
                Name
              </Label>
              <Input
                id="node-label"
                size="small"
                value={values.label}
                onChange={handleTextChange('label')}
                placeholder="Enter node name"
              />
            </div>

            <div className={styles.field}>
              <Label htmlFor="output-variable" size="small">
                Output Variable
              </Label>
              <Input
                id="output-variable"
                size="small"
                value={values.outputVariable}
                onChange={handleTextChange('outputVariable')}
                placeholder="e.g., extractedEntities"
              />
              <Text size={100} className={styles.fieldHint}>
                Reference as {'{{'}
                {values.outputVariable || 'variableName'}
                {'}}'}
              </Text>
            </div>
          </AccordionPanel>
        </AccordionItem>

        {/* AI Model Section (only for AI nodes) */}
        {isAiNode && (
          <AccordionItem value="aiModel">
            <AccordionHeader size="small">AI Model</AccordionHeader>
            <AccordionPanel className={styles.accordionPanel}>
              <ModelSelector
                nodeId={node.id}
                selectedModelId={values.modelDeploymentId}
                onUpdate={updateNode}
              />
            </AccordionPanel>
          </AccordionItem>
        )}

        {/* Skills Section */}
        <AccordionItem value="skills">
          <AccordionHeader size="small">Skills</AccordionHeader>
          <AccordionPanel className={styles.accordionPanel}>
            <ScopeSelector
              nodeId={node.id}
              nodeType={node.data.type}
              skillIds={values.skillIds}
              knowledgeIds={[]}
              toolId={undefined}
              showSkills={true}
              showKnowledge={false}
              showTools={false}
            />
          </AccordionPanel>
        </AccordionItem>

        {/* Knowledge Section */}
        <AccordionItem value="knowledge">
          <AccordionHeader size="small">Knowledge</AccordionHeader>
          <AccordionPanel className={styles.accordionPanel}>
            <ScopeSelector
              nodeId={node.id}
              nodeType={node.data.type}
              skillIds={[]}
              knowledgeIds={values.knowledgeIds}
              toolId={undefined}
              showSkills={false}
              showKnowledge={true}
              showTools={false}
            />
          </AccordionPanel>
        </AccordionItem>

        {/* Tools Section */}
        <AccordionItem value="tools">
          <AccordionHeader size="small">Tools</AccordionHeader>
          <AccordionPanel className={styles.accordionPanel}>
            <ScopeSelector
              nodeId={node.id}
              nodeType={node.data.type}
              skillIds={[]}
              knowledgeIds={[]}
              toolId={values.toolId}
              showSkills={false}
              showKnowledge={false}
              showTools={true}
            />
          </AccordionPanel>
        </AccordionItem>

        {/* Runtime Settings Section (formerly Execution) */}
        <AccordionItem value="runtime">
          <AccordionHeader size="small">Runtime Settings</AccordionHeader>
          <AccordionPanel className={styles.accordionPanel}>
            <div className={styles.field}>
              <Label htmlFor="timeout-seconds" size="small">
                Timeout (seconds)
              </Label>
              <SpinButton
                id="timeout-seconds"
                size="small"
                value={values.timeoutSeconds}
                onChange={handleNumberChange('timeoutSeconds')}
                min={30}
                max={3600}
                step={30}
              />
              <Text size={100} className={styles.fieldHint}>
                30-3600 seconds
              </Text>
            </div>

            <div className={styles.field}>
              <Label htmlFor="retry-count" size="small">
                Retry Count
              </Label>
              <SpinButton
                id="retry-count"
                size="small"
                value={values.retryCount}
                onChange={handleNumberChange('retryCount')}
                min={0}
                max={5}
                step={1}
              />
              <Text size={100} className={styles.fieldHint}>
                0-5 retries on failure
              </Text>
            </div>
          </AccordionPanel>
        </AccordionItem>

        {/* Condition Expression (only for condition nodes) */}
        {isConditionNode && (
          <AccordionItem value="condition">
            <AccordionHeader size="small">Condition</AccordionHeader>
            <AccordionPanel className={styles.accordionPanel}>
              <ConditionEditor
                conditionJson={values.conditionJson}
                onChange={(json) => handleUpdate('conditionJson', json)}
              />
            </AccordionPanel>
          </AccordionItem>
        )}
      </Accordion>
    </div>
  );
});
