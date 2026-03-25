/**
 * WorkAssignmentWizardDialog.tsx
 * Orchestrator for the Work Assignment creation wizard.
 *
 * Uses WizardShell directly (not CreateRecordWizard) because the step
 * sequence differs: Step 1 is record selection, Step 2 is file upload,
 * and follow-on steps have different fields.
 *
 * Steps:
 *   [0] Work to Assign (SelectWorkStep)
 *   [1] Add Files (AddFilesStep) -- skippable
 *   [2] Enter Info (EnterInfoStep) -- pre-filled from record or AI
 *   [3] Next Steps (NextStepsSelectionStep) -- early finish if 0 selected
 *   [4+] Dynamic: Assign Work, Send Email, Create Event
 *
 * Processing:
 *   When files are uploaded, the finish handler uploads to SPE, creates
 *   document records, and triggers AI analysis (same as Create Matter).
 *   The finishing label changes to "Processing..." when files exist.
 *
 * Dependencies are injected via props -- no solution-specific imports.
 */
import * as React from 'react';
import { Button, Text, Spinner, tokens } from '@fluentui/react-components';
import { CheckmarkCircleFilled } from '@fluentui/react-icons';

import { WizardShell } from '../Wizard/WizardShell';
import type {
  IWizardShellHandle,
  IWizardStepConfig,
  IWizardSuccessConfig,
} from '../Wizard/wizardShellTypes';
import { SendEmailStep } from '../CreateRecordWizard/steps/SendEmailStep';
import { searchUsersAsLookup } from './workAssignmentService';

