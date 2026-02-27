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
 * BFF API route mapping:
 *   Analysis endpoints:  /api/ai/analysis/{analysisId}
 *   Document metadata:   /api/v1/documents/{documentId}
 *   Document preview:    /api/documents/{documentId}/preview-url
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
import { getBffBaseUrl } from "../config/bffConfig";

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

/**
 * BFF API base URL — resolved to the absolute BFF host.
 * MUST NOT use a relative path like "/api" because the page origin is the
 * Dataverse org (e.g., spaarkedev1.crm.dynamics.com), not the BFF API host.
 */
const API_BASE_URL = getBffBaseUrl();

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
// Response Mapping
// ---------------------------------------------------------------------------

/* eslint-disable @typescript-eslint/no-explicit-any */

/**
 * Map the BFF AnalysisDetailResult response to the Code Page's AnalysisRecord type.
 *
 * BFF returns:  { id, documentId, documentName, action, status, workingDocument, finalOutput, chatHistory, ... }
 * Code Page needs: { id, title, content, status, sourceDocumentId, createdOn, modifiedOn, ... }
 */
function mapAnalysisDetailToRecord(detail: any): AnalysisRecord {
    return {
        id: detail.id,
        title: detail.action?.name ?? detail.documentName ?? "Analysis",
        content: detail.workingDocument ?? detail.finalOutput ?? "",
        status: mapAnalysisStatus(detail.status),
        sourceDocumentId: detail.documentId,
        createdOn: detail.startedOn ?? new Date().toISOString(),
        modifiedOn: detail.completedOn ?? detail.startedOn ?? new Date().toISOString(),
        playbookId: detail.action?.playbookId,
        createdBy: undefined,
    };
}

/**
 * Map BFF status string to Code Page AnalysisStatus.
 */
function mapAnalysisStatus(status: string): AnalysisRecord["status"] {
    const lower = (status ?? "").toLowerCase();
    if (lower === "completed" || lower === "complete") return "completed";
    if (lower === "in_progress" || lower === "inprogress" || lower === "running") return "in_progress";
    if (lower === "error" || lower === "failed") return "error";
    if (lower === "archived") return "archived";
    return "draft";
}

/**
 * Map the BFF /api/v1/documents/{id} response to DocumentMetadata.
 *
 * BFF returns: { data: { sprk_documentid, sprk_name, sprk_mimetype, sprk_filesize, ... }, metadata: { ... } }
 */
function mapDocumentResponse(response: any): DocumentMetadata {
    const doc = response.data ?? response;
    return {
        id: doc.sprk_documentid ?? doc.id ?? "",
        name: doc.sprk_name ?? doc.sprk_filename ?? doc.name ?? "Document",
        mimeType: doc.sprk_mimetype ?? doc.mimeType ?? "application/octet-stream",
        size: doc.sprk_filesize ?? doc.size ?? 0,
        viewUrl: doc.sprk_filepath ?? doc.viewUrl ?? "",
        fileExtension: extractExtension(doc.sprk_name ?? doc.sprk_filename ?? doc.name),
        containerId: doc.sprk_containerid ?? doc.containerId,
    };
}

/* eslint-enable @typescript-eslint/no-explicit-any */

/**
 * Extract file extension from filename.
 */
function extractExtension(name?: string): string | undefined {
    if (!name) return undefined;
    const dotIndex = name.lastIndexOf(".");
    return dotIndex >= 0 ? name.substring(dotIndex + 1).toLowerCase() : undefined;
}

// ---------------------------------------------------------------------------
// API Functions
// ---------------------------------------------------------------------------

/**
 * Resume an analysis session in the BFF API's in-memory store.
 *
 * BFF route: POST /api/ai/analysis/{analysisId}/resume
 *
 * The BFF keeps analysis state in memory. When re-opening an existing
 * analysis (e.g., navigating to a previously completed record), the
 * in-memory session won't exist. This call hydrates it from Dataverse
 * so subsequent GET / continue / save calls work.
 *
 * @param analysisId - The GUID of the analysis to resume
 * @param documentId - The source document GUID (required by BFF)
 * @param token - Bearer auth token
 * @returns true if resume succeeded
 */
