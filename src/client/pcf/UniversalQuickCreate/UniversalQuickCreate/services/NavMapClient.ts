/**
 * Navigation Property Metadata Client (Phase 7)
 *
 * Provides type-safe methods for retrieving Dataverse navigation property metadata
 * from the BFF API NavMapEndpoints. Solves the case-sensitivity issue for @odata.bind
 * operations (e.g., sprk_Matter vs sprk_matter).
 *
 * Architecture: 3-layer fallback (Server → Cache → Hardcoded)
 * - Layer 1 (Server): BFF API queries Dataverse EntityDefinitions metadata
 * - Layer 2 (L1 Cache): BFF API in-memory cache (15-minute TTL)
 * - Layer 3 (Hardcoded): BFF API fallback values for known entities
 *
 * Authentication: Uses same MSAL token as SdapApiClient (OBO flow)
 * Error Handling: Automatic retry with cache clear on 401 errors
 *
 * ADR Compliance:
 * - ADR-001: Uses fetch API (no dependencies)
 * - ADR-008: Requires authentication (Bearer token)
 * - ADR-010: Minimal dependencies (same pattern as SdapApiClient)
 */

import { logger } from '../utils/logger';
import { MsalAuthProvider } from './auth/MsalAuthProvider';

/**
 * Entity Set Name Response
 * Returned by GET /api/navmap/{entityLogicalName}/entityset
 */
export interface EntitySetNameResponse {
    /** Entity logical name (input) - e.g., "sprk_document" */
    entityLogicalName: string;

    /** Entity set name (plural) - e.g., "sprk_documents" */
    entitySetName: string;

    /** Data source: "dataverse", "cache", or "hardcoded" */
    source: string;
}

/**
 * Lookup Navigation Response (CRITICAL for @odata.bind)
 * Returned by GET /api/navmap/{childEntity}/{relationship}/lookup
 */
export interface LookupNavigationResponse {
    /** Child entity logical name (input) - e.g., "sprk_document" */
    childEntity: string;

    /** Relationship schema name (input) - e.g., "sprk_matter_document" */
    relationship: string;

    /** Lookup attribute logical name (lowercase) - e.g., "sprk_matter" */
    logicalName: string;

    /** Lookup attribute schema name - e.g., "sprk_Matter" */
    schemaName: string;

    /**
     * Navigation property name for @odata.bind (CASE-SENSITIVE!)
     * Example: "sprk_Matter" (capital M)
     *
     * This is the CRITICAL value that solves the Phase 6 case-sensitivity issue.
     * Use this exact string in @odata.bind operations.
     */
    navigationPropertyName: string;

    /** Target entity logical name (parent) - e.g., "sprk_matter" */
    targetEntity: string;

    /** Data source: "dataverse", "cache", or "hardcoded" */
    source: string;
}

/**
 * Collection Navigation Response
 * Returned by GET /api/navmap/{parentEntity}/{relationship}/collection
 */
export interface CollectionNavigationResponse {
    /** Parent entity logical name (input) - e.g., "sprk_matter" */
    parentEntity: string;

    /** Relationship schema name (input) - e.g., "sprk_matter_document" */
    relationship: string;

    /** Collection navigation property name - e.g., "sprk_matter_document" */
    collectionPropertyName: string;

    /** Data source: "dataverse", "cache", or "hardcoded" */
    source: string;
}

/**
 * Navigation Property Metadata Client
 *
 * Provides methods to query Dataverse navigation property metadata
 * via the BFF API's NavMapEndpoints.
 */
export class NavMapClient {
    private baseUrl: string;
    private timeout: number;
    private getAccessToken: () => Promise<string>;

    constructor(
        baseUrl: string,
        getAccessToken: () => Promise<string>,
        timeout = 30000 // 30 seconds default (metadata queries are fast)
    ) {
        this.baseUrl = baseUrl.endsWith('/') ? baseUrl.slice(0, -1) : baseUrl;
        this.getAccessToken = getAccessToken;
        this.timeout = timeout;

        logger.info('NavMapClient', 'Initialized', {
            baseUrl: this.baseUrl,
            timeout: this.timeout
        });
    }

