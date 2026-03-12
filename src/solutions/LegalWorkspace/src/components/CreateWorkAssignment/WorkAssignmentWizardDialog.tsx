/**
 * WorkAssignmentWizardDialog.tsx
 * Orchestrator for the Work Assignment creation wizard.
 *
 * Uses WizardShell directly (not CreateRecordWizard) because the step
 * sequence differs: Step 1 is record selection, Step 2 is document sharing,
 * and follow-on steps have different fields.
 *
 * Steps:
 *   [0] Work to Assign (SelectWorkStep)
 *   [1] Share Documents (ShareDocumentsStep) — skippable
 *   [2] Enter Info (EnterInfoStep)
 *   [3] Next Steps (NextStepsSelectionStep) — early finish if 0 selected
 *   [4+] Dynamic: Assign Work, Send Email, Create Event
 */
import * as React from 'react';
import { Button, Text, tokens } from '@fluentui/react-components';
import { CheckmarkCircleRegular } from '@fluentui/react-icons';

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
import { ShareDocumentsStep } from './ShareDocumentsStep';
import { EnterInfoStep } from './EnterInfoStep';
import { NextStepsSelectionStep } from './NextStepsSelectionStep';
import { AssignWorkStep } from './AssignWorkStep';
import { CreateFollowOnEventStep } from './CreateFollowOnEventStep';
import type { IUploadedFile } from '../CreateMatter/wizardTypes';
import type { IWebApi } from '../../types/xrm';

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
  const [linkedDocIds, setLinkedDocIds] = React.useState<string[]>([]);
  const [uploadedFiles, setUploadedFiles] = React.useState<IUploadedFile[]>([]);
  const [selectedActions, setSelectedActions] = React.useState<WorkAssignmentFollowOnId[]>([]);
  const [assignWorkState, setAssignWorkState] = React.useState<IAssignWorkState>(EMPTY_ASSIGN_WORK_STATE);
  const [followOnEventState, setFollowOnEventState] = React.useState<ICreateFollowOnEventState>(EMPTY_FOLLOW_ON_EVENT_STATE);
  const [emailTo, setEmailTo] = React.useState('');
  const [emailSubject, setEmailSubject] = React.useState('');
  const [emailBody, setEmailBody] = React.useState('');

  // ── canAdvance tracking via refs ──────────────────────────────────────
  const selectWorkValidRef = React.useRef(false);
  const enterInfoValidRef = React.useRef(false);
  const followOnEventValidRef = React.useRef(true);

  // ── Refs for stale closure prevention ─────────────────────────────────
  const formStateRef = React.useRef(formState);
  formStateRef.current = formState;
  const linkedDocIdsRef = React.useRef(linkedDocIds);
  linkedDocIdsRef.current = linkedDocIds;
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

  // ── Service ───────────────────────────────────────────────────────────
  const serviceRef = React.useRef<WorkAssignmentService | null>(null);
  if (!serviceRef.current) {
    serviceRef.current = new WorkAssignmentService(webApi, containerId);
  }

  // ── Reset on open ─────────────────────────────────────────────────────
  React.useEffect(() => {
    if (open) {
      setFormState(EMPTY_WORK_ASSIGNMENT_FORM);
      setLinkedDocIds([]);
      setUploadedFiles([]);
      setSelectedActions([]);
      setAssignWorkState(EMPTY_ASSIGN_WORK_STATE);
      setFollowOnEventState(EMPTY_FOLLOW_ON_EVENT_STATE);
      setEmailTo('');
      setEmailSubject('');
      setEmailBody('');
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

  const handleLinkedDocsChange = React.useCallback((ids: string[]) => setLinkedDocIds(ids), []);
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
                  emailTo={emailToRef.current}
                  onEmailToChange={setEmailTo}
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
      setEmailBody(
        `<p>A new work assignment has been created:</p>` +
        `<p><strong>Name:</strong> ${f.name}</p>` +
        (f.description ? `<p><strong>Description:</strong> ${f.description}</p>` : '') +
        (f.matterTypeName ? `<p><strong>Matter Type:</strong> ${f.matterTypeName}</p>` : '') +
        (f.practiceAreaName ? `<p><strong>Practice Area:</strong> ${f.practiceAreaName}</p>` : '') +
        (f.responseDueDate ? `<p><strong>Response Due Date:</strong> ${f.responseDueDate}</p>` : '') +
        `<p><strong>Priority:</strong> ${getPriorityLabel(f.priority)}</p>`
      );
    }
  }, [selectedActions, emailSubject]);

  // ── Build base steps ──────────────────────────────────────────────────
  const steps = React.useMemo<IWizardStepConfig[]>(
    () => [
      {
        id: 'select-work',
        label: 'Work to Assign',
        canAdvance: () => selectWorkValidRef.current,
        renderContent: () => (
          <SelectWorkStep
            webApi={webApi}
            containerId={containerId}
            onValidChange={handleSelectWorkValid}
            onFormValues={handleSelectWorkValues}
          />
        ),
      },
      {
        id: 'share-documents',
        label: 'Share Documents',
        canAdvance: () => true,
        isSkippable: true,
        renderContent: () => (
          <ShareDocumentsStep
            webApi={webApi}
            containerId={containerId}
            onLinkedDocsChange={handleLinkedDocsChange}
            onUploadedFilesChange={handleUploadedFilesChange}
          />
        ),
      },
      {
        id: 'enter-info',
        label: 'Enter Info',
        canAdvance: () => enterInfoValidRef.current,
        renderContent: () => (
          <EnterInfoStep
            webApi={webApi}
            onValidChange={handleEnterInfoValid}
            onFormValues={handleEnterInfoValues}
          />
        ),
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
    [webApi, containerId, selectedActions, handleSelectWorkValid, handleSelectWorkValues, handleLinkedDocsChange, handleUploadedFilesChange, handleEnterInfoValid, handleEnterInfoValues]
  );

  // ── Finish handler ────────────────────────────────────────────────────
  const handleFinish = React.useCallback(async (): Promise<IWizardSuccessConfig | void> => {
    const form = formStateRef.current;
    const actions = selectedActionsRef.current;
    const service = serviceRef.current!;

    // 1. Create work assignment record
    const result = await service.createWorkAssignment(
      form,
      linkedDocIdsRef.current,
      uploadedFilesRef.current,
      actions.includes('assign-work') ? assignWorkStateRef.current : undefined
    );

    if (result.status === 'error') {
      return {
        icon: null,
        title: 'Error',
        body: result.errorMessage ?? 'An unknown error occurred.',
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
        emailBodyRef.current
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

    return {
      icon: <CheckmarkCircleRegular style={{ fontSize: 48, color: tokens.colorPaletteGreenForeground1 }} />,
      title: 'Work Assignment Created',
      body: (
        <Text>
          <strong>{form.name}</strong> has been created successfully.
        </Text>
      ),
      actions: (
        <Button appearance="primary" onClick={onClose}>
          Done
        </Button>
      ),
      warnings: warnings.length > 0 ? warnings : undefined,
    };
  }, [onClose]);

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
      finishingLabel="Creating..."
    />
  );
};

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

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
