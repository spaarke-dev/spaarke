/**
 * BFF API Client for SPE Document Operations
 *
 * Handles HTTP calls to Spaarke BFF API with:
 * - Bearer token authentication via @spaarke/auth (authenticatedFetch)
 * - Correlation ID tracking
 * - Check-out/Check-in workflow
 * - Delete operations
 *
 * MIGRATION NOTE: This client now uses authenticatedFetch() from @spaarke/auth
 * instead of receiving accessToken as a parameter. Token acquisition, caching,
 * and 401 retry logic are handled by the shared auth library.
 */

import { authenticatedFetch, ApiError } from '@spaarke/auth';

import {
    FilePreviewResponse,
    CheckoutResponse,
    CheckInResponse,
    DiscardResponse,
    DeleteDocumentResponse,
    OpenLinksResponse,
    BffErrorResponse,
    DocumentLockedError
} from './types';

/**
 * BffClient encapsulates all HTTP communication with the BFF API.
 *
 * After migration to @spaarke/auth, this client no longer accepts accessToken
 * parameters. Authentication is handled transparently by authenticatedFetch().
 */
export class BffClient {
    private baseUrl: string;

    constructor(baseUrl: string) {
        this.baseUrl = baseUrl.endsWith('/') ? baseUrl.slice(0, -1) : baseUrl;
    }

    /**
     * Get preview URL for a document (includes checkout status)
     *
     * GET /api/documents/{documentId}/preview-url
     */
    public async getPreviewUrl(
        documentId: string,
        correlationId: string
    ): Promise<FilePreviewResponse> {
        const url = `${this.baseUrl}/api/documents/${documentId}/preview-url`;

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

        return await response.json() as FilePreviewResponse;
    }

    /**
     * Get view URL for a document (real-time, no cache delay)
     *
     * GET /api/documents/{documentId}/view-url
     *
     * Unlike getPreviewUrl() which uses Graph's Preview action (30-60s cache),
     * this uses driveItem.webUrl for near real-time viewing without cache delay.
     * Includes checkout status for showing lock indicators.
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

            // Parse successful response
            const data = await response.json() as FilePreviewResponse;

            console.log(`[BffClient] View URL acquired for document: ${data.documentInfo?.name}`);
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
     * Check out a document for editing
     *
     * POST /api/documents/{documentId}/checkout
     */
    public async checkout(
        documentId: string,
        correlationId: string
    ): Promise<CheckoutResponse> {
        const url = `${this.baseUrl}/api/documents/${documentId}/checkout`;

        console.log(`[BffClient] POST ${url}`);

        try {
            const response = await authenticatedFetch(url, {
                method: 'POST',
                headers: {
                    'X-Correlation-Id': correlationId,
                    'Accept': 'application/json',
                    'Content-Type': 'application/json'
                },
                mode: 'cors',
                credentials: 'omit'
            });

            return await response.json() as CheckoutResponse;
        } catch (error) {
            // Special handling for 409 Conflict (document locked)
            if (error instanceof ApiError && error.status === 409) {
                const errorBody = error.problemDetails as unknown as DocumentLockedError;
                if (errorBody) {
                    throw new DocumentLockedException(errorBody);
                }
            }
            if (error instanceof ApiError) {
                this.handleApiError(error, correlationId);
            }
            throw error;
        }
    }

    /**
     * Check in a document (commit changes, create version)
     *
     * POST /api/documents/{documentId}/checkin
     */
    public async checkIn(
        documentId: string,
        correlationId: string,
        comment?: string
    ): Promise<CheckInResponse> {
        const url = `${this.baseUrl}/api/documents/${documentId}/checkin`;

        console.log(`[BffClient] POST ${url}`);

        try {
            const response = await authenticatedFetch(url, {
                method: 'POST',
                headers: {
                    'X-Correlation-Id': correlationId,
                    'Accept': 'application/json',
                    'Content-Type': 'application/json'
                },
                body: JSON.stringify({ comment }),
                mode: 'cors',
                credentials: 'omit'
            });

            return await response.json() as CheckInResponse;
        } catch (error) {
            if (error instanceof ApiError) {
                this.handleApiError(error, correlationId);
            }
            throw error;
        }
    }

    /**
     * Discard checkout (cancel without saving)
     *
     * POST /api/documents/{documentId}/discard
     */
    public async discard(
        documentId: string,
        correlationId: string
    ): Promise<DiscardResponse> {
        const url = `${this.baseUrl}/api/documents/${documentId}/discard`;

        console.log(`[BffClient] POST ${url}`);

        try {
            const response = await authenticatedFetch(url, {
                method: 'POST',
                headers: {
                    'X-Correlation-Id': correlationId,
                    'Accept': 'application/json',
                    'Content-Type': 'application/json'
                },
                mode: 'cors',
                credentials: 'omit'
            });

            return await response.json() as DiscardResponse;
        } catch (error) {
            if (error instanceof ApiError) {
                this.handleApiError(error, correlationId);
            }
            throw error;
        }
    }