    /**
     * Get entity set name (plural collection name) for an entity.
     *
     * Example: sprk_document → sprk_documents
     *
     * API: GET /api/navmap/{entityLogicalName}/entityset
     *
     * @param entityLogicalName Entity logical name (e.g., "sprk_document")
     * @returns EntitySetNameResponse with plural entity set name
     * @throws Error if entity not found or API error
     */
    async getEntitySetName(entityLogicalName: string): Promise<EntitySetNameResponse> {
        logger.info('NavMapClient', 'Getting entity set name', { entityLogicalName });

        try {
            const token = await this.getAccessToken();

            const url = `${this.baseUrl}/api/navmap/${encodeURIComponent(entityLogicalName)}/entityset`;

            const response = await this.fetchWithTimeout(
                url,
                {
                    method: 'GET',
                    headers: {
                        'Authorization': `Bearer ${token}`,
                        'Accept': 'application/json'
                    }
                }
            );

            const result = await this.handleResponse<EntitySetNameResponse>(response);

            logger.info('NavMapClient', 'Entity set name retrieved', {
                entityLogicalName,
                entitySetName: result.entitySetName,
                source: result.source
            });

            return result;

        } catch (error) {
            logger.error('NavMapClient', 'Failed to get entity set name', error);
            throw this.enhanceError(error, `Failed to get entity set name for ${entityLogicalName}`);
        }
    }

    /**
     * Get lookup navigation property metadata for a child → parent relationship.
     *
     * This is the MOST CRITICAL method - it returns the exact case-sensitive
     * navigation property name required for @odata.bind operations.
     *
     * Example: sprk_document + sprk_matter_document → sprk_Matter (capital M)
     *
     * API: GET /api/navmap/{childEntity}/{relationship}/lookup
     *
     * @param childEntity Child entity logical name (e.g., "sprk_document")
     * @param relationship Relationship schema name (e.g., "sprk_matter_document")
     * @returns LookupNavigationResponse with case-sensitive navigation property name
     * @throws Error if relationship not found or API error
     */
    async getLookupNavigation(
        childEntity: string,
        relationship: string
    ): Promise<LookupNavigationResponse> {
        logger.info('NavMapClient', 'Getting lookup navigation', {
            childEntity,
            relationship
        });

        try {
            const token = await this.getAccessToken();

            const url = `${this.baseUrl}/api/navmap/${encodeURIComponent(childEntity)}/${encodeURIComponent(relationship)}/lookup`;

            const response = await this.fetchWithTimeout(
                url,
                {
                    method: 'GET',
                    headers: {
                        'Authorization': `Bearer ${token}`,
                        'Accept': 'application/json'
                    }
                }
            );

            const result = await this.handleResponse<LookupNavigationResponse>(response);

            logger.info('NavMapClient', 'Lookup navigation retrieved', {
                childEntity,
                relationship,
                navigationPropertyName: result.navigationPropertyName,
                targetEntity: result.targetEntity,
                source: result.source
            });

            return result;

        } catch (error) {
            logger.error('NavMapClient', 'Failed to get lookup navigation', error);
            throw this.enhanceError(error, `Failed to get lookup navigation for ${childEntity}.${relationship}`);
        }
    }

    /**
     * Get collection navigation property name for a parent → child relationship.
     *
     * Example: sprk_matter + sprk_matter_document → sprk_matter_document
     *
     * API: GET /api/navmap/{parentEntity}/{relationship}/collection
     *
     * @param parentEntity Parent entity logical name (e.g., "sprk_matter")
     * @param relationship Relationship schema name (e.g., "sprk_matter_document")
     * @returns CollectionNavigationResponse with collection property name
     * @throws Error if relationship not found or API error
     */
    async getCollectionNavigation(
        parentEntity: string,
        relationship: string
    ): Promise<CollectionNavigationResponse> {
        logger.info('NavMapClient', 'Getting collection navigation', {
            parentEntity,
            relationship
        });

        try {
            const token = await this.getAccessToken();

            const url = `${this.baseUrl}/api/navmap/${encodeURIComponent(parentEntity)}/${encodeURIComponent(relationship)}/collection`;

            const response = await this.fetchWithTimeout(
                url,
                {
                    method: 'GET',
                    headers: {
                        'Authorization': `Bearer ${token}`,
                        'Accept': 'application/json'
                    }
                }
            );

            const result = await this.handleResponse<CollectionNavigationResponse>(response);

            logger.info('NavMapClient', 'Collection navigation retrieved', {
                parentEntity,
                relationship,
                collectionPropertyName: result.collectionPropertyName,
                source: result.source
            });

            return result;

        } catch (error) {
            logger.error('NavMapClient', 'Failed to get collection navigation', error);
            throw this.enhanceError(error, `Failed to get collection navigation for ${parentEntity}.${relationship}`);
        }
    }

