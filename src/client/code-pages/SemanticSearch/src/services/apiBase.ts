/**
 * Shared API base utilities for BFF API service clients.
 *
 * Provides:
 * - getBffBaseUrl() — resolves BFF API base URL from Dataverse env vars at runtime
 * - buildAuthHeaders() — constructs Authorization + Content-Type headers via @spaarke/auth
 * - handleApiResponse<T>() — parses success or throws typed ApiError (RFC 7807 ProblemDetails)
 *
 * @see ADR-013: All AI calls go through BFF API
 */

import { getAuthHeader } from './authInit';
import { resolveRuntimeConfig } from '@spaarke/auth';
import type { ApiError } from '../types';

/**
 * Module-level cache for the resolved BFF base URL.
 * Set once by initializeRuntimeConfig() called from authInit.ts at bootstrap.
 */
let _bffBaseUrl: string | null = null;

/**
 * Initialize the BFF base URL from Dataverse environment variables.
 * Called once at bootstrap by initializeAuth() before the app renders.
 *
 * @internal Used by authInit.ts — not intended for direct consumption.
 */
export async function initializeRuntimeConfig(): Promise<void> {
  const config = await resolveRuntimeConfig();
  _bffBaseUrl = config.bffBaseUrl;
}

/**
 * Get the resolved BFF API base URL.
 * Throws if initializeRuntimeConfig() has not been called.
 */
export function getBffBaseUrl(): string {
  if (!_bffBaseUrl) {
    throw new Error(
      '[SemanticSearch] BFF base URL not initialized. ' +
      'Ensure initializeAuth() is awaited before making API calls.'
    );
  }
  return _bffBaseUrl;
}

/**
 * Build authenticated request headers for BFF API calls.
 * Acquires a Bearer token via @spaarke/auth and returns
 * Authorization + Content-Type headers.
 *
 * @returns Header record ready for fetch() calls
 * @throws Error if auth is not initialized or token acquisition fails
 */
export async function buildAuthHeaders(): Promise<Record<string, string>> {
  const authHeader = await getAuthHeader();
  return {
    Authorization: authHeader,
    'Content-Type': 'application/json',
  };
}

/**
 * Handle a fetch Response, parsing JSON on success or throwing a typed ApiError.
 *
 * On success (2xx): parses and returns JSON body as T.
 * On failure: attempts to parse RFC 7807 ProblemDetails body, then throws ApiError.
 *
 * @template T - Expected response body type
 * @param response - The fetch Response object
 * @returns Parsed response body
 * @throws ApiError with status, title, detail, type, and optional validation errors
 */
export async function handleApiResponse<T>(response: Response): Promise<T> {
  if (response.ok) {
    return response.json() as Promise<T>;
  }

  // Parse ProblemDetails error response (RFC 7807)
  let error: ApiError;
  try {
    const body = await response.json();
    error = {
      status: response.status,
      title: body.title || response.statusText,
      detail: body.detail,
      type: body.type,
      errors: body.errors,
    };
  } catch {
    error = {
      status: response.status,
      title: response.statusText,
    };
  }

  throw error;
}
