/**
 * Comprehensive TypeScript type definitions for the SemanticSearch code page.
 * All types match BFF API JSON contracts (camelCase from [JsonPropertyName] attributes).
 *
 * @see spec.md for API contract details
 * @see src/server/api/Sprk.Bff.Api/Models/Ai/SemanticSearch/ for document search models
 * @see src/server/api/Sprk.Bff.Api/Models/Ai/RecordSearch/ for record search models
 */

// =============================================
// Domain Types
// =============================================

/** Active search domain — determines which entity type tab is active */
export type SearchDomain = "documents" | "matters" | "projects" | "invoices";

/** View mode toggle — grid (tabular) or graph (cluster visualization) */
export type ViewMode = "grid" | "graph";

/** Graph clustering category — determines how results are grouped in graph view */
export type GraphClusterBy = "MatterType" | "PracticeArea" | "DocumentType" | "Organization" | "PersonContact";

/** Hybrid search mode — matches C# HybridSearchMode / RecordHybridSearchMode constants */
export type HybridMode = "rrf" | "vectorOnly" | "keywordOnly";

/** Sort direction for grid columns and saved searches */
export type SortDirection = "asc" | "desc";

// =============================================
// Filter Types
// =============================================

/** Single selectable filter option with optional result count */
export interface FilterOption {
    value: string;
    label: string;
    count?: number;
}

/** Date range with nullable boundaries (ISO 8601 strings) */
export interface DateRange {
    from: string | null;
    to: string | null;
}

/**
 * Client-side search filters — unified filter state used by the UI.
 * Maps to C# SearchFilters (document search) and RecordSearchFilters (record search)
 * depending on the active domain.
 */
export interface SearchFilters {
    documentTypes: string[];
    fileTypes: string[];
    matterTypes: string[];
    dateRange: DateRange;
    threshold: number;
    searchMode: HybridMode;
    entityTypes?: string[];
}

// =============================================
// Document Search Types (POST /api/ai/search)
// =============================================
// Matches C# SemanticSearchRequest (JsonPropertyName attributes)

/**
 * Request body for document semantic search.
 * @see SemanticSearchRequest.cs
 */
export interface DocumentSearchRequest {
    /** Search query text. Max 1000 characters. */
    query?: string;
    /** Search scope: "all", "entity", or "documentIds". */
    scope: string;
    /** Parent entity type when scope=entity (e.g., "matter", "project"). */
    entityType?: string;
    /** Parent entity ID (GUID) when scope=entity. */
    entityId?: string;
    /** List of document IDs when scope=documentIds. Max 100. */
    documentIds?: string[];
    /** Optional search filters. */
    filters?: DocumentSearchFilters;
    /** Pagination and behavior options. */
    options?: DocumentSearchOptions;
}

/**
 * Filters for document semantic search.
 * Matches C# SearchFilters with [JsonPropertyName] attributes.
 * @see SearchFilters.cs
 */
export interface DocumentSearchFilters {
    /** Filter by parent entity types (e.g., "matter", "project", "invoice"). */
    entityTypes?: string[];
    /** Filter by document types (e.g., "Contract", "Agreement"). */
    documentTypes?: string[];
    /** Filter by file types/extensions (e.g., "pdf", "docx"). */
    fileTypes?: string[];
    /** Filter by document tags. */
    tags?: string[];
    /** Filter by date range. */
    dateRange?: DateRangeFilter;
}

/**
 * Date range filter for document search.
 * Matches C# DateRangeFilter with [JsonPropertyName] attributes.
 * @see SearchFilters.cs — DateRangeFilter record
 */
export interface DateRangeFilter {
    /** Field to filter on: "createdAt" or "updatedAt". */
    field: string;
    /** Start of date range (inclusive). ISO 8601 format. */
    from?: string | null;
    /** End of date range (inclusive). ISO 8601 format. */
    to?: string | null;
}

/**
 * Options for document search pagination and behavior.
 * Matches C# SearchOptions with [JsonPropertyName] attributes.
 * @see SearchOptions.cs
 */
export interface DocumentSearchOptions {
    /** Maximum number of results to return. Range: 1-50, default: 20. */
    limit: number;
    /** Number of results to skip for pagination. Range: 0-1000, default: 0. */
    offset: number;
    /** Whether to include highlighted text snippets. Default: true. */
    includeHighlights: boolean;
    /** Hybrid search mode: "rrf", "vectorOnly", or "keywordOnly". Default: "rrf". */
    hybridMode: HybridMode;
}

