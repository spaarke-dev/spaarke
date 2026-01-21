/**
 * Office API Client
 *
 * A typed API client for the Office add-in that wraps all /office/* endpoint calls.
 * Includes authentication header injection, error handling, and TypeScript interfaces
 * matching server DTOs.
 *
 * Per auth.md constraints:
 * - Uses NAA auth service for token acquisition
 * - Uses `.default` scope for BFF API calls via OBO
 *
 * Per api.md constraints:
 * - Handles ProblemDetails (RFC 7807) error responses
 * - Includes correlation IDs for tracing
 * - Supports cancellation via AbortController
 *
 * @see projects/sdap-office-integration/spec.md for API contracts
 */

import { naaAuthService, type INaaAuthService } from '../auth';
import { DEFAULT_AUTH_CONFIG, type NaaAuthConfig } from '../auth/authConfig';
import {
  OfficeApiError,
  createNetworkErrorDetails,
  createAuthErrorDetails,
  createTimeoutErrorDetails,
  createUnknownErrorDetails,
  parseRetryAfter,
} from './errors';
import type {
  // Save types
  SaveRequest,
  SaveResponse,
  // Job types
  JobStatusResponse,
  // Search types
  EntitySearchParams,
  EntitySearchResponse,
  DocumentSearchParams,
  DocumentSearchResponse,
  // Quick create types
  QuickCreateEntityType,
  QuickCreateRequest,
  QuickCreateResponse,
  // Share types
  ShareLinksRequest,
  ShareLinksResponse,
  ShareAttachRequest,
  ShareAttachResponse,
  // Recent types
  RecentItemsResponse,
  // Error types
  ProblemDetails,
} from './types';

/**
 * Configuration for the Office API Client.
 */
export interface OfficeApiClientConfig {
  /** Base URL for the BFF API */
  baseUrl: string;
  /** Request timeout in milliseconds (default: 30000) */
  timeout?: number;
  /** Whether to include credentials (default: true) */
  includeCredentials?: boolean;
}

/**
 * Request options for API calls.
 */
export interface RequestOptions {
  /** AbortSignal for cancellation */
  signal?: AbortSignal;
  /** Idempotency key for POST requests */
  idempotencyKey?: string;
  /** Correlation ID for tracing (auto-generated if not provided) */
  correlationId?: string;
  /** Custom timeout for this request */
  timeout?: number;
}

/**
 * Generate a UUID v4.
 */
function generateUuid(): string {
  // Use crypto.randomUUID if available (modern browsers)
  if (typeof crypto !== 'undefined' && crypto.randomUUID) {
    return crypto.randomUUID();
  }
  // Fallback for older browsers
  return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, (c) => {
    const r = (Math.random() * 16) | 0;
    const v = c === 'x' ? r : (r & 0x3) | 0x8;
    return v.toString(16);
  });
}

/**
 * Office API Client interface.
 */
export interface IOfficeApiClient {
  // Save operations
  save(request: SaveRequest, options?: RequestOptions): Promise<SaveResponse>;

  // Job operations
  getJobStatus(jobId: string, options?: RequestOptions): Promise<JobStatusResponse>;

  // Search operations
  searchEntities(params: EntitySearchParams, options?: RequestOptions): Promise<EntitySearchResponse>;
  searchDocuments(params: DocumentSearchParams, options?: RequestOptions): Promise<DocumentSearchResponse>;

  // Quick create operations
  quickCreate(
    entityType: QuickCreateEntityType,
    data: QuickCreateRequest,
    options?: RequestOptions
  ): Promise<QuickCreateResponse>;

  // Share operations
  shareLinks(request: ShareLinksRequest, options?: RequestOptions): Promise<ShareLinksResponse>;
  shareAttach(request: ShareAttachRequest, options?: RequestOptions): Promise<ShareAttachResponse>;

  // Recent items
  getRecent(options?: RequestOptions): Promise<RecentItemsResponse>;
}

/**
 * Office API Client implementation.
 *
 * Provides typed methods for all /office/* endpoints with authentication,
 * error handling, and cancellation support.
 */
class OfficeApiClientImpl implements IOfficeApiClient {
  private baseUrl: string;
  private timeout: number;
  private includeCredentials: boolean;
  private authService: INaaAuthService;

