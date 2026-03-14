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
import { consoleLogger } from './types';

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
export class NavMapClient {
  private readonly baseUrl: string;
  private readonly timeout: number;
  private readonly getAccessToken: ITokenProvider;
  private readonly logger: ILogger;
  private readonly onUnauthorized?: () => void;

  constructor(options: NavMapClientOptions) {
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
  async getEntitySetName(entityLogicalName: string): Promise<EntitySetNameResponse> {
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

      const result = await this.handleResponse<EntitySetNameResponse>(response);

      this.logger.info('NavMapClient', 'Entity set name retrieved', {
        entityLogicalName,
        entitySetName: result.entitySetName,
        source: result.source,
      });

      return result;
    } catch (error) {
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
  async getLookupNavigation(childEntity: string, relationship: string): Promise<LookupNavigationResponse> {
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

      const result = await this.handleResponse<LookupNavigationResponse>(response);

      this.logger.info('NavMapClient', 'Lookup navigation retrieved', {
        childEntity,
        relationship,
        navigationPropertyName: result.navigationPropertyName,
        targetEntity: result.targetEntity,
        source: result.source,
      });

      return result;
    } catch (error) {
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
  async getCollectionNavigation(parentEntity: string, relationship: string): Promise<CollectionNavigationResponse> {
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

      const result = await this.handleResponse<CollectionNavigationResponse>(response);

      this.logger.info('NavMapClient', 'Collection navigation retrieved', {
        parentEntity,
        relationship,
        collectionPropertyName: result.collectionPropertyName,
        source: result.source,
      });

      return result;
    } catch (error) {
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
  private async fetchWithTimeout(url: string, options: RequestInit): Promise<Response> {
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
            (options.headers as Record<string, string>)['Authorization'] = `Bearer ${newToken}`;
          }

          this.logger.info('NavMapClient', 'Retrying request with fresh token');
          continue;
        }

        return response;
      } catch (error) {
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
  private async handleResponse<T>(response: Response): Promise<T> {
    if (!response.ok) {
      let errorMessage = `HTTP ${response.status}: ${response.statusText}`;
      let errorDetails = '';

      try {
        const errorData = await response.json();
        errorMessage = errorData.error || errorMessage;
        errorDetails = errorData.details || '';
      } catch {
        try {
          errorDetails = await response.text();
        } catch {
          // Ignore
        }
      }

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

    if (response.status === 204 || response.headers.get('content-length') === '0') {
      return undefined as T;
    }

    try {
      return await response.json();
    } catch (error) {
      this.logger.error('NavMapClient', 'Failed to parse response JSON', error);
      throw new Error('Invalid JSON response from server');
    }
  }

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
        return originalMessage;
    }
  }

  private enhanceError(error: unknown, context: string): Error {
    if (error instanceof Error) {
      error.message = `${context}: ${error.message}`;
      return error;
    }
    return new Error(`${context}: ${String(error)}`);
  }
}
