/**
 * formTypes.ts
 * Form state types and interfaces for Create New Matter — Step 2 (Create Record).
 *
 * Covers:
 *   - ICreateMatterFormState: full form field values
 *   - ICreateMatterFormErrors: field-level validation error messages
 *   - IAiPrefillFields: shape of the BFF pre-fill response
 *   - IAiPrefillStatus: loading / success / error / idle for AI pre-fill lifecycle
 *   - FormAction: discriminated union for useReducer
 *   - ICreateRecordStepProps: props accepted by CreateRecordStep
 */

// ---------------------------------------------------------------------------
// Option enumerations
// ---------------------------------------------------------------------------

/** Allowed values for the Matter Type dropdown. */
export type MatterType =
  | 'Litigation'
  | 'Transaction'
  | 'Advisory'
  | 'Regulatory'
  | 'IP'
  | 'Employment'
  | '';

/** Allowed values for the Practice Area dropdown. */
export type PracticeArea =
  | 'Corporate'
  | 'Real Estate'
  | 'IP'
  | 'Employment'
  | 'Litigation'
  | 'Tax'
  | 'Environmental'
  | '';

// ---------------------------------------------------------------------------
// Form state
// ---------------------------------------------------------------------------

/** Mutable form field values managed by useReducer. */
export interface ICreateMatterFormState {
  /** Matter Type (required). */
  matterType: MatterType;
  /** Matter Name — free text (required). */
  matterName: string;
  /** Estimated Budget — numeric string; empty string when not set. */
  estimatedBudget: string;
  /** Practice Area (required). */
  practiceArea: PracticeArea;
  /** Organization — free text (required). */
  organization: string;
  /** Key Parties — free text, multi-line. */
  keyParties: string;
  /** Summary — free text, multi-line. */
  summary: string;
}

/** Field-level validation error messages (undefined = no error). */
export interface ICreateMatterFormErrors {
  matterType?: string;
  matterName?: string;
  practiceArea?: string;
  organization?: string;
}

// ---------------------------------------------------------------------------
// AI pre-fill
// ---------------------------------------------------------------------------

/**
 * Subset of ICreateMatterFormState that the BFF AI pre-fill endpoint may
 * populate.  All fields are optional — the BFF returns only what it could
 * confidently extract.
 */
export interface IAiPrefillFields {
  matterType?: MatterType;
  matterName?: string;
  estimatedBudget?: string;
  practiceArea?: PracticeArea;
  organization?: string;
  keyParties?: string;
  summary?: string;
}

/**
 * Status of the AI pre-fill call lifecycle.
 *   - idle     : not yet triggered (no files, or hasn't mounted)
 *   - loading  : BFF call in flight; fields show skeleton
 *   - success  : BFF responded; some or all fields may have been pre-filled
 *   - error    : BFF call failed; form shows empty defaults (graceful fallback)
 */
export type AiPrefillStatus = 'idle' | 'loading' | 'success' | 'error';

/** Complete AI pre-fill state tracked alongside the form. */
export interface IAiPrefillState {
  /** Lifecycle status. */
  status: AiPrefillStatus;
  /**
   * Set of field names that were populated by the AI pre-fill call.
   * Used to decide which labels should show the sparkle "AI" tag.
   */
  prefilledFields: Set<keyof ICreateMatterFormState>;
}

// ---------------------------------------------------------------------------
// Reducer actions
// ---------------------------------------------------------------------------

/**
 * Discriminated union of actions accepted by the form's useReducer.
 * Each action maps to a specific mutation of ICreateMatterFormState or
 * IAiPrefillState.
 */
export type FormAction =
  /** Update a single text field. */
  | {
      type: 'SET_FIELD';
      field: keyof ICreateMatterFormState;
      value: string;
    }
  /** Bulk-apply AI pre-filled values and record which fields were set. */
  | {
      type: 'APPLY_AI_PREFILL';
      fields: IAiPrefillFields;
    }
  /** Mark AI pre-fill call as in-flight. */
  | { type: 'AI_PREFILL_LOADING' }
  /** Mark AI pre-fill call as completed (may have partially-filled fields). */
  | { type: 'AI_PREFILL_SUCCESS' }
  /** Mark AI pre-fill call as failed; leave form fields empty. */
  | { type: 'AI_PREFILL_ERROR' }
  /** Clear field-level validation errors. */
  | { type: 'CLEAR_ERRORS' };

// ---------------------------------------------------------------------------
// BFF request / response types
// ---------------------------------------------------------------------------

/**
 * Multipart-compatible request payload sent to
 * POST /api/workspace/matters/pre-fill.
 *
 * The BFF endpoint accepts a JSON body with file identifiers.  The actual
 * File objects from Step 1 are uploaded separately or referenced by the
 * session context; here we send their names as context hints.
 */
export interface IAiPrefillRequest {
  /** File names from the Step 1 upload, used as context hints by the BFF. */
  fileNames: string[];
}

/**
 * Expected JSON shape returned by
 * POST /api/workspace/matters/pre-fill.
 */
export interface IAiPrefillResponse {
  /** Extracted / inferred field values. Only present fields were extracted. */
  fields: IAiPrefillFields;
}

// ---------------------------------------------------------------------------
// Component prop types
// ---------------------------------------------------------------------------

export interface ICreateRecordStepProps {
  /**
   * Files uploaded in Step 1.  Passed to CreateRecordStep so it can trigger
   * the BFF AI pre-fill call on mount when files are present.
   */
  uploadedFileNames: string[];

  /**
   * Called by the step when form validity changes.  The parent wizard uses
   * this to enable / disable the Next button.
   */
  onValidChange: (isValid: boolean) => void;

  /**
   * Called by the parent wizard just before advancing to Step 3, so the
   * wizard can store the final form values in its own state for task 024.
   */
  onSubmit: (values: ICreateMatterFormState) => void;
}
