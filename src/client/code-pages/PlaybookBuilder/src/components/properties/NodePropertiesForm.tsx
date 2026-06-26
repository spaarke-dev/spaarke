/**
 * NodePropertiesForm — Main properties form for a selected playbook node.
 *
 * Renders collapsible Accordion sections:
 *   - Basic (always): Name, Output Variable
 *   - AI Model (aiAnalysis, aiCompletion only): ModelSelector
 *   - Prompt Configuration (aiAnalysis, aiCompletion only): PromptSchemaForm | PromptSchemaEditor
 *   - Type-Specific Config: DeliverOutputForm | SendEmailForm | CreateTaskForm | AiCompletionForm | WaitForm
 *   - Skills (capability-dependent): ScopeSelector
 *   - Knowledge (capability-dependent): ScopeSelector
 *   - Tools (capability-dependent): ScopeSelector
 *   - Condition (condition nodes only): ConditionEditor
 *   - Runtime Settings: Timeout, Retry Count
 *
 * All changes flow through canvasStore.updateNodeData().
 */

import { memo, useCallback, useEffect, useMemo, useState } from 'react';
import {
  makeStyles,
  tokens,
  shorthands,
  Accordion,
  AccordionItem,
  AccordionHeader,
  AccordionPanel,
  Input,
  Label,
  SpinButton,
  Button,
  Text,
  Divider,
} from '@fluentui/react-components';
import { Delete20Regular } from '@fluentui/react-icons';
import type { PlaybookNode } from '../../types/canvas';
import { useCanvasStore } from '../../stores/canvasStore';

// Sub-components
import { ActionSelector } from './ActionSelector';
import { ModelSelector } from './ModelSelector';
import { ScopeSelector } from './ScopeSelector';
import { ConditionEditor } from './ConditionEditor';
import { DeliverOutputForm } from './DeliverOutputForm';
import { DeliverToIndexForm } from './DeliverToIndexForm';
import { SendEmailForm } from './SendEmailForm';
import { CreateTaskForm } from './CreateTaskForm';
import { AiCompletionForm } from './AiCompletionForm';
import { WaitForm } from './WaitForm';
import { UpdateRecordForm } from './UpdateRecordForm';
import { CreateNotificationForm } from './CreateNotificationForm';
import { LookupUserMembershipForm } from './LookupUserMembershipForm';
import { EntityNameValidatorForm } from './EntityNameValidatorForm';
import { NodeValidationBadge } from './NodeValidationBadge';
import { PromptSchemaForm } from './PromptSchemaForm';
import { PromptSchemaEditor } from './PromptSchemaEditor';
import { RenameGuardDialog, type RenameGuardAction } from './RenameGuardDialog';
import { findOutputVariableReferences, type OutputVariableReference } from '../../services/canvasValidation';
import type { PromptSchema } from '../../types/promptSchema';

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

interface NodePropertiesFormProps {
  node: PlaybookNode;
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    ...shorthands.gap('0px'),
  },
  header: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    ...shorthands.padding('8px', '12px'),
  },
  headerLeft: {
    display: 'flex',
    alignItems: 'center',
    ...shorthands.gap('8px'),
  },
  typeBadge: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground3,
    textTransform: 'capitalize' as const,
  },
  accordionPanel: {
    ...shorthands.padding('12px', '16px', '16px'),
  },
  fieldGroup: {
    display: 'flex',
    flexDirection: 'column',
    ...shorthands.gap('6px'),
    marginBottom: '12px',
  },
  deleteSection: {
    ...shorthands.padding('12px'),
    ...shorthands.borderTop('1px', 'solid', tokens.colorNeutralStroke2),
  },
});

// ---------------------------------------------------------------------------
// Node type labels
// ---------------------------------------------------------------------------

