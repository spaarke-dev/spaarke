/**
 * Analysis API Service — BFF API + Dataverse client for AnalysisWorkspace.
 *
 * Spaarke Auth v2 (task 026): all BFF API calls go through `authenticatedFetch`
 * from @spaarke/auth. NO `token: string` parameters. The SSE streaming path
 * (executeAnalysis) accepts a `getAccessToken: () => Promise<string>` getter
 * — the only place where a token string is materialized, and only on the
 * line that opens the SSE fetch (never snapshotted).
 *
 * Analysis loading reads directly from Dataverse Web API (same-origin, browser
 * session cookie — no Bearer token needed).
 *
 * @see ADR-007 — Document access through SpeFileStore facade (BFF API)
 * @see ADR-008 — Endpoint filters for auth
 * @see CLAUDE.md §D-AUTH-7 — Bearer literals confined to authenticatedFetch
 */

import type { AuthenticatedFetchFn } from '@spaarke/auth';
import type {
  AnalysisRecord,
  DocumentMetadata,
  AnalysisError,
  ProblemDetails,
  ExportFormat,
  IChatMessage,
} from '../types';
import { getRuntimeConfig } from './authInit';

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

/**
 * Get the BFF API base URL from runtime config (resolved during bootstrap).
 * @throws Error if called before initializeAuth() completes.
 */
function getApiBaseUrl(): string {
  return getRuntimeConfig().bffBaseUrl;
}

const LOG_PREFIX = '[AnalysisWorkspace:AnalysisApi]';

/** Default request timeout in milliseconds (30 seconds). */
const DEFAULT_TIMEOUT_MS = 30_000;

// ---------------------------------------------------------------------------
// Error Handling
// ---------------------------------------------------------------------------

/**
 * Parse a ProblemDetails response from the BFF API into an AnalysisError.
 */
