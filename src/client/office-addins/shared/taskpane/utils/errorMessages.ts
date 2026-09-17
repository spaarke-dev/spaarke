/**
 * Error message mapping for OFFICE_* error codes.
 *
 * Maps API error codes to user-friendly messages per spec.md Error Code Catalog.
 * Messages are actionable where possible (e.g., "Please select an entity..."
 * rather than "Association required").
 *
 * Error format: ProblemDetails (RFC 7807)
 */

/**
 * ProblemDetails error response from BFF API.
 * Per ADR-019 and spec.md.
 */
export interface ProblemDetails {
  type: string;
  title: string;
  status: number;
  detail: string;
  instance?: string;
  correlationId?: string;
  errorCode?: string;
  /**
   * Authorization reason code. Present on 403s issued by the BFF authorization filters
   * (`ProblemDetailsHelper.Forbidden`), which carry no `errorCode`.
   */
  reasonCode?: string;
  errors?: Record<string, string[]>;
  /**
   * Task 025 (OFFICE_020 name-collision only): the file name that collided.
   */
  fileName?: string;
  /**
   * Task 025 (OFFICE_020 name-collision only): the `sprk_document` that already holds `fileName` in the
   * target drive, when the server could resolve it (Document saves only). Absent when the collision has
   * no owning document, or the lookup was unavailable — in which case only "Keep both" is offered.
   */
  existingDocumentId?: string;
}

/**
 * Notification type for toast display.
 */
export type NotificationType = 'success' | 'warning' | 'error' | 'info';

/**
 * User-friendly error message with type.
 */
export interface ErrorMessage {
  title: string;
  message: string;
  type: NotificationType;
  /** Whether this error is recoverable (user can retry) */
  recoverable: boolean;
  /** Suggested action for the user */
  action?: string;
  /**
   * The failed save was a VERSION save (FR-11) that the server refused for a reason tied to the existing
   * document it named — so "Save as new document" is a way forward, and the pane offers it (task 024).
   */
  offerSaveAsNew?: boolean;
  /**
   * Task 025: the failed save was a CREATE refused on a filename collision (OFFICE_020) — the pane offers
   * the two-option choice ("Keep both" always; "Save as new version" only when `collisionExistingDocumentId`
   * is present) instead of the generic error actions.
   */
  offerCollisionChoice?: boolean;
  /** Task 025: the file name that collided — set only when `offerCollisionChoice` is true. */
  collisionFileName?: string;
  /**
   * Task 025: the existing document to retry as a version save of, when the server could resolve one.
   * Absent → only "Keep both" is offered.
   */
  collisionExistingDocumentId?: string;
}

/**
 * Error code catalog from spec.md.
 * Maps OFFICE_* codes to user-friendly messages.
 */
