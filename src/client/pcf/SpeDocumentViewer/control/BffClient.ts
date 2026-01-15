/**
 * BFF API Client for SPE Document Operations
 *
 * Handles HTTP calls to Spaarke BFF API with:
 * - Bearer token authentication (from MSAL)
 * - Correlation ID tracking
 * - Check-out/Check-in workflow
 * - Delete operations
 */

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
 * BffClient encapsulates all HTTP communication with the BFF API
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
        accessToken: string,
        correlationId: string
    ): Promise<FilePreviewResponse> {
        const url = `${this.baseUrl}/api/documents/${documentId}/preview-url`;

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

        return await response.json() as FilePreviewResponse;
    }

    /**
     * Check out a document for editing
     *
     * POST /api/documents/{documentId}/checkout
     */
    public async checkout(
        documentId: string,
        accessToken: string,
        correlationId: string
    ): Promise<CheckoutResponse> {
        const url = `${this.baseUrl}/api/documents/${documentId}/checkout`;

        console.log(`[BffClient] POST ${url}`);

        const response = await fetch(url, {
            method: 'POST',
            headers: {
                'Authorization': `Bearer ${accessToken}`,
                'X-Correlation-Id': correlationId,
                'Accept': 'application/json',
                'Content-Type': 'application/json'
            },
            mode: 'cors',
            credentials: 'omit'
        });

        if (!response.ok) {
            // Special handling for 409 Conflict (document locked)
            if (response.status === 409) {
                const errorBody = await response.json() as DocumentLockedError;
                throw new DocumentLockedException(errorBody);
            }
            await this.handleErrorResponse(response, correlationId);
        }

        return await response.json() as CheckoutResponse;
    }

    /**
     * Check in a document (commit changes, create version)
     *
     * POST /api/documents/{documentId}/checkin
     */
    public async checkIn(
        documentId: string,
        accessToken: string,
        correlationId: string,
        comment?: string
    ): Promise<CheckInResponse> {
        const url = `${this.baseUrl}/api/documents/${documentId}/checkin`;

        console.log(`[BffClient] POST ${url}`);

        const response = await fetch(url, {
            method: 'POST',
            headers: {
                'Authorization': `Bearer ${accessToken}`,
                'X-Correlation-Id': correlationId,
                'Accept': 'application/json',
                'Content-Type': 'application/json'
            },
            body: JSON.stringify({ comment }),
            mode: 'cors',
            credentials: 'omit'
        });

        if (!response.ok) {
            await this.handleErrorResponse(response, correlationId);
        }

        return await response.json() as CheckInResponse;
    }

    /**
     * Discard checkout (cancel without saving)
     *
     * POST /api/documents/{documentId}/discard
     */
    public async discard(
        documentId: string,
        accessToken: string,
        correlationId: string
    ): Promise<DiscardResponse> {
        const url = `${this.baseUrl}/api/documents/${documentId}/discard`;

        console.log(`[BffClient] POST ${url}`);

        const response = await fetch(url, {
            method: 'POST',
            headers: {
                'Authorization': `Bearer ${accessToken}`,
                'X-Correlation-Id': correlationId,
                'Accept': 'application/json',
                'Content-Type': 'application/json'
            },
            mode: 'cors',
            credentials: 'omit'
        });

        if (!response.ok) {
            await this.handleErrorResponse(response, correlationId);
        }

        return await response.json() as DiscardResponse;
    }

    /**
     * Delete a document (SPE file + Dataverse record)
     *
     * DELETE /api/documents/{documentId}
     */
    public async deleteDocument(
        documentId: string,
        accessToken: string,
        correlationId: string
    ): Promise<DeleteDocumentResponse> {
        const url = `${this.baseUrl}/api/documents/${documentId}`;

        console.log(`[BffClient] DELETE ${url}`);

        const response = await fetch(url, {
            method: 'DELETE',
            headers: {
                'Authorization': `Bearer ${accessToken}`,
                'X-Correlation-Id': correlationId,
                'Accept': 'application/json'
            },
            mode: 'cors',
            credentials: 'omit'
        });

        if (!response.ok) {
            // Special handling for 409 Conflict (document locked)
            if (response.status === 409) {
                const errorBody = await response.json() as DocumentLockedError;
                throw new DocumentLockedException(errorBody);
            }
            await this.handleErrorResponse(response, correlationId);
        }

        return await response.json() as DeleteDocumentResponse;
    }

    /**
     * Get open links (desktop + web URLs) for a document
     *
     * GET /api/documents/{documentId}/open-links
     */
    public async getOpenLinks(
        documentId: string,
        accessToken: string,
        correlationId: string
    ): Promise<OpenLinksResponse> {
        const url = `${this.baseUrl}/api/documents/${documentId}/open-links`;

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

        return await response.json() as OpenLinksResponse;
    }

    /**
     * Get download URL for a document (uses app-only auth on server)
     *
     * Returns the URL to the download endpoint that proxies the file through
     * the BFF using app-only authentication. This works for all documents,
     * including those uploaded by background processes (email-to-document).
     *
     * Note: The caller should append the access token as a query parameter
     * or use this URL with proper Authorization header.
     */
    public getDownloadUrl(documentId: string): string {
        // Use the v1 documents API which has the download endpoint
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
        accessToken: string,
        correlationId: string,
        filename?: string
    ): Promise<void> {
        const url = this.getDownloadUrl(documentId);

        console.log(`[BffClient] Download via ${url}`);

        // Fetch the file as a blob
        const response = await fetch(url, {
            method: 'GET',
            headers: {
                'Authorization': `Bearer ${accessToken}`,
                'X-Correlation-Id': correlationId
            },
            mode: 'cors',
            credentials: 'omit'
        });

        if (!response.ok) {
            await this.handleErrorResponse(response, correlationId);
        }

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
     * Handle error responses from BFF API
     */
    private async handleErrorResponse(response: Response, correlationId: string): Promise<never> {
        const responseText = await response.text();
        let errorBody: BffErrorResponse | null = null;

        try {
            errorBody = JSON.parse(responseText) as BffErrorResponse;
        } catch {
            // Non-JSON response
        }

        const errorCode = errorBody?.extensions?.code as string | undefined;
        const title = errorBody?.title || `HTTP ${response.status}`;
        const detail = errorBody?.detail || '';

        console.error('[BffClient] BFF API Error:', {
            status: response.status,
            code: errorCode,
            title,
            detail,
            correlationId: errorBody?.correlationId || correlationId
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
                switch (response.status) {
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
                        throw new Error(`Server error (${response.status}). Please try again later.`);
                    default:
                        throw new Error(detail || title || 'An unexpected error occurred.');
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
