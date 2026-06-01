/**
 * SemanticSearchApiService
 *
 * API service for calling the semantic search endpoint.
 * Handles request building, authentication, and response parsing.
 *
 * @see spec.md for API contract details
 */

import { authenticatedFetch } from '@spaarke/auth';
import { SearchRequest, SearchResponse, SearchError } from '../types';

/**
 * API error response structure
 */
interface ApiErrorResponse {
  error?: string;
  message?: string;
  code?: string;
}

/**
 * Response from GET /api/documents/{id}/open-links
 */
export interface OpenLinksResponse {
  desktopUrl: string | null;
  webUrl: string;
  mimeType: string;
  fileName: string;
}

/**
 * Response from GET /api/documents/{id}/preview-url
 */
export interface PreviewUrlResponse {
  previewUrl: string;
  documentInfo: {
    name: string;
    mimeType: string;
  } | null;
  checkoutStatus: unknown;
  correlationId: string;
}

/**
 * Service for semantic search API calls.
 *
 * MIGRATION NOTE: This service now uses authenticatedFetch() from @spaarke/auth
 * instead of receiving MsalAuthProvider as a parameter. Token acquisition, caching,
 * and 401 retry logic are handled by the shared auth library.
 */
export class SemanticSearchApiService {
  private readonly apiBaseUrl: string;

  /**
   * Create a new SemanticSearchApiService
   * @param apiBaseUrl - Base URL for the BFF API (e.g., https://api.example.com)
   */
  constructor(apiBaseUrl: string) {
    this.apiBaseUrl = apiBaseUrl.replace(/\/+$/, ''); // Remove trailing slash
  }

