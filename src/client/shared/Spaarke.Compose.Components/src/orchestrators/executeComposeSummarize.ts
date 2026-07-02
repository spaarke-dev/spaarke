/**
 * executeComposeSummarize.ts — spaarkeai-compose-r1 task 098 / Phase 9.
 *
 * SSE consumer for `POST /api/compose/action/compose-summarize` (task 097 backend).
 *
 * ────────────────────────────────────────────────────────────────────────────
 * Flow
 * ────────────────────────────────────────────────────────────────────────────
 *
 *   1. POST JSON body to `${bffBaseUrl}/api/compose/action/compose-summarize`
 *      with `Accept: text/event-stream`.
 *   2. Consume the `ReadableStream<Uint8Array>` response body line-by-line
 *      using a TextDecoder + `\n\n` framing rule (canonical SSE).
 *   3. Each frame is one or more `data: <json>` lines whose payload is an
 *      `AnalysisStreamChunk` (see `Api/Ai/AnalysisEndpoints.cs`). The stream
 *      terminates with a literal `data: [DONE]` sentinel emitted by
 *      `ComposeEndpoints.DispatchAction`.
 *   4. Chunks are demultiplexed by their `type` discriminant and forwarded to
 *      the caller's callbacks:
 *
 *         type='progress'  → onProgress(step, message)
 *         type='result'    → onResult(parsedResultPayload)
 *         type='done'      → onDone()  — terminal (matches TokenUsage carry)
 *         type='error'     → onError(message) — terminal
 *
 *   5. Any network failure, malformed response, or unhandled exception fires
 *      `onError` and returns (does NOT throw — the caller drives UI state
 *      exclusively through callbacks).
 *
 * ────────────────────────────────────────────────────────────────────────────
 * Auth contract (ADR-028 / D-AUTH-7)
 * ────────────────────────────────────────────────────────────────────────────
 * - `getAccessToken` is called ONCE per stream open, immediately before the
 *   fetch. Token is NEVER snapshotted across streams.
 * - The `authenticatedFetch` wrapper CANNOT be used here because it does not
 *   preserve the `ReadableStream<Uint8Array>` body — Chrome/Firefox return an
 *   already-drained clone otherwise. We use raw `fetch` + `Authorization`
 *   header + fresh token, matching the pattern in `executeSummarizeIntent.ts`.
 *
 * ────────────────────────────────────────────────────────────────────────────
 * Cancellation
 * ────────────────────────────────────────────────────────────────────────────
 * - Optional `signal` is forwarded to `fetch`. On abort, the reader loop
 *   throws; we swallow it and DO NOT invoke `onError` (aborts are
 *   caller-intentional, not stream errors).
 *
 * ────────────────────────────────────────────────────────────────────────────
 * Reference
 * ────────────────────────────────────────────────────────────────────────────
 * - Spec supplement `spec-supplement-2026-07-01-three-pane-pivot.md` FR-S6.
 * - Task 097 backend: `Api/ComposeEndpoints.cs` DispatchAction (SSE).
 * - Sibling frontend precedent: `executeSummarizeIntent.ts` in SpaarkeAi
 *   (chat-session-scoped summarize SSE) — this module differs in that Compose
 *   consumes a raw structured payload rather than routing through the
 *   sseToPaneEventBridge (Compose's chat surface renders the final
 *   `textContent` directly as an assistant message; there is no
 *   streaming-widget target).
 */

/**
 * Discriminated shape of the `data: {...}` events emitted by the compose
 * SSE endpoint (mirrors C# `AnalysisStreamChunk` in `AnalysisEndpoints.cs`).
 *
 * We intentionally do NOT re-export a strict `AnalysisStreamChunk` type from
 * a shared TypeScript surface — the SSE contract is stable JSON and inlining
 * the shape here avoids coupling to any specific TS emit of the C# record.
 */
interface ComposeSseChunk {
  readonly type: 'progress' | 'result' | 'done' | 'error' | 'metadata' | 'chunk';
  readonly content?: string | null;
  readonly step?: string | null;
  readonly error?: string | null;
  readonly done?: boolean;
  readonly analysisId?: string | null;
}

/**
 * Parsed `type='result'` payload. Field-compatible with the pre-SSE
 * `ComposeActionResponse` shape (see `Api/ComposeEndpoints.cs` SseJsonOptions
 * `resultPayload` — task 097 preserves this shape verbatim so existing
 * consumers keep working).
 */
