/**
 * executeSummarizeIntent.ts — R5 task 036 / P2-CLOSEOUT-05.
 *
 * Promote-and-execute orchestrator for the Summarize intent.
 *
 * Flow (per `notes/task-036-design-2026-06-05.md` §3.5):
 *
 *   1. For each held file, POST FormData (`file=<binary>`) to
 *      `/api/ai/chat/sessions/{sessionId}/documents`. Capture
 *      `{ documentId, fileName }` from the 202 response. If ANY response is
 *      non-ok, THROW immediately (no partial promotion). The caller surfaces
 *      an error chip in the chat thread.
 *
 *   2. Emit `context.files_staged` PaneEventBus event with ALL promoted
 *      documentIds + filenames. (Additive event type per R5 task 016.)
 *
 *   3. POST JSON `{ fileIds: [...] }` to
 *      `/api/ai/chat/sessions/{sessionId}/summarize` (the canonical R5 task
 *      014 endpoint). Body shape mirrors `SummarizeSessionRequest`
 *      (`Sprk.Bff.Api.Api.Ai.SummarizeSessionEndpoint`).
 *
 *      Server contract: returns `text/event-stream` with `AnalysisChunk`
 *      events. SSE consumption uses manual `fetch` + `ReadableStream` (NOT
 *      `authenticatedFetch`, which is for one-shot calls) — matching the
 *      pattern in `useSseStream.ts` (Auth v2 §H-4 / D-AUTH-7).
 *
 *   4. Consume the SSE stream as AnalysisChunk events. Pass each to the
 *      `sseToPaneEventBridge` instance and publish bridged events on the
 *      `workspace` channel.
 *
 *   5. On stream error (network, parse), emit a terminal streaming_complete
 *      with completionStatus="declined" and THROW so the caller can surface
 *      an error chip.
 *
 * Pure auth contract (ADR-028)
 * ────────────────────────────
 * - `authenticatedFetch` is used for the one-shot /documents POSTs (matches
 *   the @spaarke/auth contract for non-streaming requests).
 * - `getAccessToken` is used for the streaming /summarize POST (manual SSE
 *   loop) — token is re-acquired per call, NEVER snapshotted.
 *
 * Atomicity
 * ─────────
 * Per task-036 design §3.5: if ANY /documents POST fails, /summarize is NOT
 * called. The error is thrown out. Files remain "Held" so the user can retry.
 *
 * @see ADR-028 — Spaarke Auth v2; never snapshot tokens
 * @see ADR-030 — PaneEventBus channels closed at 4; this module emits ONLY
 *                `context.files_staged` + workspace.streaming_*` events
 *                within the existing channels
 * @see ADR-019 — ProblemDetails error contract; this module reads only
 *                stable errorCode strings, never raw exception text
 */

import {
  createSseToPaneEventBridge,
  type AnalysisChunk,
} from './sseToPaneEventBridge';
import type {
  WorkspacePaneEvent,
  ContextPaneEvent,
  PaneChannel,
  PaneChannelEventMap,
  StructuredOutputStreamWidgetData,
} from '@spaarke/ai-widgets';
import {
  STRUCTURED_OUTPUT_STREAM_WIDGET_TYPE,
  SUMMARIZE_SCHEMA,
  SUM_CHAT_OUTPUT_SCHEMA,
} from '@spaarke/ai-widgets';

// ---------------------------------------------------------------------------
// Public input types
// ---------------------------------------------------------------------------

/**
 * A file held in the chat thread (paperclip uploaded; not yet promoted to
 * server-side session-files). The orchestrator promotes these in order via
 * `POST /api/ai/chat/sessions/{id}/documents`.
 *
 * `file` carries the original binary — required because the `/documents`
 * endpoint accepts `multipart/form-data` with a binary `file` field
 * (`ChatDocumentEndpoints.cs` line ~206).
 *
 * NOTE: The current SprkChat `AttachmentChip` does NOT expose the original
 * File object (the hook consumes it during extraction). Surfacing the File
 * alongside the chip is a separate shared-library change tracked in
 * `notes/task-036-implementation-notes.md`. Until that lands, the host
 * SHOULD capture the File alongside the chip id at upload time (e.g. by
 * subscribing to the `onAttachmentReady` callback and storing the original
 * File reference passed to it — but that requires SprkChat to forward it).
 */
