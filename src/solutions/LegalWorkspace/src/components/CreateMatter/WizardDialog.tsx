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
 *   1 — Create record     (CreateRecordStep)
 *   2 — Next Steps        (NextStepsStep — checkbox card selection)
 *   3+ — Follow-on steps  (AssignCounselStep, DraftSummaryStep, SendEmailStep)
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
import { AssignCounselStep } from './AssignCounselStep';
import { DraftSummaryStep } from './DraftSummaryStep';
import {
  SendEmailStep,
  buildDefaultEmailSubject,
  buildDefaultEmailBody,
} from './SendEmailStep';
import { MatterService, IFollowOnActions } from './matterService';
import type { ICreateMatterFormState } from './formTypes';
import type { IContact } from '../../types/entities';
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

  // ── Assign Counsel state ────────────────────────────────────────────────
  const [selectedContact, setSelectedContact] = React.useState<IContact | null>(null);

  // ── Draft Summary state ─────────────────────────────────────────────────
  const [summaryText, setSummaryText] = React.useState('');
  const [recipientEmails, setRecipientEmails] = React.useState<string[]>([]);

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
      setSelectedContact(null);
      setSummaryText('');
      setRecipientEmails([]);
      setEmailTo('');
      setEmailSubject('');
      setEmailBody('');
    }
  }, [open]);

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
            if (stepId === 'followon-assign-counsel') return selectedContact !== null;
            if (stepId === 'followon-send-email') {
              return emailTo.trim() !== '' && emailSubject.trim() !== '' && emailBody.trim() !== '';
            }
            return true; // draft-summary has no hard requirement
          },
          renderContent: () => {
            if (stepId === 'followon-assign-counsel') {
              if (!webApi) {
                return (
                  <Text size={300} style={{ color: tokens.colorNeutralForeground3 }}>
                    Dataverse connection not available.
                  </Text>
                );
              }
              return (
                <AssignCounselStep
                  webApi={webApi}
                  selectedContact={selectedContact}
                  onContactChange={setSelectedContact}
                />
              );
            }
            if (stepId === 'followon-draft-summary') {
              return (
                <DraftSummaryStep
                  formValues={step2FormValues}
                  summaryText={summaryText}
                  onSummaryChange={setSummaryText}
                  recipientEmails={recipientEmails}
                  onRecipientsChange={setRecipientEmails}
                />
              );
            }
            if (stepId === 'followon-send-email') {
              return (
                <SendEmailStep
                  formValues={step2FormValues}
                  emailTo={emailTo}
                  onEmailToChange={setEmailTo}
                  emailSubject={emailSubject}
                  onEmailSubjectChange={setEmailSubject}
                  emailBody={emailBody}
                  onEmailBodyChange={setEmailBody}
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
  }, [selectedActions, selectedContact, webApi, step2FormValues, summaryText, recipientEmails, emailTo, emailSubject, emailBody]);

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

  const handleFinish = React.useCallback(async (): Promise<IWizardSuccessConfig> => {
    if (!webApi) {
      throw new Error('Dataverse connection not available. Please close and retry.');
    }

    const followOnActions: IFollowOnActions = {};

    if (selectedActions.includes('assign-counsel') && selectedContact) {
      followOnActions.assignCounsel = {
        contactId: selectedContact.sprk_contactid,
        contactName: selectedContact.sprk_name,
      };
    }

    if (selectedActions.includes('draft-summary')) {
      followOnActions.draftSummary = { recipientEmails };
    }

    if (selectedActions.includes('send-email') && emailTo.trim()) {
      followOnActions.sendEmail = {
        to: emailTo.trim(),
        subject: emailSubject,
        body: emailBody,
      };
    }

    const service = new MatterService(webApi, speContainerId || undefined);
    const result = await service.createMatter(
      step2FormValues,
      fileState.uploadedFiles,
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
  }, [
    webApi,
    speContainerId,
    step2FormValues,
    fileState.uploadedFiles,
    selectedActions,
    selectedContact,
    recipientEmails,
    emailTo,
    emailSubject,
    emailBody,
    onClose,
  ]);

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
        label: 'Create record',
        canAdvance: () => step2Valid,
        renderContent: () => (
          <CreateRecordStep
            webApi={webApi!}
            uploadedFileNames={fileState.uploadedFiles.map((f) => f.name)}
            uploadedFiles={fileState.uploadedFiles}
            onValidChange={setStep2Valid}
            onSubmit={(values) => setStep2FormValues(values)}
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
      finishingLabel="Creating matter\u2026"
      finishLabel="Finish"
    />
  );
};

// Default export enables React.lazy() dynamic import for bundle-size optimization (Task 033).
// Named export WizardDialog above is preserved for direct imports in tests.
export default WizardDialog;
