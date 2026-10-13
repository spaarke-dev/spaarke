/**
 * Shared API base utilities for BFF API service clients.
 *
 * Provides:
 * - getBffBaseUrl() — resolves BFF API base URL from Dataverse env vars at runtime
 * - buildAuthHeaders() — constructs Authorization + Content-Type headers via @spaarke/auth
 * - handleApiResponse<T>() — parses the JSON body of a successful (2xx) response
 *
 * @see ADR-013: All AI calls go through BFF API
 */

import { getAuthHeader } from './authInit';
import { resolveRuntimeConfig } from '@spaarke/auth';
import type { OkResponse } from '@spaarke/auth';

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
      '[SemanticSearch] BFF base URL not initialized. ' + 'Ensure initializeAuth() is awaited before making API calls.'
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
 * Parse the JSON body of a successful fetch Response.
 *
 * `@spaarke/auth`'s `authenticatedFetch` resolves only with a 2xx ({@link OkResponse}); every failure
 * is THROWN (`ApiError` / `AuthError`) before this is reached, and the callers' error mapping
 * (`extractErrorMessage`) reads those thrown shapes.
 *
 * @template T - Expected response body type
 * @param response - The successful fetch Response object
 * @returns Parsed response body
 */
export async function handleApiResponse<T>(response: OkResponse): Promise<T> {
  return response.json() as Promise<T>;
}
