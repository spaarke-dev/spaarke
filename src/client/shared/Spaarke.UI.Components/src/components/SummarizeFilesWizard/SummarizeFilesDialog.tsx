/**
 * SummarizeFilesDialog.tsx
 * Multi-step wizard dialog for "Summarize New File(s)".
 *
 * Uses WizardShell with 3 static steps + dynamic follow-on steps:
 *   0 — Upload file(s)       (FileUploadZone + UploadedFileList)
 *   1 — Run Analysis          (SummaryResultsStep — AI-generated summary)
 *   2 — Next Steps            (SummaryNextStepsStep — card selection)
 *   3+ — Follow-on steps:
 *        - Send Email          (SummarizeSendEmailStep)
 *        - Create Project      (SummarizeCreateProjectStep)
 *        - Work on Analysis    (SummarizeAnalysisStep)
 *
 * Dynamic steps are injected/removed via shellRef.current.addDynamicStep()
 * / removeDynamicStep(), mirroring the CreateMatter/WizardDialog pattern.
 *
 * This shared library version accepts `authenticatedFetch`, `bffBaseUrl`,
 * `dataService`, and `navigationService` as props — no platform-specific
 * imports are used.
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
  IWizardShellHandle,
  IWizardStepConfig,
  IWizardSuccessConfig,
} from '../Wizard/wizardShellTypes';

import type { IUploadedFile, IFileValidationError } from '../FileUpload/fileUploadTypes';
import { FileUploadZone } from '../FileUpload/FileUploadZone';
import { UploadedFileList } from '../FileUpload/UploadedFileList';
import { searchUsersAsLookup } from '../CreateMatterWizard/matterService';

import { SummaryResultsStep } from './SummaryResultsStep';
import {
  SummaryNextStepsStep,
  FOLLOW_ON_STEP_ID_MAP,
  FOLLOW_ON_STEP_LABEL_MAP,
  FOLLOW_ON_CANONICAL_ORDER,
} from './SummaryNextStepsStep';
import type { SummaryActionId } from './SummaryNextStepsStep';
import {
  SummarizeSendEmailStep,
  buildSummaryEmailSubject,
  buildSummaryEmailBody,
} from './SummarizeSendEmailStep';
import { SummarizeCreateProjectStep } from './SummarizeCreateProjectStep';
import { SummarizeAnalysisStep } from './SummarizeAnalysisStep';
import { streamSummarize } from './summarizeService';
import type { AuthenticatedFetchFn } from './summarizeService';
import type { ISummarizeResult, SummarizeStatus } from './summarizeTypes';
import { DOCUMENT_ANALYSIS_STEPS } from '../AiProgressStepper';
import type { ICreateProjectFormState } from '../CreateProjectWizard/projectFormTypes';
import { EMPTY_PROJECT_FORM } from '../CreateProjectWizard/projectFormTypes';
import { ProjectService } from '../CreateProjectWizard/projectService';
import type { IDataService, INavigationService } from '../../types/serviceInterfaces';

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface ISummarizeFilesDialogProps {
  open: boolean;
  onClose: () => void;
  /** IDataService for Dataverse operations. */
  dataService?: IDataService;
  /** Navigation service for opening entity records. */
  navigationService?: INavigationService;
  /** Authenticated fetch function for BFF API calls. */
  authenticatedFetch?: AuthenticatedFetchFn;
  /** Base URL for the BFF API. */
  bffBaseUrl?: string;
  /** When true, hides the built-in dialog chrome (for Dataverse embedded mode). */
  embedded?: boolean;
}

