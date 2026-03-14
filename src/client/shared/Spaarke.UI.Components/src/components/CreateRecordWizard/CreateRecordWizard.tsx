/**
 * CreateRecordWizard.tsx
 * Reusable multi-step wizard for creating Dataverse records.
 *
 * Extracts ~265 LOC of duplicated boilerplate from entity-specific wizards:
 *   - File upload reducer + state
 *   - SPE container resolution
 *   - Reset-on-open
 *   - Follow-on step sync (dynamic add/remove via shellRef)
 *   - Assign Resources / Draft Summary / Send Email step rendering
 *   - Email pre-fill
 *   - Ref-based stale closure prevention
 *
 * Each entity provides only:
 *   - config.infoStep: entity-specific form (canAdvance + renderContent)
 *   - config.onFinish: record creation + success screen
 *   - Search callbacks (contacts, organizations, users)
 *
 * Steps:
 *   [0] Add file(s) — always skip-able (canAdvance: true)
 *   [1] Entity info  — from config.infoStep
 *   [2] Next Steps   — follow-on action card selection
 *   [3+] Dynamic     — Assign Resources, Draft Summary, Send Email
 *
 * @see WizardShell — underlying generic dialog shell
 * @see ADR-012 — Shared Component Library
 */
import * as React from 'react';
import { MessageBar, MessageBarBody, Text, makeStyles, tokens } from '@fluentui/react-components';

import { WizardShell } from '../Wizard/WizardShell';
import type { IWizardShellHandle, IWizardStepConfig, IWizardSuccessConfig } from '../Wizard/wizardShellTypes';

import { FileUploadZone } from '../FileUpload/FileUploadZone';
import { UploadedFileList } from '../FileUpload/UploadedFileList';
import type { IUploadedFile, IFileValidationError } from '../FileUpload/fileUploadTypes';
import type { ILookupItem } from '../../types/LookupTypes';

import type { ICreateRecordWizardProps, FollowOnActionId, IRecipientItem } from './types';

import {
  NextStepsStep,
  FOLLOW_ON_STEP_ID_MAP,
  FOLLOW_ON_STEP_LABEL_MAP,
  FOLLOW_ON_CANONICAL_ORDER,
} from './FollowOnSteps';

import { AssignResourcesStep } from './steps/AssignResourcesStep';
import { DraftSummaryStep } from './steps/DraftSummaryStep';
import { SendEmailStep } from './steps/SendEmailStep';

// ---------------------------------------------------------------------------
// File reducer
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
    color: tokens.colorNeutralForeground1,
    marginBottom: tokens.spacingVerticalXS,
  },
  stepSubtitle: {
    color: tokens.colorNeutralForeground3,
    marginBottom: tokens.spacingVerticalM,
  },
  errorBar: {
    flexShrink: 0,
  },
});

// ---------------------------------------------------------------------------
// Empty search callback (for when entity doesn't provide search)
// ---------------------------------------------------------------------------

const EMPTY_SEARCH = () => Promise.resolve([] as ILookupItem[]);

// ---------------------------------------------------------------------------
// CreateRecordWizard
// ---------------------------------------------------------------------------

