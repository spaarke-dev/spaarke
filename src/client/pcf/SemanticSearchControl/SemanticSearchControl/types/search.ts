/**
 * TypeScript interfaces for Semantic Search API contract.
 * Matches the BFF API /api/ai/search/semantic endpoint.
 *
 * @see spec.md for API contract details
 */

/**
 * Search scope modes
 * - "all": Search across all documents (standalone search page)
 * - "matter", "project", "invoice", "account", "contact": Entity-scoped search
 * - "custom": Custom scope using documentIds (future)
 */
export type SearchScope = "all" | "matter" | "project" | "invoice" | "account" | "contact" | "custom";

/**
 * Date range filter
 */
export interface DateRange {
    from: string | null;
    to: string | null;
}

/**
 * Filters applied to semantic search
 */
export interface SearchFilters {
    documentTypes: string[];
    matterTypes: string[];
    dateRange: DateRange | null;
    fileTypes: string[];
}

/**
 * Pagination and result options
 */
export interface SearchOptions {
    limit: number;
    offset: number;
    includeHighlights: boolean;
}

/**
 * Request body for semantic search API
 */
export interface SearchRequest {
    query: string;
    scope: SearchScope;
    scopeId: string | null;
    filters: SearchFilters;
    options: SearchOptions;
}

/**
 * Single search result from the API
 */
export interface SearchResult {
    documentId: string;
    name: string;
    fileType: string;
    documentType: string;
    /** Parent entity name (e.g., matter name) - maps to API parentEntityName */
    matterName: string | null;
    /** Parent entity ID - maps to API parentEntityId */
    matterId: string | null;
    /** Creation timestamp - maps to API createdAt */
    createdAt: string | null;
    /** Combined relevance score (0-1 normalized) */
    combinedScore: number;
    highlights: string[];
    fileUrl: string;
    recordUrl: string;
    /** Parent entity type - maps to API parentEntityType */
    parentEntityType?: string;
    /** Parent entity ID - maps to API parentEntityId */
    parentEntityId?: string;
    entityLogicalName?: string;
    recordId?: string;
}

/**
 * Metadata about the search execution
 */
export interface SearchMetadata {
    searchTimeMs: number;
    query: string;
}

/**
 * Response from semantic search API
 */
export interface SearchResponse {
    results: SearchResult[];
    totalCount: number;
    metadata: SearchMetadata;
}

/**
 * Search state for hook management
 */
export type SearchState = "idle" | "loading" | "loadingMore" | "error" | "success";

/**
 * Error information
 */
export interface SearchError {
    message: string;
    code?: string;
    retryable: boolean;
}