export interface ComposeSummarizeResult {
  readonly runId?: string;
  readonly textContent: string;
  readonly structuredData?: unknown;
  readonly confidence?: number;
  readonly durationMs?: number;
  readonly citationCount?: number;
  readonly correlationId?: string;
}

/**
 * Compose-summarize orchestrator inputs.
 *
 * All fields except the `on*` callbacks map directly to the BFF request body
 * shape declared in `ComposeEndpoints.DispatchActionBody` (task 097).
 */
export interface ExecuteComposeSummarizeInputs {
  /** BFF base URL (host only, e.g. `https://host.azurewebsites.net`). */
  readonly bffBaseUrl: string;

  /** SPE drive-item id of the ephemeral document being summarized. */
  readonly documentSpeId: string;

  /** SPE driveId — required query param on the BFF Load endpoint. */
  readonly driveId: string;

  /** Microsoft Entra tenant id (ADR-015 Tier 3 scoping). */
  readonly tenantId: string;

  /**
   * Active ChatSession id (Tier 1 safe correlation identifier). Optional
   * on the wire but the toolbar always emits a value.
   */
  readonly sessionId?: string;

  /** `sprk_documentid` GUID (post-promotion) — optional. */
  readonly documentRecordId?: string;

  /** Optional document display name for logging + telemetry. */
  readonly documentName?: string;

  /**
   * Fresh access-token getter (Auth v2 / ADR-028). Called ONCE per stream
   * open immediately before opening the fetch. NEVER snapshot.
   */
  readonly getAccessToken: () => Promise<string>;

  /** Optional AbortSignal — aborts fetch + reader loop. */
  readonly signal?: AbortSignal;

  /**
   * Progress events. Called for each `type='progress'` chunk with the
   * server-emitted `step` slug + human-readable `message`.
   */
  readonly onProgress?: (step: string, message: string) => void;

  /**
   * Terminal-success callback. Invoked exactly once with the parsed
   * `type='result'` payload when the server signals a successful playbook
   * invocation. Guaranteed to fire BEFORE the terminal `data: [DONE]`.
   */
  readonly onResult?: (result: ComposeSummarizeResult) => void;

  /**
   * Terminal-failure callback. Fired on `type='error'` chunk, pre-stream
   * HTTP failure, network abort (except caller-driven AbortSignal), or
   * malformed response body.
   */
  readonly onError?: (message: string) => void;

  /**
   * Terminal callback. Fires exactly once when the stream is closed
   * (either after `type='done'` chunk, `data: [DONE]` sentinel, or on
   * error). Callers use this to clear an "in-flight" UI state.
   */
  readonly onDone?: () => void;
}

/**
 * Run the compose-summarize SSE flow. Never throws — all failures surface
 * through `onError` + `onDone`. Aborts (via `signal`) invoke `onDone` only
 * (aborts are caller-intentional; no error is surfaced).
 *
 * Returns after the stream has closed (either terminal event, sentinel, or
 * error). The caller may `await` this promise to know when the stream is
 * fully drained.
 */
