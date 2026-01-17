/**
 * Test Options Dialog - Select test execution mode and options
 *
 * Allows users to choose between Mock, Quick, and Production test modes.
 * - Mock: Uses sample data, fastest (~5s)
 * - Quick: Upload a document, uses temp storage (24hr TTL), ~20-30s
 * - Production: Uses existing SPE document, full flow, ~30-60s
 *
 * @version 1.0.0
 */

import * as React from 'react';
import { useState, useCallback, useRef } from 'react';
import {
  Dialog,
  DialogSurface,
  DialogTitle,
  DialogBody,
  DialogContent,
  DialogActions,
  Button,
  RadioGroup,
  Radio,
  Label,
  Text,
  Input,
  makeStyles,
  tokens,
  shorthands,
  MessageBar,
  MessageBarBody,
  Card,
  CardHeader,
  Spinner,
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
} from '@fluentui/react-icons';

// ─────────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Test execution mode.
 */
export type TestMode = 'mock' | 'quick' | 'production';

/**
 * Test options selected by the user.
 */
export interface TestOptions {
  mode: TestMode;
  documentFile?: File;
  documentId?: string;
  driveId?: string;
  itemId?: string;
}

// ─────────────────────────────────────────────────────────────────────────────
// Styles
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
  dialogSurface: {
    maxWidth: '560px',
    width: '90vw',
  },
  titleContainer: {
    display: 'flex',
    alignItems: 'center',
    ...shorthands.gap(tokens.spacingHorizontalS),
  },
  sectionTitle: {
    marginBottom: tokens.spacingVerticalS,
    fontWeight: tokens.fontWeightSemibold,
  },
  radioGroup: {
    display: 'flex',
    flexDirection: 'column',
    ...shorthands.gap(tokens.spacingVerticalM),
  },
  modeCard: {
    cursor: 'pointer',
    ...shorthands.padding(tokens.spacingVerticalM, tokens.spacingHorizontalM),
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
  modeDescription: {
    marginTop: tokens.spacingVerticalXS,
    color: tokens.colorNeutralForeground2,
    fontSize: tokens.fontSizeBase200,
  },
  modeBadge: {
    marginLeft: 'auto',
    fontSize: tokens.fontSizeBase100,
    color: tokens.colorNeutralForeground3,
  },
  uploadSection: {
    marginTop: tokens.spacingVerticalL,
    ...shorthands.padding(tokens.spacingVerticalM),
    backgroundColor: tokens.colorNeutralBackground2,
    ...shorthands.borderRadius(tokens.borderRadiusMedium),
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
  },
  infoSection: {
    display: 'flex',
    alignItems: 'flex-start',
    ...shorthands.gap(tokens.spacingHorizontalS),
    marginTop: tokens.spacingVerticalL,
    ...shorthands.padding(tokens.spacingVerticalS, tokens.spacingHorizontalM),
    backgroundColor: tokens.colorNeutralBackground2,
    ...shorthands.borderRadius(tokens.borderRadiusMedium),
    color: tokens.colorNeutralForeground2,
    fontSize: tokens.fontSizeBase200,
  },
  infoIcon: {
    flexShrink: 0,
    marginTop: '2px',
  },
  productionNote: {
    marginTop: tokens.spacingVerticalL,
    ...shorthands.padding(tokens.spacingVerticalM),
    backgroundColor: tokens.colorNeutralBackground2,
    ...shorthands.borderRadius(tokens.borderRadiusMedium),
  },
  documentInput: {
    marginTop: tokens.spacingVerticalS,
  },
});

// ─────────────────────────────────────────────────────────────────────────────
// Props
// ─────────────────────────────────────────────────────────────────────────────

export interface TestOptionsDialogProps {
  open: boolean;
  onClose: () => void;
  onStartTest: (options: TestOptions) => void;
  isExecuting?: boolean;
  playbookSaved?: boolean;
}

// ─────────────────────────────────────────────────────────────────────────────
// Component
// ─────────────────────────────────────────────────────────────────────────────