export const CreateRecordWizard: React.FC<ICreateRecordWizardProps> = ({ open, onClose, config }) => {
  const styles = useStyles();
  const shellRef = React.useRef<IWizardShellHandle>(null);

  // ── File state ──────────────────────────────────────────────────────────
  const [fileState, fileDispatch] = React.useReducer(fileReducer, {
    uploadedFiles: [],
    validationErrors: [],
  });

  // ── Follow-on step selections ───────────────────────────────────────────
  const [selectedActions, setSelectedActions] = React.useState<FollowOnActionId[]>([]);

  // ── Assign Resources state ──────────────────────────────────────────────
  const [assignedAttorneyId, setAssignedAttorneyId] = React.useState('');
  const [assignedAttorneyName, setAssignedAttorneyName] = React.useState('');
  const [assignedParalegalId, setAssignedParalegalId] = React.useState('');
  const [assignedParalegalName, setAssignedParalegalName] = React.useState('');
  const [assignedOutsideCounselId, setAssignedOutsideCounselId] = React.useState('');
  const [assignedOutsideCounselName, setAssignedOutsideCounselName] = React.useState('');
  const [notifyResources, setNotifyResources] = React.useState(false);

  // ── Draft Summary state ─────────────────────────────────────────────────
  const [summaryText, setSummaryText] = React.useState('');
  const [recipients, setRecipients] = React.useState<IRecipientItem[]>([]);
  const [ccRecipients, setCcRecipients] = React.useState<IRecipientItem[]>([]);

  // ── Send Email state ────────────────────────────────────────────────────
  const [emailTo, setEmailTo] = React.useState('');
  const [emailSubject, setEmailSubject] = React.useState('');
  const [emailBody, setEmailBody] = React.useState('');

  // ── SPE container ID ────────────────────────────────────────────────────
  const [speContainerId, setSpeContainerId] = React.useState('');

  React.useEffect(() => {
    if (open && config.resolveSpeContainerId) {
      config.resolveSpeContainerId().then(id => setSpeContainerId(id));
    }
  }, [open, config]);

  // ── Reset all state on open ─────────────────────────────────────────────
  React.useEffect(() => {
    if (open) {
      fileDispatch({ type: 'RESET' });
      setSelectedActions([]);
      setAssignedAttorneyId('');
      setAssignedAttorneyName('');
      setAssignedParalegalId('');
      setAssignedParalegalName('');
      setAssignedOutsideCounselId('');
      setAssignedOutsideCounselName('');
      setNotifyResources(false);
      setSummaryText('');
      setRecipients([]);
      setCcRecipients([]);
      setEmailTo('');
      setEmailSubject('');
      setEmailBody('');
    }
  }, [open]);

  // ── Refs for stale closure prevention in dynamic step renderContent ─────
  const notifyResourcesRef = React.useRef(notifyResources);
  notifyResourcesRef.current = notifyResources;
  const summaryTextRef = React.useRef(summaryText);
  summaryTextRef.current = summaryText;
  const recipientsRef = React.useRef(recipients);
  recipientsRef.current = recipients;
  const ccRecipientsRef = React.useRef(ccRecipients);
  ccRecipientsRef.current = ccRecipients;
  const emailToRef = React.useRef(emailTo);
  emailToRef.current = emailTo;
  const emailSubjectRef = React.useRef(emailSubject);
  emailSubjectRef.current = emailSubject;
  const emailBodyRef = React.useRef(emailBody);
  emailBodyRef.current = emailBody;
  const selectedActionsRef = React.useRef(selectedActions);
  selectedActionsRef.current = selectedActions;
  const fileStateRef = React.useRef(fileState);
  fileStateRef.current = fileState;
  const speContainerIdRef = React.useRef(speContainerId);
  speContainerIdRef.current = speContainerId;

  // Assign resources refs
  const assignedAttorneyIdRef = React.useRef(assignedAttorneyId);
  assignedAttorneyIdRef.current = assignedAttorneyId;
  const assignedAttorneyNameRef = React.useRef(assignedAttorneyName);
  assignedAttorneyNameRef.current = assignedAttorneyName;
  const assignedParalegalIdRef = React.useRef(assignedParalegalId);
  assignedParalegalIdRef.current = assignedParalegalId;
  const assignedParalegalNameRef = React.useRef(assignedParalegalName);
  assignedParalegalNameRef.current = assignedParalegalName;
  const assignedOutsideCounselIdRef = React.useRef(assignedOutsideCounselId);
  assignedOutsideCounselIdRef.current = assignedOutsideCounselId;
  const assignedOutsideCounselNameRef = React.useRef(assignedOutsideCounselName);
  assignedOutsideCounselNameRef.current = assignedOutsideCounselName;

  // ── Search callbacks (from config, with fallbacks) ──────────────────────
  const searchContacts = config.searchContacts ?? EMPTY_SEARCH;
  const searchOrganizations = config.searchOrganizations ?? EMPTY_SEARCH;
  const searchUsers = config.searchUsers ?? EMPTY_SEARCH;

  // ── Assign Resources change handlers ────────────────────────────────────
  const handleAttorneyChange = React.useCallback((item: ILookupItem | null) => {
    setAssignedAttorneyId(item?.id ?? '');
    setAssignedAttorneyName(item?.name ?? '');
  }, []);
  const handleParalegalChange = React.useCallback((item: ILookupItem | null) => {
    setAssignedParalegalId(item?.id ?? '');
    setAssignedParalegalName(item?.name ?? '');
  }, []);
  const handleOutsideCounselChange = React.useCallback((item: ILookupItem | null) => {
    setAssignedOutsideCounselId(item?.id ?? '');
    setAssignedOutsideCounselName(item?.name ?? '');
  }, []);

  // ── Sync dynamic steps with selected action cards ───────────────────────
  const prevSelectedActionsRef = React.useRef<FollowOnActionId[]>([]);

  React.useEffect(() => {
    const prev = prevSelectedActionsRef.current;
    const next = selectedActions;

    next.forEach(actionId => {
      if (!prev.includes(actionId)) {
        const stepId = FOLLOW_ON_STEP_ID_MAP[actionId];
        const stepLabel = FOLLOW_ON_STEP_LABEL_MAP[actionId];

        const dynamicConfig: IWizardStepConfig = {
          id: stepId,
          label: stepLabel,
          canAdvance: () => {
            if (stepId === 'followon-send-email') {
              return (
                emailToRef.current.trim() !== '' &&
                emailSubjectRef.current.trim() !== '' &&
                emailBodyRef.current.trim() !== ''
              );
            }
            return true;
          },
          renderContent: () => {
            if (stepId === 'followon-assign-counsel') {
              const attVal: ILookupItem | null = assignedAttorneyIdRef.current
                ? {
                    id: assignedAttorneyIdRef.current,
                    name: assignedAttorneyNameRef.current,
                  }
                : null;
              const paraVal: ILookupItem | null = assignedParalegalIdRef.current
                ? {
                    id: assignedParalegalIdRef.current,
                    name: assignedParalegalNameRef.current,
                  }
                : null;
              const ocVal: ILookupItem | null = assignedOutsideCounselIdRef.current
                ? {
                    id: assignedOutsideCounselIdRef.current,
                    name: assignedOutsideCounselNameRef.current,
                  }
                : null;

              return (
                <AssignResourcesStep
                  attorneyValue={attVal}
                  onAttorneyChange={handleAttorneyChange}
                  onSearchAttorneys={searchContacts}
                  paralegalValue={paraVal}
                  onParalegalChange={handleParalegalChange}
                  onSearchParalegals={searchContacts}
                  outsideCounselValue={ocVal}
                  onOutsideCounselChange={handleOutsideCounselChange}
                  onSearchOutsideCounsel={searchOrganizations}
                  notifyResources={notifyResourcesRef.current}
                  onNotifyChange={setNotifyResources}
                />
              );
            }
            if (stepId === 'followon-draft-summary') {
              return (
                <DraftSummaryStep
                  summaryText={summaryTextRef.current}
                  onSummaryChange={setSummaryText}
                  recipients={recipientsRef.current}
                  onRecipientsChange={setRecipients}
                  ccRecipients={ccRecipientsRef.current}
                  onCcRecipientsChange={setCcRecipients}
                  onSearchContacts={searchContacts}
                  fetchAiSummary={config.fetchAiSummary}
                />
              );
            }
            if (stepId === 'followon-send-email') {
              return (
                <SendEmailStep
                  emailTo={emailToRef.current}
                  onEmailToChange={setEmailTo}
                  emailSubject={emailSubjectRef.current}
                  onEmailSubjectChange={setEmailSubject}
                  emailBody={emailBodyRef.current}
                  onEmailBodyChange={setEmailBody}
                  onSearchUsers={searchUsers}
                />
              );
            }
            return <Text size={300}>{stepLabel}</Text>;
          },
        };

        shellRef.current?.addDynamicStep(dynamicConfig, FOLLOW_ON_CANONICAL_ORDER);
      }
    });

    prev.forEach(actionId => {
      if (!next.includes(actionId)) {
        shellRef.current?.removeDynamicStep(FOLLOW_ON_STEP_ID_MAP[actionId]);
      }
    });

    prevSelectedActionsRef.current = next;
  }, [
    selectedActions,
    searchContacts,
    searchOrganizations,
    searchUsers,
    handleAttorneyChange,
    handleParalegalChange,
    handleOutsideCounselChange,
    config,
  ]);

  // ── Email pre-fill when send-email is selected ──────────────────────────
  React.useEffect(() => {
    if (selectedActions.includes('send-email') && !emailSubject) {
      const entityName = config.getEntityName?.() ?? '';
      if (entityName) {
        const subject = config.buildEmailSubject ? config.buildEmailSubject(entityName) : `New Record: ${entityName}`;
        setEmailSubject(subject);

        const fields = config.getFormFields?.() ?? {};
        const body = config.buildEmailBody
          ? config.buildEmailBody(fields)
          : `Dear Client,\n\nA new record "${entityName}" has been created.\n\nKind regards`;
        setEmailBody(body);
      }
    }
  }, [selectedActions, emailSubject, config]);

  // ── File handler callbacks ──────────────────────────────────────────────
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

  // ── Finish handler ──────────────────────────────────────────────────────
  const handleFinish = React.useCallback(async (): Promise<IWizardSuccessConfig> => {
    return config.onFinish({
      uploadedFiles: fileStateRef.current.uploadedFiles,
      speContainerId: speContainerIdRef.current,
      selectedActions: selectedActionsRef.current,
      followOn: {
        assignedAttorneyId: assignedAttorneyIdRef.current,
        assignedAttorneyName: assignedAttorneyNameRef.current,
        assignedParalegalId: assignedParalegalIdRef.current,
        assignedParalegalName: assignedParalegalNameRef.current,
        assignedOutsideCounselId: assignedOutsideCounselIdRef.current,
        assignedOutsideCounselName: assignedOutsideCounselNameRef.current,
        notifyResources: notifyResourcesRef.current,
        summaryText: summaryTextRef.current,
        recipients: recipientsRef.current,
        ccRecipients: ccRecipientsRef.current,
        emailTo: emailToRef.current,
        emailSubject: emailSubjectRef.current,
        emailBody: emailBodyRef.current,
      },
    });
  }, [config]);

  // ── Step configurations ─────────────────────────────────────────────────
  const filesStepSubtitle = config.filesStepSubtitle ?? 'Upload documents for AI analysis, or click Next to skip.';

  const stepConfigs: IWizardStepConfig[] = React.useMemo(
    () => [
      {
        id: 'add-files',
        label: 'Add file(s)',
        canAdvance: () => true, // always skip-able
        isSkippable: true,
        renderContent: () => (
          <>
            <div>
              <Text as="h2" size={500} weight="semibold" className={styles.stepTitle}>
                Add file(s)
              </Text>
              <Text size={200} className={styles.stepSubtitle}>
                {filesStepSubtitle}
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
      {
        id: config.infoStep.id,
        label: config.infoStep.label,
        canAdvance: config.infoStep.canAdvance,
        renderContent: () => config.infoStep.renderContent(fileStateRef.current.uploadedFiles),
      },
      {
        id: 'next-steps',
        label: 'Next Steps',
        canAdvance: () => true,
        isEarlyFinish: () => selectedActions.length === 0,
        renderContent: () => (
          <NextStepsStep
            selectedActions={selectedActions}
            onSelectionChange={setSelectedActions}
            entityLabel={config.entityLabel}
          />
        ),
      },
    ],
    [
      fileState.uploadedFiles,
      fileState.validationErrors,
      selectedActions,
      config,
      styles,
      filesStepSubtitle,
      handleFilesAccepted,
      handleValidationErrors,
      handleRemoveFile,
      handleClearErrors,
    ]
  );

  // ── Render ──────────────────────────────────────────────────────────────
  return (
    <WizardShell
      ref={shellRef}
      open={open}
      title={config.title}
      ariaLabel={config.title}
      steps={stepConfigs}
      onClose={onClose}
      onFinish={handleFinish}
      finishingLabel={config.finishingLabel ?? 'Creating\u2026'}
      finishLabel="Finish"
    />
  );
};

export default CreateRecordWizard;
