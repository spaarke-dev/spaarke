/**
 * Analysis API Service - BFF API + Dataverse client for AnalysisWorkspace Code Page
 *
 * Analysis loading reads directly from Dataverse Web API (same-origin).
 * The analysis record is the source of truth — sprk_workingdocument holds
 * the persisted content. No BFF round-trip is needed for loading.
 *
 * BFF API is used for operations that require server-side processing:
 *   - Document metadata (BFF proxies Graph/SPE)
 *   - Save (BFF writes to Dataverse + manages in-memory streaming state)
 *   - Export (BFF generates Word/PDF from content)
 *
 * @see ADR-007 - Document access through SpeFileStore facade (BFF API)
 * @see ADR-008 - Endpoint filters for auth (Bearer token)
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
 * Used for document metadata, save, and export operations.
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
 * Map the BFF /api/v1/documents/{id} response to DocumentMetadata.
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
 * Fetch an analysis record directly from the Dataverse Web API.
 *
 * The Code Page runs as a web resource on the Dataverse org domain, so
 * /api/data/v9.2/ is same-origin and uses the browser's session cookies.
 * Dataverse is the source of truth — sprk_workingdocument holds the
 * persisted analysis content.
 *
 * @param analysisId - The GUID of the analysis to fetch
 * @returns The analysis record including content
 * @throws AnalysisError on failure
 */
export async function fetchAnalysis(analysisId: string): Promise<AnalysisRecord> {
    console.log(`${LOG_PREFIX} Loading analysis from Dataverse: ${analysisId}`);

    const controller = createTimeoutController();
    const response = await fetch(
        `/api/data/v9.2/sprk_analysises(${analysisId})?$select=sprk_name,sprk_workingdocument,statecode,statuscode,createdon,modifiedon,_sprk_documentid_value,_sprk_actionid_value,_sprk_playbook_value`,
        {
            method: "GET",
            headers: {
                "Accept": "application/json",
                "OData-MaxVersion": "4.0",
                "OData-Version": "4.0",
            },
            credentials: "same-origin",
            signal: controller.signal,
        }
    );

    if (!response.ok) {
        console.error(`${LOG_PREFIX} Dataverse read failed: ${response.status} ${response.statusText}`);
        throw {
            errorCode: `HTTP_${response.status}`,
            message: "Failed to load analysis from Dataverse",
            detail: `${response.status} ${response.statusText}`,
            status: response.status,
        } as AnalysisError;
    }

    const data = await response.json();

    let status: AnalysisRecord["status"] = "draft";
    if (data.statecode === 1) {
        status = "archived";
    } else if (data.sprk_workingdocument) {
        status = "completed";
    }

    const record: AnalysisRecord = {
        id: analysisId,
        title: data.sprk_name ?? "Analysis",
        content: data.sprk_workingdocument ?? "",
        status,
        sourceDocumentId: data._sprk_documentid_value ?? "",
        createdOn: data.createdon ?? new Date().toISOString(),
        modifiedOn: data.modifiedon ?? new Date().toISOString(),
        actionId: data._sprk_actionid_value ?? undefined,
        playbookId: data._sprk_playbook_value ?? undefined,
        statusCode: data.statuscode ?? undefined,
        createdBy: undefined,
    };

    console.log(
        `${LOG_PREFIX} Analysis loaded: "${record.title}" (${record.content.length} chars, status: ${record.status})`
    );
    return record;
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
    console.log(`${LOG_PREFIX} Fetching document metadata: ${documentId}`);

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
    console.log(`${LOG_PREFIX} Document metadata loaded: "${data.name}" (${data.mimeType})`);
    return data;
}

/**
 * Get a preview/view URL for a document.
 *
 * BFF route: GET /api/documents/{documentId}/preview-url
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
    console.log(`${LOG_PREFIX} Fetching document view URL: ${documentId}`);

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
 * Save analysis content directly to Dataverse (same-origin Web API).
 *
 * Writes the sprk_workingdocument field on the analysis record via PATCH.
 * No BFF round-trip needed — Dataverse is the source of truth.
 *
 * @param analysisId - The GUID of the analysis to save
 * @param content - Content from the editor to persist
 * @throws AnalysisError on API failure
 */
export async function saveAnalysisContent(
    analysisId: string,
    content: string
): Promise<void> {
    console.log(`${LOG_PREFIX} Saving analysis content: ${analysisId} (${content.length} chars)`);

    const controller = createTimeoutController();

    const response = await fetch(
        `/api/data/v9.2/sprk_analysises(${analysisId})`,
        {
            method: "PATCH",
            headers: {
                "Accept": "application/json",
                "Content-Type": "application/json",
                "OData-MaxVersion": "4.0",
                "OData-Version": "4.0",
            },
            credentials: "same-origin",
            body: JSON.stringify({ sprk_workingdocument: content }),
            signal: controller.signal,
        }
    );

    if (!response.ok) {
        const errorText = await response.text().catch(() => "Unknown error");
        const error: AnalysisError = {
            errorCode: `HTTP_${response.status}`,
            message: `Failed to save analysis content: ${errorText}`,
            status: response.status,
        };
        console.error(`${LOG_PREFIX} Failed to save analysis content:`, error);
        throw error;
    }

    console.log(`${LOG_PREFIX} Analysis content saved successfully`);
}

