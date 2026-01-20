/**
 * TestModeSelector - Test execution mode selection component
 *
 * Provides an inline mode selector for choosing between Mock, Quick, and Production
 * test modes. Includes mode descriptions, file upload for Quick mode, confirmation
 * dialog for Production mode, and displays test progress/results.
 *
 * This component is designed to be embedded directly in the UI, unlike TestOptionsDialog
 * which wraps everything in a modal dialog.
 *
 * Uses Fluent UI v9 components with design tokens for full dark mode support (ADR-021).
 *
 * @version 1.0.0
 */

import * as React from 'react';
import { useState, useCallback, useRef, useEffect, useMemo } from 'react';
import {
  RadioGroup,
  Radio,
  Label,
  Text,
  Button,
  Input,
  makeStyles,
  tokens,
  shorthands,
  MessageBar,
  MessageBarBody,
  MessageBarTitle,
  Card,
  Spinner,
  Dialog,
  DialogSurface,
  DialogTitle,
  DialogBody,
  DialogContent,
  DialogActions,
  ProgressBar,
  Badge,
  mergeClasses,
} from '@fluentui/react-components';
import {
  Play20Regular,
  Dismiss20Regular,
  Document20Regular,
  DocumentArrowUp20Regular,
  Beaker20Regular,
  Flash20Regular,
  Rocket20Regular,
  Info16Regular,
  Warning16Regular,
  Checkmark16Regular,
  Stop16Regular,
} from '@fluentui/react-icons';
import {
  useAiAssistantStore,
  type TestMode,
  type TestOptions,
  type TestNodeProgress,
} from '../../stores/aiAssistantStore';

// ============================================================================
// Styles (ADR-021: Fluent UI v9 design tokens for dark mode support)
// ============================================================================

