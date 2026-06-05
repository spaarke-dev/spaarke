/**
 * SemanticSearchApiService — Document search API client for the SemanticSearch code page.
 *
 * Calls POST /api/ai/search on the BFF API to execute document semantic search.
 * Adapted from the PCF SemanticSearchApiService but removes PCF context dependency
 * and uses the shared apiBase utilities for authentication and error handling.
 *
 * @see ADR-013: All AI calls go through BFF API
 * @see types/index.ts — DocumentSearchRequest, DocumentSearchResponse
 */

import { authenticatedFetch } from '@spaarke/auth';
import { getBffBaseUrl, handleApiResponse } from './apiBase';
import type { DocumentSearchRequest, DocumentSearchResponse } from '../types';

/**
 * Execute a document semantic search via the BFF API.
 *
 * @param request - Document search request with query, scope, filters, and options
 * @returns DocumentSearchResponse containing results and metadata
 * @throws ApiError on HTTP errors (400, 401, 403, 429, 5xx)
 * @throws Error on network failure or MSAL token acquisition failure
 */
export async function search(request: DocumentSearchRequest): Promise<DocumentSearchResponse> {
  // Canonical Spaarke auth pattern: authenticatedFetch attaches the Bearer token,
  // guards against empty tokens, and retries 401s with cache invalidation.
  // Do NOT replace with raw fetch() + buildAuthHeaders() — see backlog #9.
  const response = await authenticatedFetch(`${getBffBaseUrl()}/api/ai/search`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(request),
  });

  return handleApiResponse<DocumentSearchResponse>(response);
}
