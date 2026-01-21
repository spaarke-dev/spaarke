/**
 * Office API Client Error Handling
 *
 * Provides typed error handling for the Office API client with ProblemDetails support.
 * Maps API errors to user-friendly messages for display in the task pane UI.
 *
 * @see projects/sdap-office-integration/spec.md for error code catalog
 */

import type { ProblemDetails, OfficeApiErrorCode } from './types';

/**
 * Error thrown by the Office API client.
 * Contains the full ProblemDetails response from the server.
 */
export class OfficeApiError extends Error {
  public readonly problemDetails: ProblemDetails;
  public readonly isNetworkError: boolean;
  public readonly isAuthError: boolean;
  public readonly isRateLimited: boolean;
  public readonly retryAfterSeconds?: number;

  constructor(
    problemDetails: ProblemDetails,
    options?: {
      isNetworkError?: boolean;
      isAuthError?: boolean;
      isRateLimited?: boolean;
      retryAfterSeconds?: number;
    }
  ) {
    super(problemDetails.detail || problemDetails.title);
    this.name = 'OfficeApiError';
    this.problemDetails = problemDetails;
    this.isNetworkError = options?.isNetworkError ?? false;
    this.isAuthError = options?.isAuthError ?? false;
    this.isRateLimited = options?.isRateLimited ?? false;
    this.retryAfterSeconds = options?.retryAfterSeconds;
  }

  /**
   * Get the error code (e.g., OFFICE_001).
   */
  get errorCode(): string | undefined {
    return this.problemDetails.errorCode;
  }

  /**
   * Get the HTTP status code.
   */
  get status(): number {
    return this.problemDetails.status;
  }

  /**
   * Get the correlation ID for support/debugging.
   */
  get correlationId(): string | undefined {
    return this.problemDetails.correlationId;
  }

  /**
   * Get field-level validation errors.
   */
  get validationErrors(): Record<string, string[]> | undefined {
    return this.problemDetails.errors;
  }

  /**
   * Check if this is a specific error code.
   */
  isErrorCode(code: OfficeApiErrorCode | string): boolean {
    return this.problemDetails.errorCode === code;
  }

  /**
   * Get a user-friendly error message.
   */
  getUserMessage(): string {
    return getUserFriendlyMessage(this.problemDetails);
  }
}

/**
 * Maps error codes to user-friendly messages.
 */
const ERROR_CODE_MESSAGES: Record<string, string> = {
  // Validation errors
  OFFICE_001: 'The content type is not supported.',
  OFFICE_002: 'Please select a valid entity type.',
  OFFICE_003: 'Please select an association target (Matter, Project, Account, etc.).',
  OFFICE_004: 'This attachment is too large. Maximum size is 25MB per file.',
  OFFICE_005: 'Total attachments are too large. Maximum combined size is 100MB.',
  OFFICE_006: 'This file type is not allowed for security reasons.',
  // Not found errors
  OFFICE_007: 'The selected entity could not be found. It may have been deleted.',
  OFFICE_008: 'The job status could not be found. It may have expired.',
  // Permission errors
  OFFICE_009: 'You do not have permission to perform this action.',
  OFFICE_010: 'You do not have permission to create this type of entity.',
  // Conflict
  OFFICE_011: 'This document has already been saved with this association.',
  // Service errors
  OFFICE_012: 'There was a problem uploading the file. Please try again.',
  OFFICE_013: 'There was a problem connecting to Microsoft services. Please try again.',
  OFFICE_014: 'There was a problem saving to the database. Please try again.',
  OFFICE_015: 'The processing service is currently unavailable. Please try again later.',
};

/**
 * Maps HTTP status codes to generic messages.
 */
const STATUS_CODE_MESSAGES: Record<number, string> = {
  400: 'The request was invalid. Please check your input and try again.',
  401: 'Your session has expired. Please sign in again.',
  403: 'You do not have permission to perform this action.',
  404: 'The requested resource could not be found.',
  408: 'The request timed out. Please try again.',
  409: 'There was a conflict with your request. Please refresh and try again.',
  429: 'Too many requests. Please wait a moment and try again.',
  500: 'An unexpected error occurred. Please try again later.',
  502: 'A service is temporarily unavailable. Please try again.',
  503: 'The service is temporarily unavailable. Please try again later.',
  504: 'The request timed out. Please try again.',
};

