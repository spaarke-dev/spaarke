/**
 * NodePropertiesDialog — Fixed-size landscape modal for editing node properties.
 *
 * Opens automatically when a node is selected on the canvas.
 * Fixed dialog shell (860×560) with horizontal tabs at top:
 *   - Overview: Name, Output Variable, AI Model selector
 *   - Action: Action lookup + Executor Type selector (side-by-side) — R7 FR-24
 *   - Prompt: Prompt Configuration (AI nodes only)
 *   - Skills: Skill scope selector
 *   - Knowledge: Knowledge scope selector
 *   - Tools: Tool scope selector
 *   - Configuration: Type-specific config, Condition, Runtime Settings
 *
 * Delete button pinned at bottom. Dialog size never changes between tabs.
 *
 * @see ADR-021 - Fluent UI v9 design system (dark mode required)
 */

import { memo, useCallback, useEffect, useState, useMemo } from 'react';
import {
  makeStyles,
  tokens,
  shorthands,
  Dialog,
  DialogSurface,
  DialogBody,
  DialogTitle,
  DialogContent,
  Button,
  Badge,
  TabList,
  Tab,
  Input,
  Label,
  SpinButton,
  Divider,
  Text,
} from '@fluentui/react-components';
import { Dismiss24Regular, Delete20Regular } from '@fluentui/react-icons';
import { useCanvasStore } from '../../stores/canvasStore';

// Sub-components
import { ActionSelector } from './ActionSelector';
import { ExecutorTypeSelector } from './ExecutorTypeSelector';
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
import { LookupUserMembershipForm } from './LookupUserMembershipForm';
import { EntityNameValidatorForm } from './EntityNameValidatorForm';
import { PromptSchemaForm } from './PromptSchemaForm';
import { PromptSchemaEditor } from './PromptSchemaEditor';
import { RenameGuardDialog, type RenameGuardAction } from './RenameGuardDialog';
import { TypedConfigForm } from './TypedConfigForm';
import { findOutputVariableReferences, type OutputVariableReference } from '../../services/canvasValidation';
import {
  fetchExecutorSchemas,
  getSchemaForExecutorTypeName,
  type ExecutorConfigSchema,
} from '../../services/executorSchemaService';
import { useTemplateStore } from '../../stores/templateStore';
import type { PromptSchema } from '../../types/promptSchema';

// R7 Wave 8 task 083 (FR-23) — map camelCase canvas node types to PascalCase ExecutorType
// enum member names served by the BFF schema endpoint. Once Wave 8 tasks 081 + 088 add
// the numeric `sprk_executortype` to the canvas node data, prefer `getSchemaForExecutorType`
// over this name-based lookup. Names omitted from the map (e.g., 'start') fall through to
// the schema service's "no schema available" placeholder branch.
const CANVAS_NODE_TYPE_TO_EXECUTOR_NAME: Record<string, string> = {
  start: 'Start',
  aiAnalysis: 'AiAnalysis',
  aiCompletion: 'AiCompletion',
  condition: 'Condition',
  deliverOutput: 'DeliverOutput',
  deliverToIndex: 'DeliverToIndex',
  updateRecord: 'UpdateRecord',
  createTask: 'CreateTask',
  sendEmail: 'SendEmail',
  createNotification: 'CreateNotification',
  lookupUserMembership: 'LookupUserMembership',
  entityNameValidator: 'EntityNameValidator',
  wait: 'Wait',
};

