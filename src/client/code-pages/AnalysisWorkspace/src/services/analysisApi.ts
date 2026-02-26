/**
 * Analysis API Service - BFF API client for AnalysisWorkspace Code Page
 *
 * Provides typed functions for all BFF API interactions required by the
 * AnalysisWorkspace, including analysis loading, saving, export, and
 * document metadata retrieval.
 *
 * All functions accept an auth token parameter and include Bearer authorization
 * headers. Token acquisition is handled by AuthContext (task 066).
 *
 * Error handling follows ADR-019: ProblemDetails responses are parsed and
 * transformed into typed AnalysisError objects.
 *
 * @see ADR-007 - Document access through SpeFileStore facade (BFF API)
 * @see ADR-008 - Endpoint filters for auth (Bearer token)
 * @see ADR-019 - ProblemDetails for all errors
 */

import type {
    AnalysisRecord,
    DocumentMetadata,
    AnalysisError,
    ProblemDetails,
    ExportFormat,
} from "../types";

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

/** BFF API base URL — relative path works with any deployment host. */
const API_BASE_URL = "/api";

const LOG_PREFIX = "[AnalysisWorkspace:AnalysisApi]";

/** Default request timeout in milliseconds (30 seconds) */
const DEFAULT_TIMEOUT_MS = 30_000;

// ---------------------------------------------------------------------------
// Error Handling
// ---------------------------------------------------------------------------

/**
 * Parse a ProblemDetails response from the BFF API into an AnalysisError.
 *
 * @param response - The HTTP response with a non-OK status
 * @returns A typed AnalysisError with parsed ProblemDetails fields
 */
async function parseApiError(response: Response): Promise<AnalysisError> {
    try {
        const contentType = response.headers.get("Content-Type") ?? "";

        if (contentType.includes("application/json") || contentType.includes("application/problem+json")) {
            const problem: ProblemDetails = await response.json();
            return {
                errorCode: problem.errorCode ?? `HTTP_${response.status}`,
                message: problem.title ?? response.statusText,
                detail: problem.detail,
                correlationId: problem.correlationId,
                status: problem.status ?? response.status,
            };
        }

        // Non-JSON error response
        const text = await response.text();
        return {
            errorCode: `HTTP_${response.status}`,
            message: response.statusText || "Request failed",
            detail: text || undefined,
            status: response.status,
        };
    } catch {
        return {
            errorCode: `HTTP_${response.status}`,
            message: response.statusText || "Request failed",
            status: response.status,
        };
    }
}

/**
 * Create an AbortController with a timeout.
 * The signal will abort the request if the timeout is exceeded.
 */
function createTimeoutController(timeoutMs: number = DEFAULT_TIMEOUT_MS): AbortController {
    const controller = new AbortController();
    setTimeout(() => controller.abort(), timeoutMs);
    return controller;
}

/**
 * Build standard headers for BFF API requests.
 */
function buildHeaders(token: string): Record<string, string> {
    return {
        "Authorization": `Bearer ${token}`,
        "Content-Type": "application/json",
        "Accept": "application/json",
    };
}

// ---------------------------------------------------------------------------
// API Functions
// ---------------------------------------------------------------------------

/**
 * Fetch an analysis record from the BFF API.
 *
 * @param analysisId - The GUID of the analysis to fetch
 * @param token - Bearer auth token from AuthContext
 * @returns The analysis record including HTML content
 * @throws AnalysisError on API failure
 *
 * @example
 * ```ts
 * const analysis = await fetchAnalysis("abc-123", authToken);
 * editorRef.current?.setHtml(analysis.content);
 * ```
 */
export async function fetchAnalysis(
    analysisId: string,
    token: string
): Promise<AnalysisRecord> {
    console.info(`${LOG_PREFIX} Fetching analysis: ${analysisId}`);

    const controller = createTimeoutController();

    const response = await fetch(`${API_BASE_URL}/analyses/${analysisId}`, {
        method: "GET",
        headers: buildHeaders(token),
        signal: controller.signal,
    });

    if (!response.ok) {
        const error = await parseApiError(response);
        console.error(`${LOG_PREFIX} Failed to fetch analysis:`, error);
        throw error;
    }

    const data: AnalysisRecord = await response.json();
    console.info(`${LOG_PREFIX} Analysis loaded: "${data.title}" (status: ${data.status})`);
    return data;
}

