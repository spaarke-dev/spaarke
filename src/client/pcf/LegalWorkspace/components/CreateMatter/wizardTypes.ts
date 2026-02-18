/**
 * wizardTypes.ts
 * Type definitions for the Create New Matter multi-step wizard dialog.
 */

// ---------------------------------------------------------------------------
// Wizard step definitions
// ---------------------------------------------------------------------------

/** Unique identifiers for each step in the wizard. */
export type WizardStepId =
  | 'add-files'
  | 'create-record'
  | 'next-steps'
  | string; // allows dynamic follow-on steps added in task 024

/** Status of a wizard step from the sidebar stepper's perspective. */
export type WizardStepStatus = 'pending' | 'active' | 'completed';

/** Descriptor for a single step shown in the sidebar. */
export interface IWizardStep {
  /** Unique identifier, used as key and for routing step content. */
  id: WizardStepId;
  /** Display label shown in the sidebar. */
  label: string;
  /** Whether the step is currently active / completed / pending. */
  status: WizardStepStatus;
}

// ---------------------------------------------------------------------------
// File upload types
// ---------------------------------------------------------------------------

/** MIME types accepted by the file upload zone. */
export type AcceptedMimeType =
  | 'application/pdf'
  | 'application/vnd.openxmlformats-officedocument.wordprocessingml.document'
  | 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet';

/** Category derived from accepted file type — drives the icon selection. */
export type UploadedFileType = 'pdf' | 'docx' | 'xlsx';

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
  /** The underlying browser File object (retained for upload in step 2+). */
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
// Wizard state
// ---------------------------------------------------------------------------

/** Immutable shape of the wizard's useReducer state. */
export interface IWizardState {
  /** Index of the currently visible step (0-based into `steps` array). */
  currentStepIndex: number;
  /** Ordered step descriptors — includes dynamic follow-on steps. */
  steps: IWizardStep[];
  /** Files accepted in Step 1. */
  uploadedFiles: IUploadedFile[];
  /** Latest batch of validation errors (cleared on next drop / browse). */
  validationErrors: IFileValidationError[];
}

// ---------------------------------------------------------------------------
// Wizard reducer actions
// ---------------------------------------------------------------------------

export type WizardAction =
  | { type: 'NEXT_STEP' }
  | { type: 'PREV_STEP' }
  | { type: 'GO_TO_STEP'; stepIndex: number }
  | { type: 'ADD_FILES'; files: IUploadedFile[] }
  | { type: 'REMOVE_FILE'; fileId: string }
  | { type: 'SET_VALIDATION_ERRORS'; errors: IFileValidationError[] }
  | { type: 'CLEAR_VALIDATION_ERRORS' }
  | { type: 'ADD_DYNAMIC_STEP'; step: IWizardStep }
  | { type: 'REMOVE_DYNAMIC_STEP'; stepId: string };

// ---------------------------------------------------------------------------
// Component prop interfaces
// ---------------------------------------------------------------------------

export interface IWizardDialogProps {
  /** Whether the dialog is currently open. */
  open: boolean;
  /** Callback invoked when the user clicks Cancel or closes the dialog. */
  onClose: () => void;
}

export interface IWizardStepperProps {
  /** Ordered step descriptors to render in the sidebar. */
  steps: IWizardStep[];
}

export interface IFileUploadZoneProps {
  /** Called when the user drops or browses valid files. */
  onFilesAccepted: (files: IUploadedFile[]) => void;
  /** Called when validation errors occur (invalid type / size). */
  onValidationErrors: (errors: IFileValidationError[]) => void;
}

export interface IUploadedFileListProps {
  /** Accepted files to display. */
  files: IUploadedFile[];
  /** Called when the user clicks the remove button on a file row. */
  onRemove: (fileId: string) => void;
}