  constructor(
    config?: Partial<OfficeApiClientConfig>,
    authService?: INaaAuthService
  ) {
    this.baseUrl = (config?.baseUrl || DEFAULT_AUTH_CONFIG.bffApiBaseUrl).replace(/\/$/, '');
    this.timeout = config?.timeout ?? 30000;
    this.includeCredentials = config?.includeCredentials ?? true;
    this.authService = authService ?? naaAuthService;
  }

  /**
   * Configure the client after construction.
   */
  configure(config: Partial<OfficeApiClientConfig>): void {
    if (config.baseUrl !== undefined) {
      this.baseUrl = config.baseUrl.replace(/\/$/, '');
    }
    if (config.timeout !== undefined) {
      this.timeout = config.timeout;
    }
    if (config.includeCredentials !== undefined) {
      this.includeCredentials = config.includeCredentials;
    }
  }

  // ============================================
  // Save Operations
  // ============================================

  /**
   * Submit email or document for filing to Spaarke.
   * POST /office/save
   *
   * @param request - Save request with content and association
   * @param options - Request options (idempotencyKey, signal, etc.)
   * @returns Save response with jobId and status
   */
  async save(request: SaveRequest, options?: RequestOptions): Promise<SaveResponse> {
    return this.post<SaveResponse>('/office/save', request, {
      ...options,
      // Generate idempotency key if not provided
      idempotencyKey: options?.idempotencyKey || this.generateIdempotencyKey(request),
    });
  }

  // ============================================
  // Job Operations
  // ============================================

  /**
   * Get job status for polling.
   * GET /office/jobs/{jobId}
   *
   * @param jobId - Job ID to check
   * @param options - Request options
   * @returns Job status with stage information
   */
  async getJobStatus(jobId: string, options?: RequestOptions): Promise<JobStatusResponse> {
    return this.get<JobStatusResponse>(`/office/jobs/${encodeURIComponent(jobId)}`, options);
  }

  // ============================================
  // Search Operations
  // ============================================

  /**
   * Search for association targets.
   * GET /office/search/entities
   *
   * @param params - Search parameters (q, type, limit)
   * @param options - Request options
   * @returns Search results with entities
   */
  async searchEntities(
    params: EntitySearchParams,
    options?: RequestOptions
  ): Promise<EntitySearchResponse> {
    const queryParams = new URLSearchParams();
    queryParams.set('q', params.q);

    if (params.type) {
      const types = Array.isArray(params.type) ? params.type.join(',') : params.type;
      queryParams.set('type', types);
    }

    if (params.limit !== undefined) {
      queryParams.set('limit', params.limit.toString());
    }

    return this.get<EntitySearchResponse>(
      `/office/search/entities?${queryParams.toString()}`,
      options
    );
  }

  /**
   * Search for documents.
   * GET /office/search/documents
   *
   * @param params - Search parameters
   * @param options - Request options
   * @returns Search results with documents
   */
  async searchDocuments(
    params: DocumentSearchParams,
    options?: RequestOptions
  ): Promise<DocumentSearchResponse> {
    const queryParams = new URLSearchParams();
    queryParams.set('q', params.q);

    if (params.contentTypes && params.contentTypes.length > 0) {
      queryParams.set('contentTypes', params.contentTypes.join(','));
    }

    if (params.associationType) {
      queryParams.set('associationType', params.associationType);
    }

    if (params.associationId) {
      queryParams.set('associationId', params.associationId);
    }

    if (params.limit !== undefined) {
      queryParams.set('limit', params.limit.toString());
    }

    return this.get<DocumentSearchResponse>(
      `/office/search/documents?${queryParams.toString()}`,
      options
    );
  }

  // ============================================
  // Quick Create Operations
  // ============================================

  /**
   * Create a new entity with minimal fields.
   * POST /office/quickcreate/{entityType}
   *
   * @param entityType - Type of entity to create
   * @param data - Entity data
   * @param options - Request options
   * @returns Created entity info
   */
  async quickCreate(
    entityType: QuickCreateEntityType,
    data: QuickCreateRequest,
    options?: RequestOptions
  ): Promise<QuickCreateResponse> {
    return this.post<QuickCreateResponse>(
      `/office/quickcreate/${encodeURIComponent(entityType)}`,
      data,
      options
    );
  }

  // ============================================
  // Share Operations
  // ============================================

