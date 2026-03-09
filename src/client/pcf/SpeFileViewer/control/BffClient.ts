/**
 * BFF API Client for SPE File Operations
 *
 * Handles HTTP calls to Spaarke BFF API with:
 * - Bearer token authentication via @spaarke/auth (authenticatedFetch)
 * - Correlation ID tracking (X-Correlation-Id header)
 * - Error handling and logging
 *
 * MIGRATION NOTE: This client now uses authenticatedFetch() from @spaarke/auth
 * instead of receiving accessToken as a parameter. Token acquisition, caching,
 * and 401 retry logic are handled by the shared auth library.
 */

import { authenticatedFetch, ApiError } from '@spaarke/auth';
import { FilePreviewResponse, BffErrorResponse, OfficeUrlResponse, OpenLinksResponse } from './types';

/**
 * BffClient encapsulates all HTTP communication with the BFF API.
 *
 * After migration to @spaarke/auth, this client no longer accepts accessToken
 * parameters. Authentication is handled transparently by authenticatedFetch().
 */
export class BffClient {
    private baseUrl: string;

    /**
     * @param baseUrl BFF base URL (e.g., "https://spe-api-dev-67e2xz.azurewebsites.net")
     */
    constructor(baseUrl: string) {
        // Remove trailing slash if present
        this.baseUrl = baseUrl.endsWith('/') ? baseUrl.slice(0, -1) : baseUrl;
    }

    /**
     * Get preview URL for a document
     *
     * Calls: GET /api/documents/{documentId}/preview-url
     *
     * @param documentId Document GUID
     * @param correlationId Correlation ID for distributed tracing
     * @returns FilePreviewResponse with preview URL and metadata
     * @throws Error if API call fails
     */
    public async getPreviewUrl(
        documentId: string,
        correlationId: string
    ): Promise<FilePreviewResponse> {
        const url = `${this.baseUrl}/api/documents/${documentId}/preview-url`;

        console.log(`[BffClient] GET ${url}`);
        console.log(`[BffClient] Correlation ID: ${correlationId}`);

        try {
            const response = await authenticatedFetch(url, {
                method: 'GET',
                headers: {
                    'X-Correlation-Id': correlationId,
                    'Accept': 'application/json'
                },
                mode: 'cors',
                credentials: 'omit'
            });

            const data = await response.json() as FilePreviewResponse;

            console.log(`[BffClient] Preview URL acquired for document: ${data.documentInfo.name}`);

            // Verify correlation ID round-trip
            if (data.correlationId !== correlationId) {
                console.warn(`[BffClient] Correlation ID mismatch! Sent: ${correlationId}, Received: ${data.correlationId}`);
            }

            return data;

        } catch (error) {
            if (error instanceof ApiError) {
                this.handleApiError(error, correlationId);
            }
            console.error('[BffClient] Request failed:', error);
            throw new Error(`Failed to get preview URL: ${error instanceof Error ? error.message : String(error)}`);
        }
    }

    /**
     * Get Office Online editor URL for a document
     *
     * Calls: GET /api/documents/{documentId}/office
     *
     * @param documentId Document GUID
     * @param correlationId Correlation ID for distributed tracing
     * @returns Promise<OfficeUrlResponse> with editor URL and permissions
     * @throws Error if API call fails
     */
    public async getOfficeUrl(
        documentId: string,
        correlationId: string
    ): Promise<OfficeUrlResponse> {
        const url = `${this.baseUrl}/api/documents/${documentId}/office`;

        console.log(`[BffClient] GET ${url} (Office Editor)`);
        console.log(`[BffClient] Correlation ID: ${correlationId}`);

        try {
            const response = await authenticatedFetch(url, {
                method: 'GET',
                headers: {
                    'X-Correlation-Id': correlationId,
                    'Accept': 'application/json'
                },
                mode: 'cors',
                credentials: 'omit'
            });

            const data = await response.json() as OfficeUrlResponse;

            console.log(`[BffClient] Office URL acquired for document`);
            console.log(`[BffClient] Can Edit: ${data.permissions.canEdit}`);
            console.log(`[BffClient] User Role: ${data.permissions.role}`);

            // Verify correlation ID round-trip
            if (data.correlationId !== correlationId) {
                console.warn(
                    `[BffClient] Correlation ID mismatch! Sent: ${correlationId}, Received: ${data.correlationId}`
                );
            }

            return data;

        } catch (error) {
            if (error instanceof ApiError) {
                this.handleApiError(error, correlationId);
            }
            console.error('[BffClient] Request failed:', error);
            throw new Error(
                `Failed to get Office URL: ${error instanceof Error ? error.message : String(error)}`
            );
        }
    }