const ERROR_CODE_MAP: Record<string, ErrorMessage> = {
  // Validation errors (400)
  OFFICE_001: {
    title: 'Invalid Source',
    message: 'The source type is not recognized. Please try again or contact support.',
    type: 'error',
    recoverable: false,
  },
  OFFICE_002: {
    title: 'Invalid Association',
    message: 'The selected entity type is not supported for document association.',
    type: 'error',
    recoverable: false,
  },
  // OFFICE_003 removed - association is now optional (users can save documents without association)
  OFFICE_004: {
    title: 'File Too Large',
    message: 'The attachment exceeds the 25MB size limit. Please reduce the file size or upload it separately.',
    type: 'error',
    recoverable: true,
    action: 'Remove large attachments or upload them directly in Spaarke.',
  },
  OFFICE_005: {
    title: 'Total Size Exceeded',
    message: 'The combined size of all attachments exceeds 100MB. Please select fewer attachments.',
    type: 'error',
    recoverable: true,
    action: 'Uncheck some attachments and try again.',
  },
  OFFICE_006: {
    title: 'Blocked File Type',
    message: 'This file type is not allowed for security reasons. Executable files cannot be saved.',
    type: 'error',
    recoverable: false,
  },

  // Not found errors (404)
  OFFICE_007: {
    title: 'Entity Not Found',
    message: 'The selected entity could not be found. It may have been deleted or you may not have access.',
    type: 'error',
    recoverable: true,
    action: 'Search for a different entity.',
  },
  OFFICE_008: {
    title: 'Job Not Found',
    message: 'The processing job could not be found. It may have expired or been deleted.',
    type: 'error',
    recoverable: false,
  },

  // Permission errors (403)
  OFFICE_009: {
    title: 'Access Denied',
    message: 'You do not have permission to perform this action. Please contact your administrator.',
    type: 'error',
    recoverable: false,
  },
  OFFICE_010: {
    title: 'Cannot Create Entity',
    message: 'You do not have permission to create new entities. Please contact your administrator.',
    type: 'error',
    recoverable: false,
  },

  // Task 053: the structured creation-refusal code family from RecordCreationService (tasks 030/031) —
  // snake_case, NOT part of the OFFICE_* catalog, but a real `errorCode` value the QuickCreate endpoint's
  // own ProblemDetails sends as a top-level JSON property (`OfficeEndpoints.QuickCreateAsync`'s
  // `catch (SdapProblemException)` → `Results.Problem(..., extensions: { ["errorCode"] = problem.Code })`
  // — ASP.NET flattens `extensions` to top-level properties via `[JsonExtensionData]`; confirmed against
  // `OfficeQuickCreateContractTests`/`OfficeQuickCreateProjectContractTests`, which assert exactly this
  // shape). Load-bearing since task 031 made Project owner resolution refuse-before-write (403, no row)
  // the same way task 030 already did for Matter. No retry button: the cause is the caller's own AAD
  // identity not being provisioned as a Dataverse user, which a same-click retry cannot fix — the
  // message's own "ask an administrator" IS the way forward (root CLAUDE.md §6 framing: retry / correct
  // input / admin must act).
  owner_unresolved: {
    title: 'Account Not Provisioned',
    message: 'Your account could not be matched to a Dataverse user, so the record could not be created.',
    type: 'error',
    recoverable: false,
    action: 'Ask an administrator to check that your user is provisioned in this environment.',
  },

  // Conflict errors (409)
  OFFICE_011: {
    title: 'Document Exists',
    message: 'This document has already been saved to the selected entity.',
    type: 'info',
    recoverable: false,
    action: 'View the existing document or select a different entity.',
  },
  // Task 025 (spaarkeai-word-add-in-r1): a filename collision on CREATE, refused before any bytes moved.
  // Stated as a neutral choice, not an alarm — mirrors the shipped OBO wizard's own collision copy
  // ("Nothing has been uploaded or changed — choose how to continue"), never error-red.
  OFFICE_020: {
    title: 'Name Already Exists',
    message: 'A document with this name already exists here. Nothing was saved.',
    type: 'warning',
    recoverable: false,
    action: 'Choose how to continue.',
  },

  // Service errors (502)
  OFFICE_012: {
    title: 'Upload Failed',
    message: 'Unable to upload the file. Please check your connection and try again.',
    type: 'error',
    recoverable: true,
    action: 'Wait a moment and try again.',
  },
  OFFICE_013: {
    title: 'Service Error',
    message: 'A service error occurred while processing your request. Please try again later.',
    type: 'error',
    recoverable: true,
    action: 'Wait a moment and try again.',
  },
  OFFICE_014: {
    title: 'Database Error',
    message: 'Unable to save the record. Please try again later.',
    type: 'error',
    recoverable: true,
    action: 'Wait a moment and try again.',
  },

  // Unavailable (503)
  OFFICE_015: {
    title: 'Service Unavailable',
    message: 'The processing service is temporarily unavailable. Please try again later.',
    type: 'error',
    recoverable: true,
    action: 'Wait a few minutes and try again.',
  },

  // FR-11 version save refusals (spaarkeai-word-add-in-r1 task 023 server, task 024 pane). Each is
  // refused BEFORE anything is written ("Nothing was saved" is literal), and each is about the EXISTING
  // document the save named — which is why the pane offers "Save as new document" for them.
  OFFICE_016: {
    title: 'Document Not Found',
    message: 'The document this save was meant to add a version to could not be found. Nothing was saved.',
    type: 'error',
    recoverable: false,
    action: 'Save your changes as a new document instead.',
  },
  OFFICE_017: {
    title: 'Document Has No File',
    message: 'The document this save was meant to add a version to has no file in storage. Nothing was saved.',
    type: 'error',
    recoverable: false,
    action: 'Save your changes as a new document instead.',
  },
  OFFICE_018: {
    title: 'Version Request Rejected',
    message: 'The save named an existing document without asking for a new version. Nothing was saved.',
    type: 'error',
    recoverable: false,
    action: 'Save your changes as a new document instead.',
  },
  OFFICE_019: {
    title: 'Document Locked',
    message: 'The document is locked for editing, so a new version could not be written. Nothing was saved.',
    type: 'error',
    recoverable: true,
    action: 'Try again when it is released, or save your changes as a new document.',
  },
};