import type {
  ICreateWorkAssignmentFormState,
  IAssignWorkState,
  ICreateFollowOnEventState,
  WorkAssignmentFollowOnId,
} from './formTypes';
import {
  EMPTY_WORK_ASSIGNMENT_FORM,
  EMPTY_ASSIGN_WORK_STATE,
  EMPTY_FOLLOW_ON_EVENT_STATE,
  WA_FOLLOW_ON_STEP_ID_MAP,
  WA_FOLLOW_ON_STEP_LABEL_MAP,
  WA_FOLLOW_ON_CANONICAL_ORDER,
} from './formTypes';
import { WorkAssignmentService } from './workAssignmentService';
import { SelectWorkStep } from './SelectWorkStep';
import { AddFilesStep } from './AddFilesStep';
import { EnterInfoStep } from './EnterInfoStep';
import { NextStepsSelectionStep } from './NextStepsSelectionStep';
import { AssignWorkStep } from './AssignWorkStep';
import { CreateFollowOnEventStep } from './CreateFollowOnEventStep';
import type { IUploadedFile } from '../FileUpload/fileUploadTypes';
import type { IDataService, INavigationService } from '../../types/serviceInterfaces';
import type { AuthenticatedFetchFn } from '../../services/EntityCreationService';

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface IWorkAssignmentWizardDialogProps {
  open: boolean;
  onClose: () => void;
  /** IDataService for Dataverse operations. */
  dataService: IDataService;
  /**
   * Authenticated fetch function for BFF API calls.
   * Required for AI pre-fill and file upload features.
   */
  authenticatedFetch: AuthenticatedFetchFn;
  /**
   * BFF API base URL (e.g. "https://spe-api-dev-67e2xz.azurewebsites.net/api").
   */
  bffBaseUrl: string;
  /**
   * SPE container ID. If not provided, the wizard will attempt
   * to resolve it from the business unit (requires Xrm context).
   */
  containerId?: string;
  /**
   * Optional navigation service for opening entity records.
   * If provided, the success screen "View Record" button will use this.
   */
  navigationService?: INavigationService;
  /**
   * When `embedded={true}`, the wizard relies on the Dataverse modal chrome
   * for the title bar and close button. Default: false.
   */
  embedded?: boolean;
  /**
   * Resolves the SPE container ID for file uploads.
   * Called once during the finish handler. If not provided and containerId is
   * not set, file uploads will be skipped.
   */
  resolveSpeContainerId?: () => Promise<string>;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

const WorkAssignmentWizardDialog: React.FC<IWorkAssignmentWizardDialogProps> = ({
  open,
  onClose,
  dataService,
  authenticatedFetch,
  bffBaseUrl,
  containerId,
  navigationService,
  embedded,
  resolveSpeContainerId,
}) => {
  const shellRef = React.useRef<IWizardShellHandle>(null);

  // -- Form state ------------------------------------------------------------
  const [formState, setFormState] = React.useState<ICreateWorkAssignmentFormState>(EMPTY_WORK_ASSIGNMENT_FORM);
  const [uploadedFiles, setUploadedFiles] = React.useState<IUploadedFile[]>([]);
  const [selectedActions, setSelectedActions] = React.useState<WorkAssignmentFollowOnId[]>([]);
  const [assignWorkState, setAssignWorkState] = React.useState<IAssignWorkState>(EMPTY_ASSIGN_WORK_STATE);
  const [followOnEventState, setFollowOnEventState] = React.useState<ICreateFollowOnEventState>(EMPTY_FOLLOW_ON_EVENT_STATE);
  const [emailTo, setEmailTo] = React.useState('');
  const [emailSubject, setEmailSubject] = React.useState('');
  const [emailBody, setEmailBody] = React.useState('');

  // Pre-fill values from selected record (loaded asynchronously)
  const [prefillValues, setPrefillValues] = React.useState<Partial<ICreateWorkAssignmentFormState> | undefined>(undefined);
  const [isPrefilling, setIsPrefilling] = React.useState(false);

  // Email CC state
  const [emailCc, setEmailCc] = React.useState('');

  // -- canAdvance tracking via refs ------------------------------------------
  const selectWorkValidRef = React.useRef(false);
  const enterInfoValidRef = React.useRef(false);
  const followOnEventValidRef = React.useRef(true);

  // -- Refs for stale closure prevention -------------------------------------
  const formStateRef = React.useRef(formState);
  formStateRef.current = formState;
  const uploadedFilesRef = React.useRef(uploadedFiles);
  uploadedFilesRef.current = uploadedFiles;
  const selectedActionsRef = React.useRef(selectedActions);
  selectedActionsRef.current = selectedActions;
  const assignWorkStateRef = React.useRef(assignWorkState);
  assignWorkStateRef.current = assignWorkState;
  const followOnEventStateRef = React.useRef(followOnEventState);
  followOnEventStateRef.current = followOnEventState;
  const emailToRef = React.useRef(emailTo);
  emailToRef.current = emailTo;
  const emailSubjectRef = React.useRef(emailSubject);
  emailSubjectRef.current = emailSubject;
  const emailBodyRef = React.useRef(emailBody);
  emailBodyRef.current = emailBody;
  const emailCcRef = React.useRef(emailCc);
  emailCcRef.current = emailCc;

  // Refs for pre-fill state (stale closure prevention in renderContent)
  const isPrefillRef = React.useRef(isPrefilling);
  isPrefillRef.current = isPrefilling;
  const prefillRef = React.useRef(prefillValues);
  prefillRef.current = prefillValues;

  // -- Service (re-created when containerId resolves) ------------------------
  const [resolvedContainerId, setResolvedContainerId] = React.useState(containerId ?? '');
  const serviceRef = React.useRef<WorkAssignmentService | null>(null);
  if (!serviceRef.current) {
    serviceRef.current = new WorkAssignmentService(dataService, authenticatedFetch, bffBaseUrl, resolvedContainerId || containerId);
  }

  // -- Resolve container ID on open ------------------------------------------
  React.useEffect(() => {
    if (!open) return;
    if (containerId) {
      setResolvedContainerId(containerId);
      serviceRef.current = new WorkAssignmentService(dataService, authenticatedFetch, bffBaseUrl, containerId);
      return;
    }
    if (resolveSpeContainerId) {
      let cancelled = false;
      resolveSpeContainerId().then((cid) => {
        if (!cancelled && cid) {
          setResolvedContainerId(cid);
          serviceRef.current = new WorkAssignmentService(dataService, authenticatedFetch, bffBaseUrl, cid);
        }
      }).catch((err) => {
        console.warn('[WorkAssignmentWizard] Container ID resolution failed:', err);
      });
      return () => { cancelled = true; };
    }
    return undefined;
  }, [open, dataService, authenticatedFetch, bffBaseUrl, containerId, resolveSpeContainerId]);

  // -- Reset on open ---------------------------------------------------------
  React.useEffect(() => {
    if (open) {
      setFormState(EMPTY_WORK_ASSIGNMENT_FORM);
      setUploadedFiles([]);
      setSelectedActions([]);
      setAssignWorkState(EMPTY_ASSIGN_WORK_STATE);
      setFollowOnEventState(EMPTY_FOLLOW_ON_EVENT_STATE);
      setEmailTo('');
      setEmailCc('');
      setEmailSubject('');
      setEmailBody('');
      setPrefillValues(undefined);
      setIsPrefilling(false);
      selectWorkValidRef.current = false;
      enterInfoValidRef.current = false;
      followOnEventValidRef.current = true;
    }
  }, [open]);

  // -- Step validity callbacks -----------------------------------------------
  const handleSelectWorkValid = React.useCallback((valid: boolean) => {
    selectWorkValidRef.current = valid;
    shellRef.current?.requestUpdate();
  }, []);

  const handleSelectWorkValues = React.useCallback(
    (values: Pick<ICreateWorkAssignmentFormState, 'recordType' | 'recordId' | 'recordName'>) => {
      setFormState((prev) => ({ ...prev, ...values }));
    },
    []
  );

  // -- Pre-fill from selected record -----------------------------------------
  React.useEffect(() => {
    const { recordType, recordId } = formState;
    if (!recordId || !recordType) {
      setPrefillValues(undefined);
      return;
    }

    let cancelled = false;
    (async () => {
      setIsPrefilling(true);
      try {
        const values = await serviceRef.current!.readRecordForPrefill(
          recordType as 'matter' | 'project' | 'invoice' | 'event',
          recordId
        );
        if (!cancelled) {
          setPrefillValues(values);
        }
      } catch {
        // Non-fatal -- user can still fill in manually
      } finally {
        if (!cancelled) setIsPrefilling(false);
      }
    })();

    return () => { cancelled = true; };
  }, [formState.recordId, formState.recordType]);

  const handleUploadedFilesChange = React.useCallback((files: IUploadedFile[]) => setUploadedFiles(files), []);

  const handleEnterInfoValid = React.useCallback((valid: boolean) => {
    enterInfoValidRef.current = valid;
    shellRef.current?.requestUpdate();
  }, []);

  const handleEnterInfoValues = React.useCallback(
    (values: Pick<ICreateWorkAssignmentFormState, 'name' | 'description' | 'matterTypeId' | 'matterTypeName' | 'practiceAreaId' | 'practiceAreaName' | 'priority' | 'responseDueDate'>) => {
      setFormState((prev) => ({ ...prev, ...values }));
    },
    []
  );

  const handleFollowOnEventValid = React.useCallback((valid: boolean) => {
    followOnEventValidRef.current = valid;
    shellRef.current?.requestUpdate();
  }, []);

  const handleSearchUsers = React.useCallback(
    (query: string) => searchUsersAsLookup(dataService, query),
    [dataService]
  );

  // -- Dynamic step injection ------------------------------------------------
  const prevSelectedActionsRef = React.useRef<WorkAssignmentFollowOnId[]>([]);

  React.useEffect(() => {
    const prev = prevSelectedActionsRef.current;
    const next = selectedActions;

    next.forEach((actionId) => {
      if (!prev.includes(actionId)) {
        const stepId = WA_FOLLOW_ON_STEP_ID_MAP[actionId];
        const stepLabel = WA_FOLLOW_ON_STEP_LABEL_MAP[actionId];

        const dynamicConfig: IWizardStepConfig = {
          id: stepId,
          label: stepLabel,
          canAdvance: () => {
            if (stepId === 'followon-wa-send-email') {
              return (
                emailToRef.current.trim() !== '' &&
                emailSubjectRef.current.trim() !== '' &&
                emailBodyRef.current.trim() !== ''
              );
            }
            if (stepId === 'followon-wa-create-event') {
              return followOnEventValidRef.current;
            }
            return true;
          },
          renderContent: () => {
            if (stepId === 'followon-wa-assign-work') {
              return (
                <AssignWorkStep
                  dataService={dataService}
                  authenticatedFetch={authenticatedFetch}
                  bffBaseUrl={bffBaseUrl}
                  containerId={containerId}
                  onFormValues={setAssignWorkState}
                  initialValues={assignWorkStateRef.current}
                />
              );
            }
            if (stepId === 'followon-wa-send-email') {
              return (
                <SendEmailStep
                  title="Send Email"
                  emailTo={emailToRef.current}
                  onEmailToChange={setEmailTo}
                  emailCc={emailCcRef.current}
                  onEmailCcChange={setEmailCc}
                  emailSubject={emailSubjectRef.current}
                  onEmailSubjectChange={setEmailSubject}
                  emailBody={emailBodyRef.current}
                  onEmailBodyChange={setEmailBody}
                  onSearchUsers={handleSearchUsers}
                />
              );
            }
            if (stepId === 'followon-wa-create-event') {
              return (
                <CreateFollowOnEventStep
                  dataService={dataService}
                  onValidChange={handleFollowOnEventValid}
                  onFormValues={setFollowOnEventState}
                  initialValues={followOnEventStateRef.current}
                />
              );
            }
            return null;
          },
        };

        shellRef.current?.addDynamicStep(dynamicConfig, WA_FOLLOW_ON_CANONICAL_ORDER);
      }
    });

    prev.forEach((actionId) => {
      if (!next.includes(actionId)) {
        shellRef.current?.removeDynamicStep(WA_FOLLOW_ON_STEP_ID_MAP[actionId]);
      }
    });

    prevSelectedActionsRef.current = [...next];
  }, [selectedActions, dataService, authenticatedFetch, bffBaseUrl, containerId, handleFollowOnEventValid, handleSearchUsers]);

  // -- Pre-fill email when entering Send Email step --------------------------
  React.useEffect(() => {
    if (selectedActions.includes('send-email') && emailSubject === '' && formStateRef.current.name) {
      const f = formStateRef.current;
      setEmailSubject(`Work Assignment: ${f.name}`);
      const lines = [`A new work assignment has been created:`, '', `Name: ${f.name}`];
      if (f.description) lines.push(`Description: ${f.description}`);
      if (f.matterTypeName) lines.push(`Matter Type: ${f.matterTypeName}`);
      if (f.practiceAreaName) lines.push(`Practice Area: ${f.practiceAreaName}`);
      if (f.responseDueDate) lines.push(`Response Due Date: ${f.responseDueDate}`);
      lines.push(`Priority: ${getPriorityLabel(f.priority)}`);
      setEmailBody(lines.join('\n'));

      // Auto-CC the current user
      if (!emailCc) {
        resolveCurrentUserEmail(dataService).then((email) => {
          if (email) setEmailCc(email);
        }).catch(() => {});
      }
    }
  }, [selectedActions, emailSubject, emailCc, dataService]);

  // -- Build base steps ------------------------------------------------------
  const steps = React.useMemo<IWizardStepConfig[]>(
    () => [
      {
        id: 'select-work',
        label: 'Work to Assign',
        canAdvance: () => selectWorkValidRef.current,
        isSkippable: true,
        renderContent: () => (
          <SelectWorkStep
            onValidChange={handleSelectWorkValid}
            onFormValues={handleSelectWorkValues}
            navigationService={navigationService}
          />
        ),
      },
      {
        id: 'add-files',
        label: 'Add Files',
        canAdvance: () => true,
        isSkippable: true,
        renderContent: () => (
          <AddFilesStep
            onUploadedFilesChange={handleUploadedFilesChange}
          />
        ),
      },
      {
        id: 'enter-info',
        label: 'Enter Info',
        canAdvance: () => enterInfoValidRef.current,
        renderContent: () => {
          if (isPrefillRef.current) {
            return (
              <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: tokens.spacingVerticalL, padding: tokens.spacingVerticalXXL }}>
                <Spinner size="medium" label="Loading record details..." />
              </div>
            );
          }
          return (
            <EnterInfoStep
              dataService={dataService}
              onValidChange={handleEnterInfoValid}
              onFormValues={handleEnterInfoValues}
              initialValues={prefillRef.current}
              uploadedFiles={uploadedFilesRef.current}
              hasInitialValues={!!prefillRef.current}
              authenticatedFetch={authenticatedFetch}
              bffBaseUrl={bffBaseUrl}
            />
          );
        },
      },
      {
        id: 'next-steps',
        label: 'Next Steps',
        canAdvance: () => true,
        isEarlyFinish: () => selectedActionsRef.current.length === 0,
        renderContent: () => (
          <NextStepsSelectionStep
            selectedActions={selectedActions}
            onSelectedActionsChange={setSelectedActions}
          />
        ),
      },
    ],
    [dataService, authenticatedFetch, bffBaseUrl, containerId, selectedActions, handleSelectWorkValid, handleSelectWorkValues, handleUploadedFilesChange, handleEnterInfoValid, handleEnterInfoValues]
  );

  // -- Finish handler --------------------------------------------------------
  const handleFinish = React.useCallback(async (): Promise<IWizardSuccessConfig | void> => {
    const form = formStateRef.current;
    const actions = selectedActionsRef.current;
    const files = uploadedFilesRef.current;
    const service = serviceRef.current!;

    // 1. Create work assignment record (includes file upload + document records)
    const result = await service.createWorkAssignment(
      form,
      [], // No linked doc IDs -- docs are on the associated record
      files,
      actions.includes('assign-work') ? assignWorkStateRef.current : undefined
    );

    if (result.status === 'error') {
      return {
        icon: <CheckmarkCircleFilled fontSize={64} style={{ color: tokens.colorPaletteRedForeground1 }} />,
        title: 'Error',
        body: (
          <Text size={300} style={{ color: tokens.colorNeutralForeground2 }}>
            {result.errorMessage ?? 'An unknown error occurred.'}
          </Text>
        ),
        actions: (
          <Button appearance="primary" onClick={onClose}>
            Close
          </Button>
        ),
        warnings: result.warnings,
      };
    }

    const waId = result.workAssignmentId!;
    const warnings = [...result.warnings];

    // 2. Execute follow-on: Send Email
    if (actions.includes('send-email')) {
      const emailResult = await service.sendEmail(
        waId,
        form.name,
        emailToRef.current,
        emailSubjectRef.current,
        emailBodyRef.current,
        emailCcRef.current
      );
      if (!emailResult.success && emailResult.warning) {
        warnings.push(emailResult.warning);
      }
    }

    // 3. Execute follow-on: Create Event
    if (actions.includes('create-event')) {
      const eventResult = await service.createFollowOnEvent(
        waId,
        followOnEventStateRef.current
      );
      if (!eventResult.success && eventResult.warning) {
        warnings.push(eventResult.warning);
      }
    }

    const hasWarnings = warnings.length > 0;

    const viewRecord = () => {
      if (navigationService) {
        navigationService.openRecord('sprk_workassignment', waId);
      }
      onClose();
    };

    return {
      icon: <CheckmarkCircleFilled fontSize={64} style={{ color: tokens.colorPaletteGreenForeground1 }} />,
      title: hasWarnings ? 'Work assignment created with warnings' : 'Work assignment created!',
      body: (
        <Text size={300} style={{ color: tokens.colorNeutralForeground2 }}>
          <span style={{ color: tokens.colorBrandForeground1, fontWeight: 600 }}>&ldquo;{form.name}&rdquo;</span>{' '}
          has been created{hasWarnings ? ', though some follow-on actions could not complete. See details below.' : ' and is ready to use.'}
        </Text>
      ),
      actions: (
        <>
          <Button appearance="primary" onClick={viewRecord} aria-label={`View record: ${form.name}`}>View Record</Button>
          <Button appearance="secondary" onClick={onClose}>Close</Button>
        </>
      ),
      warnings: hasWarnings ? warnings : undefined,
    };
  }, [onClose, navigationService]);

  // -- Determine finishing label based on whether files exist -----------------
  const hasFiles = uploadedFiles.length > 0;
  const finishingLabel = hasFiles ? 'Processing...' : 'Creating...';

  // -- Render ----------------------------------------------------------------
  return (
    <WizardShell
      ref={shellRef}
      open={open}
      title="Create Work Assignment"
      steps={steps}
      onClose={onClose}
      onFinish={handleFinish}
      finishLabel="Create"
      finishingLabel={finishingLabel}
      embedded={embedded}
      hideTitle={embedded}
    />
  );
};

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