export interface HeldFile {
  /** Stable id matching the SprkChat `AttachmentChip.id`. */
  readonly id: string;
  /** Original binary file (for multipart upload). */
  readonly file: File;
}

/**
 * The dependencies executeSummarizeIntent needs from the host.
 */
export interface ExecuteSummarizeIntentInputs {
  /** BFF API base URL (e.g. `https://spaarke-bff.azurewebsites.net`). */
  readonly bffBaseUrl: string;
  /** Active chat session id. */
  readonly sessionId: string;
  /** Files to promote, in user order. */
  readonly heldFiles: ReadonlyArray<HeldFile>;
  /**
   * One-shot authenticated fetch (Auth v2 / ADR-028). Used for /documents
   * POSTs. NEVER snapshot the token; the implementation is provided by
   * `@spaarke/auth` `useAuth()`.
   */
  readonly authenticatedFetch: (url: string, init?: RequestInit) => Promise<Response>;
  /**
   * Fresh access-token getter (Auth v2 / ADR-028). Called ONCE per stream
   * open immediately before opening the fetch. Used for the /summarize SSE
   * stream where the consumer needs raw fetch + Authorization header (the
   * authenticatedFetch wrapper does not support streaming ReadableStream).
   */
  readonly getAccessToken: () => Promise<string>;
  /**
   * Publisher for PaneEventBus events. The host typically wires this from
   * `useDispatchPaneEvent()`. The orchestrator emits:
   *   - `context.files_staged` after promotion completes
   *   - `workspace.streaming_started` / `workspace.field_delta` /
   *     `workspace.streaming_complete` during the /summarize SSE stream
   */
  readonly publishPaneEvent: <C extends PaneChannel>(
    channel: C,
    event: PaneChannelEventMap[C]
  ) => void;
  /**
   * Optional style hint passed through to `/summarize` body (`style` field
   * in `SummarizeSessionRequest`).
   */
  readonly styleHint?: string;
  /** Optional AbortSignal forwarded to all fetch calls + the SSE stream. */
  readonly signal?: AbortSignal;
  /**
   * Optional stream id; defaults to a random one. Subscribers use this to
   * disambiguate concurrent streams.
   */
  readonly streamId?: string;
}

/**
 * Result of a successful execution.
 */
export interface ExecuteSummarizeIntentResult {
  /** Stream id emitted across all `workspace.streaming_*` events. */
  readonly streamId: string;
  /** Document ids assigned by the server (one per promoted file). */
  readonly documentIds: ReadonlyArray<string>;
  /** Filenames echoed back from the /documents 202 response. */
  readonly filenames: ReadonlyArray<string>;
}

// ---------------------------------------------------------------------------
// Implementation
// ---------------------------------------------------------------------------

/**
 * Response body of `POST /api/ai/chat/sessions/{id}/documents` (202 Accepted).
 * Mirrors `Sprk.Bff.Api.Models.Ai.Chat.DocumentUploadResponse`.
 */
interface DocumentUploadResponse {
  documentId: string;
  filename: string;
  status: string;
  pageCount?: number;
  tokenEstimate?: number;
  wasTruncated?: boolean;
}

function generateStreamId(): string {
  // Deterministic-ish unique id; not security-sensitive.
  return `summarize-${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 8)}`;
}

/**
 * Build the Summary-tab title suffix from the list of source filenames.
 *
 * - 0 names → '' (caller emits plain `Summary`).
 * - 1 name  → the filename verbatim.
 * - 2 names → `<a>, <b>` (joined by comma + space).
 * - 3+ names → `N files` (avoid unbounded title growth).
 *
 * @see Wave B-G9c2 — B8 per-run tab titles include the source filename.
 */
function formatTitleSuffix(names: ReadonlyArray<string>): string {
  if (!names || names.length === 0) return '';
  if (names.length === 1) return names[0];
  if (names.length === 2) return `${names[0]}, ${names[1]}`;
  return `${names.length} files`;
}

/**
 * Promote held files + run the deterministic Summarize action.
 *
 * Throws on any /documents error, any /summarize 4xx/5xx, SSE network error,
 * or terminal `error` chunk. Caller surfaces a chat-thread error chip.
 */
