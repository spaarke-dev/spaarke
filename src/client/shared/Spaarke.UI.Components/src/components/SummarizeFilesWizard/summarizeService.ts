/**
 * summarizeService.ts
 * Service layer for the Summarize New File(s) wizard.
 * Calls the BFF summarize endpoint with uploaded files via SSE stream.
 *
 * Accepts `authenticatedFetch` and `bffBaseUrl` as parameters so the service
 * remains decoupled from any specific auth/config module.
 */
import type { IUploadedFile } from '../FileUpload/fileUploadTypes';
import type { ISummarizeResult } from './summarizeTypes';

const LOG_PREFIX = '[SummarizeService]';

// ---------------------------------------------------------------------------
// Authenticated fetch type
// ---------------------------------------------------------------------------

/** Type for an authenticated fetch function matching the standard Fetch API signature. */
export type AuthenticatedFetchFn = (
  input: RequestInfo | URL,
  init?: RequestInit,
) => Promise<Response>;

// ---------------------------------------------------------------------------
// SSE streaming entry point
// ---------------------------------------------------------------------------

export interface StreamSummarizeCallbacks {
  /** Called when a progress step event is received (step = "document_loaded" | "extracting_text" | ...) */
  onProgress?: (stepId: string) => void;
}

/**
 * Calls POST /api/workspace/files/summarize via SSE.
 * Fires onProgress callbacks as each pipeline step is announced.
 * Resolves with the structured ISummarizeResult when the stream completes.
 * Throws on error or if the stream ends with no result.
 */
export async function streamSummarize(
  files: IUploadedFile[],
  callbacks: StreamSummarizeCallbacks = {},
  signal?: AbortSignal,
  authenticatedFetch?: AuthenticatedFetchFn,
  bffBaseUrl?: string,
): Promise<ISummarizeResult> {
  const baseUrl = bffBaseUrl ?? '';
  const url = `${baseUrl}/api/workspace/files/summarize`;
  if (!authenticatedFetch) {
    throw new Error(`${LOG_PREFIX} authenticatedFetch is required — unauthenticated BFF calls are not permitted.`);
  }
  const fetchFn = authenticatedFetch;

  const formData = new FormData();
  for (const f of files) {
    formData.append('files', f.file, f.name);
  }

  console.info(`${LOG_PREFIX} Sending ${files.length} file(s) to ${url} (SSE)`);

  const response = await fetchFn(url, {
    method: 'POST',
    body: formData,
    signal,
  });

  if (!response.ok) {
    const errorText = await response.text().catch(() => 'Unknown error');
    console.error(`${LOG_PREFIX} BFF returned ${response.status}: ${errorText}`);
    throw new Error(`Summarize failed (${response.status}): ${errorText}`);
  }

  if (!response.body) {
    throw new Error('Response body is not readable');
  }

  const reader = response.body.getReader();
  const decoder = new TextDecoder();
  let buffer = '';
  let rawResult: unknown = null;

  try {
    while (true) {
      const { done, value } = await reader.read();
      if (done) break;

      buffer += decoder.decode(value, { stream: true });
      const events = buffer.split('\n\n');
      buffer = events.pop() ?? '';

      for (const event of events) {
        for (const line of event.split('\n')) {
          const trimmed = line.trim();
          if (!trimmed.startsWith('data:')) continue;
          const jsonStr = trimmed.slice(5).trim();
          if (!jsonStr || jsonStr === '[DONE]') continue;

          let chunk: { type?: string; step?: string; content?: string; error?: string; done?: boolean };
          try {
            chunk = JSON.parse(jsonStr);
          } catch {
            continue;
          }

          if (chunk.type === 'progress' && chunk.step) {
            callbacks.onProgress?.(chunk.step);
          } else if (chunk.type === 'result' && chunk.content) {
            try {
              rawResult = JSON.parse(chunk.content);
            } catch {
              console.warn(`${LOG_PREFIX} Failed to parse result content as JSON`);
            }
          } else if (chunk.type === 'error') {
            throw new Error(chunk.error ?? chunk.content ?? 'Summarization failed');
          } else if (chunk.done) {
            break;
          }
        }
      }
    }
  } finally {
    reader.releaseLock();
  }

  if (!rawResult) {
    throw new Error('Summarize stream ended without a result');
  }

  const normalized = normalizeResult(rawResult);
  console.info(`${LOG_PREFIX} SSE summary received, confidence=${normalized?.confidence}`);
  return normalized;
}

