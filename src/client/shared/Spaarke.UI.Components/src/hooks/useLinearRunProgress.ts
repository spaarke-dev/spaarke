/**
 * useLinearRunProgress — Shared Linear AI Consumer SSE progress hook
 *
 * Consumes the canonical `AnalysisStreamChunk` SSE envelope emitted by R7 Wave 12
 * "Linear AI Consumers" (Document Upload / Profile Document, File Summarize, and
 * any future linear consumer). Presents a UI-friendly event history plus the
 * final structured result so multiple client surfaces can render the same
 * stream honestly (no client-side step interpretation).
 *
 * Server-side wire contract (see `AnalysisStreamChunk` record in
 * `src/server/api/Sprk.Bff.Api/Api/Ai/AnalysisEndpoints.cs` around line 894):
 *
 *   Type: "metadata" | "progress" | "chunk" | "result" | "done" | "error"
 *   Content: string | null  (progress message text, chunk tokens, result JSON)
 *   Step: string | null     (stable key on progress chunks, e.g. "extracting_text")
 *   Done: bool
 *   AnalysisId: Guid | null
 *   DocumentName: string | null
 *   TokenUsage: { Input, Output } | null
 *   Error: string | null
 *
 * Terminator lines: `data: [DONE]` (raw, not a chunk).
 *
 * Why this exists: the Document Upload wizard renders progress as scrolling
 * text (honest — shows exactly what the server sent), the Summarize wizard
 * used to render a hardcoded 5-step visual bar (misleading — steps did not
 * reflect real state). This hook is the shared substrate that lets every
 * client surface adopt the honest scrolling pattern with zero server-format
 * interpretation on the client. SprkChat's Context pane will consume the
 * same hook in a follow-on task.
 *
 * The hook wraps `fetch()` + `ReadableStream.getReader()` because Linear
 * consumers include multipart/form-data uploads (File Summarize) that
 * EventSource cannot send.
 *
 * @see AnalysisEndpoints.cs (server-side envelope + factory methods)
 * @see useSseStream.ts (companion SSE hook for chat message streams)
 * @see LinearRunProgressList.tsx (default presenter for this hook's events)
 */

import { useCallback, useEffect, useRef, useState } from 'react';

// ─────────────────────────────────────────────────────────────────────────────
// Public types
// ─────────────────────────────────────────────────────────────────────────────

/**
 * The kinds of events a Linear AI Consumer emits. Matches the server-side
 * `AnalysisStreamChunk.Type` discriminator plus a synthetic "done" derived
 * from the terminator line.
 */
export type LinearRunEventKind = 'metadata' | 'progress' | 'chunk' | 'result' | 'error' | 'done';

/**
 * A single event in the Linear run history. The kind mirrors the server's
 * chunk type; `content` and `step` carry the raw server strings without
 * interpretation.
 */
export interface LinearRunEvent {
  /** Chunk type discriminator. */
  kind: LinearRunEventKind;
  /** Stable step key for grouping / filtering (progress chunks only, e.g. `"extracting_text"`). */
  step?: string;
  /** Human-readable message text (progress chunks) or streamed token text (chunk chunks). */
  content?: string;
  /** When the client observed this event. */
  timestamp: Date;
}

/**
 * The full state of an in-progress or finished Linear run.
 */
export interface LinearRunState {
  /** Lifecycle state. */
  status: 'idle' | 'running' | 'complete' | 'error';
  /** Append-only event history in arrival order. */
  events: LinearRunEvent[];
  /** Most recent event, if any. Convenience mirror of `events[events.length - 1]`. */
  latest?: LinearRunEvent;
  /** Parsed JSON payload from a `result` chunk, when the consumer emits one. */
  result?: unknown;
  /** Error message when `status === 'error'`. */
  error?: string;
  /** Token usage reported on the `done` chunk, when present. */
  tokenUsage?: { input: number; output: number };
}

/**
 * Options for {@link useLinearRunProgress}.
 */
export interface UseLinearRunProgressOptions {
  /**
   * Opens the SSE stream. Caller controls the URL, body, headers, and auth so
   * the hook stays context-agnostic. MUST return a `Response` whose body is
   * a readable SSE stream (Content-Type `text/event-stream`).
   */
  start: () => Promise<Response>;
  /**
   * When true, calls `start` on mount. Defaults to `false` so the caller can
   * gate stream opening (e.g. wait for uploads to be ready).
   */
  autoStart?: boolean;
}

/**
 * Return type of {@link useLinearRunProgress}.
 */