export async function executeSummarizeIntent(
  inputs: ExecuteSummarizeIntentInputs
): Promise<ExecuteSummarizeIntentResult> {
  const {
    bffBaseUrl,
    sessionId,
    heldFiles,
    authenticatedFetch,
    getAccessToken,
    publishPaneEvent,
    styleHint,
    signal,
  } = inputs;

  if (!sessionId) {
    throw new Error('executeSummarizeIntent: sessionId is required');
  }
  if (heldFiles.length === 0) {
    throw new Error('executeSummarizeIntent: heldFiles must be non-empty');
  }

  const streamId = inputs.streamId ?? generateStreamId();

  // ───────────────────────────────────────────────────────────────────────
  // Step 1: Promote each held file (atomic — abort on ANY failure)
  // ───────────────────────────────────────────────────────────────────────
  const documentsUrl = `${bffBaseUrl}/api/ai/chat/sessions/${encodeURIComponent(sessionId)}/documents`;
  const promotedIds: string[] = [];
  const promotedFilenames: string[] = [];

  for (const held of heldFiles) {
    const form = new FormData();
    form.append('file', held.file, held.file.name);

    const response = await authenticatedFetch(documentsUrl, {
      method: 'POST',
      body: form,
      signal,
    });

    if (!response.ok) {
      // Try to surface the server's stable errorCode (ADR-019). On parse
      // failure, fall back to status + filename.
      let errorCode = 'documents.upload-failed';
      try {
        const problem = (await response.json()) as { errorCode?: string };
        if (problem && typeof problem.errorCode === 'string') {
          errorCode = problem.errorCode;
        }
      } catch {
        // Body not JSON — keep fallback errorCode.
      }
      throw new Error(
        `executeSummarizeIntent: /documents POST failed (status=${response.status}, ` +
          `errorCode=${errorCode}, filename=${held.file.name})`
      );
    }

    const body = (await response.json()) as DocumentUploadResponse;
    if (!body || typeof body.documentId !== 'string') {
      throw new Error(
        `executeSummarizeIntent: /documents response missing documentId (filename=${held.file.name})`
      );
    }
    promotedIds.push(body.documentId);
    promotedFilenames.push(body.filename || held.file.name);
  }

  // ───────────────────────────────────────────────────────────────────────
  // Step 2: Emit context.files_staged (post-promotion confirmation)
  // ───────────────────────────────────────────────────────────────────────
  const stagedEvent: ContextPaneEvent = {
    type: 'files_staged',
    stagedFileIds: promotedIds.slice(),
  };
  publishPaneEvent('context', stagedEvent);

  // ───────────────────────────────────────────────────────────────────────
  // Step 3a: Hotfix Wave B-G9c2 (B7 + B8) — emit `workspace.widget_load` to
  // install a NEW Summary tab for THIS run (deferred install + per-run tab).
  //
  // B7 (defer install): WorkspacePane no longer auto-installs a Summary tab
  // on mount. The tab is created on demand by the existing `widget_load`
  // handler in WorkspacePane when this event fires.
  //
  // B8 (new tab per run): The streamId for THIS invocation is unique (the
  // caller defaults to `generateStreamId()` when no `streamId` is passed in
  // `ExecuteSummarizeIntentInputs`). The widget binds its correlationId to
  // the streamId so events from concurrent / subsequent runs land in their
  // own tabs (FR-06 restoration; the widget config in
  // `register-structured-output-stream-widget.ts` already has
  // `allowMultiple: true`).
  //
  // Tab title includes the source filename(s) when known. Up to 2 filenames
  // verbatim; 3+ collapse to "N files" to avoid runaway tab titles.
  // ───────────────────────────────────────────────────────────────────────
  const titleSuffix = formatTitleSuffix(promotedFilenames);
  const tabDisplayName = `Summary${titleSuffix ? `: ${titleSuffix}` : ''}`;

  const widgetData: StructuredOutputStreamWidgetData & {
    sessionId?: string;
    fileIds?: string[];
  } = {
    mode: 'streaming',
    schema: SUMMARIZE_SCHEMA,
    // R6 Hotfix Wave B-G9a parity (mirrors `dispatchSummarizeOnly` in
    // FilePreviewContextWidget): without `outputSchema`, `tldr` / `entities`
    // fall back to legacy display hints.
    outputSchema: SUM_CHAT_OUTPUT_SCHEMA,
    correlationId: streamId,
    title: tabDisplayName,
    sessionId,
    fileIds: promotedIds.slice(),
  };

  publishPaneEvent('workspace', {
    type: 'widget_load',
    widgetType: STRUCTURED_OUTPUT_STREAM_WIDGET_TYPE,
    widgetData,
    displayName: tabDisplayName,
  });

  // ───────────────────────────────────────────────────────────────────────
  // Step 3b: POST /summarize and consume the SSE stream
  // ───────────────────────────────────────────────────────────────────────
  const summarizeUrl = `${bffBaseUrl}/api/ai/chat/sessions/${encodeURIComponent(sessionId)}/summarize`;
  const bridge = createSseToPaneEventBridge(streamId);

  // Auth v2 §H-4 / D-AUTH-7: fresh token per stream open. NEVER snapshot.
  const token = await getAccessToken();

  const summarizeBody: Record<string, unknown> = { fileIds: promotedIds };
  if (styleHint) {
    summarizeBody.style = styleHint;
  }

  const response = await fetch(summarizeUrl, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      Authorization: `Bearer ${token}`,
    },
    body: JSON.stringify(summarizeBody),
    signal,
  });

  if (!response.ok) {
    // Pre-stream error (ProblemDetails JSON body). Surface errorCode if present.
    let errorCode = 'summarize.failed';
    try {
      const problem = (await response.json()) as { errorCode?: string };
      if (problem && typeof problem.errorCode === 'string') {
        errorCode = problem.errorCode;
      }
    } catch {
      // Non-JSON body — fall through.
    }
    // Emit a terminal "declined" event so subscribers can clear UI state.
    publishPaneEvent('workspace', {
      type: 'streaming_complete',
      streamId,
      completionStatus: 'declined',
    });
    throw new Error(
      `executeSummarizeIntent: /summarize POST failed (status=${response.status}, errorCode=${errorCode})`
    );
  }

  if (!response.body) {
    publishPaneEvent('workspace', {
      type: 'streaming_complete',
      streamId,
      completionStatus: 'declined',
    });
    throw new Error('executeSummarizeIntent: /summarize response body is empty');
  }

  const reader = response.body.getReader();
  const decoder = new TextDecoder();
  let buffer = '';
  let sawError = false;
  let sawComplete = false;

  try {
    // Main read loop — line-based SSE parse. SSE events are
    // separated by double newlines; each event has zero or more `data:` lines.
    // The AnalysisChunk JSON is the value of the `data:` line(s) joined.
    while (true) {
      const { done, value } = await reader.read();
      if (done) {
        break;
      }
      buffer += decoder.decode(value, { stream: true });

      // SSE events are separated by "\n\n".
      const parts = buffer.split('\n\n');
      buffer = parts.pop() ?? '';

      for (const part of parts) {
        const dataLines: string[] = [];
        for (const line of part.split('\n')) {
          if (line.startsWith('data:')) {
            dataLines.push(line.slice(5).trimStart());
          }
        }
        if (dataLines.length === 0) {
          continue;
        }

        const json = dataLines.join('\n');
        let chunk: AnalysisChunk;
        try {
          chunk = JSON.parse(json) as AnalysisChunk;
        } catch {
          // Malformed JSON — skip the event. Per ADR-019 we do not propagate
          // raw exception text. The bridge handles malformed AnalysisChunk
          // values defensively too, but we filter out parse-failures here.
          continue;
        }

        const busEvents = bridge.consume(chunk);
        for (const ev of busEvents) {
          publishPaneEvent('workspace', ev);
        }

        if (chunk.type === 'error') {
          sawError = true;
        }
        if (chunk.type === 'complete') {
          sawComplete = true;
        }
      }
    }

    // If the stream ended without emitting a terminal chunk, emit one
    // ourselves so subscribers can clear UI state (defensive — the server
    // SHOULD always emit a Completed or Error chunk).
    if (!sawError && !sawComplete) {
      publishPaneEvent('workspace', {
        type: 'streaming_complete',
        streamId,
        completionStatus: 'empty',
      });
    }
  } catch (err) {
    // Network error / abort / parse failure inside the loop.
    publishPaneEvent('workspace', {
      type: 'streaming_complete',
      streamId,
      completionStatus: 'declined',
    } satisfies WorkspacePaneEvent);
    // Re-throw so the caller can surface the chat-thread error chip.
    throw err;
  } finally {
    try {
      reader.releaseLock();
    } catch {
      // Cleanup-tail; safe to ignore.
    }
  }

  if (sawError) {
    throw new Error('executeSummarizeIntent: /summarize stream emitted an error chunk');
  }

  return {
    streamId,
    documentIds: promotedIds,
    filenames: promotedFilenames,
  };
}