/**
 * Individual document search result.
 * Matches C# SearchResult with [JsonPropertyName] attributes.
 * @see SearchResult.cs
 */
export interface DocumentSearchResult {
    /** Spaarke document ID (GUID). */
    documentId?: string;
    /** SharePoint Embedded file ID. */
    speFileId?: string;
    /** Document display name. */
    name?: string;
    /** Document type classification (e.g., "Contract", "Invoice"). */
    documentType?: string;
    /** File extension/type (e.g., "pdf", "docx"). */
    fileType?: string;
    /** Combined relevance score from RRF fusion (0.0-1.0). */
    combinedScore: number;
    /** Vector similarity score. */
    similarity?: number;
    /** Keyword/BM25 score. */
    keywordScore?: number;
    /** Highlighted text snippets containing matching content. */
    highlights?: string[];
    /** Parent entity type (matter, project, invoice, account, contact). */
    parentEntityType?: string;
    /** Parent entity ID (GUID). */
    parentEntityId?: string;
    /** Parent entity display name. */
    parentEntityName?: string;
    /** URL to access the file content. */
    fileUrl?: string;
    /** URL to the Dataverse record. */
    recordUrl?: string;
    /** Document creation timestamp. ISO 8601 string. */
    createdAt?: string;
    /** Document last update timestamp. ISO 8601 string. */
    updatedAt?: string;
    /** Name of user who created the document. */
    createdBy?: string;
    /** AI-generated full summary. */
    summary?: string;
    /** AI-generated TL;DR. */
    tldr?: string;
}

/**
 * Response from document semantic search API.
 * Matches C# SemanticSearchResponse with [JsonPropertyName] attributes.
 * @see SemanticSearchResponse.cs
 */
export interface DocumentSearchResponse {
    /** List of search results. */
    results: DocumentSearchResult[];
    /** Search metadata including timing and warnings. */
    metadata: DocumentSearchMetadata;
}

/**
 * Metadata about the document search execution.
 * Matches C# SearchMetadata with [JsonPropertyName] attributes.
 * @see SearchMetadata.cs
 */
export interface DocumentSearchMetadata {
    /** Total number of matching documents in the index. */
    totalResults: number;
    /** Number of results returned in this response. */
    returnedResults: number;
    /** Total search execution time in milliseconds. */
    searchDurationMs: number;
    /** Embedding generation time in milliseconds. */
    embeddingDurationMs: number;
    /** The actual search mode executed (may differ from requested if fallback occurred). */
    executedMode?: string;
    /** Filters that were applied to the search. */
    appliedFilters?: AppliedFilters;
    /** Any warnings generated during search execution. */
    warnings?: SearchWarning[];
}

/**
 * Filters that were applied to the search (echoed back in metadata).
 * Matches C# AppliedFilters with [JsonPropertyName] attributes.
 * @see SearchMetadata.cs — AppliedFilters record
 */
export interface AppliedFilters {
    /** Search scope that was applied. */
    scope?: string;
    /** Entity type filter (when scope=entity). */
    entityType?: string;
    /** Entity ID filter (when scope=entity). */
    entityId?: string;
    /** Number of document IDs filtered (when scope=documentIds). */
    documentIdCount?: number;
    /** Entity types that were filtered. */
    entityTypes?: string[];
    /** Document types that were filtered. */
    documentTypes?: string[];
    /** File types that were filtered. */
    fileTypes?: string[];
    /** Tags that were filtered. */
    tags?: string[];
    /** Date range that was applied. */
    dateRange?: AppliedDateRange;
}

/**
 * Applied date range filter details (echoed back in metadata).
 * Matches C# AppliedDateRange with [JsonPropertyName] attributes.
 * @see SearchMetadata.cs — AppliedDateRange record
 */
export interface AppliedDateRange {
    /** Start of date range. ISO 8601 string. */
    from?: string | null;
    /** End of date range. ISO 8601 string. */
    to?: string | null;
}

/**
 * Warning generated during search execution.
 * Matches C# SearchWarning with [JsonPropertyName] attributes.
 * @see SearchMetadata.cs — SearchWarning record
 */
export interface SearchWarning {
    /** Warning code (e.g., "EMBEDDING_UNAVAILABLE", "EMBEDDING_FALLBACK"). */
    code: string;
    /** Human-readable warning message. */
    message: string;
    /** Optional additional details. */
    details?: string;
}

