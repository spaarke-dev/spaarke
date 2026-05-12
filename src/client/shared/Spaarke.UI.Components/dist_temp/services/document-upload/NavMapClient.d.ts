/**
 * Navigation Property Metadata Client
 *
 * Provides type-safe methods for retrieving Dataverse navigation property metadata
 * from the BFF API NavMapEndpoints. Solves the case-sensitivity issue for @odata.bind
 * operations (e.g., sprk_Matter vs sprk_matter).
 *
 * Architecture: 3-layer fallback (Server -> Cache -> Hardcoded)
 * Authentication: Uses ITokenProvider (works with both PCF and Code Page contexts)
 *
 * ADR Compliance:
 * - ADR-007: All metadata queries through BFF API
 * - ADR-008: Requires authentication (Bearer token via ITokenProvider)
 *
 * @version 1.0.0
 */
import type { ITokenProvider, ILogger, LookupNavigationResponse } from './types';
/**
 * Entity Set Name Response from BFF NavMap API.
 */
export interface EntitySetNameResponse {
    /** Entity logical name (input) */
    entityLogicalName: string;
    /** Entity set name (plural) */
    entitySetName: string;
    /** Data source: "dataverse", "cache", or "hardcoded" */
    source: string;
}
/**
 * Collection Navigation Response from BFF NavMap API.
 */
export interface CollectionNavigationResponse {
    /** Parent entity logical name (input) */
    parentEntity: string;
    /** Relationship schema name (input) */
    relationship: string;
    /** Collection navigation property name */
    collectionPropertyName: string;
    /** Data source: "dataverse", "cache", or "hardcoded" */
    source: string;
}
/**
 * Configuration for NavMapClient.
 */
export interface NavMapClientOptions {
    /** BFF API base URL */
    baseUrl: string;
    /** Token provider function */
    getAccessToken: ITokenProvider;
    /** Request timeout in milliseconds (default: 30000 = 30 seconds) */
    timeout?: number;
    /** Logger implementation */
    logger?: ILogger;
    /** Callback invoked on 401 responses before retry */
    onUnauthorized?: () => void;
}
/**
 * Navigation Property Metadata Client.
 */
export declare class NavMapClient {
    private readonly baseUrl;
    private readonly timeout;
    private readonly getAccessToken;
    private readonly logger;
    private readonly onUnauthorized?;
    constructor(options: NavMapClientOptions);
    /**
     * Get entity set name (plural collection name) for an entity.
     *
     * Example: sprk_document -> sprk_documents
     *
     * API: GET /api/navmap/{entityLogicalName}/entityset
     */
    getEntitySetName(entityLogicalName: string): Promise<EntitySetNameResponse>;
    /**
     * Get lookup navigation property metadata for a child -> parent relationship.
     *
     * This is the MOST CRITICAL method -- it returns the exact case-sensitive
     * navigation property name required for @odata.bind operations.
     *
     * Example: sprk_document + sprk_matter_document -> sprk_Matter (capital M)
     *
     * API: GET /api/navmap/{childEntity}/{relationship}/lookup
     */
    getLookupNavigation(childEntity: string, relationship: string): Promise<LookupNavigationResponse>;
    /**
     * Get collection navigation property name for a parent -> child relationship.
     *
     * Example: sprk_matter + sprk_matter_document -> sprk_matter_document
     *
     * API: GET /api/navmap/{parentEntity}/{relationship}/collection
     */
    getCollectionNavigation(parentEntity: string, relationship: string): Promise<CollectionNavigationResponse>;
    /**
     * Fetch with timeout support and automatic 401 retry.
     */
    private fetchWithTimeout;
    /**
     * Handle API response and parse JSON.
     */
    private handleResponse;
    private getUserFriendlyErrorMessage;
    private enhanceError;
}
//# sourceMappingURL=NavMapClient.d.ts.map