    /**
     * Delete a document (SPE file + Dataverse record)
     *
     * DELETE /api/documents/{documentId}
     */
    public async deleteDocument(
        documentId: string,
        correlationId: string
    ): Promise<DeleteDocumentResponse> {
        const url = `${this.baseUrl}/api/documents/${documentId}`;

        console.log(`[BffClient] DELETE ${url}`);

        try {
            const response = await authenticatedFetch(url, {
                method: 'DELETE',
                headers: {
                    'X-Correlation-Id': correlationId,
                    'Accept': 'application/json'
                },
                mode: 'cors',
                credentials: 'omit'
            });

            return await response.json() as DeleteDocumentResponse;
        } catch (error) {
            // Special handling for 409 Conflict (document locked)
            if (error instanceof ApiError && error.status === 409) {
                const errorBody = error.problemDetails as unknown as DocumentLockedError;
                if (errorBody) {
                    throw new DocumentLockedException(errorBody);
                }
            }
            if (error instanceof ApiError) {
                this.handleApiError(error, correlationId);
            }
            throw error;
        }
    }

    /**
     * Get open links (desktop + web URLs) for a document
     *
     * GET /api/documents/{documentId}/open-links
     */
    public async getOpenLinks(
        documentId: string,
        correlationId: string
    ): Promise<OpenLinksResponse> {
        const url = `${this.baseUrl}/api/documents/${documentId}/open-links`;

        console.log(`[BffClient] GET ${url}`);

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

            return await response.json() as OpenLinksResponse;
        } catch (error) {
            if (error instanceof ApiError) {
                this.handleApiError(error, correlationId);
            }
            throw error;
        }
    }

    /**
     * Get download URL for a document (uses app-only auth on server)
     *
     * Returns the URL to the download endpoint that proxies the file through
     * the BFF using app-only authentication. This works for all documents,
     * including those uploaded by background processes (email-to-document).
     */
    public getDownloadUrl(documentId: string): string {
        return `${this.baseUrl}/api/v1/documents/${documentId}/download`;
    }

    /**
     * Download a document through the BFF proxy (app-only auth)
     *
     * This method triggers a download by creating a temporary anchor element.
     * The BFF endpoint uses app-only auth, so it works for documents
     * that users don't have direct SPE permissions for.
     *
     * GET /api/v1/documents/{documentId}/download
     */
    public async downloadDocument(
        documentId: string,
        correlationId: string,
        filename?: string
    ): Promise<void> {
        const url = this.getDownloadUrl(documentId);

        console.log(`[BffClient] Download via ${url}`);

        const response = await authenticatedFetch(url, {
            method: 'GET',
            headers: {
                'X-Correlation-Id': correlationId
            },
            mode: 'cors',
            credentials: 'omit'
        });

        // Get filename from Content-Disposition header if not provided
        const contentDisposition = response.headers.get('Content-Disposition');
        let downloadFilename = filename;
        if (!downloadFilename && contentDisposition) {
            const match = contentDisposition.match(/filename[*]?=['"]?(?:UTF-8'')?([^'";]+)['"]?/i);
            if (match) {
                downloadFilename = decodeURIComponent(match[1]);
            }
        }
        downloadFilename = downloadFilename || 'document';

        // Convert to blob and trigger download
        const blob = await response.blob();
        const blobUrl = URL.createObjectURL(blob);

        // Create temporary anchor to trigger download
        const anchor = document.createElement('a');
        anchor.href = blobUrl;
        anchor.download = downloadFilename;
        anchor.style.display = 'none';
        document.body.appendChild(anchor);
        anchor.click();

        // Cleanup
        document.body.removeChild(anchor);
        URL.revokeObjectURL(blobUrl);

        console.log(`[BffClient] Download triggered for ${downloadFilename}`);
    }

    /**
     * Map ApiError from @spaarke/auth to user-friendly error messages.
     *
     * @spaarke/auth's authenticatedFetch() throws ApiError for non-2xx responses
     * with RFC 7807 ProblemDetails already parsed. This method maps the error
     * codes from the BFF to user-friendly messages.
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

        // Map error codes to user-friendly messages
        switch (errorCode) {
            case 'invalid_id':
                throw new Error('Invalid document ID format.');
            case 'document_not_found':
                throw new Error('Document not found. It may have been deleted.');
            case 'document_locked':
                throw new Error('Document is locked by another user.');
            case 'not_checked_out':
                throw new Error('Document is not checked out.');
            case 'not_authorized':
                throw new Error('You are not authorized to perform this action.');
            default:
                switch (error.status) {
                    case 401:
                        throw new Error('Authentication failed. Please refresh the page.');
                    case 403:
                        throw new Error('You do not have permission to access this file.');
                    case 404:
                        throw new Error('Document not found.');
                    case 409:
                        throw new Error('Document is currently locked.');
                    case 500:
                    case 502:
                    case 503:
                    case 504:
                        throw new Error(`Server error (${error.status}). Please try again later.`);
                    default:
                        throw new Error(error.message || 'An unexpected error occurred.');
                }
        }
    }
}

/**
 * Custom exception for document lock conflicts (409)
 */
export class DocumentLockedException extends Error {
    public readonly lockedError: DocumentLockedError;

    constructor(error: DocumentLockedError) {
        super(error.detail);
        this.name = 'DocumentLockedException';
        this.lockedError = error;
    }
}
