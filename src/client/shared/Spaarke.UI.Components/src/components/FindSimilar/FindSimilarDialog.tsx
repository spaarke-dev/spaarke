/**
 * FindSimilarDialog.tsx
 * Two-step wizard dialog for "Find Similar".
 *
 * Uses WizardShell with 2 steps:
 *   0 — Upload file(s)   (FileUploadZone + UploadedFileList — from shared FileUpload)
 *   1 — Results           (FindSimilarResultsStep — tabbed grid of Documents / Matters / Projects)
 *
 * Shared library version — all external dependencies are injected via props:
 *   - IFindSimilarServiceConfig for BFF calls (authenticatedFetch, getBffBaseUrl)
 *   - IFilePreviewServices for the document preview dialog
 *   - onNavigateToEntity for Dataverse record navigation
 */
import * as React from "react";
import {
  Button,
  MessageBar,
  MessageBarBody,
  Text,
  makeStyles,
  tokens,
} from "@fluentui/react-components";
import { CheckmarkCircleFilled } from "@fluentui/react-icons";

import { WizardShell } from "../Wizard/WizardShell";
import type {
  IWizardStepConfig,
  IWizardSuccessConfig,
} from "../Wizard/wizardShellTypes";

import { FileUploadZone } from "../FileUpload/FileUploadZone";
import { UploadedFileList } from "../FileUpload/UploadedFileList";
import type {
  IUploadedFile,
  IFileValidationError,
} from "../FileUpload/fileUploadTypes";

import { FindSimilarResultsStep } from "./FindSimilarResultsStep";
import { runFindSimilar } from "./findSimilarService";
import type {
  IFindSimilarResults,
  FindSimilarStatus,
  IFindSimilarServiceConfig,
  INavigationMessage,
} from "./findSimilarTypes";
import type { IFilePreviewServices } from "../FilePreview/filePreviewTypes";

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface IFindSimilarDialogProps {
  open: boolean;
  onClose: () => void;
  /** Service configuration for BFF API calls (text extraction, search). */
  serviceConfig: IFindSimilarServiceConfig;
  /** Navigate to a Dataverse entity record (used by results grid). */
  onNavigateToEntity: (message: INavigationMessage) => void;
  /** Service callbacks for the FilePreviewDialog (preview URL, open links, etc.). */
  filePreviewServices: IFilePreviewServices;
}

// ---------------------------------------------------------------------------
// File state reducer (same pattern as SummarizeFilesDialog)
// ---------------------------------------------------------------------------

interface IFileState {
  uploadedFiles: IUploadedFile[];
  validationErrors: IFileValidationError[];
}

type FileAction =
  | { type: "ADD_FILES"; files: IUploadedFile[] }
  | { type: "REMOVE_FILE"; fileId: string }
  | { type: "SET_VALIDATION_ERRORS"; errors: IFileValidationError[] }
  | { type: "CLEAR_VALIDATION_ERRORS" }
  | { type: "RESET" };

function fileReducer(state: IFileState, action: FileAction): IFileState {
  switch (action.type) {
    case "ADD_FILES": {
      const existing = new Set(
        state.uploadedFiles.map((f) => `${f.name}::${f.sizeBytes}`),
      );
      const newFiles = action.files.filter(
        (f) => !existing.has(`${f.name}::${f.sizeBytes}`),
      );
      return {
        ...state,
        uploadedFiles: [...state.uploadedFiles, ...newFiles],
        validationErrors: [],
      };
    }
    case "REMOVE_FILE":
      return {
        ...state,
        uploadedFiles: state.uploadedFiles.filter(
          (f) => f.id !== action.fileId,
        ),
      };
    case "SET_VALIDATION_ERRORS":
      return { ...state, validationErrors: action.errors };
    case "CLEAR_VALIDATION_ERRORS":
      return { ...state, validationErrors: [] };
    case "RESET":
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
    display: "block",
    color: tokens.colorNeutralForeground1,
    marginBottom: tokens.spacingVerticalXS,
  },
  stepSubtitle: {
    display: "block",
    color: tokens.colorNeutralForeground3,
    marginBottom: tokens.spacingVerticalM,
  },
  errorBar: {
    flexShrink: 0,
  },
});

// ---------------------------------------------------------------------------
// FindSimilarDialog
// ---------------------------------------------------------------------------

