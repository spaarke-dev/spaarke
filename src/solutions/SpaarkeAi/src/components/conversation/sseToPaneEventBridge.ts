/**
 * sseToPaneEventBridge.ts — R5 task 036 / P2-CLOSEOUT-05.
 *
 * Pure transformer: maps `AnalysisChunk` SSE events (BFF
 * `src/server/api/Sprk.Bff.Api/Models/Ai/AnalysisChunk.cs`) to the typed
 * `PaneEventBus` workspace-channel events consumed by
 * `StructuredOutputStreamWidget` (R5 task 017 / D2-07) and downstream
 * subscribers.
 *
 * Why a separate module
 * ─────────────────────
 * Backend emits AnalysisChunk envelopes; downstream widgets subscribe to
 * `workspace.streaming_*` PaneEventBus events. NOTHING translates between
 * them in the SpaarkeAi shell today. Tasks 037 + 038 build subscribers; this
 * module is the publisher's translator — the missing piece per the
 * 2026-06-05 verification (see `notes/task-036-design-2026-06-05.md` §1.3).
 *
 * Event mapping (per R5 PaneEventTypes — closed at 4 channels per ADR-030)
 * ──────────────────────────────────────────────────────────────────────────
 *
 *   AnalysisChunk.Type === "text"     → no event (purely free-form streaming;
 *                                       the structured-output stream relies
 *                                       on the "delta" event below)
 *   AnalysisChunk.Type === "delta"    → workspace.field_delta with
 *                                       fieldPath = Delta.Path
 *                                       fieldContent = Delta.Content
 *                                       sequence    = Delta.Sequence
 *   AnalysisChunk.Type === "complete" → workspace.streaming_complete with
 *                                       completionStatus = "complete"
 *   AnalysisChunk.Type === "error"    → workspace.streaming_complete with
 *                                       completionStatus = "declined" (NO
 *                                       `streaming_error` discriminant exists
 *                                       in the registered event-type union
 *                                       — see PaneEventTypes.ts. Errors are
 *                                       carried as terminal `declined`
 *                                       events. Bridge emits the bus event
 *                                       AND lets the caller raise on error
 *                                       at a higher layer.)
 *
 * The bridge ALSO emits `workspace.streaming_started` on the FIRST non-error
 * chunk it sees (lazily). Callers (executeSummarizeIntent) MUST start a new
 * bridge instance per stream so each stream gets its own once-only
 * streaming_started event.
 *
 * `streamId` correlation
 * ──────────────────────
 * Per PaneEventTypes.streamId requirement (R5 D2-06), all three lifecycle
 * events MUST carry the same `streamId` so subscribers can disambiguate
 * concurrent streams. The bridge constructor accepts a streamId; the caller
 * (executeSummarizeIntent) generates one per execution.
 *
 * Side effects
 * ────────────
 * Pure transformer. The bridge's `consume()` method does NOT publish — it
 * RETURNS the event payload. The caller publishes via `dispatch("workspace",
 * payload)` after receiving the value. This keeps the bridge testable with
 * no mocks and lets the caller batch / debounce / drop events if needed.
 *
 * Note: `context.files_staged` (emitted on the `context` channel after
 * promotion) is NOT produced by this bridge. It is emitted directly by
 * `executeSummarizeIntent` AFTER the `/documents` POST succeeds and BEFORE
 * the `/summarize` SSE stream opens (see executeSummarizeIntent.ts).
 *
 * @see ADR-028 — Auth v2; this module does NO IO
 * @see ADR-030 — PaneEventBus channels; this module emits only ADDITIVE
 *                event types on the `workspace` channel
 */

import type { WorkspacePaneEvent } from '@spaarke/ai-widgets';

// ---------------------------------------------------------------------------
// Input shape (AnalysisChunk from the BFF SSE stream)
// ---------------------------------------------------------------------------

/**
 * TypeScript counterpart of the BFF
 * `Sprk.Bff.Api.Models.Ai.AnalysisChunk` record (C# `Models/Ai/AnalysisChunk.cs`).
 *
 * Fields use camelCase per the BFF's
 * `JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }`
 * (verified at SummarizeSessionEndpoint.cs `s_jsonOptions`).
 *
 * Fields are all optional except `type` because the C# record uses
 * `JsonIgnoreCondition.WhenWritingNull` on the `delta` property and only
 * sets the relevant payload per event type.
 */
export interface AnalysisChunk {
  /** Event discriminator: "text" | "complete" | "error" | "delta". */
  type: string;
  /** Token chunk for "text" events (legacy streaming). Empty for others. */
  content?: string;
  /** Whether this is the terminal chunk. True on "complete" + "error". */
  done?: boolean;
  /** Final summary text on "complete" (legacy). */
  summary?: string;
  /** Structured result on "complete". */
  result?: unknown;
  /** Error message on "error". Never leaked to the bus event (per ADR-019). */
  error?: string;
  /** Structured-field delta payload on "delta" — R5 additive variant. */
  delta?: FieldDelta;
}