/**
 * Fetch document metadata from the BFF API.
 *
 * @param documentId - The GUID of the document
 * @param token - Bearer auth token
 * @returns Document metadata including name, MIME type, and view URL
 * @throws AnalysisError on API failure
 */
export async function fetchDocumentMetadata(
    documentId: string,
    token: string
): Promise<DocumentMetadata> {
    console.info(`${LOG_PREFIX} Fetching document metadata: ${documentId}`);

    const controller = createTimeoutController();

    const response = await fetch(`${API_BASE_URL}/documents/${documentId}`, {
        method: "GET",
        headers: buildHeaders(token),
        signal: controller.signal,
    });

    if (!response.ok) {
        const error = await parseApiError(response);
        console.error(`${LOG_PREFIX} Failed to fetch document metadata:`, error);
        throw error;
    }

    const data: DocumentMetadata = await response.json();
    console.info(`${LOG_PREFIX} Document metadata loaded: "${data.name}" (${data.mimeType})`);
    return data;
}

/**
 * Get a preview/view URL for a document.
 *
 * Returns a URL suitable for embedding in an iframe. For PDFs this is a
 * direct URL; for Office documents it may be an Office Online embed URL.
 *
 * @param documentId - The GUID of the document
 * @param token - Bearer auth token
 * @returns The URL for embedding in an iframe
 * @throws AnalysisError on API failure
 */
export async function getDocumentViewUrl(
    documentId: string,
    token: string
): Promise<string> {
    console.info(`${LOG_PREFIX} Fetching document view URL: ${documentId}`);

    const controller = createTimeoutController();

    const response = await fetch(`${API_BASE_URL}/documents/${documentId}/preview-url`, {
        method: "GET",
        headers: buildHeaders(token),
        signal: controller.signal,
    });

    if (!response.ok) {
        const error = await parseApiError(response);
        console.error(`${LOG_PREFIX} Failed to fetch document view URL:`, error);
        throw error;
    }

    const data = await response.json();
    return data.previewUrl as string;
}

/**
 * Save analysis content to the BFF API.
 *
 * Called by the auto-save hook to persist editor content. Uses PUT
 * for idempotent updates.
 *
 * @param analysisId - The GUID of the analysis to save
 * @param content - HTML content from the RichTextEditor
 * @param token - Bearer auth token
 * @throws AnalysisError on API failure
 */
export async function saveAnalysisContent(
    analysisId: string,
    content: string,
    token: string
): Promise<void> {
    console.debug(`${LOG_PREFIX} Saving analysis content: ${analysisId} (${content.length} chars)`);

    const controller = createTimeoutController();

    const response = await fetch(`${API_BASE_URL}/analyses/${analysisId}/content`, {
        method: "PUT",
        headers: buildHeaders(token),
        body: JSON.stringify({ content }),
        signal: controller.signal,
    });

    if (!response.ok) {
        const error = await parseApiError(response);
        console.error(`${LOG_PREFIX} Failed to save analysis content:`, error);
        throw error;
    }

    console.debug(`${LOG_PREFIX} Analysis content saved successfully`);
}

/**
 * Export an analysis to a downloadable document format.
 *
 * Calls the BFF API export endpoint which generates a Word or PDF document
 * from the analysis content. Returns a Blob for client-side download.
 *
 * @param analysisId - The GUID of the analysis to export
 * @param format - Export format: "docx" or "pdf"
 * @param token - Bearer auth token
 * @returns A Blob containing the exported document
 * @throws AnalysisError on API failure
 */
export async function exportAnalysis(
    analysisId: string,
    format: ExportFormat,
    token: string
): Promise<Blob> {
    console.info(`${LOG_PREFIX} Exporting analysis: ${analysisId} as ${format}`);

    const controller = createTimeoutController(60_000); // 60s timeout for export

    const response = await fetch(`${API_BASE_URL}/analyses/${analysisId}/export?format=${format}`, {
        method: "GET",
        headers: {
            "Authorization": `Bearer ${token}`,
            "Accept": format === "docx"
                ? "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
                : "application/pdf",
        },
        signal: controller.signal,
    });

    if (!response.ok) {
        const error = await parseApiError(response);
        console.error(`${LOG_PREFIX} Failed to export analysis:`, error);
        throw error;
    }

    const blob = await response.blob();
    console.info(`${LOG_PREFIX} Analysis exported successfully (${blob.size} bytes)`);
    return blob;
}