const useStyles = makeStyles({
  container: {
    display: 'flex',
    flexDirection: 'column',
    ...shorthands.gap(tokens.spacingVerticalM),
    ...shorthands.padding(tokens.spacingVerticalM),
    backgroundColor: tokens.colorNeutralBackground1,
    ...shorthands.borderRadius(tokens.borderRadiusMedium),
  },
  sectionTitle: {
    marginBottom: tokens.spacingVerticalS,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
  },
  modeContainer: {
    display: 'flex',
    flexDirection: 'column',
    ...shorthands.gap(tokens.spacingVerticalS),
  },
  modeCard: {
    cursor: 'pointer',
    ...shorthands.padding(tokens.spacingVerticalM, tokens.spacingHorizontalM),
    backgroundColor: tokens.colorNeutralBackground2,
    ...shorthands.borderRadius(tokens.borderRadiusMedium),
    ...shorthands.border('1px', 'solid', tokens.colorNeutralStroke2),
    transitionProperty: 'background-color, border-color',
    transitionDuration: '100ms',
    '&:hover': {
      backgroundColor: tokens.colorNeutralBackground1Hover,
    },
  },
  modeCardSelected: {
    ...shorthands.borderColor(tokens.colorBrandStroke1),
    backgroundColor: tokens.colorBrandBackground2,
    '&:hover': {
      backgroundColor: tokens.colorBrandBackground2Hover,
    },
  },
  modeHeader: {
    display: 'flex',
    alignItems: 'center',
    ...shorthands.gap(tokens.spacingHorizontalS),
  },
  modeIcon: {
    color: tokens.colorBrandForeground1,
  },
  modeLabel: {
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
  },
  modeBadge: {
    marginLeft: 'auto',
    fontSize: tokens.fontSizeBase100,
    color: tokens.colorNeutralForeground3,
  },
  modeDescription: {
    marginTop: tokens.spacingVerticalXS,
    color: tokens.colorNeutralForeground2,
    fontSize: tokens.fontSizeBase200,
  },
  uploadSection: {
    marginTop: tokens.spacingVerticalS,
    ...shorthands.padding(tokens.spacingVerticalM),
    backgroundColor: tokens.colorNeutralBackground2,
    ...shorthands.borderRadius(tokens.borderRadiusMedium),
    ...shorthands.border('1px', 'solid', tokens.colorNeutralStroke2),
  },
  uploadInput: {
    display: 'none',
  },
  uploadButton: {
    marginTop: tokens.spacingVerticalS,
  },
  uploadedFile: {
    display: 'flex',
    alignItems: 'center',
    ...shorthands.gap(tokens.spacingHorizontalS),
    marginTop: tokens.spacingVerticalS,
    ...shorthands.padding(tokens.spacingVerticalS, tokens.spacingHorizontalM),
    backgroundColor: tokens.colorNeutralBackground3,
    ...shorthands.borderRadius(tokens.borderRadiusMedium),
  },
  uploadedFileName: {
    flex: 1,
    ...shorthands.overflow('hidden'),
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
    color: tokens.colorNeutralForeground1,
  },
  infoSection: {
    display: 'flex',
    alignItems: 'flex-start',
    ...shorthands.gap(tokens.spacingHorizontalS),
    marginTop: tokens.spacingVerticalS,
    ...shorthands.padding(tokens.spacingVerticalS, tokens.spacingHorizontalM),
    backgroundColor: tokens.colorNeutralBackground2,
    ...shorthands.borderRadius(tokens.borderRadiusMedium),
    color: tokens.colorNeutralForeground2,
    fontSize: tokens.fontSizeBase200,
  },
  infoIcon: {
    flexShrink: 0,
    marginTop: '2px',
    color: tokens.colorNeutralForeground3,
  },
  productionSection: {
    marginTop: tokens.spacingVerticalS,
    ...shorthands.padding(tokens.spacingVerticalM),
    backgroundColor: tokens.colorNeutralBackground2,
    ...shorthands.borderRadius(tokens.borderRadiusMedium),
    ...shorthands.border('1px', 'solid', tokens.colorNeutralStroke2),
  },
  documentInput: {
    marginTop: tokens.spacingVerticalS,
  },
  actions: {
    display: 'flex',
    justifyContent: 'flex-end',
    ...shorthands.gap(tokens.spacingHorizontalS),
    marginTop: tokens.spacingVerticalM,
    ...shorthands.padding(tokens.spacingVerticalS, '0'),
    ...shorthands.borderTop('1px', 'solid', tokens.colorNeutralStroke2),
    paddingTop: tokens.spacingVerticalM,
  },
  progressContainer: {
    display: 'flex',
    flexDirection: 'column',
    ...shorthands.gap(tokens.spacingVerticalS),
  },
  progressHeader: {
    display: 'flex',
    alignItems: 'center',
    ...shorthands.gap(tokens.spacingHorizontalS),
  },
  progressInfo: {
    display: 'flex',
    justifyContent: 'space-between',
    alignItems: 'center',
  },
  progressText: {
    color: tokens.colorNeutralForeground2,
    fontSize: tokens.fontSizeBase200,
  },
  currentStep: {
    display: 'flex',
    alignItems: 'center',
    ...shorthands.gap(tokens.spacingHorizontalS),
    ...shorthands.padding(tokens.spacingVerticalS, tokens.spacingHorizontalM),
    backgroundColor: tokens.colorBrandBackground2,
    ...shorthands.borderRadius(tokens.borderRadiusMedium),
    ...shorthands.border('1px', 'solid', tokens.colorBrandStroke1),
  },
  currentStepLabel: {
    color: tokens.colorBrandForeground1,
    fontWeight: tokens.fontWeightSemibold,
  },
  nodeList: {
    display: 'flex',
    flexDirection: 'column',
    ...shorthands.gap(tokens.spacingVerticalXS),
    maxHeight: '200px',
    overflowY: 'auto',
  },
  nodeItem: {
    display: 'flex',
    alignItems: 'center',
    ...shorthands.gap(tokens.spacingHorizontalS),
    ...shorthands.padding(tokens.spacingVerticalXS, tokens.spacingHorizontalS),
    ...shorthands.borderRadius(tokens.borderRadiusSmall),
    backgroundColor: tokens.colorNeutralBackground2,
  },
  nodeItemCompleted: {
    backgroundColor: tokens.colorPaletteGreenBackground1,
  },
  nodeItemFailed: {
    backgroundColor: tokens.colorPaletteRedBackground1,
  },
  nodeItemRunning: {
    backgroundColor: tokens.colorBrandBackground2,
    ...shorthands.border('1px', 'solid', tokens.colorBrandStroke1),
  },
  nodeIcon: {
    flexShrink: 0,
    width: '16px',
    height: '16px',
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
  },
  resultsSummary: {
    display: 'flex',
    flexDirection: 'column',
    ...shorthands.gap(tokens.spacingVerticalS),
    ...shorthands.padding(tokens.spacingVerticalM),
    backgroundColor: tokens.colorNeutralBackground2,
    ...shorthands.borderRadius(tokens.borderRadiusMedium),
    ...shorthands.border('1px', 'solid', tokens.colorNeutralStroke2),
  },
  resultsSummarySuccess: {
    backgroundColor: tokens.colorPaletteGreenBackground1,
    ...shorthands.border('1px', 'solid', tokens.colorPaletteGreenBorder1),
  },
  resultsSummaryFailed: {
    backgroundColor: tokens.colorPaletteRedBackground1,
    ...shorthands.border('1px', 'solid', tokens.colorPaletteRedBorder1),
  },
  summaryStats: {
    display: 'flex',
    ...shorthands.gap(tokens.spacingHorizontalL),
    flexWrap: 'wrap',
  },
  statItem: {
    display: 'flex',
    alignItems: 'center',
    ...shorthands.gap(tokens.spacingHorizontalXS),
  },
  statLabel: {
    color: tokens.colorNeutralForeground2,
    fontSize: tokens.fontSizeBase200,
  },
  statValue: {
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
  },
  confirmDialogSurface: {
    maxWidth: '440px',
  },
});