export type UseLinearRunProgressResult = LinearRunState & {
  /** Explicit trigger for the SSE stream when `autoStart` is false. */
  start: () => void;
  /** Aborts an in-flight stream. Safe to call in any state. */
  cancel: () => void;
};

// ─────────────────────────────────────────────────────────────────────────────
// Internal wire types (mirror server AnalysisStreamChunk record)
// ─────────────────────────────────────────────────────────────────────────────

interface AnalysisStreamChunkWire {
  type?: string;
  content?: string | null;
  done?: boolean;
  step?: string | null;
  analysisId?: string | null;
  documentName?: string | null;
  tokenUsage?: { input: number; output: number } | null;
  error?: string | null;
}

// ─────────────────────────────────────────────────────────────────────────────
// SSE line parsing
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Parses a single SSE `data: ...` line into a wire chunk. Returns `null` for
 * non-data lines, blanks, and the `[DONE]` terminator (the reader loop treats
 * `[DONE]` separately since it has no chunk payload).
 */
function parseSseLine(line: string): AnalysisStreamChunkWire | null | 'DONE' {
  const trimmed = line.trim();
  if (!trimmed || trimmed.startsWith(':')) return null;
  if (!trimmed.startsWith('data:')) return null;

  const jsonStr = trimmed.slice(5).trim();
  if (!jsonStr) return null;
  if (jsonStr === '[DONE]') return 'DONE';

  try {
    return JSON.parse(jsonStr) as AnalysisStreamChunkWire;
  } catch {
    // Malformed chunk — treat as no-op rather than fatal; the run may recover.
    return null;
  }
}

/**
 * Maps a server wire chunk to a public {@link LinearRunEvent}, preserving the
 * server-emitted content and step verbatim. Returns `null` when the chunk type
 * is unknown so the reader loop can skip it.
 */
function toLinearRunEvent(chunk: AnalysisStreamChunkWire): LinearRunEvent | null {
  const kind = chunk.type as LinearRunEventKind | undefined;
  if (!kind || (kind !== 'metadata' && kind !== 'progress' && kind !== 'chunk' && kind !== 'result' && kind !== 'error' && kind !== 'done')) {
    return null;
  }

  return {
    kind,
    step: chunk.step ?? undefined,
    content: chunk.content ?? undefined,
    timestamp: new Date(),
  };
}

// ─────────────────────────────────────────────────────────────────────────────
// Hook
// ─────────────────────────────────────────────────────────────────────────────

const INITIAL_STATE: LinearRunState = {
  status: 'idle',
  events: [],
};

/**
 * Consumes a Linear AI Consumer SSE stream and exposes an append-only event
 * history + terminal state (result, error, token usage).
 *
 * Caller owns the fetch (URL, body, auth headers) via {@link UseLinearRunProgressOptions.start},
 * so the hook is reusable across every Linear consumer surface (Document
 * Upload, File Summarize, future Context-pane consumers).
 *
 * @example
 * ```tsx
 * const run = useLinearRunProgress({
 *   start: async () => {
 *     const form = new FormData();
 *     files.forEach(f => form.append('files', f.file, f.name));
 *     return authenticatedFetch(`${bffBaseUrl}/api/workspace/files/summarize`, {
 *       method: 'POST',
 *       body: form,
 *     });
 *   },
 * });
 *
 * return (
 *   <>
 *     <Button onClick={run.start} disabled={run.status === 'running'}>
 *       Analyze
 *     </Button>
 *     <LinearRunProgressList events={run.events} />
 *   </>
 * );
 * ```
 */