function parseConfigBag(configJson: string | undefined): Record<string, unknown> {
  if (!configJson) return {};
  try {
    const parsed = JSON.parse(configJson);
    return parsed && typeof parsed === 'object' && !Array.isArray(parsed) ? (parsed as Record<string, unknown>) : {};
  } catch {
    return {};
  }
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  surface: {
    width: '50vw',
    minWidth: '480px',
    maxWidth: '800px',
    height: '60vh',
    minHeight: '400px',
    maxHeight: '85vh',
    ...shorthands.padding('0'),
  },
  body: {
    display: 'flex',
    flexDirection: 'column',
    height: '100%',
    ...shorthands.overflow('hidden'),
  },
  title: {
    ...shorthands.padding('16px', '24px', '8px'),
    flexShrink: 0,
  },
  titleContent: {
    display: 'flex',
    alignItems: 'center',
    ...shorthands.gap('10px'),
  },
  typeBadge: {
    textTransform: 'capitalize',
  },
  tabBar: {
    ...shorthands.padding('0', '24px'),
    flexShrink: 0,
  },
  content: {
    ...shorthands.padding('20px', '24px', '24px'),
    overflowY: 'auto',
    flex: 1,
    display: 'flex',
    flexDirection: 'column',
  },
  fieldGroup: {
    display: 'flex',
    flexDirection: 'column',
    ...shorthands.gap('6px'),
    marginBottom: '16px',
  },
  fieldRow: {
    display: 'flex',
    ...shorthands.gap('16px'),
  },
  fieldCol: {
    flex: 1,
    display: 'flex',
    flexDirection: 'column',
    ...shorthands.gap('6px'),
  },
  sectionTitle: {
    marginTop: '8px',
    marginBottom: '8px',
  },
  deleteSection: {
    ...shorthands.padding('12px', '24px'),
    ...shorthands.borderTop('1px', 'solid', tokens.colorNeutralStroke2),
    flexShrink: 0,
  },
});

// ---------------------------------------------------------------------------
// Tab IDs
// ---------------------------------------------------------------------------