// =============================================
// Record Search Types (POST /api/ai/search/records)
// =============================================
// Matches C# RecordSearch models (JsonPropertyName attributes)

/**
 * Request body for record search (Matters, Projects, Invoices).
 * @see RecordSearchRequest.cs
 */
export interface RecordSearchRequest {
    /** Search query text. Required. Max 1000 characters. */
    query: string;
    /** Dataverse entity logical names to search (e.g., "sprk_matter", "sprk_project"). At least one required. */
    recordTypes: string[];
    /** Optional filters for organizations, people, and reference numbers. */
    filters?: RecordSearchFilters;
    /** Search options for pagination and hybrid mode. */
    options?: RecordSearchOptions;
}

/**
 * Filters for record search.
 * Matches C# RecordSearchFilters with [JsonPropertyName] attributes.
 * @see RecordSearchFilters.cs
 */
export interface RecordSearchFilters {
    /** Filter by organization names associated with records. */
    organizations?: string[];
    /** Filter by people (contact names) associated with records. */
    people?: string[];
    /** Filter by reference numbers (e.g., matter numbers, project codes, invoice numbers). */
    referenceNumbers?: string[];
}

/**
 * Options for record search pagination and behavior.
 * Matches C# RecordSearchOptions with [JsonPropertyName] attributes.
 * @see RecordSearchOptions.cs
 */
export interface RecordSearchOptions {
    /** Maximum number of results to return. Range: 1-50, default: 20. */
    limit: number;
    /** Number of results to skip for pagination. Range: 0-1000, default: 0. */
    offset: number;
    /** Hybrid search mode: "rrf", "vectorOnly", or "keywordOnly". Default: "rrf". */
    hybridMode: HybridMode;
}

/**
 * Individual record search result.
 * Matches C# RecordSearchResult with [JsonPropertyName] attributes.
 * @see RecordSearchResult.cs
 */
export interface RecordSearchResult {
    /** Dataverse record ID (GUID). */
    recordId: string;
    /** Dataverse entity logical name (e.g., "sprk_matter", "sprk_project", "sprk_invoice"). */
    recordType: string;
    /** Display name of the record. */
    recordName: string;
    /** Optional description or summary of the record. */
    recordDescription?: string;
    /** Combined relevance score from RRF fusion (0.0-1.0). */
    confidenceScore: number;
    /** AI-generated explanation strings describing why this record matched. */
    matchReasons?: string[];
    /** Organizations associated with this record. */
    organizations?: string[];
    /** People (contact names) associated with this record. */
    people?: string[];
    /** Keywords extracted from the record content. */
    keywords?: string[];
    /** Record creation timestamp. ISO 8601 string. */
    createdAt?: string;
    /** Record last modification timestamp. ISO 8601 string. */
    modifiedAt?: string;
}

/**
 * Response from record search API.
 * Matches C# RecordSearchResponse with [JsonPropertyName] attributes.
 * @see RecordSearchResponse.cs
 */
export interface RecordSearchResponse {
    /** List of matching record search results. */
    results: RecordSearchResult[];
    /** Search metadata including total count, timing, and mode. */
    metadata: RecordSearchMetadata;
}

/**
 * Metadata about the record search execution.
 * Matches C# RecordSearchMetadata with [JsonPropertyName] attributes.
 * @see RecordSearchMetadata.cs
 */
export interface RecordSearchMetadata {
    /** Total number of matching records in the index. */
    totalCount: number;
    /** Total search execution time in milliseconds. */
    searchTime: number;
    /** The hybrid search mode that was executed. */
    hybridMode: string;
}

// =============================================
// Grid Types
// =============================================

/** Column definition for the results data grid */
export interface GridColumnDef {
    /** Unique column identifier. */
    key: string;
    /** Display label for column header. */
    label: string;
    /** Preferred column width in pixels. */
    width?: number;
    /** Minimum column width in pixels. */
    minWidth?: number;
    /** Whether the column supports sorting. */
    sortable?: boolean;
    /** Custom render function for cell content. */
    render?: (value: unknown, row: Record<string, unknown>) => string;
}

// =============================================
// Graph Types
// =============================================