// ---------------------------------------------------------------------------
// Result normalization (shared by both streamSummarize and runSummarize)
// ---------------------------------------------------------------------------

function normalizeResult(raw: unknown): ISummarizeResult {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  let result: any = raw;

  // Unwrap rawResponse envelope if present (GenericAnalysisHandler wraps output this way)
  if (result && typeof result === 'object' && 'rawResponse' in result) {
    try {
      result = typeof result.rawResponse === 'string'
        ? JSON.parse(result.rawResponse)
        : result.rawResponse;
    } catch {
      result = {
        tldr: String(result.rawResponse).substring(0, 200),
        summary: String(result.rawResponse),
        shortSummary: String(result.rawResponse).substring(0, 200),
        confidence: 0.5,
      };
    }
  }

  if (result && typeof result === 'object') {
    if (result.fileHighlights) {
      console.info(`${LOG_PREFIX} fileHighlights (${Array.isArray(result.fileHighlights) ? result.fileHighlights.length : typeof result.fileHighlights} items):`,
        JSON.stringify(result.fileHighlights).substring(0, 800));
    }
    if (result.mentionedParties) {
      console.info(`${LOG_PREFIX} mentionedParties:`, JSON.stringify(result.mentionedParties).substring(0, 400));
    }

    // fileHighlights: ensure each entry is { fileName, documentType, highlights[] }
    if (result.fileHighlights && Array.isArray(result.fileHighlights)) {
      const rawFh = result.fileHighlights;
      if (rawFh.length > 0 && typeof rawFh[0] === 'string') {
        const parsed: { fileName: string; documentType: string; summary: string; highlights: string[] }[] = [];
        let current: { fileName: string; documentType: string; summary: string; highlights: string[] } | null = null;
        const fileExtPattern = /\.(docx?|pdf|xlsx?|pptx?|txt|csv|rtf|msg|eml)$/i;
        type ParsePhase = 'expect-type' | 'expect-summary' | 'in-summary' | 'in-highlights';
        let phase: ParsePhase = 'expect-type';

        for (const line of rawFh as string[]) {
          const trimmed = line.trim();
          if (!trimmed) continue;
          const docPrefixMatch = trimmed.match(/^Document:\s*(.+)/);
          const isFileName = fileExtPattern.test(trimmed) || docPrefixMatch;
          if (isFileName) {
            if (current) parsed.push(current);
            const name = docPrefixMatch ? docPrefixMatch[1] : trimmed;
            current = { fileName: name, documentType: '', summary: '', highlights: [] };
            phase = 'expect-type';
          } else if (/^Type:\s*/i.test(trimmed) && current) {
            current.documentType = trimmed.replace(/^Type:\s*/i, '');
            phase = 'expect-summary';
          } else if (/^Summary:\s*/i.test(trimmed) && current) {
            const inlineText = trimmed.replace(/^Summary:\s*/i, '');
            if (inlineText) current.summary = inlineText;
            phase = 'in-summary';
          } else if (/^Highlights?:/i.test(trimmed)) {
            phase = 'in-highlights';
          } else if (trimmed.startsWith('-') && current) {
            phase = 'in-highlights';
            current.highlights.push(trimmed.replace(/^-\s*/, ''));
          } else if (current) {
            if (phase === 'expect-type' && !current.documentType) {
              current.documentType = trimmed;
              phase = 'expect-summary';
            } else if (phase === 'expect-summary' || phase === 'in-summary') {
              current.summary = current.summary ? `${current.summary} ${trimmed}` : trimmed;
              phase = 'in-summary';
            } else if (phase === 'in-highlights') {
              current.highlights.push(trimmed);
            }
          }
        }
        if (current) parsed.push(current);
        result.fileHighlights = parsed;
        console.info(`${LOG_PREFIX} Parsed ${parsed.length} file highlights from flat string format`);
      } else {
        result.fileHighlights = rawFh
          .map((fh: unknown) => {
            if (typeof fh !== 'object' || fh === null) return null;
            const obj = fh as Record<string, unknown>;
            const fileName = (obj.fileName ?? obj.file_name ?? obj.name ?? 'Unknown') as string;
            const documentType = (obj.documentType ?? obj.document_type ?? obj.type ?? '') as string;
            const summary = (obj.summary ?? obj.fileSummary ?? obj.file_summary ?? '') as string;
            const highlights = Array.isArray(obj.highlights) ? obj.highlights
              : Array.isArray(obj.key_points) ? obj.key_points
              : [];
            return { fileName, documentType, summary, highlights };
          })
          .filter(Boolean);
      }
    } else if (result.fileHighlights && !Array.isArray(result.fileHighlights)) {
      result.fileHighlights = [];
    }

    if (result.practiceAreas && !Array.isArray(result.practiceAreas)) {
      result.practiceAreas = [];
    }

    if (result.mentionedParties && Array.isArray(result.mentionedParties)) {
      result.mentionedParties = result.mentionedParties
        .map((p: unknown) => {
          if (typeof p === 'string') return { name: p, role: '' };
          if (typeof p !== 'object' || p === null) return null;
          const obj = p as Record<string, unknown>;
          const name = (obj.name ?? obj.partyName ?? obj.party_name ?? '') as string;
          const role = (obj.role ?? obj.partyRole ?? obj.party_role ?? '') as string;
          return name ? { name, role } : null;
        })
        .filter(Boolean);
    } else if (result.mentionedParties && !Array.isArray(result.mentionedParties)) {
      result.mentionedParties = [];
    }
  }

  return result as ISummarizeResult;
}