type TabId = 'overview' | 'action' | 'prompt' | 'skills' | 'knowledge' | 'tools' | 'configuration';

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const NodePropertiesDialog = memo(function NodePropertiesDialog() {
  const styles = useStyles();

  const selectedNodeId = useCanvasStore(s => s.selectedNodeId);
  const nodes = useCanvasStore(s => s.nodes);
  const selectNode = useCanvasStore(s => s.selectNode);
  const updateNodeData = useCanvasStore(s => s.updateNodeData);
  const removeNode = useCanvasStore(s => s.removeNode);
  // R3 P9 H2 (task 091): rename-guard store action (auto-rename action).
  const renameOutputVariableReferences = useCanvasStore(s => s.renameOutputVariableReferences);

  const selectedNode = selectedNodeId ? (nodes.find(n => n.id === selectedNodeId) ?? null) : null;

  const [activeTab, setActiveTab] = useState<TabId>('overview');
  const [editorMode, setEditorMode] = useState<'form' | 'editor'>('form');

  const handleClose = useCallback(() => {
    selectNode(null);
    setActiveTab('overview');
  }, [selectNode]);

  const isOpen = selectedNode !== null;

  // Node type helpers
  const nodeType = selectedNode?.data.type ?? '';
  const isAiNode = nodeType === 'aiAnalysis' || nodeType === 'aiCompletion';
  const isConditionNode = nodeType === 'condition';
  const isStartNode = nodeType === 'start';
  // R7 Wave 8 task 089 (FR-27): when the node has been coerced to 'unknown'
  // by canvasStore.coerceUnknownNodeTypes (executorType not in catalog),
  // restrict the dialog to the Action tab only so the maker is funneled toward
  // picking a known Executor Type via the ExecutorTypeSelector.
  const isUnknownNode = nodeType === 'unknown';
  const hasTypeForm = [
    'deliverOutput',
    'deliverToIndex',
    'updateRecord',
    'sendEmail',
    'createTask',
    'aiCompletion',
    'wait',
    'lookupUserMembership',
    'entityNameValidator',
  ].includes(nodeType);
  const hasConfigTab = hasTypeForm || isConditionNode || !isStartNode;

  // Handlers
  const handleUpdate = useCallback(
    (field: string, value: unknown) => {
      if (selectedNode) updateNodeData(selectedNode.id, { [field]: value });
    },
    [selectedNode, updateNodeData]
  );

  const handleConfigChange = useCallback(
    (json: string) => {
      if (selectedNode) updateNodeData(selectedNode.id, { configJson: json });
    },
    [selectedNode, updateNodeData]
  );

  const handlePromptSchemaChange = useCallback(
    (schema: PromptSchema) => {
      if (selectedNode) updateNodeData(selectedNode.id, { promptSchema: schema });
    },
    [selectedNode, updateNodeData]
  );

  const handleDelete = useCallback(() => {
    if (selectedNode) {
      removeNode(selectedNode.id);
      selectNode(null);
    }
  }, [selectedNode, removeNode, selectNode]);

  // R7 Wave 8 task 083 (FR-23) — typed config schema renderer wiring.
  //
  // Lazy-fetch the executor config schemas on first dialog open. Cached across opens
  // via in-memory + sessionStorage by `executorSchemaService`. Re-renders this dialog
  // when the cache flips from "loading" → "ready" via the local `executorSchema` state.
  //
  // The hand-crafted forms above (AiCompletionForm, CreateTaskForm, etc.) remain in
  // place at this task — tasks 084 + 085 replace them incrementally on top of the
  // TypedConfigForm renderer rendered below them.
  const apiBaseUrl = useTemplateStore(s => s.apiBaseUrl);
  const [executorSchema, setExecutorSchema] = useState<ExecutorConfigSchema | undefined>(undefined);
  const [schemasReady, setSchemasReady] = useState<boolean>(false);

  useEffect(() => {
    if (!isOpen || !apiBaseUrl) return;
    let cancelled = false;
    fetchExecutorSchemas(apiBaseUrl)
      .then(() => {
        if (!cancelled) setSchemasReady(true);
      })
      .catch(err => {
        // Non-fatal: typed form will render the "no schema available" placeholder
        // and existing hand-crafted forms continue to work unchanged.
        console.warn('[NodePropertiesDialog] failed to load executor config schemas', err);
      });
    return () => {
      cancelled = true;
    };
  }, [isOpen, apiBaseUrl]);

  useEffect(() => {
    if (!schemasReady || !nodeType) {
      setExecutorSchema(undefined);
      return;
    }
    const executorName = CANVAS_NODE_TYPE_TO_EXECUTOR_NAME[nodeType];
    setExecutorSchema(executorName ? getSchemaForExecutorTypeName(executorName) : undefined);
  }, [schemasReady, nodeType]);

  const typedConfigValue = useMemo(
    () => parseConfigBag(selectedNode?.data.configJson),
    [selectedNode?.data.configJson]
  );

  const handleTypedConfigChange = useCallback(
    (next: Record<string, unknown>) => {
      if (!selectedNode) return;
      try {
        const json = JSON.stringify(next);
        updateNodeData(selectedNode.id, { configJson: json });
      } catch (err) {
        console.warn('[NodePropertiesDialog] failed to serialize typed config', err);
      }
    },
    [selectedNode, updateNodeData]
  );

  // -----------------------------------------------------------------------
  // R3 P9 H2 (task 091) — OutputVariable rename guard (FR-3H2.1 / AC-H2.1).
  // Same intercept as NodePropertiesForm: controlled local draft, commit on
  // blur, dialog when other nodes reference the old name. See NodePropertiesForm
  // for the rationale on using onBlur instead of onChange.
  // -----------------------------------------------------------------------
  const committedOutputVar = selectedNode?.data.outputVariable ?? '';
  const [outputVarDraft, setOutputVarDraft] = useState<string>(committedOutputVar);
  const [renameGuard, setRenameGuard] = useState<{
    open: boolean;
    oldName: string;
    newName: string;
    references: OutputVariableReference[];
  } | null>(null);

  useEffect(() => {
    if (renameGuard === null) {
      setOutputVarDraft(committedOutputVar);
    }
  }, [committedOutputVar, renameGuard]);

  const handleOutputVariableCommit = useCallback(() => {
    if (!selectedNode) return;
    const oldName = committedOutputVar.trim();
    const newName = outputVarDraft.trim();

    if (newName === committedOutputVar) return;
    if (oldName === '') {
      updateNodeData(selectedNode.id, { outputVariable: outputVarDraft });
      return;
    }
    if (oldName === newName) {
      updateNodeData(selectedNode.id, { outputVariable: outputVarDraft });
      return;
    }

    const references = findOutputVariableReferences(oldName, nodes, selectedNode.id);
    if (references.length === 0) {
      updateNodeData(selectedNode.id, { outputVariable: outputVarDraft });
      return;
    }

    setRenameGuard({ open: true, oldName, newName, references });
  }, [selectedNode, committedOutputVar, outputVarDraft, nodes, updateNodeData]);

  const handleRenameGuardResolve = useCallback(
    (action: RenameGuardAction) => {
      if (!renameGuard || !selectedNode) return;
      const { oldName, newName } = renameGuard;

      if (action === 'autoRename') {
        renameOutputVariableReferences(oldName, newName);
        updateNodeData(selectedNode.id, { outputVariable: outputVarDraft });
      } else {
        setOutputVarDraft(committedOutputVar);
      }
      setRenameGuard(null);
    },
    [renameGuard, selectedNode, committedOutputVar, outputVarDraft, updateNodeData, renameOutputVariableReferences]
  );

  // Which tabs to show — dynamic based on node type.
  // R7 Wave 8 task 086 (FR-24): tab order is Overview, Action, Prompt, Skills,
  // Knowledge, Tools, Configuration. Action tab is hidden on Start nodes
  // (Start is a canvas anchor with no Action / ExecutorType to choose).
  // R7 Wave 8 task 089 (FR-27): when nodeType === 'unknown', show ONLY the
  // Action tab. The maker must pick a known Executor Type via the
  // ExecutorTypeSelector before any other tab is meaningful (Prompt depends on
  // executor being prompt-driven; Configuration depends on the executor's
  // typed config schema; Skills/Knowledge/Tools depend on executor capability).
  const visibleTabs = useMemo(() => {
    if (isUnknownNode) {
      return [{ id: 'action' as TabId, label: 'Action' }];
    }
    const tabs: { id: TabId; label: string }[] = [{ id: 'overview', label: 'Overview' }];
    if (!isStartNode) {
      tabs.push({ id: 'action', label: 'Action' });
    }
    if (isAiNode) {
      tabs.push({ id: 'prompt', label: 'Prompt' });
    }
    if (!isStartNode) {
      tabs.push({ id: 'skills', label: 'Skills' });
      tabs.push({ id: 'knowledge', label: 'Knowledge' });
      tabs.push({ id: 'tools', label: 'Tools' });
    }
    if (hasConfigTab) {
      tabs.push({ id: 'configuration', label: 'Configuration' });
    }
    return tabs;
  }, [isAiNode, isStartNode, hasConfigTab, isUnknownNode]);

  // R7 Wave 8 task 089 (FR-27): when the selected node is unknown, force
  // activeTab to 'action' so the dialog opens directly on the
  // ExecutorTypeSelector. Without this, the previously-selected tab (e.g.
  // 'overview') would persist across selection changes and the maker would
  // see an empty pane.
  useEffect(() => {
    if (isUnknownNode && activeTab !== 'action') {
      setActiveTab('action');
    }
  }, [isUnknownNode, activeTab]);

  return (
    <Dialog
      open={isOpen}
      onOpenChange={(_e, data) => {
        if (!data.open) handleClose();
      }}
      modalType="non-modal"
    >
      <DialogSurface className={styles.surface}>
        <DialogBody className={styles.body}>
          {/* Title bar — action slot places close X on the right */}
          <DialogTitle
            className={styles.title}
            action={<Button appearance="subtle" aria-label="Close" icon={<Dismiss24Regular />} onClick={handleClose} />}
          >
            <div className={styles.titleContent}>
              {selectedNode?.data.label || 'Node Properties'}
              {selectedNode && (
                <Badge size="small" appearance="outline" className={styles.typeBadge}>
                  {nodeType}
                </Badge>
              )}
            </div>
          </DialogTitle>

          {/* Tabs at top */}
          <div className={styles.tabBar}>
            <TabList
              selectedValue={activeTab}
              onTabSelect={(_e, data) => setActiveTab(data.value as TabId)}
              size="medium"
            >
              {visibleTabs.map(tab => (
                <Tab key={tab.id} value={tab.id}>
                  {tab.label}
                </Tab>
              ))}
            </TabList>
          </div>

          {/* Scrollable content area (fixed size, never resizes dialog) */}
          <DialogContent className={styles.content}>
            {selectedNode && (
              <>
                {/* === OVERVIEW === */}
                {activeTab === 'overview' && (
                  <>
                    <div className={styles.fieldRow}>
                      <div className={styles.fieldCol}>
                        <Label htmlFor={`${selectedNode.id}-name`}>Name</Label>
                        <Input
                          id={`${selectedNode.id}-name`}
                          size="medium"
                          value={selectedNode.data.label}
                          onChange={(_, data) => handleUpdate('label', data.value)}
                        />
                      </div>
                      {!isStartNode && (
                        <div className={styles.fieldCol}>
                          <Label htmlFor={`${selectedNode.id}-outputVar`}>Output Variable</Label>
                          <Input
                            id={`${selectedNode.id}-outputVar`}
                            size="medium"
                            value={outputVarDraft}
                            onChange={(_, data) => setOutputVarDraft(data.value)}
                            onBlur={handleOutputVariableCommit}
                            placeholder={`output_${nodeType}`}
                          />
                        </div>
                      )}
                    </div>

                    {/*
                      R7 Wave 8 task 086 (FR-24): ActionSelector has been promoted
                      out of Overview tab into the new dedicated Action tab below.
                      Overview tab now shows only: Name, Output Variable, AI Model.
                    */}

                    {isAiNode && (
                      <>
                        <Divider className={styles.sectionTitle} />
                        <Text weight="semibold" size={300} className={styles.sectionTitle}>
                          AI Model
                        </Text>
                        <div className={styles.fieldGroup}>
                          <ModelSelector
                            modelDeploymentId={selectedNode.data.modelDeploymentId}
                            onModelChange={id => handleUpdate('modelDeploymentId', id)}
                          />
                        </div>
                      </>
                    )}
                  </>
                )}

                {/* === ACTION (R7 FR-24 — Action + Executor Type side-by-side) === */}
                {activeTab === 'action' && !isStartNode && (
                  <div className={styles.fieldRow}>
                    <div className={styles.fieldCol}>
                      <ActionSelector
                        selectedActionId={selectedNode.data.actionId}
                        onActionChange={id => handleUpdate('actionId', id)}
                      />
                    </div>
                    <div className={styles.fieldCol}>
                      <ExecutorTypeSelector
                        value={
                          typeof selectedNode.data.executorType === 'number'
                            ? selectedNode.data.executorType
                            : undefined
                        }
                        onChange={value => handleUpdate('executorType', value)}
                      />
                    </div>
                  </div>
                )}

                {/* === PROMPT (AI nodes only) === */}
                {activeTab === 'prompt' &&
                  isAiNode &&
                  (editorMode === 'form' ? (
                    <PromptSchemaForm
                      schema={selectedNode.data.promptSchema ?? null}
                      onChange={handlePromptSchemaChange}
                      onSwitchToEditor={() => setEditorMode('editor')}
                      nodeId={selectedNode.id}
                    />
                  ) : (
                    <PromptSchemaEditor
                      schema={selectedNode.data.promptSchema ?? null}
                      onChange={handlePromptSchemaChange}
                      onSwitchToForm={() => setEditorMode('form')}
                    />
                  ))}

                {/* === SKILLS === */}
                {activeTab === 'skills' && !isStartNode && (
                  <ScopeSelector
                    nodeType={nodeType}
                    skillIds={selectedNode.data.skillIds ?? []}
                    knowledgeIds={selectedNode.data.knowledgeIds ?? []}
                    toolIds={selectedNode.data.toolIds ?? []}
                    showSkills
                    onSkillsChange={ids => handleUpdate('skillIds', ids)}
                    onKnowledgeChange={() => {}}
                    onToolsChange={() => {}}
                  />
                )}

                {/* === KNOWLEDGE === */}
                {activeTab === 'knowledge' && !isStartNode && (
                  <ScopeSelector
                    nodeType={nodeType}
                    skillIds={selectedNode.data.skillIds ?? []}
                    knowledgeIds={selectedNode.data.knowledgeIds ?? []}
                    toolIds={selectedNode.data.toolIds ?? []}
                    showKnowledge
                    onSkillsChange={() => {}}
                    onKnowledgeChange={ids => handleUpdate('knowledgeIds', ids)}
                    onToolsChange={() => {}}
                  />
                )}

                {/* === TOOLS === */}
                {activeTab === 'tools' && !isStartNode && (
                  <ScopeSelector
                    nodeType={nodeType}
                    skillIds={selectedNode.data.skillIds ?? []}
                    knowledgeIds={selectedNode.data.knowledgeIds ?? []}
                    toolIds={selectedNode.data.toolIds ?? []}
                    showTools
                    onSkillsChange={() => {}}
                    onKnowledgeChange={() => {}}
                    onToolsChange={ids => handleUpdate('toolIds', ids)}
                  />
                )}

                {/* === CONFIGURATION === */}
                {activeTab === 'configuration' && (
                  <>
                    {hasTypeForm && (
                      <div className={styles.fieldGroup}>
                        {nodeType === 'deliverOutput' && (
                          <DeliverOutputForm
                            nodeId={selectedNode.id}
                            data={selectedNode.data}
                            onUpdate={handleUpdate}
                          />
                        )}
                        {nodeType === 'deliverToIndex' && (
                          <DeliverToIndexForm
                            nodeId={selectedNode.id}
                            data={selectedNode.data}
                            onUpdate={handleUpdate}
                          />
                        )}
                        {nodeType === 'updateRecord' && (
                          <UpdateRecordForm
                            nodeId={selectedNode.id}
                            configJson={selectedNode.data.configJson ?? '{}'}
                            onConfigChange={handleConfigChange}
                          />
                        )}
                        {nodeType === 'sendEmail' && (
                          <SendEmailForm
                            nodeId={selectedNode.id}
                            configJson={selectedNode.data.configJson ?? '{}'}
                            onConfigChange={handleConfigChange}
                          />
                        )}
                        {nodeType === 'createTask' && (
                          <CreateTaskForm
                            nodeId={selectedNode.id}
                            configJson={selectedNode.data.configJson ?? '{}'}
                            onConfigChange={handleConfigChange}
                          />
                        )}
                        {nodeType === 'aiCompletion' && (
                          <AiCompletionForm
                            nodeId={selectedNode.id}
                            configJson={selectedNode.data.configJson ?? '{}'}
                            onConfigChange={handleConfigChange}
                          />
                        )}
                        {nodeType === 'wait' && (
                          <WaitForm
                            nodeId={selectedNode.id}
                            configJson={selectedNode.data.configJson ?? '{}'}
                            onConfigChange={handleConfigChange}
                          />
                        )}
                        {nodeType === 'lookupUserMembership' && (
                          <LookupUserMembershipForm
                            nodeId={selectedNode.id}
                            configJson={selectedNode.data.configJson ?? '{}'}
                            onConfigChange={handleConfigChange}
                          />
                        )}
                        {nodeType === 'entityNameValidator' && (
                          <EntityNameValidatorForm
                            nodeId={selectedNode.id}
                            configJson={selectedNode.data.configJson ?? '{}'}
                            onConfigChange={handleConfigChange}
                          />
                        )}
                      </div>
                    )}

                    {isConditionNode && (
                      <div className={styles.fieldGroup}>
                        <Text weight="semibold" size={300}>
                          Condition
                        </Text>
                        <ConditionEditor
                          conditionJson={selectedNode.data.conditionJson ?? '{}'}
                          onConditionChange={json => handleUpdate('conditionJson', json)}
                        />
                      </div>
                    )}

                    {!isStartNode && schemasReady && (
                      <>
                        {(hasTypeForm || isConditionNode) && <Divider style={{ marginBottom: '16px' }} />}
                        <Text weight="semibold" size={300} className={styles.sectionTitle}>
                          Typed Configuration (R7 FR-23)
                        </Text>
                        <div className={styles.fieldGroup}>
                          <TypedConfigForm
                            nodeId={selectedNode.id}
                            schema={executorSchema}
                            value={typedConfigValue}
                            onChange={handleTypedConfigChange}
                          />
                        </div>
                      </>
                    )}

                    {!isStartNode && (
                      <>
                        {(hasTypeForm || isConditionNode || schemasReady) && (
                          <Divider style={{ marginBottom: '16px' }} />
                        )}
                        <Text weight="semibold" size={300} className={styles.sectionTitle}>
                          Runtime Settings
                        </Text>
                        <div className={styles.fieldRow}>
                          <div className={styles.fieldCol}>
                            <Label>Timeout (seconds)</Label>
                            <SpinButton
                              size="medium"
                              min={0}
                              max={3600}
                              step={30}
                              value={selectedNode.data.timeoutSeconds ?? 300}
                              onChange={(_, data) => handleUpdate('timeoutSeconds', data.value ?? 300)}
                            />
                          </div>
                          <div className={styles.fieldCol}>
                            <Label>Retry Count</Label>
                            <SpinButton
                              size="medium"
                              min={0}
                              max={5}
                              step={1}
                              value={selectedNode.data.retryCount ?? 0}
                              onChange={(_, data) => handleUpdate('retryCount', data.value ?? 0)}
                            />
                          </div>
                        </div>
                      </>
                    )}
                  </>
                )}
              </>
            )}
          </DialogContent>

          {/* Delete button pinned at bottom */}
          {selectedNode && !isStartNode && (
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
        </DialogBody>
      </DialogSurface>
    </Dialog>
  );
});