  /**
   * v1.1.49 (Item 8 Part B) — "All Documents" client-side merge.
   *
   * Investigation (see SemanticSearchService.cs Step 4 BuildFilter + line 96
   * SearchAssociatedOnlyAsync): the BFF treats `associatedOnly` as TWO
   * disjoint paths:
   *   - `true`  → bypasses Azure AI Search, queries Dataverse directly →
   *               returns ALL parent-associated docs (no semantic filter)
   *   - `false` → goes through Azure AI Search WITH entity scope filter →
   *               returns ONLY indexed docs scoped by entity (recently
   *               uploaded docs may not be in the index yet)
   * This is BY DESIGN — the AI-Search path needs an indexed doc to score it.
   * To deliver the user's expected "All Documents = semantic + associated"
   * behavior we therefore have to MERGE client-side. We fire both paths in
   * parallel, dedupe by `documentId`, and sort by combinedScore DESC, then
   * modifiedAt DESC. The merged result count + paging semantics are
   * preserved relative to the AI-Search path (the associated-only path
   * contributes any docs the AI-Search path missed).
   *
   * IMPORTANT: only triggered when scope is entity-scoped AND
   * associatedOnly=false. The associatedOnly=true path is unchanged. When
   * scope = 'all'/'custom' the union is meaningless (no parent FK) so we
   * skip it.
   */
  async searchUnion(request: SearchRequest): Promise<SearchResponse> {
    const isEntityScope = ['matter', 'project', 'invoice', 'account', 'contact', 'document', 'entity'].includes(
      request.scope
    );
    const associatedOnly = request.filters?.associatedOnly === true;

    // Not eligible for union — fall back to plain search.
    if (!isEntityScope || associatedOnly) {
      return this.search(request);
    }

    // Build the associatedOnly=true variant. We reuse the request shape but
    // flip the filter and force offset=0 so the associated path always
    // returns from the top (it's a small N — direct Dataverse query).
    const associatedRequest: SearchRequest = {
      ...request,
      filters: {
        ...(request.filters ?? {
          documentTypes: [],
          matterTypes: [],
          dateRange: null,
          fileTypes: [],
          threshold: 0,
          searchMode: 'hybrid',
        }),
        associatedOnly: true,
      },
      options: { ...request.options, offset: 0 },
    };

    // Fire both in parallel. If either fails, we still want partial coverage
    // — wrap in `Promise.allSettled` and fall back to whichever resolved.
    const [semanticOutcome, associatedOutcome] = await Promise.allSettled([
      this.search(request),
      this.search(associatedRequest),
    ]);

    const semantic = semanticOutcome.status === 'fulfilled' ? semanticOutcome.value : null;
    const associated = associatedOutcome.status === 'fulfilled' ? associatedOutcome.value : null;

    // Both failed — re-throw the semantic error (the primary path).
    if (!semantic && !associated) {
      if (semanticOutcome.status === 'rejected') {
        throw semanticOutcome.reason;
      }
      if (associatedOutcome.status === 'rejected') {
        throw associatedOutcome.reason;
      }
    }

    // Dedupe by documentId.
    //
    // v1.1.50 — Each result is tagged with `relationship` so the list view
    // can render the Relationship + Similarity pills correctly (Items 3 + 5).
    //
    // v1.1.51 (Item 2) — Conflict handling changed.
    //   Before: on conflict, we tagged the row `'associated'` and adopted
    //   the semantic combinedScore. Result: the Relationship column
    //   correctly showed "Same Matter", but the Similarity column read the
    //   tag FIRST and rendered the no-percentage chip → the user lost
    //   visibility of the semantic match strength on docs that appeared
    //   in BOTH paths (UAT round 6 Item 2: "results only return 'Same
    //   matter' not Semantic related").
    //   Now: on conflict, we tag the row `'both'`. The Relationship column
    //   still renders "Same Matter" (canonical/stronger label), but the
    //   Similarity column renders the % chip for `'both'` rows (treated
    //   as semantic for the similarity readout). Behavior for rows that
    //   appear in ONLY ONE path is unchanged.
    //
    //   Dedupe order: associated first, semantic merges in. When the same
    //   docId is in both, the SEMANTIC record wins on body+score (it has
    //   the meaningful combinedScore the user wants to see) but is tagged
    //   `'both'` so the UI preserves the "Same Matter" relationship label.
    const byId = new Map<string, typeof semantic extends null ? never : SearchResponse['results'][number]>();
    if (associated) {
      for (const r of associated.results) {
        if (r.documentId) byId.set(r.documentId, { ...r, relationship: 'associated' });
      }
    }
    if (semantic) {
      for (const r of semantic.results) {
        if (!r.documentId) continue;
        const prior = byId.get(r.documentId);
        if (prior && prior.relationship === 'associated') {
          // Conflict: tag 'both' so the UI shows "Same Matter" pill AND
          // the semantic similarity %. Adopt the semantic record's
          // combinedScore + metadata (it carries the meaningful score).
          byId.set(r.documentId, { ...r, relationship: 'both' });
        } else {
          byId.set(r.documentId, { ...r, relationship: 'semantic' });
        }
      }
    }

    // Sort: combinedScore DESC, then modifiedAt DESC. Stable order so the
    // user's mental "this is most relevant" + "this is most recent" model
    // both line up at the top of the list.
    const merged = Array.from(byId.values()).sort((a, b) => {
      const scoreDelta = (b.combinedScore ?? 0) - (a.combinedScore ?? 0);
      if (scoreDelta !== 0) return scoreDelta;
      const aMod = a.modifiedAt ? new Date(a.modifiedAt).getTime() : 0;
      const bMod = b.modifiedAt ? new Date(b.modifiedAt).getTime() : 0;
      return bMod - aMod;
    });

    // Composite totalCount: prefer the larger of the two reported totals
    // so the footer reads "N of M" with the user's expected upper bound.
    const totalCount = Math.max(semantic?.totalCount ?? 0, associated?.totalCount ?? 0, merged.length);

    return {
      results: merged,
      totalCount,
      metadata: semantic?.metadata ?? associated?.metadata ?? { searchTimeMs: 0, query: request.query },
    };
  }