// ============================================================================
// Types
// ============================================================================

/**
 * Mode information for display.
 */
interface ModeInfo {
  icon: React.ReactNode;
  label: string;
  description: string;
  badge: string;
}

/**
 * Props for TestModeSelector component.
 */
export interface TestModeSelectorProps {
  /** Callback when test starts */
  onStartTest?: (options: TestOptions) => void;
  /** Callback when test is cancelled */
  onCancel?: () => void;
  /** Callback when test completes */
  onComplete?: () => void;
  /** Whether the playbook has been saved (required for Production mode) */
  playbookSaved?: boolean;
  /** Whether the control is in a disabled state */
  disabled?: boolean;
  /** Show compact mode (fewer details) */
  compact?: boolean;
  /** Custom class name */
  className?: string;
}

// ============================================================================
// Helpers
// ============================================================================

/**
 * Format duration in ms to human-readable string.
 */
const formatDuration = (ms: number): string => {
  if (ms < 1000) return `${ms}ms`;
  if (ms < 60000) return `${(ms / 1000).toFixed(1)}s`;
  const minutes = Math.floor(ms / 60000);
  const seconds = ((ms % 60000) / 1000).toFixed(0);
  return `${minutes}m ${seconds}s`;
};

// ============================================================================
// Component
// ============================================================================

