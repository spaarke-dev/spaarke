/**
 * types.ts
 * Wizard-specific type definitions for the Document Upload Wizard.
 *
 * Re-exports shared file upload types from @spaarke/ui-components and adds
 * domain-specific types for the upload wizard's state management.
 *
 * @see ADR-007 - Document access through BFF API (SpeFileStore facade)
 */

import type { IUploadedFile, IFileValidationError } from "@spaarke/ui-components/components/FileUpload";

// ---------------------------------------------------------------------------
// Re-exports from shared library (convenience for wizard consumers)
// ---------------------------------------------------------------------------

export type { IUploadedFile, IFileValidationError };

// ---------------------------------------------------------------------------
// Upload status tracking (per-file)
// ---------------------------------------------------------------------------

/** Upload progress state for an individual file. */
export type UploadStatus = "pending" | "uploading" | "completed" | "failed";

/** Tracks upload progress for a single file. */
export interface IFileUploadProgress {
    /** Matches the IUploadedFile.id for correlation. */
    fileId: string;
    /** Current upload status. */
    status: UploadStatus;
    /** Upload progress percentage (0-100). Only meaningful when status is "uploading". */
    progressPercent: number;
    /** Error message when status is "failed". */
    errorMessage?: string;
}

// ---------------------------------------------------------------------------
// File state (managed by useReducer in DocumentUploadWizardDialog)
// ---------------------------------------------------------------------------

/** Domain state for selected files and their upload progress. */
export interface IFileState {
    /** Files accepted via drag-and-drop or browse. */
    selectedFiles: IUploadedFile[];
    /** Validation errors from the most recent file selection attempt. */
    validationErrors: IFileValidationError[];
    /** Per-file upload progress (populated when upload begins). */
    uploadProgress: IFileUploadProgress[];
}

/** Discriminated union of actions for the file state reducer. */
export type FileAction =
    | { type: "ADD_FILES"; files: IUploadedFile[] }
    | { type: "REMOVE_FILE"; fileId: string }
    | { type: "SET_VALIDATION_ERRORS"; errors: IFileValidationError[] }
    | { type: "CLEAR_VALIDATION_ERRORS" }
    | { type: "START_UPLOAD" }
    | { type: "UPDATE_PROGRESS"; fileId: string; progressPercent: number }
    | { type: "UPLOAD_FILE_COMPLETED"; fileId: string }
    | { type: "UPLOAD_FILE_FAILED"; fileId: string; errorMessage: string }
    | { type: "RESET" };

// ---------------------------------------------------------------------------
// Wizard-level state (for Step 2 and Step 3 — placeholder for future tasks)
// ---------------------------------------------------------------------------

/** Summary results displayed on Step 2 (populated after upload completes). */
export interface ISummaryResults {
    /** Total number of files uploaded successfully. */
    successCount: number;
    /** Total number of files that failed to upload. */
    failureCount: number;
    /** Total bytes uploaded. */
    totalBytesUploaded: number;
}

/** Available next-step action IDs for Step 3. */
export type NextStepActionId = "send-email" | "work-on-analysis" | "find-similar";

// ---------------------------------------------------------------------------
// Component props
// ---------------------------------------------------------------------------

/** Props for the DocumentUploadWizardDialog component. */
export interface IDocumentUploadWizardDialogProps {
    /** Dataverse entity type of the parent record (e.g., "sprk_document"). */
    parentEntityType: string;
    /** ID of the parent record. */
    parentEntityId: string;
    /** Display name of the parent record. */
    parentEntityName: string;
    /** SPE container ID for file uploads. */
    containerId: string;
    /** Callback invoked when the wizard is closed or cancelled. */
    onClose: () => void;
}

// ---------------------------------------------------------------------------
// Standalone mode (AssociateToStep resolution)
// ---------------------------------------------------------------------------

/** Resolved parent context from the AssociateToStep (standalone mode). */
export interface IResolvedParentContext {
    /** Parent entity logical name (e.g., "sprk_matter"). Empty string if unassociated. */
    parentEntityType: string;
    /** Parent record GUID. Empty string if unassociated. */
    parentEntityId: string;
    /** Parent record display name. Empty string if unassociated. */
    parentEntityName: string;
    /** SPE container ID — always resolved (from record or business unit). */
    containerId: string;
    /** Whether this is an unassociated upload (no parent record). */
    isUnassociated: boolean;
}

// ---------------------------------------------------------------------------
// Component props
// ---------------------------------------------------------------------------

/** Props for the AddFilesStep component. */
export interface IAddFilesStepProps {
    /** Currently selected files. */
    files: IUploadedFile[];
    /** Callback when new files are accepted via drag-and-drop or browse. */
    onFilesAdded: (files: IUploadedFile[]) => void;
    /** Callback when a file is removed from the list. */
    onFileRemoved: (fileId: string) => void;
    /** Display name of the parent entity (shown in "Related To" info bar). */
    parentEntityName: string;
    /** Entity type of the parent record (shown in "Related To" info bar). */
    parentEntityType: string;
    /** Validation errors from the most recent file selection attempt. */
    validationErrors?: IFileValidationError[];
    /** Callback to clear validation errors (e.g., on user interaction). */
    onClearErrors?: () => void;
    /** Whether this is an unassociated upload (standalone mode, no parent). */
    isUnassociated?: boolean;
}