  /**
   * Execute a semantic search
   * @param request - Search request parameters
   * @returns Search response with results
   * @throws SearchError on failure
   */
  async search(request: SearchRequest): Promise<SearchResponse> {
    const endpoint = `${this.apiBaseUrl}/api/ai/search`;
    // v1.1.50 — tag each result with its relationship origin so the list
    // view's Relationship + Similarity pills render correctly (Items 3 + 5).
    // When associatedOnly=true the BFF served the Dataverse-direct path;
    // every result is 'associated'. Otherwise the BFF returned semantic
    // (AI-Search) results. Note: `searchUnion` overrides this tagging on
    // its dedupe step (associated wins on conflict) — see above.
    const relationshipTag: 'associated' | 'semantic' =
      request.filters?.associatedOnly === true ? 'associated' : 'semantic';

    try {
      // Transform PCF request format to API format
      const apiRequest = this.transformRequest(request);

      // DEBUG: Log the API request
      console.log('[SemanticSearchApiService] API request:', {
        endpoint,
        pcfRequest: request,
        apiRequest,
      });

      // Use authenticatedFetch — token acquisition and 401 retry handled by @spaarke/auth
      const response = await authenticatedFetch(endpoint, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
        },
        body: JSON.stringify(apiRequest),
      });

      // Handle HTTP errors
      if (!response.ok) {
        return await this.handleHttpError(response);
      }

      // Parse response
      const data: SearchResponse = await response.json();

      // DEBUG: Log the API response including documentId fields
      console.log('[SemanticSearchApiService] API response:', {
        totalCount: data.totalCount,
        resultsCount: data.results?.length ?? 0,
        metadata: data.metadata,
        firstResults: data.results?.slice(0, 3).map(r => ({
          documentId: r.documentId,
          name: r.name,
          keys: Object.keys(r as object),
        })),
      });