export const TestModeSelector: React.FC<TestModeSelectorProps> = ({
  onStartTest,
  onCancel,
  onComplete,
  playbookSaved = false,
  disabled = false,
  compact = false,
  className,
}) => {
  const styles = useStyles();
  const fileInputRef = useRef<HTMLInputElement>(null);

  // Local state
  const [selectedMode, setSelectedMode] = useState<TestMode>('quick');
  const [uploadedFile, setUploadedFile] = useState<File | null>(null);
  const [documentId, setDocumentId] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [showProductionConfirm, setShowProductionConfirm] = useState(false);

  // Store state
  const { testExecution, startTestExecution, resetTestExecution } = useAiAssistantStore();

  // Mode descriptions
  const modeInfo: Record<TestMode, ModeInfo> = {
    mock: {
      icon: <Beaker20Regular className={styles.modeIcon} />,
      label: 'Mock Test',
      description: 'Uses sample data based on document type. No document needed. Best for rapid iteration.',
      badge: '~5s',
    },
    quick: {
      icon: <Flash20Regular className={styles.modeIcon} />,
      label: 'Quick Test',
      description: 'Upload a document for real extraction. Uses temp storage with 24hr TTL.',
      badge: '~20-30s',
    },
    production: {
      icon: <Rocket20Regular className={styles.modeIcon} />,
      label: 'Production Test',
      description: 'Full flow with existing SPE document. Creates test records in Dataverse.',
      badge: '~30-60s',
    },
  };

  // Calculate progress
  const progress = useMemo(() => {
    const { nodesProgress } = testExecution;
    if (nodesProgress.length === 0) return 0;

    const completed = nodesProgress.filter(
      (n) => n.status === 'completed' || n.status === 'failed' || n.status === 'skipped'
    ).length;

    return completed / nodesProgress.length;
  }, [testExecution.nodesProgress]);

  // Calculate stats
  const stats = useMemo(() => {
    const { nodesProgress } = testExecution;
    return {
      total: nodesProgress.length,
      completed: nodesProgress.filter((n) => n.status === 'completed').length,
      failed: nodesProgress.filter((n) => n.status === 'failed').length,
      skipped: nodesProgress.filter((n) => n.status === 'skipped').length,
    };
  }, [testExecution.nodesProgress]);

  // Current node
  const currentNode = useMemo(() => {
    if (!testExecution.currentNodeId) return null;
    return testExecution.nodesProgress.find((n) => n.nodeId === testExecution.currentNodeId);
  }, [testExecution.currentNodeId, testExecution.nodesProgress]);

  // Is test complete?
  const isComplete = !testExecution.isActive && testExecution.nodesProgress.length > 0;
  const hasError = testExecution.error !== null || stats.failed > 0;

  // Handle mode selection
  const handleModeSelect = useCallback((mode: TestMode) => {
    if (disabled || testExecution.isActive) return;
    setSelectedMode(mode);
    setError(null);
  }, [disabled, testExecution.isActive]);

  // Handle file upload
  const handleFileSelect = useCallback((e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (file) {
      // Validate file size (max 50MB)
      if (file.size > 50 * 1024 * 1024) {
        setError('File size exceeds 50MB limit');
        return;
      }
      setUploadedFile(file);
      setError(null);
    }
  }, []);

  // Handle file upload button click
  const handleUploadClick = useCallback(() => {
    fileInputRef.current?.click();
  }, []);

  // Remove uploaded file
  const handleRemoveFile = useCallback(() => {
    setUploadedFile(null);
    if (fileInputRef.current) {
      fileInputRef.current.value = '';
    }
  }, []);

  // Validate and prepare to start test
  const handleRunTestClick = useCallback(() => {
    // Validate based on mode
    if (selectedMode === 'quick' && !uploadedFile) {
      setError('Please upload a document for Quick test mode');
      return;
    }

    if (selectedMode === 'production') {
      if (!playbookSaved) {
        setError('Please save the playbook first to use Production test mode');
        return;
      }
      if (!documentId.trim()) {
        setError('Please enter a document ID for Production test mode');
        return;
      }
      // Show confirmation dialog for Production mode
      setShowProductionConfirm(true);
      return;
    }

    // Start test for Mock and Quick modes (no confirmation needed)
    executeTest();
  }, [selectedMode, uploadedFile, documentId, playbookSaved]);

  // Execute the test
  const executeTest = useCallback(() => {
    const options: TestOptions = {
      mode: selectedMode,
    };

    if (selectedMode === 'quick' && uploadedFile) {
      options.documentFile = uploadedFile;
    }

    if (selectedMode === 'production' && documentId) {
      options.documentId = documentId.trim();
    }

    // Start test execution in store
    startTestExecution(options);

    // Notify parent
    onStartTest?.(options);

    // Close confirmation dialog if open
    setShowProductionConfirm(false);
  }, [selectedMode, uploadedFile, documentId, startTestExecution, onStartTest]);

  // Handle cancel
  const handleCancel = useCallback(() => {
    onCancel?.();
  }, [onCancel]);

  // Handle done/reset
  const handleDone = useCallback(() => {
    resetTestExecution();
    setUploadedFile(null);
    setDocumentId('');
    setError(null);
    onComplete?.();
  }, [resetTestExecution, onComplete]);

  // Handle production confirmation
  const handleProductionConfirm = useCallback(() => {
    setShowProductionConfirm(false);
    executeTest();
  }, [executeTest]);

  // Check if start button should be disabled
  const isStartDisabled =
    disabled ||
    testExecution.isActive ||
    (selectedMode === 'quick' && !uploadedFile) ||
    (selectedMode === 'production' && (!playbookSaved || !documentId.trim()));

  // Render node status icon
  const renderNodeIcon = (node: TestNodeProgress) => {
    switch (node.status) {
      case 'running':
        return <Spinner size="extra-tiny" />;
      case 'completed':
        return <Checkmark16Regular style={{ color: tokens.colorPaletteGreenForeground1 }} />;
      case 'failed':
        return <Dismiss20Regular style={{ color: tokens.colorPaletteRedForeground1 }} />;
      default:
        return null;
    }
  };

  // Get node item class
  const getNodeItemClass = (node: TestNodeProgress) => {
    const base = styles.nodeItem;
    switch (node.status) {
      case 'running':
        return mergeClasses(base, styles.nodeItemRunning);
      case 'completed':
        return mergeClasses(base, styles.nodeItemCompleted);
      case 'failed':
        return mergeClasses(base, styles.nodeItemFailed);
      default:
        return base;
    }
  };

  return (
    <div className={mergeClasses(styles.container, className)}>
      {/* Error message */}
      {error && (
        <MessageBar intent="error">
          <MessageBarBody>{error}</MessageBarBody>
        </MessageBar>
      )}

      {/* Test execution in progress */}
      {testExecution.isActive && (
        <div className={styles.progressContainer}>
          <div className={styles.progressHeader}>
            <Spinner size="tiny" />
            <Text weight="semibold">Running Test...</Text>
            <Badge appearance="outline" size="small" style={{ textTransform: 'capitalize' }}>
              {testExecution.mode ?? 'Test'}
            </Badge>
          </div>

          {/* Progress bar */}
          <div className={styles.progressInfo}>
            <Text className={styles.progressText}>
              {stats.completed + stats.failed + stats.skipped} of {stats.total} nodes
            </Text>
            <Text className={styles.progressText}>{Math.round(progress * 100)}%</Text>
          </div>
          <ProgressBar value={progress} thickness="large" />

          {/* Current step */}
          {currentNode && (
            <div className={styles.currentStep}>
              <Spinner size="extra-tiny" />
              <Text className={styles.currentStepLabel}>{currentNode.label}</Text>
            </div>
          )}

          {/* Node list */}
          {!compact && testExecution.nodesProgress.length > 0 && (
            <div className={styles.nodeList}>
              {testExecution.nodesProgress.map((node) => (
                <div key={node.nodeId} className={getNodeItemClass(node)}>
                  <div className={styles.nodeIcon}>{renderNodeIcon(node)}</div>
                  <Text size={200}>{node.label}</Text>
                  {node.durationMs !== undefined && node.status === 'completed' && (
                    <Text size={100} style={{ color: tokens.colorNeutralForeground3, marginLeft: 'auto' }}>
                      {formatDuration(node.durationMs)}
                    </Text>
                  )}
                </div>
              ))}
            </div>
          )}

          {/* Cancel button */}
          <div className={styles.actions}>
            <Button appearance="secondary" icon={<Stop16Regular />} onClick={handleCancel}>
              Cancel
            </Button>
          </div>
        </div>
      )}

      {/* Test completed - show results summary */}
      {isComplete && (
        <div
          className={mergeClasses(
            styles.resultsSummary,
            hasError ? styles.resultsSummaryFailed : styles.resultsSummarySuccess
          )}
        >
          <div className={styles.progressHeader}>
            {hasError ? (
              <Warning16Regular style={{ color: tokens.colorPaletteRedForeground1 }} />
            ) : (
              <Checkmark16Regular style={{ color: tokens.colorPaletteGreenForeground1 }} />
            )}
            <Text weight="semibold">{hasError ? 'Test Failed' : 'Test Complete'}</Text>
          </div>

          {testExecution.error && (
            <MessageBar intent="error">
              <MessageBarBody>{testExecution.error}</MessageBarBody>
            </MessageBar>
          )}

          <div className={styles.summaryStats}>
            <div className={styles.statItem}>
              <Text className={styles.statLabel}>Completed:</Text>
              <Text className={styles.statValue}>{stats.completed}</Text>
            </div>
            {stats.failed > 0 && (
              <div className={styles.statItem}>
                <Text className={styles.statLabel}>Failed:</Text>
                <Text className={styles.statValue} style={{ color: tokens.colorPaletteRedForeground1 }}>
                  {stats.failed}
                </Text>
              </div>
            )}
            {stats.skipped > 0 && (
              <div className={styles.statItem}>
                <Text className={styles.statLabel}>Skipped:</Text>
                <Text className={styles.statValue}>{stats.skipped}</Text>
              </div>
            )}
            <div className={styles.statItem}>
              <Text className={styles.statLabel}>Duration:</Text>
              <Text className={styles.statValue}>{formatDuration(testExecution.totalDurationMs)}</Text>
            </div>
          </div>

          {testExecution.analysisId && (
            <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>
              Analysis ID: {testExecution.analysisId}
            </Text>
          )}

          <div className={styles.actions}>
            <Button appearance="primary" onClick={handleDone}>
              Done
            </Button>
          </div>
        </div>
      )}

      {/* Mode selection (not shown during execution or when complete) */}
      {!testExecution.isActive && !isComplete && (
        <>
          {/* Mode cards */}
          <div>
            <Label className={styles.sectionTitle}>Select Test Mode</Label>
            <div className={styles.modeContainer}>
              {(Object.entries(modeInfo) as [TestMode, ModeInfo][]).map(([mode, info]) => (
                <Card
                  key={mode}
                  className={mergeClasses(
                    styles.modeCard,
                    selectedMode === mode ? styles.modeCardSelected : undefined
                  )}
                  onClick={() => handleModeSelect(mode)}
                >
                  <div className={styles.modeHeader}>
                    {info.icon}
                    <Text className={styles.modeLabel}>{info.label}</Text>
                    <Text className={styles.modeBadge}>{info.badge}</Text>
                  </div>
                  <Text className={styles.modeDescription}>{info.description}</Text>
                </Card>
              ))}
            </div>
          </div>

          {/* Quick mode: File upload */}
          {selectedMode === 'quick' && (
            <div className={styles.uploadSection}>
              <Label>Upload Test Document</Label>
              <input
                ref={fileInputRef}
                type="file"
                className={styles.uploadInput}
                accept=".pdf,.docx,.xlsx,.png,.jpg,.jpeg"
                onChange={handleFileSelect}
              />
              {!uploadedFile ? (
                <Button
                  className={styles.uploadButton}
                  appearance="primary"
                  icon={<DocumentArrowUp20Regular />}
                  onClick={handleUploadClick}
                  disabled={disabled}
                >
                  Choose File
                </Button>
              ) : (
                <div className={styles.uploadedFile}>
                  <Document20Regular />
                  <Text className={styles.uploadedFileName}>{uploadedFile.name}</Text>
                  <Button
                    appearance="subtle"
                    size="small"
                    icon={<Dismiss20Regular />}
                    onClick={handleRemoveFile}
                    disabled={disabled}
                  />
                </div>
              )}
              <div className={styles.infoSection}>
                <Info16Regular className={styles.infoIcon} />
                <Text>
                  Supported formats: PDF, DOCX, XLSX, PNG, JPG. Max size: 50MB.
                  Document will be processed using Azure Document Intelligence.
                </Text>
              </div>
            </div>
          )}

          {/* Production mode: Document ID input */}
          {selectedMode === 'production' && (
            <div className={styles.productionSection}>
              <Label>Document ID</Label>
              {!playbookSaved && (
                <MessageBar intent="warning" style={{ marginTop: tokens.spacingVerticalS }}>
                  <MessageBarBody>
                    Playbook must be saved before running Production tests.
                  </MessageBarBody>
                </MessageBar>
              )}
              <Input
                className={styles.documentInput}
                placeholder="Enter Dataverse document record ID"
                value={documentId}
                onChange={(_, data) => setDocumentId(data.value)}
                disabled={disabled || !playbookSaved}
              />
              <div className={styles.infoSection}>
                <Info16Regular className={styles.infoIcon} />
                <Text>
                  Production test uses an existing document from SharePoint Embedded.
                  This creates test records in Dataverse with the IsTestExecution flag.
                </Text>
              </div>
            </div>
          )}

          {/* Mock mode: Info */}
          {selectedMode === 'mock' && (
            <div className={styles.infoSection}>
              <Info16Regular className={styles.infoIcon} />
              <Text>
                Mock test generates sample data based on document type definitions.
                Use this for rapid iteration when designing playbook logic.
              </Text>
            </div>
          )}

          {/* Actions */}
          <div className={styles.actions}>
            <Button
              appearance="primary"
              icon={<Play20Regular />}
              onClick={handleRunTestClick}
              disabled={isStartDisabled}
            >
              Run Test
            </Button>
          </div>
        </>
      )}

      {/* Production confirmation dialog */}
      <Dialog open={showProductionConfirm} onOpenChange={(_, data) => setShowProductionConfirm(data.open)}>
        <DialogSurface className={styles.confirmDialogSurface}>
          <DialogTitle>
            <div style={{ display: 'flex', alignItems: 'center', gap: tokens.spacingHorizontalS }}>
              <Warning16Regular style={{ color: tokens.colorPaletteYellowForeground1 }} />
              <span>Confirm Production Test</span>
            </div>
          </DialogTitle>
          <DialogBody>
            <DialogContent>
              <MessageBar intent="warning">
                <MessageBarTitle>Production Environment</MessageBarTitle>
                <MessageBarBody>
                  This test will execute against your production SharePoint Embedded document
                  and create test records in Dataverse. The records will be flagged as test data.
                </MessageBarBody>
              </MessageBar>
              <Text style={{ display: 'block', marginTop: tokens.spacingVerticalM, color: tokens.colorNeutralForeground2 }}>
                Document ID: <strong>{documentId}</strong>
              </Text>
              <Text style={{ display: 'block', marginTop: tokens.spacingVerticalS, color: tokens.colorNeutralForeground2 }}>
                Are you sure you want to proceed?
              </Text>
            </DialogContent>
          </DialogBody>
          <DialogActions>
            <Button appearance="secondary" onClick={() => setShowProductionConfirm(false)}>
              Cancel
            </Button>
            <Button appearance="primary" onClick={handleProductionConfirm}>
              Confirm &amp; Run
            </Button>
          </DialogActions>
        </DialogSurface>
      </Dialog>
    </div>
  );
};

export default TestModeSelector;