export function useLinearRunProgress(options: UseLinearRunProgressOptions): UseLinearRunProgressResult {
  const { start: startFn, autoStart = false } = options;

  const [state, setState] = useState<LinearRunState>(INITIAL_STATE);
  const abortRef = useRef<AbortController | null>(null);
  const activeRef = useRef<boolean>(false);
  const startFnRef = useRef(startFn);
  startFnRef.current = startFn;

  const appendEvent = useCallback((event: LinearRunEvent) => {
    setState(prev => ({
      ...prev,
      events: [...prev.events, event],
      latest: event,
    }));
  }, []);

  const cancel = useCallback(() => {
    if (abortRef.current) {
      abortRef.current.abort();
      abortRef.current = null;
    }
    activeRef.current = false;
  }, []);

  const start = useCallback(() => {
    // Abort any in-flight stream before starting a new run.
    if (abortRef.current) {
      abortRef.current.abort();
    }

    const controller = new AbortController();
    abortRef.current = controller;
    activeRef.current = true;

    // Reset state — a fresh run replaces prior history.
    setState({
      status: 'running',
      events: [],
    });

    void (async () => {
      try {
        const response = await startFnRef.current();

        if (!response.ok) {
          const errorText = await response.text().catch(() => `HTTP ${response.status}`);
          throw new Error(errorText || `HTTP ${response.status}`);
        }

        if (!response.body) {
          throw new Error('Response body is not readable');
        }

        const reader = response.body.getReader();
        const decoder = new TextDecoder();
        let buffer = '';

        try {
          // eslint-disable-next-line no-constant-condition
          while (true) {
            if (controller.signal.aborted) {
              break;
            }

            const { done, value } = await reader.read();
            if (done) break;

            buffer += decoder.decode(value, { stream: true });
            const events = buffer.split('\n\n');
            buffer = events.pop() ?? '';

            for (const eventBlock of events) {
              for (const line of eventBlock.split('\n')) {
                const parsed = parseSseLine(line);
                if (parsed === null) continue;
                if (parsed === 'DONE') {
                  // Terminator — treat as clean end. If the server did not emit a
                  // `done` chunk, synthesize completion here.
                  setState(prev =>
                    prev.status === 'running' ? { ...prev, status: 'complete' } : prev
                  );
                  return;
                }

                // Error chunk → terminal error state.
                if (parsed.type === 'error') {
                  const message = parsed.error ?? parsed.content ?? 'Linear run failed';
                  const event: LinearRunEvent = {
                    kind: 'error',
                    content: message,
                    timestamp: new Date(),
                  };
                  setState(prev => ({
                    ...prev,
                    status: 'error',
                    error: message,
                    events: [...prev.events, event],
                    latest: event,
                  }));
                  return;
                }

                // Done chunk → capture token usage + mark complete.
                if (parsed.type === 'done' || parsed.done === true) {
                  const event: LinearRunEvent = {
                    kind: 'done',
                    content: parsed.content ?? undefined,
                    timestamp: new Date(),
                  };
                  setState(prev => ({
                    ...prev,
                    status: 'complete',
                    tokenUsage: parsed.tokenUsage ?? prev.tokenUsage,
                    events: [...prev.events, event],
                    latest: event,
                  }));
                  return;
                }

                // Result chunk → parse the raw JSON body once and store.
                if (parsed.type === 'result') {
                  let parsedResult: unknown = parsed.content;
                  if (typeof parsed.content === 'string') {
                    try {
                      parsedResult = JSON.parse(parsed.content);
                    } catch {
                      // Leave as raw string when JSON parse fails; caller can
                      // still inspect it. ADR-015: do not log payload content.
                    }
                  }
                  const event: LinearRunEvent = {
                    kind: 'result',
                    content: parsed.content ?? undefined,
                    timestamp: new Date(),
                  };
                  setState(prev => ({
                    ...prev,
                    result: parsedResult,
                    events: [...prev.events, event],
                    latest: event,
                  }));
                  continue;
                }

                // Metadata / progress / chunk — append as-is.
                const event = toLinearRunEvent(parsed);
                if (event) {
                  appendEvent(event);
                }
              }
            }
          }

          // Stream ended without an explicit `[DONE]` or `done` chunk.
          setState(prev => (prev.status === 'running' ? { ...prev, status: 'complete' } : prev));
        } finally {
          try {
            reader.releaseLock();
          } catch {
            // Reader may already be released; ignore.
          }
        }
      } catch (err) {
        if (err instanceof DOMException && err.name === 'AbortError') {
          // Cancelled — leave state as-is so caller can distinguish.
          return;
        }
        if (controller.signal.aborted) {
          return;
        }

        const message = err instanceof Error ? err.message : 'Linear run failed';
        const event: LinearRunEvent = {
          kind: 'error',
          content: message,
          timestamp: new Date(),
        };
        setState(prev => ({
          ...prev,
          status: 'error',
          error: message,
          events: [...prev.events, event],
          latest: event,
        }));
      } finally {
        activeRef.current = false;
        if (abortRef.current === controller) {
          abortRef.current = null;
        }
      }
    })();
  }, [appendEvent]);

  // Auto-start on mount when requested.
  useEffect(() => {
    if (autoStart) {
      start();
    }
    // Deliberately run once at mount — subsequent option changes do not
    // re-trigger. Callers who need retry semantics call `.start()` manually.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // Cleanup on unmount — abort any in-flight fetch to avoid state updates on
  // an unmounted component.
  useEffect(() => {
    return () => {
      if (abortRef.current) {
        abortRef.current.abort();
        abortRef.current = null;
      }
    };
  }, []);

  return {
    ...state,
    start,
    cancel,
  };
}

export default useLinearRunProgress;