const NODE_TYPE_LABELS: Record<string, string> = {
  start: 'Start',
  aiAnalysis: 'AI Analysis',
  aiCompletion: 'AI Completion',
  condition: 'Condition',
  deliverOutput: 'Deliver Output',
  updateRecord: 'Update Record',
  createTask: 'Create Task',
  sendEmail: 'Send Email',
  createNotification: 'Create Notification',
  lookupUserMembership: 'Lookup User Membership',
  entityNameValidator: 'Entity Name Validator',
  wait: 'Wait',
};

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const NodePropertiesForm = memo(function NodePropertiesForm({ node }: NodePropertiesFormProps) {
  const styles = useStyles();
  const updateNodeData = useCanvasStore(s => s.updateNodeData);
  const removeNode = useCanvasStore(s => s.removeNode);
  // R3 P9 H2 (task 091): rename-guard wiring. Read nodes from store (NOT from
  // props) to scan current canvas state. renameOutputVariableReferences mutates
  // every node whose serialized config references the renamed variable.
  const allNodes = useCanvasStore(s => s.nodes);
  const renameOutputVariableReferences = useCanvasStore(s => s.renameOutputVariableReferences);

  const nodeType = node.data.type;
  const isAiNode = nodeType === 'aiAnalysis' || nodeType === 'aiCompletion';
  const isConditionNode = nodeType === 'condition';
  const isStartNode = nodeType === 'start';

  // Determine which type-specific form to show
  const hasTypeForm = [
    'deliverOutput',
    'deliverToIndex',
    'updateRecord',
    'sendEmail',
    'createTask',
    'createNotification',
    'aiCompletion',
    'wait',
    'lookupUserMembership',
    'entityNameValidator',
  ].includes(nodeType);

  // Generic field updater
  const handleUpdate = useCallback(
    (field: string, value: unknown) => {
      updateNodeData(node.id, { [field]: value });
    },
    [node.id, updateNodeData]
  );

  // configJson handler for type-specific forms
  const handleConfigChange = useCallback(
    (json: string) => {
      updateNodeData(node.id, { configJson: json });
    },
    [node.id, updateNodeData]
  );

  const handleDelete = useCallback(() => {
    removeNode(node.id);
  }, [node.id, removeNode]);

  // -----------------------------------------------------------------------
  // R3 P9 H2 (task 091) — OutputVariable rename guard (FR-3H2.1 / AC-H2.1).
  //
  // outputVarDraft is a CONTROLLED local mirror of node.data.outputVariable.
  // On every keystroke we mutate the draft (instant feedback in the input).
  // On blur we compare the committed canvas value (oldName) to the typed
  // value (newName); if non-trivial AND other nodes reference oldName, open
  // the rename-guard dialog instead of committing the rename.
  //
  // Using onBlur (not onChange) prevents the dialog from firing per-keystroke.
  // -----------------------------------------------------------------------
  const committedOutputVar = node.data.outputVariable ?? '';
  const [outputVarDraft, setOutputVarDraft] = useState<string>(committedOutputVar);
  const [renameGuard, setRenameGuard] = useState<{
    open: boolean;
    oldName: string;
    newName: string;
    references: OutputVariableReference[];
  } | null>(null);

  // Sync the controlled local draft when the canvas-committed value changes
  // (node selection change, auto-rename rewrite, external state mutation).
  // We only resync when the dialog is closed to avoid clobbering a user
  // mid-decision.
  useEffect(() => {
    if (renameGuard === null) {
      setOutputVarDraft(committedOutputVar);
    }
  }, [committedOutputVar, renameGuard]);

  const commitOutputVariableRename = useCallback(
    (newName: string) => {
      // Commit on the node itself.
      updateNodeData(node.id, { outputVariable: newName });
    },
    [node.id, updateNodeData]
  );

  const handleOutputVariableCommit = useCallback(() => {
    const oldName = committedOutputVar.trim();
    const newName = outputVarDraft.trim();

    // No-op cases: unchanged value, empty old (first set), or identical.
    if (newName === committedOutputVar) return;
    if (oldName === '') {
      commitOutputVariableRename(outputVarDraft);
      return;
    }
    if (oldName === newName) {
      // Pure whitespace change — still commit so users can normalize spacing.
      commitOutputVariableRename(outputVarDraft);
      return;
    }

    // Scan all OTHER nodes for {{oldName.output.*}} references.
    const references = findOutputVariableReferences(oldName, allNodes, node.id);

    if (references.length === 0) {
      // No downstream impact — apply rename immediately.
      commitOutputVariableRename(outputVarDraft);
      return;
    }

    // Open the rename-guard dialog (caller decides next step).
    setRenameGuard({ open: true, oldName, newName, references });
  }, [committedOutputVar, outputVarDraft, allNodes, node.id, commitOutputVariableRename]);

  const handleRenameGuardResolve = useCallback(
    (action: RenameGuardAction) => {
      if (!renameGuard) return;
      const { oldName, newName } = renameGuard;

      if (action === 'autoRename') {
        // Rewrite all references FIRST so newName resolves immediately, then
        // commit the new name on the renamed node.
        renameOutputVariableReferences(oldName, newName);
        commitOutputVariableRename(outputVarDraft);
      } else {
        // 'keepOldName' or 'cancel' — revert the draft to the committed value.
        setOutputVarDraft(committedOutputVar);
      }

      setRenameGuard(null);
    },
    [renameGuard, committedOutputVar, outputVarDraft, commitOutputVariableRename, renameOutputVariableReferences]
  );

  // Prompt Configuration editor mode: "form" (Level 1) or "editor" (Level 2)
  const [editorMode, setEditorMode] = useState<'form' | 'editor'>('form');

  const handlePromptSchemaChange = useCallback(
    (schema: PromptSchema) => {
      updateNodeData(node.id, { promptSchema: schema });
    },
    [node.id, updateNodeData]
  );

  // Default open accordion items — only "basic" tab open initially
  const defaultOpenItems = useMemo(() => ['basic'], []);

  return (
    <div className={styles.root}>
      {/* Header */}
      <div className={styles.header}>
        <div className={styles.headerLeft}>
          <Text weight="semibold" size={400}>
            {node.data.label || 'Unnamed Node'}
          </Text>
          <Text className={styles.typeBadge}>{NODE_TYPE_LABELS[nodeType] ?? nodeType}</Text>
          <NodeValidationBadge
            validationErrors={node.data.validationErrors ?? []}
            warnings={node.data.warnings ?? []}
          />
        </div>
      </div>

      <Divider />

      <Accordion multiple collapsible defaultOpenItems={defaultOpenItems}>
        {/* Basic Section — Always shown */}
        <AccordionItem value="basic">
          <AccordionHeader size="small">Basic</AccordionHeader>
          <AccordionPanel className={styles.accordionPanel}>
            <div className={styles.fieldGroup}>
              <Label size="small" htmlFor={`${node.id}-name`}>
                Name
              </Label>
              <Input
                id={`${node.id}-name`}
                size="small"
                value={node.data.label}
                onChange={(_, data) => handleUpdate('label', data.value)}
              />
            </div>
            {!isStartNode && (
              <div className={styles.fieldGroup}>
                <Label size="small" htmlFor={`${node.id}-outputVar`}>
                  Output Variable
                </Label>
                <Input
                  id={`${node.id}-outputVar`}
                  size="small"
                  value={outputVarDraft}
                  onChange={(_, data) => setOutputVarDraft(data.value)}
                  onBlur={handleOutputVariableCommit}
                  placeholder={`output_${nodeType}`}
                />
              </div>
            )}
          </AccordionPanel>
        </AccordionItem>

        {/* Action Section — Only for AI nodes */}
        {isAiNode && (
          <AccordionItem value="action">
            <AccordionHeader size="small">Action</AccordionHeader>
            <AccordionPanel className={styles.accordionPanel}>
              <ActionSelector
                selectedActionId={node.data.actionId}
                onActionChange={id => handleUpdate('actionId', id)}
              />
            </AccordionPanel>
          </AccordionItem>
        )}

        {/* AI Model Section — Only for AI nodes */}
        {isAiNode && (
          <AccordionItem value="aiModel">
            <AccordionHeader size="small">AI Model</AccordionHeader>
            <AccordionPanel className={styles.accordionPanel}>
              <ModelSelector
                modelDeploymentId={node.data.modelDeploymentId}
                onModelChange={id => handleUpdate('modelDeploymentId', id)}
              />
            </AccordionPanel>
          </AccordionItem>
        )}

        {/* Prompt Configuration — Only for AI nodes */}
        {isAiNode && (
          <AccordionItem value="promptConfig">
            <AccordionHeader size="small">Prompt Configuration</AccordionHeader>
            <AccordionPanel className={styles.accordionPanel}>
              {editorMode === 'form' ? (
                <PromptSchemaForm
                  schema={node.data.promptSchema ?? null}
                  onChange={handlePromptSchemaChange}
                  onSwitchToEditor={() => setEditorMode('editor')}
                  nodeId={node.id}
                />
              ) : (
                <PromptSchemaEditor
                  schema={node.data.promptSchema ?? null}
                  onChange={handlePromptSchemaChange}
                  onSwitchToForm={() => setEditorMode('form')}
                />
              )}
            </AccordionPanel>
          </AccordionItem>
        )}

        {/* Type-Specific Configuration Form */}
        {hasTypeForm && (
          <AccordionItem value="typeConfig">
            <AccordionHeader size="small">Configuration</AccordionHeader>
            <AccordionPanel className={styles.accordionPanel}>
              {nodeType === 'deliverOutput' && (
                <DeliverOutputForm nodeId={node.id} data={node.data} onUpdate={handleUpdate} />
              )}
              {nodeType === 'deliverToIndex' && (
                <DeliverToIndexForm nodeId={node.id} data={node.data} onUpdate={handleUpdate} />
              )}
              {nodeType === 'updateRecord' && (
                <UpdateRecordForm
                  nodeId={node.id}
                  configJson={node.data.configJson ?? '{}'}
                  onConfigChange={handleConfigChange}
                />
              )}
              {nodeType === 'sendEmail' && (
                <SendEmailForm
                  nodeId={node.id}
                  configJson={node.data.configJson ?? '{}'}
                  onConfigChange={handleConfigChange}
                />
              )}
              {nodeType === 'createTask' && (
                <CreateTaskForm
                  nodeId={node.id}
                  configJson={node.data.configJson ?? '{}'}
                  onConfigChange={handleConfigChange}
                />
              )}
              {nodeType === 'createNotification' && (
                <CreateNotificationForm
                  nodeId={node.id}
                  configJson={node.data.configJson ?? '{}'}
                  onConfigChange={handleConfigChange}
                />
              )}
              {nodeType === 'aiCompletion' && (
                <AiCompletionForm
                  nodeId={node.id}
                  configJson={node.data.configJson ?? '{}'}
                  onConfigChange={handleConfigChange}
                />
              )}
              {nodeType === 'wait' && (
                <WaitForm
                  nodeId={node.id}
                  configJson={node.data.configJson ?? '{}'}
                  onConfigChange={handleConfigChange}
                />
              )}
              {nodeType === 'lookupUserMembership' && (
                <LookupUserMembershipForm
                  nodeId={node.id}
                  configJson={node.data.configJson ?? '{}'}
                  onConfigChange={handleConfigChange}
                />
              )}
              {nodeType === 'entityNameValidator' && (
                <EntityNameValidatorForm
                  nodeId={node.id}
                  configJson={node.data.configJson ?? '{}'}
                  onConfigChange={handleConfigChange}
                />
              )}
            </AccordionPanel>
          </AccordionItem>
        )}

        {/* Skills Section */}
        {!isStartNode && (
          <AccordionItem value="skills">
            <AccordionHeader size="small">Skills</AccordionHeader>
            <AccordionPanel className={styles.accordionPanel}>
              <ScopeSelector
                nodeType={nodeType}
                skillIds={node.data.skillIds ?? []}
                knowledgeIds={node.data.knowledgeIds ?? []}
                toolIds={node.data.toolIds ?? []}
                showSkills
                onSkillsChange={ids => handleUpdate('skillIds', ids)}
                onKnowledgeChange={() => {}}
                onToolsChange={() => {}}
              />
            </AccordionPanel>
          </AccordionItem>
        )}

        {/* Knowledge Section */}
        {!isStartNode && (
          <AccordionItem value="knowledge">
            <AccordionHeader size="small">Knowledge</AccordionHeader>
            <AccordionPanel className={styles.accordionPanel}>
              <ScopeSelector
                nodeType={nodeType}
                skillIds={node.data.skillIds ?? []}
                knowledgeIds={node.data.knowledgeIds ?? []}
                toolIds={node.data.toolIds ?? []}
                showKnowledge
                onSkillsChange={() => {}}
                onKnowledgeChange={ids => handleUpdate('knowledgeIds', ids)}
                onToolsChange={() => {}}
              />
            </AccordionPanel>
          </AccordionItem>
        )}

        {/* Tools Section */}
        {!isStartNode && (
          <AccordionItem value="tools">
            <AccordionHeader size="small">Tools</AccordionHeader>
            <AccordionPanel className={styles.accordionPanel}>
              <ScopeSelector
                nodeType={nodeType}
                skillIds={node.data.skillIds ?? []}
                knowledgeIds={node.data.knowledgeIds ?? []}
                toolIds={node.data.toolIds ?? []}
                showTools
                onSkillsChange={() => {}}
                onKnowledgeChange={() => {}}
                onToolsChange={ids => handleUpdate('toolIds', ids)}
              />
            </AccordionPanel>
          </AccordionItem>
        )}

        {/* Condition Section — Only for condition nodes */}
        {isConditionNode && (
          <AccordionItem value="condition">
            <AccordionHeader size="small">Condition</AccordionHeader>
            <AccordionPanel className={styles.accordionPanel}>
              <ConditionEditor
                conditionJson={node.data.conditionJson ?? '{}'}
                onConditionChange={json => handleUpdate('conditionJson', json)}
              />
            </AccordionPanel>
          </AccordionItem>
        )}

        {/* Runtime Settings */}
        {!isStartNode && (
          <AccordionItem value="runtime">
            <AccordionHeader size="small">Runtime Settings</AccordionHeader>
            <AccordionPanel className={styles.accordionPanel}>
              <div className={styles.fieldGroup}>
                <Label size="small">Timeout (seconds)</Label>
                <SpinButton
                  size="small"
                  min={0}
                  max={3600}
                  step={30}
                  value={node.data.timeoutSeconds ?? 300}
                  onChange={(_, data) => handleUpdate('timeoutSeconds', data.value ?? 300)}
                />
              </div>
              <div className={styles.fieldGroup}>
                <Label size="small">Retry Count</Label>
                <SpinButton
                  size="small"
                  min={0}
                  max={5}
                  step={1}
                  value={node.data.retryCount ?? 0}
                  onChange={(_, data) => handleUpdate('retryCount', data.value ?? 0)}
                />
              </div>
            </AccordionPanel>
          </AccordionItem>
        )}
      </Accordion>

      {/* Delete button */}
      {!isStartNode && (
        <div className={styles.deleteSection}>
          <Button
            appearance="subtle"
            icon={<Delete20Regular />}
            onClick={handleDelete}
            style={{ color: tokens.colorPaletteRedForeground1 }}
          >
            Delete Node
          </Button>
        </div>
      )}

      {/* R3 P9 H2 (task 091) — OutputVariable rename guard. */}
      {renameGuard && (
        <RenameGuardDialog
          open={renameGuard.open}
          oldName={renameGuard.oldName}
          newName={renameGuard.newName}
          references={renameGuard.references}
          onResolve={handleRenameGuardResolve}
        />
      )}
    </div>
  );
});
