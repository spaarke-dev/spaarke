/**
 * formTypes.ts
 * Form state types and interfaces for Create New Matter — Step 2 (Create Record).
 *
 * Covers:
 *   - ICreateMatterFormState: full form field values (lookup-based)
 *   - ICreateMatterFormErrors: field-level validation error messages
 *   - IAiPrefillFields: shape of the BFF pre-fill response
 *   - IAiPrefillStatus: loading / success / error / idle for AI pre-fill lifecycle
 *   - FormAction: discriminated union for useReducer
 *   - ICreateRecordStepProps: props accepted by CreateRecordStep
 */

import type { IWebApi } from '../../types/xrm';
import type { IUploadedFile } from './wizardTypes';

// ---------------------------------------------------------------------------
// Form state
// ---------------------------------------------------------------------------

/** Mutable form field values managed by useReducer. */
export interface ICreateMatterFormState {
  /** sprk_mattertype_ref lookup — GUID of the selected record. */
  matterTypeId: string;
  /** Display name of the selected matter type. */
  matterTypeName: string;
  /** sprk_practicearea_ref lookup — GUID of the selected record. */
  practiceAreaId: string;
  /** Display name of the selected practice area. */
  practiceAreaName: string;
  /** Matter Name — free text (required). Maps to sprk_name. */
  matterName: string;
  /** Assigned Attorney — contact GUID. */
  assignedAttorneyId: string;
  /** Display name of the assigned attorney. */
  assignedAttorneyName: string;
  /** Assigned Paralegal — contact GUID. */
  assignedParalegalId: string;
  /** Display name of the assigned paralegal. */
  assignedParalegalName: string;
  /** Summary / Description — free text, multi-line. Maps to sprk_description. */
  summary: string;
}

/** Field-level validation error messages (undefined = no error). */
export interface ICreateMatterFormErrors {
  matterTypeId?: string;
  practiceAreaId?: string;
  matterName?: string;
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
  matterTypeId?: string;
  matterTypeName?: string;
  practiceAreaId?: string;
  practiceAreaName?: string;
  matterName?: string;
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
  /** Set a lookup field (id + name pair). */
  | {
      type: 'SET_LOOKUP';
      idField: keyof ICreateMatterFormState;
      nameField: keyof ICreateMatterFormState;
      id: string;
      name: string;
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
  /** Xrm.WebApi reference for Dataverse lookup queries. */
  webApi: IWebApi;

  /**
   * Files uploaded in Step 1.  Passed to CreateRecordStep so it can trigger
   * the BFF AI pre-fill call on mount when files are present.
   */
  uploadedFileNames: string[];

  /**
   * Actual uploaded file objects from Step 1. Needed for multipart/form-data
   * upload to the BFF AI pre-fill endpoint.
   */
  uploadedFiles: IUploadedFile[];

  /**
   * Called by the step when form validity changes.  The parent wizard uses
   * this to enable / disable the Next button.
   */
  onValidChange: (isValid: boolean) => void;

  /**
   * Called by the parent wizard just before advancing to Step 3, so the
   * wizard can store the final form values in its own state.
   */
  onSubmit: (values: ICreateMatterFormState) => void;
}
