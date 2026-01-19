/**
 * PlaybookBuilderHost React Component
 *
 * Direct React Flow integration - NO iframe.
 * Uses react-flow-renderer v10 for React 16 compatibility.
 *
 * Architecture:
 * - Renders BuilderLayout directly in PCF control
 * - Canvas state managed by Zustand store
 * - Dirty state tracked and exposed to PCF host
 *
 * @version 2.20.0
 */

import * as React from 'react';
import { useEffect, useCallback, useRef, useState } from 'react';
import {
  Button,
  Spinner,
  Text,
  Tooltip,
  makeStyles,
  tokens,
  shorthands,
} from '@fluentui/react-components';
import { DocumentMultiple20Regular } from '@fluentui/react-icons';
import { ReactFlowProvider } from 'react-flow-renderer';
import { BuilderLayout, TemplateLibraryDialog } from './components';
import { useCanvasStore, useTemplateStore, useAiAssistantStore } from './stores';

// ─────────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────────

export interface PlaybookBuilderHostProps {
  playbookId: string;
  playbookName: string;
  playbookDescription?: string;
  canvasJson: string;
  apiBaseUrl?: string;
  onDirtyChange: (isDirty: boolean) => void;
  onSave: (canvasJson: string, name: string, description: string) => void;
}

// ─────────────────────────────────────────────────────────────────────────────
// Styles
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
  container: {
    display: 'flex',
    flexDirection: 'column',
    width: '100%',
    height: '100%',
    flex: 1,
    minHeight: 0, // Critical for flex child sizing
    boxSizing: 'border-box',
    ...shorthands.overflow('hidden'),
  },
  header: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'flex-end',
    ...shorthands.padding(tokens.spacingVerticalS, tokens.spacingHorizontalL),
    ...shorthands.gap(tokens.spacingHorizontalS),
    ...shorthands.borderBottom(tokens.strokeWidthThin, 'solid', tokens.colorNeutralStroke1),
    backgroundColor: tokens.colorNeutralBackground1,
    flexShrink: 0,
  },
  headerActions: {
    display: 'flex',
    alignItems: 'center',
    ...shorthands.gap(tokens.spacingHorizontalS),
  },
  builderContainer: {
    flex: 1,
    position: 'relative',
    minHeight: 0, // Allow flex shrink, parent has explicit height
    ...shorthands.overflow('hidden'),
  },
  loading: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    height: '100%',
    flexDirection: 'column',
    ...shorthands.gap(tokens.spacingVerticalM),
  },
  error: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    height: '100%',
    ...shorthands.padding(tokens.spacingVerticalXXL),
    textAlign: 'center',
    color: tokens.colorPaletteRedForeground1,
  },
  dirtyIndicator: {
    color: tokens.colorPaletteMarigoldForeground1,
    minWidth: '110px', // Reserve space to prevent layout shift
    textAlign: 'right',
  },
  footer: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'flex-start',
    ...shorthands.padding(tokens.spacingVerticalXS, tokens.spacingHorizontalM),
    backgroundColor: tokens.colorNeutralBackground2,
    ...shorthands.borderTop(tokens.strokeWidthThin, 'solid', tokens.colorNeutralStroke2),
    flexShrink: 0,
  },
  versionBadge: {
    fontSize: '10px',
    color: tokens.colorNeutralForeground3,
    pointerEvents: 'none',
  },
});

// ─────────────────────────────────────────────────────────────────────────────
// Component
// ─────────────────────────────────────────────────────────────────────────────