/**
 * Default error message for unknown error codes.
 */
const DEFAULT_ERROR: ErrorMessage = {
  title: 'Error',
  message: 'An unexpected error occurred. Please try again or contact support.',
  type: 'error',
  recoverable: true,
};

/**
 * Maps a ProblemDetails response to a user-friendly error message.
 *
 * @param problem - ProblemDetails error from API
 * @returns User-friendly error message
 *
 * @example
 * ```typescript
 * const response = await fetch('/office/save', ...);
 * if (!response.ok) {
 *   const problem = await response.json();
 *   const error = mapProblemDetailsToMessage(problem);
 *   showNotification(error);
 * }
 * ```
 */
export function mapProblemDetailsToMessage(problem: ProblemDetails): ErrorMessage {
  // Check if we have a known error code
  const mapped = problem.errorCode ? ERROR_CODE_MAP[problem.errorCode] : undefined;
  if (mapped) {
    // Prefer the API's detail if it's more specific
    return {
      ...mapped,
      message: problem.detail || mapped.message,
    };
  }

  // Fall back to default with API detail
  return {
    ...DEFAULT_ERROR,
    title: problem.title || DEFAULT_ERROR.title,
    message: problem.detail || DEFAULT_ERROR.message,
  };
}

/**
 * `AuthorizationService` denies with this reason code when Dataverse is unavailable DURING the
 * authorization check — a transient failure, not a decision about the caller. The one client-side
 * definition: `documentIdentityService` classifies the same code as `indeterminate`.
 */
export const ACCESS_SYSTEM_FAILURE_REASON_CODE = 'sdap.access.error.system_failure';

/**
 * Codes whose refusal is about the EXISTING document a version save named: the save cannot be completed as
 * a version, but the user's changes can still be saved as a new document. OFFICE_009 is here because on the
 * version path it means SharePoint Embedded refused the caller's write to that document's file.
 */
const VERSION_TARGET_REFUSAL_CODES: ReadonlySet<string> = new Set([
  'OFFICE_009',
  'OFFICE_016',
  'OFFICE_017',
  'OFFICE_018',
  'OFFICE_019',
]);

/**
 * Maps a refused FR-11 VERSION save (task 024) to a message the pane can act on.
 *
 * The version path's own refusals (OFFICE_016–019, plus OFFICE_009 from SPE) keep their catalog message and
 * gain `offerSaveAsNew`. A 403 with NO `errorCode` comes from the version-save authorization filter (ADR-008),
 * which deliberately answers "you may not write this document" and "no such document" identically — the
 * anti-enumeration rule — so the message covers both, and never claims which one it was. A 403 whose
 * `reasonCode` is the authorization system-failure code is transient: it is retryable, and does NOT push the
 * user toward a new document.
 */
