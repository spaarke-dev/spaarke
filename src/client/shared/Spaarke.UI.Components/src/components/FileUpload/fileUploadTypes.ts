/**
 * fileUploadTypes.ts
 * Type definitions for the FileUpload shared components.
 *
 * These types are generic and domain-agnostic — no wizard or matter-specific
 * concepts. Consumers provide their own validation logic via callbacks.
 */

// ---------------------------------------------------------------------------
// File classification
// ---------------------------------------------------------------------------

/** MIME types recognized by the default file upload validation. */
export type AcceptedMimeType =
  | "application/pdf"
  | "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
  | "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

/** Category derived from accepted file type — drives icon selection. */
export type UploadedFileType = "pdf" | "docx" | "xlsx";

// ---------------------------------------------------------------------------
// Data models
// ---------------------------------------------------------------------------

/** A validated file that has been accepted into the upload list. */
export interface IUploadedFile {
  /** Stable unique identifier (generated on acceptance). */
  id: string;
  /** Original file name. */
  name: string;
  /** File size in bytes. */
  sizeBytes: number;
  /** Derived file type for icon / display purposes. */
  fileType: UploadedFileType;
  /** The underlying browser File object (retained for upload). */
  file: File;
}

/** A validation failure produced during file acceptance. */
export interface IFileValidationError {
  /** File name that failed validation. */
  fileName: string;
  /** Human-readable reason for rejection. */
  reason: string;
}

// ---------------------------------------------------------------------------
// Validation configuration
// ---------------------------------------------------------------------------

/**
 * Optional validation configuration for FileUploadZone.
 * All fields are optional — sensible defaults are used when omitted.
 */
export interface IFileValidationConfig {
  /** Maximum file size in bytes. Defaults to 10 MB. */
  maxFileSizeBytes?: number;
  /** Allowed file extensions (with leading dot, lower-cased). Defaults to ['.pdf', '.docx', '.xlsx']. */
  acceptedExtensions?: string[];
  /** HTML accept attribute value for the file input. Derived from acceptedExtensions if not provided. */
  inputAccept?: string;
  /**
   * Custom validation function applied after built-in checks pass.
   * Return null/undefined for success, or an error reason string for failure.
   */
  customValidator?: (file: File) => string | null | undefined;
}

// ---------------------------------------------------------------------------
// Component prop interfaces
// ---------------------------------------------------------------------------

export interface IFileUploadZoneProps {
  /** Called when the user drops or browses valid files. */
  onFilesAccepted: (files: IUploadedFile[]) => void;
  /** Called when validation errors occur (invalid type / size). */
  onValidationErrors: (errors: IFileValidationError[]) => void;
  /** Optional validation configuration overrides. */
  validationConfig?: IFileValidationConfig;
  /** Whether the upload zone is disabled. */
  disabled?: boolean;
}

export interface IUploadedFileListProps {
  /** Accepted files to display. */
  files: IUploadedFile[];
  /** Called when the user clicks the remove button on a file row. */
  onRemove: (fileId: string) => void;
  /** Whether remove buttons should be disabled. */
  disabled?: boolean;
}
