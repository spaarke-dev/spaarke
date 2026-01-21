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
  errors?: Record<string, string[]>;
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
  OFFICE_003: {
    title: 'Association Required',
    message: 'Please select a Matter, Project, Invoice, Account, or Contact to associate this document with.',
    type: 'warning',
    recoverable: true,
    action: 'Select an entity to continue.',
  },
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

  // Conflict errors (409)
  OFFICE_011: {
    title: 'Document Exists',
    message: 'This document has already been saved to the selected entity.',
    type: 'info',
    recoverable: false,
    action: 'View the existing document or select a different entity.',
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
  if (problem.errorCode && ERROR_CODE_MAP[problem.errorCode]) {
    const mapped = ERROR_CODE_MAP[problem.errorCode];
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
  if (problem.errorCode && ERROR_CODE_MAP[problem.errorCode]) {
    return ERROR_CODE_MAP[problem.errorCode].recoverable;
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
  return (
    typeof obj.type === 'string' &&
    typeof obj.title === 'string' &&
    typeof obj.status === 'number'
  );
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
} as const;
