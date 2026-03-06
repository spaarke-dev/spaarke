/**
 * WizardDialog.tsx
 * Multi-step wizard dialog for "Create New Matter".
 *
 * Refactored to delegate all shell concerns (Dialog, navigation state,
 * sidebar stepper, footer buttons, finish flow, success screen) to the
 * generic WizardShell component. This file retains only:
 *   - Domain state (files, form values, follow-on selections, email fields)
 *   - Step configuration (canAdvance, isEarlyFinish, renderContent)
 *   - Dynamic step sync (via shellRef imperative handle)
 *   - handleFinish: calls MatterService and returns IWizardSuccessConfig
 *
 * Steps:
 *   0 — Add file(s)       (FileUploadZone + UploadedFileList)
 *   1 — Enter Info         (CreateRecordStep)
 *   2 — Next Steps         (NextStepsStep — checkbox card selection)
 *   3+ — Follow-on steps   (AssignResourcesStep, DraftSummaryStep, SendEmailStep)
 *        Injected dynamically based on card selections in Step 2.
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

import type { IWizardDialogProps, IUploadedFile, IFileValidationError } from './wizardTypes';
import { FileUploadZone } from './FileUploadZone';
import { UploadedFileList } from './UploadedFileList';
import { CreateRecordStep } from './CreateRecordStep';
import {
  NextStepsStep,
  FollowOnActionId,
  FOLLOW_ON_STEP_ID_MAP,
  FOLLOW_ON_STEP_LABEL_MAP,
} from './NextStepsStep';
import { AssignResourcesStep } from './AssignResourcesStep';
import { DraftSummaryStep } from './DraftSummaryStep';
import type { IRecipientItem } from './RecipientField';
import {
  SendEmailStep,
  buildDefaultEmailSubject,
  buildDefaultEmailBody,
} from './SendEmailStep';
import {
  MatterService,
  IFollowOnActions,
  searchContactsAsLookup,
  searchOrganizationsAsLookup,
  searchUsersAsLookup,
} from './matterService';
import type { ICreateMatterFormState } from './formTypes';
import type { ILookupItem } from '../../types/entities';
import type { IWebApi } from '../../types/xrm';
import { getSpeContainerIdFromBusinessUnit } from '../../services/xrmProvider';
import { navigateToEntity } from '../../utils/navigation';

// ---------------------------------------------------------------------------
// Extended props interface (exported — used by App to pass webApi)
// ---------------------------------------------------------------------------

/**
 * Extended props accepted by WizardDialog when the parent App passes webApi.
 * This extends the public IWizardDialogProps without breaking the existing
 * interface contract.
 */
export interface IWizardDialogPropsInternal extends IWizardDialogProps {
  /** PCF WebApi for Dataverse operations (MatterService, contact search). */
  webApi?: IWebApi;
}

