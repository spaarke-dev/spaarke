/**
 * SummarizeFilesDialog.tsx
 * Multi-step wizard dialog for "Summarize New File(s)".
 *
 * Uses WizardShell with 3 steps:
 *   0 — Upload file(s)       (FileUploadZone + UploadedFileList — reused from CreateMatter)
 *   1 — Run Analysis          (SummaryResultsStep — AI-generated summary display)
 *   2 — Next Steps            (SummaryNextStepsStep — action cards)
 */
import * as React from 'react';
import {
  Button,
  MessageBar,
  MessageBarBody,
  Text,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import { CheckmarkCircleFilled } from '@fluentui/react-icons';

import { WizardShell } from '../Wizard/WizardShell';
import type {
  IWizardStepConfig,
  IWizardSuccessConfig,
} from '../Wizard/wizardShellTypes';

import type { IUploadedFile, IFileValidationError } from '../CreateMatter/wizardTypes';
import { FileUploadZone } from '../CreateMatter/FileUploadZone';
import { UploadedFileList } from '../CreateMatter/UploadedFileList';

import { SummaryResultsStep } from './SummaryResultsStep';
import { SummaryNextStepsStep } from './SummaryNextStepsStep';
import type { SummaryActionId } from './SummaryNextStepsStep';
import { runSummarize } from './summarizeService';
import type { ISummarizeResult, SummarizeStatus } from './summarizeTypes';

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface ISummarizeFilesDialogProps {
  open: boolean;
  onClose: () => void;
}

// ---------------------------------------------------------------------------
// File state reducer (reuses pattern from CreateMatter WizardDialog)
// ---------------------------------------------------------------------------

interface IFileState {
  uploadedFiles: IUploadedFile[];
  validationErrors: IFileValidationError[];
}

type FileAction =
  | { type: 'ADD_FILES'; files: IUploadedFile[] }
  | { type: 'REMOVE_FILE'; fileId: string }
  | { type: 'SET_VALIDATION_ERRORS'; errors: IFileValidationError[] }
  | { type: 'CLEAR_VALIDATION_ERRORS' }
  | { type: 'RESET' };

function fileReducer(state: IFileState, action: FileAction): IFileState {
  switch (action.type) {
    case 'ADD_FILES': {
      const existing = new Set(
        state.uploadedFiles.map((f) => `${f.name}::${f.sizeBytes}`),
      );
      const newFiles = action.files.filter(
        (f) => !existing.has(`${f.name}::${f.sizeBytes}`),
      );
      return {
        ...state,
        uploadedFiles: [...state.uploadedFiles, ...newFiles],
        validationErrors: [],
      };
    }
    case 'REMOVE_FILE':
      return {
        ...state,
        uploadedFiles: state.uploadedFiles.filter((f) => f.id !== action.fileId),
      };
    case 'SET_VALIDATION_ERRORS':
      return { ...state, validationErrors: action.errors };
    case 'CLEAR_VALIDATION_ERRORS':
      return { ...state, validationErrors: [] };
    case 'RESET':
      return { uploadedFiles: [], validationErrors: [] };
    default:
      return state;
  }
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  stepTitle: {
    display: 'block',
    color: tokens.colorNeutralForeground1,
    marginBottom: tokens.spacingVerticalXS,
  },
  stepSubtitle: {
    display: 'block',
    color: tokens.colorNeutralForeground3,
    marginBottom: tokens.spacingVerticalM,
  },
  errorBar: {
    flexShrink: 0,
  },
});

// ---------------------------------------------------------------------------
// SummarizeFilesDialog
// ---------------------------------------------------------------------------