async function parseApiError(response: Response): Promise<AnalysisError> {
  try {
    const contentType = response.headers.get('Content-Type') ?? '';

    if (contentType.includes('application/json') || contentType.includes('application/problem+json')) {
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
      message: response.statusText || 'Request failed',
      detail: text || undefined,
      status: response.status,
    };
  } catch {
    return {
      errorCode: `HTTP_${response.status}`,
      message: response.statusText || 'Request failed',
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
    id: doc.sprk_documentid ?? doc.id ?? '',
    name: doc.sprk_name ?? doc.sprk_filename ?? doc.name ?? 'Document',
    mimeType: doc.sprk_mimetype ?? doc.mimeType ?? 'application/octet-stream',
    size: doc.sprk_filesize ?? doc.size ?? 0,
    viewUrl: doc.sprk_filepath ?? doc.viewUrl ?? '',
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
  const dotIndex = name.lastIndexOf('.');
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
 * No BFF Bearer token needed.
 */
export async function fetchAnalysis(analysisId: string): Promise<AnalysisRecord> {
  console.log(`${LOG_PREFIX} Loading analysis from Dataverse: ${analysisId}`);

  const controller = createTimeoutController();
  const response = await fetch(
    `/api/data/v9.2/sprk_analysises(${analysisId})?$select=sprk_name,sprk_workingdocument,sprk_chathistory,statecode,statuscode,createdon,modifiedon,_sprk_documentid_value,_sprk_actionid_value,_sprk_playbook_value`,
    {
      method: 'GET',
      headers: {
        Accept: 'application/json',
        'OData-MaxVersion': '4.0',
        'OData-Version': '4.0',
      },
      credentials: 'same-origin',
      signal: controller.signal,
    }
  );

  if (!response.ok) {
    console.error(`${LOG_PREFIX} Dataverse read failed: ${response.status} ${response.statusText}`);
    throw {
      errorCode: `HTTP_${response.status}`,
      message: 'Failed to load analysis from Dataverse',
      detail: `${response.status} ${response.statusText}`,
      status: response.status,
    } as AnalysisError;
  }

  const data = await response.json();

  let status: AnalysisRecord['status'] = 'draft';
  if (data.statecode === 1) {
    status = 'archived';
  } else if (data.sprk_workingdocument) {
    status = 'completed';
  }

  let chatHistory: IChatMessage[] | undefined;
  if (data.sprk_chathistory) {
    try {
      chatHistory = JSON.parse(data.sprk_chathistory) as IChatMessage[];
    } catch {
      console.warn(`${LOG_PREFIX} Failed to parse sprk_chathistory for analysis ${analysisId}`);
    }
  }

  const record: AnalysisRecord = {
    id: analysisId,
    title: data.sprk_name ?? 'Analysis',
    content: data.sprk_workingdocument ?? '',
    status,
    sourceDocumentId: data._sprk_documentid_value ?? '',
    createdOn: data.createdon ?? new Date().toISOString(),
    modifiedOn: data.modifiedon ?? new Date().toISOString(),
    actionId: data._sprk_actionid_value ?? undefined,
    playbookId: data._sprk_playbook_value ?? undefined,
    statusCode: data.statuscode ?? undefined,
    createdBy: undefined,
    chatHistory,
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
 * @param documentId The GUID of the document.
 * @param authenticatedFetch Authenticated fetch from useAuth() / @spaarke/auth.
 */
export async function fetchDocumentMetadata(
  documentId: string,
  authenticatedFetch: AuthenticatedFetchFn
): Promise<DocumentMetadata> {
  console.log(`${LOG_PREFIX} Fetching document metadata: ${documentId}`);

  const controller = createTimeoutController();

  const response = await authenticatedFetch(`${getApiBaseUrl()}/api/v1/documents/${documentId}`, {
    method: 'GET',
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
 */
export async function getDocumentViewUrl(
  documentId: string,
  authenticatedFetch: AuthenticatedFetchFn
): Promise<string> {
  console.log(`${LOG_PREFIX} Fetching document view URL: ${documentId}`);

  const controller = createTimeoutController();

  const response = await authenticatedFetch(`${getApiBaseUrl()}/api/documents/${documentId}/preview-url`, {
    method: 'GET',
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
 * No BFF round-trip; no Bearer token needed.
 */
export async function saveAnalysisContent(analysisId: string, content: string): Promise<void> {
  console.log(`${LOG_PREFIX} Saving analysis content: ${analysisId} (${content.length} chars)`);

  const controller = createTimeoutController();

  const response = await fetch(`/api/data/v9.2/sprk_analysises(${analysisId})`, {
    method: 'PATCH',
    headers: {
      Accept: 'application/json',
      'Content-Type': 'application/json',
      'OData-MaxVersion': '4.0',
      'OData-Version': '4.0',
    },
    credentials: 'same-origin',
    body: JSON.stringify({ sprk_workingdocument: content }),
    signal: controller.signal,
  });

  if (!response.ok) {
    const errorText = await response.text().catch(() => 'Unknown error');
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
  type: 'metadata' | 'progress' | 'chunk' | 'done' | 'error' | 'status';
  content?: string;
  analysisId?: string;
  error?: string;
  /** Step identifier for progress events (e.g. "extracting_text"). */
  step?: string;
}

/**
 * Parameters for triggering analysis execution via the BFF.
 *
 * Spaarke Auth v2: no `token: string`. SSE requires a fresh Bearer header at
 * stream-open; we accept a `getAccessToken: () => Promise<string>` getter and
 * await it ONCE immediately before the fetch. Never snapshot the result.
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
  /**
   * Token getter — called exactly once, just before the fetch. The token
   * value is never persisted or passed across a component boundary.
   */
  getAccessToken: () => Promise<string>;
  /** Called for each SSE chunk received */
  onChunk?: (chunk: AnalysisStreamChunk) => void;
  /** AbortSignal for cancellation */
  signal?: AbortSignal;
}

/**
 * Execute an analysis via the BFF SSE endpoint.
 *
 * NB: SSE bypasses authenticatedFetch because the ReadableStream lifecycle
 * doesn't fit fetch's request/response shape. We await getAccessToken() once
 * on the exact line before fetch(), per CLAUDE.md §D-AUTH-7.
 */
export async function executeAnalysis(params: ExecuteAnalysisParams): Promise<void> {
  const { analysisId, documentIds, actionId, playbookId, getAccessToken, onChunk, signal } = params;

  console.log(
    `${LOG_PREFIX} Executing analysis: ${analysisId} (action: ${actionId ?? 'none'}, playbook: ${playbookId ?? 'none'})`
  );

  const body: Record<string, unknown> = {
    analysisId,
    documentIds,
    outputType: 0, // Document
  };
  if (actionId) body.actionId = actionId;
  if (playbookId) body.playbookId = playbookId;

  // Auth v2 (D-AUTH-7): SSE exception site — token is acquired with fresh
  // `await getAccessToken()` on every stream open, never snapshotted.
  const token = await getAccessToken();
  const response = await fetch(`${getApiBaseUrl()}/api/ai/analysis/execute`, {
    method: 'POST',
    headers: {
      Authorization: `Bearer ${token}`,
      'Content-Type': 'application/json',
      Accept: 'text/event-stream',
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
      errorCode: 'NO_STREAM',
      message: 'Server did not return a stream',
    } as AnalysisError;
  }

  // Read SSE stream
  const reader = response.body.getReader();
  const decoder = new TextDecoder();
  let buffer = '';
  let chunkCount = 0;
  const startTime = Date.now();

  try {
    while (true) {
      const { done, value } = await reader.read();
      if (done) break;

      const decoded = decoder.decode(value, { stream: true });
      buffer += decoded;

      // Process complete SSE lines
      const lines = buffer.split('\n');
      buffer = lines.pop() ?? ''; // Keep incomplete last line

      for (const line of lines) {
        const trimmed = line.trim();
        if (!trimmed || trimmed.startsWith(':')) continue; // Skip empty/comment lines

        if (trimmed === 'data: [DONE]') {
          console.log(
            `${LOG_PREFIX} SSE complete (${chunkCount} chunks, ${((Date.now() - startTime) / 1000).toFixed(1)}s)`
          );
          onChunk?.({ type: 'status', content: 'done' });
          return;
        }

        if (trimmed.startsWith('data: ')) {
          try {
            const chunk = JSON.parse(trimmed.substring(6)) as AnalysisStreamChunk;
            chunkCount++;
            const elapsed = ((Date.now() - startTime) / 1000).toFixed(1);
            console.log(
              `${LOG_PREFIX} SSE [${chunkCount}] +${elapsed}s type=${chunk.type} ${chunk.content ? chunk.content.substring(0, 80) : ''}`
            );
            onChunk?.(chunk);

            if (chunk.type === 'error') {
              console.error(`${LOG_PREFIX} SSE error:`, chunk.error);
              throw {
                errorCode: 'EXECUTION_ERROR',
                message: chunk.error ?? 'Analysis execution failed',
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

  console.log(
    `${LOG_PREFIX} SSE stream ended (${chunkCount} chunks, ${((Date.now() - startTime) / 1000).toFixed(1)}s)`
  );
}

/**
 * Export an analysis to a downloadable document format.
 *
 * BFF route: POST /api/ai/analysis/{analysisId}/export
 *
 * @returns A Blob containing the exported document.
 */
export async function exportAnalysis(
  analysisId: string,
  format: ExportFormat,
  authenticatedFetch: AuthenticatedFetchFn
): Promise<Blob> {
  console.log(`${LOG_PREFIX} Exporting analysis: ${analysisId} as ${format}`);

  const controller = createTimeoutController(60_000); // 60s timeout for export

  const response = await authenticatedFetch(`${getApiBaseUrl()}/api/ai/analysis/${analysisId}/export`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
    },
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
