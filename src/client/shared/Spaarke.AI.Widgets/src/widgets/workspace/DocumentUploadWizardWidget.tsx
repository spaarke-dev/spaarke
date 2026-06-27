/**
 * @spaarke/ai-widgets — DocumentUploadWizardWidget
 *
 * Workspace widget that embeds a document upload flow as a multi-step wizard
 * inside a workspace tab (no modal overlay).
 *
 * Behavior:
 * - Step 1 "Select files": FileUploadZone + UploadedFileList (from @spaarke/ui-components)
 * - Step 2 "Enter details": document title + optional description fields
 * - Step 3 "Review & upload": summary of selected files before committing
 * - On upload complete: dispatches `widget_load` on the `workspace` channel
 *   with `widgetType: "document-viewer"` and the uploaded document IDs so the
 *   shell can open a DocumentViewer tab for the newly uploaded document.
 *
 * PaneEventBus integration:
 * - Subscribes to `wizard_step` events filtered by `wizardId`.
 * - `next` / `back` advance or retreat the internal step index.
 * - `set-field` pre-fills the document title or description from the AI.
 *
 * Session restore (D-08):
 * - Serializes `wizardId` and current `stepIndex` only.
 * - File list is not serialized — users re-select files after restore.
 *
 * Context pane sync:
 * - On step change dispatches `stage_change` on the `context` channel so
 *   ContextPaneController can show step-specific help (e.g., supported file
 *   formats on step 1, document naming tips on step 2).
 *
 * React 19, NOT PCF-safe.
 *
 * Task: AIPU2-104
 *
 * @see FileUploadZone    — upstream file drop zone (@spaarke/ui-components)
 * @see UploadedFileList  — uploaded file list component (@spaarke/ui-components)
 * @see WizardStepEvent   — PaneEventBus event type for AI-driven step control
 * @see ADR-012           — Shared component library (reuse, not copy)
 * @see ADR-021           — Fluent UI v9, no hard-coded colors
 */

import React, { useCallback, useRef, useState } from 'react';
import {
  Button,
  Field,
  Input,
  Spinner,
  Text,
  Textarea,
  makeStyles,
  mergeClasses,
  tokens,
} from '@fluentui/react-components';
import {
  ArrowLeft24Regular,
  ArrowRight24Regular,
  CloudArrowUp24Regular,
  Checkmark24Regular,
} from '@fluentui/react-icons';

import { FileUploadZone } from '@spaarke/ui-components/components/FileUpload/FileUploadZone';
import { UploadedFileList } from '@spaarke/ui-components/components/FileUpload/UploadedFileList';
import type { IUploadedFile, IFileValidationError } from '@spaarke/ui-components/components/FileUpload/fileUploadTypes';

import type { WorkspaceWidgetProps } from '../../types/widget-types';
import type { WidgetState } from '../../types/shared';
import { usePaneEvent } from '../../events/usePaneEvent';
import { useDispatchPaneEvent } from '../../events/useDispatchPaneEvent';
import type { WizardStepEvent } from '../../events/PaneEventTypes';

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

const STEP_COUNT = 3;
const STEP_LABELS = ['Select Files', 'Enter Details', 'Review & Upload'] as const;

// ---------------------------------------------------------------------------
// Data payload shape
// ---------------------------------------------------------------------------

/**
 * Data delivered to this widget via the workspace SSE event or on mount.
 */
export interface DocumentUploadWizardData {
  /** Stable identifier for this wizard instance. */
  wizardId: string;
  /** BFF API base URL for the upload endpoint. */
  bffBaseUrl?: string;
  /** Authenticated fetch injected by the shell (wraps MSAL token acquisition). */
  authenticatedFetch?: (input: RequestInfo, init?: RequestInit) => Promise<Response>;
  /** Cosmos / session ID for correlating the uploaded document with the session. */
  sessionId?: string;
  /** Initial step index to restore to (0-based). Default: 0. */
  initialStepIndex?: number;
}

// ---------------------------------------------------------------------------
// Serialized query params (D-08)
// ---------------------------------------------------------------------------