      return this.validateResponse(data, relationshipTag);
    } catch (error) {
      // Re-throw SearchError as-is
      if (this.isSearchError(error)) {
        throw error;
      }

      // Handle network errors
      if (error instanceof TypeError && error.message.includes('fetch')) {
        throw this.createError(
          'Unable to connect to the search service. Please check your connection.',
          'NETWORK_ERROR',
          true
        );
      }

      // Unknown error
      throw this.createError('An unexpected error occurred while searching.', 'UNKNOWN_ERROR', true);
    }
  }

  /**
   * Get open links (web URL and desktop protocol URL) for a document.
   * Calls GET /api/documents/{documentId}/open-links which fetches SPE file URLs
   * via Graph API and constructs the appropriate desktop protocol URL.
   * @param documentId - Dataverse sprk_document record GUID
   * @returns OpenLinksResponse with webUrl and optional desktopUrl
   */
  async getOpenLinks(documentId: string): Promise<OpenLinksResponse> {
    const endpoint = `${this.apiBaseUrl}/api/documents/${encodeURIComponent(documentId)}/open-links`;

    try {
      console.log('[SemanticSearchApiService] getOpenLinks:', {
        documentId,
        endpoint,
      });

      const response = await authenticatedFetch(endpoint, {
        method: 'GET',
      });

      if (!response.ok) {
        const errorMessage = `Failed to get open links (HTTP ${response.status})`;
        throw this.createError(errorMessage, `HTTP_${response.status}`, false);
      }

      const data = (await response.json()) as OpenLinksResponse;

      console.log('[SemanticSearchApiService] getOpenLinks response:', {
        hasDesktopUrl: !!data.desktopUrl,
        hasWebUrl: !!data.webUrl,
        mimeType: data.mimeType,
      });

      return data;
    } catch (error) {
      if (this.isSearchError(error)) {
        throw error;
      }
      if (error instanceof TypeError && error.message.includes('fetch')) {
        throw this.createError('Unable to connect to the search service.', 'NETWORK_ERROR', true);
      }
      throw this.createError('Failed to get file open links.', 'OPEN_LINKS_ERROR', false);
    }
  }

  /**
   * Get a read-only preview URL for a document.
   * Calls GET /api/documents/{documentId}/preview-url which uses the Graph API
   * preview endpoint to return an ephemeral embed URL (~10 min expiry).
   * @param documentId - Dataverse sprk_document record GUID
   * @returns Preview URL string, or null if not available
   */
  async getPreviewUrl(documentId: string): Promise<string | null> {
    const endpoint = `${this.apiBaseUrl}/api/documents/${encodeURIComponent(documentId)}/preview-url`;

    try {
      console.log('[SemanticSearchApiService] getPreviewUrl:', {
        documentId,
        endpoint,
      });

      const response = await authenticatedFetch(endpoint, {
        method: 'GET',
      });

      if (!response.ok) {
        console.warn('[SemanticSearchApiService] getPreviewUrl failed:', response.status);
        return null;
      }

      const data = (await response.json()) as PreviewUrlResponse;

      console.log('[SemanticSearchApiService] getPreviewUrl response:', {
        hasPreviewUrl: !!data.previewUrl,
        documentInfo: data.documentInfo,
      });

      return data.previewUrl ?? null;
    } catch (error) {
      if (this.isSearchError(error)) {
        throw error;
      }
      console.error('[SemanticSearchApiService] getPreviewUrl error:', error);
      return null;
    }
  }

  /**
   * Handle HTTP error responses
   */
  private async handleHttpError(response: Response): Promise<never> {
    let errorMessage = 'Search failed';
    let errorCode = `HTTP_${response.status}`;
    let retryable = false;

    try {
      const errorData: ApiErrorResponse = await response.json();
      errorMessage = errorData.message ?? errorData.error ?? errorMessage;
      if (errorData.code) {
        errorCode = errorData.code;
      }
    } catch {
      // Response not JSON, use status text
      errorMessage = response.statusText || errorMessage;
    }

    console.error('[SemanticSearchApiService] HTTP error response:', {
      status: response.status,
      statusText: response.statusText,
      errorCode,
      errorMessage,
    });

    switch (response.status) {
      case 400:
        throw this.createError('Invalid search request. Please modify your query.', errorCode, false);
      case 401:
        throw this.createError('Your session has expired. Please sign in again.', 'AUTH_EXPIRED', true);
      case 403:
        throw this.createError('You do not have permission to perform this search.', 'FORBIDDEN', false);
      case 404:
        throw this.createError('Search service not found. Please contact support.', 'NOT_FOUND', false);
      case 429:
        throw this.createError('Too many requests. Please wait a moment and try again.', 'RATE_LIMITED', true);
      case 500:
      case 502:
      case 503:
      case 504:
        retryable = true;
        throw this.createError(
          'The search service is temporarily unavailable. Please try again.',
          errorCode,
          retryable
        );
      default:
        throw this.createError(errorMessage, errorCode, retryable);
    }
  }

  /**
   * Validate and normalize the API response.
   *
   * @param data Raw API response payload.
   * @param relationshipTag v1.1.50 — relationship origin to apply to each
   *        result (Items 3 + 5). The BFF itself does not yet emit this
   *        field; we tag client-side based on which BFF path was invoked.
   *        `searchUnion` may overwrite the tag during dedupe.
   */
  private validateResponse(data: unknown, relationshipTag: 'associated' | 'semantic' = 'semantic'): SearchResponse {
    // Type guard for response structure
    if (!data || typeof data !== 'object') {
      throw this.createError('Invalid response from search service.', 'INVALID_RESPONSE', true);
    }

    const response = data as SearchResponse;

    // Ensure required fields exist
    if (!Array.isArray(response.results)) {
      throw this.createError('Invalid response format from search service.', 'INVALID_RESPONSE', true);
    }

    // Normalize response — ensure array fields are never null (API may return null instead of [])
    return {
      results: response.results.map(r => ({
        ...r,
        highlights: Array.isArray(r.highlights) ? r.highlights : [],
        documentType: r.documentType ?? '',
        fileUrl: r.fileUrl ?? '',
        recordUrl: r.recordUrl ?? '',
        createdBy: r.createdBy ?? null,
        // FR-BFF-01 (task 050) — modifiedAt/modifiedBy from post-search Dataverse lookup.
        // The BFF SearchResult record always emits these fields; missing values arrive as null.
        modifiedAt: r.modifiedAt ?? null,
        modifiedBy: r.modifiedBy ?? null,
        // fileSize from sprk_filesize via BFF enrichment — drives the DocumentEmailWizard
        // 25 MB attachment-cap warning. Missing values arrive as null.
        fileSize: r.fileSize ?? null,
        summary: r.summary ?? null,
        tldr: r.tldr ?? null,
        // v1.1.50 — relationship origin (Items 3 + 5).
        relationship: r.relationship ?? relationshipTag,
      })),
      // BFF returns total count in metadata.totalResults, not at top level
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      totalCount: response.totalCount ?? (response.metadata as any)?.totalResults ?? response.results.length,
      metadata: response.metadata ?? {
        searchTimeMs: 0,
        query: '',
      },
    };
  }

  /**
   * Create a SearchError
   */
  private createError(message: string, code: string, retryable: boolean): SearchError {
    return {
      message,
      code,
      retryable,
    };
  }

  /**
   * Type guard for SearchError
   */
  private isSearchError(error: unknown): error is SearchError {
    return typeof error === 'object' && error !== null && 'message' in error && 'retryable' in error;
  }

  /**
   * Transform PCF request format to API request format.
   *
   * PCF uses: scope = "all" | "matter" | "project" | ... | "custom" with scopeId
   * API uses: scope = "entity" | "documentIds" with entityType/entityId
   *
   * Mapping:
   * - PCF entity scopes (matter, project, invoice, account, contact) + scopeId
   *   → API "entity" + entityType + entityId=scopeId
   * - PCF "all" → Not supported in R1 (API returns proper error)
   * - PCF "custom" → Would need documentIds (not implemented)
   */
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  private transformRequest(request: SearchRequest): Record<string, any> {
    // Map PCF scope to API scope
    let apiScope: string;
    let entityType: string | undefined;
    let entityId: string | undefined;

    switch (request.scope) {
      case 'matter':
      case 'project':
      case 'invoice':
      case 'account':
      case 'contact':
      case 'document':
        // Entity scopes map to API entity scope with corresponding entityType
        apiScope = 'entity';
        entityType = request.scope;
        entityId = request.scopeId ?? undefined;
        break;
      case 'all':
        apiScope = 'all';
        break;
      case 'custom':
        // Custom scope would use documentIds, not implemented yet
        apiScope = 'documentIds';
        break;
      default:
        // Unknown entity types still scope to "entity" with scopeId
        // so the BFF filters by the record, not returning all documents
        apiScope = 'entity';
        entityType = request.scope;
        entityId = request.scopeId ?? undefined;
    }

    // Convert the client's 0-100 threshold into the BFF's 0.0-1.0 MinScore.
    // Threshold filtering only applies to the semantic path — BFF ignores MinScore
    // when associatedOnly=true, so we always include it.
    const minScore =
      request.filters?.threshold !== undefined && request.filters.threshold > 0
        ? Math.min(1, Math.max(0, request.filters.threshold / 100))
        : 0;

    // Build API request
    return {
      query: request.query,
      scope: apiScope,
      entityType,
      entityId,
      // Pass associatedOnly to the BFF so it can branch to the Dataverse-direct path.
      // The BFF requires scope=entity + entityType + entityId when this is true.
      associatedOnly: request.filters?.associatedOnly ?? false,
      filters: request.filters
        ? {
            documentTypes: request.filters.documentTypes,
            matterTypes: request.filters.matterTypes,
            fileTypes: request.filters.fileTypes,
            dateRange: request.filters.dateRange
              ? {
                  field: 'createdAt',
                  from: request.filters.dateRange.from ? `${request.filters.dateRange.from}T00:00:00Z` : undefined,
                  to: request.filters.dateRange.to ? `${request.filters.dateRange.to}T23:59:59Z` : undefined,
                }
              : undefined,
          }
        : undefined,
      options: {
        limit: request.options?.limit,
        offset: request.options?.offset,
        includeHighlights: request.options?.includeHighlights,
        minScore,
        // Pass search mode (hybrid, vectorOnly, keywordOnly) to BFF
        ...(request.filters?.searchMode && request.filters.searchMode !== 'hybrid'
          ? { hybridMode: request.filters.searchMode }
          : {}),
      },
    };
  }
}

export default SemanticSearchApiService;
