/**
 * types.ts
 * Shared type definitions for the CreateDocument wizard Code Page.
 */

// ---------------------------------------------------------------------------
// File upload types
// ---------------------------------------------------------------------------

/** MIME types accepted by the file upload zone. */
export type AcceptedMimeType =
    | "application/pdf"
    | "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
    | "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

/** Category derived from accepted file type. */
export type UploadedFileType = "pdf" | "docx" | "xlsx";

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
    /** Upload progress (0-100). Undefined means not yet started. */
    progress?: number;
    /** Upload status. */
    uploadStatus?: "pending" | "uploading" | "complete" | "error";
    /** Error message if upload failed. */
    uploadError?: string;
}

/** A validation failure produced during file acceptance. */
export interface IFileValidationError {
    /** File name that failed validation. */
    fileName: string;
    /** Human-readable reason for rejection. */
    reason: string;
}

// ---------------------------------------------------------------------------
// Document form types
// ---------------------------------------------------------------------------

/** Document type options for the combobox. */
export type DocumentType =
    | "contract"
    | "agreement"
    | "memo"
    | "brief"
    | "correspondence"
    | "report"
    | "other";

/** Form values for the Document Details step. */
export interface IDocumentFormValues {
    /** Display name for the document. */
    name: string;
    /** Document type classification. */
    documentType: DocumentType | "";
    /** Free-text description. */
    description: string;
}

// ---------------------------------------------------------------------------
// Next steps types
// ---------------------------------------------------------------------------

/** IDs for optional follow-on actions. */
export type NextStepActionId =
    | "run-analysis"
    | "share-document"
    | "create-task";

/** Wizard step configuration interface (simplified for this Code Page). */
export interface IWizardStepConfig {
    /** Unique step identifier. */
    id: string;
    /** Display label for stepper. */
    label: string;
    /** Whether the user can advance past this step. */
    canAdvance: () => boolean;
}

// ---------------------------------------------------------------------------
// Upload result types
// ---------------------------------------------------------------------------

/** Result from the file upload service. */
export interface IUploadResult {
    /** Whether the upload succeeded. */
    success: boolean;
    /** File name that was uploaded. */
    fileName: string;
    /** SPE drive item ID on success. */
    driveItemId?: string;
    /** Error message on failure. */
    error?: string;
}

/** Result from the document record creation. */
export interface ICreateDocumentResult {
    /** Whether the record was created. */
    success: boolean;
    /** Dataverse record ID on success. */
    documentId?: string;
    /** Error message on failure. */
    error?: string;
}
