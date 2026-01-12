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
 * @version 2.6.0
 */

import * as React from 'react';
import { useEffect, useCallback, useRef, useState } from 'react';
import {
  Button,
  Spinner,
  Text,
  Input,
  Textarea,
  Label,
  makeStyles,
  tokens,
  shorthands,
} from '@fluentui/react-components';
import { Save20Regular } from '@fluentui/react-icons';
import { ReactFlowProvider } from 'react-flow-renderer';
import { BuilderLayout } from './components';
import { useCanvasStore } from './stores';

// ─────────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────────

export interface PlaybookBuilderHostProps {
  playbookId: string;
  playbookName: string;
  playbookDescription?: string;
  canvasJson: string;
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
    ...shorthands.overflow('hidden'),
  },
  header: {
    display: 'flex',
    alignItems: 'flex-start',
    justifyContent: 'space-between',
    ...shorthands.padding(tokens.spacingVerticalM, tokens.spacingHorizontalL),
    ...shorthands.gap(tokens.spacingHorizontalL),
    ...shorthands.borderBottom(tokens.strokeWidthThin, 'solid', tokens.colorNeutralStroke1),
    backgroundColor: tokens.colorNeutralBackground1,
    flexShrink: 0,
  },
  headerFields: {
    display: 'flex',
    flexDirection: 'column',
    ...shorthands.gap(tokens.spacingVerticalS),
    flex: 1,
    maxWidth: '600px',
  },
  headerField: {
    display: 'flex',
    flexDirection: 'column',
    ...shorthands.gap(tokens.spacingVerticalXXS),
  },
  headerActions: {
    display: 'flex',
    alignItems: 'center',
    ...shorthands.gap(tokens.spacingHorizontalS),
    flexShrink: 0,
  },
  builderContainer: {
    flex: 1,
    position: 'relative',
    minHeight: 0, // Critical for flex child to shrink properly
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
  },
  versionBadge: {
    fontSize: '10px',
    color: tokens.colorNeutralForeground3,
    position: 'absolute',
    bottom: tokens.spacingVerticalXS,
    right: tokens.spacingHorizontalS,
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
  onDirtyChange,
  onSave,
}) => {
  const styles = useStyles();
  const initializedRef = useRef(false);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  // Local state for editable fields
  const [name, setName] = useState(playbookName || '');
  const [description, setDescription] = useState(playbookDescription || '');
  const [fieldsModified, setFieldsModified] = useState(false);

  // Get store state and actions
  const { isDirty: canvasDirty, loadCanvas, getCanvasJson, clearDirty } = useCanvasStore((state) => ({
    isDirty: state.isDirty,
    loadCanvas: state.loadCanvas,
    getCanvasJson: state.getCanvasJson,
    clearDirty: state.clearDirty,
  }));

  // Combined dirty state: canvas changes OR field changes
  const isDirty = canvasDirty || fieldsModified;

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

  // Handle name change
  const handleNameChange = useCallback((e: React.ChangeEvent<HTMLInputElement>) => {
    setName(e.target.value);
    setFieldsModified(true);
  }, []);

  // Handle description change
  const handleDescriptionChange = useCallback((e: React.ChangeEvent<HTMLTextAreaElement>) => {
    setDescription(e.target.value);
    setFieldsModified(true);
  }, []);

  // Handle save button click
  const handleSaveClick = useCallback(() => {
    try {
      const json = getCanvasJson();
      console.info('[PlaybookBuilderHost] Saving canvas', {
        jsonLength: json.length,
        name,
        description: description.substring(0, 50) + '...',
      });
      onSave(json, name, description);
      clearDirty();
      setFieldsModified(false);
    } catch (err) {
      console.error('[PlaybookBuilderHost] Failed to save canvas:', err);
      setError('Failed to save the playbook. Please try again.');
    }
  }, [getCanvasJson, onSave, clearDirty, name, description]);

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
      {/* Header with Name/Description and Save */}
      <div className={styles.header}>
        <div className={styles.headerFields}>
          <div className={styles.headerField}>
            <Label htmlFor="playbook-name" required size="small">
              Playbook Name
            </Label>
            <Input
              id="playbook-name"
              size="small"
              value={name}
              onChange={handleNameChange}
              placeholder="Enter playbook name"
            />
          </div>
          <div className={styles.headerField}>
            <Label htmlFor="playbook-description" size="small">
              Description
            </Label>
            <Textarea
              id="playbook-description"
              size="small"
              value={description}
              onChange={handleDescriptionChange}
              placeholder="Enter playbook description"
              rows={2}
              resize="none"
            />
          </div>
        </div>
        <div className={styles.headerActions}>
          {isDirty && (
            <Text className={styles.dirtyIndicator} size={200}>
              Unsaved changes
            </Text>
          )}
          <Button
            appearance="primary"
            icon={<Save20Regular />}
            disabled={!isDirty}
            onClick={handleSaveClick}
          >
            Save
          </Button>
        </div>
      </div>

      {/* Builder Container with ReactFlowProvider */}
      <div className={styles.builderContainer}>
        <ReactFlowProvider>
          <BuilderLayout />
        </ReactFlowProvider>
        {/* Version badge in corner */}
        <Text className={styles.versionBadge}>v2.6.0</Text>
      </div>
    </div>
  );
};
