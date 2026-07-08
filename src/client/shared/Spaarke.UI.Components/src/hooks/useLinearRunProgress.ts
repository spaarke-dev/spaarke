/**
 * LinearRunEvent types — Linear AI Consumer SSE event vocabulary.
 *
 * TASK-045 DELETION NOTE (ai-architecture-redesign-r1, FR-P3-06, NFR-08):
 * the `useLinearRunProgress` hook implementation that used to live here
 * (fetch + getReader + TextDecoder + private `parseSseLine` loop) was a DEAD
 * hook body — a repo grep found ZERO runtime call sites — and duplicated the
 * canonical SSE reader (`readSseStream` + `parseSseEvent` in
 * `./useSseStream.ts`). It was hard-deleted per the one-reader-loop rule.
 * Any future Linear-consumer progress hook MUST consume `readSseStream`.
 *
 * The TYPES below are retained at the same module path because they are
 * consumed by:
 *   - `components/LinearRunProgress/LinearRunProgressList.tsx` (presenter)
 *   - `components/SummarizeFilesWizard/SummarizeFilesDialog.tsx`
 *   - `components/SummarizeFilesWizard/SummaryResultsStep.tsx`
 *
 * Server-side wire contract (see `AnalysisStreamChunk` record in
 * `src/server/api/Sprk.Bff.Api/Api/Ai/AnalysisEndpoints.cs`):
 *
 *   Type: "metadata" | "progress" | "chunk" | "result" | "done" | "error"
 *   Content: string | null  (progress message text, chunk tokens, result JSON)
 *   Step: string | null     (stable key on progress chunks, e.g. "extracting_text")
 *
 * @see useSseStream.ts — the canonical SSE reader/parse primitives (NFR-08)
 * @see LinearRunProgressList.tsx — default presenter for these events
 */

// ─────────────────────────────────────────────────────────────────────────────
// Public types (hook deleted — types only, see header)
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