    /**
     * Fetch with timeout support and automatic 401 retry
     *
     * If a 401 error occurs, clears MSAL cache and retries once with fresh token.
     * This handles race conditions where token expires between cache check and API call.
     *
     * Same pattern as SdapApiClient for consistency.
     */
    private async fetchWithTimeout(
        url: string,
        options: RequestInit
    ): Promise<Response> {
        let attempt = 0;
        const maxAttempts = 2;

        while (attempt < maxAttempts) {
            attempt++;

            const controller = new AbortController();
            const timeoutId = setTimeout(() => controller.abort(), this.timeout);

            try {
                const response = await fetch(url, {
                    ...options,
                    signal: controller.signal
                });

                clearTimeout(timeoutId);

                // Success or last attempt - return response
                if (response.ok || attempt === maxAttempts) {
                    return response;
                }

                // 401 Unauthorized - token may have expired during request
                if (response.status === 401 && attempt < maxAttempts) {
                    logger.warn('NavMapClient', '401 Unauthorized response - clearing token cache and retrying', {
                        url,
                        attempt,
                        maxAttempts
                    });

                    // Clear MSAL cache to force fresh token acquisition
                    MsalAuthProvider.getInstance().clearCache();

                    // Get fresh token for retry
                    const newToken = await this.getAccessToken();

                    // Update Authorization header with fresh token
                    if (options.headers) {
                        (options.headers as Record<string, string>)['Authorization'] = `Bearer ${newToken}`;
                    }

                    logger.info('NavMapClient', 'Retrying request with fresh token');

                    // Continue to next iteration (retry)
                    continue;
                }

                // Other errors (non-401) - return immediately
                return response;

            } catch (error) {
                clearTimeout(timeoutId);

                if (error instanceof Error && error.name === 'AbortError') {
                    throw new Error(`Request timeout after ${this.timeout}ms`);
                }

                throw error;
            }
        }

        // Should never reach here, but TypeScript needs return
        throw new Error('Unexpected error in fetchWithTimeout retry logic');
    }

    /**
     * Handle API response and parse JSON
     *
     * Provides user-friendly error messages for common failure scenarios
     * Same pattern as SdapApiClient for consistency.
     */
    private async handleResponse<T>(response: Response): Promise<T> {
        if (!response.ok) {
            let errorMessage = `HTTP ${response.status}: ${response.statusText}`;
            let errorDetails = '';

            try {
                const errorData = await response.json();
                errorMessage = errorData.error || errorMessage;
                errorDetails = errorData.details || '';
            } catch {
                // If parsing fails, use text
                try {
                    errorDetails = await response.text();
                } catch {
                    // Ignore text parsing errors
                }
            }

            // Create user-friendly error messages for common scenarios
            const userFriendlyMessage = this.getUserFriendlyErrorMessage(response.status, errorMessage);

            const error = new Error(userFriendlyMessage) as Error & {
                details?: string;
                status?: number;
                originalMessage?: string;
            };
            error.details = errorDetails;
            error.status = response.status;
            error.originalMessage = errorMessage;

            throw error;
        }

        // For 204 No Content or empty responses
        if (response.status === 204 || response.headers.get('content-length') === '0') {
            return undefined as T;
        }

        try {
            return await response.json();
        } catch (error) {
            logger.error('NavMapClient', 'Failed to parse response JSON', error);
            throw new Error('Invalid JSON response from server');
        }
    }

    /**
     * Get user-friendly error message based on HTTP status code
     */
    private getUserFriendlyErrorMessage(status: number, originalMessage: string): string {
        switch (status) {
            case 401:
                return 'Authentication failed. Your session may have expired. Please refresh the page and try again.';

            case 403:
                return 'Access denied. You do not have permission to query metadata. Please contact your administrator.';

            case 404:
                return 'Metadata not found. The entity or relationship may not exist in Dataverse.';

            case 408:
            case 504:
                return 'Request timeout. The metadata query took too long. Please try again.';

            case 429:
                return 'Too many requests. Please wait a moment and try again.';

            case 500:
                return 'Server error occurred while querying metadata. Please try again later.';

            case 502:
            case 503:
                return 'The service is temporarily unavailable. Please try again in a few minutes.';

            default:
                // For other errors, return original message
                return originalMessage;
        }
    }

    /**
     * Enhance error with context
     */
    private enhanceError(error: unknown, context: string): Error {
        if (error instanceof Error) {
            error.message = `${context}: ${error.message}`;
            return error;
        }

        return new Error(`${context}: ${String(error)}`);
    }
}