// ---------------------------------------------------------------------------
// File state reducer
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
  dataService,
  navigationService,
  authenticatedFetch,
  bffBaseUrl,
  embedded,
}) => {
  const styles = useStyles();
  const shellRef = React.useRef<IWizardShellHandle>(null);

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

  // ── SSE progress step state (driven by real backend events) ──────────
  const ALL_STEP_IDS = React.useMemo(() => DOCUMENT_ANALYSIS_STEPS.map((s) => s.id), []);
  const [activeStepId, setActiveStepId] = React.useState<string | null>(null);
  const [completedStepIds, setCompletedStepIds] = React.useState<string[]>([]);

  // ── Next Steps state ──────────────────────────────────────────────────
  const [selectedActions, setSelectedActions] = React.useState<SummaryActionId[]>([]);
  const [includeShortSummary, setIncludeShortSummary] = React.useState(false);

  // ── Send Email state ──────────────────────────────────────────────────
  const [emailTo, setEmailTo] = React.useState('');
  const [emailSubject, setEmailSubject] = React.useState('');
  const [emailBody, setEmailBody] = React.useState('');

  // ── Create Project state ──────────────────────────────────────────────
  const [projectFormValues, setProjectFormValues] = React.useState<ICreateProjectFormState>({ ...EMPTY_PROJECT_FORM });
  const [projectFormValid, setProjectFormValid] = React.useState(false);

  // ── Reset on open ─────────────────────────────────────────────────────
  React.useEffect(() => {
    if (open) {
      fileDispatch({ type: 'RESET' });
      setSummarizeStatus('idle');
      setSummarizeResult(null);
      setSummarizeError(null);
      setActiveStepId(null);
      setCompletedStepIds([]);
      setSelectedActions([]);
      setIncludeShortSummary(false);
      setEmailTo('');
      setEmailSubject('');
      setEmailBody('');
      setProjectFormValues({ ...EMPTY_PROJECT_FORM });
      setProjectFormValid(false);
    }
    return () => {
      abortControllerRef.current?.abort();
    };
  }, [open]);

  // ── Refs for dynamic step closures (prevents stale closure bug) ───────
  const summarizeResultRef = React.useRef(summarizeResult);
  summarizeResultRef.current = summarizeResult;
  const selectedActionsRef = React.useRef(selectedActions);
  selectedActionsRef.current = selectedActions;
  const includeShortSummaryRef = React.useRef(includeShortSummary);
  includeShortSummaryRef.current = includeShortSummary;
  const fileStateRef = React.useRef(fileState);
  fileStateRef.current = fileState;
  const emailToRef = React.useRef(emailTo);
  emailToRef.current = emailTo;
  const emailSubjectRef = React.useRef(emailSubject);
  emailSubjectRef.current = emailSubject;
  const emailBodyRef = React.useRef(emailBody);
  emailBodyRef.current = emailBody;
  const projectFormValuesRef = React.useRef(projectFormValues);
  projectFormValuesRef.current = projectFormValues;
  const projectFormValidRef = React.useRef(projectFormValid);
  projectFormValidRef.current = projectFormValid;

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

  // ── Stable search callback ────────────────────────────────────────────
  const handleSearchUsers = React.useCallback(
    (query: string) => dataService ? searchUsersAsLookup(dataService, query) : Promise.resolve([]),
    [dataService]
  );

  // ── Run analysis ──────────────────────────────────────────────────────
  const runAnalysis = React.useCallback(async () => {
    if (fileState.uploadedFiles.length === 0) return;

    abortControllerRef.current?.abort();
    const controller = new AbortController();
    abortControllerRef.current = controller;

    setSummarizeStatus('loading');
    setSummarizeError(null);
    setActiveStepId(ALL_STEP_IDS[0] ?? null);
    setCompletedStepIds([]);

    try {
      const result = await streamSummarize(
        fileState.uploadedFiles,
        {
          onProgress: (stepId) => {
            if (controller.signal.aborted) return;
            setActiveStepId(stepId);
            setCompletedStepIds((prev) => {
              const idx = ALL_STEP_IDS.indexOf(stepId);
              return idx > 0 ? ALL_STEP_IDS.slice(0, idx) : prev;
            });
          },
        },
        controller.signal,
        authenticatedFetch,
        bffBaseUrl,
      );
      if (!controller.signal.aborted) {
        setSummarizeResult(result);
        setSummarizeStatus('success');
        setCompletedStepIds(ALL_STEP_IDS);
        setActiveStepId(null);
      }
    } catch (err: unknown) {
      if (!controller.signal.aborted) {
        const message = err instanceof Error ? err.message : 'An unknown error occurred.';
        setSummarizeError(message);
        setSummarizeStatus('error');
      }
    }
  }, [fileState.uploadedFiles, ALL_STEP_IDS, authenticatedFetch, bffBaseUrl]);

  // Auto-run analysis when entering Step 2 (on first visit with files)
  const analysisAttemptedRef = React.useRef(false);

  // Reset analysis flag when files change
  React.useEffect(() => {
    analysisAttemptedRef.current = false;
    setSummarizeStatus('idle');
    setSummarizeResult(null);
    setSummarizeError(null);
    setActiveStepId(null);
    setCompletedStepIds([]);
  }, [fileState.uploadedFiles.length]);

  // ── Skip handler for follow-on steps ────────────────────────────────
  // Deselecting the action removes the dynamic step, causing the shell
  // to advance to the next remaining step automatically.
  const handleSkipAction = React.useCallback(
    (actionId: SummaryActionId) => {
      setSelectedActions((prev) => prev.filter((a) => a !== actionId));
    },
    []
  );

  // ── Sync dynamic steps with selected action cards (via shellRef) ──────
  const prevSelectedActionsRef = React.useRef<SummaryActionId[]>([]);

  React.useEffect(() => {
    const prev = prevSelectedActionsRef.current;
    const next = selectedActions;

    // Add newly selected follow-on steps
    next.forEach((actionId) => {
      if (!prev.includes(actionId)) {
        const stepId = FOLLOW_ON_STEP_ID_MAP[actionId];
        const stepLabel = FOLLOW_ON_STEP_LABEL_MAP[actionId];

        const dynamicConfig: IWizardStepConfig = {
          id: stepId,
          label: stepLabel,
          canAdvance: () => {
            if (stepId === 'followon-send-email') {
              return emailToRef.current.trim() !== '' &&
                emailSubjectRef.current.trim() !== '' &&
                emailBodyRef.current.trim() !== '';
            }
            if (stepId === 'followon-create-project') {
              return projectFormValidRef.current;
            }
            return true; // work-on-analysis has no hard requirement
          },
          footerActions: (
            <Button
              appearance="subtle"
              onClick={() => handleSkipAction(actionId)}
            >
              Skip
            </Button>
          ),
          renderContent: () => {
            if (stepId === 'followon-send-email') {
              return (
                <SummarizeSendEmailStep
                  emailTo={emailToRef.current}
                  onEmailToChange={setEmailTo}
                  emailSubject={emailSubjectRef.current}
                  onEmailSubjectChange={setEmailSubject}
                  emailBody={emailBodyRef.current}
                  onEmailBodyChange={setEmailBody}
                  onSearchUsers={handleSearchUsers}
                  includeShortSummary={includeShortSummaryRef.current}
                  onIncludeShortSummaryChange={setIncludeShortSummary}
                />
              );
            }
            if (stepId === 'followon-create-project') {
              return (
                <SummarizeCreateProjectStep
                  dataService={dataService!}
                  uploadedFiles={fileStateRef.current.uploadedFiles}
                  onValidChange={setProjectFormValid}
                  onFormValues={setProjectFormValues}
                  initialFormValues={projectFormValuesRef.current}
                />
              );
            }
            if (stepId === 'followon-work-on-analysis') {
              return (
                <SummarizeAnalysisStep
                  dataService={dataService!}
                  navigationService={navigationService}
                />
              );
            }
            return <Text size={300}>{stepLabel}</Text>;
          },
        };

        shellRef.current?.addDynamicStep(dynamicConfig, FOLLOW_ON_CANONICAL_ORDER);
      }
    });

    // Remove deselected follow-on steps
    prev.forEach((actionId) => {
      if (!next.includes(actionId)) {
        shellRef.current?.removeDynamicStep(FOLLOW_ON_STEP_ID_MAP[actionId]);
      }
    });

    prevSelectedActionsRef.current = next;
  }, [selectedActions, dataService, navigationService, handleSearchUsers, handleSkipAction]);

  // ── Pre-fill email fields when send-email is selected ─────────────────
  React.useEffect(() => {
    if (
      selectedActions.includes('send-email') &&
      summarizeResult &&
      !emailSubject
    ) {
      setEmailSubject(buildSummaryEmailSubject());
      setEmailBody(
        buildSummaryEmailBody(
          summarizeResult.summary,
          summarizeResult.shortSummary,
          includeShortSummary,
        )
      );
    }
  }, [selectedActions, summarizeResult, emailSubject, includeShortSummary]);

  // ── Update email body when short summary toggle changes ───────────────
  React.useEffect(() => {
    if (selectedActions.includes('send-email') && summarizeResult) {
      setEmailBody(
        buildSummaryEmailBody(
          summarizeResult.summary,
          summarizeResult.shortSummary,
          includeShortSummary,
        )
      );
    }
  // Only re-run when the toggle changes, not on every render
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [includeShortSummary]);

  // ── handleFinish ──────────────────────────────────────────────────────
  const handleFinish = React.useCallback(async (): Promise<IWizardSuccessConfig> => {
    const currentSelectedActions = selectedActionsRef.current;
    const currentEmailTo = emailToRef.current;
    const currentEmailSubject = emailSubjectRef.current;
    const currentEmailBody = emailBodyRef.current;
    const currentProjectFormValues = projectFormValuesRef.current;

    const completedActions: string[] = [];
    const warnings: string[] = [];
    let createdProjectId: string | undefined;
    let createdProjectName: string | undefined;

    // ── Send Email via BFF ────────────────────────────────────────────
    if (currentSelectedActions.includes('send-email') && currentEmailTo.trim()) {
      try {
        const fetchFn = authenticatedFetch ?? window.fetch.bind(window);
        const baseUrl = bffBaseUrl ?? '';
        const response = await fetchFn(
          `${baseUrl}/api/communications/send`,
          {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
              to: currentEmailTo.split(/[;,]/).map((a: string) => a.trim()).filter(Boolean),
              subject: currentEmailSubject,
              body: currentEmailBody,
              bodyFormat: 'Text',
            }),
          }
        );

        if (response.ok) {
          completedActions.push('Email sent');
        } else {
          const errorText = await response.text().catch(() => 'Unknown error');
          warnings.push(`Email failed: ${errorText}`);
        }
      } catch (err) {
        warnings.push(`Email failed: ${err instanceof Error ? err.message : 'Unknown error'}`);
      }
    }

    // ── Create Project via Dataverse ──────────────────────────────────
    if (currentSelectedActions.includes('create-project') && dataService) {
      try {
        const service = new ProjectService(dataService);
        const result = await service.createProject(currentProjectFormValues);

        if (result.success) {
          completedActions.push(`Project "${result.projectName}" created`);
          createdProjectId = result.projectId;
          createdProjectName = result.projectName;
        } else {
          warnings.push(`Project creation failed: ${result.errorMessage}`);
        }
      } catch (err) {
        warnings.push(`Project creation failed: ${err instanceof Error ? err.message : 'Unknown error'}`);
      }
    }

    // ── Work on Analysis — handled inline (user clicks card) ─────────
    if (currentSelectedActions.includes('work-on-analysis')) {
      completedActions.push('Analysis step viewed');
    }

    const actionSummary = completedActions.length > 0
      ? completedActions.join(', ')
      : 'No follow-on actions';

    const viewProject = createdProjectId ? () => {
      if (navigationService) {
        void navigationService.openRecord('sprk_project', createdProjectId!);
      }
      onClose();
    } : undefined;

    return {
      icon: (
        <CheckmarkCircleFilled
          fontSize={64}
          style={{ color: tokens.colorPaletteGreenForeground1 }}
        />
      ),
      title: warnings.length > 0 ? 'Summary Complete (with warnings)' : 'Summary Complete',
      body: (
        <Text size={300} style={{ color: tokens.colorNeutralForeground2 }}>
          Your file summary is ready. {actionSummary}.
        </Text>
      ),
      actions: (
        <>
          {viewProject && (
            <Button
              appearance="primary"
              onClick={viewProject}
              aria-label={`View project: ${createdProjectName}`}
            >
              View Project
            </Button>
          )}
          <Button appearance="secondary" onClick={onClose}>
            Close
          </Button>
        </>
      ),
      warnings,
    };
  }, [dataService, navigationService, authenticatedFetch, bffBaseUrl, onClose]);

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
            Promise.resolve().then(() => runAnalysis());
          }

          return (
            <SummaryResultsStep
              status={summarizeStatus}
              result={summarizeResult}
              errorMessage={summarizeError}
              onRetry={runAnalysis}
              activeStepId={activeStepId}
              completedStepIds={completedStepIds}
            />
          );
        },
      },

      // Step 2: Next Steps
      {
        id: 'next-steps',
        label: 'Next Steps',
        canAdvance: () => true,
        isEarlyFinish: () => selectedActions.length === 0,
        renderContent: () => (
          <SummaryNextStepsStep
            selectedActions={selectedActions}
            onSelectionChange={setSelectedActions}
            includeShortSummary={includeShortSummary}
            onIncludeShortSummaryChange={setIncludeShortSummary}
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
      includeShortSummary,
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
      ref={shellRef}
      open={open}
      title="Summarize New File(s)"
      ariaLabel="Summarize New File(s)"
      steps={stepConfigs}
      onClose={onClose}
      onFinish={handleFinish}
      finishingLabel="Processing&hellip;"
      finishLabel="Done"
      embedded={embedded}
    />
  );
};

// Default export enables React.lazy() dynamic import for bundle-size optimization.
export default SummarizeFilesDialog;