export interface DocumentUploadWizardQueryParams extends Record<string, string> {
  wizardId: string;
  stepIndex: string;
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    height: '100%',
    minHeight: 0,
    backgroundColor: tokens.colorNeutralBackground1,
    boxSizing: 'border-box',
  },

  // Stepper bar at top (replaces modal title bar chrome)
  stepper: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    padding: `${tokens.spacingVerticalM} ${tokens.spacingHorizontalL}`,
    borderBottom: `1px solid ${tokens.colorNeutralStroke2}`,
    flexShrink: 0,
  },
  stepItem: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
    color: tokens.colorNeutralForeground4,
    fontSize: tokens.fontSizeBase300,
  },
  stepItemActive: {
    color: tokens.colorBrandForeground1,
    fontWeight: tokens.fontWeightSemibold,
  },
  stepItemCompleted: {
    color: tokens.colorNeutralForeground3,
  },
  stepConnector: {
    width: '24px',
    height: '1px',
    backgroundColor: tokens.colorNeutralStroke2,
    flexShrink: 0,
  },
  stepDot: {
    width: '8px',
    height: '8px',
    borderRadius: '50%',
    backgroundColor: 'currentColor',
    flexShrink: 0,
  },

  // Content area
  content: {
    flex: 1,
    minHeight: 0,
    overflow: 'auto',
    padding: `${tokens.spacingVerticalL} ${tokens.spacingHorizontalL}`,
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
  },

  // Footer navigation
  footer: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'flex-end',
    gap: tokens.spacingHorizontalS,
    padding: `${tokens.spacingVerticalS} ${tokens.spacingHorizontalL}`,
    borderTop: `1px solid ${tokens.colorNeutralStroke2}`,
    flexShrink: 0,
  },

  // Step 3 review list
  reviewList: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
    padding: tokens.spacingHorizontalS,
    backgroundColor: tokens.colorNeutralBackground2,
    borderRadius: tokens.borderRadiusMedium,
    border: `1px solid ${tokens.colorNeutralStroke2}`,
  },
  reviewRow: {
    display: 'flex',
    gap: tokens.spacingHorizontalS,
    alignItems: 'flex-start',
  },
  reviewLabel: {
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
    minWidth: '80px',
    flexShrink: 0,
  },
  reviewValue: {
    color: tokens.colorNeutralForeground1,
    fontSize: tokens.fontSizeBase200,
    wordBreak: 'break-word',
  },

  centered: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    flex: 1,
    gap: tokens.spacingVerticalM,
    color: tokens.colorNeutralForeground3,
  },

  successIcon: {
    color: tokens.colorPaletteGreenForeground1,
    fontSize: '48px',
  },
});

// ---------------------------------------------------------------------------
// Sub-components
// ---------------------------------------------------------------------------

interface StepperProps {
  currentStep: number;
  labels: readonly string[];
  styles: ReturnType<typeof useStyles>;
}

const Stepper: React.FC<StepperProps> = ({ currentStep, labels, styles }) => (
  <div className={styles.stepper} role="list" aria-label="Wizard progress">
    {labels.map((label, i) => (
      <React.Fragment key={label}>
        {i > 0 && <div className={styles.stepConnector} aria-hidden />}
        <div
          className={mergeClasses(
            styles.stepItem,
            i === currentStep && styles.stepItemActive,
            i < currentStep && styles.stepItemCompleted
          )}
          role="listitem"
          aria-current={i === currentStep ? 'step' : undefined}
          aria-label={`Step ${i + 1}: ${label}${i < currentStep ? ' (completed)' : ''}`}
        >
          <div className={styles.stepDot} aria-hidden />
          <span>{label}</span>
        </div>
      </React.Fragment>
    ))}
  </div>
);

// ---------------------------------------------------------------------------
// Main component
// ---------------------------------------------------------------------------

/**
 * DocumentUploadWizardWidget
 *
 * Embedded multi-step document upload flow for the workspace pane.
 * Adapts FileUploadZone and UploadedFileList from @spaarke/ui-components
 * into a three-step wizard without any modal chrome.
 */