// ---------------------------------------------------------------------------
// Legacy REST entry point (kept for backward compat — BFF now streams SSE)
// ---------------------------------------------------------------------------

/**
 * @deprecated Use streamSummarize instead. This REST endpoint no longer exists on the BFF.
 */
export async function runSummarize(
  files: IUploadedFile[],
  signal?: AbortSignal,
  authenticatedFetch?: AuthenticatedFetchFn,
  bffBaseUrl?: string,
): Promise<ISummarizeResult> {
  const baseUrl = bffBaseUrl ?? '';
  const url = `${baseUrl}/api/workspace/files/summarize`;
  if (!authenticatedFetch) {
    throw new Error(`${LOG_PREFIX} authenticatedFetch is required — unauthenticated BFF calls are not permitted.`);
  }
  const fetchFn = authenticatedFetch;

  const formData = new FormData();
  for (const f of files) {
    formData.append('files', f.file, f.name);
  }

  console.info(`${LOG_PREFIX} Sending ${files.length} file(s) to ${url}`);

  const response = await fetchFn(url, {
    method: 'POST',
    body: formData,
    signal,
  });

  if (!response.ok) {
    const errorText = await response.text().catch(() => 'Unknown error');
    console.error(`${LOG_PREFIX} BFF returned ${response.status}: ${errorText}`);
    throw new Error(`Summarize failed (${response.status}): ${errorText}`);
  }

  const json = await response.json();
  console.info(`${LOG_PREFIX} Raw response:`, JSON.stringify(json).substring(0, 500));

  // The BFF returns { result: <playbook output> }
  const normalized = normalizeResult(json.result);
  console.info(`${LOG_PREFIX} Summary received, confidence=${normalized?.confidence}`);
  return normalized;
}
