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
import { consoleLogger } from './types';
/**
 * Navigation Property Metadata Client.
 */
export class NavMapClient {
    constructor(options) {
        this.baseUrl = options.baseUrl.endsWith('/') ? options.baseUrl.slice(0, -1) : options.baseUrl;
        this.getAccessToken = options.getAccessToken;
        this.timeout = options.timeout ?? 30000;
        this.logger = options.logger ?? consoleLogger;
        this.onUnauthorized = options.onUnauthorized;
        this.logger.info('NavMapClient', 'Initialized', {
            baseUrl: this.baseUrl,
            timeout: this.timeout,
        });
    }
    /**
     * Get entity set name (plural collection name) for an entity.
     *
     * Example: sprk_document -> sprk_documents
     *
     * API: GET /api/navmap/{entityLogicalName}/entityset
     */
    async getEntitySetName(entityLogicalName) {
        this.logger.info('NavMapClient', 'Getting entity set name', {
            entityLogicalName,
        });
        try {
            const token = await this.getAccessToken();
            const url = `${this.baseUrl}/api/navmap/${encodeURIComponent(entityLogicalName)}/entityset`;
            const response = await this.fetchWithTimeout(url, {
                method: 'GET',
                headers: {
                    Authorization: `Bearer ${token}`,
                    Accept: 'application/json',
                },
            });
            const result = await this.handleResponse(response);
            this.logger.info('NavMapClient', 'Entity set name retrieved', {
                entityLogicalName,
                entitySetName: result.entitySetName,
                source: result.source,
            });
            return result;
        }
        catch (error) {
            this.logger.error('NavMapClient', 'Failed to get entity set name', error);
            throw this.enhanceError(error, `Failed to get entity set name for ${entityLogicalName}`);
        }
    }
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
    async getLookupNavigation(childEntity, relationship) {
        this.logger.info('NavMapClient', 'Getting lookup navigation', {
            childEntity,
            relationship,
        });
        try {
            const token = await this.getAccessToken();
            const url = `${this.baseUrl}/api/navmap/${encodeURIComponent(childEntity)}/${encodeURIComponent(relationship)}/lookup`;
            const response = await this.fetchWithTimeout(url, {
                method: 'GET',
                headers: {
                    Authorization: `Bearer ${token}`,
                    Accept: 'application/json',
                },
            });
            const result = await this.handleResponse(response);
            this.logger.info('NavMapClient', 'Lookup navigation retrieved', {
                childEntity,
                relationship,
                navigationPropertyName: result.navigationPropertyName,
                targetEntity: result.targetEntity,
                source: result.source,
            });
            return result;
        }
        catch (error) {
            this.logger.error('NavMapClient', 'Failed to get lookup navigation', error);
            throw this.enhanceError(error, `Failed to get lookup navigation for ${childEntity}.${relationship}`);
        }
    }
    /**
     * Get collection navigation property name for a parent -> child relationship.
     *
     * Example: sprk_matter + sprk_matter_document -> sprk_matter_document
     *
     * API: GET /api/navmap/{parentEntity}/{relationship}/collection
     */
    async getCollectionNavigation(parentEntity, relationship) {
        this.logger.info('NavMapClient', 'Getting collection navigation', {
            parentEntity,
            relationship,
        });
        try {
            const token = await this.getAccessToken();
            const url = `${this.baseUrl}/api/navmap/${encodeURIComponent(parentEntity)}/${encodeURIComponent(relationship)}/collection`;
            const response = await this.fetchWithTimeout(url, {
                method: 'GET',
                headers: {
                    Authorization: `Bearer ${token}`,
                    Accept: 'application/json',
                },
            });
            const result = await this.handleResponse(response);
            this.logger.info('NavMapClient', 'Collection navigation retrieved', {
                parentEntity,
                relationship,
                collectionPropertyName: result.collectionPropertyName,
                source: result.source,
            });
            return result;
        }
        catch (error) {
            this.logger.error('NavMapClient', 'Failed to get collection navigation', error);
            throw this.enhanceError(error, `Failed to get collection navigation for ${parentEntity}.${relationship}`);
        }
    }
    // -----------------------------------------------------------------------
    // Private helpers
    // -----------------------------------------------------------------------
    /**
     * Fetch with timeout support and automatic 401 retry.
     */
    async fetchWithTimeout(url, options) {
        let attempt = 0;
        const maxAttempts = 2;
        while (attempt < maxAttempts) {
            attempt++;
            const controller = new AbortController();
            const timeoutId = setTimeout(() => controller.abort(), this.timeout);
            try {
                const response = await fetch(url, {
                    ...options,
                    signal: controller.signal,
                });
                clearTimeout(timeoutId);
                if (response.ok || attempt === maxAttempts) {
                    return response;
                }
                if (response.status === 401 && attempt < maxAttempts) {
                    this.logger.warn('NavMapClient', '401 Unauthorized -- clearing token cache and retrying', {
                        url,
                        attempt,
                        maxAttempts,
                    });
                    this.onUnauthorized?.();
                    const newToken = await this.getAccessToken();
                    if (options.headers) {
                        options.headers['Authorization'] = `Bearer ${newToken}`;
                    }
                    this.logger.info('NavMapClient', 'Retrying request with fresh token');
                    continue;
                }
                return response;
            }
            catch (error) {
                clearTimeout(timeoutId);
                if (error instanceof Error && error.name === 'AbortError') {
                    throw new Error(`Request timeout after ${this.timeout}ms`);
                }
                throw error;
            }
        }
        throw new Error('Unexpected error in fetchWithTimeout retry logic');
    }
    /**
     * Handle API response and parse JSON.
     */
    async handleResponse(response) {
        if (!response.ok) {
            let errorMessage = `HTTP ${response.status}: ${response.statusText}`;
            let errorDetails = '';
            try {
                const errorData = await response.json();
                errorMessage = errorData.error || errorMessage;
                errorDetails = errorData.details || '';
            }
            catch {
                try {
                    errorDetails = await response.text();
                }
                catch {
                    // Ignore
                }
            }
            const userFriendlyMessage = this.getUserFriendlyErrorMessage(response.status, errorMessage);
            const error = new Error(userFriendlyMessage);
            error.details = errorDetails;
            error.status = response.status;
            error.originalMessage = errorMessage;
            throw error;
        }
        if (response.status === 204 || response.headers.get('content-length') === '0') {
            return undefined;
        }
        try {
            return await response.json();
        }
        catch (error) {
            this.logger.error('NavMapClient', 'Failed to parse response JSON', error);
            throw new Error('Invalid JSON response from server');
        }
    }
    getUserFriendlyErrorMessage(status, originalMessage) {
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
                return originalMessage;
        }
    }
    enhanceError(error, context) {
        if (error instanceof Error) {
            error.message = `${context}: ${error.message}`;
            return error;
        }
        return new Error(`${context}: ${String(error)}`);
    }
}
//# sourceMappingURL=NavMapClient.js.map