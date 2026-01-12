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
 * @version 2.0.0
 */

import * as React from 'react';
import { useEffect, useCallback, useRef } from 'react';
import {
  Button,
  Spinner,
  Text,
  makeStyles,
  tokens,
  shorthands,
} from '@fluentui/react-components';
import { ReactFlowProvider } from 'react-flow-renderer';
import { BuilderLayout } from './components';
import { useCanvasStore } from './stores';

// ─────────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────────

export interface PlaybookBuilderHostProps {
  playbookId: string;
  playbookName: string;
  canvasJson: string;
  onDirtyChange: (isDirty: boolean) => void;
  onSave: (canvasJson: string) => void;
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
    minHeight: '800px',
    ...shorthands.overflow('hidden'),
  },
  toolbar: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'flex-end',
    ...shorthands.padding(tokens.spacingVerticalS, tokens.spacingHorizontalM),
    ...shorthands.gap(tokens.spacingHorizontalS),
    ...shorthands.borderBottom(tokens.strokeWidthThin, 'solid', tokens.colorNeutralStroke1),
    backgroundColor: tokens.colorNeutralBackground1,
  },
  builderContainer: {
    flex: 1,
    position: 'relative',
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
  footer: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'flex-end',
    ...shorthands.padding(tokens.spacingVerticalXS, tokens.spacingHorizontalS),
    fontSize: '11px',
    color: tokens.colorNeutralForeground3,
    backgroundColor: tokens.colorNeutralBackground2,
    ...shorthands.borderTop(tokens.strokeWidthThin, 'solid', tokens.colorNeutralStroke2),
  },
  dirtyIndicator: {
    color: tokens.colorPaletteMarigoldForeground1,
    marginRight: tokens.spacingHorizontalS,
  },
});

// ─────────────────────────────────────────────────────────────────────────────
// Component
// ─────────────────────────────────────────────────────────────────────────────

export const PlaybookBuilderHost: React.FC<PlaybookBuilderHostProps> = ({
  playbookId,
  playbookName,
  canvasJson,
  onDirtyChange,
  onSave,
}) => {
  const styles = useStyles();
  const initializedRef = useRef(false);
  const [isLoading, setIsLoading] = React.useState(true);
  const [error, setError] = React.useState<string | null>(null);

  // Get store state and actions
  const { isDirty, loadCanvas, getCanvasJson, clearDirty } = useCanvasStore((state) => ({
    isDirty: state.isDirty,
    loadCanvas: state.loadCanvas,
    getCanvasJson: state.getCanvasJson,
    clearDirty: state.clearDirty,
  }));

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

  // Notify parent of dirty state changes
  useEffect(() => {
    onDirtyChange(isDirty);
  }, [isDirty, onDirtyChange]);

  // Handle save button click
  const handleSaveClick = useCallback(() => {
    try {
      const json = getCanvasJson();
      console.info('[PlaybookBuilderHost] Saving canvas', { jsonLength: json.length });
      onSave(json);
      clearDirty();
    } catch (err) {
      console.error('[PlaybookBuilderHost] Failed to save canvas:', err);
      setError('Failed to save the playbook. Please try again.');
    }
  }, [getCanvasJson, onSave, clearDirty]);

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
      {/* Toolbar */}
      <div className={styles.toolbar}>
        {isDirty && (
          <Text className={styles.dirtyIndicator} size={200}>
            Unsaved changes
          </Text>
        )}
        <Button appearance="primary" disabled={!isDirty} onClick={handleSaveClick}>
          Save
        </Button>
      </div>

      {/* Builder Container with ReactFlowProvider */}
      <div className={styles.builderContainer}>
        <ReactFlowProvider>
          <BuilderLayout />
        </ReactFlowProvider>
      </div>

      {/* Footer with version */}
      <div className={styles.footer}>
        <Text size={100}>v2.0.0</Text>
      </div>
    </div>
  );
};
