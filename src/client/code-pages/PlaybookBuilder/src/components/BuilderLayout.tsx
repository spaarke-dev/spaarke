/**
 * BuilderLayout — Main layout for the Playbook Builder Code Page (Task 050)
 *
 * Wires all panels together:
 *   - Top toolbar: save, undo, redo, add node, AI assistant toggle, execution controls
 *   - Left sidebar: Node palette (drag-and-drop node types)
 *   - Center: ReactFlow canvas
 *   - Overlay: NodePropertiesDialog (landscape modal, when node selected)
 *   - Floating: AiAssistantModal (toggleable)
 *   - Overlay: ExecutionOverlay (during execution)
 */

import { useState, useCallback, useEffect, useRef } from 'react';
import {
  makeStyles,
  tokens,
  shorthands,
  Button,
  Text,
  Tooltip,
  Divider,
  mergeClasses,
  Badge,
} from '@fluentui/react-components';
import {
  Save20Regular,
  Add20Regular,
  Bot20Regular,
  Play20Regular,
  PanelLeft20Regular,
  FullScreenMaximize20Regular,
  FullScreenMinimize20Regular,
} from '@fluentui/react-icons';
import { ReactFlowProvider } from '@xyflow/react';

import { PlaybookCanvas } from './canvas/PlaybookCanvas';
import { NodePalette } from './NodePalette';
import { NodePropertiesDialog } from './properties/NodePropertiesDialog';
import { ExecutionOverlay } from './execution/ExecutionOverlay';
import { AiAssistantModal } from './ai-assistant/AiAssistantModal';
import { usePlaybookLoader } from '../hooks/usePlaybookLoader';
import { useCanvasStore } from '../stores/canvasStore';
import { useAiAssistantStore } from '../stores/aiAssistantStore';
import { useExecutionStore } from '../stores/executionStore';
import { useScopeStore } from '../stores/scopeStore';
import { useModelStore } from '../stores/modelStore';
import { useTemplateStore } from '../stores/templateStore';
import { syncNodesToDataverse } from '../services/playbookNodeSync';
import { createRecord, updateRecord } from '../services/dataverseClient';
import { useKeyboardShortcuts } from '../hooks/useKeyboardShortcuts';

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

interface BuilderLayoutProps {
  playbookId: string;
  apiBaseUrl: string;
}