async function resumeAnalysis(
    analysisId: string,
    documentId: string,
    token: string
): Promise<boolean> {
    console.info(`${LOG_PREFIX} Resuming analysis session: ${analysisId} (document: ${documentId})`);

    const controller = createTimeoutController(60_000); // 60s — resume extracts document text

    const response = await fetch(`${API_BASE_URL}/ai/analysis/${analysisId}/resume`, {
        method: "POST",
        headers: buildHeaders(token),
        body: JSON.stringify({
            documentId,
            includeChatHistory: true,
        }),
        signal: controller.signal,
    });

    if (!response.ok) {
        const error = await parseApiError(response);
        console.warn(`${LOG_PREFIX} Resume failed:`, error);
        return false;
    }

    const result = await response.json();
    console.info(
        `${LOG_PREFIX} Analysis resumed: success=${result.success}, chatRestored=${result.chatMessagesRestored}`
    );
    return result.success === true;
}

/**
 * Fetch an analysis record from the BFF API.
 *
 * BFF route: GET /api/ai/analysis/{analysisId}
 *
 * The BFF stores analysis state in memory. If the session doesn't exist
 * (e.g., re-opening an existing analysis after API restart), this function
 * automatically calls POST /resume to hydrate the session from Dataverse,
 * then retries the GET.
 *
 * @param analysisId - The GUID of the analysis to fetch
 * @param token - Bearer auth token from AuthContext
 * @param documentId - Optional source document ID; used for auto-resume on 404
 * @returns The analysis record including HTML content
 * @throws AnalysisError on API failure
 *
 * @example
 * ```ts
 * const analysis = await fetchAnalysis("abc-123", authToken, "doc-456");
 * editorRef.current?.setHtml(analysis.content);
 * ```
 */
export async function fetchAnalysis(
    analysisId: string,
    token: string,
    documentId?: string
): Promise<AnalysisRecord> {
    console.info(`${LOG_PREFIX} Fetching analysis: ${analysisId}`);

    const controller = createTimeoutController();

    let response = await fetch(`${API_BASE_URL}/ai/analysis/${analysisId}`, {
        method: "GET",
        headers: buildHeaders(token),
        signal: controller.signal,
    });

    // Auto-resume: BFF keeps analysis in memory. If 404, the session
    // doesn't exist (API restarted or first open of existing record).
    // Call /resume to hydrate from Dataverse, then retry GET.
    if (response.status === 404 && documentId) {
        console.info(`${LOG_PREFIX} Analysis not in memory, attempting resume...`);
        const resumed = await resumeAnalysis(analysisId, documentId, token);

        if (resumed) {
            const retryController = createTimeoutController();
            response = await fetch(`${API_BASE_URL}/ai/analysis/${analysisId}`, {
                method: "GET",
                headers: buildHeaders(token),
                signal: retryController.signal,
            });
        }
    }

    if (!response.ok) {
        const error = await parseApiError(response);
        console.error(`${LOG_PREFIX} Failed to fetch analysis:`, error);
        throw error;
    }

    const raw = await response.json();
    const data = mapAnalysisDetailToRecord(raw);
    console.info(`${LOG_PREFIX} Analysis loaded: "${data.title}" (status: ${data.status})`);
    return data;
}

/**
 * Fetch document metadata from the BFF API.
 *
 * BFF route: GET /api/v1/documents/{documentId}
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

    const response = await fetch(`${API_BASE_URL}/v1/documents/${documentId}`, {
        method: "GET",
        headers: buildHeaders(token),
        signal: controller.signal,
    });

    if (!response.ok) {
        const error = await parseApiError(response);
        console.error(`${LOG_PREFIX} Failed to fetch document metadata:`, error);
        throw error;
    }

    const raw = await response.json();
    const data = mapDocumentResponse(raw);
    console.info(`${LOG_PREFIX} Document metadata loaded: "${data.name}" (${data.mimeType})`);
    return data;
}

/**
 * Get a preview/view URL for a document.
 *
 * BFF route: GET /api/documents/{documentId}/preview-url
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
 * BFF route: POST /api/ai/analysis/{analysisId}/save
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

    const response = await fetch(`${API_BASE_URL}/ai/analysis/${analysisId}/save`, {
        method: "POST",
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
 * BFF route: POST /api/ai/analysis/{analysisId}/export
 *
 * Calls the BFF API export endpoint which generates a Word or PDF document
 * from the analysis content. Returns the export result.
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

    const response = await fetch(`${API_BASE_URL}/ai/analysis/${analysisId}/export`, {
        method: "POST",
        headers: buildHeaders(token),
        body: JSON.stringify({ format }),
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