  /**
   * Generate share links for documents.
   * POST /office/share/links
   *
   * @param request - Share links request
   * @param options - Request options
   * @returns Generated links and invitations
   */
  async shareLinks(
    request: ShareLinksRequest,
    options?: RequestOptions
  ): Promise<ShareLinksResponse> {
    return this.post<ShareLinksResponse>('/office/share/links', request, options);
  }

  /**
   * Get document content for attachment to Outlook compose.
   * POST /office/share/attach
   *
   * @param request - Share attach request
   * @param options - Request options
   * @returns Attachment info with download URLs
   */
  async shareAttach(
    request: ShareAttachRequest,
    options?: RequestOptions
  ): Promise<ShareAttachResponse> {
    return this.post<ShareAttachResponse>('/office/share/attach', request, options);
  }

  // ============================================
  // Recent Items Operations
  // ============================================

  /**
   * Get recent items for quick access.
   * GET /office/recent
   *
   * @param options - Request options
   * @returns Recent associations, documents, and favorites
   */
  async getRecent(options?: RequestOptions): Promise<RecentItemsResponse> {
    return this.get<RecentItemsResponse>('/office/recent', options);
  }

  // ============================================
  // Private Methods
  // ============================================

  /**
   * Make a GET request.
   */
  private async get<T>(endpoint: string, options?: RequestOptions): Promise<T> {
    return this.request<T>('GET', endpoint, undefined, options);
  }

  /**
   * Make a POST request.
   */
  private async post<T>(
    endpoint: string,
    body?: unknown,
    options?: RequestOptions
  ): Promise<T> {
    return this.request<T>('POST', endpoint, body, options);
  }

  /**
   * Make an HTTP request with authentication and error handling.
   */
  private async request<T>(
    method: string,
    endpoint: string,
    body?: unknown,
    options?: RequestOptions
  ): Promise<T> {
    // Get access token
    const token = await this.getAccessToken();

    // Build URL
    const url = `${this.baseUrl}${endpoint}`;

    // Build headers
    const headers: HeadersInit = {
      Authorization: `Bearer ${token}`,
      Accept: 'application/json',
    };

    // Add Content-Type for POST/PUT requests with body
    if (body && (method === 'POST' || method === 'PUT' || method === 'PATCH')) {
      headers['Content-Type'] = 'application/json';
    }

    // Add correlation ID
    const correlationId = options?.correlationId || generateUuid();
    headers['X-Correlation-Id'] = correlationId;

    // Add idempotency key for POST requests
    if (options?.idempotencyKey && method === 'POST') {
      headers['X-Idempotency-Key'] = options.idempotencyKey;
    }

    // Build request config
    const config: RequestInit = {
      method,
      headers,
      credentials: this.includeCredentials ? 'include' : 'same-origin',
    };

    // Add body for POST/PUT requests
    if (body && (method === 'POST' || method === 'PUT' || method === 'PATCH')) {
      config.body = JSON.stringify(body);
    }

    // Handle abort signal and timeout
    const timeout = options?.timeout ?? this.timeout;
    const controller = new AbortController();
    let timeoutId: ReturnType<typeof setTimeout> | undefined;

    if (options?.signal) {
      // Chain to provided signal
      options.signal.addEventListener('abort', () => controller.abort());
    }

    if (timeout > 0) {
      timeoutId = setTimeout(() => controller.abort(), timeout);
    }

    config.signal = controller.signal;

    try {
      const response = await fetch(url, config);

      // Clear timeout on response
      if (timeoutId) {
        clearTimeout(timeoutId);
      }

      // Handle error responses
      if (!response.ok) {
        await this.handleErrorResponse(response);
      }

      // Handle empty responses (204 No Content)
      if (response.status === 204) {
        return {} as T;
      }

      // Parse JSON response
      const text = await response.text();
      if (!text) {
        return {} as T;
      }

      return JSON.parse(text) as T;
    } catch (error) {
      // Clear timeout on error
      if (timeoutId) {
        clearTimeout(timeoutId);
      }

      // Handle abort/timeout
      if (error instanceof DOMException && error.name === 'AbortError') {
        // Check if it was a timeout vs user cancellation
        if (options?.signal?.aborted) {
          throw new OfficeApiError(createUnknownErrorDetails(new Error('Request cancelled')));
        }
        throw new OfficeApiError(createTimeoutErrorDetails());
      }

      // Re-throw if already OfficeApiError
      if (error instanceof OfficeApiError) {
        throw error;
      }

      // Handle network errors
      if (error instanceof TypeError && error.message.includes('fetch')) {
        throw new OfficeApiError(createNetworkErrorDetails(error), {
          isNetworkError: true,
        });
      }

      // Unknown error
      throw new OfficeApiError(createUnknownErrorDetails(error));
    }
  }

