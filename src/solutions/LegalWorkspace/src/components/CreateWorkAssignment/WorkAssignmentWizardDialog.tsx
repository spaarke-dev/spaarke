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
 *   [1] Add Files (AddFilesStep) — skippable
 *   [2] Enter Info (EnterInfoStep) — pre-filled from record or AI
 *   [3] Next Steps (NextStepsSelectionStep) — early finish if 0 selected
 *   [4+] Dynamic: Assign Work, Send Email, Create Event
 *
 * Processing:
 *   When files are uploaded, the finish handler uploads to SPE, creates
 *   document records, and triggers AI analysis (same as Create Matter).
 *   The finishing label changes to "Processing..." when files exist.
 */
import * as React from 'react';
import { Button, Text, Spinner, tokens } from '@fluentui/react-components';
import { CheckmarkCircleFilled } from '@fluentui/react-icons';

import { WizardShell } from '../../../../../client/shared/Spaarke.UI.Components/src/components/Wizard/WizardShell';
import type {
  IWizardShellHandle,
  IWizardStepConfig,
  IWizardSuccessConfig,
} from '../../../../../client/shared/Spaarke.UI.Components/src/components/Wizard/wizardShellTypes';
import { SendEmailStep } from '../../../../../client/shared/Spaarke.UI.Components/src/components/CreateRecordWizard/steps/SendEmailStep';
import { searchUsersAsLookup } from '../CreateMatter/matterService';

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
import type { IUploadedFile } from '../CreateMatter/wizardTypes';
import type { IWebApi } from '../../types/xrm';
import { getSpeContainerIdFromBusinessUnit } from '../../services/xrmProvider';
import { navigateToEntity } from '../../utils/navigation';

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface IWorkAssignmentWizardDialogProps {
  open: boolean;
  onClose: () => void;
  webApi: IWebApi;
  containerId?: string;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

const WorkAssignmentWizardDialog: React.FC<IWorkAssignmentWizardDialogProps> = ({
  open,
  onClose,
  webApi,
  containerId,
}) => {
  const shellRef = React.useRef<IWizardShellHandle>(null);

  // ── Form state ────────────────────────────────────────────────────────
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

  // ── canAdvance tracking via refs ──────────────────────────────────────
  const selectWorkValidRef = React.useRef(false);
  const enterInfoValidRef = React.useRef(false);
  const followOnEventValidRef = React.useRef(true);

  // ── Refs for stale closure prevention ─────────────────────────────────
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

  // ── Service (re-created when containerId resolves) ──────────────────
  const [resolvedContainerId, setResolvedContainerId] = React.useState(containerId ?? '');
  const serviceRef = React.useRef<WorkAssignmentService | null>(null);
  if (!serviceRef.current) {
    serviceRef.current = new WorkAssignmentService(webApi, resolvedContainerId || containerId);
  }

  // ── Resolve container ID from business unit on open ───────────────────
  React.useEffect(() => {
    if (!open) return;
    if (containerId) {
      setResolvedContainerId(containerId);
      serviceRef.current = new WorkAssignmentService(webApi, containerId);
      return;
    }
    let cancelled = false;
    getSpeContainerIdFromBusinessUnit(webApi).then((cid) => {
      if (!cancelled && cid) {
        setResolvedContainerId(cid);
        serviceRef.current = new WorkAssignmentService(webApi, cid);
      }
    }).catch((err) => {
      console.warn('[WorkAssignmentWizard] Container ID resolution failed:', err);
    });
    return () => { cancelled = true; };
  }, [open, webApi, containerId]);

  // ── Reset on open ─────────────────────────────────────────────────────
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

  // ── Step validity callbacks ───────────────────────────────────────────
  const handleSelectWorkValid = React.useCallback((valid: boolean) => {
    selectWorkValidRef.current = valid;
    shellRef.current?.requestUpdate();
  }, []);

  const handleSelectWorkValues = React.useCallback(
    (values: Pick<ICreateWorkAssignmentFormState, 'recordType' | 'recordId' | 'recordName' | 'assignWithoutRecord'>) => {
      setFormState((prev) => ({ ...prev, ...values }));
    },
    []
  );

  // ── Pre-fill from selected record ─────────────────────────────────────
  React.useEffect(() => {
    const { recordType, recordId, assignWithoutRecord } = formState;
    if (!recordId || !recordType || assignWithoutRecord) {
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
        // Non-fatal — user can still fill in manually
      } finally {
        if (!cancelled) setIsPrefilling(false);
      }
    })();

    return () => { cancelled = true; };
  }, [formState.recordId, formState.recordType, formState.assignWithoutRecord]);

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
    (query: string) => searchUsersAsLookup(webApi, query),
    [webApi]
  );

  // ── Dynamic step injection ────────────────────────────────────────────
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
                  webApi={webApi}
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
                  webApi={webApi}
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
  }, [selectedActions, webApi, containerId, handleFollowOnEventValid, handleSearchUsers]);

  // ── Pre-fill email when entering Send Email step ──────────────────────
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
        resolveCurrentUserEmail(webApi).then((email) => {
          if (email) setEmailCc(email);
        }).catch(() => {});
      }
    }
  }, [selectedActions, emailSubject, emailCc, webApi]);

  // ── Build base steps ──────────────────────────────────────────────────
  const steps = React.useMemo<IWizardStepConfig[]>(
    () => [
      {
        id: 'select-work',
        label: 'Work to Assign',
        canAdvance: () => selectWorkValidRef.current,
        renderContent: () => (
          <SelectWorkStep
            onValidChange={handleSelectWorkValid}
            onFormValues={handleSelectWorkValues}
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
              webApi={webApi}
              onValidChange={handleEnterInfoValid}
              onFormValues={handleEnterInfoValues}
              initialValues={prefillRef.current}
              uploadedFiles={uploadedFilesRef.current}
              hasInitialValues={!!prefillRef.current}
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
    [webApi, containerId, selectedActions, handleSelectWorkValid, handleSelectWorkValues, handleUploadedFilesChange, handleEnterInfoValid, handleEnterInfoValues]
  );

  // ── Finish handler ────────────────────────────────────────────────────
  const handleFinish = React.useCallback(async (): Promise<IWizardSuccessConfig | void> => {
    const form = formStateRef.current;
    const actions = selectedActionsRef.current;
    const files = uploadedFilesRef.current;
    const service = serviceRef.current!;

    // 1. Create work assignment record (includes file upload + document records)
    const result = await service.createWorkAssignment(
      form,
      [], // No linked doc IDs — docs are on the associated record
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
      navigateToEntity({ action: 'openRecord', entityName: 'sprk_workassignment', entityId: waId });
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
  }, [onClose]);

  // ── Determine finishing label based on whether files exist ─────────────
  const hasFiles = uploadedFiles.length > 0;
  const finishingLabel = hasFiles ? 'Processing...' : 'Creating...';

  // ── Render ────────────────────────────────────────────────────────────
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
    />
  );
};

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

async function resolveCurrentUserEmail(webApi: IWebApi): Promise<string | null> {
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

    const result = await webApi.retrieveRecord(
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
