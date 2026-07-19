/**
 * SummarizeFilesDialog.tsx
 * Multi-step wizard dialog for "Summarize New File(s)".
 *
 * Uses WizardShell with 3 static steps + dynamic follow-on steps:
 *   0 — Upload file(s)       (FileUploadZone + UploadedFileList)
 *   1 — Run Analysis          (SummaryResultsStep — AI-generated summary)
 *   2 — Next Steps            (shared WizardFollowOns FollowOnGrid — card selection)
 *   3+ — Follow-on steps (config-driven via FollowOnCardConfig[]):
 *        - Send Email          (shared SendEmailFollowOnStep + short-summary toggle)
 *        - Create Project      (SummarizeCreateProjectStep)
 *        - Work on Analysis    (SummarizeAnalysisStep)
 *
 * The follow-on card set is expressed as a FollowOnCardConfig[] consumed by the
 * shared, config-driven FollowOnGrid (design.md §5.9) — NOT a local fork. The
 * grid injects/removes each dynamic step imperatively at click time via
 * shellRef.current.addDynamicStep()/removeDynamicStep().
 *
 * This shared library version accepts `authenticatedFetch`, `bffBaseUrl`,
 * `dataService`, and `navigationService` as props — no platform-specific
 * imports are used.
 */
import * as React from 'react';
import { Button, Checkbox, MessageBar, MessageBarBody, Text, makeStyles, tokens } from '@fluentui/react-components';
import { CheckmarkCircleFilled, MailRegular, FolderAddRegular, ClipboardTaskRegular } from '@fluentui/react-icons';

import { WizardShell } from '../Wizard/WizardShell';
import type { IWizardShellHandle, IWizardStepConfig, IWizardSuccessConfig } from '../Wizard/wizardShellTypes';

import type { IUploadedFile, IFileValidationError } from '../FileUpload/fileUploadTypes';
import { FileUploadZone } from '../FileUpload/FileUploadZone';
import { UploadedFileList } from '../FileUpload/UploadedFileList';
import { searchUsersAsLookup } from '../CreateMatterWizard/matterService';

import { SummaryResultsStep } from './SummaryResultsStep';
import { FollowOnGrid, SendEmailFollowOnStep } from '../WizardFollowOns';
import type { FollowOnCardConfig } from '../WizardFollowOns';
import { SummarizeCreateProjectStep } from './SummarizeCreateProjectStep';
import { SummarizeAnalysisStep } from './SummarizeAnalysisStep';
import { streamSummarize } from './summarizeService';
import type { AuthenticatedFetchFn } from './summarizeService';
import { sendCommunication, SendCommunicationError } from '../../services/communicationApi';
import type { ISummarizeResult, SummarizeStatus } from './summarizeTypes';
import type { LinearRunEvent } from '../../hooks/useLinearRunProgress';
import type { ICreateProjectFormState } from '../CreateProjectWizard/projectFormTypes';
import { EMPTY_PROJECT_FORM } from '../CreateProjectWizard/projectFormTypes';
import { ProjectService } from '../CreateProjectWizard/projectService';
import type { IDataService, INavigationService } from '../../types/serviceInterfaces';

// ---------------------------------------------------------------------------
// Local follow-on identifiers + email template builders
// ---------------------------------------------------------------------------

/**
 * The summary-oriented follow-on card ids this wizard offers. These are plain
 * `FollowOnCardConfig.id` strings — `create-project` / `work-on-analysis` are
 * intentionally non-canonical (not in the shared {@link FollowOnId} union), and
 * that is expected: the shared grid accepts any string id (design.md §5.9).
 */
export type SummaryActionId = 'send-email' | 'create-project' | 'work-on-analysis';

/** Subject line for the file-summary email. */
export function buildSummaryEmailSubject(): string {
  return 'Document Summary';
}