export function describeVersionSaveFailure(problem: ProblemDetails): ErrorMessage {
  if (problem.status === 403 && !problem.errorCode) {
    if (problem.reasonCode === ACCESS_SYSTEM_FAILURE_REASON_CODE) {
      return {
        title: 'Could Not Check Access',
        message: 'Spaarke could not check your access to this document right now. Nothing was saved.',
        type: 'error',
        recoverable: true,
        action: 'Wait a moment and try again.',
      };
    }
    return {
      title: 'Cannot Add a Version',
      message:
        'You can’t add a version to this document: you don’t have permission to change it, or it is no longer ' +
        'available in Spaarke. Nothing was saved.',
      type: 'error',
      recoverable: false,
      action: 'Save your changes as a new document instead.',
      offerSaveAsNew: true,
    };
  }

  const mapped = mapProblemDetailsToMessage(problem);
  return problem.errorCode !== undefined && VERSION_TARGET_REFUSAL_CODES.has(problem.errorCode)
    ? { ...mapped, offerSaveAsNew: true }
    : mapped;
}

/**
 * Maps a refused CREATE save's filename collision (task 025, OFFICE_020) to a message the pane can act on.
 *
 * Mirrors {@link describeVersionSaveFailure}'s shape, for the create-path's own refusal: the catalog message
 * gains `offerCollisionChoice` plus the collision's `fileName`, and `collisionExistingDocumentId` when the
 * server could resolve which document already holds that name (Document saves only — the only content type
 * FR-11's version-save can target). Any OTHER error code on a create attempt falls through to the ordinary
 * mapping unchanged, so this is safe to call unconditionally on every create-path failure.
 */
export function describeCollisionFailure(problem: ProblemDetails): ErrorMessage {
  const mapped = mapProblemDetailsToMessage(problem);
  if (problem.errorCode !== 'OFFICE_020') {
    return mapped;
  }

  return {
    ...mapped,
    offerCollisionChoice: true,
    ...(problem.fileName !== undefined ? { collisionFileName: problem.fileName } : {}),
    ...(problem.existingDocumentId !== undefined ? { collisionExistingDocumentId: problem.existingDocumentId } : {}),
  };
}

/**
 * Task 053: turns a failed fetch `Response` into a user-facing {@link ErrorMessage}, preferring the
 * server's own ProblemDetails `detail`/`errorCode` (via {@link mapProblemDetailsToMessage}) over any
 * client-invented replacement — per the project rule that a failure the server explained precisely must
 * never be flattened into a generic "Something went wrong". Falls back to a status-aware, still honest
 * message only when the body isn't ProblemDetails-shaped at all (an infrastructure failure, e.g. a 502
 * from a gateway, has no such body to read). Never throws — safe to call on any non-OK `Response`,
 * including one whose body is empty or not JSON.
 *
 * @param response - A non-OK fetch `Response`. The body is consumed (`.json()`) by this call.
 */
export async function describeFetchFailure(response: Response): Promise<ErrorMessage> {
  let body: unknown = null;
  try {
    body = await response.json();
  } catch {
    // No body, or not JSON — an infrastructure failure (502/504 from a gateway) rather than a
    // ProblemDetails the BFF wrote itself. Fall through to the status-aware default below.
    body = null;
  }

  if (isProblemDetails(body)) {
    return mapProblemDetailsToMessage(body);
  }

  return response.status >= 500
    ? {
        title: 'Service Error',
        message: 'The server had a problem completing this request. Please try again.',
        type: 'error',
        recoverable: true,
        action: 'Wait a moment and try again.',
      }
    : {
        title: 'Error',
        message: `The request could not be completed (status ${response.status}).`,
        type: 'error',
        recoverable: false,
      };
}

/**
 * Maps an error code to a user-friendly message.
 *
 * @param errorCode - OFFICE_* error code
 * @returns User-friendly error message
 */
export function mapErrorCodeToMessage(errorCode: string): ErrorMessage {
  return ERROR_CODE_MAP[errorCode] || DEFAULT_ERROR;
}

/**
 * Determines if an error is recoverable (user can retry).
 *
 * @param problem - ProblemDetails error from API
 * @returns true if the user can retry the operation
 */
export function isRecoverableError(problem: ProblemDetails): boolean {
  const mapped = problem.errorCode ? ERROR_CODE_MAP[problem.errorCode] : undefined;
  if (mapped) {
    return mapped.recoverable;
  }
  // Default to recoverable for 5xx errors
  return problem.status >= 500;
}

/**
 * Extracts validation errors from ProblemDetails.
 *
 * @param problem - ProblemDetails with validation errors
 * @returns Record of field names to error messages
 */