/** Data for a cluster node in the graph visualization */
export interface ClusterNodeData {
    /** Unique key identifying this cluster. */
    clusterKey: string;
    /** Display label for the cluster. */
    clusterLabel: string;
    /** Number of records in this cluster. */
    recordCount: number;
    /** Average similarity score of records in this cluster (0-1). */
    avgSimilarity: number;
    /** Category that this cluster groups by. */
    category: GraphClusterBy;
    /** Top result names for preview (truncated). */
    topResults?: { name: string }[];
    /** Whether this cluster is currently expanded to show record nodes. */
    isExpanded?: boolean;
}

/** Data for an individual record node in the graph visualization */
export interface RecordNodeData {
    /** Unique record identifier (documentId or recordId). */
    recordId: string;
    /** Display name of the record. */
    recordName: string;
    /** Similarity/confidence score for this record (0-1). */
    similarity: number;
    /** Parent entity name (for documents). */
    parentEntityName?: string;
    /** Which search domain this record belongs to. */
    domain: SearchDomain;
    /** Dataverse entity logical name (e.g., sprk_document, sprk_matter). */
    recordType?: string;
}

/** Data for edges between cluster nodes */
export interface ClusterEdgeData {
    /** Edge weight (e.g., relationship count between clusters). */
    weight: number;
}

// =============================================
// Saved Search Types
// =============================================

/**
 * Saved search configuration stored in sprk_gridconfiguration.
 * Captures filter strategy + field selections per domain.
 * @see spec.md — Saved Search Schema section
 */
export interface SavedSearch {
    /** Record ID in sprk_gridconfiguration (GUID). Undefined for new unsaved searches. */
    id?: string;
    /** User-defined name for the saved search. */
    name: string;
    /** Search domain tab. */
    searchDomain: SearchDomain;
    /** Search query text. */
    query: string;
    /** Applied filters. */
    filters: SearchFilters;
    /** Active view mode. */
    viewMode: ViewMode;
    /** Visible column keys for grid view. */
    columns: string[];
    /** Column key to sort by. */
    sortColumn: string;
    /** Sort direction. */
    sortDirection: SortDirection;
    /** Graph clustering category (used when viewMode is "graph"). */
    graphClusterBy?: GraphClusterBy;
}

// =============================================
// App / URL Types
// =============================================

/** Props passed from index.tsx entry point to App */
export interface AppProps {
    /** Pre-filled search query from URL */
    initialQuery: string;
    /** Active search domain tab */
    initialDomain: SearchDomain;
    /** Search scope filter */
    initialScope: string;
    /** Entity record ID for contextual search */
    initialEntityId: string;
    /** Saved search to load on startup */
    initialSavedSearchId: string;
    /** Whether the current theme is dark */
    isDark: boolean;
}

/** Parsed URL parameters supported by the code page */
export interface AppUrlParams {
    /** Theme override: "light", "dark", or "high-contrast". */
    theme?: string;
    /** Initial search query text. */
    query?: string;
    /** Initial search domain tab. */
    domain?: SearchDomain;
    /** Search scope: "all", "entity", "documentIds". */
    scope?: string;
    /** Entity ID for scoped searches. */
    entityId?: string;
    /** Saved search ID to load on startup. */
    savedSearchId?: string;
}

/**
 * API error response following RFC 7807 ProblemDetails format.
 * @see ADR-019 — ProblemDetails for all HTTP failures
 */
export interface ApiError {
    /** HTTP status code. */
    status: number;
    /** Error type URI. */
    type?: string;
    /** Short human-readable summary of the problem. */
    title: string;
    /** Detailed human-readable explanation. */
    detail?: string;
    /** Validation errors keyed by field name. */
    errors?: Record<string, string[]>;
}

// =============================================
// Search State Types
// =============================================

/** Search execution state for hook management */
export type SearchState = "idle" | "loading" | "loadingMore" | "error" | "success";

/** Search error information */
export interface SearchError {
    /** User-friendly error message. */
    message: string;
    /** Error code from API (e.g., warning code). */
    code?: string;
    /** Whether the operation can be retried. */
    retryable: boolean;
}

// =============================================
// Constants
// =============================================

/** Known Dataverse entity logical names for record search */
export const RecordEntityTypes = {
    Matter: "sprk_matter",
    Project: "sprk_project",
    Invoice: "sprk_invoice",
} as const;

/** Valid search scope values */
export const SearchScopes = {
    All: "all",
    Entity: "entity",
    DocumentIds: "documentIds",
} as const;

/** Standard hybrid search modes */
export const HybridModes = {
    Rrf: "rrf",
    VectorOnly: "vectorOnly",
    KeywordOnly: "keywordOnly",
} as const;
