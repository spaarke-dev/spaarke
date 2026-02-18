/**
 * WizardDialog.tsx
 * Multi-step wizard dialog shell for "Create New Matter".
 *
 * Layout:
 *   ┌────────────────────────────────────────────────┐
 *   │ [Sidebar ~200px │ Content area (flex: 1)]       │
 *   │  WizardStepper  │  Step 1: FileUploadZone +     │
 *   │                 │          UploadedFileList      │
 *   ├─────────────────┴──────────────────────────────┤
 *   │ [Cancel]              [Back (hidden on step 1)] │
 *   │                                     [Next]      │
 *   └────────────────────────────────────────────────┘
 *
 * Wizard state is managed by useReducer (wizardReducer).
 * Dialog is ~800px wide. Fluent v9 Dialog + semantic tokens throughout.
 * Zero hardcoded colors.
 *
 * Steps:
 *   0 — Add file(s)       (FileUploadZone + UploadedFileList)
 *   1 — Create record     (CreateRecordStep)
 *   2 — Next Steps        (NextStepsStep — checkbox card selection)
 *   3+ — Follow-on steps  (AssignCounselStep, DraftSummaryStep, SendEmailStep)
 *        Injected dynamically based on card selections in Step 2.
 *
 * After all steps complete, SuccessConfirmation replaces the step content.
 */
import * as React from 'react';
import {
  Dialog,
  DialogSurface,
  DialogBody,
  DialogContent,
  DialogActions,
  Button,
  MessageBar,
  MessageBarBody,
  Text,
  Spinner,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import { Dismiss24Regular } from '@fluentui/react-icons';
import {
  IWizardDialogProps,
  IWizardState,
  WizardAction,
  IWizardStep,
  IUploadedFile,
  IFileValidationError,
} from './wizardTypes';
import { WizardStepper } from './WizardStepper';
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
import { SuccessConfirmation } from './SuccessConfirmation';
import { MatterService, IFollowOnActions } from './matterService';
import type { ICreateMatterFormState } from './formTypes';
import type { IContact } from '../../types/entities';

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
  webApi?: ComponentFramework.WebApi;
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  // Override DialogSurface to reach ~800px and fill height nicely
  surface: {
    width: '800px',
    maxWidth: '95vw',
    padding: '0px',
  },
  // DialogBody: remove default padding so we control layout entirely
  body: {
    padding: '0px',
    display: 'flex',
    flexDirection: 'column',
    maxHeight: '85vh',
    overflow: 'hidden',
  },
  // Custom title bar (replaces DialogTitle default rendering)
  titleBar: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    paddingTop: tokens.spacingVerticalL,
    paddingBottom: tokens.spacingVerticalL,
    paddingLeft: tokens.spacingHorizontalXL,
    paddingRight: tokens.spacingHorizontalL,
    borderBottomWidth: '1px',
    borderBottomStyle: 'solid',
    borderBottomColor: tokens.colorNeutralStroke2,
    flexShrink: 0,
  },
  titleText: {
    color: tokens.colorNeutralForeground1,
  },
  closeButton: {
    color: tokens.colorNeutralForeground3,
  },
  // Main body: sidebar + content side by side
  mainArea: {
    display: 'flex',
    flex: '1 1 auto',
    overflow: 'hidden',
  },
  // Content area (right of sidebar)
  contentArea: {
    flex: '1 1 auto',
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
    overflowY: 'auto',
    paddingTop: tokens.spacingVerticalXL,
    paddingBottom: tokens.spacingVerticalL,
    paddingLeft: tokens.spacingHorizontalXL,
    paddingRight: tokens.spacingHorizontalXL,
  },
  stepTitle: {
    color: tokens.colorNeutralForeground1,
    marginBottom: tokens.spacingVerticalXS,
  },
  stepSubtitle: {
    color: tokens.colorNeutralForeground3,
    marginBottom: tokens.spacingVerticalM,
  },
  // Validation error bar
  errorBar: {
    flexShrink: 0,
  },
  // Footer / dialog actions
  footer: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    paddingTop: tokens.spacingVerticalM,
    paddingBottom: tokens.spacingVerticalM,
    paddingLeft: tokens.spacingHorizontalXL,
    paddingRight: tokens.spacingHorizontalL,
    borderTopWidth: '1px',
    borderTopStyle: 'solid',
    borderTopColor: tokens.colorNeutralStroke2,
    backgroundColor: tokens.colorNeutralBackground1,
    flexShrink: 0,
  },
  footerLeft: {
    display: 'flex',
    gap: tokens.spacingHorizontalS,
  },
  footerRight: {
    display: 'flex',
    gap: tokens.spacingHorizontalS,
    alignItems: 'center',
  },
  // Progress indicator row
  progressRow: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    color: tokens.colorNeutralForeground3,
  },
});