    /**
     * Get open links (desktop + web URLs) for a document
     *
     * Calls: GET /api/documents/{documentId}/open-links
     *
     * @param documentId Document GUID
     * @param correlationId Correlation ID for distributed tracing
     * @returns OpenLinksResponse with desktop and web URLs
     * @throws Error if API call fails
     */
    public async getOpenLinks(
        documentId: string,
        correlationId: string
    ): Promise<OpenLinksResponse> {
        const url = `${this.baseUrl}/api/documents/${documentId}/open-links`;

        console.log(`[BffClient] GET ${url} (Open Links)`);
        console.log(`[BffClient] Correlation ID: ${correlationId}`);

        try {
            const response = await authenticatedFetch(url, {
                method: 'GET',
                headers: {
                    'X-Correlation-Id': correlationId,
                    'Accept': 'application/json'
                },
                mode: 'cors',
                credentials: 'omit'
            });

            const data = await response.json() as OpenLinksResponse;

            console.log(`[BffClient] Open links acquired for: ${data.fileName}`);
            console.log(`[BffClient] Desktop URL available: ${data.desktopUrl ? 'Yes' : 'No'}`);

            return data;

        } catch (error) {
            if (error instanceof ApiError) {
                this.handleApiError(error, correlationId);
            }
            console.error('[BffClient] Request failed:', error);
            throw new Error(
                `Failed to get open links: ${error instanceof Error ? error.message : String(error)}`
            );
        }
    }

    /**
     * Get view URL for a document (real-time, no cache)
     *
     * Calls: GET /api/documents/{documentId}/view-url
     *
     * @param documentId Document GUID
     * @param correlationId Correlation ID for distributed tracing
     * @returns FilePreviewResponse with view URL, metadata, and checkout status
     * @throws Error if API call fails
     */
    public async getViewUrl(
        documentId: string,
        correlationId: string
    ): Promise<FilePreviewResponse> {
        const url = `${this.baseUrl}/api/documents/${documentId}/view-url`;

        console.log(`[BffClient] GET ${url} (View URL - real-time)`);
        console.log(`[BffClient] Correlation ID: ${correlationId}`);

        try {
            const response = await authenticatedFetch(url, {
                method: 'GET',
                headers: {
                    'X-Correlation-Id': correlationId,
                    'Accept': 'application/json'
                },
                mode: 'cors',
                credentials: 'omit'
            });

            const data = await response.json() as FilePreviewResponse;

            console.log(`[BffClient] View URL acquired for document: ${data.documentInfo.name}`);
            if (data.checkoutStatus?.isCheckedOut) {
                console.log(`[BffClient] Document is checked out by: ${data.checkoutStatus.checkedOutBy?.name}`);
            }

            return data;

        } catch (error) {
            if (error instanceof ApiError) {
                this.handleApiError(error, correlationId);
            }
            console.error('[BffClient] Request failed:', error);
            throw new Error(`Failed to get view URL: ${error instanceof Error ? error.message : String(error)}`);
        }
    }

    /**
     * Download a document
     *
     * Calls: GET /api/documents/{documentId}/content
     *
     * @param documentId Document GUID
     * @param correlationId Correlation ID for distributed tracing
     * @returns Download URL
     */
    public async getDownloadUrl(
        documentId: string,
        correlationId: string
    ): Promise<string> {
        const url = `${this.baseUrl}/api/documents/${documentId}/content`;

        console.log(`[BffClient] GET ${url}`);

        const response = await authenticatedFetch(url, {
            method: 'GET',
            headers: {
                'X-Correlation-Id': correlationId,
                'Accept': 'application/json'
            },
            mode: 'cors',
            credentials: 'omit'
        });

        const data = await response.json();
        return data.downloadUrl;
    }

    /**
     * Health check (for testing connectivity)
     *
     * @returns true if BFF is reachable
     */
    public async healthCheck(): Promise<boolean> {
        try {
            const response = await fetch(`${this.baseUrl}/health`, {
                method: 'GET',
                mode: 'cors'
            });
            return response.ok;
        } catch {
            return false;
        }
    }

    /**
     * Map ApiError from @spaarke/auth to user-friendly error messages.
     *
     * @spaarke/auth's authenticatedFetch() throws ApiError for non-2xx responses
     * with RFC 7807 ProblemDetails already parsed.
     */
    private handleApiError(error: ApiError, correlationId: string): never {
        const problemDetails = error.problemDetails;
        const errorCode = (problemDetails as Record<string, unknown>)?.extensions?.code as string | undefined;

        console.error('[BffClient] BFF API Error:', {
            status: error.status,
            code: errorCode,
            message: error.message,
            correlationId
        });

        // Map stable error codes to user-friendly messages (per senior dev spec)
        switch (errorCode) {
            case 'invalid_id':
                throw new Error('Invalid document ID format. Please contact support.');
            case 'document_not_found':
                throw new Error('Document not found. It may have been deleted.');
            case 'mapping_missing_drive':
            case 'mapping_missing_item':
                throw new Error('This file is still initializing. Please try again in a moment.');
            case 'storage_not_found':
                throw new Error('File has been removed from storage. Contact your administrator.');
            case 'throttled_retry':
                throw new Error('Service is temporarily busy. Please try again in a few seconds.');
            default:
                switch (error.status) {
                    case 401:
                        throw new Error('Authentication failed. Please refresh the page.');
                    case 403:
                        throw new Error('You do not have permission to access this file.');
                    case 404:
                        throw new Error('Document not found. It may have been deleted.');
                    case 409:
                        throw new Error('File is not ready for preview. Please try again shortly.');
                    case 500:
                    case 502:
                    case 503:
                    case 504:
                        throw new Error(`Server error (${error.status}). Please try again later. Correlation ID: ${correlationId}`);
                    default:
                        throw new Error(error.message || 'An unexpected error occurred.');
                }
        }
    }
}
