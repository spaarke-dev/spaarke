/**
 * RecordSearchApiService — Record search API client for the SemanticSearch code page.
 *
 * Calls POST /api/ai/search/records on the BFF API to execute record-level
 * semantic search across Matters, Projects, and Invoices.
 *
 * @see ADR-013: All AI calls go through BFF API
 * @see types/index.ts — RecordSearchRequest, RecordSearchResponse
 */

import { BFF_API_BASE_URL, buildAuthHeaders, handleApiResponse } from "./apiBase";
import type { RecordSearchRequest, RecordSearchResponse } from "../types";

/**
 * Execute a record semantic search via the BFF API.
 *
 * @param request - Record search request with query, recordTypes, filters, and options
 * @returns RecordSearchResponse containing results and metadata
 * @throws ApiError on HTTP errors (400, 401, 403, 429, 5xx)
 * @throws Error on network failure or MSAL token acquisition failure
 */
export async function search(request: RecordSearchRequest): Promise<RecordSearchResponse> {
    const endpoint = `${BFF_API_BASE_URL}/api/ai/search/records`;
    const headers = await buildAuthHeaders();

    const response = await fetch(endpoint, {
        method: "POST",
        headers,
        body: JSON.stringify(request),
    });

    return handleApiResponse<RecordSearchResponse>(response);
}
