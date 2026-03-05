/**
 * ProjectWizardDialog.tsx
 * Multi-step wizard dialog for "Create New Project".
 *
 * Steps mirror the Create New Matter wizard:
 *   0 — Add file(s)       (FileUploadZone + UploadedFileList)
 *   1 — Create record     (CreateProjectStep)
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
import { WizardShell } from '../Wizard';
import type { IWizardStepConfig, IWizardSuccessConfig } from '../Wizard';
import { FileUploadZone } from '../CreateMatter/FileUploadZone';
import { UploadedFileList } from '../CreateMatter/UploadedFileList';
import type { IUploadedFile, IFileValidationError } from '../CreateMatter/wizardTypes';
import { CreateProjectStep } from './CreateProjectStep';
import { ProjectService } from './projectService';
import { EMPTY_PROJECT_FORM } from './projectFormTypes';
import type { ICreateProjectFormState } from './projectFormTypes';
import { navigateToEntity } from '../../utils/navigation';
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
// ProjectWizardDialog
// ---------------------------------------------------------------------------

const ProjectWizardDialog: React.FC<IProjectWizardDialogProps> = ({ open, onClose, webApi }) => {
  const styles = useStyles();
  const [formValid, setFormValid] = React.useState(false);
  const [formValues, setFormValues] = React.useState<ICreateProjectFormState>(EMPTY_PROJECT_FORM);
  const serviceRef = React.useRef(new ProjectService(webApi));

  // File state
  const [fileState, fileDispatch] = React.useReducer(fileReducer, {
    uploadedFiles: [],
    validationErrors: [],
  });

  // Reset on open
  React.useEffect(() => {
    if (open) {
      setFormValid(false);
      setFormValues(EMPTY_PROJECT_FORM);
      fileDispatch({ type: 'RESET' });
      serviceRef.current = new ProjectService(webApi);
    }
  }, [open, webApi]);

  // File handler callbacks
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

  const stepConfigs: IWizardStepConfig[] = React.useMemo(() => [
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
        />
      ),
    },
  ], [
    webApi,
    formValid,
    fileState.uploadedFiles,
    fileState.validationErrors,
    styles,
    handleFilesAccepted,
    handleValidationErrors,
    handleRemoveFile,
    handleClearErrors,
  ]);

  const handleFinish = React.useCallback(async (): Promise<IWizardSuccessConfig> => {
    const result = await serviceRef.current.createProject(formValues);
    if (!result.success) {
      throw new Error(result.errorMessage ?? 'Failed to create project');
    }
    return {
      icon: <CheckmarkCircleFilled fontSize={64} style={{ color: tokens.colorPaletteGreenForeground1 }} />,
      title: 'Project created!',
      body: (
        <Text size={300} style={{ color: tokens.colorNeutralForeground2 }}>
          <span style={{ color: tokens.colorBrandForeground1, fontWeight: tokens.fontWeightSemibold }}>
            &ldquo;{result.projectName}&rdquo;
          </span>{' '}
          has been created and is ready to use.
        </Text>
      ),
      actions: (
        <>
          <Button
            appearance="primary"
            onClick={() => {
              navigateToEntity({ action: 'openRecord', entityName: 'sprk_project', entityId: result.projectId });
              onClose();
            }}
          >
            View Project
          </Button>
          <Button appearance="secondary" onClick={onClose}>Close</Button>
        </>
      ),
    };
  }, [formValues, onClose]);

  return (
    <WizardShell
      open={open}
      title="Create New Project"
      ariaLabel="Create New Project"
      steps={stepConfigs}
      onClose={onClose}
      onFinish={handleFinish}
      finishingLabel="Creating project…"
      finishLabel="Create"
    />
  );
};

export default ProjectWizardDialog;
