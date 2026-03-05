/**
 * ProjectWizardDialog.tsx
 * Multi-step wizard dialog for "Create New Project".
 *
 * Steps mirror the Create New Matter wizard (WizardDialog.tsx):
 *   0 — Add file(s)       (FileUploadZone + UploadedFileList)
 *   1 — Create record     (CreateProjectStep)
 *   2 — Next Steps        (NextStepsStep — checkbox card selection)
 *   3+ — Follow-on steps  (AssignCounselStep, DraftSummaryStep, SendEmailStep)
 *        Injected dynamically based on card selections in Step 2.
 *
 * Finish handler:
 *   1. Creates sprk_project record via ProjectService
 *   2. Uploads files to SPE via EntityCreationService
 *   3. Creates sprk_document records linked to the project
 *   4. Queues AI Document Profile analysis for each document
 *   5. Executes follow-on actions (assign counsel, draft summary, send email)
 *
 * Delegates all shell concerns to WizardShell.
 *
 * Default export enables React.lazy() dynamic import for bundle-size
 * optimization (same pattern as WizardDialog.tsx).
 */
import * as React from 'react';
import {
  Text,
  Button,
  MessageBar,
  MessageBarBody,
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

import { FileUploadZone } from '../CreateMatter/FileUploadZone';
import { UploadedFileList } from '../CreateMatter/UploadedFileList';
import type { IUploadedFile, IFileValidationError } from '../CreateMatter/wizardTypes';
import {
  NextStepsStep,
  FollowOnActionId,
  FOLLOW_ON_STEP_ID_MAP,
  FOLLOW_ON_STEP_LABEL_MAP,
} from '../CreateMatter/NextStepsStep';
import { AssignCounselStep } from '../CreateMatter/AssignCounselStep';
import { DraftSummaryStep } from '../CreateMatter/DraftSummaryStep';
import {
  SendEmailStep,
  buildDefaultEmailSubject,
  buildDefaultEmailBody,
} from '../CreateMatter/SendEmailStep';

import { CreateProjectStep } from './CreateProjectStep';
import { ProjectService } from './projectService';
import { EMPTY_PROJECT_FORM } from './projectFormTypes';
import type { ICreateProjectFormState } from './projectFormTypes';

import { EntityCreationService } from '../../services/EntityCreationService';
import { getSpeContainerIdFromBusinessUnit } from '../../services/xrmProvider';
import { navigateToEntity } from '../../utils/navigation';
import type { IContact } from '../../types/entities';
import type { IWebApi } from '../../types/xrm';

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

interface IProjectWizardDialogProps {
  open: boolean;
  onClose: () => void;
  webApi: IWebApi;
}

// ---------------------------------------------------------------------------
// File state reducer (same pattern as Create Matter wizard)
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
// Canonical order for dynamic follow-on steps
// ---------------------------------------------------------------------------

const FOLLOW_ON_CANONICAL_ORDER = [
  'followon-assign-counsel',
  'followon-draft-summary',
  'followon-send-email',
];

// ---------------------------------------------------------------------------
// ProjectWizardDialog
// ---------------------------------------------------------------------------

const ProjectWizardDialog: React.FC<IProjectWizardDialogProps> = ({ open, onClose, webApi }) => {
  const styles = useStyles();
  const shellRef = React.useRef<IWizardShellHandle>(null);

  // ── Domain file state ───────────────────────────────────────────────────
  const [fileState, fileDispatch] = React.useReducer(fileReducer, {
    uploadedFiles: [],
    validationErrors: [],
  });

  // ── Step 2 state ────────────────────────────────────────────────────────
  const [formValid, setFormValid] = React.useState(false);
  const [formValues, setFormValues] = React.useState<ICreateProjectFormState>(EMPTY_PROJECT_FORM);

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
      setFormValid(false);
      setFormValues(EMPTY_PROJECT_FORM);
      setSelectedActions([]);
      setSelectedContact(null);
      setSummaryText('');
      setRecipientEmails([]);
      setEmailTo('');
      setEmailSubject('');
      setEmailBody('');
    }
  }, [open]);

  // ── Refs for dynamic step closures (prevents stale closure bug) ─────────
  const selectedContactRef = React.useRef(selectedContact);
  selectedContactRef.current = selectedContact;
  const formValuesRef = React.useRef(formValues);
  formValuesRef.current = formValues;
  const summaryTextRef = React.useRef(summaryText);
  summaryTextRef.current = summaryText;
  const recipientEmailsRef = React.useRef(recipientEmails);
  recipientEmailsRef.current = recipientEmails;
  const emailToRef = React.useRef(emailTo);
  emailToRef.current = emailTo;
  const emailSubjectRef = React.useRef(emailSubject);
  emailSubjectRef.current = emailSubject;
  const emailBodyRef = React.useRef(emailBody);
  emailBodyRef.current = emailBody;

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
            if (stepId === 'followon-assign-counsel') return selectedContactRef.current !== null;
            if (stepId === 'followon-send-email') {
              return emailToRef.current.trim() !== '' && emailSubjectRef.current.trim() !== '' && emailBodyRef.current.trim() !== '';
            }
            return true; // draft-summary has no hard requirement
          },
          renderContent: () => {
            // Build form values in the shape DraftSummaryStep / SendEmailStep expect
            const fv = formValuesRef.current;
            const matterShapedValues = {
              matterTypeId: fv.projectTypeId,
              matterTypeName: fv.projectTypeName,
              practiceAreaId: fv.practiceAreaId,
              practiceAreaName: fv.practiceAreaName,
              matterName: fv.projectName,
              assignedAttorneyId: fv.assignedAttorneyId,
              assignedAttorneyName: fv.assignedAttorneyName,
              assignedParalegalId: fv.assignedParalegalId,
              assignedParalegalName: fv.assignedParalegalName,
              assignedOutsideCounselId: fv.assignedOutsideCounselId,
              assignedOutsideCounselName: fv.assignedOutsideCounselName,
              summary: fv.description,
            };

            if (stepId === 'followon-assign-counsel') {
              return (
                <AssignCounselStep
                  webApi={webApi}
                  selectedContact={selectedContactRef.current}
                  onContactChange={setSelectedContact}
                />
              );
            }
            if (stepId === 'followon-draft-summary') {
              return (
                <DraftSummaryStep
                  formValues={matterShapedValues}
                  summaryText={summaryTextRef.current}
                  onSummaryChange={setSummaryText}
                  recipientEmails={recipientEmailsRef.current}
                  onRecipientsChange={setRecipientEmails}
                />
              );
            }
            if (stepId === 'followon-send-email') {
              return (
                <SendEmailStep
                  formValues={matterShapedValues}
                  emailTo={emailToRef.current}
                  onEmailToChange={setEmailTo}
                  emailSubject={emailSubjectRef.current}
                  onEmailSubjectChange={setEmailSubject}
                  emailBody={emailBodyRef.current}
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
  }, [selectedActions, webApi]);

  // ── Pre-fill email fields when send-email is selected ────────────────────
  React.useEffect(() => {
    if (
      selectedActions.includes('send-email') &&
      formValues.projectName &&
      !emailSubject
    ) {
      setEmailSubject(buildDefaultEmailSubject(formValues.projectName));
      // Map project form values to the shape buildDefaultEmailBody expects
      setEmailBody(buildDefaultEmailBody({
        matterTypeId: formValues.projectTypeId,
        matterTypeName: formValues.projectTypeName,
        practiceAreaId: formValues.practiceAreaId,
        practiceAreaName: formValues.practiceAreaName,
        matterName: formValues.projectName,
        assignedAttorneyId: formValues.assignedAttorneyId,
        assignedAttorneyName: formValues.assignedAttorneyName,
        assignedParalegalId: formValues.assignedParalegalId,
        assignedParalegalName: formValues.assignedParalegalName,
        summary: formValues.description,
      }));
    }
  }, [selectedActions, formValues, emailSubject]);

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
    const warnings: string[] = [];

    // 1. Create sprk_project record
    const projectService = new ProjectService(webApi);
    const result = await projectService.createProject(formValues);
    if (!result.success) {
      throw new Error(result.errorMessage ?? 'Failed to create project');
    }

    const projectId = result.projectId!;
    const projectName = result.projectName!;

    // 2. Upload files to SPE + create document records (if files uploaded and container available)
    if (fileState.uploadedFiles.length > 0 && speContainerId) {
      try {
        const entityService = new EntityCreationService(webApi);

        // Upload files to SPE
        const uploadResult = await entityService.uploadFilesToSpe(
          speContainerId,
          fileState.uploadedFiles
        );

        if (uploadResult.errors.length > 0) {
          for (const err of uploadResult.errors) {
            warnings.push(`File upload failed for "${err.fileName}": ${err.error}`);
          }
        }

        // Create sprk_document records linked to the project
        if (uploadResult.uploadedFiles.length > 0) {
          const docResult = await entityService.createDocumentRecords(
            'sprk_projects',       // Parent entity set name
            projectId,             // Parent entity ID
            'sprk_Project',        // Navigation property on sprk_document
            uploadResult.uploadedFiles,
            {
              containerId: speContainerId,
              parentRecordName: projectName,
            }
          );

          if (docResult.warnings.length > 0) {
            warnings.push(...docResult.warnings);
          }
        }
      } catch (err) {
        const message = err instanceof Error ? err.message : 'File processing failed';
        warnings.push(`File pipeline error: ${message}`);
      }
    } else if (fileState.uploadedFiles.length > 0 && !speContainerId) {
      warnings.push('SPE container not configured — files were not uploaded to SharePoint Embedded.');
    }

    // 3. Execute follow-on actions
    // (For now, follow-on data is collected but not persisted to Dataverse —
    //  same pattern as Create Matter, where the MatterService handles this.)

    const hasWarnings = warnings.length > 0;

    const viewProject = () => {
      navigateToEntity({
        action: 'openRecord',
        entityName: 'sprk_project',
        entityId: projectId,
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
      title: hasWarnings ? 'Project created with warnings' : 'Project created!',
      body: (
        <Text size={300} style={{ color: tokens.colorNeutralForeground2 }}>
          <span style={{ color: tokens.colorBrandForeground1, fontWeight: 600 }}>
            &ldquo;{projectName}&rdquo;
          </span>{' '}
          has been created
          {hasWarnings
            ? ', though some operations could not complete. See details below.'
            : ' and is ready to use.'}
        </Text>
      ),
      actions: (
        <>
          <Button
            appearance="primary"
            onClick={viewProject}
            aria-label={`View project: ${projectName}`}
          >
            View Project
          </Button>
          <Button appearance="secondary" onClick={onClose}>
            Close
          </Button>
        </>
      ),
      warnings,
    };
  }, [
    webApi,
    speContainerId,
    formValues,
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
                Upload documents to associate with this project. The AI will extract
                key information to assist with project setup.
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
        canAdvance: () => formValid,
        renderContent: () => (
          <CreateProjectStep
            webApi={webApi}
            onValidChange={setFormValid}
            onFormValues={setFormValues}
            uploadedFiles={fileState.uploadedFiles}
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
      formValid,
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
      title="Create New Project"
      ariaLabel="Create New Project"
      steps={stepConfigs}
      onClose={onClose}
      onFinish={handleFinish}
      finishingLabel="Creating project…"
      finishLabel="Finish"
    />
  );
};

export default ProjectWizardDialog;