// ---------------------------------------------------------------------------
// Node palette
// ---------------------------------------------------------------------------
//
// R7 Wave 8 task 082 (FR-22): the legacy inline ~11-tile palette + draggable
// tile render block were extracted to `./NodePalette.tsx`, which renders the
// full 33-executor catalog grouped into 6 categorized tiers (AI / Compute /
// Mutations / Control / Delivery / Capability). NodePalette writes its own
// drag-start payload containing `executorType` (FR-26 dispatch field) in
// addition to the legacy `type` discriminator, so PlaybookCanvas.handleDrop
// continues to function without changes (it reads the same MIME type).

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    height: '100%',
    backgroundColor: tokens.colorNeutralBackground1,
    color: tokens.colorNeutralForeground1,
  },
  toolbar: {
    display: 'flex',
    alignItems: 'center',
    ...shorthands.gap('4px'),
    ...shorthands.padding('6px', '12px'),
    ...shorthands.borderBottom('1px', 'solid', tokens.colorNeutralStroke2),
    backgroundColor: tokens.colorNeutralBackground3,
    flexShrink: 0,
  },
  toolbarLeft: {
    display: 'flex',
    alignItems: 'center',
    ...shorthands.gap('4px'),
  },
  toolbarCenter: {
    flex: 1,
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
  },
  toolbarRight: {
    display: 'flex',
    alignItems: 'center',
    ...shorthands.gap('4px'),
  },
  playbookTitle: {
    fontWeight: tokens.fontWeightSemibold,
  },
  dirtyBadge: {
    marginLeft: '4px',
  },
  body: {
    display: 'flex',
    flex: 1,
    overflow: 'hidden',
  },
  leftSidebar: {
    width: '200px',
    ...shorthands.borderRight('1px', 'solid', tokens.colorNeutralStroke2),
    backgroundColor: tokens.colorNeutralBackground2,
    display: 'flex',
    flexDirection: 'column',
    overflowY: 'auto',
    flexShrink: 0,
    transition: 'width 0.2s ease',
  },
  sidebarCollapsed: {
    width: '0px',
    ...shorthands.borderRight('0px', 'solid', 'transparent'),
    overflow: 'hidden',
  },
  sidebarHeader: {
    display: 'flex',
    alignItems: 'center',
    ...shorthands.gap('8px'),
    ...shorthands.padding('8px', '12px'),
    ...shorthands.borderBottom('1px', 'solid', tokens.colorNeutralStroke2),
    backgroundColor: tokens.colorNeutralBackground3,
  },
  // Palette list/item styles moved to NodePalette.tsx (task 082 / FR-22).
  // BuilderLayout retains only sidebar-container styles; tile rendering owned
  // by the extracted component.
  paletteContainer: {
    flex: 1,
    minHeight: 0,
    display: 'flex',
    flexDirection: 'column',
    overflow: 'hidden',
  },
  canvasArea: {
    flex: 1,
    position: 'relative',
    overflow: 'hidden',
  },
  loading: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    height: '100%',
    ...shorthands.gap('12px'),
  },
  error: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    height: '100%',
    color: tokens.colorPaletteRedForeground1,
    ...shorthands.gap('8px'),
  },
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export function BuilderLayout({ playbookId, apiBaseUrl }: BuilderLayoutProps): JSX.Element {
  const styles = useStyles();

  // Playbook loading
  const { isLoading, error, playbookName } = usePlaybookLoader(playbookId);

  // Store hooks
  const isDirty = useCanvasStore(s => s.isDirty);
  const selectedNodeId = useCanvasStore(s => s.selectedNodeId);
  const nodes = useCanvasStore(s => s.nodes);
  const edges = useCanvasStore(s => s.edges);
  const exportToCanvasJson = useCanvasStore(s => s.exportToCanvasJson);
  const getInitialNodeScopes = useCanvasStore(s => s.getInitialNodeScopes);
  const markSaved = useCanvasStore(s => s.markSaved);
  const isAiModalOpen = useAiAssistantStore(s => s.isModalOpen);
  const openAiModal = useAiAssistantStore(s => s.openModal);
  const closeAiModal = useAiAssistantStore(s => s.closeModal);
  const isExecuting = useExecutionStore(s => s.isExecuting);

  // Effective playbook ID — starts from prop, updated if we create a new record on first save
  const effectivePlaybookIdRef = useRef(playbookId);

  // Panel visibility
  const [leftPanelOpen, setLeftPanelOpen] = useState(true);
  const [isSaving, setIsSaving] = useState(false);

  // Fullscreen
  const [isFullscreen, setIsFullscreen] = useState(false);
  useEffect(() => {
    const onChange = () => setIsFullscreen(!!document.fullscreenElement);
    document.addEventListener('fullscreenchange', onChange);
    return () => document.removeEventListener('fullscreenchange', onChange);
  }, []);
  const toggleFullscreen = useCallback(() => {
    if (document.fullscreenElement) {
      document.exitFullscreen();
    } else {
      document.documentElement.requestFullscreen();
    }
  }, []);
  const [saveStatus, setSaveStatus] = useState<'saved' | 'error' | null>(null);
  const saveTimeoutRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const saveStatusTimeoutRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  // Initialize stores with BFF API URL on mount
  useEffect(() => {
    useScopeStore.getState().loadAllScopes();
    useModelStore.getState().loadModelDeployments();
    useTemplateStore.getState().setApiBaseUrl(apiBaseUrl);
    useAiAssistantStore.getState().setServiceConfig({ apiBaseUrl });
  }, [apiBaseUrl]);

  // Save handler
  const handleSave = useCallback(async () => {
    if (isSaving) return;
    setIsSaving(true);
    try {
      let activeId = effectivePlaybookIdRef.current;

      // New playbook — create the record in Dataverse first
      if (!activeId) {
        const newName = playbookName || 'New Playbook';
        activeId = await createRecord('sprk_analysisplaybooks', {
          sprk_name: newName,
          sprk_ispublic: true,
        });
        effectivePlaybookIdRef.current = activeId;
        console.info(`[BuilderLayout] Created new playbook: ${activeId}`);
      }

      // Save canvas JSON to Dataverse
      const canvasJson = exportToCanvasJson();
      await updateRecord('sprk_analysisplaybooks', activeId, {
        sprk_canvaslayoutjson: canvasJson,
      });
      // Sync nodes to Dataverse records
      await syncNodesToDataverse(activeId, nodes, edges, getInitialNodeScopes());
      markSaved();
      setSaveStatus('saved');
      console.info('[BuilderLayout] Playbook saved successfully');
    } catch (err) {
      setSaveStatus('error');
      console.error('[BuilderLayout] Save failed:', err);
    } finally {
      setIsSaving(false);
      // Clear status after 3 seconds
      if (saveStatusTimeoutRef.current) clearTimeout(saveStatusTimeoutRef.current);
      saveStatusTimeoutRef.current = setTimeout(() => setSaveStatus(null), 3000);
    }
  }, [isSaving, playbookName, exportToCanvasJson, nodes, edges, markSaved]);

  // Auto-save debounced (30 seconds after last change)
  useEffect(() => {
    if (!isDirty) return;
    if (saveTimeoutRef.current) {
      clearTimeout(saveTimeoutRef.current);
    }
    saveTimeoutRef.current = setTimeout(() => {
      handleSave();
    }, 30000);
    return () => {
      if (saveTimeoutRef.current) {
        clearTimeout(saveTimeoutRef.current);
      }
    };
  }, [isDirty, playbookId, handleSave]);

  // Drag-start handler is now owned by NodePalette.tsx (R7 task 082 / FR-22) —
  // it writes the FR-26 payload (with `executorType` Choice value) to dataTransfer
  // via the same 'application/reactflow' MIME type that PlaybookCanvas.handleDrop
  // already reads. BuilderLayout no longer needs a custom handler.

  // Keyboard shortcuts
  useKeyboardShortcuts({ onSave: handleSave });

  // Loading state
  if (isLoading) {
    return (
      <div className={styles.loading}>
        <Text size={400}>Loading playbook...</Text>
      </div>
    );
  }

  // Error state
  if (error) {
    return (
      <div className={styles.error}>
        <Text size={400} weight="semibold">
          Failed to load playbook
        </Text>
        <Text>{error}</Text>
      </div>
    );
  }

  return (
    <div className={styles.root}>
      {/* Toolbar */}
      <div className={styles.toolbar}>
        <div className={styles.toolbarLeft}>
          <Tooltip content={leftPanelOpen ? 'Hide node palette' : 'Show node palette'} relationship="label">
            <Button
              appearance="subtle"
              size="small"
              icon={<PanelLeft20Regular />}
              onClick={() => setLeftPanelOpen(!leftPanelOpen)}
            />
          </Tooltip>
          <Divider vertical style={{ height: '20px' }} />
          <Tooltip content="Save (Ctrl+S)" relationship="label">
            <Button
              appearance="subtle"
              size="small"
              icon={<Save20Regular />}
              onClick={handleSave}
              disabled={!isDirty || isSaving}
            />
          </Tooltip>
          {isSaving && (
            <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>
              Saving...
            </Text>
          )}
          {saveStatus === 'saved' && (
            <Text size={200} style={{ color: tokens.colorPaletteGreenForeground1 }}>
              Saved
            </Text>
          )}
          {saveStatus === 'error' && (
            <Text size={200} style={{ color: tokens.colorPaletteRedForeground1 }}>
              Save failed
            </Text>
          )}
        </div>

        <div className={styles.toolbarCenter}>
          <Text className={styles.playbookTitle}>{playbookName || 'Playbook Builder'}</Text>
          {isDirty && (
            <Badge className={styles.dirtyBadge} size="small" appearance="ghost" color="warning">
              Unsaved
            </Badge>
          )}
        </div>

        <div className={styles.toolbarRight}>
          <Tooltip content={isExecuting ? 'Execution in progress' : 'Run playbook'} relationship="label">
            <Button
              appearance="subtle"
              size="small"
              icon={<Play20Regular />}
              disabled={isExecuting || nodes.length === 0}
            />
          </Tooltip>
          <Tooltip content="AI Assistant" relationship="label">
            <Button
              appearance={isAiModalOpen ? 'primary' : 'subtle'}
              size="small"
              icon={<Bot20Regular />}
              onClick={() => (isAiModalOpen ? closeAiModal() : openAiModal())}
            />
          </Tooltip>
          <Divider vertical style={{ height: '20px' }} />
          <Tooltip content={isFullscreen ? 'Exit fullscreen' : 'Fullscreen'} relationship="label">
            <Button
              appearance="subtle"
              size="small"
              icon={isFullscreen ? <FullScreenMinimize20Regular /> : <FullScreenMaximize20Regular />}
              onClick={toggleFullscreen}
            />
          </Tooltip>
        </div>
      </div>

      {/* Body: Left Sidebar + Canvas + Right Sidebar */}
      <div className={styles.body}>
        {/* Left Sidebar — Node Palette */}
        <div className={mergeClasses(styles.leftSidebar, !leftPanelOpen && styles.sidebarCollapsed)}>
          <div className={styles.sidebarHeader}>
            <Add20Regular />
            <Text weight="semibold" size={300}>
              Node Types
            </Text>
          </div>
          <div className={styles.paletteContainer}>
            <NodePalette />
          </div>
        </div>

        {/* Canvas (center) */}
        <ReactFlowProvider>
          <div className={styles.canvasArea}>
            <PlaybookCanvas />
            {isExecuting && <ExecutionOverlay />}
          </div>
        </ReactFlowProvider>
      </div>

      {/* Node Properties Dialog */}
      <NodePropertiesDialog />

      {/* AI Assistant Modal (floating) */}
      {isAiModalOpen && <AiAssistantModal />}
    </div>
  );
}