// ---------------------------------------------------------------------------
// Analysis Execution (SSE Streaming)
// ---------------------------------------------------------------------------

/**
 * Chunk received from the BFF SSE stream during analysis execution.
 */
export interface AnalysisStreamChunk {
    type: "metadata" | "chunk" | "error" | "status";
    content?: string;
    analysisId?: string;
    error?: string;
}

/**
 * Parameters for triggering analysis execution via the BFF.
 */
export interface ExecuteAnalysisParams {
    /** Existing analysis record ID in Dataverse */
    analysisId: string;
    /** Document IDs to analyze */
    documentIds: string[];
    /** Action ID (from sprk_analysisaction) */
    actionId?: string;
    /** Playbook ID (from sprk_analysisplaybook) */
    playbookId?: string;
    /** Bearer auth token for BFF API */
    token: string;
    /** Called for each SSE chunk received */
    onChunk?: (chunk: AnalysisStreamChunk) => void;
    /** AbortSignal for cancellation */
    signal?: AbortSignal;
}

/**
 * Execute an analysis via the BFF SSE endpoint.
 *
 * Sends the analysis request to POST /api/ai/analysis/execute and reads
 * the Server-Sent Events stream. The BFF persists the working document
 * to Dataverse as it streams, so the caller just needs to reload the
 * analysis record on completion.
 *
 * @param params - Execution parameters including IDs and token
 * @throws AnalysisError on fetch failure or SSE error chunk
 */
export async function executeAnalysis(params: ExecuteAnalysisParams): Promise<void> {
    const { analysisId, documentIds, actionId, playbookId, token, onChunk, signal } = params;

    console.log(`${LOG_PREFIX} Executing analysis: ${analysisId} (action: ${actionId ?? "none"}, playbook: ${playbookId ?? "none"})`);

    const body: Record<string, unknown> = {
        analysisId,
        documentIds,
        outputType: 0, // Document
    };
    if (actionId) body.actionId = actionId;
    if (playbookId) body.playbookId = playbookId;

    const response = await fetch(`${API_BASE_URL}/ai/analysis/execute`, {
        method: "POST",
        headers: {
            "Authorization": `Bearer ${token}`,
            "Content-Type": "application/json",
            "Accept": "text/event-stream",
        },
        body: JSON.stringify(body),
        signal,
    });

    console.log(`${LOG_PREFIX} Execute response: ${response.status} ${response.statusText}`);

    if (!response.ok) {
        const error = await parseApiError(response);
        console.error(`${LOG_PREFIX} Execute analysis failed:`, error);
        throw error;
    }

    if (!response.body) {
        throw {
            errorCode: "NO_STREAM",
            message: "Server did not return a stream",
        } as AnalysisError;
    }

    // Read SSE stream
    const reader = response.body.getReader();
    const decoder = new TextDecoder();
    let buffer = "";
    let chunkCount = 0;
    const startTime = Date.now();

    try {
        while (true) {
            const { done, value } = await reader.read();
            if (done) break;

            const decoded = decoder.decode(value, { stream: true });
            buffer += decoded;

            // Process complete SSE lines
            const lines = buffer.split("\n");
            buffer = lines.pop() ?? ""; // Keep incomplete last line

            for (const line of lines) {
                const trimmed = line.trim();
                if (!trimmed || trimmed.startsWith(":")) continue; // Skip empty/comment lines

                if (trimmed === "data: [DONE]") {
                    console.log(`${LOG_PREFIX} SSE complete (${chunkCount} chunks, ${((Date.now() - startTime) / 1000).toFixed(1)}s)`);
                    onChunk?.({ type: "status", content: "done" });
                    return;
                }

                if (trimmed.startsWith("data: ")) {
                    try {
                        const chunk = JSON.parse(trimmed.substring(6)) as AnalysisStreamChunk;
                        chunkCount++;
                        const elapsed = ((Date.now() - startTime) / 1000).toFixed(1);
                        console.log(`${LOG_PREFIX} SSE [${chunkCount}] +${elapsed}s type=${chunk.type} ${chunk.content ? chunk.content.substring(0, 80) : ""}`);
                        onChunk?.(chunk);

                        if (chunk.type === "error") {
                            console.error(`${LOG_PREFIX} SSE error:`, chunk.error);
                            throw {
                                errorCode: "EXECUTION_ERROR",
                                message: chunk.error ?? "Analysis execution failed",
                            } as AnalysisError;
                        }
                    } catch (parseErr) {
                        // If it's an AnalysisError we threw, re-throw
                        if ((parseErr as AnalysisError).errorCode) throw parseErr;
                        console.warn(`${LOG_PREFIX} Failed to parse SSE line:`, trimmed);
                    }
                }
            }
        }
    } finally {
        reader.releaseLock();
    }

    console.log(`${LOG_PREFIX} SSE stream ended (${chunkCount} chunks, ${((Date.now() - startTime) / 1000).toFixed(1)}s)`);
}

/**
 * Export an analysis to a downloadable document format.
 *
 * BFF route: POST /api/ai/analysis/{analysisId}/export
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
    console.log(`${LOG_PREFIX} Exporting analysis: ${analysisId} as ${format}`);

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
    console.log(`${LOG_PREFIX} Analysis exported successfully (${blob.size} bytes)`);
    return blob;
}