// ---------------------------------------------------------------------------
// Domain file reducer (file uploads only — navigation is in WizardShell)
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
        state.uploadedFiles.map((f) => `${f.name}::${f.sizeBytes}`)
      );
      const newFiles = action.files.filter(
        (f) => !existing.has(`${f.name}::${f.sizeBytes}`)
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
// Styles (domain step content only — shell styles are in WizardShell)
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
// Empty form state helper
// ---------------------------------------------------------------------------

const EMPTY_FORM_STATE: ICreateMatterFormState = {
  matterTypeId: '',
  matterTypeName: '',
  practiceAreaId: '',
  practiceAreaName: '',
  matterName: '',
  assignedAttorneyId: '',
  assignedAttorneyName: '',
  assignedParalegalId: '',
  assignedParalegalName: '',
  assignedOutsideCounselId: '',
  assignedOutsideCounselName: '',
  summary: '',
};

// ---------------------------------------------------------------------------
// Canonical order for dynamic follow-on steps
// ---------------------------------------------------------------------------

const FOLLOW_ON_CANONICAL_ORDER = [
  'followon-assign-counsel',
  'followon-draft-summary',
  'followon-send-email',
];

// ---------------------------------------------------------------------------
// WizardDialog (exported)
// ---------------------------------------------------------------------------

export const WizardDialog: React.FC<IWizardDialogPropsInternal> = ({
  open,
  onClose,
  webApi,
}) => {
  const styles = useStyles();
  const shellRef = React.useRef<IWizardShellHandle>(null);

  // ── Domain file state ───────────────────────────────────────────────────
  const [fileState, fileDispatch] = React.useReducer(fileReducer, {
    uploadedFiles: [],
    validationErrors: [],
  });

  // ── Step 2 state ────────────────────────────────────────────────────────
  const [step2Valid, setStep2Valid] = React.useState(false);
  const [step2FormValues, setStep2FormValues] = React.useState<ICreateMatterFormState>(EMPTY_FORM_STATE);

  // ── Step 3: Next Steps selection ────────────────────────────────────────
  const [selectedActions, setSelectedActions] = React.useState<FollowOnActionId[]>([]);

  // ── Assign Resources state (notify toggle — UI only, not wired) ────────
  const [notifyResources, setNotifyResources] = React.useState(false);

  // ── Draft Summary state ─────────────────────────────────────────────────
  const [summaryText, setSummaryText] = React.useState('');
  const [recipients, setRecipients] = React.useState<IRecipientItem[]>([]);
  const [ccRecipients, setCcRecipients] = React.useState<IRecipientItem[]>([]);

  // ── Send Email state ────────────────────────────────────────────────────
  const [emailTo, setEmailTo] = React.useState('');
  const [emailSubject, setEmailSubject] = React.useState('');
  const [emailBody, setEmailBody] = React.useState('');

  // ── SPE container ID (resolved from user's Business Unit) ───────────────
  const [speContainerId, setSpeContainerId] = React.useState('');

  React.useEffect(() => {
    if (open && webApi) {
      getSpeContainerIdFromBusinessUnit(webApi).then((id) => {
        setSpeContainerId(id);
      });
    }
  }, [open, webApi]);

  // ── Reset domain state on open ──────────────────────────────────────────
  React.useEffect(() => {
    if (open) {
      fileDispatch({ type: 'RESET' });
      setStep2Valid(false);
      setStep2FormValues(EMPTY_FORM_STATE);
      setSelectedActions([]);
      setNotifyResources(false);
      setSummaryText('');
      setRecipients([]);
      setCcRecipients([]);
      setEmailTo('');
      setEmailSubject('');
      setEmailBody('');
    }
  }, [open]);

  // ── Refs for dynamic step closures (prevents stale closure bug) ─────────
  const step2FormValuesRef = React.useRef(step2FormValues);
  step2FormValuesRef.current = step2FormValues;
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

  // ── Stable search callbacks ─────────────────────────────────────────────
  const handleSearchAttorneys = React.useCallback(
    (query: string) => webApi ? searchContactsAsLookup(webApi, query) : Promise.resolve([]),
    [webApi]
  );
  const handleSearchParalegals = React.useCallback(
    (query: string) => webApi ? searchContactsAsLookup(webApi, query) : Promise.resolve([]),
    [webApi]
  );
  const handleSearchOutsideCounsel = React.useCallback(
    (query: string) => webApi ? searchOrganizationsAsLookup(webApi, query) : Promise.resolve([]),
    [webApi]
  );
  const handleSearchContacts = React.useCallback(
    (query: string) => webApi ? searchContactsAsLookup(webApi, query) : Promise.resolve([]),
    [webApi]
  );
  const handleSearchUsers = React.useCallback(
    (query: string) => webApi ? searchUsersAsLookup(webApi, query) : Promise.resolve([]),
    [webApi]
  );

  // ── Assign Resources change handlers ────────────────────────────────────
  const handleAttorneyChange = React.useCallback(
    (item: ILookupItem | null) => {
      setStep2FormValues((prev) => ({
        ...prev,
        assignedAttorneyId: item?.id ?? '',
        assignedAttorneyName: item?.name ?? '',
      }));
    },
    []
  );
  const handleParalegalChange = React.useCallback(
    (item: ILookupItem | null) => {
      setStep2FormValues((prev) => ({
        ...prev,
        assignedParalegalId: item?.id ?? '',
        assignedParalegalName: item?.name ?? '',
      }));
    },
    []
  );
  const handleOutsideCounselChange = React.useCallback(
    (item: ILookupItem | null) => {
      setStep2FormValues((prev) => ({
        ...prev,
        assignedOutsideCounselId: item?.id ?? '',
        assignedOutsideCounselName: item?.name ?? '',
      }));
    },
    []
  );

  // ── Sync dynamic steps with selected action cards (via shellRef) ────────
  const prevSelectedActionsRef = React.useRef<FollowOnActionId[]>([]);

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
              return emailToRef.current.trim() !== '' && emailSubjectRef.current.trim() !== '' && emailBodyRef.current.trim() !== '';
            }
            return true; // assign-resources and draft-summary have no hard requirement
          },
          renderContent: () => {
            if (stepId === 'followon-assign-counsel') {
              // Build lookup values from form state
              const fv = step2FormValuesRef.current;
              const attVal: ILookupItem | null = fv.assignedAttorneyId
                ? { id: fv.assignedAttorneyId, name: fv.assignedAttorneyName }
                : null;
              const paraVal: ILookupItem | null = fv.assignedParalegalId
                ? { id: fv.assignedParalegalId, name: fv.assignedParalegalName }
                : null;
              const ocVal: ILookupItem | null = fv.assignedOutsideCounselId
                ? { id: fv.assignedOutsideCounselId, name: fv.assignedOutsideCounselName }
                : null;

              return (
                <AssignResourcesStep
                  attorneyValue={attVal}
                  onAttorneyChange={handleAttorneyChange}
                  onSearchAttorneys={handleSearchAttorneys}
                  paralegalValue={paraVal}
                  onParalegalChange={handleParalegalChange}
                  onSearchParalegals={handleSearchParalegals}
                  outsideCounselValue={ocVal}
                  onOutsideCounselChange={handleOutsideCounselChange}
                  onSearchOutsideCounsel={handleSearchOutsideCounsel}
                  notifyResources={notifyResourcesRef.current}
                  onNotifyChange={setNotifyResources}
                />
              );
            }
            if (stepId === 'followon-draft-summary') {
              return (
                <DraftSummaryStep
                  formValues={step2FormValuesRef.current}
                  summaryText={summaryTextRef.current}
                  onSummaryChange={setSummaryText}
                  recipients={recipientsRef.current}
                  onRecipientsChange={setRecipients}
                  ccRecipients={ccRecipientsRef.current}
                  onCcRecipientsChange={setCcRecipients}
                  onSearchContacts={handleSearchContacts}
                />
              );
            }
            if (stepId === 'followon-send-email') {
              return (
                <SendEmailStep
                  formValues={step2FormValuesRef.current}
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
  }, [
    selectedActions,
    webApi,
    handleSearchAttorneys,
    handleSearchParalegals,
    handleSearchOutsideCounsel,
    handleSearchContacts,
    handleSearchUsers,
    handleAttorneyChange,
    handleParalegalChange,
    handleOutsideCounselChange,
  ]);

  // ── Pre-fill email fields when send-email is selected ────────────────────
  React.useEffect(() => {
    if (
      selectedActions.includes('send-email') &&
      step2FormValues.matterName &&
      !emailSubject
    ) {
      setEmailSubject(buildDefaultEmailSubject(step2FormValues.matterName));
      setEmailBody(buildDefaultEmailBody(step2FormValues));
    }
  }, [selectedActions, step2FormValues, emailSubject]);

  // ── File handler callbacks ──────────────────────────────────────────────
  const handleFilesAccepted = React.useCallback(
    (files: IUploadedFile[]) => fileDispatch({ type: 'ADD_FILES', files }),
    []
  );

  const handleValidationErrors = React.useCallback(
    (errors: IFileValidationError[]) =>
      fileDispatch({ type: 'SET_VALIDATION_ERRORS', errors }),
    []
  );

  const handleRemoveFile = React.useCallback(
    (fileId: string) => fileDispatch({ type: 'REMOVE_FILE', fileId }),
    []
  );

  const handleClearErrors = React.useCallback(
    () => fileDispatch({ type: 'CLEAR_VALIDATION_ERRORS' }),
    []
  );

  // ── Finish handler (returns IWizardSuccessConfig) ───────────────────────

  // Refs for finish handler — read latest values at invocation time to avoid
  // stale closures. The dynamic step renderContent functions already use this
  // pattern; handleFinish must do the same so record creation always reflects
  // the user's latest edits (including Assign Resources overrides).
  const selectedActionsRef = React.useRef(selectedActions);
  selectedActionsRef.current = selectedActions;
  const fileStateRef = React.useRef(fileState);
  fileStateRef.current = fileState;
  const speContainerIdRef = React.useRef(speContainerId);
  speContainerIdRef.current = speContainerId;

  const handleFinish = React.useCallback(async (): Promise<IWizardSuccessConfig> => {
    if (!webApi) {
      throw new Error('Dataverse connection not available. Please close and retry.');
    }

    // Read latest values from refs — not closure-captured state
    const currentFormValues = step2FormValuesRef.current;
    const currentSelectedActions = selectedActionsRef.current;
    const currentRecipients = recipientsRef.current;
    const currentCcRecipients = ccRecipientsRef.current;
    const currentEmailTo = emailToRef.current;
    const currentEmailSubject = emailSubjectRef.current;
    const currentEmailBody = emailBodyRef.current;
    const currentFiles = fileStateRef.current.uploadedFiles;
    const currentContainerId = speContainerIdRef.current;

    const followOnActions: IFollowOnActions = {};

    // Assign Resources — values are already in currentFormValues (written by
    // createMatter via lookup bindings). No separate follow-on action needed.

    if (currentSelectedActions.includes('draft-summary')) {
      // Extract emails from IRecipientItem[] for the BFF
      const allRecipientEmails = [
        ...currentRecipients.map((r) => r.email).filter(Boolean),
        ...currentCcRecipients.map((r) => r.email).filter(Boolean),
      ];
      followOnActions.draftSummary = { recipientEmails: allRecipientEmails };
    }

    if (currentSelectedActions.includes('send-email') && currentEmailTo.trim()) {
      followOnActions.sendEmail = {
        to: currentEmailTo.trim(),
        subject: currentEmailSubject,
        body: currentEmailBody,
      };
    }

    const service = new MatterService(webApi, currentContainerId || undefined);
    const result = await service.createMatter(
      currentFormValues,
      currentFiles,
      followOnActions
    );

    if (result.status === 'error') {
      throw new Error(result.errorMessage ?? 'An unknown error occurred.');
    }

    const matterId = result.matterId!;
    const matterName = result.matterName!;
    const hasWarnings = result.warnings.length > 0;

    const viewMatter = () => {
      navigateToEntity({
        action: 'openRecord',
        entityName: 'sprk_matter',
        entityId: matterId,
      });
      onClose();
    };

    return {
      icon: (
        <CheckmarkCircleFilled
          fontSize={64}
          style={{ color: tokens.colorPaletteGreenForeground1 }}
        />
      ),
      title: hasWarnings ? 'Matter created with warnings' : 'Matter created!',
      body: (
        <Text size={300} style={{ color: tokens.colorNeutralForeground2 }}>
          <span style={{ color: tokens.colorBrandForeground1, fontWeight: 600 }}>
            &ldquo;{matterName}&rdquo;
          </span>{' '}
          has been created
          {hasWarnings
            ? ', though some follow-on actions could not complete. See details below.'
            : ' and is ready to use.'}
        </Text>
      ),
      actions: (
        <>
          <Button
            appearance="primary"
            onClick={viewMatter}
            aria-label={`View matter: ${matterName}`}
          >
            View Matter
          </Button>
          <Button appearance="secondary" onClick={onClose}>
            Close
          </Button>
        </>
      ),
      warnings: result.warnings,
    };
  }, [webApi, onClose]);

  // ── Step configurations ─────────────────────────────────────────────────

  const stepConfigs: IWizardStepConfig[] = React.useMemo(
    () => [
      {
        id: 'add-files',
        label: 'Add file(s)',
        canAdvance: () => fileState.uploadedFiles.length > 0,
        renderContent: () => (
          <>
            <div>
              <Text as="h2" size={500} weight="semibold" className={styles.stepTitle}>
                Add file(s)
              </Text>
              <Text size={200} className={styles.stepSubtitle}>
                Upload documents for AI analysis. The AI will extract key information
                to pre-fill the matter form.
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
              <UploadedFileList files={fileState.uploadedFiles} onRemove={handleRemoveFile} />
            )}
          </>
        ),
      },
      {
        id: 'create-record',
        label: 'Enter Info',
        canAdvance: () => step2Valid,
        renderContent: () => (
          <CreateRecordStep
            webApi={webApi!}
            uploadedFileNames={fileState.uploadedFiles.map((f) => f.name)}
            uploadedFiles={fileState.uploadedFiles}
            onValidChange={setStep2Valid}
            onSubmit={(values) => setStep2FormValues(values)}
            initialFormValues={step2FormValues}
          />
        ),
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
          />
        ),
      },
    ],
    [
      fileState.uploadedFiles,
      fileState.validationErrors,
      step2Valid,
      selectedActions,
      webApi,
      styles,
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
      title="Create New Matter"
      ariaLabel="Create New Matter"
      steps={stepConfigs}
      onClose={onClose}
      onFinish={handleFinish}
      finishingLabel="Creating matter&hellip;"
      finishLabel="Finish"
    />
  );
};

// Default export enables React.lazy() dynamic import for bundle-size optimization (Task 033).
// Named export WizardDialog above is preserved for direct imports in tests.
export default WizardDialog;