export const SummarizeFilesDialog: React.FC<ISummarizeFilesDialogProps> = ({
  open,
  onClose,
}) => {
  const styles = useStyles();

  // ── File state ────────────────────────────────────────────────────────
  const [fileState, fileDispatch] = React.useReducer(fileReducer, {
    uploadedFiles: [],
    validationErrors: [],
  });

  // ── Analysis state ────────────────────────────────────────────────────
  const [summarizeStatus, setSummarizeStatus] = React.useState<SummarizeStatus>('idle');
  const [summarizeResult, setSummarizeResult] = React.useState<ISummarizeResult | null>(null);
  const [summarizeError, setSummarizeError] = React.useState<string | null>(null);
  const abortControllerRef = React.useRef<AbortController | null>(null);

  // ── Next Steps state ──────────────────────────────────────────────────
  const [selectedActions, setSelectedActions] = React.useState<SummaryActionId[]>([]);
  const [includeFullSummary, setIncludeFullSummary] = React.useState(true);

  // ── Reset on open ─────────────────────────────────────────────────────
  React.useEffect(() => {
    if (open) {
      fileDispatch({ type: 'RESET' });
      setSummarizeStatus('idle');
      setSummarizeResult(null);
      setSummarizeError(null);
      setSelectedActions([]);
      setIncludeFullSummary(true);
    }
    return () => {
      abortControllerRef.current?.abort();
    };
  }, [open]);

  // ── Refs for handleFinish (avoids stale closures) ─────────────────────
  const summarizeResultRef = React.useRef(summarizeResult);
  summarizeResultRef.current = summarizeResult;
  const selectedActionsRef = React.useRef(selectedActions);
  selectedActionsRef.current = selectedActions;
  const includeFullSummaryRef = React.useRef(includeFullSummary);
  includeFullSummaryRef.current = includeFullSummary;
  const fileStateRef = React.useRef(fileState);
  fileStateRef.current = fileState;

  // ── File handlers ─────────────────────────────────────────────────────
  const handleFilesAccepted = React.useCallback(
    (files: IUploadedFile[]) => fileDispatch({ type: 'ADD_FILES', files }),
    [],
  );
  const handleValidationErrors = React.useCallback(
    (errors: IFileValidationError[]) => fileDispatch({ type: 'SET_VALIDATION_ERRORS', errors }),
    [],
  );
  const handleRemoveFile = React.useCallback(
    (fileId: string) => fileDispatch({ type: 'REMOVE_FILE', fileId }),
    [],
  );
  const handleClearErrors = React.useCallback(
    () => fileDispatch({ type: 'CLEAR_VALIDATION_ERRORS' }),
    [],
  );

  // ── Run analysis ──────────────────────────────────────────────────────
  const runAnalysis = React.useCallback(async () => {
    if (fileState.uploadedFiles.length === 0) return;

    // Abort any previous in-flight request
    abortControllerRef.current?.abort();
    const controller = new AbortController();
    abortControllerRef.current = controller;

    setSummarizeStatus('loading');
    setSummarizeError(null);

    try {
      const result = await runSummarize(fileState.uploadedFiles, controller.signal);
      if (!controller.signal.aborted) {
        setSummarizeResult(result);
        setSummarizeStatus('success');
      }
    } catch (err: unknown) {
      if (!controller.signal.aborted) {
        const message = err instanceof Error ? err.message : 'An unknown error occurred.';
        setSummarizeError(message);
        setSummarizeStatus('error');
      }
    }
  }, [fileState.uploadedFiles]);

  // Auto-run analysis when entering Step 2 (on first visit with files)
  const analysisAttemptedRef = React.useRef(false);

  // Reset analysis flag when files change
  React.useEffect(() => {
    analysisAttemptedRef.current = false;
    setSummarizeStatus('idle');
    setSummarizeResult(null);
    setSummarizeError(null);
  }, [fileState.uploadedFiles.length]);

  // ── handleFinish ──────────────────────────────────────────────────────
  const handleFinish = React.useCallback(async (): Promise<IWizardSuccessConfig> => {
    const currentResult = summarizeResultRef.current;
    const currentActions = selectedActionsRef.current;

    // Log the selected actions for now — actual integration will be done in follow-up tasks
    console.info('[SummarizeFilesDialog] Finish with actions:', currentActions);
    if (currentResult) {
      console.info('[SummarizeFilesDialog] Summary result available, confidence:', currentResult.confidence);
    }

    const actionLabels = currentActions.length > 0
      ? currentActions.join(', ')
      : 'None selected';

    return {
      icon: (
        <CheckmarkCircleFilled
          fontSize={64}
          style={{ color: tokens.colorPaletteGreenForeground1 }}
        />
      ),
      title: 'Summary Complete',
      body: (
        <Text size={300} style={{ color: tokens.colorNeutralForeground2 }}>
          Your file summary is ready. Selected follow-up actions: {actionLabels}.
        </Text>
      ),
      actions: (
        <Button appearance="secondary" onClick={onClose}>
          Close
        </Button>
      ),
    };
  }, [onClose]);

  // ── Step configurations ───────────────────────────────────────────────

  const stepConfigs: IWizardStepConfig[] = React.useMemo(
    () => [
      // Step 0: Upload file(s)
      {
        id: 'upload-files',
        label: 'Upload file(s)',
        canAdvance: () => fileState.uploadedFiles.length > 0,
        renderContent: () => (
          <>
            <div>
              <Text as="h2" size={500} weight="semibold" className={styles.stepTitle}>
                Upload file(s)
              </Text>
              <Text size={200} className={styles.stepSubtitle}>
                Upload one or more documents to summarize. The AI will analyze and extract
                key information from your files.
              </Text>
            </div>

            {fileState.validationErrors.length > 0 && (
              <MessageBar
                intent="error"
                className={styles.errorBar}
                onMouseEnter={handleClearErrors}
              >
                <MessageBarBody>
                  {fileState.validationErrors.map((err, i) => (
                    <div key={i}>
                      <strong>{err.fileName}</strong>: {err.reason}
                    </div>
                  ))}
                </MessageBarBody>
              </MessageBar>
            )}

            <FileUploadZone
              onFilesAccepted={handleFilesAccepted}
              onValidationErrors={handleValidationErrors}
            />

            {fileState.uploadedFiles.length > 0 && (
              <UploadedFileList
                files={fileState.uploadedFiles}
                onRemove={handleRemoveFile}
              />
            )}
          </>
        ),
      },

      // Step 1: Run Analysis
      {
        id: 'run-analysis',
        label: 'Run Analysis',
        canAdvance: () => summarizeStatus === 'success' && summarizeResult !== null,
        renderContent: () => {
          // Auto-trigger analysis on first render of this step
          if (!analysisAttemptedRef.current && summarizeStatus === 'idle') {
            analysisAttemptedRef.current = true;
            // Use microtask to avoid setState during render
            Promise.resolve().then(() => runAnalysis());
          }

          return (
            <SummaryResultsStep
              status={summarizeStatus}
              result={summarizeResult}
              errorMessage={summarizeError}
              onRetry={runAnalysis}
            />
          );
        },
      },

      // Step 2: Next Steps
      {
        id: 'next-steps',
        label: 'Next Steps',
        canAdvance: () => true,
        isEarlyFinish: () => true,
        renderContent: () => (
          <SummaryNextStepsStep
            selectedActions={selectedActions}
            onSelectionChange={setSelectedActions}
            includeFullSummary={includeFullSummary}
            onIncludeFullSummaryChange={setIncludeFullSummary}
          />
        ),
      },
    ],
    [
      fileState.uploadedFiles,
      fileState.validationErrors,
      summarizeStatus,
      summarizeResult,
      summarizeError,
      selectedActions,
      includeFullSummary,
      styles,
      handleFilesAccepted,
      handleValidationErrors,
      handleRemoveFile,
      handleClearErrors,
      runAnalysis,
    ],
  );

  // ── Render ────────────────────────────────────────────────────────────

  return (
    <WizardShell
      open={open}
      title="Summarize New File(s)"
      ariaLabel="Summarize New File(s)"
      steps={stepConfigs}
      onClose={onClose}
      onFinish={handleFinish}
      finishingLabel="Processing&hellip;"
      finishLabel="Done"
    />
  );
};

// Default export enables React.lazy() dynamic import for bundle-size optimization.
export default SummarizeFilesDialog;