// ---------------------------------------------------------------------------
// Base wizard steps
// ---------------------------------------------------------------------------

const BASE_STEPS: IWizardStep[] = [
  { id: 'add-files', label: 'Add file(s)', status: 'active' },
  { id: 'create-record', label: 'Create record', status: 'pending' },
  { id: 'next-steps', label: 'Next Steps', status: 'pending' },
];

/** Step IDs for the three base steps. */
const BASE_STEP_IDS = new Set(['add-files', 'create-record', 'next-steps']);

// ---------------------------------------------------------------------------
// Initial reducer state
// ---------------------------------------------------------------------------

function buildInitialState(): IWizardState {
  return {
    currentStepIndex: 0,
    steps: BASE_STEPS,
    uploadedFiles: [],
    validationErrors: [],
  };
}

// ---------------------------------------------------------------------------
// Wizard reducer
// ---------------------------------------------------------------------------

function wizardReducer(state: IWizardState, action: WizardAction): IWizardState {
  switch (action.type) {
    case 'NEXT_STEP': {
      const nextIndex = Math.min(state.currentStepIndex + 1, state.steps.length - 1);
      if (nextIndex === state.currentStepIndex) return state;

      const updatedSteps: IWizardStep[] = state.steps.map((step, i) => {
        if (i < nextIndex) return { ...step, status: 'completed' };
        if (i === nextIndex) return { ...step, status: 'active' };
        return { ...step, status: 'pending' };
      });

      return { ...state, currentStepIndex: nextIndex, steps: updatedSteps };
    }

    case 'PREV_STEP': {
      const prevIndex = Math.max(state.currentStepIndex - 1, 0);
      if (prevIndex === state.currentStepIndex) return state;

      const updatedSteps: IWizardStep[] = state.steps.map((step, i) => {
        if (i < prevIndex) return { ...step, status: 'completed' };
        if (i === prevIndex) return { ...step, status: 'active' };
        return { ...step, status: 'pending' };
      });

      return { ...state, currentStepIndex: prevIndex, steps: updatedSteps };
    }

    case 'GO_TO_STEP': {
      const targetIndex = Math.max(0, Math.min(action.stepIndex, state.steps.length - 1));
      const updatedSteps: IWizardStep[] = state.steps.map((step, i) => {
        if (i < targetIndex) return { ...step, status: 'completed' };
        if (i === targetIndex) return { ...step, status: 'active' };
        return { ...step, status: 'pending' };
      });

      return { ...state, currentStepIndex: targetIndex, steps: updatedSteps };
    }

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

    case 'REMOVE_FILE': {
      return {
        ...state,
        uploadedFiles: state.uploadedFiles.filter((f) => f.id !== action.fileId),
      };
    }

    case 'SET_VALIDATION_ERRORS': {
      return { ...state, validationErrors: action.errors };
    }

    case 'CLEAR_VALIDATION_ERRORS': {
      return { ...state, validationErrors: [] };
    }

    case 'ADD_DYNAMIC_STEP': {
      const alreadyExists = state.steps.some((s) => s.id === action.step.id);
      if (alreadyExists) return state;

      // Keep base steps at the front; insert dynamic steps in canonical order
      const baseSteps = state.steps.filter((s) => BASE_STEP_IDS.has(s.id));
      const dynamicSteps = state.steps.filter((s) => !BASE_STEP_IDS.has(s.id));

      const canonicalOrder = [
        'followon-assign-counsel',
        'followon-draft-summary',
        'followon-send-email',
      ];
      const merged = [...dynamicSteps, action.step].sort(
        (a, b) => canonicalOrder.indexOf(a.id) - canonicalOrder.indexOf(b.id)
      );

      return { ...state, steps: [...baseSteps, ...merged] };
    }

    case 'REMOVE_DYNAMIC_STEP': {
      const filtered = state.steps.filter((s) => s.id !== action.stepId);
      if (filtered.length === state.steps.length) return state;

      const clampedIndex = Math.min(state.currentStepIndex, filtered.length - 1);
      return { ...state, steps: filtered, currentStepIndex: clampedIndex };
    }

    default:
      return state;
  }
}