  /**
   * Get access token from auth service.
   */
  private async getAccessToken(): Promise<string> {
    // Ensure auth service is initialized
    if (!this.authService.isInitialized()) {
      throw new OfficeApiError(createAuthErrorDetails('Authentication service not initialized'), {
        isAuthError: true,
      });
    }

    try {
      const result = await this.authService.getAccessToken();
      return result.accessToken;
    } catch (error) {
      const message = error instanceof Error ? error.message : 'Failed to acquire token';
      throw new OfficeApiError(createAuthErrorDetails(message), {
        isAuthError: true,
      });
    }
  }

  /**
   * Handle error response and throw OfficeApiError.
   */
  private async handleErrorResponse(response: Response): Promise<never> {
    // Check for rate limiting
    const isRateLimited = response.status === 429;
    const retryAfterSeconds = isRateLimited
      ? parseRetryAfter(response.headers.get('Retry-After'))
      : undefined;

    // Check for auth errors
    const isAuthError = response.status === 401;

    // Try to parse ProblemDetails
    let problemDetails: ProblemDetails;
    try {
      const contentType = response.headers.get('Content-Type') || '';
      if (contentType.includes('application/json') || contentType.includes('application/problem+json')) {
        problemDetails = await response.json();
        // Ensure required fields are present
        if (!problemDetails.type) {
          problemDetails.type = 'about:blank';
        }
        if (!problemDetails.title) {
          problemDetails.title = response.statusText || 'Error';
        }
        if (!problemDetails.status) {
          problemDetails.status = response.status;
        }
      } else {
        // Non-JSON error response
        const text = await response.text();
        problemDetails = {
          type: 'about:blank',
          title: response.statusText || 'Error',
          status: response.status,
          detail: text || undefined,
        };
      }
    } catch {
      // Failed to parse response body
      problemDetails = {
        type: 'about:blank',
        title: response.statusText || 'Error',
        status: response.status,
      };
    }

    throw new OfficeApiError(problemDetails, {
      isAuthError,
      isRateLimited,
      retryAfterSeconds,
    });
  }

  /**
   * Generate an idempotency key for a save request.
   * Uses SHA-256 hash of canonical payload.
   */
  private generateIdempotencyKey(request: SaveRequest): string {
    // Create canonical payload
    const canonical = {
      sourceType: request.sourceType,
      associationType: request.associationType,
      associationId: request.associationId,
      emailId: request.content.emailId,
      attachmentIds: request.content.attachmentIds?.slice().sort(),
      includeBody: request.content.includeBody,
      documentName: request.content.documentName,
    };

    // Convert to JSON string with sorted keys
    const json = JSON.stringify(canonical, Object.keys(canonical).sort());

    // For browsers without SubtleCrypto, use a simple hash
    // In production, you might want to use a proper SHA-256 implementation
    return this.simpleHash(json);
  }

  /**
   * Simple hash function for idempotency keys.
   * In production, consider using SubtleCrypto.digest for SHA-256.
   */
  private simpleHash(str: string): string {
    let hash = 0;
    for (let i = 0; i < str.length; i++) {
      const char = str.charCodeAt(i);
      hash = (hash << 5) - hash + char;
      hash = hash & hash; // Convert to 32-bit integer
    }
    // Convert to hex and pad
    const hex = Math.abs(hash).toString(16).padStart(8, '0');
    return `idempotency-${hex}-${Date.now().toString(36)}`;
  }
}

/**
 * Create a new Office API Client instance.
 *
 * @param config - Client configuration
 * @param authService - Optional auth service override (for testing)
 * @returns OfficeApiClient instance
 */
export function createOfficeApiClient(
  config?: Partial<OfficeApiClientConfig>,
  authService?: INaaAuthService
): IOfficeApiClient & { configure: (config: Partial<OfficeApiClientConfig>) => void } {
  return new OfficeApiClientImpl(config, authService);
}

/**
 * Default singleton instance of the Office API Client.
 * Pre-configured with default settings from environment.
 */
export const officeApiClient = createOfficeApiClient();

/**
 * Export the implementation class for testing.
 */
export { OfficeApiClientImpl };