export function extractValidationErrors(problem: ProblemDetails): Record<string, string[]> {
  return problem.errors || {};
}

/**
 * Formats an error for clipboard copy (for support purposes).
 *
 * @param problem - ProblemDetails error
 * @returns Formatted string with error details
 *
 * @example Output:
 * ```
 * Error: OFFICE_003 - Association Required
 * Message: Please select a Matter, Project, Invoice, Account, or Contact...
 * Correlation ID: abc-123-def
 * Time: 2026-01-20T10:30:00Z
 * ```
 */
export function formatErrorForClipboard(problem: ProblemDetails): string {
  const lines: string[] = [];

  lines.push(`Error: ${problem.errorCode || 'Unknown'} - ${problem.title}`);
  lines.push(`Message: ${problem.detail}`);

  if (problem.correlationId) {
    lines.push(`Correlation ID: ${problem.correlationId}`);
  }

  if (problem.instance) {
    lines.push(`Endpoint: ${problem.instance}`);
  }

  lines.push(`Time: ${new Date().toISOString()}`);

  if (problem.errors && Object.keys(problem.errors).length > 0) {
    lines.push('');
    lines.push('Validation Errors:');
    for (const [field, errors] of Object.entries(problem.errors)) {
      for (const err of errors) {
        lines.push(`  - ${field}: ${err}`);
      }
    }
  }

  return lines.join('\n');
}

/**
 * Creates a simple error from a caught exception.
 *
 * @param error - Unknown caught error
 * @param fallbackMessage - Message to use if error is not an Error instance
 * @returns ErrorMessage suitable for notification
 */
export function createErrorFromException(
  error: unknown,
  fallbackMessage = 'An unexpected error occurred'
): ErrorMessage {
  if (error instanceof Error) {
    return {
      title: 'Error',
      message: error.message || fallbackMessage,
      type: 'error',
      recoverable: true,
    };
  }

  if (typeof error === 'string') {
    return {
      title: 'Error',
      message: error,
      type: 'error',
      recoverable: true,
    };
  }

  return {
    ...DEFAULT_ERROR,
    message: fallbackMessage,
  };
}

/**
 * Checks if an error response is a ProblemDetails object.
 *
 * @param response - Unknown response object
 * @returns true if response is a valid ProblemDetails
 */
export function isProblemDetails(response: unknown): response is ProblemDetails {
  if (!response || typeof response !== 'object') {
    return false;
  }

  const obj = response as Record<string, unknown>;
  return typeof obj.type === 'string' && typeof obj.title === 'string' && typeof obj.status === 'number';
}

// Export error codes for reference
export const ERROR_CODES = {
  INVALID_SOURCE_TYPE: 'OFFICE_001',
  INVALID_ASSOCIATION_TYPE: 'OFFICE_002',
  ASSOCIATION_REQUIRED: 'OFFICE_003',
  ATTACHMENT_TOO_LARGE: 'OFFICE_004',
  TOTAL_SIZE_EXCEEDED: 'OFFICE_005',
  BLOCKED_FILE_TYPE: 'OFFICE_006',
  ENTITY_NOT_FOUND: 'OFFICE_007',
  JOB_NOT_FOUND: 'OFFICE_008',
  ACCESS_DENIED: 'OFFICE_009',
  CANNOT_CREATE_ENTITY: 'OFFICE_010',
  DOCUMENT_ALREADY_EXISTS: 'OFFICE_011',
  SPE_UPLOAD_FAILED: 'OFFICE_012',
  GRAPH_API_ERROR: 'OFFICE_013',
  DATAVERSE_ERROR: 'OFFICE_014',
  PROCESSING_UNAVAILABLE: 'OFFICE_015',
  VERSION_TARGET_NOT_FOUND: 'OFFICE_016',
  VERSION_TARGET_HAS_NO_FILE: 'OFFICE_017',
  VERSION_INTENT_MISMATCH: 'OFFICE_018',
  VERSION_TARGET_LOCKED: 'OFFICE_019',
  NAME_COLLISION: 'OFFICE_020',
} as const;