const DocumentUploadWizardWidget: React.FC<WorkspaceWidgetProps<DocumentUploadWizardData>> = ({
  data,
  isLoading,
  error,
  className,
}) => {
  const styles = useStyles();
  const dispatch = useDispatchPaneEvent();

  const wizardId = data?.wizardId ?? 'document-upload';

  // ── Step state ───────────────────────────────────────────────────────────
  const [stepIndex, setStepIndex] = useState<number>(data?.initialStepIndex ?? 0);
  const stepIndexRef = useRef(stepIndex);
  stepIndexRef.current = stepIndex;

  // ── Step 1: file selection ───────────────────────────────────────────────
  const [uploadedFiles, setUploadedFiles] = useState<IUploadedFile[]>([]);
  const [validationErrors, setValidationErrors] = useState<IFileValidationError[]>([]);

  const handleFilesAccepted = useCallback((files: IUploadedFile[]) => {
    setUploadedFiles(prev => {
      const existing = new Set(prev.map(f => `${f.name}::${f.sizeBytes}`));
      const newFiles = files.filter(f => !existing.has(`${f.name}::${f.sizeBytes}`));
      return [...prev, ...newFiles];
    });
    setValidationErrors([]);
  }, []);

  const handleValidationErrors = useCallback((errs: IFileValidationError[]) => {
    setValidationErrors(errs);
  }, []);

  const handleRemoveFile = useCallback((fileId: string) => {
    setUploadedFiles(prev => prev.filter(f => f.id !== fileId));
  }, []);

  // ── Step 2: document details ─────────────────────────────────────────────
  const [documentTitle, setDocumentTitle] = useState('');
  const [documentDescription, setDocumentDescription] = useState('');

  // ── Upload state ─────────────────────────────────────────────────────────
  const [isUploading, setIsUploading] = useState(false);
  const [uploadError, setUploadError] = useState<string | null>(null);
  const [isComplete, setIsComplete] = useState(false);
  const [uploadedDocumentIds, setUploadedDocumentIds] = useState<string[]>([]);

  // ── PaneEventBus: wizard_step events ────────────────────────────────────
  usePaneEvent(
    'workspace',
    useCallback(
      event => {
        if (event.type !== 'wizard_step') return;
        const wizardEvent = event as WizardStepEvent;
        if (wizardEvent.wizardId !== wizardId) return;

        switch (wizardEvent.wizardAction) {
          case 'next':
            setStepIndex(prev => Math.min(prev + 1, STEP_COUNT - 1));
            break;
          case 'back':
            setStepIndex(prev => Math.max(prev - 1, 0));
            break;
          case 'set-field':
            if (wizardEvent.fieldName === 'documentTitle' && typeof wizardEvent.fieldValue === 'string') {
              setDocumentTitle(wizardEvent.fieldValue);
            }
            if (wizardEvent.fieldName === 'documentDescription' && typeof wizardEvent.fieldValue === 'string') {
              setDocumentDescription(wizardEvent.fieldValue);
            }
            break;
        }
      },
      [wizardId]
    )
  );

  // ── Context pane sync on step change ─────────────────────────────────────
  const handleStepChange = useCallback(
    (newIndex: number) => {
      setStepIndex(newIndex);
      dispatch('context', {
        type: 'stage_change',
        contextType: 'wizard-step',
        contextData: {
          wizardId,
          wizardType: 'document-upload',
          stepIndex: newIndex,
          stepLabel: STEP_LABELS[newIndex],
        },
      });
    },
    [wizardId, dispatch]
  );

  // ── Navigation handlers ──────────────────────────────────────────────────
  const handleNext = useCallback(() => {
    if (stepIndex < STEP_COUNT - 1) {
      handleStepChange(stepIndex + 1);
    }
  }, [stepIndex, handleStepChange]);

  const handleBack = useCallback(() => {
    if (stepIndex > 0) {
      handleStepChange(stepIndex - 1);
    }
  }, [stepIndex, handleStepChange]);

  // ── Upload handler ───────────────────────────────────────────────────────
  const handleUpload = useCallback(async () => {
    if (!data?.authenticatedFetch || !data?.bffBaseUrl) {
      setUploadError('Upload service not configured. Please contact support.');
      return;
    }

    setIsUploading(true);
    setUploadError(null);

    try {
      const formData = new FormData();
      formData.append('title', documentTitle.trim() || uploadedFiles[0]?.name || 'Untitled Document');
      if (documentDescription.trim()) {
        formData.append('description', documentDescription.trim());
      }
      if (data.sessionId) {
        formData.append('sessionId', data.sessionId);
      }

      for (const file of uploadedFiles) {
        if (file.file) {
          formData.append('files', file.file, file.name);
        }
      }

      const response = await data.authenticatedFetch(`${data.bffBaseUrl}/documents/upload`, {
        method: 'POST',
        body: formData,
      });

      if (!response.ok) {
        const text = await response.text().catch(() => response.statusText);
        throw new Error(`Upload failed (${response.status}): ${text}`);
      }

      const result = (await response.json()) as { documentIds: string[] };
      const docIds = result.documentIds ?? [];
      setUploadedDocumentIds(docIds);
      setIsComplete(true);

      // Dispatch widget_load so the shell can open a DocumentViewer tab
      dispatch('workspace', {
        type: 'widget_load',
        widgetType: 'document-viewer',
        widgetData: {
          documentIds: docIds,
          sessionId: data.sessionId,
          title: documentTitle.trim() || uploadedFiles[0]?.name,
        },
      });

      // Dispatch context_update so the Context pane reflects the new document
      dispatch('context', {
        type: 'context_update',
        contextType: 'document',
        contextData: {
          documentIds: docIds,
          documentTitle: documentTitle.trim() || uploadedFiles[0]?.name,
          sessionId: data.sessionId,
        },
      });
    } catch (err) {
      const message = err instanceof Error ? err.message : 'Upload failed. Please try again.';
      setUploadError(message);
    } finally {
      setIsUploading(false);
    }
  }, [data, uploadedFiles, documentTitle, documentDescription, dispatch]);

  // ── canAdvance per step ──────────────────────────────────────────────────
  const canAdvance = (): boolean => {
    switch (stepIndex) {
      case 0:
        return uploadedFiles.length > 0;
      case 1:
        return true; // details are optional
      case 2:
        return !isUploading;
      default:
        return false;
    }
  };

  // ── Render: loading ──────────────────────────────────────────────────────
  if (isLoading) {
    return (
      <div className={mergeClasses(styles.root, className)}>
        <div className={styles.centered}>
          <Spinner size="medium" label="Loading document upload wizard..." />
        </div>
      </div>
    );
  }

  if (error) {
    return (
      <div className={mergeClasses(styles.root, className)}>
        <div className={styles.centered}>
          <Text style={{ color: tokens.colorStatusDangerForeground1 }}>{error}</Text>
        </div>
      </div>
    );
  }

  // ── Render: completed ────────────────────────────────────────────────────
  if (isComplete) {
    return (
      <div className={mergeClasses(styles.root, className)}>
        <div className={styles.centered}>
          <Checkmark24Regular className={styles.successIcon} />
          <Text size={400} weight="semibold">
            {uploadedFiles.length === 1
              ? 'Document uploaded successfully'
              : `${uploadedFiles.length} documents uploaded`}
          </Text>
          <Text style={{ color: tokens.colorNeutralForeground3 }}>
            The document viewer has been opened in a new workspace tab.
          </Text>
        </div>
      </div>
    );
  }

  // ── Render: wizard ───────────────────────────────────────────────────────
  return (
    <div className={mergeClasses(styles.root, className)}>
      {/* Stepper — replaces modal title chrome */}
      <Stepper currentStep={stepIndex} labels={STEP_LABELS} styles={styles} />

      {/* Step content */}
      <div className={styles.content}>
        {stepIndex === 0 && (
          <>
            <Text as="h2" size={500} weight="semibold">
              Select Files
            </Text>
            <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>
              Upload one or more documents for AI analysis. Supported formats: PDF, DOCX, PPTX, XLSX, TXT.
            </Text>

            <FileUploadZone onFilesAccepted={handleFilesAccepted} onValidationErrors={handleValidationErrors} />

            {validationErrors.length > 0 && (
              <div>
                {validationErrors.map((e, i) => (
                  <Text key={i} style={{ color: tokens.colorStatusDangerForeground1, display: 'block' }}>
                    {e.fileName}: {e.reason}
                  </Text>
                ))}
              </div>
            )}

            {uploadedFiles.length > 0 && <UploadedFileList files={uploadedFiles} onRemove={handleRemoveFile} />}
          </>
        )}

        {stepIndex === 1 && (
          <>
            <Text as="h2" size={500} weight="semibold">
              Enter Details
            </Text>
            <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>
              Provide a title and optional description for the uploaded document(s).
            </Text>

            <Field label="Document title" required>
              <Input
                value={documentTitle}
                onChange={(_, v) => setDocumentTitle(v.value)}
                placeholder={uploadedFiles[0]?.name ?? 'Enter document title...'}
                aria-label="Document title"
              />
            </Field>

            <Field label="Description (optional)">
              <Textarea
                value={documentDescription}
                onChange={(_, v) => setDocumentDescription(v.value)}
                placeholder="Add a brief description of this document..."
                rows={4}
                aria-label="Document description"
              />
            </Field>
          </>
        )}

        {stepIndex === 2 && (
          <>
            <Text as="h2" size={500} weight="semibold">
              Review &amp; Upload
            </Text>
            <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>
              Review the details below before uploading.
            </Text>

            <div className={styles.reviewList}>
              <div className={styles.reviewRow}>
                <Text className={styles.reviewLabel}>Files</Text>
                <div>
                  {uploadedFiles.map(f => (
                    <Text key={f.id} className={styles.reviewValue} style={{ display: 'block' }}>
                      {f.name}
                    </Text>
                  ))}
                </div>
              </div>

              <div className={styles.reviewRow}>
                <Text className={styles.reviewLabel}>Title</Text>
                <Text className={styles.reviewValue}>
                  {documentTitle.trim() || uploadedFiles[0]?.name || '(not set)'}
                </Text>
              </div>

              {documentDescription.trim() && (
                <div className={styles.reviewRow}>
                  <Text className={styles.reviewLabel}>Description</Text>
                  <Text className={styles.reviewValue}>{documentDescription.trim()}</Text>
                </div>
              )}
            </div>

            {uploadError && <Text style={{ color: tokens.colorStatusDangerForeground1 }}>{uploadError}</Text>}

            {isUploading && (
              <div style={{ display: 'flex', alignItems: 'center', gap: tokens.spacingHorizontalS }}>
                <Spinner size="tiny" />
                <Text style={{ color: tokens.colorNeutralForeground3 }}>Uploading...</Text>
              </div>
            )}
          </>
        )}
      </div>

      {/* Footer navigation — no modal chrome, workspace-native buttons */}
      <div className={styles.footer}>
        {stepIndex > 0 && (
          <Button
            appearance="subtle"
            icon={<ArrowLeft24Regular />}
            onClick={handleBack}
            disabled={isUploading}
            aria-label="Back"
            data-testid="wizard-back-button"
          >
            Back
          </Button>
        )}

        {stepIndex < STEP_COUNT - 1 ? (
          <Button
            appearance="primary"
            icon={<ArrowRight24Regular />}
            iconPosition="after"
            onClick={handleNext}
            disabled={!canAdvance()}
            aria-label="Next"
            data-testid="wizard-next-button"
          >
            Next
          </Button>
        ) : (
          <Button
            appearance="primary"
            icon={<CloudArrowUp24Regular />}
            onClick={handleUpload}
            disabled={!canAdvance() || isUploading}
            aria-label="Upload"
            data-testid="wizard-next-button"
          >
            {isUploading ? 'Uploading...' : 'Upload'}
          </Button>
        )}
      </div>
    </div>
  );
};

// ---------------------------------------------------------------------------
// serializeState helper (D-08 — query params only)
// ---------------------------------------------------------------------------

/**
 * Serialize the widget's recoverable state for Cosmos DB persistence.
 * Stores the wizardId and last-known step index only — file list and
 * form fields are NOT persisted.
 */
export function serializeDocumentUploadWizardState(
  wizardId: string,
  stepIndex: number
): WidgetState<DocumentUploadWizardData> {
  return {
    widgetType: 'document-upload-wizard',
    version: 1,
    queryParams: {
      wizardId,
      stepIndex: String(stepIndex),
    },
    timestamp: new Date().toISOString(),
  };
}

DocumentUploadWizardWidget.displayName = 'DocumentUploadWizardWidget';

export default DocumentUploadWizardWidget;