export const PlaybookBuilderHost: React.FC<PlaybookBuilderHostProps> = ({
  playbookId,
  playbookName,
  playbookDescription = '',
  canvasJson,
  apiBaseUrl = '',
  onDirtyChange,
  onSave,
}) => {
  const styles = useStyles();
  const initializedRef = useRef(false);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  // Template library dialog state
  const [templateDialogOpen, setTemplateDialogOpen] = useState(false);

  // Template store - initialize API base URL
  const { setApiBaseUrl } = useTemplateStore((state) => ({
    setApiBaseUrl: state.setApiBaseUrl,
  }));

  // AI Assistant store - initialize playbook ID and service config
  const { setPlaybookId, setServiceConfig, startSession } = useAiAssistantStore((state) => ({
    setPlaybookId: state.setPlaybookId,
    setServiceConfig: state.setServiceConfig,
    startSession: state.startSession,
  }));

  // Get store state and actions
  const { isDirty, loadCanvas, getCanvasJson, clearDirty } = useCanvasStore((state) => ({
    isDirty: state.isDirty,
    loadCanvas: state.loadCanvas,
    getCanvasJson: state.getCanvasJson,
    clearDirty: state.clearDirty,
  }));

  // Debounce timer ref for auto-sync
  const syncTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  // Initialize canvas from props (only once)
  useEffect(() => {
    if (initializedRef.current) return;
    initializedRef.current = true;

    try {
      console.info('[PlaybookBuilderHost] Initializing canvas', {
        playbookId,
        playbookName,
        hasCanvasJson: !!canvasJson,
      });

      if (canvasJson) {
        loadCanvas(canvasJson);
      }

      setIsLoading(false);
    } catch (err) {
      console.error('[PlaybookBuilderHost] Failed to initialize canvas:', err);
      setError('Failed to load the playbook canvas. Please try again.');
      setIsLoading(false);
    }
  }, [canvasJson, loadCanvas, playbookId, playbookName]);

  // Initialize template store API URL
  useEffect(() => {
    if (apiBaseUrl) {
      setApiBaseUrl(apiBaseUrl);
    }
  }, [apiBaseUrl, setApiBaseUrl]);

  // Initialize AI Assistant store with playbook ID and service config
  useEffect(() => {
    if (playbookId) {
      setPlaybookId(playbookId);
      startSession(playbookId);
    }
    if (apiBaseUrl) {
      // Note: accessToken is empty for now - authentication to be configured
      // The BFF API endpoint will return an error if authentication is required
      setServiceConfig({
        apiBaseUrl,
        accessToken: '', // TODO: Get access token from PCF context or auth provider
      });
    }
  }, [playbookId, apiBaseUrl, setPlaybookId, setServiceConfig, startSession]);

  // Notify parent of dirty state changes
  useEffect(() => {
    onDirtyChange(isDirty);
  }, [isDirty, onDirtyChange]);

  // Auto-sync canvas changes to bound field (debounced)
  // This enables the form's Save button to persist canvas changes
  useEffect(() => {
    if (!isDirty || !initializedRef.current) return;

    // Clear any pending sync
    if (syncTimerRef.current) {
      clearTimeout(syncTimerRef.current);
    }

    // Debounce sync by 500ms to avoid excessive updates during drag
    syncTimerRef.current = setTimeout(() => {
      try {
        const json = getCanvasJson();
        console.info('[PlaybookBuilderHost] Auto-syncing canvas to bound field', {
          jsonLength: json.length,
        });
        // Sync to bound field - form Save button will persist
        onSave(json, playbookName, playbookDescription || '');
        // Clear dirty state - canvas is now synced to bound field
        clearDirty();
      } catch (err) {
        console.error('[PlaybookBuilderHost] Failed to sync canvas:', err);
      }
    }, 500);

    return () => {
      if (syncTimerRef.current) {
        clearTimeout(syncTimerRef.current);
      }
    };
  }, [isDirty, getCanvasJson, onSave, playbookName, playbookDescription, clearDirty]);

  // Handle retry
  const handleRetryClick = useCallback(() => {
    setError(null);
    setIsLoading(true);
    initializedRef.current = false;

    try {
      if (canvasJson) {
        loadCanvas(canvasJson);
      }
      setIsLoading(false);
    } catch (err) {
      console.error('[PlaybookBuilderHost] Retry failed:', err);
      setError('Failed to load the playbook canvas. Please try again.');
      setIsLoading(false);
    }
  }, [canvasJson, loadCanvas]);

  // Handle template library open
  const handleOpenTemplateLibrary = useCallback(() => {
    setTemplateDialogOpen(true);
  }, []);

  // Handle template library close
  const handleCloseTemplateLibrary = useCallback(() => {
    setTemplateDialogOpen(false);
  }, []);

  // Handle clone success - navigate to the cloned playbook
  const handleCloneSuccess = useCallback((clonedId: string, clonedName: string) => {
    setTemplateDialogOpen(false);
    console.info('[PlaybookBuilderHost] Template cloned successfully', { clonedId, clonedName });

    // Navigate to the cloned playbook record
    // In Dataverse, we use Xrm.Navigation.openForm to navigate to records
    try {
      const Xrm = (window as unknown as { Xrm?: { Navigation?: { openForm: (options: unknown) => void } } }).Xrm;
      if (Xrm?.Navigation?.openForm) {
        Xrm.Navigation.openForm({
          entityName: 'sprk_analysisplaybook',
          entityId: clonedId,
        });
      } else {
        // Fallback: reload the page with the new record ID in URL
        const currentUrl = window.location.href;
        const newUrl = currentUrl.replace(/id=[^&]+/, `id=${clonedId}`);
        if (newUrl !== currentUrl) {
          window.location.href = newUrl;
        } else {
          console.warn('[PlaybookBuilderHost] Could not navigate to cloned playbook - Xrm.Navigation not available');
        }
      }
    } catch (err) {
      console.error('[PlaybookBuilderHost] Failed to navigate to cloned playbook:', err);
    }
  }, []);

  // Error state
  if (error) {
    return (
      <div className={styles.container}>
        <div className={styles.error}>
          <Text size={400} weight="semibold">
            Unable to Load Builder
          </Text>
          <Text size={300}>{error}</Text>
          <Button appearance="primary" onClick={handleRetryClick}>
            Retry
          </Button>
        </div>
      </div>
    );
  }

  // Loading state
  if (isLoading) {
    return (
      <div className={styles.container}>
        <div className={styles.loading}>
          <Spinner size="medium" />
          <Text>Loading Playbook Builder...</Text>
        </div>
      </div>
    );
  }

  return (
    <div className={styles.container}>
      {/* Header with Actions */}
      <div className={styles.header}>
        <div className={styles.headerActions}>
          <Text
            className={styles.dirtyIndicator}
            size={200}
            style={{ visibility: isDirty ? 'visible' : 'hidden' }}
          >
            Unsaved changes
          </Text>
          {apiBaseUrl && (
            <Tooltip content="Browse playbook templates" relationship="description">
              <Button
                appearance="subtle"
                icon={<DocumentMultiple20Regular />}
                onClick={handleOpenTemplateLibrary}
              >
                Templates
              </Button>
            </Tooltip>
          )}
        </div>
      </div>

      {/* Builder Container with ReactFlowProvider */}
      <div className={styles.builderContainer}>
        <ReactFlowProvider>
          <BuilderLayout />
        </ReactFlowProvider>
      </div>

      {/* Footer with Version */}
      <div className={styles.footer}>
        <Text className={styles.versionBadge}>v2.20.6 2026-01-19</Text>
      </div>

      {/* Template Library Dialog */}
      <TemplateLibraryDialog
        open={templateDialogOpen}
        onClose={handleCloseTemplateLibrary}
        onCloneSuccess={handleCloneSuccess}
      />
    </div>
  );
};
