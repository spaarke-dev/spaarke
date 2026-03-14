/**
 * NodePropertiesDialog — Fixed-size landscape modal for editing node properties.
 *
 * Opens automatically when a node is selected on the canvas.
 * Fixed dialog shell (860×560) with horizontal tabs at top:
 *   - Overview: Name, Output Variable, Action selector, AI Model selector
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

import { memo, useCallback, useState, useMemo } from 'react';
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
import { PromptSchemaForm } from './PromptSchemaForm';
import { PromptSchemaEditor } from './PromptSchemaEditor';
import type { PromptSchema } from '../../types/promptSchema';

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

type TabId = 'overview' | 'prompt' | 'skills' | 'knowledge' | 'tools' | 'configuration';

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
  const hasTypeForm = [
    'deliverOutput',
    'deliverToIndex',
    'updateRecord',
    'sendEmail',
    'createTask',
    'aiCompletion',
    'wait',
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

  // Which tabs to show — dynamic based on node type
  const visibleTabs = useMemo(() => {
    const tabs: { id: TabId; label: string }[] = [{ id: 'overview', label: 'Overview' }];
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
  }, [isAiNode, isStartNode, hasConfigTab]);

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
                            value={selectedNode.data.outputVariable ?? ''}
                            onChange={(_, data) => handleUpdate('outputVariable', data.value)}
                            placeholder={`output_${nodeType}`}
                          />
                        </div>
                      )}
                    </div>

                    {isAiNode && (
                      <>
                        <Divider className={styles.sectionTitle} />
                        <Text weight="semibold" size={300} className={styles.sectionTitle}>
                          Action
                        </Text>
                        <div className={styles.fieldGroup}>
                          <ActionSelector
                            selectedActionId={selectedNode.data.actionId}
                            onActionChange={id => handleUpdate('actionId', id)}
                          />
                        </div>
                      </>
                    )}

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

                    {!isStartNode && (
                      <>
                        {(hasTypeForm || isConditionNode) && <Divider style={{ marginBottom: '16px' }} />}
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