/**
 * Get a user-friendly error message from ProblemDetails.
 */
export function getUserFriendlyMessage(problemDetails: ProblemDetails): string {
  // First, check for a specific error code message
  if (problemDetails.errorCode && ERROR_CODE_MESSAGES[problemDetails.errorCode]) {
    return ERROR_CODE_MESSAGES[problemDetails.errorCode];
  }

  // If there's a specific detail message, use it (but sanitize)
  if (problemDetails.detail && !problemDetails.detail.includes('Exception')) {
    // Return the detail if it doesn't look like a stack trace
    return problemDetails.detail;
  }

  // Fall back to status code message
  if (STATUS_CODE_MESSAGES[problemDetails.status]) {
    return STATUS_CODE_MESSAGES[problemDetails.status];
  }

  // Last resort
  return problemDetails.title || 'An unexpected error occurred.';
}

/**
 * Create a ProblemDetails object from a network error.
 */
export function createNetworkErrorDetails(error: Error): ProblemDetails {
  return {
    type: 'https://spaarke.com/errors/network-error',
    title: 'Network Error',
    status: 0,
    detail: 'Unable to connect to the server. Please check your internet connection.',
  };
}

/**
 * Create a ProblemDetails object from an authentication error.
 */
export function createAuthErrorDetails(message?: string): ProblemDetails {
  return {
    type: 'https://spaarke.com/errors/auth-error',
    title: 'Authentication Error',
    status: 401,
    detail: message || 'Your session has expired. Please sign in again.',
  };
}

/**
 * Create a ProblemDetails object from a timeout error.
 */
export function createTimeoutErrorDetails(): ProblemDetails {
  return {
    type: 'https://spaarke.com/errors/timeout',
    title: 'Request Timeout',
    status: 408,
    detail: 'The request took too long to complete. Please try again.',
  };
}

/**
 * Create a ProblemDetails object for an unknown error.
 */
export function createUnknownErrorDetails(error?: unknown): ProblemDetails {
  const message = error instanceof Error ? error.message : 'An unexpected error occurred.';
  return {
    type: 'https://spaarke.com/errors/unknown',
    title: 'Error',
    status: 500,
    detail: message,
  };
}

/**
 * Parse the Retry-After header value.
 * Returns the number of seconds to wait, or undefined if not present/invalid.
 */
export function parseRetryAfter(retryAfter: string | null): number | undefined {
  if (!retryAfter) {
    return undefined;
  }

  // Try to parse as seconds
  const seconds = parseInt(retryAfter, 10);
  if (!isNaN(seconds) && seconds > 0) {
    return seconds;
  }

  // Try to parse as HTTP-date
  const date = new Date(retryAfter);
  if (!isNaN(date.getTime())) {
    const diffMs = date.getTime() - Date.now();
    if (diffMs > 0) {
      return Math.ceil(diffMs / 1000);
    }
  }

  return undefined;
}

/**
 * Check if an error is retryable.
 */
export function isRetryableError(error: OfficeApiError): boolean {
  // Rate limited - should retry after waiting
  if (error.isRateLimited) {
    return true;
  }

  // Network errors are usually transient
  if (error.isNetworkError) {
    return true;
  }

  // Server errors (5xx) are usually retryable
  const status = error.status;
  if (status >= 500 && status < 600) {
    return true;
  }

  // Timeouts are retryable
  if (status === 408 || status === 504) {
    return true;
  }

  // Specific error codes that are retryable
  const retryableCodes = ['OFFICE_012', 'OFFICE_013', 'OFFICE_014', 'OFFICE_015'];
  if (error.errorCode && retryableCodes.includes(error.errorCode)) {
    return true;
  }

  return false;
}

/**
 * Format validation errors for display.
 */
export function formatValidationErrors(errors: Record<string, string[]>): string {
  const messages: string[] = [];
  for (const [field, fieldErrors] of Object.entries(errors)) {
    for (const error of fieldErrors) {
      messages.push(`${field}: ${error}`);
    }
  }
  return messages.join('\n');
}
