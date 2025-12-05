/**
 * BFF API Client for SPE File Operations
 *
 * Handles HTTP calls to Spaarke BFF API with:
 * - Bearer token authentication (from MSAL)
 * - Correlation ID tracking (X-Correlation-Id header)
 * - Error handling and logging
 */

import { FilePreviewResponse, BffErrorResponse, OfficeUrlResponse, OpenLinksResponse } from './types';

/**
 * BffClient encapsulates all HTTP communication with the BFF API
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
     * @param accessToken Bearer token from MSAL
     * @param correlationId Correlation ID for distributed tracing
     * @returns FilePreviewResponse with preview URL and metadata
     * @throws Error if API call fails
     */
    public async getPreviewUrl(
        documentId: string,
        accessToken: string,
        correlationId: string
    ): Promise<FilePreviewResponse> {
        const url = `${this.baseUrl}/api/documents/${documentId}/preview-url`;

        console.log(`[BffClient] GET ${url}`);
        console.log(`[BffClient] Correlation ID: ${correlationId}`);

        try {
            const response = await fetch(url, {
                method: 'GET',
                headers: {
                    'Authorization': `Bearer ${accessToken}`,
                    'X-Correlation-Id': correlationId,
                    'Accept': 'application/json'
                },
                mode: 'cors', // Required for cross-origin BFF calls
                credentials: 'omit' // Don't send cookies (JWT only)
            });

            // Handle non-2xx responses
            if (!response.ok) {
                await this.handleErrorResponse(response, correlationId);
            }

            // Parse successful response
            const data = await response.json() as FilePreviewResponse;

            console.log(`[BffClient] Preview URL acquired for document: ${data.documentInfo.name}`);

            // Verify correlation ID round-trip
            if (data.correlationId !== correlationId) {
                console.warn(`[BffClient] Correlation ID mismatch! Sent: ${correlationId}, Received: ${data.correlationId}`);
            }

            return data;

        } catch (error) {
            // Network errors, JSON parse errors, etc.
            console.error('[BffClient] Request failed:', error);
            throw new Error(`Failed to get preview URL: ${error instanceof Error ? error.message : String(error)}`);
        }
    }

    /**
     * Get Office Online editor URL for a document
     *
     * Calls: GET /api/documents/{documentId}/office
     *
     * This method requests the Office Online editor URL from the BFF API.
     * The BFF uses On-Behalf-Of (OBO) flow to call Microsoft Graph API
     * with the user's permissions, ensuring security.
     *
     * @param documentId Document GUID (e.g., "550e8400-e29b-41d4-a716-446655440000")
     * @param accessToken Bearer token from MSAL (user's access token)
     * @param correlationId Correlation ID for distributed tracing
     * @returns Promise<OfficeUrlResponse> with editor URL and permissions
     * @throws Error if API call fails (network error, 4xx/5xx response)
     *
     * @example
     * ```typescript
     * const response = await bffClient.getOfficeUrl(
     *     "550e8400-e29b-41d4-a716-446655440000",
     *     accessToken,
     *     "trace-abc-123"
     * );
     * console.log(response.officeUrl); // Office Online URL
     * console.log(response.permissions.canEdit); // true or false
     * ```
     */
    public async getOfficeUrl(
        documentId: string,
        accessToken: string,
        correlationId: string
    ): Promise<OfficeUrlResponse> {
        const url = `${this.baseUrl}/api/documents/${documentId}/office`;

        console.log(`[BffClient] GET ${url} (Office Editor)`);
        console.log(`[BffClient] Correlation ID: ${correlationId}`);

        try {
            const response = await fetch(url, {
                method: 'GET',
                headers: {
                    'Authorization': `Bearer ${accessToken}`,
                    'X-Correlation-Id': correlationId,
                    'Accept': 'application/json'
                },
                mode: 'cors', // Required for cross-origin BFF calls
                credentials: 'omit' // Don't send cookies (JWT only)
            });

            // Handle non-2xx responses
            if (!response.ok) {
                await this.handleErrorResponse(response, correlationId);
            }

            // Parse successful response
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
            // Network errors, JSON parse errors, etc.
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
     * Returns URLs for opening the document in:
     * - Desktop Office app (Word, Excel, PowerPoint) via protocol handler
     * - Web browser via SharePoint URL
     *
     * @param documentId Document GUID
     * @param accessToken Bearer token from MSAL
     * @param correlationId Correlation ID for distributed tracing
     * @returns OpenLinksResponse with desktop and web URLs
     * @throws Error if API call fails
     */
    public async getOpenLinks(
        documentId: string,
        accessToken: string,
        correlationId: string
    ): Promise<OpenLinksResponse> {
        const url = `${this.baseUrl}/api/documents/${documentId}/open-links`;

        console.log(`[BffClient] GET ${url} (Open Links)`);
        console.log(`[BffClient] Correlation ID: ${correlationId}`);

        try {
            const response = await fetch(url, {
                method: 'GET',
                headers: {
                    'Authorization': `Bearer ${accessToken}`,
                    'X-Correlation-Id': correlationId,
                    'Accept': 'application/json'
                },
                mode: 'cors',
                credentials: 'omit'
            });

            // Handle non-2xx responses
            if (!response.ok) {
                await this.handleErrorResponse(response, correlationId);
            }

            // Parse successful response
            const data = await response.json() as OpenLinksResponse;

            console.log(`[BffClient] Open links acquired for: ${data.fileName}`);
            console.log(`[BffClient] Desktop URL available: ${data.desktopUrl ? 'Yes' : 'No'}`);

            return data;

        } catch (error) {
            console.error('[BffClient] Request failed:', error);
            throw new Error(
                `Failed to get open links: ${error instanceof Error ? error.message : String(error)}`
            );
        }
    }

    /**
     * Handle error responses from BFF API
     *
     * Maps stable error codes from BFF to user-friendly messages.
     * Supports RFC 7807 Problem Details with custom error codes in extensions.
     *
     * @param response Fetch response object
     * @param correlationId Correlation ID for error reporting
     * @throws Error with detailed message
     */
    private async handleErrorResponse(response: Response, correlationId: string): Promise<never> {
        const responseText = await response.text();
        let errorBody: BffErrorResponse | null = null;

        try {
            // Try to parse as JSON (RFC 7807 Problem Details)
            errorBody = JSON.parse(responseText) as BffErrorResponse;
        } catch {
            // Non-JSON response (unexpected)
        }

        // Extract error code from extensions (as per senior dev spec)
        const errorCode = errorBody?.extensions?.code as string | undefined;
        const title = errorBody?.title || `HTTP ${response.status}`;
        const detail = errorBody?.detail || '';

        console.error('[BffClient] BFF API Error:', {
            status: response.status,
            code: errorCode,
            title: title,
            detail: detail,
            correlationId: errorBody?.correlationId || correlationId
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
                // Fall back to HTTP status code mapping
                switch (response.status) {
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
                        throw new Error(`Server error (${response.status}). Please try again later. Correlation ID: ${correlationId}`);
                    default:
                        throw new Error(detail || title || 'An unexpected error occurred.');
                }
        }
    }

    /**
     * Download a document
     *
     * Calls: GET /api/documents/{documentId}/content
     * Returns download URL (not implemented in this phase - preview only)
     *
     * @param documentId Document GUID
     * @param accessToken Bearer token from MSAL
     * @param correlationId Correlation ID for distributed tracing
     * @returns Download URL
     */
    public async getDownloadUrl(
        documentId: string,
        accessToken: string,
        correlationId: string
    ): Promise<string> {
        const url = `${this.baseUrl}/api/documents/${documentId}/content`;

        console.log(`[BffClient] GET ${url}`);

        const response = await fetch(url, {
            method: 'GET',
            headers: {
                'Authorization': `Bearer ${accessToken}`,
                'X-Correlation-Id': correlationId,
                'Accept': 'application/json'
            },
            mode: 'cors',
            credentials: 'omit'
        });

        if (!response.ok) {
            await this.handleErrorResponse(response, correlationId);
        }

        const data = await response.json();
        return data.downloadUrl; // BFF returns { downloadUrl: "..." }
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
}