async function resolveCurrentUserEmail(dataService: IDataService): Promise<string | null> {
  try {
    // Get current user ID from Xrm
    const frames: Window[] = [window];
    try { if (window.parent !== window) frames.push(window.parent); } catch { /* cross-origin */ }
    try { if (window.top && window.top !== window) frames.push(window.top); } catch { /* cross-origin */ }

    let userId = '';
    for (const frame of frames) {
      try {
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        const xrm = (frame as any).Xrm;
        if (xrm?.Utility?.getGlobalContext) {
          const ctx = xrm.Utility.getGlobalContext();
          userId = ctx.userSettings?.userId?.replace(/[{}]/g, '').toLowerCase() ?? '';
          if (userId) break;
        }
      } catch { /* cross-origin */ }
    }
    if (!userId) return null;

    const result = await dataService.retrieveRecord(
      'systemuser', userId,
      '?$select=internalemailaddress'
    );
    return (result['internalemailaddress'] as string) || null;
  } catch {
    return null;
  }
}

function getPriorityLabel(priority: number): string {
  switch (priority) {
    case 100000000: return 'Low';
    case 100000001: return 'Normal';
    case 100000002: return 'High';
    case 100000003: return 'Urgent';
    default: return 'Normal';
  }
}

export default WorkAssignmentWizardDialog;
