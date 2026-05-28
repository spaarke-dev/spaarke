/**
 * TypeScript interfaces for Semantic Search API contract.
 * Matches the BFF API /api/ai/search/semantic endpoint.
 *
 * @see spec.md for API contract details
 */

/**
 * Search scope modes
 * - "all": Search across all documents (standalone search page)
 * - "entity": Auto-detect entity type from form context (resolves to matter/project/invoice/etc.)
 * - "matter", "project", "invoice", "account", "contact": Explicit entity-scoped search
 * - "custom": Custom scope using documentIds (future)
 */
export type SearchScope =
  | 'all'
  | 'entity'
  | 'matter'
  | 'project'
  | 'invoice'
  | 'account'
  | 'contact'
  | 'document'
  | 'custom'
  | (string & {});

/**
 * Search mode for controlling how vector and keyword search are combined.
 * - "hybrid": Reciprocal Rank Fusion of vector + keyword (default)
 * - "vectorOnly": Pure semantic/concept search
 * - "keywordOnly": Pure BM25 keyword search
 */
export type SearchMode = 'hybrid' | 'vectorOnly' | 'keywordOnly';

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
  /** Minimum relevance score threshold (0-100). 0 = show all results. */
  threshold: number;
  /** Search mode: hybrid (default), vectorOnly, or keywordOnly */
  searchMode: SearchMode;
  /** When true, only show documents directly associated to the parent record (not semantic matches). */
  associatedOnly?: boolean;
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
  matterName: string | null;
  matterId: string | null;
  createdAt: string | null;
  combinedScore: number;
  highlights: string[];
  fileUrl: string;
  recordUrl: string;
  entityLogicalName?: string;
  recordId?: string;
  createdBy: string | null;
  /**
   * Document last-modified timestamp. Populated by FR-BFF-01 (task 050) post-search
   * projection from Dataverse `modifiedon`; falls back to AI Search `updatedAt` upstream.
   * Consumed by the Documents PCF list view for default sort (`modifiedAt DESC`).
   */
  modifiedAt: string | null;
  /**
   * Name of user who last modified the document. From FR-BFF-01 post-search enrichment
   * of `_modifiedby_value` formatted-value. Consumed by the list view "Modified by" column.
   */
  modifiedBy: string | null;
  summary: string | null;
  tldr: string | null;
  /** SPE driveItem ID. Needed alongside driveId to invoke AI analysis. */
  speFileId?: string;
  /** SPE drive ID. Needed alongside speFileId to invoke AI analysis. */
  driveId?: string;
  /**
   * v1.1.50 — Relationship origin tag, used to drive the list-view
   * Relationship + Similarity pill styling (Items 3 + 5).
   *
   * - `'associated'`: result came ONLY from the direct-association
   *   (Dataverse-direct) path — i.e. parent record FK match. Pill label
   *   "Same Matter", green tint badge, similarity column shows no
   *   percentage (the BFF returns 0 score on this path).
   * - `'semantic'`: result came ONLY from the Azure AI Search semantic
   *   path. Pill label "Semantic", brand-colored tint badge, similarity
   *   column shows the combinedScore percentage in a light-yellow pill.
   * - `'both'` (v1.1.51, Item 2): result appeared in BOTH paths during
   *   the client-side `searchUnion` merge — same `documentId` was returned
   *   by the direct-association query AND the semantic AI search.
   *   Relationship pill renders "Same Matter" (canonical, stronger
   *   signal), but the Similarity column STILL surfaces the semantic %
   *   so the user sees the semantic match strength they expected.
   *   Without this tag (the v1.1.50 behavior), the row got tagged
   *   `'associated'` and the semantic % was hidden — which is what UAT
   *   round 6 flagged ("Search documents only return 'Same matter' not
   *   Semantic related").
   *
   * Tagged client-side by `SemanticSearchApiService.searchUnion` after
   * merging both paths; on dedupe (same docId in both), the row is tagged
   * `'both'` (v1.1.51 — was `'associated'` in v1.1.50).
   *
   * When undefined (legacy single-path requests), the UI falls back to
   * inferring from `combinedScore` (zero → associated, >0 → semantic).
   */
  relationship?: 'associated' | 'semantic' | 'both';
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
export type SearchState = 'idle' | 'loading' | 'loadingMore' | 'error' | 'success';

/**
 * Error information
 */
export interface SearchError {
  message: string;
  code?: string;
  retryable: boolean;
}
