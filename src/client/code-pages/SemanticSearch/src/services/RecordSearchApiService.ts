/**
 * RecordSearchApiService — Record search API client for the SemanticSearch code page.
 *
 * Calls POST /api/ai/search/records on the BFF API to execute record-level
 * semantic search across Matters, Projects, and Invoices.
 *
 * @see ADR-013: All AI calls go through BFF API
 * @see types/index.ts — RecordSearchRequest, RecordSearchResponse
 */

import { authenticatedFetch } from '@spaarke/auth';
import { getBffBaseUrl, handleApiResponse } from './apiBase';
import type { RecordSearchRequest, RecordSearchResponse } from '../types';

/**
 * Execute a record semantic search via the BFF API.
 *
 * @param request - Record search request with query, recordTypes, filters, and options
 * @returns RecordSearchResponse containing results and metadata
 * @throws ApiError on HTTP errors (400, 401, 403, 429, 5xx)
 * @throws Error on network failure or MSAL token acquisition failure
 */
export async function search(request: RecordSearchRequest): Promise<RecordSearchResponse> {
  // Canonical Spaarke auth pattern: authenticatedFetch attaches the Bearer token,
  // guards against empty tokens, and retries 401s with cache invalidation.
  // Do NOT replace with raw fetch() + buildAuthHeaders() — see backlog #9.
  const response = await authenticatedFetch(`${getBffBaseUrl()}/api/ai/search/records`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(request),
  });

  return handleApiResponse<RecordSearchResponse>(response);
}