/**
 * The structured-field delta payload carried by a `type: "delta"`
 * AnalysisChunk. Per C# `Sprk.Bff.Api.Models.Ai.FieldDelta`.
 */
export interface FieldDelta {
  /** JSON path of the target field, e.g. "tldr", "summary", "fileHighlights[0].summary". */
  path: string;
  /** Token chunk to append to the field. */
  content: string;
  /** Monotonic sequence number from the producer (for ordering correctness). */
  sequence: number;
}

// ---------------------------------------------------------------------------
// Bridge
// ---------------------------------------------------------------------------

/**
 * One bus event optionally emitted per consumed AnalysisChunk.
 * Either a single payload OR `null` (chunk produced no bus event).
 */
export type BridgedEvent = WorkspacePaneEvent | null;

/**
 * Public interface of the bridge instance.
 */
export interface SseToPaneEventBridge {
  /**
   * Consume a single AnalysisChunk and return zero-or-one bus events.
   *
   * Returns an ARRAY because the FIRST non-error chunk produces TWO bus
   * events: `streaming_started` + the chunk's own event. Subsequent chunks
   * produce exactly ONE event (or null for ignored chunks like "text").
   *
   * Pure: same input + same bridge state → same output. Never throws on
   * malformed chunks — falls back to ignoring the chunk (returns []).
   */
  consume(chunk: AnalysisChunk): WorkspacePaneEvent[];

  /**
   * Whether the bridge has already emitted `streaming_started`. Useful for
   * tests; not part of the consumer contract.
   */
  readonly hasStarted: boolean;
}

/**
 * Construct a fresh bridge for ONE SSE stream. The bridge holds two pieces
 * of internal state:
 *
 *   - `streamId` — supplied by caller; embedded into every emitted event so
 *                  subscribers can disambiguate concurrent streams.
 *   - `hasStarted` — whether `streaming_started` has been emitted yet (one-shot).
 *
 * Create one bridge per stream. Do NOT reuse across streams.
 */
export function createSseToPaneEventBridge(streamId: string): SseToPaneEventBridge {
  let started = false;

  function consume(chunk: AnalysisChunk): WorkspacePaneEvent[] {
    // Defensive: malformed chunk (missing or non-string type) — ignore.
    if (!chunk || typeof chunk.type !== 'string') {
      return [];
    }

    const events: WorkspacePaneEvent[] = [];

    // Map the chunk to its bus event (or null if no mapping applies).
    let mapped: WorkspacePaneEvent | null = null;

    switch (chunk.type) {
      case 'delta': {
        // Defensive: "delta" without payload — ignore.
        const delta = chunk.delta;
        if (
          !delta ||
          typeof delta.path !== 'string' ||
          typeof delta.content !== 'string' ||
          typeof delta.sequence !== 'number'
        ) {
          return [];
        }

        // BFF's IncrementalJsonParser emits JSONPath-style keys ($.tldr,
        // $.summary, $.keywords, $.entities) but the StructuredOutputStreamWidget's
        // SUMMARIZE_SCHEMA declares bare top-level keys (tldr, summary, keywords,
        // entities) — see Spaarke.AI.Widgets workspace/StructuredOutputStreamWidget.tsx
        // SUMMARIZE_SCHEMA export. Without this normalization the widget can't
        // map deltas to schema fields and sections render empty even though
        // events ARE arriving. Observed in R5 SC-18 cycle 9 / 2026-06-05.
        const normalizedPath = delta.path.startsWith('$.')
          ? delta.path.slice(2)
          : delta.path;
        mapped = {
          type: 'field_delta',
          streamId,
          fieldPath: normalizedPath,
          fieldContent: delta.content,
          sequence: delta.sequence,
        };
        break;
      }

      case 'complete': {
        mapped = {
          type: 'streaming_complete',
          streamId,
          completionStatus: 'complete',
        };
        break;
      }

      case 'error': {
        // No `streaming_error` discriminant exists per PaneEventTypes.ts —
        // surface as a terminal `streaming_complete` with
        // completionStatus="declined". The caller raises at a higher layer
        // (executeSummarizeIntent throws on error chunks; downstream
        // subscribers render the declined state).
        mapped = {
          type: 'streaming_complete',
          streamId,
          completionStatus: 'declined',
        };
        break;
      }

      case 'text':
      default:
        // "text" events are legacy free-form streaming — the structured-
        // output stream relies on "delta" + "complete" only. Unknown event
        // types are ignored.
        mapped = null;
        break;
    }

    if (mapped === null) {
      return events;
    }

    // First non-error chunk also emits streaming_started (one-shot per stream).
    // We emit streaming_started BEFORE the mapped event so subscribers see the
    // lifecycle in order: started → (deltas | complete | declined).
    if (!started) {
      started = true;
      events.push({
        type: 'streaming_started',
        streamId,
      });
    }

    events.push(mapped);
    return events;
  }

  return {
    consume,
    get hasStarted() {
      return started;
    },
  };
}