/** Body for the file-summary email, using the long or short summary. */
export function buildSummaryEmailBody(summary: string, shortSummary: string, useShort: boolean): string {
  const summaryContent = useShort ? shortSummary : summary;

  return (
    `Dear Colleague,\n\n` +
    `Please find the AI-generated summary of the uploaded documents below.\n\n` +
    `Kind regards,\n` +
    `[Your Name]\n\n` +
    `────────────────────────────────────\n\n` +
    summaryContent
  );
}

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
      const existing = new Set(state.uploadedFiles.map(f => `${f.name}::${f.sizeBytes}`));
      const newFiles = action.files.filter(f => !existing.has(`${f.name}::${f.sizeBytes}`));
      return {
        ...state,
        uploadedFiles: [...state.uploadedFiles, ...newFiles],
        validationErrors: [],
      };
    }
    case 'REMOVE_FILE':
      return {
        ...state,
        uploadedFiles: state.uploadedFiles.filter(f => f.id !== action.fileId),
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

  // ── SSE progress event history (Wave 12 R7) ───────────────────────────
  // Replaces the prior hardcoded numbered step visual. Every `progress`
  // chunk from the BFF /api/workspace/files/summarize SSE stream is appended
  // verbatim and rendered by <LinearRunProgressList> in SummaryResultsStep.
  // The same event shape is what `useLinearRunProgress` produces, so a
  // follow-on refactor can drop `streamSummarize`'s bespoke SSE loop in
  // favor of the shared hook without changing the UI wiring below.
  const [progressEvents, setProgressEvents] = React.useState<LinearRunEvent[]>([]);

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
      setProgressEvents([]);
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
    []
  );
  const handleValidationErrors = React.useCallback(
    (errors: IFileValidationError[]) => fileDispatch({ type: 'SET_VALIDATION_ERRORS', errors }),
    []
  );
  const handleRemoveFile = React.useCallback((fileId: string) => fileDispatch({ type: 'REMOVE_FILE', fileId }), []);
  const handleClearErrors = React.useCallback(() => fileDispatch({ type: 'CLEAR_VALIDATION_ERRORS' }), []);

  // ── Stable search callback ────────────────────────────────────────────
  const handleSearchUsers = React.useCallback(
    (query: string) => (dataService ? searchUsersAsLookup(dataService, query) : Promise.resolve([])),
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
    setProgressEvents([]);

    try {
      const result = await streamSummarize(
        fileState.uploadedFiles,
        {
          // Wave 12 R7 — each server-emitted `progress` chunk is appended to
          // the LinearRunEvent history verbatim. No client-side step
          // interpretation; the shared LinearRunProgressList renders these
          // as scrolling text.
          onProgressEvent: (step, message) => {
            if (controller.signal.aborted) return;
            setProgressEvents(prev => [
              ...prev,
              {
                kind: 'progress',
                step,
                content: message,
                timestamp: new Date(),
              },
            ]);
          },
        },
        controller.signal,
        authenticatedFetch,
        bffBaseUrl
      );
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
  }, [fileState.uploadedFiles, authenticatedFetch, bffBaseUrl]);

  // Auto-run analysis when entering Step 2 (on first visit with files)
  const analysisAttemptedRef = React.useRef(false);

  // Reset analysis flag when files change
  React.useEffect(() => {
    analysisAttemptedRef.current = false;
    setSummarizeStatus('idle');
    setSummarizeResult(null);
    setSummarizeError(null);
    setProgressEvents([]);
  }, [fileState.uploadedFiles.length]);

  // ── Follow-on card set (config-driven, shared WizardFollowOns grid) ───
  // The summary-oriented card set is expressed as FollowOnCardConfig[] rather
  // than a fork of the grid (design.md §5.9). FollowOnGrid injects/removes the
  // dynamic step imperatively at click time via shellRef; each card owns its
  // step body through `renderStep`, reading dialog form state via refs (the
  // same stale-closure-proof pattern the prior hand-rolled sync effect used).
  //
  // `renderSelectedExtra` carries the "include only short summary" toggle inline
  // in the grid while Send Email is selected — the slot added specifically for
  // this wizard, so its summary-specific card stays config, not a grid fork.
  const followOnCards: FollowOnCardConfig[] = React.useMemo(
    () => [
      {
        id: 'send-email',
        label: 'Send Email',
        description: 'Compose and send an email with the file summary.',
        icon: <MailRegular fontSize={28} />,
        isSkippable: true,
        canAdvance: () =>
          emailToRef.current.trim() !== '' &&
          emailSubjectRef.current.trim() !== '' &&
          emailBodyRef.current.trim() !== '',
        renderSelectedExtra: () => (
          <Checkbox
            checked={includeShortSummary}
            onChange={(_e, data) => setIncludeShortSummary(!!data.checked)}
            label="Include only short summary in email"
          />
        ),
        renderStep: () => (
          <SendEmailFollowOnStep
            title="Send Email"
            subtitle="Compose an email with the file summary. It will be sent via the system."
            infoNote="This email will be sent via the BFF communication endpoint."
            emailTo={emailToRef.current}
            onEmailToChange={setEmailTo}
            emailSubject={emailSubjectRef.current}
            onEmailSubjectChange={setEmailSubject}
            emailBody={emailBodyRef.current}
            onEmailBodyChange={setEmailBody}
            onSearchUsers={handleSearchUsers}
            headerContent={
              <Checkbox
                checked={includeShortSummaryRef.current}
                onChange={(_e, data) => setIncludeShortSummary(!!data.checked)}
                label="Include only short summary"
              />
            }
          />
        ),
      },
      {
        id: 'create-project',
        label: 'Create Project',
        description: 'Launch the Create Project wizard with the uploaded files.',
        icon: <FolderAddRegular fontSize={28} />,
        isSkippable: true,
        canAdvance: () => projectFormValidRef.current,
        renderStep: () => (
          <SummarizeCreateProjectStep
            dataService={dataService!}
            uploadedFiles={fileStateRef.current.uploadedFiles}
            onValidChange={setProjectFormValid}
            onFormValues={setProjectFormValues}
            initialFormValues={projectFormValuesRef.current}
          />
        ),
      },
      {
        id: 'work-on-analysis',
        label: 'Work on Analysis',
        description: 'Choose a playbook to run analysis on the uploaded files.',
        icon: <ClipboardTaskRegular fontSize={28} />,
        isSkippable: true,
        canAdvance: () => true, // work-on-analysis has no hard requirement
        renderStep: () => (
          <SummarizeAnalysisStep
            dataService={dataService!}
            navigationService={navigationService}
            uploadedFiles={fileStateRef.current.uploadedFiles}
            authenticatedFetch={authenticatedFetch}
            bffBaseUrl={bffBaseUrl}
          />
        ),
      },
    ],
    [dataService, navigationService, authenticatedFetch, bffBaseUrl, handleSearchUsers, includeShortSummary]
  );

  // ── Pre-fill email fields when send-email is selected ─────────────────
  React.useEffect(() => {
    if (selectedActions.includes('send-email') && summarizeResult && !emailSubject) {
      setEmailSubject(buildSummaryEmailSubject());
      setEmailBody(buildSummaryEmailBody(summarizeResult.summary, summarizeResult.shortSummary, includeShortSummary));
    }
  }, [selectedActions, summarizeResult, emailSubject, includeShortSummary]);

  // ── Update email body when short summary toggle changes ───────────────
  React.useEffect(() => {
    if (selectedActions.includes('send-email') && summarizeResult) {
      setEmailBody(buildSummaryEmailBody(summarizeResult.summary, summarizeResult.shortSummary, includeShortSummary));
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

    // ── Send Email via canonical sendCommunication() (ADR-045) ─────────
    // Task 060 (W6): replaced the prior inline BFF send fetch with the typed
    // wrapper so this follow-on shares the single canonical send path +
    // ProblemDetails error contract (ADR-019). `bodyFormat: 'text'` maps to
    // the BFF `PlainText` enum inside the wrapper.
    if (currentSelectedActions.includes('send-email') && currentEmailTo.trim()) {
      try {
        if (!authenticatedFetch) {
          throw new Error(
            '[SummarizeFilesDialog] authenticatedFetch is required — unauthenticated BFF calls are not permitted.'
          );
        }
        await sendCommunication(
          {
            to: currentEmailTo
              .split(/[;,]/)
              .map((a: string) => a.trim())
              .filter(Boolean),
            subject: currentEmailSubject,
            body: currentEmailBody,
            bodyFormat: 'text',
            sendMode: 'sharedMailbox',
          },
          { authenticatedFetch, bffBaseUrl }
        );
        completedActions.push('Email sent');
      } catch (err) {
        const detail =
          err instanceof SendCommunicationError
            ? err.detail
            : err instanceof Error
              ? err.message
              : 'Unknown error';
        warnings.push(`Email failed: ${detail}`);
      }
    }

    // ── Create Project via Dataverse ──────────────────────────────────
    // Guard on projectFormValid: with the shared grid's `isSkippable` Skip the
    // card stays selected even when skipped (unlike the prior deselect-on-skip
    // footer button), so only create when the form was actually completed —
    // matching the original flow, which could never Finish with an invalid
    // create-project step selected (Finish was disabled by canAdvance).
    if (currentSelectedActions.includes('create-project') && projectFormValidRef.current && dataService) {
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

    const actionSummary = completedActions.length > 0 ? completedActions.join(', ') : 'No follow-on actions';

    const viewProject = createdProjectId
      ? () => {
          if (navigationService) {
            void navigationService.openRecord('sprk_project', createdProjectId!);
          }
          onClose();
        }
      : undefined;

    return {
      icon: <CheckmarkCircleFilled fontSize={64} style={{ color: tokens.colorPaletteGreenForeground1 }} />,
      title: warnings.length > 0 ? 'Summary Complete (with warnings)' : 'Summary Complete',
      body: (
        <Text size={300} style={{ color: tokens.colorNeutralForeground2 }}>
          Your file summary is ready. {actionSummary}.
        </Text>
      ),
      actions: (
        <>
          {viewProject && (
            <Button appearance="primary" onClick={viewProject} aria-label={`View project: ${createdProjectName}`}>
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
                Upload one or more documents to summarize. The AI will analyze and extract key information from your
                files.
              </Text>
            </div>

            {fileState.validationErrors.length > 0 && (
              <MessageBar intent="error" className={styles.errorBar} onMouseEnter={handleClearErrors}>
                <MessageBarBody>
                  {fileState.validationErrors.map((err, i) => (
                    <div key={i}>
                      <strong>{err.fileName}</strong>: {err.reason}
                    </div>
                  ))}
                </MessageBarBody>
              </MessageBar>
            )}

            <FileUploadZone onFilesAccepted={handleFilesAccepted} onValidationErrors={handleValidationErrors} />

            {fileState.uploadedFiles.length > 0 && (
              <UploadedFileList files={fileState.uploadedFiles} onRemove={handleRemoveFile} />
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
              events={progressEvents}
            />
          );
        },
      },

      // Step 2: Next Steps (shared, config-driven FollowOnGrid)
      {
        id: 'next-steps',
        label: 'Next Steps',
        canAdvance: () => true,
        isEarlyFinish: () => selectedActions.length === 0,
        renderContent: () => (
          <FollowOnGrid
            cards={followOnCards}
            selected={selectedActions}
            onSelectionChange={next => setSelectedActions(next as SummaryActionId[])}
            addDynamicStep={(cfg, order) => shellRef.current?.addDynamicStep(cfg, order)}
            removeDynamicStep={id => shellRef.current?.removeDynamicStep(id)}
            title="Next Steps"
            subtitle="Choose what you'd like to do with the summary results. Select one or more actions, or click Finish to close."
            emptyHint="No actions selected — click Finish to complete without follow-on steps."
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
      followOnCards,
      progressEvents,
      styles,
      handleFilesAccepted,
      handleValidationErrors,
      handleRemoveFile,
      handleClearErrors,
      runAnalysis,
    ]
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
      hideTitle={embedded}
    />
  );
};

// Default export enables React.lazy() dynamic import for bundle-size optimization.
export default SummarizeFilesDialog;