// ---------------------------------------------------------------------------
// Step 1 content
// ---------------------------------------------------------------------------

interface IStep1ContentProps {
  uploadedFiles: IUploadedFile[];
  validationErrors: IFileValidationError[];
  onFilesAccepted: (files: IUploadedFile[]) => void;
  onValidationErrors: (errors: IFileValidationError[]) => void;
  onRemoveFile: (fileId: string) => void;
  onClearErrors: () => void;
}

const Step1Content: React.FC<IStep1ContentProps> = ({
  uploadedFiles,
  validationErrors,
  onFilesAccepted,
  onValidationErrors,
  onRemoveFile,
  onClearErrors,
}) => {
  const styles = useStyles();

  return (
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

      {validationErrors.length > 0 && (
        <MessageBar
          intent="error"
          className={styles.errorBar}
          onMouseEnter={onClearErrors}
        >
          <MessageBarBody>
            {validationErrors.map((err, i) => (
              <div key={i}>
                <strong>{err.fileName}</strong>: {err.reason}
              </div>
            ))}
          </MessageBarBody>
        </MessageBar>
      )}

      <FileUploadZone
        onFilesAccepted={onFilesAccepted}
        onValidationErrors={onValidationErrors}
      />

      {uploadedFiles.length > 0 && (
        <UploadedFileList files={uploadedFiles} onRemove={onRemoveFile} />
      )}
    </>
  );
};

// ---------------------------------------------------------------------------
// Empty form state helper
// ---------------------------------------------------------------------------

const EMPTY_FORM_STATE: ICreateMatterFormState = {
  matterType: '',
  matterName: '',
  estimatedBudget: '',
  practiceArea: '',
  organization: '',
  keyParties: '',
  summary: '',
};

// ---------------------------------------------------------------------------
// WizardDialog (exported)
// ---------------------------------------------------------------------------