export async function executeComposeSummarize(
  inputs: ExecuteComposeSummarizeInputs
): Promise<void> {
  const {
    bffBaseUrl,
    documentSpeId,
    driveId,
    tenantId,
    sessionId,
    documentRecordId,
    documentName,
    getAccessToken,
    signal,
    onProgress,
    onResult,
    onError,
    onDone,
  } = inputs;

  if (!bffBaseUrl || !documentSpeId || !driveId || !tenantId) {
    onError?.(
      'Compose summarize: missing required inputs (bffBaseUrl, documentSpeId, driveId, tenantId).'
    );
    onDone?.();
    return;
  }

  const url = `${bffBaseUrl.replace(/\/$/, '')}/api/compose/action/compose-summarize`;
  const body = {
    documentSpeId,
    tenantId,
    driveId,
    sessionId: sessionId || undefined,
    documentRecordId: documentRecordId || undefined,
    documentName: documentName || undefined,
  };

  let doneFired = false;
  const fireDone = (): void => {
    if (!doneFired) {
      doneFired = true;
      onDone?.();
    }
  };

  let token: string;
  try {
    token = await getAccessToken();
  } catch (err) {
    const message = err instanceof Error ? err.message : String(err);
    onError?.(`Compose summarize: failed to acquire access token — ${message}`);
    fireDone();
    return;
  }

  let response: Response;
  try {
    response = await fetch(url, {
      method: 'POST',
      headers: {
        Accept: 'text/event-stream',
        'Content-Type': 'application/json',
        Authorization: `Bearer ${token}`,
      },
      body: JSON.stringify(body),
      signal,
    });
  } catch (err) {
    if (signal?.aborted) {
      fireDone();
      return;
    }
    const message = err instanceof Error ? err.message : String(err);
    onError?.(`Compose summarize: network error — ${message}`);
    fireDone();
    return;
  }

  if (!response.ok) {
    let detail = `HTTP ${response.status}`;
    try {
      const text = await response.text();
      if (text) detail += ` — ${text.slice(0, 400)}`;
    } catch {
      /* body unavailable */
    }
    onError?.(`Compose summarize: request rejected (${detail}).`);
    fireDone();
    return;
  }

  if (!response.body) {
    onError?.('Compose summarize: response body was empty.');
    fireDone();
    return;
  }

  const reader = response.body.getReader();
  const decoder = new TextDecoder();
  let buffer = '';
  let sawResult = false;
  let sawDone = false;
  let sawError = false;

  try {
    // SSE frame loop. Each event is separated by "\n\n"; each event has one
    // or more "data:" lines whose values concatenate to a JSON string, OR
    // the literal terminal sentinel `[DONE]`.
    while (true) {
      const { done, value } = await reader.read();
      if (done) break;

      buffer += decoder.decode(value, { stream: true });

      const parts = buffer.split('\n\n');
      buffer = parts.pop() ?? '';

      for (const part of parts) {
        const dataLines: string[] = [];
        for (const line of part.split('\n')) {
          if (line.startsWith('data:')) {
            dataLines.push(line.slice(5).trimStart());
          }
        }
        if (dataLines.length === 0) continue;

        const joined = dataLines.join('\n');

        if (joined === '[DONE]') {
          // Terminal sentinel — stream is done. Exit outer loop cleanly.
          sawDone = true;
          break;
        }

        let chunk: ComposeSseChunk;
        try {
          chunk = JSON.parse(joined) as ComposeSseChunk;
        } catch {
          // Skip malformed frames — the server may emit non-JSON keepalives
          // in the future; defensive-parse rather than fail the stream.
          continue;
        }

        switch (chunk.type) {
          case 'progress': {
            const step = chunk.step ?? '';
            const message = chunk.content ?? '';
            onProgress?.(step, message);
            break;
          }
          case 'result': {
            // The result chunk carries `content` as a JSON-encoded payload
            // matching the pre-SSE ComposeActionResponse shape.
            if (chunk.content) {
              try {
                const parsed = JSON.parse(chunk.content) as ComposeSummarizeResult;
                if (typeof parsed.textContent === 'string' && parsed.textContent.length > 0) {
                  sawResult = true;
                  onResult?.(parsed);
                } else {
                  sawError = true;
                  onError?.('Compose summarize: server returned an empty text result.');
                }
              } catch (err) {
                sawError = true;
                const message = err instanceof Error ? err.message : String(err);
                onError?.(`Compose summarize: could not parse result payload — ${message}`);
              }
            } else {
              sawError = true;
              onError?.('Compose summarize: server emitted a result event with no content.');
            }
            break;
          }
          case 'done': {
            sawDone = true;
            break;
          }
          case 'error': {
            sawError = true;
            onError?.(chunk.error ?? 'Compose summarize: unknown server error.');
            break;
          }
          default:
            // Unknown chunk type — ignore. The stream contract is forward-
            // compatible; new chunk kinds do not break existing consumers.
            break;
        }

        if (sawDone) break;
      }

      if (sawDone) break;
    }

    // If the stream ended without a terminal chunk, treat it as success only
    // if a result was seen; otherwise surface an error.
    if (!sawDone && !sawResult && !sawError) {
      onError?.('Compose summarize: stream ended without a terminal event.');
    }
  } catch (err) {
    if (signal?.aborted) {
      // Caller-initiated abort — no error surfacing.
      fireDone();
      return;
    }
    const message = err instanceof Error ? err.message : String(err);
    onError?.(`Compose summarize: stream read failed — ${message}`);
  } finally {
    try {
      reader.releaseLock();
    } catch {
      // Cleanup-tail; safe to ignore.
    }
    fireDone();
  }
}