export const FindSimilarDialog: React.FC<IFindSimilarDialogProps> = ({
  open,
  onClose,
  serviceConfig,
  onNavigateToEntity,
  filePreviewServices,
}) => {
  const styles = useStyles();

  // -- File state --
  const [fileState, fileDispatch] = React.useReducer(fileReducer, {
    uploadedFiles: [],
    validationErrors: [],
  });

  // -- Search state --
  const [searchStatus, setSearchStatus] =
    React.useState<FindSimilarStatus>("idle");
  const [searchResults, setSearchResults] =
    React.useState<IFindSimilarResults | null>(null);
  const [searchError, setSearchError] = React.useState<string | null>(null);
  const abortControllerRef = React.useRef<AbortController | null>(null);

  // -- Reset on open --
  React.useEffect(() => {
    if (open) {
      fileDispatch({ type: "RESET" });
      setSearchStatus("idle");
      setSearchResults(null);
      setSearchError(null);
    }
    return () => {
      abortControllerRef.current?.abort();
    };
  }, [open]);

  // -- File handlers --
  const handleFilesAccepted = React.useCallback(
    (files: IUploadedFile[]) => fileDispatch({ type: "ADD_FILES", files }),
    [],
  );
  const handleValidationErrors = React.useCallback(
    (errors: IFileValidationError[]) =>
      fileDispatch({ type: "SET_VALIDATION_ERRORS", errors }),
    [],
  );
  const handleRemoveFile = React.useCallback(
    (fileId: string) => fileDispatch({ type: "REMOVE_FILE", fileId }),
    [],
  );
  const handleClearErrors = React.useCallback(
    () => fileDispatch({ type: "CLEAR_VALIDATION_ERRORS" }),
    [],
  );

  // -- Run search --
  const runSearch = React.useCallback(async () => {
    if (fileState.uploadedFiles.length === 0) return;

    abortControllerRef.current?.abort();
    const controller = new AbortController();
    abortControllerRef.current = controller;

    setSearchStatus("loading");
    setSearchError(null);

    try {
      const result = await runFindSimilar(
        fileState.uploadedFiles,
        serviceConfig,
        controller.signal,
      );
      if (!controller.signal.aborted) {
        setSearchResults(result);
        setSearchStatus("success");
      }
    } catch (err: unknown) {
      if (!controller.signal.aborted) {
        const message =
          err instanceof Error ? err.message : "An unknown error occurred.";
        setSearchError(message);
        setSearchStatus("error");
      }
    }
  }, [fileState.uploadedFiles, serviceConfig]);

  // Auto-run search when entering Step 2
  const searchAttemptedRef = React.useRef(false);

  // Reset search flag when files change
  React.useEffect(() => {
    searchAttemptedRef.current = false;
    setSearchStatus("idle");
    setSearchResults(null);
    setSearchError(null);
  }, [fileState.uploadedFiles.length]);

  // -- handleFinish --
  const handleFinish =
    React.useCallback(async (): Promise<IWizardSuccessConfig> => {
      const currentResults = searchResults;
      const totalFound = currentResults
        ? currentResults.documentsTotalCount +
          currentResults.mattersTotalCount +
          currentResults.projectsTotalCount
        : 0;

      console.info(
        "[FindSimilarDialog] Finish with",
        totalFound,
        "total results",
      );

      return {
        icon: (
          <CheckmarkCircleFilled
            fontSize={64}
            style={{ color: tokens.colorPaletteGreenForeground1 }}
          />
        ),
        title: "Search Complete",
        body: (
          <Text size={300} style={{ color: tokens.colorNeutralForeground2 }}>
            Found {totalFound} similar item{totalFound !== 1 ? "s" : ""} across
            documents, matters, and projects.
          </Text>
        ),
        actions: (
          <Button appearance="secondary" onClick={onClose}>
            Close
          </Button>
        ),
      };
    }, [searchResults, onClose]);

  // -- Step configurations --

  const stepConfigs: IWizardStepConfig[] = React.useMemo(
    () => [
      // Step 0: Upload file(s)
      {
        id: "upload-files",
        label: "Upload file(s)",
        canAdvance: () => fileState.uploadedFiles.length > 0,
        renderContent: () => (
          <>
            <div>
              <Text
                as="h2"
                size={500}
                weight="semibold"
                className={styles.stepTitle}
              >
                Upload file(s)
              </Text>
              <Text size={200} className={styles.stepSubtitle}>
                Upload one or more documents. The AI will extract text and
                search for similar documents, matters, and projects.
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
              <UploadedFileList
                files={fileState.uploadedFiles}
                onRemove={handleRemoveFile}
              />
            )}
          </>
        ),
      },

      // Step 1: Results
      {
        id: "results",
        label: "Results",
        canAdvance: () => searchStatus === "success" && searchResults !== null,
        isEarlyFinish: () =>
          searchStatus === "success" && searchResults !== null,
        renderContent: () => {
          // Auto-trigger search on first render of this step
          if (!searchAttemptedRef.current && searchStatus === "idle") {
            searchAttemptedRef.current = true;
            Promise.resolve().then(() => runSearch());
          }

          return (
            <FindSimilarResultsStep
              status={searchStatus}
              results={searchResults}
              errorMessage={searchError}
              onRetry={runSearch}
              onNavigateToEntity={onNavigateToEntity}
              filePreviewServices={filePreviewServices}
            />
          );
        },
      },
    ],
    [
      fileState.uploadedFiles,
      fileState.validationErrors,
      searchStatus,
      searchResults,
      searchError,
      styles,
      handleFilesAccepted,
      handleValidationErrors,
      handleRemoveFile,
      handleClearErrors,
      runSearch,
      onNavigateToEntity,
      filePreviewServices,
    ],
  );

  // -- Render --

  return (
    <WizardShell
      open={open}
      title="Find Similar Records"
      ariaLabel="Find Similar Records"
      steps={stepConfigs}
      onClose={onClose}
      onFinish={handleFinish}
      finishingLabel="Processing&hellip;"
      finishLabel="Done"
    />
  );
};

// Default export enables React.lazy() dynamic import for bundle-size optimization.
export default FindSimilarDialog;