export const WizardDialog: React.FC<IWizardDialogPropsInternal> = ({
  open,
  onClose,
  webApi,
}) => {
  const styles = useStyles();

  const [state, dispatch] = React.useReducer(
    wizardReducer,
    undefined,
    buildInitialState
  );

  // ── Step 2 state ─────────────────────────────────────────────────────────
  const [step2Valid, setStep2Valid] = React.useState(false);
  const [step2FormValues, setStep2FormValues] = React.useState<ICreateMatterFormState>(EMPTY_FORM_STATE);

  // ── Step 3: Next Steps selection ─────────────────────────────────────────
  const [selectedActions, setSelectedActions] = React.useState<FollowOnActionId[]>([]);

  // ── Assign Counsel state ──────────────────────────────────────────────────
  const [selectedContact, setSelectedContact] = React.useState<IContact | null>(null);

  // ── Draft Summary state ───────────────────────────────────────────────────
  const [summaryText, setSummaryText] = React.useState('');
  const [recipientEmails, setRecipientEmails] = React.useState<string[]>([]);

  // ── Send Email state ──────────────────────────────────────────────────────
  const [emailTo, setEmailTo] = React.useState('');
  const [emailSubject, setEmailSubject] = React.useState('');
  const [emailBody, setEmailBody] = React.useState('');

  // ── Creation flow state ──────────────────────────────────────────────────
  const [isCreating, setIsCreating] = React.useState(false);
  const [createError, setCreateError] = React.useState<string | null>(null);
  const [successResult, setSuccessResult] = React.useState<{
    matterId: string;
    matterName: string;
    warnings: string[];
  } | null>(null);

  // ── Reset on open ─────────────────────────────────────────────────────────
  React.useEffect(() => {
    if (open) {
      dispatch({ type: 'GO_TO_STEP', stepIndex: 0 });
      setStep2Valid(false);
      setStep2FormValues(EMPTY_FORM_STATE);
      setSelectedActions([]);
      setSelectedContact(null);
      setSummaryText('');
      setRecipientEmails([]);
      setEmailTo('');
      setEmailSubject('');
      setEmailBody('');
      setIsCreating(false);
      setCreateError(null);
      setSuccessResult(null);
    }
  }, [open]);

  // ── Sync dynamic steps with selected action cards ─────────────────────────
  const prevSelectedActionsRef = React.useRef<FollowOnActionId[]>([]);

  React.useEffect(() => {
    const prev = prevSelectedActionsRef.current;
    const next = selectedActions;

    // Add newly selected follow-on steps
    next.forEach((actionId) => {
      if (!prev.includes(actionId)) {
        dispatch({
          type: 'ADD_DYNAMIC_STEP',
          step: {
            id: FOLLOW_ON_STEP_ID_MAP[actionId],
            label: FOLLOW_ON_STEP_LABEL_MAP[actionId],
            status: 'pending',
          },
        });
      }
    });

    // Remove deselected follow-on steps
    prev.forEach((actionId) => {
      if (!next.includes(actionId)) {
        dispatch({
          type: 'REMOVE_DYNAMIC_STEP',
          stepId: FOLLOW_ON_STEP_ID_MAP[actionId],
        });
      }
    });

    prevSelectedActionsRef.current = next;
  }, [selectedActions]);

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

  // ── Event handlers ────────────────────────────────────────────────────────

  const handleFilesAccepted = React.useCallback(
    (files: IUploadedFile[]) => dispatch({ type: 'ADD_FILES', files }),
    []
  );

  const handleValidationErrors = React.useCallback(
    (errors: IFileValidationError[]) =>
      dispatch({ type: 'SET_VALIDATION_ERRORS', errors }),
    []
  );

  const handleRemoveFile = React.useCallback(
    (fileId: string) => dispatch({ type: 'REMOVE_FILE', fileId }),
    []
  );

  const handleClearErrors = React.useCallback(
    () => dispatch({ type: 'CLEAR_VALIDATION_ERRORS' }),
    []
  );

  const handleNext = React.useCallback(() => {
    dispatch({ type: 'NEXT_STEP' });
  }, []);

  const handleBack = React.useCallback(() => {
    dispatch({ type: 'PREV_STEP' });
  }, []);

  // ── Finish handler ────────────────────────────────────────────────────────

  const handleFinish = React.useCallback(async () => {
    if (!webApi) {
      setCreateError('Dataverse connection not available. Please close and retry.');
      return;
    }

    setIsCreating(true);
    setCreateError(null);

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

    const service = new MatterService(webApi);
    const result = await service.createMatter(
      step2FormValues,
      state.uploadedFiles,
      followOnActions
    );

    setIsCreating(false);

    if (result.status === 'error') {
      setCreateError(result.errorMessage ?? 'An unknown error occurred.');
      return;
    }

    setSuccessResult({
      matterId: result.matterId!,
      matterName: result.matterName!,
      warnings: result.warnings,
    });
  }, [
    webApi,
    step2FormValues,
    state.uploadedFiles,
    selectedActions,
    selectedContact,
    recipientEmails,
    emailTo,
    emailSubject,
    emailBody,
  ]);

  // ── Derived values ────────────────────────────────────────────────────────

  const currentStep = state.steps[state.currentStepIndex];
  const isFirstStep = state.currentStepIndex === 0;
  const isLastStep = state.currentStepIndex === state.steps.length - 1;
  const isNextStepsStep = currentStep?.id === 'next-steps';

  // At the Next Steps step with zero cards selected → clicking Next = Finish
  const nextStepsIsFinish = isNextStepsStep && selectedActions.length === 0;

  const canAdvance = React.useMemo((): boolean => {
    if (isCreating) return false;

    switch (state.currentStepIndex) {
      case 0:
        return state.uploadedFiles.length > 0;
      case 1:
        return step2Valid;
      case 2:
        // Next Steps: always advance (0 selections = skip all follow-ons)
        return true;
      default: {
        if (!currentStep) return true;
        if (currentStep.id === 'followon-assign-counsel') {
          return selectedContact !== null;
        }
        if (currentStep.id === 'followon-send-email') {
          return (
            emailTo.trim() !== '' &&
            emailSubject.trim() !== '' &&
            emailBody.trim() !== ''
          );
        }
        // Draft summary — no hard requirement
        return true;
      }
    }
  }, [
    state.currentStepIndex,
    state.uploadedFiles.length,
    step2Valid,
    currentStep,
    selectedContact,
    emailTo,
    emailSubject,
    emailBody,
    isCreating,
  ]);

  // ── Step content renderer ─────────────────────────────────────────────────

  const renderStepContent = (): React.ReactNode => {
    // Success screen replaces all step content
    if (successResult) {
      return (
        <SuccessConfirmation
          matterName={successResult.matterName}
          matterId={successResult.matterId}
          warnings={successResult.warnings}
          onClose={onClose}
        />
      );
    }

    switch (state.currentStepIndex) {
      case 0:
        return (
          <Step1Content
            uploadedFiles={state.uploadedFiles}
            validationErrors={state.validationErrors}
            onFilesAccepted={handleFilesAccepted}
            onValidationErrors={handleValidationErrors}
            onRemoveFile={handleRemoveFile}
            onClearErrors={handleClearErrors}
          />
        );

      case 1:
        return (
          <CreateRecordStep
            uploadedFileNames={state.uploadedFiles.map((f) => f.name)}
            onValidChange={setStep2Valid}
            onSubmit={(values) => setStep2FormValues(values)}
          />
        );

      case 2:
        return (
          <NextStepsStep
            selectedActions={selectedActions}
            onSelectionChange={setSelectedActions}
          />
        );

      default: {
        // Follow-on steps — identified by step ID
        if (!currentStep) return null;

        if (currentStep.id === 'followon-assign-counsel') {
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

        if (currentStep.id === 'followon-draft-summary') {
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

        if (currentStep.id === 'followon-send-email') {
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

        return (
          <Text size={300} style={{ color: tokens.colorNeutralForeground3 }}>
            {currentStep.label}
          </Text>
        );
      }
    }
  };

  // ── Determine primary button label and action ─────────────────────────────

  const primaryButtonLabel: string = (() => {
    if (isCreating) return 'Creating\u2026';
    if (isLastStep || nextStepsIsFinish) return 'Finish';
    return 'Next';
  })();

  const handlePrimaryButtonClick = React.useCallback(() => {
    if (isLastStep || nextStepsIsFinish) {
      void handleFinish();
    } else {
      handleNext();
    }
  }, [isLastStep, nextStepsIsFinish, handleFinish, handleNext]);

  // ── Render ────────────────────────────────────────────────────────────────

  return (
    <Dialog
      open={open}
      onOpenChange={(_e, data) => {
        if (!data.open) onClose();
      }}
    >
      <DialogSurface className={styles.surface} aria-label="Create New Matter">
        <DialogBody className={styles.body}>
          {/* Custom title bar with close button */}
          <div className={styles.titleBar}>
            <Text as="h1" size={500} weight="semibold" className={styles.titleText}>
              Create New Matter
            </Text>
            <Button
              appearance="subtle"
              size="small"
              icon={<Dismiss24Regular />}
              className={styles.closeButton}
              onClick={onClose}
              aria-label="Close dialog"
            />
          </div>

          {/* Sidebar + content area */}
          <div className={styles.mainArea}>
            <WizardStepper steps={state.steps} />

            <DialogContent className={styles.contentArea}>
              {/* Creation error bar — role="alert" for assertive form validation feedback */}
              {createError && (
                <MessageBar intent="error" role="alert">
                  <MessageBarBody>{createError}</MessageBarBody>
                </MessageBar>
              )}

              {renderStepContent()}
            </DialogContent>
          </div>

          {/* Footer — hidden after success (SuccessConfirmation has its own buttons) */}
          {!successResult && (
            <DialogActions className={styles.footer}>
              <div className={styles.footerLeft}>
                <Button
                  appearance="secondary"
                  onClick={onClose}
                  disabled={isCreating}
                >
                  Cancel
                </Button>
              </div>

              <div className={styles.footerRight}>
                {/* In-progress spinner */}
                {isCreating && (
                  <div className={styles.progressRow}>
                    <Spinner size="tiny" />
                    <Text size={200}>Creating matter\u2026</Text>
                  </div>
                )}

                {/* Back button — hidden on step 1 */}
                {!isFirstStep && (
                  <Button
                    appearance="secondary"
                    onClick={handleBack}
                    disabled={isCreating}
                  >
                    Back
                  </Button>
                )}

                {/* Next / Finish */}
                <Button
                  appearance="primary"
                  onClick={handlePrimaryButtonClick}
                  disabled={!canAdvance || isCreating}
                >
                  {primaryButtonLabel}
                </Button>
              </div>
            </DialogActions>
          )}
        </DialogBody>
      </DialogSurface>
    </Dialog>
  );
};

// Default export enables React.lazy() dynamic import for bundle-size optimization (Task 033).
// Named export WizardDialog above is preserved for direct imports in tests.
export default WizardDialog;
