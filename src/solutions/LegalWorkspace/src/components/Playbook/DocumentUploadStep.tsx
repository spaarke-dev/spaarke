/**
 * DocumentUploadStep.tsx
 * Wizard step that composes FileUploadZone and UploadedFileList for document
 * selection as part of the Playbook Quick Start wizard flow.
 *
 * Bridges the IUploadedFile-based CreateMatter components to the simpler
 * File[]-based onFilesReady callback expected by the wizard orchestrator.
 *
 * Zero new upload logic — all drag-drop, validation, and list rendering is
 * delegated to the existing CreateMatter components.
 */
import React from "react";
import {
  makeStyles,
  tokens,
  Text,
  MessageBar,
  MessageBarBody,
  MessageBarTitle,
} from "@fluentui/react-components";
import { FileUploadZone } from "../CreateMatter/FileUploadZone";
import { UploadedFileList } from "../CreateMatter/UploadedFileList";
import type {
  IUploadedFile,
  IFileValidationError,
} from "../CreateMatter/wizardTypes";

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalM,
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
  },
  heading: {
    color: tokens.colorNeutralForeground1,
  },
  subheading: {
    color: tokens.colorNeutralForeground3,
    marginTop: tokens.spacingVerticalXS,
  },
  fileListSection: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalXS,
  },
  fileListLabel: {
    color: tokens.colorNeutralForeground2,
  },
  errorList: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalXS,
  },
});

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface IDocumentUploadStepProps {
  /**
   * MIME types and/or extensions accepted by the upload zone.
   * Passed as a hint to the parent; the underlying FileUploadZone enforces
   * its own internal validation (PDF, DOCX, XLSX) regardless.
   * Defaults to ["application/pdf", ".docx", ".xlsx"].
   */
  accept?: string[];
  /**
   * Whether multiple files can be selected at once.
   * Defaults to true.
   */
  multiple?: boolean;
  /**
   * Called whenever the file selection changes (file added or removed).
   * Receives the current full list of accepted File objects.
   */
  onFilesReady: (files: File[]) => void;
  /**
   * Maximum file size in MB. Displayed in the UI as a hint.
   * Note: enforcement is handled by FileUploadZone (hardcoded at 10 MB).
   * Defaults to 10.
   */
  maxSizeMB?: number;
}

// ---------------------------------------------------------------------------
// Utilities
// ---------------------------------------------------------------------------

// ---------------------------------------------------------------------------
// DocumentUploadStep
// ---------------------------------------------------------------------------

export const DocumentUploadStep: React.FC<IDocumentUploadStepProps> = ({
  accept = ["application/pdf", ".docx", ".xlsx"],
  multiple = true,
  onFilesReady,
  maxSizeMB = 10,
}) => {
  const styles = useStyles();

  // IUploadedFile[] is the internal representation used by the composed
  // CreateMatter components. We maintain this state here and derive File[]
  // for the onFilesReady callback.
  const [uploadedFiles, setUploadedFiles] = React.useState<IUploadedFile[]>([]);
  const [validationErrors, setValidationErrors] = React.useState<
    IFileValidationError[]
  >([]);

  // -------------------------------------------------------------------------
  // Handlers — forwarded to FileUploadZone
  // -------------------------------------------------------------------------

  const handleFilesAccepted = React.useCallback(
    (newFiles: IUploadedFile[]) => {
      setValidationErrors([]);

      setUploadedFiles((prev) => {
        // When multiple=false keep only the latest single file; otherwise append.
        const next = multiple ? [...prev, ...newFiles] : newFiles.slice(-1);
        onFilesReady(next.map((f) => f.file));
        return next;
      });
    },
    [multiple, onFilesReady]
  );

  const handleValidationErrors = React.useCallback(
    (errors: IFileValidationError[]) => {
      setValidationErrors(errors);
    },
    []
  );

  // -------------------------------------------------------------------------
  // Handler — forwarded to UploadedFileList
  // -------------------------------------------------------------------------

  const handleRemoveFile = React.useCallback(
    (fileId: string) => {
      setUploadedFiles((prev) => {
        const next = prev.filter((f) => f.id !== fileId);
        onFilesReady(next.map((f) => f.file));
        return next;
      });
    },
    [onFilesReady]
  );

  // -------------------------------------------------------------------------
  // Render
  // -------------------------------------------------------------------------

  const acceptedTypesLabel = accept.join(", ");

  return (
    <div className={styles.root}>
      {/* Section heading */}
      <div>
        <Text weight="semibold" size={400} className={styles.heading}>
          Upload Documents
        </Text>
        <Text
          as="p"
          size={200}
          block
          className={styles.subheading}
        >
          Accepted file types: {acceptedTypesLabel}. Maximum {maxSizeMB} MB per
          file.
        </Text>
      </div>

      {/* Drag-and-drop zone — reuses CreateMatter component unchanged */}
      <FileUploadZone
        onFilesAccepted={handleFilesAccepted}
        onValidationErrors={handleValidationErrors}
      />

      {/* Validation error messages */}
      {validationErrors.length > 0 && (
        <div className={styles.errorList} role="alert" aria-live="polite">
          {validationErrors.map((err) => (
            <MessageBar
              key={`${err.fileName}-${err.reason}`}
              intent="error"
            >
              <MessageBarBody>
                <MessageBarTitle>{err.fileName}</MessageBarTitle>
                {err.reason}
              </MessageBarBody>
            </MessageBar>
          ))}
        </div>
      )}

      {/* Accepted file list — reuses CreateMatter component unchanged */}
      {uploadedFiles.length > 0 && (
        <div className={styles.fileListSection}>
          <Text size={200} className={styles.fileListLabel}>
            {uploadedFiles.length === 1
              ? "1 file selected"
              : `${uploadedFiles.length} files selected`}
          </Text>
          <UploadedFileList
            files={uploadedFiles}
            onRemove={handleRemoveFile}
          />
        </div>
      )}
    </div>
  );
};