export const TestOptionsDialog: React.FC<TestOptionsDialogProps> = ({
  open,
  onClose,
  onStartTest,
  isExecuting = false,
  playbookSaved = false,
}) => {
  const styles = useStyles();
  const fileInputRef = useRef<HTMLInputElement>(null);

  // State
  const [selectedMode, setSelectedMode] = useState<TestMode>('quick');
  const [uploadedFile, setUploadedFile] = useState<File | null>(null);
  const [documentId, setDocumentId] = useState('');
  const [error, setError] = useState<string | null>(null);

  // Mode descriptions
  const modeInfo = {
    mock: {
      icon: <Beaker20Regular className={styles.modeIcon} />,
      label: 'Mock Test',
      description: 'Uses sample data based on document type. No document needed.',
      badge: '~5s',
    },
    quick: {
      icon: <Flash20Regular className={styles.modeIcon} />,
      label: 'Quick Test',
      description: 'Upload a document for real extraction. Temp storage with 24hr TTL.',
      badge: '~20-30s',
    },
    production: {
      icon: <Rocket20Regular className={styles.modeIcon} />,
      label: 'Production Test',
      description: 'Full flow with existing SPE document. Creates test records in Dataverse.',
      badge: '~30-60s',
      requiresSaved: true,
    },
  };

  // Handle mode selection
  const handleModeSelect = useCallback((mode: TestMode) => {
    setSelectedMode(mode);
    setError(null);
  }, []);

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

  // Handle start test
  const handleStartTest = useCallback(() => {
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
    }

    const options: TestOptions = {
      mode: selectedMode,
    };

    if (selectedMode === 'quick' && uploadedFile) {
      options.documentFile = uploadedFile;
    }

    if (selectedMode === 'production' && documentId) {
      options.documentId = documentId.trim();
    }

    onStartTest(options);
  }, [selectedMode, uploadedFile, documentId, playbookSaved, onStartTest]);

  // Handle close
  const handleClose = useCallback(() => {
    if (!isExecuting) {
      setSelectedMode('quick');
      setUploadedFile(null);
      setDocumentId('');
      setError(null);
      onClose();
    }
  }, [isExecuting, onClose]);

  // Check if start button should be disabled
  const isStartDisabled =
    isExecuting ||
    (selectedMode === 'quick' && !uploadedFile) ||
    (selectedMode === 'production' && (!playbookSaved || !documentId.trim()));

  return (
    <Dialog open={open} onOpenChange={(_, data) => !data.open && handleClose()}>
      <DialogSurface className={styles.dialogSurface}>
        <DialogTitle>
          <div className={styles.titleContainer}>
            <Play20Regular />
            <Text>Test Playbook</Text>
          </div>
        </DialogTitle>

        <DialogBody>
          <DialogContent>
            {/* Error message */}
            {error && (
              <MessageBar intent="error" style={{ marginBottom: tokens.spacingVerticalM }}>
                <MessageBarBody>{error}</MessageBarBody>
              </MessageBar>
            )}

            {/* Test mode selection */}
            <Label className={styles.sectionTitle}>Select Test Mode</Label>
            <div className={styles.radioGroup}>
              {(Object.entries(modeInfo) as [TestMode, typeof modeInfo.mock][]).map(
                ([mode, info]) => (
                  <Card
                    key={mode}
                    className={`${styles.modeCard} ${
                      selectedMode === mode ? styles.modeCardSelected : ''
                    }`}
                    onClick={() => handleModeSelect(mode)}
                  >
                    <div className={styles.modeHeader}>
                      {info.icon}
                      <Text weight="semibold">{info.label}</Text>
                      <Text className={styles.modeBadge}>{info.badge}</Text>
                    </div>
                    <Text className={styles.modeDescription}>{info.description}</Text>
                  </Card>
                )
              )}
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
                    disabled={isExecuting}
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
                      disabled={isExecuting}
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

            {/* Production mode: Document selection */}
            {selectedMode === 'production' && (
              <div className={styles.productionNote}>
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
                  disabled={isExecuting || !playbookSaved}
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
              <div className={styles.infoSection} style={{ marginTop: tokens.spacingVerticalL }}>
                <Info16Regular className={styles.infoIcon} />
                <Text>
                  Mock test generates sample data based on document type definitions.
                  Use this for rapid iteration when designing playbook logic.
                </Text>
              </div>
            )}
          </DialogContent>
        </DialogBody>

        <DialogActions>
          <Button
            appearance="secondary"
            icon={<Dismiss20Regular />}
            onClick={handleClose}
            disabled={isExecuting}
          >
            Cancel
          </Button>
          <Button
            appearance="primary"
            icon={isExecuting ? <Spinner size="tiny" /> : <Play20Regular />}
            onClick={handleStartTest}
            disabled={isStartDisabled}
          >
            {isExecuting ? 'Running...' : 'Start Test'}
          </Button>
        </DialogActions>
      </DialogSurface>
    </Dialog>
  );
};
